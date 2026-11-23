using System.Net;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-03
// Purpose / scenario: A chunk that isn't audio, or a recording that can't be decoded, is refused with a message the
//   page can show, and nothing that isn't audio reaches the speech model.
// Type: Functional (invalid input)
// Requirements: FR-10
// Use case: UC 9, Transcribe dictated audio (step 2: the audio can't be transcribed)
// Preconditions: The clinician has started a dictation from the dictation page for a patient on their panel.
// Inputs and system conditions: POST /api/dictation/sessions/{sessionId}/audio?seq=0 with a body that isn't audio;
//   separately, a recording the decoder can't read, then Stop. The dictation service is the test fixture's stub, set
//   to "undecodable" for the second part.
// Test data: Patients Ivy Invalid and Uma Undecodable. Bytes 1, 2, 3 sent as "application/json", "text/plain",
//   "video/webm" and with no content type.
// Steps:
//   1. Send each body that isn't audio as chunk 0.
//   2. Start another dictation, send a chunk the decoder can't read, and select Stop.
// Expected result: Each body that isn't audio is refused with 415 unsupported_type, "Send the recording as audio.",
//   and no chunk reaches the dictation service. Stop on the undecodable recording is refused with 422 not_audio,
//   "The audio couldn't be decoded."
// Creator: Samir Mukshe
public class TC_UC9_03_InvalidInput(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("video/webm")]
    [InlineData(null)]
    public async Task A_chunk_that_isnt_audio_is_refused_before_it_reaches_the_speech_model(string? type)
    {
        var flow = await BeginAsync(apps, "Ivy", "Invalid");

        using var response = await SendChunkAsync(flow, 0, [1, 2, 3], type);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var refusal = await JsonAsync(response);
        Assert.Equal("unsupported_type", refusal.GetProperty("error").GetString());
        Assert.Equal("Send the recording as audio.", refusal.GetProperty("message").GetString());
        Assert.DoesNotContain(service.Received, request => request.PathAndQuery.Contains("/audio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_recording_that_cant_be_decoded_is_refused_at_stop()
    {
        var flow = await BeginAsync(apps, "Uma", "Undecodable");
        service.Undecodable = true;
        using (var chunk = await SendChunkAsync(flow, 0))
        {
            Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
        }

        using var stopped = await StopAsync(flow);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, stopped.StatusCode);
        var refusal = await JsonAsync(stopped);
        Assert.Equal("not_audio", refusal.GetProperty("error").GetString());
        Assert.Equal("The audio couldn't be decoded.", refusal.GetProperty("message").GetString());
    }
}
