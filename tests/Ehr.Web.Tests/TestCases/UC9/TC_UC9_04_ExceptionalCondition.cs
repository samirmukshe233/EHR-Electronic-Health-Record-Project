using System.Net;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-04
// Purpose / scenario: The connection drops mid-dictation and a chunk is lost. The next chunk to arrive skips ahead, so
//   the dictation says which chunk to send again; once the page resends from there, the transcript carries on with
//   nothing lost or doubled.
// Type: Functional (exceptional condition)
// Requirements: FR-07, FR-10
// Use case: UC 9, Transcribe dictated audio (step 2 alternative: the connection to the dictation service drops)
// Preconditions: The clinician has started a dictation from the dictation page for a patient on their panel, and
//   chunk 0 has been accepted.
// Inputs and system conditions: Chunk 2 sent before chunk 1, as when chunk 1 was lost; chunk 1 resent; chunk 2 resent.
//   The dictation service is the test fixture's stub.
// Test data: Patient Riley Resumed. Chunks seq 0 (bytes 1, 2, 3), seq 1 (bytes 4, 5, 6) and seq 2 (bytes 7, 8, 9).
// Steps:
//   1. Send chunk 0.
//   2. Send chunk 2.
//   3. Send chunk 1, then chunk 2 again, as the page does after the refusal.
// Expected result: Chunk 2 is refused with 409 out_of_order, expectedSeq 1, "Send the audio again from chunk 1."
//   Chunk 1 is then accepted with receivedSeq 1, and chunk 2 with receivedSeq 2. The transcript reads "Cough for three
//   days. No fever. Chest is clear.", each sentence once.
// Creator: Samir Mukshe
public class TC_UC9_04_ExceptionalCondition(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Fact]
    public async Task After_a_lost_chunk_the_dictation_says_where_to_resume_and_the_transcript_carries_on()
    {
        var flow = await BeginAsync(apps, "Riley", "Resumed");
        using (var first = await SendChunkAsync(flow, 0, [1, 2, 3]))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var skipped = await SendChunkAsync(flow, 2, [7, 8, 9]);
        var refusal = await JsonAsync(skipped);
        using var resent = await SendChunkAsync(flow, 1, [4, 5, 6]);
        var afterResent = await JsonAsync(resent);
        using var next = await SendChunkAsync(flow, 2, [7, 8, 9]);
        var afterNext = await JsonAsync(next);

        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        Assert.Equal("out_of_order", refusal.GetProperty("error").GetString());
        Assert.Equal(1, refusal.GetProperty("expectedSeq").GetInt64());
        Assert.Equal("Send the audio again from chunk 1.", refusal.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Equal(1, afterResent.GetProperty("receivedSeq").GetInt64());
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(2, afterNext.GetProperty("receivedSeq").GetInt64());
        Assert.Equal("Cough for three days. No fever. Chest is clear.", afterNext.GetProperty("transcript").GetProperty("text").GetString());
        // The service took three chunks, though four were sent: the one that skipped ahead never counted.
        Assert.Equal(4, service.Received.Count(r => r.PathAndQuery.Contains("/audio?seq=", StringComparison.Ordinal)));
    }
}
