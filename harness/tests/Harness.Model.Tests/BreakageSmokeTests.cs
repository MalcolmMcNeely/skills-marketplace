using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Two runs, before the 149. The free tests prove the overlay is the right FILE; only a real run
/// proves the CLI loaded it, and finding that out at run 3 of 149 costs two and a half hours.
/// </summary>
public class BreakageSmokeTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    [LiveFact]
    public async Task The_broken_description_reaches_the_model_and_the_broken_body_reaches_disk()
    {
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));
        var builder = new FixtureBuilder(Paths);
        var ledger = new SpendLedger(2.00m);

        // Layer 3, one run against the broken description. Expected to MISS, but the smoke test is
        // agnostic: what it proves is that a run completes and scores against the overlaid catalogue.
        var catalogue = builder.Build(Paths.StubCatalogue, Paths.BreakOverlay("description-catalogue"));
        var positive = suite.Firing.ShouldFire[0];
        var firing = Scoring.ScoreFiring(
            await new FiringRunner(Paths, catalogue).RunAsync(positive.Prompt, CaseKind.ShouldFire), positive.Expect);
        ledger.Record(firing);
        output.WriteLine($"layer 3 / description-catalogue  {firing.Verdict,-9} {firing.Detail}  {firing.Cost}");

        // Layer 4, one run against the broken body. This one has a definite expectation: the fixture
        // must still LOAD, or every run in the real pass voids on the same precondition.
        var plugin = builder.Build(Paths.GoodPlugin, Paths.BreakOverlay("body-plugin"));
        var c = suite.Contract[0];
        var contract = Scoring.ScoreContract(
            await new ContractRunner(Paths).RunAsync(suite.SkillUnderTest, c.Task, plugin),
            suite.SkillUnderTest, AssertionCatalogue.Resolve(c));
        ledger.Record(contract);
        output.WriteLine($"layer 4 / body-plugin            {contract.Verdict,-9} {contract.Detail}  {contract.Cost}");
        foreach (var a in contract.Assertions)
            output.WriteLine($"    A{a.Number} {(a.Passed ? "pass" : "FAIL")} [{a.Kind}/{a.Evidence}] {a.Description} :: {a.Detail}");

        output.WriteLine(ledger.Report());

        Assert.NotEqual(Verdict.Void, firing.Verdict);
        Assert.NotEqual(Verdict.Void, contract.Verdict);
        Assert.NotEmpty(contract.Assertions);
    }
}
