using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// UC6 "View test results" and FR-16 (Simar Rekhi): a signed-in patient sees their own released results, newest first,
// in plain language, and nobody else's; a patient with none is told so; and the page works off-site, read-only, while
// the clinic's server is down. TC-UC6-01, 02 and 04 are in TestCases/UC6, one file each, and use the dates and the
// sign-in below; TC-UC6-03 and 07 stay here.
public class PortalResultsTests(WebApps apps) : IClassFixture<WebApps>
{
    internal static readonly DateTimeOffset Nov2 = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    internal static readonly DateTimeOffset Oct14 = new(2026, 10, 14, 15, 0, 0, TimeSpan.Zero);

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // TC-UC6-01 and TC-UC6-02 moved to TestCases/UC6/TC_UC6_01_SuccessfulFlow.cs and TC_UC6_02_AlternativeFlow.cs.

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

    // TC-UC6-04 moved to TestCases/UC6/TC_UC6_04_ExceptionalCondition.cs.

    // A confirmed portal account for the patient, signed in through the given app's form.
    internal static async Task<(HttpClient Client, CookieContainer Jar)> SignInPatientAsync(WebApps apps, WebApplicationFactory<Program> app, Guid patient)
    {
        var (email, password) = await apps.CreatePatientAccountAsync(patient);
        var jar = new CookieContainer();
        var client = Client(app, jar);
        using var signedIn = await PostFormAsync(client, "/portal/sign-in", "/portal/sign-in", ("Input.Email", email), ("Input.Password", password));
        Assert.Equal("/portal", PathOf(signedIn));
        return (client, jar);
    }
}
