using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Two runs a suite, before the pass. The free tests prove the overlay is the right FILE; only a real
/// run proves the CLI loaded it, and finding that out at run 3 of 256 costs over three hours.
///
/// #27. One case per discovered suite, because a new suite's overlays are exactly the material this
/// is here to smoke out, and a smoke test that only ever checked the first skill would send the next
/// one into the pass unchecked.
/// </summary>
public class BreakageSmokeTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    [LiveTheory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public async Task The_broken_description_reaches_the_model_and_the_broken_body_reaches_disk(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var suite = found.Suite;
        var builder = new FixtureBuilder(Paths);
        var ledger = new SpendLedger(2.00m);

        // Layer 3, one run against the broken description. Expected to MISS, but the smoke test is
        // agnostic: what it proves is that a run completes and scores against the overlaid catalogue.
        var catalogue = builder.Build(Paths.StubCatalogue, found.BreakOverlay("description/catalogue"));
        var positive = suite.Firing.ShouldFire[0];
        var firing = Scoring.ScoreFiring(
            await new FiringRunner(Paths, catalogue).RunAsync(positive.Prompt, CaseKind.ShouldFire), positive.Expect);
        ledger.Record(firing);
        output.WriteLine($"{suiteName} layer 3 / description-catalogue  {firing.Verdict,-9} {firing.Detail}  {firing.Cost}");

        // Layer 4, one run against the broken body. This one has a definite expectation: the fixture
        // must still LOAD, or every run in the real pass voids on the same precondition.
        var plugin = builder.Build(found.Plugin, found.BreakOverlay("body/plugin"));
        var c = suite.Contract[0];
        var contract = Scoring.ScoreContract(
            await new ContractRunner(Paths).RunAsync(suite.SkillUnderTest, c.Task, plugin),
            suite.SkillUnderTest, AssertionCatalogue.Resolve(c));
        ledger.Record(contract);
        output.WriteLine($"{suiteName} layer 4 / body-plugin            {contract.Verdict,-9} {contract.Detail}  {contract.Cost}");
        foreach (var a in contract.Assertions)
            output.WriteLine($"    A{a.Number} {(a.Passed ? "pass" : "FAIL")} [{a.Kind}/{a.Evidence}] {a.Description} :: {a.Detail}");

        output.WriteLine(ledger.Report());

        Assert.NotEqual(Verdict.Void, firing.Verdict);
        Assert.NotEqual(Verdict.Void, contract.Verdict);
        Assert.NotEmpty(contract.Assertions);
    }
}
