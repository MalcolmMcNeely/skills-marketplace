using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Layer 4: does the BODY obey itself? Invoked by name, so firing cannot miss.
///
/// #27. One case per discovered suite, for the same reason layer 3 is: a skill folder widens this
/// layer by being there, and a run over no suites fails rather than passes.
/// </summary>
public class Layer4_ContractTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>Suite-wide hard stop. Ten void runs cost $2.28 on this ticket before the resample cap fired.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 5.00m;

    [LiveTheory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public async Task The_good_fixture_holds_its_contract(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var suite = found.Suite;

        // A suite may be graded on its description alone. Nothing for layer 4 to run is that suite's
        // declaration, not this layer quietly narrowing to the suites it likes.
        if (suite.Contract.Count == 0)
        {
            output.WriteLine($"{suiteName}: declares no contract case, so layer 4 has nothing to run");
            return;
        }

        var c = suite.Contract[0];
        var assertions = AssertionCatalogue.Resolve(c);
        var runner = new ContractRunner(Paths);

        var runs = int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : c.Runs;
        var ledger = new SpendLedger(Ceiling);
        var sample = await Resampler.CollectAsync(runs, c.Cap,
            async ct => Scoring.ScoreContract(await runner.RunAsync(suite.SkillUnderTest, c.Task, found.Plugin, ct),
                suite.SkillUnderTest, assertions), ledger);

        foreach (var s in sample.Scores)
        {
            output.WriteLine($"{s.Verdict,-7} {s.Detail}  {s.Cost}");
            foreach (var a in s.Assertions)
                output.WriteLine($"    A{a.Number} {(a.Passed ? "pass" : "FAIL")} [{a.Kind}/{a.Evidence}] {a.Description} :: {a.Detail}");
        }

        Assert.Null(sample.Failure);
        Assert.DoesNotContain(sample.Scores, s => s.Verdict == Verdict.Broken);
    }
}
