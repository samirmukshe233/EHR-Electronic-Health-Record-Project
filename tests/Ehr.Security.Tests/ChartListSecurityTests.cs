using Ehr.Data.Accounts;
using Ehr.Testing;
using Npgsql;
using Xunit;

namespace Ehr.Security.Tests;

// The expansion plan's step 2: what the database lets each account do with a patient's allergies, medications and
// diagnoses, as the runtime roles, in SQL of its own, so these hold whatever the app checks (db/policies/10-grants.sql,
// 30-rls.sql and 65-chart-lists.sql). A clinician adds and changes entries only for a patient on their active panel;
// nothing deletes one or moves one to another patient; the database keeps the history of every change, which nobody
// at runtime can write; and a diagnosis in a sensitive range is sensitive whatever was sent.
public class ChartListSecurityTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    string App => database.ConnectionStringFor("ehr_app", MigratedDatabase.AppPassword);

    string Read => database.ConnectionStringFor("ehr_read", MigratedDatabase.ReadPassword);

    static readonly Dictionary<string, (string Insert, string Update)> Statements = new()
    {
        ["allergy_intolerances"] = (
            "INSERT INTO ehr.allergy_intolerances (id, patient_id, code, code_system, display, clinical_status, recorded_at) VALUES (@id, @patient, 'QE1QX6B99R', 'unii', 'Peanut', 'active', now())",
            "UPDATE ehr.allergy_intolerances SET clinical_status = 'inactive' WHERE id = @id"),
        ["medication_statements"] = (
            "INSERT INTO ehr.medication_statements (id, patient_id, code, display, status) VALUES (@id, @patient, '314076', 'lisinopril 10 MG Oral Tablet', 'active')",
            "UPDATE ehr.medication_statements SET status = 'stopped' WHERE id = @id"),
        ["conditions"] = (
            "INSERT INTO ehr.conditions (id, patient_id, code, display, clinical_status) VALUES (@id, @patient, 'I10', 'Essential (primary) hypertension', 'active')",
            "UPDATE ehr.conditions SET clinical_status = 'resolved' WHERE id = @id"),
    };

    public static TheoryData<string> Tables => [.. Statements.Keys];

    async Task<(Guid Clinician, Guid Patient)> OnPanelAsync()
    {
        var clinician = await Accounts.StaffAsync(database, StaffRoles.Clinician);
        var patient = await Accounts.PatientAsync(database, clinician);
        await Accounts.AssignAsync(database, clinician, patient);
        return (clinician, patient);
    }

    Task<List<string>> AsSuperuserAsync(string sql, params (string Name, object Value)[] parameters) =>
        Accounts.QueryAsync(database.SuperuserConnectionString, null, sql, parameters);

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task A_clinician_adds_and_changes_entries_for_a_patient_on_their_panel_and_the_history_and_audit_record_both(string table)
    {
        var (clinician, patient) = await OnPanelAsync();
        var id = Guid.CreateVersion7();

        await Accounts.ExecuteAsync(App, clinician, Statements[table].Insert, ("id", id), ("patient", patient));
        await Accounts.ExecuteAsync(App, clinician, Statements[table].Update, ("id", id));
        // An update that changes nothing adds no history.
        await Accounts.ExecuteAsync(App, clinician, Statements[table].Update, ("id", id));

        // The history, as a clinician reads it: added, then changed, each by this clinician, with the entry before and after.
        Assert.Equal([$"{clinician} {patient} true", $"{clinician} {patient} false"], await Accounts.QueryAsync(App, clinician,
            """
            SELECT changed_by_id || ' ' || patient_id || ' ' || (before IS NULL) FROM ehr.chart_history
            WHERE table_name = @table AND row_id = @id ORDER BY changed_at
            """,
            ("table", table), ("id", id)));
        Assert.Equal(["true"], await AsSuperuserAsync(
            "SELECT (before <> after)::text FROM ehr.chart_history WHERE row_id = @id AND before IS NOT NULL", ("id", id)));
        // The write audit names the clinician for the insert and for each update (it records the one that changed nothing too).
        Assert.Equal([$"insert {clinician} 1", $"update {clinician} 2"], await AsSuperuserAsync(
            "SELECT action || ' ' || actor_id || ' ' || count(*) FROM ehr.audit_events WHERE table_name = @table AND row_id = @id AND patient_id = @patient GROUP BY action, actor_id ORDER BY action",
            ("table", table), ("id", id), ("patient", patient)));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task Nobody_but_a_clinician_with_the_patient_on_their_panel_adds_or_changes_an_entry(string table)
    {
        var (clinician, patient) = await OnPanelAsync();
        var existing = Guid.CreateVersion7();
        await Accounts.ExecuteAsync(App, clinician, Statements[table].Insert, ("id", existing), ("patient", patient));
        var offPanel = await Accounts.StaffAsync(database, StaffRoles.Clinician);
        var ended = await Accounts.StaffAsync(database, StaffRoles.Clinician);
        await Accounts.AssignAsync(database, ended, patient);
        await Accounts.ExecuteAsync(App, ended, "UPDATE ehr.patient_assignments SET ended_at = now() WHERE clinician_id = @id", ("id", ended));
        var admin = await Accounts.StaffAsync(database, StaffRoles.Admin);
        var patientAccount = await Accounts.PatientAccountAsync(database, patient);
        var disabled = await Accounts.StaffAsync(database, StaffRoles.Clinician);
        await Accounts.AssignAsync(database, disabled, patient);
        await Accounts.LockOutAsync(database, disabled);

        foreach (var refused in new Guid?[] { offPanel, ended, admin, patientAccount, disabled, null, Guid.NewGuid() })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Accounts.ExecuteAsync(App, refused, Statements[table].Insert, ("id", Guid.CreateVersion7()), ("patient", patient)));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            // Row-level security shows them no entry to update: nothing changes.
            await Accounts.ExecuteAsync(App, refused, Statements[table].Update, ("id", existing));
        }

        // The cloud app's role writes nothing, a clinician's context or not.
        foreach (var sql in new[] { Statements[table].Insert, Statements[table].Update })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Accounts.ExecuteAsync(Read, clinician, sql, ("id", existing), ("patient", patient)));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }

        Assert.Equal(["1"], await AsSuperuserAsync($"SELECT count(*) FROM ehr.{table} WHERE patient_id = @patient", ("patient", patient)));
        Assert.Equal(["1"], await AsSuperuserAsync("SELECT count(*) FROM ehr.chart_history WHERE row_id = @id", ("id", existing)));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task An_entry_is_never_deleted_or_moved_to_another_patient(string table)
    {
        var (clinician, patient) = await OnPanelAsync();
        var other = await Accounts.PatientAsync(database, clinician);
        await Accounts.AssignAsync(database, clinician, other);
        var id = Guid.CreateVersion7();
        await Accounts.ExecuteAsync(App, clinician, Statements[table].Insert, ("id", id), ("patient", patient));

        foreach (var sql in new[]
        {
            $"DELETE FROM ehr.{table} WHERE id = @id",
            $"UPDATE ehr.{table} SET patient_id = @other WHERE id = @id",
            $"UPDATE ehr.{table} SET id = @other WHERE id = @id",
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Accounts.ExecuteAsync(App, clinician, sql, ("id", id), ("other", other)));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }

        Assert.Equal([patient.ToString()], await AsSuperuserAsync($"SELECT patient_id::text FROM ehr.{table} WHERE id = @id", ("id", id)));
    }

    // The rule in db/policies/65-chart-lists.sql and the plan's decision log.
    [Theory]
    [InlineData("F41.1", true)]
    [InlineData("F10.20", true)]
    [InlineData("f32.a", true)]
    [InlineData("F99", true)]
    [InlineData("A54.00", true)]
    [InlineData("A64", true)]
    [InlineData("A49.9", false)]
    [InlineData("A65", false)]
    [InlineData("B20", true)]
    [InlineData("Z21", true)]
    [InlineData("Z33.2", true)]
    [InlineData("Z33.1", false)]
    [InlineData("O04.80", true)]
    [InlineData("T74.11XA", true)]
    [InlineData("T76.21XA", true)]
    [InlineData("X78.1XXA", true)]
    [InlineData("X70.XXXA", false)]
    [InlineData("R45.851", true)]
    [InlineData("R45.0", false)]
    [InlineData("I10", false)]
    [InlineData("E11.9", false)]
    [InlineData("", false)]
    public async Task The_sensitive_ranges_are_the_ones_the_plan_records(string code, bool sensitive)
    {
        Assert.Equal([sensitive.ToString().ToLowerInvariant()], await Accounts.QueryAsync(App, null,
            "SELECT ehr.icd10cm_is_sensitive(@code)::text", ("code", code)));
    }

    [Fact]
    public async Task A_diagnosis_in_a_sensitive_range_is_sensitive_whatever_was_sent_and_any_other_is_as_marked()
    {
        var (clinician, patient) = await OnPanelAsync();
        var anxiety = Guid.CreateVersion7();
        var hypertension = Guid.CreateVersion7();
        const string insert = "INSERT INTO ehr.conditions (id, patient_id, code, display, clinical_status, sensitive) VALUES (@id, @patient, @code, 'Synthetic', 'active', false)";
        await Accounts.ExecuteAsync(App, clinician, insert, ("id", anxiety), ("patient", patient), ("code", "F41.1"));
        await Accounts.ExecuteAsync(App, clinician, insert, ("id", hypertension), ("patient", patient), ("code", "I10"));
        Task<List<string>> FlagsAsync() => AsSuperuserAsync(
            "SELECT code || ' ' || sensitive FROM ehr.conditions WHERE patient_id = @patient ORDER BY code", ("patient", patient));

        Assert.Equal(["F41.1 true", "I10 false"], await FlagsAsync());

        // Clearing it for a sensitive code does nothing; a clinician may mark any other, and unmark it.
        await Accounts.ExecuteAsync(App, clinician, "UPDATE ehr.conditions SET sensitive = false WHERE id = @id", ("id", anxiety));
        await Accounts.ExecuteAsync(App, clinician, "UPDATE ehr.conditions SET sensitive = true WHERE id = @id", ("id", hypertension));
        Assert.Equal(["F41.1 true", "I10 true"], await FlagsAsync());
        await Accounts.ExecuteAsync(App, clinician, "UPDATE ehr.conditions SET sensitive = false WHERE id = @id", ("id", hypertension));
        // A code changed into a sensitive range becomes sensitive.
        await Accounts.ExecuteAsync(App, clinician, "UPDATE ehr.conditions SET code = 'F32.A' WHERE id = @id", ("id", hypertension));
        Assert.Equal(["F32.A true", "F41.1 true"], await FlagsAsync());
    }

    [Fact]
    public async Task Clinicians_read_the_history_and_nobody_else_does()
    {
        var (clinician, patient) = await OnPanelAsync();
        var id = Guid.CreateVersion7();
        await Accounts.ExecuteAsync(App, clinician, Statements["conditions"].Insert, ("id", id), ("patient", patient));
        var colleague = await Accounts.StaffAsync(database, StaffRoles.Clinician);
        var admin = await Accounts.StaffAsync(database, StaffRoles.Admin);
        var patientAccount = await Accounts.PatientAccountAsync(database, patient);
        const string read = "SELECT count(*) FROM ehr.chart_history WHERE row_id = @id";

        foreach (var connection in new[] { App, Read })
        {
            Assert.Equal(["1"], await Accounts.QueryAsync(connection, colleague, read, ("id", id)));
            foreach (var refused in new Guid?[] { admin, patientAccount, null, Guid.NewGuid() })
            {
                Assert.Equal(["0"], await Accounts.QueryAsync(connection, refused, read, ("id", id)));
            }
        }
    }

    // The reference codes are loaded with the schema (db/reference), each kind from its source, and a diagnosis is
    // flagged by the same rule the trigger uses.
    [Fact]
    public async Task The_reference_codes_are_loaded_and_flag_sensitive_diagnoses()
    {
        Assert.Equal(["allergen rxnorm true", "allergen unii true", "condition icd-10-cm true", "medication rxnorm true"], await Accounts.QueryAsync(Read, null,
            "SELECT kind || ' ' || system || ' ' || (count(*) > 10) FROM ehr.clinical_codes GROUP BY kind, system ORDER BY kind, system"));
        Assert.Equal(["0"], await Accounts.QueryAsync(Read, null,
            "SELECT count(*) FROM ehr.clinical_codes WHERE sensitive <> (kind = 'condition' AND ehr.icd10cm_is_sensitive(code))"));
        Assert.Equal(["Generalized anxiety disorder true"], await Accounts.QueryAsync(Read, null,
            "SELECT display || ' ' || sensitive FROM ehr.clinical_codes WHERE kind = 'condition' AND code = 'F41.1'"));
    }
}
