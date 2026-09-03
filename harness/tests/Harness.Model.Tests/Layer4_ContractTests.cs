using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>Layer 4: does the BODY obey itself? Invoked by name, so firing cannot miss.</summary>
public class Layer4_ContractTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>Suite-wide hard stop. Ten void runs cost $2.28 on this ticket before the resample cap fired.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 5.00m;

    [LiveFact]
    public async Task The_good_fixture_holds_its_contract()
    {
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));
        var c = suite.Contract[0];
        var assertions = AssertionCatalogue.Resolve(c);
        var runner = new ContractRunner(Paths);

        var runs = int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : c.Runs;
        var ledger = new SpendLedger(Ceiling);
        var sample = await Resampler.CollectAsync(runs, c.Cap,
            async ct => Scoring.ScoreContract(await runner.RunAsync(suite.SkillUnderTest, c.Task, Paths.GoodPlugin, ct),
                suite.SkillUnderTest, assertions), ledger);

        foreach (var s in sample.Scores)
        {
            output.WriteLine($"{s.Verdict,-7} {s.Detail}  ${s.CostUsd:0.000}");
            foreach (var a in s.Assertions)
                output.WriteLine($"    A{a.Number} {(a.Passed ? "pass" : "FAIL")} [{a.Kind}/{a.Evidence}] {a.Description} :: {a.Detail}");
        }

        Assert.Null(sample.Failure);
        Assert.DoesNotContain(sample.Scores, s => s.Verdict == Verdict.Broken);
    }
}
