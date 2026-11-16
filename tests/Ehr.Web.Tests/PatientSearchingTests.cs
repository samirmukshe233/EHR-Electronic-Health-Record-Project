
using System.Text.RegularExpressions;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// Phase 4 acceptance tests for UC 3.
// TC-01: Successful patient search.
// Requirement: FR-01.
public partial class Phase4PatientSearchTests(WebApps apps)
    : IClassFixture<WebApps>
{
    static CancellationToken Cancel =>
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task TC01_Search_with_valid_patient_information()
    {
        // Arrange: Create a unique synthetic patient.
        var family = "Doe" + Guid.NewGuid().ToString("N")[..8];

        var patient = await apps.InsertPatientAsync(
            "John",
            family,
            new DateOnly(1999, 1, 1));

        var mrn = WebApps.MrnOf(patient);

        // Create an unrelated patient.
        var unrelated = await apps.InsertPatientAsync(
            "Jane",
            family,
            new DateOnly(1995, 5, 15));

        // Sign in as an authorized clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            await apps.ClinicianAsync());

        // Act: Search using name, DOB, and MRN.
        var html = await session.Client.GetStringAsync(
            $"/patients?name={Uri.EscapeDataString(family)}" +
            $"&birthdate=1999-01-01&mrn={mrn}",
            Cancel);

        // Assert: Search results contain exactly John.
        Assert.Contains("Search results", html);
        Assert.Equal([patient], Listed(html));

        // Verify the unrelated patient is excluded.
        Assert.DoesNotContain($"/patients/{unrelated}", html);
    }

    // TC-02: Invalid search criteria.
    // UC 3 alternative flow 1; FR-01.
    [Fact]
    public async Task TC02_Invalid_date_of_birth_is_rejected()
    {
        // Arrange: Sign in as an authorized clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            await apps.ClinicianAsync());

        // Use the invalid DOB specified in TC-02.
        var invalidDob = "13/45/1999";

        // Act: Attempt a patient search.
        var html = await session.Client.GetStringAsync(
            $"/patients?name={Uri.EscapeDataString("John Doe")}" +
            $"&birthdate={Uri.EscapeDataString(invalidDob)}" +
            $"&mrn=123456789",
            Cancel);

        // Assert: The system displays a validation message.
        Assert.Contains(
            "Enter the date of birth as a full date",
            html);

        // No patient search results should be displayed.
        Assert.DoesNotContain("Search results", html);

        // No patient chart links should be returned.
        Assert.Empty(Listed(html));
    }


    // TC-03: Incomplete search criteria.
    // UC 3 alternative flow 1; FR-01.
    [Fact]
    public async Task TC03_Empty_search_criteria_are_rejected()
    {
        // Arrange: Sign in as an authorized clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            await apps.ClinicianAsync());

        // Act: Submit a search with all fields empty.
        var html = await session.Client.GetStringAsync(
            "/patients?name=&birthdate=&mrn=",
            Cancel);

        // Assert: The system should prompt the clinician
        // to provide identifying information.
        Assert.Contains(
            "Enter a name, date of birth, or MRN",
            html,
            StringComparison.OrdinalIgnoreCase);

        // No search results should be displayed.
        Assert.DoesNotContain("Search results", html);

        // No patient chart links should be returned.
        Assert.Empty(Listed(html));
    }


    // TC-04: No patients match the search criteria.
    // UC 3 alternative flow 2; FR-01.
    [Fact]
    public async Task TC04_No_matching_patients_displays_message()
    {
        // Arrange: Sign in as an authorized clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            await apps.ClinicianAsync());

        // Use a unique name to ensure no patient matches.
        var nonexistentName =
            "Smith" + Guid.NewGuid().ToString("N")[..8];

        // Act: Search using valid identifying information
        // that does not match any existing patient.
        var html = await session.Client.GetStringAsync(
            $"/patients?name={Uri.EscapeDataString(nonexistentName)}" +
            $"&birthdate=1995-05-15" +
            $"&mrn=999999999",
            Cancel);

        // Assert: The system displays a no-match message.
        Assert.Contains("No patients match.", html);

        // No patient records should be listed.
        Assert.Empty(Listed(html));
    }




    // Extract patient IDs from chart links.
    static List<Guid> Listed(string html) =>
        [.. ChartLink().Matches(html)
            .Select(match =>
                Guid.Parse(match.Groups[1].Value))];

    [GeneratedRegex("href=\"/patients/([0-9a-f-]{36})")]
    private static partial Regex ChartLink();
}
