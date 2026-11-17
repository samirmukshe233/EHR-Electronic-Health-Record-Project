using Ehr.Testing;
using Npgsql;
using Xunit;

namespace Ehr.Web.Tests.TestCases.UC3;

// Test Case ID: TC-UC3-06
// Purpose / scenario: One authoritative copy of each patient record. The patient record a search finds is the on-site
//   primary's: the cloud's read role, which serves search off-site, can't insert, change or delete a patient, and a
//   write it tries leaves the patient's row exactly as it was.
// Type: Non-functional (NFR-09)
// Requirements: NFR-09
// Use case: UC 3, Search for a patient
// Preconditions: The database is migrated with db/policies applied. One synthetic patient exists.
// Inputs and system conditions: The database's own record of privileges on ehr.patients, and statements run as
//   ehr_read, the role the cloud app reads the replica with.
// Test data: Synthetic patient Synthetic Onecopy{8 hex digits}, born 1990-02-03. As ehr_read:
//   UPDATE ehr.patients SET family_name = 'Changed' WHERE id = {patient};
//   DELETE FROM ehr.patients WHERE id = {patient};
//   INSERT INTO ehr.patients (id, mrn, given_name, family_name, birth_date, gender, version, updated_at)
//     VALUES (gen_random_uuid(), 'SYN-COPY', 'Second', 'Copy', '1990-02-03', 'unknown', 1, now()).
// Steps:
//   1. Insert the patient, and read their row.
//   2. Ask the database whether ehr_read may INSERT into, UPDATE, DELETE from or TRUNCATE ehr.patients.
//   3. As ehr_read, run the UPDATE, the DELETE and the INSERT.
//   4. Read the patient's row again, and count the patients with MRN SYN-COPY.
// Expected result: Step 2 answers no to all four. Each statement in step 3 is refused, SQLSTATE 42501 (insufficient
//   privilege). Step 4's row is the same as step 1's, field for field, and no patient has MRN SYN-COPY. Acceptance
//   criterion: zero privileges to write ehr.patients, zero rows changed or added by a refused write.
// Creator: Diego Valenzuela
public class TC_UC3_06_Nfr09_OneAuthoritativeCopy(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_clouds_read_role_can_neither_change_nor_add_a_patient_record()
    {
        var patient = await apps.InsertPatientAsync("Synthetic", "Onecopy" + Guid.NewGuid().ToString("N")[..8], new DateOnly(1990, 2, 3));
        var before = await RowAsync(patient);

        await using (var catalog = await apps.OpenDatabaseAsync())
        {
            await using var privileges = new NpgsqlCommand(
                """
                SELECT has_table_privilege('ehr_read', 'ehr.patients', 'INSERT')
                    OR has_table_privilege('ehr_read', 'ehr.patients', 'UPDATE')
                    OR has_table_privilege('ehr_read', 'ehr.patients', 'DELETE')
                    OR has_table_privilege('ehr_read', 'ehr.patients', 'TRUNCATE')
                """, catalog);
            Assert.False((bool)(await privileges.ExecuteScalarAsync(Cancel))!);
        }

        await using (var asReadRole = new NpgsqlConnection(apps.Database.ConnectionStringFor("ehr_read", MigratedDatabase.ReadPassword)))
        {
            await asReadRole.OpenAsync(Cancel);
            foreach (var statement in new[]
            {
                "UPDATE ehr.patients SET family_name = 'Changed' WHERE id = @patient",
                "DELETE FROM ehr.patients WHERE id = @patient",
                """
                INSERT INTO ehr.patients (id, mrn, given_name, family_name, birth_date, gender, version, updated_at)
                VALUES (gen_random_uuid(), 'SYN-COPY', 'Second', 'Copy', '1990-02-03', 'unknown', 1, now())
                """,
            })
            {
                await using var write = new NpgsqlCommand(statement, asReadRole);
                write.Parameters.AddWithValue("patient", patient);
                var refused = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync(Cancel));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
            }
        }

        Assert.Equal(before, await RowAsync(patient));
        await using var db = await apps.OpenDatabaseAsync();
        await using var copies = new NpgsqlCommand("SELECT count(*) FROM ehr.patients WHERE mrn = 'SYN-COPY'", db);
        Assert.Equal(0L, (long)(await copies.ExecuteScalarAsync(Cancel))!);
    }

    // The patient's row, every column, as text.
    async Task<string> RowAsync(Guid patient)
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var row = new NpgsqlCommand("SELECT p::text FROM ehr.patients p WHERE p.id = @patient", db);
        row.Parameters.AddWithValue("patient", patient);
        return Assert.IsType<string>(await row.ExecuteScalarAsync(Cancel));
    }
}
