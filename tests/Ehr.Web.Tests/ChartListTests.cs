using System.Net;
using Ehr.Data.Accounts;
using Ehr.Data.Audit;
using Ehr.Data.Patients;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// The expansion plan's step 2: adding to and changing a patient's allergies, medications and diagnoses, the helpers
// for it, and coverage beside the test cases in TestCases/UC4 (TC-UC4-07 to TC-UC4-12): the code search, sensitive
// items, the history page, the banner on other patient pages, and the off-site path. What the database refuses
// whatever the app does is in Ehr.Security.Tests (ChartListSecurityTests).
public class ChartListTests(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    internal static string EntryPath(Guid patient, string list, Guid? entry = null) => $"/patients/{patient}/{list}{(entry is null ? "" : $"/{entry}")}";

    internal static async Task<(StaffAccount Clinician, Guid Patient, Session Session)> OnPanelAsync(WebApps apps, string family = "Listed")
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Synthetic", family);
        await apps.AssignAsync(clinician, patient);
        return (clinician, patient, await SignInAsync(apps.OnPrem, clinician));
    }

    // "Save", as the entry form posts it.
    internal static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid patient, string list, Guid? entry, params (string Name, object Value)[] fields) =>
        PostFormAsync(client, EntryPath(patient, list, entry), EntryPath(patient, list, entry), fields);

    // One column of a patient's rows in a chart table, in the order given.
    internal static async Task<List<string>> RowsAsync(WebApps apps, string table, Guid patient, string expression, string order = "display")
    {
        await using var db = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand($"SELECT {expression} FROM ehr.{table} WHERE patient_id = @patient ORDER BY {order}", db);
        command.Parameters.AddWithValue("patient", patient);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        while (await reader.ReadAsync(Cancel))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    internal static async Task<Guid> IdAsync(WebApps apps, string table, Guid patient) =>
        Guid.Parse((await RowsAsync(apps, table, patient, "id::text", "id")).Single());

    // The search offers names that start with what was typed first, then names with a word that does, and a diagnosis
    // by its code; it reads nothing about a patient, so it records nothing; and only clinicians reach it.
    [Fact]
    public async Task The_code_search_offers_matching_names_and_is_for_clinicians_only()
    {
        var (clinician, patient, session) = await OnPanelAsync(apps);
        var reads = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id);

        var hypertension = await session.Client.GetStringAsync("/codes?kind=condition&q=essential", Cancel);
        Assert.Contains("<option value=\"Essential (primary) hypertension\" label=\"I10\"></option>", hypertension);
        var byWord = await session.Client.GetStringAsync("/codes?kind=condition&q=anxiety", Cancel);
        Assert.Contains("value=\"Generalized anxiety disorder\"", byWord);
        Assert.Contains("value=\"Type 2 diabetes mellitus without complications\"", await session.Client.GetStringAsync("/codes?kind=condition&q=e11.9", Cancel));
        Assert.Contains("value=\"lisinopril 10 MG Oral Tablet\"", await session.Client.GetStringAsync("/codes?kind=medication&q=lisin", Cancel));
        var allergens = await session.Client.GetStringAsync("/codes?kind=allergen&q=pe", Cancel);
        Assert.Contains("value=\"Peanut\"", allergens);
        Assert.Contains("value=\"Penicillin G\"", allergens);
        Assert.DoesNotContain("label=", allergens);
        // Too little typed, an unknown kind, and LIKE's own characters: nothing.
        foreach (var address in new[] { "/codes?kind=condition&q=e", "/codes?kind=order&q=essential", "/codes?kind=condition&q=%25%25", "/codes" })
        {
            Assert.DoesNotContain("<option", await session.Client.GetStringAsync(address, Cancel));
        }

        Assert.True(System.Text.RegularExpressions.Regex.Matches(await session.Client.GetStringAsync("/codes?kind=condition&q=type", Cancel), "<option").Count
            <= Ehr.Domain.Patients.ChartLists.Suggestions);
        Assert.Equal(reads, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id));
        _ = patient;

        var admin = await SignInAsync(apps.OnPrem, await apps.CreateStaffAsync("Code Search Admin", StaffRoles.Admin));
        using var asAdmin = await Client(apps.OnPrem, admin.Jar, followRedirects: false).GetAsync("/codes?kind=condition&q=essential", Cancel);
        Assert.Equal("/denied", new Uri(Base, asAdmin.Headers.Location!).AbsolutePath);
        using var anonymous = await CreateClient(apps.OnPrem).GetAsync("/codes?kind=condition&q=essential", Cancel);
        Assert.DoesNotContain("<option", await anonymous.Content.ReadAsStringAsync(Cancel));
    }

    // A sensitive diagnosis or medication isn't sent with the chart until the clinician asks; asking is recorded as its
    // own audit action; and it never appears in the banner. The history page follows the same rule.
    [Fact]
    public async Task Sensitive_items_are_left_out_until_asked_for_and_asking_is_recorded()
    {
        var (clinician, patient, session) = await OnPanelAsync(apps, "Sensitive");
        using var anxiety = await SaveAsync(session.Client, patient, "diagnoses", null, ("Name", "Generalized anxiety disorder"), ("Status", "active"));
        using var hypertension = await SaveAsync(session.Client, patient, "diagnoses", null, ("Name", "Essential (primary) hypertension"), ("Status", "active"));
        using var sertraline = await SaveAsync(session.Client, patient, "medications", null, ("Name", "sertraline 50 MG Oral Tablet"), ("Status", "active"), ("Sensitive", "true"));
        // The database marked the F-code diagnosis sensitive, though the form didn't; the medication is as the clinician marked it.
        Assert.Equal(["Essential (primary) hypertension false", "Generalized anxiety disorder true"],
            await RowsAsync(apps, "conditions", patient, "display || ' ' || sensitive"));
        Assert.Equal(["sertraline 50 MG Oral Tablet true"], await RowsAsync(apps, "medication_statements", patient, "display || ' ' || sensitive"));
        var shownBefore = await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient);

        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);
        Assert.Contains("Essential (primary) hypertension", chart);
        Assert.DoesNotContain("anxiety", chart, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sertraline", chart, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sensitive items hidden: 2 diagnoses or medications are not shown.", Words(chart));
        Assert.Contains($"href=\"/patients/{patient}?sensitive=true\">Show sensitive items</a>", chart);
        var history = await session.Client.GetStringAsync($"/patients/{patient}/chart-history", Cancel);
        Assert.DoesNotContain("anxiety", history, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sensitive items hidden: 2 changes are not shown.", Words(history));
        Assert.Equal(shownBefore, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));

        var shown = await session.Client.GetStringAsync($"/patients/{patient}?sensitive=true", Cancel);
        Assert.Contains("<strong>Generalized anxiety disorder</strong>", shown);
        Assert.Contains("<strong>sertraline 50 MG Oral Tablet</strong>", shown);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(shown, "<span class=\"sc-tag sc-tag--warning\">Sensitive</span>").Count);
        Assert.Contains("Sensitive items shown: your view of them is recorded.", Words(shown));
        Assert.Equal(shownBefore + 1, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));
        Assert.Contains("Generalized anxiety disorder", await session.Client.GetStringAsync($"/patients/{patient}/chart-history?sensitive=true", Cancel));
        Assert.Equal(shownBefore + 2, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));

        // Opening a sensitive entry's form shows it, so that is recorded too.
        var entry = Guid.Parse((await RowsAsync(apps, "conditions", patient, "id::text", "display DESC")).First());
        Assert.Contains("value=\"Generalized anxiety disorder\"", await session.Client.GetStringAsync(EntryPath(patient, "diagnoses", entry), Cancel));
        Assert.Equal(shownBefore + 3, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));

        // Off-site, from the cloud copy, asking is recorded in the cloud app's audit queue and arrives on-site.
        var cloud = Client(apps.Cloud, session.Jar);
        Assert.Contains("<strong>Generalized anxiety disorder</strong>", await cloud.GetStringAsync($"/patients/{patient}?sensitive=true", Cancel));
        Assert.True(await apps.Cloud.Services.GetRequiredService<Ehr.Web.CloudAudit.CloudAuditShipper>().ShipAsync(Cancel));
        Assert.Equal(1, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient, AuditSources.Cloud));
    }

    // An entry marked sensitive after it was added is sensitive throughout its history: the history page leaves out
    // every change to it, the ones made before it was marked included. And the desktop app's offline copy never holds
    // sensitive items: it is told how many it wasn't given.
    [Fact]
    public async Task An_entry_marked_sensitive_later_leaves_the_history_and_the_offline_copy()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Later", StaffRoles.Clinician);
        var patient = await apps.InsertPatientAsync("Synthetic", "Marked later");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        using var added = await SaveAsync(session.Client, patient, "medications", null, ("Name", "sertraline 50 MG Oral Tablet"), ("Status", "active"));
        using var other = await SaveAsync(session.Client, patient, "medications", null, ("Name", "lisinopril 10 MG Oral Tablet"), ("Status", "active"));
        using var anxiety = await SaveAsync(session.Client, patient, "diagnoses", null, ("Name", "Generalized anxiety disorder"), ("Status", "active"));
        Assert.Contains("sertraline", await session.Client.GetStringAsync($"/patients/{patient}/chart-history", Cancel));
        var entry = Guid.Parse((await RowsAsync(apps, "medication_statements", patient, "id::text", "display DESC")).First());

        using var marked = await SaveAsync(session.Client, patient, "medications", entry, ("Name", "sertraline 50 MG Oral Tablet"), ("Status", "active"), ("Sensitive", "true"));

        var before = await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient);
        var history = await session.Client.GetStringAsync($"/patients/{patient}/chart-history", Cancel);
        Assert.DoesNotContain("sertraline", history, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", history, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lisinopril", history);
        // The sertraline's two changes and the diagnosis's one.
        Assert.Contains("Sensitive items hidden: 3 changes are not shown.", Words(history));
        Assert.Equal(before, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));
        var shown = Words(await session.Client.GetStringAsync($"/patients/{patient}/chart-history?sensitive=true", Cancel));
        Assert.Contains("Sensitive: no → yes", shown);
        Assert.Equal(before + 1, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));

        var token = await apps.DesktopSignInAsync(apps.OnPrem, clinician);
        using var offline = await DesktopSignInTests.PostAsync(apps.OnPrem, "/api/desktop/offline-panel", token);
        var copy = await offline.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("lisinopril 10 MG Oral Tablet", copy);
        Assert.DoesNotContain("sertraline", copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", copy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sensitiveNotKept\":2", copy);
        Assert.Equal(before + 1, await apps.AuditEventsAsync(AuditActions.ReadSensitive, clinician.Id, patient));
    }

    // The banner shows the patient's allergies on the note and dictation pages too, within the read each already records.
    [Fact]
    public async Task The_banner_shows_allergies_on_the_note_and_dictation_pages()
    {
        var (clinician, patient, session) = await OnPanelAsync(apps, "Bannered");
        using var saved = await SaveAsync(session.Client, patient, "allergies", null, ("Name", "Peanut"), ("Reaction", "Anaphylaxis"), ("Criticality", "high"),
            ("Severity", "severe"), ("Status", "active"));
        Assert.Equal(["Peanut QE1QX6B99R unii food high severe"],
            await RowsAsync(apps, "allergy_intolerances", patient, "concat_ws(' ', display, code, code_system, category, criticality, severity)"));
        var reads = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);

        var dictate = await session.Client.GetStringAsync($"/patients/{patient}/dictate", Cancel);

        Assert.Contains("<strong>Peanut</strong>: Anaphylaxis", dictate);
        Assert.Equal(reads + 1, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));
        using var created = await PostFormAsync(session.Client, $"/patients/{patient}?handler=NewNote", $"/patients/{patient}");
        Assert.Contains("<strong>Peanut</strong>: Anaphylaxis", await ReadAsync(created));
    }

    // The entry form is never kept by the browser, opened to add or to change, and a sensitive entry's form included:
    // the page says so itself, whatever else on it would.
    [Fact]
    public async Task The_entry_form_is_never_cached()
    {
        var (_, patient, session) = await OnPanelAsync(apps, "Uncached");
        using var added = await SaveAsync(session.Client, patient, "diagnoses", null, ("Name", "Generalized anxiety disorder"), ("Status", "active"));
        var entry = await IdAsync(apps, "conditions", patient);

        foreach (var path in new[] { EntryPath(patient, "diagnoses"), EntryPath(patient, "medications"), EntryPath(patient, "allergies"), EntryPath(patient, "diagnoses", entry) })
        {
            using var form = await session.Client.GetAsync(path, Cancel);
            Assert.Equal(HttpStatusCode.OK, form.StatusCode);
            Assert.True(form.Headers.CacheControl?.NoStore, $"{path} may be cached: {form.Headers.CacheControl}");
        }
    }

    // The search the clinician came from travels through the form and back to the chart, and the entry's own name
    // never becomes a search criterion: it would put the name, a sensitive diagnosis's too, in the chart's address.
    [Fact]
    public async Task Saving_an_entry_keeps_the_search_and_never_puts_the_entrys_name_in_the_address()
    {
        var (_, patient, session) = await OnPanelAsync(apps, "Addressed");
        var form = $"{EntryPath(patient, "diagnoses")}?name=Addressed";

        using var saved = await PostFormAsync(session.Client, form, form, ("Name", "Generalized anxiety disorder"), ("Status", "active"));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var address = saved.RequestMessage!.RequestUri!;
        Assert.Equal($"/patients/{patient}", address.AbsolutePath);
        Assert.Contains("name=Addressed", address.Query);
        Assert.DoesNotContain("anxiety", Uri.UnescapeDataString(address.Query), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", await saved.Content.ReadAsStringAsync(Cancel), StringComparison.OrdinalIgnoreCase);

        // Without a search, nothing at all is added for one.
        using var plain = await SaveAsync(session.Client, patient, "diagnoses", null, ("Name", "Essential (primary) hypertension"), ("Status", "active"));
        Assert.DoesNotContain("name=", plain.RequestMessage!.RequestUri!.Query);
    }

    // Off-site, a saved entry is forwarded to the on-site server; the history is read from the cloud copy.
    [Fact]
    public async Task Off_site_a_saved_entry_is_forwarded_and_saved_on_site()
    {
        var (_, patient, session) = await OnPanelAsync(apps, "Forwarded");
        var cloud = Client(apps.Cloud, session.Jar);

        using var saved = await SaveAsync(cloud, patient, "medications", null, ("Name", "Lisinopril 10 mg oral tablet"), ("Dosage", "1 tablet by mouth daily"), ("Status", "active"));

        Assert.Contains($"POST {EntryPath(patient, "medications")}", apps.Forwarded);
        // The name is the reference code's own, whatever case it was typed in.
        Assert.Equal(["lisinopril 10 MG Oral Tablet 314076 1 tablet by mouth daily"],
            await RowsAsync(apps, "medication_statements", patient, "concat_ws(' ', display, code, dosage)"));
    }
}
