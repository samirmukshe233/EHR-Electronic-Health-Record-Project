using System.Net;
using Ehr.Data.Audit;
using Xunit;
using static Ehr.Web.Tests.Browser;
using static Ehr.Web.Tests.ChartListTests;

namespace Ehr.Web.Tests.TestCases.UC4;

// Test Case ID: TC-UC4-07
// Purpose / scenario: A clinician adds a diagnosis, a medication and an allergy to a patient's chart, each picked from
//   the standard codes. The chart lists them, the history records who added each, and the audit log records the writes.
// Type: Functional (successful flow)
// Requirements: FR-02, proposed FR-38
// Use case: UC 4, View a patient's chart (editing the chart's lists)
// Preconditions: The clinician is signed in on the on-site app and the patient is on their panel, with nothing in the
//   three lists. The reference codes are loaded. The clinic's server is up.
// Inputs and system conditions: GET /patients/{id}; GET and POST /patients/{id}/diagnoses, /medications and
//   /allergies; GET /patients/{id}/chart-history; on the on-site app.
// Test data: Patient Synthetic Listed. Diagnosis "Essential (primary) hypertension" (ICD-10-CM I10), onset 2024-05-01.
//   Medication "lisinopril 10 MG Oral Tablet" (RxNorm 314076), "1 tablet by mouth daily". Allergy "Penicillin G"
//   (RxNorm 7980), reaction "Hives", risk high. Clinician "Dr. Test".
// Steps:
//   1. Open the chart and follow "Add diagnosis".
//   2. Enter the diagnosis and its onset and choose "Save diagnosis".
//   3. Add the medication and the allergy the same way.
//   4. Open the chart, then "History of allergies, medications and diagnoses".
// Expected result: The chart offers "Add allergy", "Add medication" and "Add diagnosis". The form opens with the
//   cursor in a required name field that searches the codes, shows the patient's banner, and is one recorded read.
//   After saving, the chart lists "Essential (primary) hypertension (I10) since 2024-05-01", the medication with how
//   it is taken, and the allergy with "High risk", each with a "Change" link; the banner shows the allergy. Each row
//   holds its code. The history lists three entries as "Added" by Dr. Test. The audit log has one insert per entry.
// Creator: Tahseen Arnan
public class TC_UC4_07_AddEntrySuccessfulFlow(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_clinician_adds_coded_entries_and_the_chart_history_and_audit_show_them()
    {
        var (clinician, patient, session) = await OnPanelAsync(apps);

        var chart = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);
        foreach (var (list, label) in new[] { ("allergies", "Add allergy"), ("medications", "Add medication"), ("diagnoses", "Add diagnosis") })
        {
            Assert.Contains($"<a href=\"{EntryPath(patient, list)}\" class=\"sc-btn sc-btn--compact\" data-add-entry=\"{list}\">{label}</a>", chart);
        }

        var reads = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);
        var form = await session.Client.GetStringAsync(EntryPath(patient, "diagnoses"), Cancel);
        Assert.Contains("Add diagnosis: Listed, Synthetic", form);
        Assert.Matches("<input [^>]*name=\"Name\"[^>]*required[^>]*autofocus[^>]*data-code-search=\"condition\"", form);
        Assert.Contains("/_content/Ehr.Design/js/code-search.", form);
        Assert.Contains(">Save diagnosis</button>", form);
        Assert.DoesNotContain("htmx.min.js", form);
        Assert.Equal(reads + 1, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));

        using var diagnosis = await SaveAsync(session.Client, patient, "diagnoses", null,
            ("Name", "Essential (primary) hypertension"), ("Onset", "2024-05-01"), ("Status", "active"));
        Assert.Equal(HttpStatusCode.OK, diagnosis.StatusCode);
        Assert.Equal($"/patients/{patient}", diagnosis.RequestMessage!.RequestUri!.AbsolutePath);
        using var medication = await SaveAsync(session.Client, patient, "medications", null,
            ("Name", "lisinopril 10 MG Oral Tablet"), ("Dosage", "1 tablet by mouth daily"), ("Status", "active"));
        using var allergy = await SaveAsync(session.Client, patient, "allergies", null,
            ("Name", "Penicillin G"), ("Reaction", "Hives"), ("Criticality", "high"), ("Status", "active"));

        var saved = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);
        Assert.Matches(@"<strong>Essential \(primary\) hypertension</strong>\s*\(I10\)\s*since 2024-05-01", saved);
        Assert.Contains("<strong>lisinopril 10 MG Oral Tablet</strong>: 1 tablet by mouth daily", saved);
        Assert.Contains("<strong>Penicillin G</strong>: Hives", saved);
        Assert.Contains("High risk", saved);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(saved, "data-change-entry=").Count);
        Assert.Equal(["Essential (primary) hypertension I10 active 2024-05-01"],
            await RowsAsync(apps, "conditions", patient, "concat_ws(' ', display, code, clinical_status, onset_date)"));
        Assert.Equal(["lisinopril 10 MG Oral Tablet 314076"], await RowsAsync(apps, "medication_statements", patient, "concat_ws(' ', display, code)"));
        Assert.Equal(["Penicillin G 7980 rxnorm medication"], await RowsAsync(apps, "allergy_intolerances", patient, "concat_ws(' ', display, code, code_system, category)"));

        var history = Words(await session.Client.GetStringAsync($"/patients/{patient}/chart-history", Cancel));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(history, @"Dr\. Test").Count - 1);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(history, "Added").Count);
        Assert.Contains("Penicillin G", history);
        Assert.Equal(3, await apps.AuditEventsAsync(AuditActions.Insert, clinician.Id, patient) - 3);
    }
}
