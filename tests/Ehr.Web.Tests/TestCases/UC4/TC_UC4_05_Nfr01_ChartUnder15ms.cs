using Xunit;

namespace Ehr.Web.Tests.TestCases.UC4;

// Test Case ID: TC-UC4-05
// Purpose / scenario: Chart under 15 ms with 50,000 patients. On-site, opening the chart of a patient on the
//   clinician's panel takes under 15 ms of server time at the 99th percentile, in a clinic of 50,000 patients.
// Type: Non-functional (NFR-01, NFR-04)
// Requirements: NFR-01, NFR-04
// Use case: UC 4, View a patient's chart
// Preconditions: A run of the render benchmark (bench/Ehr.Bench) on a machine doing nothing else: its own PostgreSQL
//   18 with fsync, synchronous_commit and full_page_writes on, seeded with 50,000 synthetic patients; the on-site app
//   in memory, so no network time is in the measurement; a clinician signed in with 100 patients on their panel.
// Inputs and system conditions: GET /patients/{id}, 100 times to warm up and then 1,000 times, one after another, for
//   patients picked at random from the clinician's panel.
// Test data: The synthetic clinic of 50,000 patients (bench/Ehr.Seed). Each of the 100 panel patients has four
//   active orders, one of each type, signed by the benchmark's clinician.
// Steps:
//   1. Run the benchmark with --results, as PerformanceResults.cs says.
//   2. Run this test case against that file.
// Expected result: The p99 of the whole request, the commit of its audit event included, is under 15 ms for the row
//   "Chart, a patient on the panel". Acceptance criterion: p99 < 15 ms, at 50,000 patients (NFR-04's scale). The same
//   row is also checked by TC-UC1-05, for the chart with its active orders.
// Creator: Diego Valenzuela
public class TC_UC4_05_Nfr01_ChartUnder15ms
{
    const double LimitMilliseconds = 15;

    [Theory]
    [Trait("Category", PerformanceResults.Category)]
    [InlineData("Chart, a patient on the panel")]
    public void On_site_a_chart_on_the_panel_renders_in_under_15_ms_at_p99_with_50000_patients(string path)
    {
        var row = PerformanceResults.Load().Row(path);

        Assert.True(row.P99 < LimitMilliseconds, $"{path}: p99 {row.P99} ms, the commit included, is not under {LimitMilliseconds} ms (NFR-01).");
    }
}
