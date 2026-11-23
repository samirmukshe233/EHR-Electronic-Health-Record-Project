using System.Net;
using Ehr.Data.Audit;
using Ehr.Web.Api.Dictation;
using Xunit;
using static Ehr.Web.Tests.Browser;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-01
// Purpose / scenario: A clinician opens "Dictate a note" for a patient on their panel, starts dictating, and the
//   transcript builds as the audio arrives.
// Type: Functional (successful flow)
// Requirements: FR-07, FR-10, FR-32, NFR-14
// Use case: UC 9, Transcribe dictated audio (main flow, steps 1 to 3)
// Preconditions: The clinician is signed in with their authenticator code on the on-site app, and the patient is on
//   their panel. The clinic's server is up.
// Inputs and system conditions: GET /patients/{id}/dictate; POST /api/dictation/sessions/for-note with the page's form
//   token; two audio chunks, seq 0 and 1. The dictation service is the test fixture's stub.
// Test data: Patient Dana Dictated. Chunks of type "audio/webm; codecs=opus"; the stub hears "Cough for three days."
//   in chunk 0 and "No fever." in chunk 1.
// Steps:
//   1. Open the dictation page and count the patient's recorded reads before and after.
//   2. Start a dictation for a new note with the page's form token.
//   3. Send chunk 0, then chunk 1, and read the transcript after each.
// Expected result: The page shows "Dictate a note: Dictated, Dana", a Dictate button and "Dictation supports English
//   only and needs a microphone.", and opening it is exactly one recorded read. The dictation starts with 201 Created,
//   in the "recording" state, with a new note draft. After chunk 0 the transcript is "Cough for three days."; after
//   chunk 1 it is "Cough for three days. No fever.", not yet final. Each chunk reaches the dictation service under the
//   clinician's own ID.
// Creator: Samir Mukshe
public class TC_UC9_01_SuccessfulFlow(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_transcript_builds_as_the_clinician_dictates()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Dana", "Dictated");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var readsBefore = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);

        var html = await session.Client.GetStringAsync($"/patients/{patient}/dictate", Cancel);

        Assert.Contains("Dictate a note: Dictated, Dana", html);
        Assert.Contains("data-dictation-start", html);
        Assert.Contains("Dictation supports English only and needs a microphone.", html);
        Assert.Equal(readsBefore + 1, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));

        var token = Token(html);
        using var started = await PostJsonAsync(session.Client, token, $"{Sessions}/for-note",
            new { reviewId = Guid.NewGuid(), patientId = patient, draftId = (Guid?)null, version = (long?)null });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var binding = await JsonAsync(started);
        Assert.Equal("recording", binding.GetProperty("state").GetString());
        Assert.NotEqual(Guid.Empty, binding.GetProperty("draftId").GetGuid());
        var flow = new Flow(session, patient, token, binding.GetProperty("reviewId").GetGuid(), binding.GetProperty("draftId").GetGuid(),
            binding.GetProperty("sessionId").GetString()!, binding.GetProperty("version").GetInt64());

        using var first = await SendChunkAsync(flow, 0);
        var afterFirst = await JsonAsync(first);
        using var second = await SendChunkAsync(flow, 1);
        var afterSecond = await JsonAsync(second);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Cough for three days.", afterFirst.GetProperty("transcript").GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, afterSecond.GetProperty("receivedSeq").GetInt64());
        Assert.Equal("Cough for three days. No fever.", afterSecond.GetProperty("transcript").GetProperty("text").GetString());
        Assert.False(afterSecond.GetProperty("transcript").GetProperty("final").GetBoolean());
        var chunks = service.Received.Where(r => r.PathAndQuery.Contains("/audio?seq=", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(clinician.Id.ToString(), chunk.Headers[DictationSidecar.OwnerHeader]));
    }
}
