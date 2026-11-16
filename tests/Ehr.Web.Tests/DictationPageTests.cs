using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Ehr.Data.Accounts;
using Ehr.Data.Audit;
using Ehr.Web.Api.Dictation;
using Ehr.Web.CloudAudit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// The dictation page (Areas/Clinical/Pages/Patients/Dictate; UC 9 and UC 10, Samir Mukshe): who reaches it, what it
// offers on-site and off-site, and that the form token it renders is what its script (js/dictation.js) needs to drive a
// whole dictation through the API (DictationApi) against the stub dictation service. The script's own behaviour in a
// browser is checked by hand: these tests cover the server side of the screen.
public class DictationPageTests(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    static string DictatePath(Guid patient) => $"/patients/{patient}/dictate";

    [Fact]
    public async Task A_clinician_opens_dictation_for_a_patient_on_their_panel()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Dana", "Dictated");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var before = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);

        var html = await session.Client.GetStringAsync(DictatePath(patient), Cancel);

        Assert.Contains("Dictate a note: Dictated, Dana", html);
        Assert.Contains("data-dictation-start", html);
        Assert.Contains($"data-dictation-api=\"{DictationApi.Path}\"", html);
        // The script, under its fingerprinted name (dictation.<hash>.js).
        Assert.Contains("/_content/Ehr.Design/js/dictation.", html);
        Assert.Contains("Dictation supports English only", html);
        Assert.Contains("Nothing is saved", html);
        Assert.Contains("orders and allergy changes are never filled in from dictation", html);
        foreach (var field in new[] { "history", "assessment", "plan" })
        {
            Assert.Contains($"data-dictation-field=\"{field}\"", html);
        }

        Assert.NotEmpty(Token(html));
        // Showing the patient's name is one recorded read, as the chart's is.
        Assert.Equal(before + 1, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));
    }

    [Fact]
    public async Task The_page_token_drives_a_whole_dictation_to_pending_fields()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Remy", "Spoken");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var token = Token(await session.Client.GetStringAsync(DictatePath(patient), Cancel));

        // As js/dictation.js sends them: start, two chunks, stop, then result.
        using var started = await SendAsync(session.Client, token, HttpMethod.Post, DictationApi.Path);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (string)(await JsonAsync(started))["sessionId"]!;
        using var first = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/audio?seq=0", Audio([1, 2, 3]));
        using var second = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/audio?seq=1", Audio([4, 5, 6]));
        using var stopped = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/stop");
        using var result = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/result");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Cough for three days. No fever.", (string?)(await JsonAsync(second))["transcript"]!["text"]);
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        var json = await JsonAsync(result);
        Assert.Equal("done", (string?)json["sorting"]);
        Assert.Equal("pending", (string?)json["fields"]!["history"]!["status"]);
        Assert.Equal("Cough for three days.", (string?)json["fields"]!["history"]!["value"]);
        Assert.Equal("empty", (string?)json["fields"]!["plan"]!["status"]);
        Assert.Equal("order", (string?)json["notCarriedOver"]![0]!["reason"]);
        // The audio reached the service for this clinician, in the type the browser recorded.
        var chunk = Assert.Single(service.Received, r => r.PathAndQuery.EndsWith("/audio?seq=0", StringComparison.Ordinal));
        Assert.Equal(clinician.Id.ToString(), chunk.Headers[DictationSidecar.OwnerHeader]);
    }

    [Fact]
    public async Task With_the_model_unavailable_the_transcript_comes_back_with_the_message_the_page_shows()
    {
        service.Sorting = StubSorting.Unavailable;
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Quinn", "Unsorted");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var token = Token(await session.Client.GetStringAsync(DictatePath(patient), Cancel));

        using var started = await SendAsync(session.Client, token, HttpMethod.Post, DictationApi.Path);
        var id = (string)(await JsonAsync(started))["sessionId"]!;
        using var chunk = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/audio?seq=0", Audio([1, 2, 3]));
        using var stopped = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/stop");

        var json = await JsonAsync(stopped);
        Assert.Equal("unavailable", (string?)json["sorting"]);
        Assert.Equal(DictationResult.UnsortedMessage, (string?)json["message"]);
        Assert.False(string.IsNullOrEmpty((string?)json["transcript"]!["text"]));
    }

    [Fact]
    public async Task Cancel_from_the_page_discards_the_dictation()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Sky", "Cancelled");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var token = Token(await session.Client.GetStringAsync(DictatePath(patient), Cancel));
        using var started = await SendAsync(session.Client, token, HttpMethod.Post, DictationApi.Path);
        var id = (string)(await JsonAsync(started))["sessionId"]!;
        using var chunk = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/audio?seq=0", Audio([1, 2, 3]));

        using var cancelled = await SendAsync(session.Client, token, HttpMethod.Delete, $"{DictationApi.Path}/{id}");
        using var afterwards = await SendAsync(session.Client, token, HttpMethod.Post, $"{DictationApi.Path}/{id}/stop");

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task A_patient_off_the_panel_without_emergency_access_is_asked_for_a_reason_first()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Noel", "Notmine");
        var session = await SignInAsync(apps.OnPrem, clinician);
        var before = await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient);

        using var response = await Client(apps.OnPrem, session.Jar, followRedirects: false).GetAsync(DictatePath(patient), Cancel);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/patients/{patient}/reason", new Uri(Base, response.Headers.Location!).AbsolutePath);
        // Nothing about the patient was read.
        Assert.Equal(before, await apps.AuditEventsAsync(AuditActions.Read, clinician.Id, patient));
    }

    [Fact]
    public async Task Anyone_but_a_clinician_is_refused()
    {
        var patient = await apps.InsertPatientAsync("Ola", "Guarded");
        using var anonymous = await Client(apps.OnPrem, new CookieContainer(), followRedirects: false).GetAsync(DictatePath(patient), Cancel);
        var admin = await SignInAsync(apps.OnPrem, await apps.CreateStaffAsync("Dictation Page Admin", StaffRoles.Admin));
        using var asAdmin = await Client(apps.OnPrem, admin.Jar, followRedirects: false).GetAsync(DictatePath(patient), Cancel);

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/sign-in", new Uri(Base, anonymous.Headers.Location!).AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect, asAdmin.StatusCode);
        Assert.Equal("/denied", new Uri(Base, asAdmin.Headers.Location!).AbsolutePath);
    }

    [Fact]
    public async Task Off_site_with_the_clinics_server_down_no_dictation_is_offered()
    {
        var clinician = await apps.CreateStaffAsync("Dr. Dictating Away", StaffRoles.Clinician);
        var patient = await apps.InsertPatientAsync("Pat", "Offsite");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        // The cloud app finds out within a couple of seconds; this is that check, now.
        Assert.False(await apps.CloudCutOff.Services.GetRequiredService<CloudAuditShipper>().ShipAsync(Cancel));

        var html = await Client(apps.CloudCutOff, session.Jar).GetStringAsync(DictatePath(patient), Cancel);

        Assert.Contains("Dictate a note: Offsite, Pat", html);
        Assert.Contains("Dictation needs the clinic's server, which can't be reached right now.", html);
        Assert.DoesNotContain("data-dictation-start", html);
        Assert.DoesNotContain("/js/dictation.", html);
    }

    [Fact]
    public async Task The_chart_links_to_dictation()
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Cleo", "Linked");
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);

        var html = await session.Client.GetStringAsync($"/patients/{patient}", Cancel);

        Assert.Contains($"href=\"{DictatePath(patient)}\"", html);
        Assert.Contains("Dictate a note", html);
        // Only the dictation page loads its script.
        Assert.DoesNotContain("/js/dictation.", html);
    }

    static ByteArrayContent Audio(byte[] bytes) =>
        new(bytes) { Headers = { ContentType = MediaTypeHeaderValue.Parse("audio/webm; codecs=opus") } };

    static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request, Cancel);
    }

    static async Task<JsonNode> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;
}
