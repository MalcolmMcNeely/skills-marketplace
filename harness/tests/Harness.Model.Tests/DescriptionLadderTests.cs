using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #6, the screen before the pass. The first description break did not break anything: it
/// dropped the "Use when" trigger but kept the words the prompt used, and the skill fired anyway.
///
/// So the break itself needs choosing, and a 60-run pass is the wrong instrument to choose it with.
/// Three runs on one prompt separates "fires every time" from "never fires" for about $2, and the
/// candidate that goes quiet is the one worth an hour.
///
/// #27. Every discovered suite, and the candidates each one DECLARES under breaks/candidates/ rather
/// than a list typed in here. A suite with none to screen says so and costs nothing. One test that
/// loops rather than a theory, because the ledger is shared across every suite it screens.
///
/// Nine runs a suite, roughly ten minutes. Its own lock, because it still costs money.
/// </summary>
public class DescriptionLadderTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>The break group a candidate sits in. The suite decides what is in it.</summary>
    private const string Candidates = "candidates";

    private static int Runs =>
        int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : 3;

    [LadderFact]
    public async Task Screen_the_description_break_candidates()
    {
        var builder = new FixtureBuilder(Paths);
        var ledger = new SpendLedger(8.00m);
        var results = new List<(string Suite, string Candidate, int Fired, int Valid, SpendTotal Cost)>();

        output.WriteLine($"{RunEnvironment.Current}");

        foreach (var found in SuitesUnderTest.All(Paths))
        {
            var candidates = found.BreakOverlaysIn(Candidates);
            if (candidates.Count == 0 || found.Suite.Firing.ShouldFire.Count == 0)
            {
                output.WriteLine($"{found.Name}: {candidates.Count} candidate(s) and {found.Suite.Firing.ShouldFire.Count} should-fire case(s), so there is nothing to screen");
                continue;
            }

            var probe = found.Suite.Firing.ShouldFire[0];
            output.WriteLine($"{found.Name}, prompt {probe.Id}: {probe.Prompt}");

            foreach (var candidate in candidates)
            {
                var overlay = found.BreakOverlay(candidate);
                var catalogue = builder.Build(Paths.StubCatalogue, overlay);
                var runner = new FiringRunner(Paths, catalogue);

                var sample = await Resampler.CollectAsync(Runs, Runs * 2,
                    async ct => Scoring.ScoreFiring(await runner.RunAsync(probe.Prompt, CaseKind.ShouldFire, ct), probe.Expect), ledger);

                var valid = sample.Scores.Where(s => s.Verdict != Verdict.Void).ToList();
                var fired = valid.Count(s => s.Verdict == Verdict.Held);
                results.Add((found.Name, candidate, fired, valid.Count, SpendTotal.Of(sample.Scores.Select(s => s.Cost))));

                var skillFile = Path.Combine(overlay, "skills", found.Suite.SkillUnderTest, "SKILL.md");
                output.WriteLine($"{candidate,-30} fired {fired}/{valid.Count}  {FixtureBuilder.DescriptionOf(skillFile)}");
                foreach (var s in sample.Scores) output.WriteLine($"    {s.Verdict,-9} {s.Detail}  {s.Cost}");
            }
        }

        output.WriteLine("");
        output.WriteLine("| Suite | Candidate | Fired | Cost |");
        output.WriteLine("|---|---|---|---|");
        foreach (var (suite, candidate, fired, valid, cost) in results)
            output.WriteLine($"| {suite} | {candidate} | {fired}/{valid} | {cost} |");
        output.WriteLine(ledger.Report());

        // A screen, not a gate. Every candidate firing every time is the finding that a description
        // break cannot be made to bite, and it has to reach the report rather than die here.
        Assert.All(results, r => Assert.True(r.Valid > 0, $"{r.Suite}/{r.Candidate}: no valid runs"));
    }
}

public sealed class LadderFactAttribute : Xunit.FactAttribute
{
    public LadderFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls; set SKILL_HARNESS_LIVE=1";
        else if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LADDER") != "1")
            Skip = "nine runs a suite and about ten minutes; set SKILL_HARNESS_LADDER=1";
    }
}
