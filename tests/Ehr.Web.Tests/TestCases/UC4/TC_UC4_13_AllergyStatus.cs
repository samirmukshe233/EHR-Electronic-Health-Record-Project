using Ehr.Data.Accounts;
using Ehr.Data.Audit;
using Xunit;
using static Ehr.Web.Tests.Browser;
using static Ehr.Web.Tests.ChartListTests;

namespace Ehr.Web.Tests.TestCases.UC4;

// Test Case ID: TC-UC4-13
// Purpose / scenario: A patient's allergy list is in one of three states, and the chart, the banner and the desktop
//   app's offline copy each show the right one: no allergy information recorded; "No known allergies", which a
//   clinician records; or a list of allergies, and adding the first one clears "No known allergies".
// Type: Functional (alternative flow)
// Requirements: FR-02, proposed FR-38
// Use case: UC 4, View a patient's chart (editing the chart's lists)
// Preconditions: The clinician is signed in on the on-site app, and in the desktop app, and the patient is on their
//   panel with no allergy information. A second clinician has emergency access only. The clinic's server is up.
// Inputs and system conditions: GET /patients/{id}; POST /patients/{id}?handler=NoKnownAllergies; POST
//   /patients/{id}/allergies; POST /api/desktop/offline-panel; on the on-site app.
// Test data: Patient Synthetic Allergy status. Allergy "Peanut" (UNII QE1QX6B99R), reaction "Hives". Clinician
//   "Dr. Allergy Status"; "Dr. Covering Status" with emergency access.
// Steps:
//   1. Open the chart, and download the offline copy.
//   2. Choose "Record no known allergies". Open the chart and download the offline copy again.
//   3. Add the allergy. Open the chart and download the offline copy again.
//   4. As the second clinician, open the chart with a reason, and post "Record no known allergies".
// Expected result: Step 1: the chart and banner say "No allergy information recorded", the chart offers "Record no
//   known allergies", and the offline copy says "not-recorded" with no allergies. Step 2: the chart says "No known
//   allergies (recorded <date> by Dr. Allergy Status)", the banner "No known allergies", the button is gone, the offline copy says
//   "none-known" with no allergies, and the write audit has an insert by the clinician. Step 3: the chart and banner
//   list Peanut, "No known allergies" is gone and its row is inactive, and the offline copy says "listed" with Peanut
//   only. Step 4: the second clinician is offered no button, and their post changes nothing.
// Creator: Tahseen Arnan
public class TC_UC4_13_AllergyStatus(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_allergy_list_is_not_recorded_none_known_or_listed_everywhere_it_is_shown()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Allergy Status", StaffRoles.Clinician);
        var patient = await apps.InsertPatientAsync("Synthetic", "Allergy status");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var desktop = await apps.DesktopSignInAsync(apps.OnPrem, clinician);
        async Task<string> OfflineAsync()
        {
            using var response = await DesktopSignInTests.PostAsync(apps.OnPrem, "/api/desktop/offline-panel", desktop);
            return await response.Content.ReadAsStringAsync(Cancel);
        }

        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);
        Assert.Contains("<p class=\"sc-muted\">No allergy information recorded.</p>", chart);
        Assert.Contains("<span class=\"sc-allergy sc-allergy--none\">No allergy information recorded</span>", chart);
        Assert.Contains("data-no-known-allergies>Record no known allergies</button>", chart);
        var offline = await OfflineAsync();
        Assert.Contains("\"allergies\":[]", offline);
        Assert.Contains("\"allergyStatus\":\"not-recorded\"", offline);

        var inserts = await apps.AuditEventsAsync(AuditActions.Insert, clinician.Id, patient);
        using var recorded = await PostFormAsync(session.Client, $"/patients/{patient}?handler=NoKnownAllergies", $"/patients/{patient}");
        var none = await ReadAsync(recorded);
        Assert.Matches(@"<p>No known allergies \(recorded \d{4}-\d{2}-\d{2} by Dr\. Allergy Status\)\.</p>", none);
        Assert.Contains("<span class=\"sc-allergy\">No known allergies</span>", none);
        Assert.DoesNotContain("data-no-known-allergies", none);
        Assert.Equal(["No known allergies no-known-allergy active"], await RowsAsync(apps, "allergy_intolerances", patient, "concat_ws(' ', display, code, clinical_status)"));
        // The write audit: the entry, and its copy in the lists' history.
        Assert.Equal(inserts + 2, await apps.AuditEventsAsync(AuditActions.Insert, clinician.Id, patient));
        offline = await OfflineAsync();
        Assert.Contains("\"allergies\":[]", offline);
        Assert.Contains("\"allergyStatus\":\"none-known\"", offline);
        Assert.Contains("No known allergies", Words(await session.Client.GetStringAsync($"/patients/{patient}/chart-history", Cancel)));

        using var added = await SaveAsync(session.Client, patient, "allergies", null, ("Name", "Peanut"), ("Reaction", "Hives"), ("Status", "active"));
        var listed = await ReadAsync(added);
        Assert.Contains("<strong>Peanut</strong>: Hives", listed);
        Assert.DoesNotContain("No known allergies", listed);
        Assert.DoesNotContain("No allergy information recorded", listed);
        Assert.Equal(["No known allergies inactive", "Peanut active"], await RowsAsync(apps, "allergy_intolerances", patient, "concat_ws(' ', display, clinical_status)"));
        offline = await OfflineAsync();
        Assert.Contains("\"allergyStatus\":\"listed\"", offline);
        Assert.Contains("\"display\":\"Peanut\"", offline);
        Assert.DoesNotContain("No known allergy", offline);

        // With an allergy recorded, the database refuses "No known allergies": the post saves nothing.
        using var refused = await PostFormAsync(session.Client, $"/patients/{patient}?handler=NoKnownAllergies", $"/patients/{patient}");
        Assert.Equal(2, (await RowsAsync(apps, "allergy_intolerances", patient, "display")).Count);

        // Emergency access reads the chart and changes nothing.
        var other = await apps.InsertPatientAsync("Synthetic", "Allergy unknown");
        await apps.AssignAsync(clinician, other);
        var covering = await SignInAsync(apps.OnPrem, await apps.CreateStaffAsync("Dr. Covering Status", StaffRoles.Clinician));
        using var opened = await PostFormAsync(covering.Client, $"/patients/{other}/reason", $"/patients/{other}/reason", ("Reason", "Covering the night shift"));
        var theirs = await ReadAsync(opened);
        Assert.Contains("No allergy information recorded", theirs);
        Assert.DoesNotContain("data-no-known-allergies", theirs);
        using var post = await covering.Client.PostAsync($"/patients/{other}?handler=NoKnownAllergies", Form(("__RequestVerificationToken", Token(theirs))), Cancel);
        Assert.Empty(await RowsAsync(apps, "allergy_intolerances", other, "display"));
    }
}
