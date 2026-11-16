using Xunit;

namespace Ehr.Web.Tests.TestCases.UC2;

// Test Case ID: TC-UC2-05
// Purpose / scenario: Off-site, a page takes one network round trip plus under 20 ms of server time at the 99th
//   percentile: the chart, the panel and every kind of search, read from the replica during an outage.
// Type: Non-functional (performance)
// Requirements: NFR-03
// Use case: UC 2, Read records during an on-site outage
// Preconditions: A run of the render benchmark (bench/Ehr.Bench) on a machine doing nothing else: its own PostgreSQL
//   18 with fsync, synchronous_commit and full_page_writes on, seeded with 50,000 synthetic patients; the cloud app
//   in memory, reading as ehr_read with its link to the on-site server down, so no network time is in the
//   measurement; a clinician signed in with 100 patients on their panel.
// Inputs and system conditions: Each off-site row's request, 100 times to warm up and then 1,000 times, one after
//   another: the chart, the panel, the six kinds of search, and the order form.
// Test data: The synthetic clinic of 50,000 patients (bench/Ehr.Seed).
// Steps:
//   1. Run the benchmark with --results, as PerformanceResults.cs says.
//   2. Run this test case against that file.
// Expected result: For every row whose name starts "Off-site", the p99 of server time excluding the commit is under
//   20 ms. Off-site the cloud app records reads in its audit queue, and that queue's write and flush is what counts
//   as the commit. The round trip itself is the network's and isn't measured here. Acceptance criterion: p99
//   excluding the commit < 20 ms, every off-site row.
// Creator: Tahseen Arnan
public class TC_UC2_05_Nfr03_OffSiteNavigation
{
    const double LimitMilliseconds = 20;

    [Fact]
    [Trait("Category", PerformanceResults.Category)]
    public void Off_site_every_page_renders_in_under_20_ms_of_server_time_at_p99()
    {
        var offSite = PerformanceResults.Load().Rows.Where(row => row.Name.StartsWith("Off-site, ", StringComparison.Ordinal)).ToList();

        Assert.Contains(offSite, row => row.Name == "Off-site, Chart, a patient on the panel");
        Assert.Contains(offSite, row => row.Name == "Off-site, Panel, 100 patients");
        Assert.True(offSite.Count >= 8, $"Only {offSite.Count} off-site rows were measured.");
        Assert.All(offSite, row => Assert.True(row.P99ExcludingCommit < LimitMilliseconds,
            $"{row.Name}: p99 {row.P99ExcludingCommit} ms excluding the commit is not under {LimitMilliseconds} ms (NFR-03)."));
    }
}
