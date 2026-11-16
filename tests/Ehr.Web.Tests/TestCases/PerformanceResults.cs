using System.Text.Json;
using Xunit;

namespace Ehr.Web.Tests.TestCases;

// The performance test cases' measurements (Tahseen Arnan). A timing assertion can't run in the normal test run: the
// tests' databases have durability off, and shared CI runners' timings are unsteady. So each performance test case
// reads the results of one run of the render benchmark (bench/Ehr.Bench, with --results), which starts its own
// PostgreSQL with durability on and 50,000 synthetic patients, and checks its own path's p99 against its own
// requirement's limit. Without a results file the test case is skipped, and says how to run it:
//
//   dotnet run -c Release --project bench/Ehr.Bench -- --assert --results bench-results.json
//   EHR_BENCH_RESULTS=<full path to bench-results.json> dotnet test --project tests/Ehr.Web.Tests -c Release --filter-trait "Category=Performance"
//
// (In PowerShell: $env:EHR_BENCH_RESULTS = "<full path>" first.) Run the benchmark with nothing else running.
public sealed record PerformanceResults(int Patients, bool DurabilityOn, int Measured, IReadOnlyList<PerformanceRow> Rows)
{
    public const string Variable = "EHR_BENCH_RESULTS";

    public const string Category = "Performance";

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // The run to check, or the test case is skipped. The run must be the one the requirements are stated for.
    public static PerformanceResults Load()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrEmpty(path),
            $"Not run: a performance test case checks a run of the render benchmark. Run it with --results, then set {Variable} to that file (see PerformanceResults.cs).");
        var results = JsonSerializer.Deserialize<PerformanceResults>(File.ReadAllText(path!), Json)!;
        Assert.True(results.DurabilityOn, "The benchmark ran with durability off, so its commits didn't wait for the disk.");
        Assert.Equal(50_000, results.Patients);
        Assert.Equal(1_000, results.Measured);
        return results;
    }

    public PerformanceRow Row(string name) => Assert.Single(Rows, row => row.Name == name);
}

// One kind of request: milliseconds of server time, for the whole request (the commit included) and excluding the commit.
public sealed record PerformanceRow(string Name, double P50, double P99, double P50ExcludingCommit, double P99ExcludingCommit);
