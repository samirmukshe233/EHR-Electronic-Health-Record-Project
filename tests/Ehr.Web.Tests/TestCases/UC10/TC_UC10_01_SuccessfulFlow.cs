using System.Net;
using Ehr.Data.Patients;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC10;

// Test Case ID: TC-UC10-01
// Purpose / scenario: After Stop, the fields the model proposed and validation passed come back awaiting the
//   clinician's review, each with the words of the transcript it came from, and are saved as proposals for review. No
//   AI text is in the note until the clinician approves it (UC 13).
// Type: Functional (successful flow)
// Requirements: FR-08, FR-11, FR-12, FR-13
// Use case: UC 10, Structure and validate note fields (main flow, steps 1 to 3)
// Preconditions: The clinician has dictated five chunks from the dictation page for a patient on their panel and
//   selected Stop. The model has sorted the transcript.
// Inputs and system conditions: POST /api/dictation/sessions/{sessionId}/result, then what the page sends once the
//   fields are sorted: POST /api/dictation/sessions/review/{reviewId}/stage with the draft's version. The dictation
//   service is the test fixture's stub, which proposes a History and an Assessment and nothing for the Plan.
// Test data: Patient Remy Spoken. Transcript "Cough for three days. No fever. Chest is clear. Likely viral. Fluids
//   and rest."
// Steps:
//   1. Ask for the result and read each field.
//   2. Stage the result for review.
//   3. Read the saved proposals and the note draft.
// Expected result: The result is "done". History awaits review ("pending") with "Cough for three days.", sourced from
//   the transcript's characters 0 to 21; Assessment awaits review with "No fever."; Plan is empty with the reason
//   "not_dictated". Staging answers 200 with the review page's address. Two proposals are saved, both pending:
//   history "Cough for three days." and assessment "No fever.". The review is "pending", and the draft's History,
//   Assessment and Plan are still empty, marked as having proposals to review.
// Creator: Samir Mukshe
public class TC_UC10_01_SuccessfulFlow(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Fact]
    public async Task The_proposed_fields_come_back_with_their_sources_and_are_saved_for_review_only()
    {
        var flow = await BeginAsync(apps, "Remy", "Spoken");
        await DictateAsync(flow, 5);

        using var result = await ResultAsync(flow);
        var json = await JsonAsync(result);
        using var staged = await StageAsync(flow);
        var stage = await JsonAsync(staged);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("done", json.GetProperty("sorting").GetString());
        Assert.Equal("Cough for three days. No fever. Chest is clear. Likely viral. Fluids and rest.",
            json.GetProperty("transcript").GetProperty("text").GetString());
        var fields = json.GetProperty("fields");
        Assert.Equal("pending", fields.GetProperty("history").GetProperty("status").GetString());
        Assert.Equal("Cough for three days.", fields.GetProperty("history").GetProperty("value").GetString());
        var source = fields.GetProperty("history").GetProperty("sources")[0];
        Assert.Equal((0, 21), (source.GetProperty("start").GetInt32(), source.GetProperty("end").GetInt32()));
        Assert.Equal("pending", fields.GetProperty("assessment").GetProperty("status").GetString());
        Assert.Equal("No fever.", fields.GetProperty("assessment").GetProperty("value").GetString());
        Assert.Equal("empty", fields.GetProperty("plan").GetProperty("status").GetString());
        Assert.Equal("not_dictated", fields.GetProperty("plan").GetProperty("reasons")[0].GetString());

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.EndsWith($"/review/{flow.Review}", stage.GetProperty("url").GetString());
        Assert.Equal(AiReviewStates.Pending, await ScalarAsync<string>(apps, "SELECT state FROM ehr.ai_note_reviews WHERE id = @id", ("id", flow.Review)));
        Assert.Equal("assessment=No fever.|history=Cough for three days.", await ScalarAsync<string>(apps,
            "SELECT string_agg(field || '=' || original_value, '|' ORDER BY field) FROM ehr.ai_field_proposals WHERE review_id = @id AND state = 'pending' AND approved_value IS NULL",
            ("id", flow.Review)));
        Assert.Equal(1, await ScalarAsync<long>(apps,
            "SELECT count(*) FROM ehr.clinical_drafts WHERE id = @id AND coalesce(history, '') = '' AND coalesce(assessment, '') = '' AND coalesce(plan, '') = '' AND has_unreviewed_proposals",
            ("id", flow.Draft)));
        Assert.Contains(service.Received, request => request.PathAndQuery.EndsWith("/stop", StringComparison.Ordinal));
    }
}
