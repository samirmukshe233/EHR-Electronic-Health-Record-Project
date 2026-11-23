using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ehr.Testing;
using Ehr.Web.Api.Dictation;
using Npgsql;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests.TestCases;

// What the UC 9 and UC 10 test cases (TestCases/UC9, TestCases/UC10; Samir Mukshe) share: a dictation driven through
// the web app's API the way the dictation page's script (js/dictation.js) drives it. The page's form token goes in a
// header; the dictation starts for the patient's note ("/for-note", which binds it to a review of a note draft); the
// audio goes up in numbered chunks; Stop, then the result; and the result is staged for review. The dictation service
// is the fixture's stub (DictationSidecarStub), which answers as the real one does. Also the committed dictation eval
// results, which the non-functional cases measure.
static class DictationCaseFlow
{
    public const string Sessions = DictationApi.Path;

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // A dictation under way: who started it, for which patient and draft, and the IDs the page keeps.
    public sealed record Flow(Session Session, Guid Patient, string Token, Guid Review, Guid Draft, string SessionId, long Version);

    // Signs the clinician in, opens the dictation page for a patient on their panel for its form token, and starts a
    // dictation for a new note, as the page's Dictate button does.
    public static async Task<Flow> BeginAsync(WebApps apps, string given, string family)
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync(given, family);
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var token = Token(await session.Client.GetStringAsync($"/patients/{patient}/dictate", Cancel));
        var review = Guid.NewGuid();
        using var started = await PostJsonAsync(session.Client, token, $"{Sessions}/for-note",
            new { reviewId = review, patientId = patient, draftId = (Guid?)null, version = (long?)null });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var json = await JsonAsync(started);
        return new(session, patient, token, review, json.GetProperty("draftId").GetGuid(),
            json.GetProperty("sessionId").GetString()!, json.GetProperty("version").GetInt64());
    }

    public static async Task<HttpResponseMessage> SendChunkAsync(Flow flow, int seq, byte[]? bytes = null, string? type = "audio/webm; codecs=opus")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Sessions}/{flow.SessionId}/audio?seq={seq}")
        {
            Content = new ByteArrayContent(bytes ?? [1, 2, 3]),
        };
        if (type is not null)
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        }

        request.Headers.Add("RequestVerificationToken", flow.Token);
        return await flow.Session.Client.SendAsync(request, Cancel);
    }

    public static async Task<HttpResponseMessage> SendAsync(Flow flow, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("RequestVerificationToken", flow.Token);
        return await flow.Session.Client.SendAsync(request, Cancel);
    }

    public static Task<HttpResponseMessage> StopAsync(Flow flow) => SendAsync(flow, HttpMethod.Post, $"{Sessions}/{flow.SessionId}/stop");

    public static Task<HttpResponseMessage> ResultAsync(Flow flow) => SendAsync(flow, HttpMethod.Post, $"{Sessions}/{flow.SessionId}/result");

    public static Task<HttpResponseMessage> StageAsync(Flow flow) =>
        PostJsonAsync(flow.Session.Client, flow.Token, $"{Sessions}/review/{flow.Review}/stage", new { version = flow.Version });

    // Sends `chunks` chunks in order, each accepted, then Stop.
    public static async Task DictateAsync(Flow flow, int chunks)
    {
        for (var seq = 0; seq < chunks; seq++)
        {
            using var chunk = await SendChunkAsync(flow, seq);
            Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
        }

        using var stopped = await StopAsync(flow);
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
    }

    public static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string token, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request, Cancel);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).RootElement.Clone();

    // One value from the database, read as the superuser, so row-level security doesn't hide what the test checks.
    public static async Task<T> ScalarAsync<T>(WebApps apps, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync(Cancel))!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    // The committed dictation eval results (eval/results/latest.json), which CI's eval gate checks against this commit.
    public static JsonElement EvalResults() =>
        JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("eval/results/latest.json"))).RootElement.Clone();
}
