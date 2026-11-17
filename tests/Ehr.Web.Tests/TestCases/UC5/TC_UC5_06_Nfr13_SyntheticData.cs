using System.Text.RegularExpressions;
using Ehr.Seed;
using Ehr.Testing;
using Xunit;

namespace Ehr.Web.Tests.TestCases.UC5;

// Test Case ID: TC-UC5-06
// Purpose / scenario: Registration testing uses only synthetic data. The portal registration tests register only
//   addresses at reserved test domains, and the example portal account the local stack registers belongs to a
//   synthetic patient of the synthetic clinic.
// Type: Non-functional (NFR-13)
// Requirements: NFR-13, FR-15
// Use case: UC 5, Register for the patient portal
// Preconditions: The repository as committed. No database is needed.
// Inputs and system conditions: The registration tests (PortalTests.cs, PortalResendConfirmationTests.cs and
//   PortalPasswordResetTests.cs), the test fixture's address generator (WebApps.NewEmail), the local stack's example
//   accounts (infra/compose/test-accounts.cs), and the synthetic clinic (bench/Ehr.Seed).
// Test data: The example portal account patient@example.com, registered for MRN SYN0000001, and the first patient of
//   the synthetic clinic.
// Steps:
//   1. In each registration test file, collect every whole email address written in it, and check that it gets the
//      addresses it registers from the fixture.
//   2. Make an address with the test fixture.
//   3. Read the example portal account's address and MRN from test-accounts.cs.
//   4. Make the synthetic clinic's patient with that MRN.
// Expected result: In step 1 every address found is at example.test or example.com, domains reserved for tests and
//   examples, and each file calls WebApps.NewEmail. Step 2's address is at example.test. In step 3 the address is at
//   example.com and the MRN starts SYN. Step 4's patient has that MRN, and given and family names that carry a number,
//   as no real name does. Acceptance criterion: no address outside a reserved domain, no MRN outside the synthetic
//   clinic's.
// Creator: Tahseen Arnan
public partial class TC_UC5_06_Nfr13_SyntheticData
{
    [Theory]
    [InlineData("tests/Ehr.Web.Tests/PortalTests.cs")]
    [InlineData("tests/Ehr.Web.Tests/PortalResendConfirmationTests.cs")]
    [InlineData("tests/Ehr.Web.Tests/PortalPasswordResetTests.cs")]
    public void The_registration_tests_register_only_addresses_at_reserved_domains(string file)
    {
        var text = File.ReadAllText(RepositoryRoot.Combine(file));

        Assert.Contains("WebApps.NewEmail()", text);
        Assert.All(EmailAddress().Matches(text).Select(m => m.Value).Distinct(),
            address => Assert.True(address.EndsWith("@example.test", StringComparison.Ordinal) || address.EndsWith("@example.com", StringComparison.Ordinal),
                $"{file} writes {address}, which isn't at a reserved test domain."));
    }

    [Fact]
    public void The_fixture_registers_addresses_at_a_reserved_test_domain() =>
        Assert.EndsWith("@example.test", WebApps.NewEmail());

    [Fact]
    public void The_example_portal_account_is_registered_for_a_synthetic_patient()
    {
        var text = File.ReadAllText(RepositoryRoot.Combine("infra/compose/test-accounts.cs"));

        var email = Assert.Single(PatientEmail().Matches(text)).Groups[1].Value;
        var mrn = Assert.Single(PatientMrn().Matches(text)).Groups[1].Value;
        Assert.EndsWith("@example.com", email);
        Assert.StartsWith("SYN", mrn);

        var patient = SyntheticClinic.Chart(0).Patient;
        Assert.Equal(mrn, patient.Mrn);
        Assert.Matches("[0-9]", patient.GivenName);
        Assert.Matches("[0-9]", patient.FamilyName);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"PatientEmail = ""([^""]+)""")]
    private static partial Regex PatientEmail();

    [GeneratedRegex(@"PatientMrn = ""([^""]+)""")]
    private static partial Regex PatientMrn();
}
