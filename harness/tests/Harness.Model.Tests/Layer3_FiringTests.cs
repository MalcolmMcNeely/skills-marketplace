using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>Layer 3: does the DESCRIPTION fire? Stub catalogue, natural prompt, killed at the first tool call.</summary>
public class Layer3_FiringTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    [LiveFact]
    public async Task One_positive_case_end_to_end()
    {
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));
        var c = suite.Firing.ShouldFire[0];
        var runner = new FiringRunner(Paths);

        var runs = int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : c.Runs;
        var sample = await Resampler.CollectAsync(runs, c.Cap, async ct =>
            Scoring.ScoreFiring(await runner.RunAsync(c.Prompt, ct), c.Expect));

        foreach (var s in sample.Scores)
            output.WriteLine($"{s.Verdict,-9} {s.Detail}  ${s.CostUsd:0.000}");

        Assert.Null(sample.Failure);
        var pool = Pooling.Pool([new CaseResult(c.Id, sample.Scores)], suite.PGood);
        output.WriteLine($"pooled {pool.PooledPassed}/{pool.PooledValid} gate>={pool.GateK} total ${pool.TotalCostUsd:0.00}");
    }
}
