using Ehr.Data.Patients;
using Microsoft.Playwright;
using Npgsql;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// The dictation page in a real browser (UC 9 Transcribe dictated audio, UC 10 Structure and validate note fields;
// Samir Mukshe): what the clinician sees while dictating and after Stop, which the API-level tests can't show. Chromium,
// driven by Playwright, opens the real in-memory app over loopback HTTPS (ClinicalBrowserHost); the microphone is a
// fake recorder that hands the page five chunks of synthetic audio, and the dictation service is the fixture's stub.
// No real microphone, voice or model. Each test names the test case whose on-screen part it checks.
//
// In the browser collection, so nothing changes the machine's network under Chromium (AiReviewBrowserTests says why).
[Collection(BrowserTests.Collection)]
public class DictationBrowserTests(WebApps apps) : IClassFixture<WebApps>
{
    // A microphone that works: five chunks, 50 ms apart, then nothing until Stop.
    const string WorkingMicrophone = """
        Object.defineProperty(navigator, 'mediaDevices', { value: { getUserMedia: async () => ({ getTracks: () => [{ stop() {} }] }) } });
        window.MediaRecorder = class extends EventTarget {
            constructor() { super(); this.state = 'inactive'; this.mimeType = 'audio/webm'; }
            start() {
                this.state = 'recording';
                for (let i = 0; i < 5; i++) setTimeout(() => {
                    if (this.state === 'recording') this.dispatchEvent(new BlobEvent('dataavailable', { data: new Blob(['synthetic audio'], { type: this.mimeType }) }));
                }, 20 + i * 50);
            }
            stop() { this.state = 'inactive'; this.dispatchEvent(new Event('stop')); }
        };
        """;

    // A microphone the browser blocks, as when the clinician has denied the site access to it.
    const string BlockedMicrophone = """
        Object.defineProperty(navigator, 'mediaDevices', { value: { getUserMedia: async () => { throw new DOMException('Permission denied', 'NotAllowedError'); } } });
        """;

    // The stub's transcript after all five chunks.
    const string WholeTranscript = "Cough for three days. No fever. Chest is clear. Likely viral. Fluids and rest.";

    readonly DictationSidecarStub service = apps.Dictation.Reset();

    // A clinician signed in, with a new patient on their panel, and a browser page for them with the given microphone.
    sealed record Dictation(Guid Patient, ClinicalBrowserHost Server, IPlaywright Playwright, IBrowser Browser, IBrowserContext Context, IPage Page, List<string> Errors)
        : IAsyncDisposable
    {
        public string DictateUrl => $"{Server.Url}/patients/{Patient}/dictate";

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Browser.DisposeAsync();
            Playwright.Dispose();
            await Server.DisposeAsync();
        }
    }

    async Task<Dictation> OpenAsync(string family, string microphone = WorkingMicrophone)
    {
        var clinician = await apps.ClinicianAsync();
        var patient = await apps.InsertPatientAsync("Synthetic", family);
        await apps.AssignAsync(clinician, patient);
        var session = await SignInAsync(apps.OnPrem, clinician);
        var server = await ClinicalBrowserHost.StartAsync(apps);
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await AiReviewBrowserTests.LaunchAsync(playwright);
        var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        await context.AddCookiesAsync(session.Jar.GetCookies(Base).Cast<System.Net.Cookie>().Select(c => new Microsoft.Playwright.Cookie
        { Name = c.Name, Value = c.Value, Domain = "localhost", Path = c.Path, Secure = true, HttpOnly = c.HttpOnly, SameSite = SameSiteAttribute.Strict }));
        await context.AddInitScriptAsync(microphone);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var dictation = new Dictation(patient, server, playwright, browser, context, page, errors);
        await page.GotoAsync(dictation.DictateUrl);
        return dictation;
    }

    static Task<string?> StatusAsync(IPage page) => page.Locator("[data-dictation-status]").TextContentAsync();

    static Task WaitForStatusAsync(IPage page, string text) =>
        page.WaitForFunctionAsync("text => document.querySelector('[data-dictation-status]').textContent.includes(text)", text);

    static Task WaitForTranscriptAsync(IPage page, string text) =>
        page.WaitForFunctionAsync("text => document.querySelector('[data-dictation-transcript]').textContent.includes(text)", text);

    // Stop, then wait for the page to stage the result and go to its review, saying what it showed if it never does.
    static async Task StopAndReachReviewAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Alt+Shift+D");
        try
        {
            await page.WaitForURLAsync("**/review/*");
        }
        catch (TimeoutException)
        {
            Assert.Fail($"The dictation never reached its review. Page: {page.Url}. Status: \"{await StatusAsync(page)}\".");
        }
    }

    async Task<string> ScalarAsync(string sql, Guid id)
    {
        await using var connection = await apps.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    // TC-UC9-01 on screen (FR-07, FR-12): while the clinician dictates, the page says it's recording and the transcript
    // builds; a passage too noisy to trust is marked, with why, and the page says to say it again.
    [Fact]
    public async Task TC_UC9_01_the_transcript_builds_on_screen_and_an_unclear_passage_is_marked()
    {
        await using var dictation = await OpenAsync("Onscreen");
        var page = dictation.Page;

        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForTranscriptAsync(page, "Fluids and rest.");

        Assert.Equal("Recording. Speak your note, then select Stop.", await StatusAsync(page));
        Assert.True(await page.Locator("[data-dictation-recording]").IsVisibleAsync());
        Assert.Equal(WholeTranscript, (await page.Locator("[data-dictation-transcript]").TextContentAsync())?.Trim());
        var unclear = page.Locator("mark.sc-transcript__unclear");
        Assert.Equal(1, await unclear.CountAsync());
        Assert.Equal("Chest is clear.", await unclear.TextContentAsync());
        Assert.Equal("Say this again: too much background noise.", await unclear.GetAttributeAsync("title"));
        Assert.True(await page.Locator("[data-dictation-unclear]").IsVisibleAsync());
        Assert.Empty(dictation.Errors);
    }

    // UC 9 step 1 alternative (NFR-11): the browser blocks the microphone. The page says how to fix it, starts nothing on
    // the server, and can be tried again.
    [Fact]
    public async Task A_blocked_microphone_says_how_to_allow_it_and_starts_nothing()
    {
        await using var dictation = await OpenAsync("Blocked", BlockedMicrophone);
        var page = dictation.Page;

        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForStatusAsync(page, "The microphone is blocked.");

        Assert.Equal("The microphone is blocked. Allow it for this site in the browser's settings (the icon beside the address), "
            + "then select Dictate again. You can still type the note.", await StatusAsync(page));
        Assert.True(await page.Locator("[data-dictation-start]").IsEnabledAsync());
        Assert.False(await page.Locator("[data-dictation-recording]").IsVisibleAsync());
        Assert.Empty(service.Received);
        Assert.Equal("0", await ScalarAsync("SELECT count(*) FROM ehr.ai_note_reviews WHERE patient_id = @id", dictation.Patient));
        Assert.Empty(dictation.Errors);
    }

    // TC-UC9-04 on screen (FR-07, FR-10): the connection to the dictation service drops while the clinician dictates.
    // The page keeps recording and says the transcript is paused; when the connection returns it sends what it held, and
    // the transcript catches up with each sentence once.
    [Fact]
    public async Task TC_UC9_04_a_dropped_connection_pauses_the_transcript_and_it_catches_up()
    {
        await using var dictation = await OpenAsync("Reconnected");
        var page = dictation.Page;
        await page.RouteAsync("**/audio?seq=*", route => route.AbortAsync("connectionrefused"));

        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForStatusAsync(page, "The connection to the dictation service dropped.");
        var whilePaused = (await page.Locator("[data-dictation-transcript]").TextContentAsync())?.Trim();
        await page.UnrouteAsync("**/audio?seq=*");
        await WaitForStatusAsync(page, "Connection back.");
        await WaitForTranscriptAsync(page, "Fluids and rest.");

        Assert.Equal("Your words appear here as you speak.", whilePaused);
        Assert.Equal("Connection back. Recording; the transcript has caught up.", await StatusAsync(page));
        Assert.Equal(WholeTranscript, (await page.Locator("[data-dictation-transcript]").TextContentAsync())?.Trim());
        Assert.True(await page.Locator("[data-dictation-recording]").IsVisibleAsync());
        Assert.Empty(dictation.Errors);
    }

    // TC-UC9-02 on screen (FR-13): the clinician cancels mid-dictation. The page says the dictation was discarded and no
    // AI text approved, clears the transcript, and the review and the dictation are gone.
    [Fact]
    public async Task TC_UC9_02_cancel_says_the_dictation_was_discarded_and_clears_the_screen()
    {
        await using var dictation = await OpenAsync("Discarded");
        var page = dictation.Page;
        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForTranscriptAsync(page, "Cough for three days.");

        await page.Locator("[data-dictation-cancel]").ClickAsync();
        await WaitForStatusAsync(page, "Temporary dictation discarded.");

        Assert.Equal("Temporary dictation discarded. The manual draft was kept; no AI text was approved.", await StatusAsync(page));
        Assert.Equal("Your words appear here as you speak.", (await page.Locator("[data-dictation-transcript]").TextContentAsync())?.Trim());
        Assert.False(await page.Locator("[data-dictation-recording]").IsVisibleAsync());
        Assert.EndsWith("/dictate", page.Url);
        Assert.Equal(AiReviewStates.Cancelled, await ScalarAsync("SELECT state FROM ehr.ai_note_reviews WHERE patient_id = @id", dictation.Patient));
        Assert.Contains(service.Received, request => request.Method == "DELETE");
        Assert.Empty(dictation.Errors);
    }

    // TC-UC10-01 and TC-UC10-02 on screen (FR-08, FR-13, FR-14): after Stop the page saves the proposals for review and
    // opens the review, where each proposed field awaits review, a field with nothing dictated says so, and the dictated
    // order is listed as not carried over, for the clinician to place.
    [Fact]
    public async Task TC_UC10_01_02_after_stop_the_review_shows_the_proposals_and_the_order_not_carried_over()
    {
        await using var dictation = await OpenAsync("Reviewed");
        var page = dictation.Page;
        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForTranscriptAsync(page, "Fluids and rest.");

        await StopAndReachReviewAsync(page);
        await page.WaitForFunctionAsync("document.querySelector('[data-review-progress]').textContent.includes('of 2')");

        var history = page.Locator("[data-dictation-field=history]");
        Assert.Equal("Awaiting review", await history.Locator("[data-field-status]").TextContentAsync());
        Assert.Contains("Cough for three days.", await history.Locator("[data-field-value]").TextContentAsync());
        Assert.Equal("Awaiting review", await page.Locator("[data-dictation-field=assessment] [data-field-status]").TextContentAsync());
        var plan = page.Locator("[data-dictation-field=plan]");
        Assert.Equal("Left empty", await plan.Locator("[data-field-status]").TextContentAsync());
        Assert.Contains("Nothing was dictated for this field.", await plan.Locator("[data-field-value]").TextContentAsync());
        Assert.Equal("0 of 2 proposals reviewed.", await page.Locator("[data-review-progress]").TextContentAsync());
        Assert.True(await page.Locator("[data-dictation-not-carried]").IsVisibleAsync());
        Assert.Equal("Order: “Cough”", (await page.Locator("[data-dictation-not-carried-list] li").TextContentAsync())?.Trim());
        Assert.Empty(dictation.Errors);
    }

    // TC-UC10-03 on screen (FR-12): validation discarded a proposed Plan. Its text appears nowhere on the review, and the
    // Plan says what was proposed didn't pass the checks.
    [Fact]
    public async Task TC_UC10_03_a_discarded_value_never_appears_on_the_review()
    {
        service.Sorting = StubSorting.Discarded;
        await using var dictation = await OpenAsync("Unsupported");
        var page = dictation.Page;
        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForTranscriptAsync(page, "Fluids and rest.");

        await StopAndReachReviewAsync(page);
        await page.WaitForFunctionAsync("document.querySelector('[data-review-progress]').textContent.includes('of 1')");

        Assert.DoesNotContain(DictationSidecarStub.DiscardedText, await page.ContentAsync());
        var plan = page.Locator("[data-dictation-field=plan]");
        Assert.Equal("Left empty", await plan.Locator("[data-field-status]").TextContentAsync());
        Assert.Contains("No suggestion: what was proposed didn't pass the automatic checks.", await plan.Locator("[data-field-value]").TextContentAsync());
        Assert.Equal("Awaiting review", await page.Locator("[data-dictation-field=history] [data-field-status]").TextContentAsync());
        Assert.Empty(dictation.Errors);
    }

    // TC-UC10-04 on screen (FR-11): the language model is down. After Stop the page still goes to the review, which shows
    // the transcript as plain text, says the fields couldn't be sorted, and has nothing to review.
    [Fact]
    public async Task TC_UC10_04_with_the_model_down_the_review_shows_the_transcript_and_says_why()
    {
        service.Sorting = StubSorting.Unavailable;
        await using var dictation = await OpenAsync("Unsorted");
        var page = dictation.Page;
        await page.Keyboard.PressAsync("Alt+Shift+D");
        await WaitForTranscriptAsync(page, "Fluids and rest.");

        await StopAndReachReviewAsync(page);
        await page.WaitForFunctionAsync("document.querySelector('[data-dictation-sort-message]').textContent.length > 0");

        Assert.Equal("The fields couldn't be sorted, so the transcript is kept as plain text.",
            await page.Locator("[data-dictation-sort-message]").TextContentAsync());
        Assert.Equal(WholeTranscript, (await page.Locator("[data-dictation-transcript]").TextContentAsync())?.Trim());
        Assert.Equal("0 of 0 proposals reviewed.", await page.Locator("[data-review-progress]").TextContentAsync());
        Assert.Equal(0, await page.Locator("[data-field-status]", new() { HasTextString = "Awaiting review" }).CountAsync());
        Assert.Empty(dictation.Errors);
    }
}
