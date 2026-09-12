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

        // #30. A suite takes part in #6's differential by declaring the overlays its arms name. One
        // that declares none has nothing to smoke out, and saying so is cheaper than a thrown path.
        if (!found.DeclaresBreaks)
        {
            output.WriteLine($"{suiteName}: declares no break overlays, so there is nothing to smoke out");
            return;
        }

        // The two halves are declared separately and smoked separately. Skipping the firing half
        // because a suite wrote no contract case would leave its overlays unchecked for a reason
        // that has nothing to do with them.
        var ran = 0;

        if (suite.Firing.ShouldFire.Count > 0)
        {
            // Layer 3, one run against the broken description. Expected to MISS, but the smoke test is
            // agnostic: what it proves is that a run completes and scores against the overlaid catalogue.
            var catalogue = builder.Build(Paths.StubCatalogue, found.BreakOverlay("description/catalogue"));
            var positive = suite.Firing.ShouldFire[0];
            var firing = Scoring.ScoreFiring(
                await new FiringRunner(Paths, found, catalogue).RunAsync(positive.Prompt, CaseKind.ShouldFire), positive.Expect);
            ledger.Record(firing);
            output.WriteLine($"{suiteName} layer 3 / description-catalogue  {firing.Verdict,-9} {firing.Detail}  {firing.Cost}");
            ran++;

            Assert.NotEqual(Verdict.Void, firing.Verdict);
        }
        else output.WriteLine($"{suiteName}: declares no should-fire case, so layer 3 has nothing to smoke out");

        if (suite.Contract.Count > 0)
        {
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
            ran++;

            Assert.NotEqual(Verdict.Void, contract.Verdict);
            Assert.NotEmpty(contract.Assertions);
        }
        else output.WriteLine($"{suiteName}: declares no contract case, so layer 4 has nothing to smoke out");

        output.WriteLine(ledger.Report());

        // A suite that declared overlays and then smoked neither half has overlays nothing loads,
        // which is the material this test exists to catch before the pass pays for it.
        Assert.True(ran > 0, $"{suiteName} declares break overlays but no case either layer can run against them");
    }
}
