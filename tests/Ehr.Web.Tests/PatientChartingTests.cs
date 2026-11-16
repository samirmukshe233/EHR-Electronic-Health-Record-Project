
using Ehr.Data.Audit;
using System.Net;
using Ehr.Data.Accounts;
using Ehr.Data.Patients;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// Phase 4 functional tests for UC 4.
// TC-07: Successful Patient Chart Viewing.
// Requirements: FR-02, FR-30, FR-32.
public class Phase4PatientChartTests(WebApps apps)
    : IClassFixture<WebApps>
{
    static CancellationToken Cancel =>
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task TC07_Authorized_clinician_can_view_patient_chart()
    {
        // Arrange: Create a synthetic patient.
        var family = "Doe" + Guid.NewGuid().ToString("N")[..8];

        var patient = await apps.InsertPatientAsync(
            "John",
            family,
            new DateOnly(1999, 1, 1));

        var mrn = WebApps.MrnOf(patient);

        // Create and authenticate an authorized clinician.
        var clinician = await apps.ClinicianAsync();

        // Assign the patient to the clinician.
        await apps.AssignAsync(clinician, patient);

        // Populate the patient's chart with clinical data.
        await apps.InsertAllergyAsync(
            patient,
            "Penicillin",
            criticality: AllergyCriticality.High,
            reaction: "Hives");

        await apps.InsertMedicationAsync(
            patient,
            "Lisinopril 10 mg tablet",
            dosage: "1 tablet by mouth daily");

        await apps.InsertConditionAsync(
            patient,
            "Essential (primary) hypertension",
            code: "I10",
            onset: new DateOnly(2019, 4, 1));

        var visitDate = DateTimeOffset.UtcNow.AddDays(-3);

        await apps.InsertVisitAsync(
            patient,
            visitDate,
            reason: "Annual physical",
            systolic: 128,
            diastolic: 82,
            heartRate: 72);

        var session = await SignInAsync(
            apps.OnPrem,
            clinician);

        // Act: Search for the patient using identifying info.
        var searchHtml = await session.Client.GetStringAsync(
            $"/patients?name={Uri.EscapeDataString(family)}" +
            $"&birthdate=1999-01-01&mrn={mrn}",
            Cancel);

        // Verify the correct patient appears in search results.
        Assert.Contains($"/patients/{patient}", searchHtml);

        // Record the existing patient-specific audit count.
        var readsBefore = await apps.AuditEventsAsync(
            AuditActions.Read,
            clinician.Id,
            patient);

        // Act: Open the patient's chart.
        var chartHtml = await session.Client.GetStringAsync(
            $"/patients/{patient}",
            Cancel);

        // Assert: Correct patient identity is displayed.
        Assert.Contains($"{family}, John", chartHtml);

        // Verify allergy information.
        Assert.Contains(
            "<strong>Penicillin</strong>: Hives",
            chartHtml);

        Assert.Contains("High risk", chartHtml);

        // Verify active medication information.
        Assert.Contains(
            "<strong>Lisinopril 10 mg tablet</strong>: " +
            "1 tablet by mouth daily",
            chartHtml);

        // Verify diagnosis information.
        Assert.Contains(
            "Essential (primary) hypertension",
            chartHtml);

        Assert.Contains("(I10)", chartHtml);

        // Verify recent visit information.
        Assert.Contains("Annual physical", chartHtml);

        Assert.Contains(
            "blood pressure 128/82 mmHg, heart rate 72/min.",
            chartHtml);

        // Verify chart access generates exactly one audit event.
        var readsAfter = await apps.AuditEventsAsync(
            AuditActions.Read,
            clinician.Id,
            patient);

        Assert.Equal(readsBefore + 1, readsAfter);
    }


    // TC-08: Unauthorized patient chart access.
    // UC 4 alternative flow 1; FR-02, FR-30, FR-32.
    [Fact]
    public async Task TC08_Unassigned_clinician_cannot_directly_view_chart()
    {
        // Arrange: Create a synthetic patient.
        var family = "Restricted" +
            Guid.NewGuid().ToString("N")[..8];

        var patient = await apps.InsertPatientAsync(
            "John",
            family,
            new DateOnly(1999, 1, 1));

        // Create an authenticated clinician.
        var clinician = await apps.ClinicianAsync();

        // Do NOT assign the patient to this clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            clinician);

        // Act: Attempt to open the patient chart directly.
        using var response = await session.Client.GetAsync(
            $"/patients/{patient}",
            Cancel);

        var html = await response.Content.ReadAsStringAsync(
            Cancel);

        // Assert: Protected chart content must not appear
        // without the required authorization.
        Assert.DoesNotContain(
            $"{family}, John",
            html);

        // Inspect the response to determine whether the
        // application denies access or requires a reason.
        Assert.True(
            response.StatusCode ==
                System.Net.HttpStatusCode.Forbidden ||
            response.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized ||
            !response.IsSuccessStatusCode ||
            html.Contains(
                "reason",
                StringComparison.OrdinalIgnoreCase) ||
            html.Contains(
                "access denied",
                StringComparison.OrdinalIgnoreCase),
            "The clinician should be denied direct chart " +
            "access or required to complete an authorization step.");
    }


    // TC-09: Restricted chart - Emergency access offered.
    // UC 4 alternative flow 2; FR-02, FR-30, FR-32.
    [Fact]
    public async Task TC09_Restricted_chart_offers_emergency_access()
    {
        // Arrange: Create a synthetic patient.
        var family = "Restricted" +
            Guid.NewGuid().ToString("N")[..8];

        var patient = await apps.InsertPatientAsync(
            "John",
            family,
            new DateOnly(1999, 1, 1));

        // Create a clinician who is not assigned
        // to the patient.
        var clinician = await apps.ClinicianAsync();

        // Do not call apps.AssignAsync().
        var session = await SignInAsync(
            apps.OnPrem,
            clinician);

        // Act: Attempt to open the patient's chart.
        using var response = await session.Client.GetAsync(
            $"/patients/{patient}",
            Cancel);

        var html = await response.Content.ReadAsStringAsync(
            Cancel);

        // Assert: Normal access is restricted.
        Assert.DoesNotContain(
            $"{family}, John",
            html);

        // Verify that emergency access is offered.
        Assert.Contains(
            "Emergency",
            html,
            StringComparison.OrdinalIgnoreCase);

        // Verify that the page provides an access
        // restriction or reason prompt.
        Assert.True(
            html.Contains(
                "reason",
                StringComparison.OrdinalIgnoreCase) ||
            html.Contains(
                "restricted",
                StringComparison.OrdinalIgnoreCase),
            "The page should explain the access restriction " +
            "or request an emergency access reason.");
    }


    // TC-10: Emergency access requires a reason
    // and records the access for review.
    // UC 4 alternative flow 2; FR-02, FR-30, FR-32.
    [Fact]
    public async Task TC10_Emergency_access_requires_reason_and_is_audited()
    {
        // Arrange: Create a synthetic patient.
        var family = "Emergency" +
            Guid.NewGuid().ToString("N")[..8];

        var patient = await apps.InsertPatientAsync(
            "John",
            family,
            new DateOnly(1999, 1, 1));

        // Create a clinician without normal chart access.
        var clinician = await apps.CreateStaffAsync(
            "Dr. Emergency",
            StaffRoles.Clinician);

        // Do not assign this patient to the clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            clinician);

        const string reason =
            "Patient requires immediate medical attention";

        // Step 1: Attempt to open the restricted chart.
        using var restricted = await Client(
            apps.OnPrem,
            session.Jar,
            followRedirects: false)
            .GetAsync($"/patients/{patient}", Cancel);

        // Assert: Normal access requires justification.
        Assert.Equal(
            HttpStatusCode.Redirect,
            restricted.StatusCode);

        Assert.Equal(
            $"/patients/{patient}/reason",
            new Uri(Base, restricted.Headers.Location!)
                .AbsolutePath);

        // Verify no chart read occurred yet.
        Assert.Equal(
            0,
            await apps.AuditEventsAsync(
                AuditActions.Read,
                actorId: clinician.Id,
                patientId: patient));

        // Step 2: Submit an insufficient justification.
        using var invalid = await PostFormAsync(
            session.Client,
            $"/patients/{patient}/reason",
            $"/patients/{patient}/reason",
            ("Reason", "ER"));

        Assert.Contains(
            "Use at least 10 characters.",
            await ReadAsync(invalid));

        // No emergency access should be granted.
        Assert.Equal(
            0,
            await apps.AuditEventsAsync(
                AuditActions.BreakGlass,
                actorId: clinician.Id,
                patientId: patient));

        // Step 3: Submit the valid emergency reason.
        using var granted = await PostFormAsync(
            session.Client,
            $"/patients/{patient}/reason",
            $"/patients/{patient}/reason",
            ("Reason", reason));

        var chartHtml = await ReadAsync(granted);

        // Assert: The patient chart opens successfully.
        Assert.Equal(
            $"/patients/{patient}",
            PathOf(granted));

        Assert.Contains(
            $"{family}, John",
            chartHtml);

        Assert.Contains(
            "Emergency access",
            chartHtml);

        // Step 4: Verify emergency access is audited.
        Assert.Equal(
            1,
            await apps.AuditEventsAsync(
                AuditActions.BreakGlass,
                actorId: clinician.Id,
                patientId: patient));

        // Verify the exact justification was recorded.
        Assert.Equal(
            [reason],
            await apps.ReasonsAsync(
                AuditActions.BreakGlass,
                clinician.Id,
                patient));

        // Step 5: Verify subsequent chart access works.
        using var again = await Client(
            apps.OnPrem,
            session.Jar,
            followRedirects: false)
            .GetAsync($"/patients/{patient}", Cancel);

        Assert.Equal(
            HttpStatusCode.OK,
            again.StatusCode);

        // Verify both chart views were audited.
        Assert.Equal(
            2,
            await apps.AuditEventsAsync(
                AuditActions.Read,
                actorId: clinician.Id,
                patientId: patient));
    }

}
