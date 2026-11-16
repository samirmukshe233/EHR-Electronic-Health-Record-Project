using Xunit;

namespace Ehr.Web.Tests.TestCases.UC1;

// Test Case ID: TC-UC1-05
// Purpose / scenario: On-site, opening a chart with its active orders, and opening the order form, each take under
//   15 ms of server time at the 99th percentile.
// Type: Non-functional (performance)
// Requirements: NFR-01
// Use case: UC 1, Place an order
// Preconditions: A run of the render benchmark (bench/Ehr.Bench) on a machine doing nothing else: its own PostgreSQL
//   18 with fsync, synchronous_commit and full_page_writes on, seeded with 50,000 synthetic patients; the on-site app
//   in memory, so no network time is in the measurement; a clinician signed in with 100 patients on their panel.
// Inputs and system conditions: GET /patients/{id} and GET /patients/{id}/orders/new?type=lab, each 100 times to
//   warm up and then 1,000 times, one after another, for patients on the clinician's panel.
// Test data: The synthetic clinic of 50,000 patients (bench/Ehr.Seed). Each of the 100 panel patients has four
//   active orders, one of each type, signed by the benchmark's clinician.
// Steps:
//   1. Run the benchmark with --results, as PerformanceResults.cs says.
//   2. Run this test case against that file.
// Expected result: The p99 of the whole request, the commit of its audit event included, is under 15 ms for the row
//   "Chart, a patient on the panel" and for the row "Order form, a patient on the panel". Acceptance criterion:
//   p99 < 15 ms, each.
// Creator: Tahseen Arnan
public class TC_UC1_05_Nfr01_OnSiteNavigation
{
    const double LimitMilliseconds = 15;

    [Theory]
    [Trait("Category", PerformanceResults.Category)]
    [InlineData("Chart, a patient on the panel")]
    [InlineData("Order form, a patient on the panel")]
    public void On_site_the_chart_with_active_orders_and_the_order_form_render_in_under_15_ms_at_p99(string path)
    {
        var row = PerformanceResults.Load().Row(path);

        Assert.True(row.P99 < LimitMilliseconds, $"{path}: p99 {row.P99} ms, the commit included, is not under {LimitMilliseconds} ms (NFR-01).");
    }
}
