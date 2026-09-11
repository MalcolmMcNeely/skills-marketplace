using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #6, the screen before the pass. The first description break did not break anything: it
/// dropped the "Use when" trigger but kept the words "C# class", and the skill fired anyway.
///
/// So the break itself needs choosing, and a 60-run pass is the wrong instrument to choose it with.
/// Three runs on one prompt separates "fires every time" from "never fires" for about $2, and the
/// candidate that goes quiet is the one worth an hour.
///
/// Nine runs, roughly ten minutes. Its own lock, because it still costs money.
/// </summary>
public class DescriptionLadderTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    private static readonly string[] Candidates = ["vague-label", "boundary-inverted", "wrong-subject"];

    private static int Runs =>
        int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_RUNS"), out var n) ? n : 3;

    [LadderFact]
    public async Task Screen_the_description_break_candidates()
    {
        var suite = Found.Suite;
        var builder = new FixtureBuilder(Paths);
        var ledger = new SpendLedger(8.00m);
        var probe = suite.Firing.ShouldFire[0];

        output.WriteLine($"{RunEnvironment.Current}, prompt {probe.Id}: {probe.Prompt}");
        output.WriteLine("");

        var results = new List<(string Candidate, int Fired, int Valid, SpendTotal Cost)>();

        foreach (var candidate in Candidates)
        {
            var overlay = Found.BreakOverlay($"candidates/{candidate}");
            var catalogue = builder.Build(Paths.StubCatalogue, overlay);
            var runner = new FiringRunner(Paths, catalogue);

            var sample = await Resampler.CollectAsync(Runs, Runs * 2,
                async ct => Scoring.ScoreFiring(await runner.RunAsync(probe.Prompt, CaseKind.ShouldFire, ct), probe.Expect), ledger);

            var valid = sample.Scores.Where(s => s.Verdict != Verdict.Void).ToList();
            var fired = valid.Count(s => s.Verdict == Verdict.Held);
            results.Add((candidate, fired, valid.Count, SpendTotal.Of(sample.Scores.Select(s => s.Cost))));

            output.WriteLine($"{candidate,-18} fired {fired}/{valid.Count}  {FixtureBuilder.DescriptionOf(Path.Combine(overlay, "skills", suite.SkillUnderTest, "SKILL.md"))}");
            foreach (var s in sample.Scores) output.WriteLine($"    {s.Verdict,-9} {s.Detail}  {s.Cost}");
        }

        output.WriteLine("");
        output.WriteLine("| Candidate | Fired | Cost |");
        output.WriteLine("|---|---|---|");
        foreach (var (candidate, fired, valid, cost) in results)
            output.WriteLine($"| {candidate} | {fired}/{valid} | {cost} |");
        output.WriteLine(ledger.Report());

        // A screen, not a gate. Every candidate firing every time is the finding that a description
        // break cannot be made to bite, and it has to reach the report rather than die here.
        Assert.All(results, r => Assert.True(r.Valid > 0, $"{r.Candidate}: no valid runs"));
    }
}

public sealed class LadderFactAttribute : Xunit.FactAttribute
{
    public LadderFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls; set SKILL_HARNESS_LIVE=1";
        else if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LADDER") != "1")
            Skip = "nine runs and about ten minutes; set SKILL_HARNESS_LADDER=1";
    }
}
