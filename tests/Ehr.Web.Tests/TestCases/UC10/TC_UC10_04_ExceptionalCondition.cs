using System.Net;
using Ehr.Data.Patients;
using Ehr.Web.Api.Dictation;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC10;

// Test Case ID: TC-UC10-04
// Purpose / scenario: The language model is down, or too slow to answer, so the transcript can't be sorted into fields.
//   The transcript comes back as plain text with a message saying so, and the review the page goes to has nothing to
//   review: the clinician writes the note from the transcript.
// Type: Functional (exceptional condition)
// Requirements: FR-11, FR-13
// Use case: UC 10, Structure and validate note fields (step 1 alternative: the language model is unavailable or times
//   out)
// Preconditions: The clinician has dictated from the dictation page for a patient on their panel.
// Inputs and system conditions: Stop, then staging the result for review, as the page does. The dictation service is
//   the test fixture's stub, set to answer that the model is unavailable, or that it timed out.
// Test data: Patient Quinn Unsorted; two audio chunks, transcribed as "Cough for three days. No fever."
// Steps:
//   1. Set the model's failure: unavailable, or timed out.
//   2. Dictate two chunks and select Stop.
//   3. Stage the result for review, and read the saved review.
// Expected result: Stop answers 200 with sorting "unavailable", the reason ("model_unavailable" or "model_timeout"),
//   the message "The fields couldn't be sorted, so the transcript is kept as plain text." and the transcript
//   "Cough for three days. No fever."; no field awaits review. Staging answers 200 with the review page's address;
//   the review is "completed" with no proposals.
// Creator: Samir Mukshe
public class TC_UC10_04_ExceptionalCondition(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Theory]
    [InlineData(StubSorting.Unavailable, "model_unavailable")]
    [InlineData(StubSorting.Timeout, "model_timeout")]
    public async Task With_the_model_down_the_transcript_stays_plain_text_and_says_why(StubSorting failure, string reason)
    {
        service.Sorting = failure;
        var flow = await BeginAsync(apps, "Quinn", "Unsorted");

        for (var seq = 0; seq < 2; seq++)
        {
            using var chunk = await SendChunkAsync(flow, seq);
            Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
        }

        using var stopped = await StopAsync(flow);
        var json = await JsonAsync(stopped);
        using var staged = await StageAsync(flow);
        var stage = await JsonAsync(staged);

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.Equal("unavailable", json.GetProperty("sorting").GetString());
        Assert.Equal(reason, json.GetProperty("reason").GetString());
        Assert.Equal(DictationResult.UnsortedMessage, json.GetProperty("message").GetString());
        Assert.Equal("The fields couldn't be sorted, so the transcript is kept as plain text.", json.GetProperty("message").GetString());
        Assert.Equal("Cough for three days. No fever.", json.GetProperty("transcript").GetProperty("text").GetString());
        if (json.GetProperty("fields").ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            Assert.All(json.GetProperty("fields").EnumerateObject(), field => Assert.Equal("empty", field.Value.GetProperty("status").GetString()));
        }

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.EndsWith($"/review/{flow.Review}", stage.GetProperty("url").GetString());
        Assert.Equal(AiReviewStates.Completed, await ScalarAsync<string>(apps, "SELECT state FROM ehr.ai_note_reviews WHERE id = @id", ("id", flow.Review)));
        Assert.Equal(0, await ScalarAsync<long>(apps, "SELECT count(*) FROM ehr.ai_field_proposals WHERE review_id = @id", ("id", flow.Review)));
    }
}
