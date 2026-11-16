using System.Net;
using Ehr.Data.Audit;
using Ehr.Web.CloudAudit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// UC6 "View test results" and FR-16 (Simar Rekhi): a signed-in patient sees their own released results, newest first,
// in plain language, and nobody else's; a patient with none is told so; and the page works off-site, read-only, while
// the clinic's server is down. Test cases TC-UC6-01 to TC-UC6-04 in the SE document.
public class PortalResultsTests(WebApps apps) : IClassFixture<WebApps>
{
    static readonly DateTimeOffset Nov2 = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    static readonly DateTimeOffset Oct14 = new(2026, 10, 14, 15, 0, 0, TimeSpan.Zero);

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // TC-UC6-01: the successful flow.
    [Fact]
    public async Task A_patient_sees_their_released_results_newest_first_in_plain_language()
    {
        var own = await apps.InsertPatientAsync("Jordan", "Results");
        var other = await apps.InsertPatientAsync("Riley", "Elsewhere");
        await apps.InsertResultAsync(own, "LDL cholesterol", "13457-7", 90m, "mg/dL", null, 100m, Oct14, Oct14);
        await apps.InsertResultAsync(own, "Hemoglobin A1c", "4548-4", 6.9m, "%", 4.0m, 5.6m, Nov2, Nov2,
            note: "We'll talk about this at your next visit.");
        await apps.InsertResultAsync(own, "Glucose", "2345-7", 140m, "mg/dL", 70m, 99m, Nov2, issuedAt: null);
        await apps.InsertResultAsync(own, "Potassium", "2823-3", 4.1m, "mmol/L", 3.5m, 5.1m, Nov2, Nov2, status: "preliminary");
        await apps.InsertResultAsync(other, "Thyroid stimulating hormone", "3016-3", 2.1m, "mIU/L", 0.4m, 4.0m, Nov2, Nov2);
        var (client, _) = await SignInPatientAsync(apps.OnPrem, own);
        // Signing in opens the portal's home, itself a read of the patient.
        var readsBefore = await apps.AuditEventsAsync(AuditActions.Read, patientId: own);

        using var response = await client.GetAsync("/portal/results", Cancel);
        var html = await ReadAsync(response);

        // Not kept by the browser, so Back after signing out doesn't show it again.
        Assert.True(response.Headers.CacheControl?.NoStore);
        // The sample's date, labelled with its time zone.
        Assert.Contains("Nov 2, 2026 UTC", html);

        // Newest first.
        Assert.True(html.IndexOf("Hemoglobin A1c", StringComparison.Ordinal) < html.IndexOf("LDL cholesterol", StringComparison.Ordinal));
        Assert.Contains("6.9 %", html);
        Assert.Contains("4 to 5.6 %", html);
        Assert.Contains("Your result is above the normal range (4 to 5.6 %).", html);
        Assert.Contains("This test measures your average blood sugar over the past 2 to 3 months.", html);
        Assert.Contains("We&#x27;ll talk about this at your next visit.", html);
        Assert.Contains(">High<", html);
        Assert.Contains("Your result is in the normal range (below 100 mg/dL).", html);
        // Not released yet, not finished, or someone else's.
        Assert.DoesNotContain("Glucose", html);
        Assert.DoesNotContain("Potassium", html);
        Assert.DoesNotContain("Thyroid", html);
        // One view of one patient: one recorded read.
        Assert.Equal(readsBefore + 1, await apps.AuditEventsAsync(AuditActions.Read, patientId: own));

        // Nor the portal's home, which shows the record too.
        using var home = await client.GetAsync("/portal", Cancel);
        Assert.True(home.Headers.CacheControl?.NoStore);
    }

    // TC-UC6-02: the "no results yet" course.
    [Fact]
    public async Task A_patient_with_no_released_results_is_told_so()
    {
        var patient = await apps.InsertPatientAsync("Morgan", "Waiting");
        await apps.InsertResultAsync(patient, "Lipid panel", "2093-3", 180m, "mg/dL", null, 200m, Nov2, issuedAt: null);
        var (client, _) = await SignInPatientAsync(apps.OnPrem, patient);

        var html = await client.GetStringAsync("/portal/results", Cancel);

        Assert.Contains("You have no test results yet. If you are waiting for one, contact your care team.", html);
        Assert.DoesNotContain("Lipid panel", html);
    }

    // TC-UC6-03: no one but a signed-in patient reaches the page.
    [Fact]
    public async Task Nobody_signed_out_and_no_clinician_reaches_the_results_page()
    {
        using var anonymous = await Client(apps.OnPrem, new CookieContainer(), followRedirects: false).GetAsync("/portal/results", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/portal/sign-in", anonymous.Headers.Location!.PathAndQuery);

        var clinician = await SignInAsync(apps.OnPrem, await apps.ClinicianAsync());
        using var staff = await Client(apps.OnPrem, clinician.Jar, followRedirects: false).GetAsync("/portal/results", Cancel);
        Assert.NotEqual(HttpStatusCode.OK, staff.StatusCode);
    }

    // TC-UC6-07: the "sign-in fails" course: the portal says so and shows no results.
    [Fact]
    public async Task A_wrong_password_is_refused_and_shows_no_results()
    {
        var patient = await apps.InsertPatientAsync("Casey", "Refused");
        await apps.InsertResultAsync(patient, "Hemoglobin A1c", "4548-4", 6.9m, "%", 4.0m, 5.6m, Nov2, Nov2);
        var (email, _) = await apps.CreatePatientAccountAsync(patient);
        var jar = new CookieContainer();
        var client = Client(apps.OnPrem, jar);

        using var refused = await PostFormAsync(client, "/portal/sign-in", "/portal/sign-in", ("Input.Email", email), ("Input.Password", "not-the-password-at-all"));
        Assert.Contains("Wrong email or password.", await ReadAsync(refused));

        using var results = await Client(apps.OnPrem, jar, followRedirects: false).GetAsync("/portal/results", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, results.StatusCode);
        Assert.StartsWith("/portal/sign-in", results.Headers.Location!.PathAndQuery);
    }

    // TC-UC6-04: the "on-site primary server is down" course (UC2): read from the cloud copy, marked read-only, and
    // the read recorded in the cloud app's queue for the clinic's server.
    [Fact]
    public async Task Off_site_with_the_clinic_server_down_results_are_read_only_and_the_read_is_queued()
    {
        var patient = await apps.InsertPatientAsync("Avery", "Offsite");
        await apps.InsertResultAsync(patient, "Hemoglobin", "718-7", 13.2m, "g/dL", 12.0m, 15.5m, Nov2, Nov2);
        // Signed in through the connected cloud app; the cookie works on the cut-off one, as it would after an outage.
        var (_, jar) = await SignInPatientAsync(apps.Cloud, patient);
        var cookie = jar.GetCookieHeader(Base);
        Assert.False(await apps.CloudCutOff.Services.GetRequiredService<CloudAuditShipper>().ShipAsync(Cancel));

        using var client = CreateClient(apps.CloudCutOff, handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/portal/results");
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request, Cancel);

        var html = await ReadAsync(response);
        Assert.Contains("13.2 g/dL", html);
        Assert.Contains("Read-only: the clinic's server can't be reached", html);
        var (queued, _) = apps.CloudCutOff.Services.GetRequiredService<CloudAuditQueue>().ReadPending(int.MaxValue);
        Assert.Contains(queued, e => e.Action == AuditActions.Read && e.PatientId == patient);
    }

    // A confirmed portal account for the patient, signed in through the given app's form.
    async Task<(HttpClient Client, CookieContainer Jar)> SignInPatientAsync(WebApplicationFactory<Program> app, Guid patient)
    {
        var (email, password) = await apps.CreatePatientAccountAsync(patient);
        var jar = new CookieContainer();
        var client = Client(app, jar);
        using var signedIn = await PostFormAsync(client, "/portal/sign-in", "/portal/sign-in", ("Input.Email", email), ("Input.Password", password));
        Assert.Equal("/portal", PathOf(signedIn));
        return (client, jar);
    }
}
