using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Layer 3: does the DESCRIPTION fire? Stub catalogue, natural prompt, killed at the first tool call.
///
/// #27. One case per discovered suite, not one case for one named suite. A skill folder landing in
/// skills/ widens this layer by being there, and a run over no suites at all fails rather than
/// passes, because xUnit refuses a theory that finds no data.
/// </summary>
public class Layer3_FiringTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>Suite-wide hard stop. Ten void runs cost $2.28 on this ticket before the resample cap fired.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 5.00m;

    [LiveTheory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public async Task One_positive_case_end_to_end(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var suite = found.Suite;

        // A suite may be graded on its body alone, and discovery allows one. No should-fire case is a
        // fact that suite declared, not this layer finding nothing: the free gate is what holds the
        // layer to every suite, and it cannot hold a suite to cases it never wrote.
        if (suite.Firing.ShouldFire.Count == 0)
        {
            output.WriteLine($"{suiteName}: declares no should-fire case, so layer 3 has nothing to run");
            return;
        }

        var c = suite.Firing.ShouldFire[0];
        var runner = new FiringRunner(Paths, found);

        var runs = int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : c.Runs;
        var ledger = new SpendLedger(Ceiling);
        var sample = await Resampler.CollectAsync(runs, c.Cap,
            async ct => Scoring.ScoreFiring(await runner.RunAsync(c.Prompt, CaseKind.ShouldFire, ct), c.Expect), ledger);

        foreach (var s in sample.Scores)
            output.WriteLine($"{s.Verdict,-9} {s.Detail}  {s.Cost}");

        Assert.Null(sample.Failure);
        var pool = Pooling.Pool([new CaseResult(c.Id, sample.Scores)], suite.PGood);
        output.WriteLine($"{suiteName} pooled {pool.PooledPassed}/{pool.PooledValid} gate>={pool.GateK} total {pool.Cost}");
    }
}
