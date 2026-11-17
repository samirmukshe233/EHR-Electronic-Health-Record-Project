using Xunit;

namespace Ehr.Web.Tests.TestCases.UC6;

// Test Case ID: TC-UC6-05
// Purpose / scenario: On-site, a patient's own test results page takes under 15 ms of server time at the 99th
//   percentile.
// Type: Non-functional (performance)
// Requirements: NFR-01, NFR-04
// Use case: UC 6, View test results in the portal
// Preconditions: A run of the render benchmark (bench/Ehr.Bench) on a machine doing nothing else: its own PostgreSQL
//   18 with fsync, synchronous_commit and full_page_writes on, seeded with 50,000 synthetic patients; the on-site app
//   in memory, so no network time is in the measurement; a patient signed in at the portal.
// Inputs and system conditions: GET /portal/results, 100 times to warm up and then 1,000 times, one after another, as
//   the signed-in patient.
// Test data: The synthetic clinic of 50,000 patients (bench/Ehr.Seed). The patient is the last one in the clinic with
//   at least three released results, with a portal account the benchmark creates and locks afterwards.
// Steps:
//   1. Run the benchmark with --results, as TestCases/PerformanceResults.cs says.
//   2. Run this test case against that file.
// Expected result: For the row "Portal results, the patient's own", the p99 of the whole request, the commit of its
//   audit event included, is under 15 ms. This is NFR-01's target, not the 20 ms gate CI applies to the p99 excluding
//   the commit. Acceptance criterion: p99, commit included, < 15 ms.
// Creator: Simar Rekhi
public class TC_UC6_05_Nfr01_OnSiteNavigation
{
    const double LimitMilliseconds = 15;

    [Fact]
    [Trait("Category", PerformanceResults.Category)]
    public void On_site_the_patients_results_page_renders_in_under_15_ms_at_p99()
    {
        var row = PerformanceResults.Load().Row("Portal results, the patient's own");

        Assert.True(row.P99 < LimitMilliseconds, $"{row.Name}: p99 {row.P99} ms, the commit included, is not under {LimitMilliseconds} ms (NFR-01).");
    }
}
