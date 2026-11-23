using System.Net;
using Ehr.Data.Patients;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-02
// Purpose / scenario: The clinician cancels mid-dictation. The recording and transcript are discarded and no AI text
//   reaches the note; the note draft the dictation started stays, empty, for the clinician to type in.
// Type: Functional (alternative flow)
// Requirements: FR-13
// Use case: UC 9, Transcribe dictated audio (alternative course, any step: the clinician cancels)
// Preconditions: The clinician has started a dictation from the dictation page for a patient on their panel, and one
//   chunk has been sent.
// Inputs and system conditions: What the page's Cancel button sends: POST
//   /api/dictation/sessions/review/{reviewId}/discard, then DELETE /api/dictation/sessions/{sessionId}. Then Stop on
//   the same dictation. The dictation service is the test fixture's stub.
// Test data: Patient Sky Cancelled; one audio chunk (bytes 1, 2, 3).
// Steps:
//   1. Start a dictation and send chunk 0.
//   2. Cancel: discard the review, then delete the dictation.
//   3. Try to stop the cancelled dictation.
// Expected result: The discard answers 200 and the delete 204 No Content. Stop afterwards answers 404 with
//   "This dictation has ended, or can't be found." The review is "cancelled" with no proposals and no stored result,
//   and the note draft is kept with History, Assessment and Plan empty.
// Creator: Samir Mukshe
public class TC_UC9_02_AlternativeFlow(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Fact]
    public async Task Cancel_discards_the_dictation_and_keeps_an_empty_draft()
    {
        var flow = await BeginAsync(apps, "Sky", "Cancelled");
        using (var chunk = await SendChunkAsync(flow, 0))
        {
            Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
        }

        using var discarded = await SendAsync(flow, HttpMethod.Post, $"{Sessions}/review/{flow.Review}/discard");
        using var deleted = await SendAsync(flow, HttpMethod.Delete, $"{Sessions}/{flow.SessionId}");
        using var afterwards = await StopAsync(flow);

        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Contains(service.Received, request => request.Method == "DELETE" && request.PathAndQuery == $"/v1/sessions/{flow.SessionId}");
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
        var refusal = await JsonAsync(afterwards);
        Assert.Equal("not_found", refusal.GetProperty("error").GetString());
        Assert.Equal("This dictation has ended, or can't be found.", refusal.GetProperty("message").GetString());
        Assert.Equal(AiReviewStates.Cancelled, await ScalarAsync<string>(apps,
            "SELECT state FROM ehr.ai_note_reviews WHERE id = @id AND result_json IS NULL", ("id", flow.Review)));
        Assert.Equal(0, await ScalarAsync<long>(apps, "SELECT count(*) FROM ehr.ai_field_proposals WHERE review_id = @id", ("id", flow.Review)));
        Assert.Equal(1, await ScalarAsync<long>(apps,
            "SELECT count(*) FROM ehr.clinical_drafts WHERE id = @id AND coalesce(history, '') = '' AND coalesce(assessment, '') = '' AND coalesce(plan, '') = ''",
            ("id", flow.Draft)));
    }
}
