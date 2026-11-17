using Xunit;

namespace Ehr.Web.Tests.TestCases.UC3;

// Test Case ID: TC-UC3-05
// Purpose / scenario: Search under 15 ms with 50,000 patients. On-site, every kind of patient search the benchmark
//   makes takes under 15 ms of server time at the 99th percentile, in a clinic of 50,000 patients.
// Type: Non-functional (NFR-01, NFR-04)
// Requirements: NFR-01, NFR-04
// Use case: UC 3, Search for a patient
// Preconditions: A run of the render benchmark (bench/Ehr.Bench) on a machine doing nothing else: its own PostgreSQL
//   18 with fsync, synchronous_commit and full_page_writes on, seeded with 50,000 synthetic patients; the on-site app
//   in memory, so no network time is in the measurement; a clinician signed in with 100 patients on their panel.
// Inputs and system conditions: GET /patients with each search the benchmark makes (bench/Ehr.Bench/RenderBenchmark.cs,
//   MeasureEachAsync): the first 3 letters of a name, a whole family name, a date of birth, an MRN, a name and date of
//   birth together, and the as-you-type results for the first 3 letters. Each 100 times to warm up and then 1,000
//   times, one after another, for patients picked at random from the synthetic clinic.
// Test data: The synthetic clinic of 50,000 patients (bench/Ehr.Seed).
// Steps:
//   1. Run the benchmark with --results, as PerformanceResults.cs says.
//   2. Run this test case against that file.
// Expected result: For each search row, the p99 of the whole request, the commit of its audit events included, is
//   under 15 ms. Acceptance criterion: p99 < 15 ms, each, at 50,000 patients (NFR-04's scale).
// Creator: Diego Valenzuela
public class TC_UC3_05_Nfr01_SearchUnder15ms
{
    const double LimitMilliseconds = 15;

    [Theory]
    [Trait("Category", PerformanceResults.Category)]
    [InlineData("Search, first 3 letters of a name")]
    [InlineData("Search, a whole family name")]
    [InlineData("Search, date of birth")]
    [InlineData("Search, MRN")]
    [InlineData("Search, name and date of birth")]
    [InlineData("As you type, first 3 letters")]
    public void On_site_each_patient_search_renders_in_under_15_ms_at_p99_with_50000_patients(string path)
    {
        var row = PerformanceResults.Load().Row(path);

        Assert.True(row.P99 < LimitMilliseconds, $"{path}: p99 {row.P99} ms, the commit included, is not under {LimitMilliseconds} ms (NFR-01).");
    }
}
