using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// The experiment specified at the end of docs/research/steering-code-style.md.
///
/// One fixed coding task, four arms, and the only thing that varies between arms is which file the
/// comment rule sits in: nowhere, CLAUDE.md, a custom output style that keeps the default coding
/// instructions, and the same style with them dropped.
///
/// Triple-locked like the other long passes. SKILL_HARNESS_LIVE=1 gets you into the project and
/// SKILL_HARNESS_DENSITY=1 gets you into this test, because 4 arms x 15 runs is 60 paid runs.
///
/// One test that loops rather than a theory, for the reason the calibration pass is: the ledger is
/// shared across every suite, and a theory would hand each suite a fresh copy of the ceiling.
///
/// The pass is a MEASUREMENT. It fails when it measured nothing usable, never because an arm scored
/// badly: a bad arm is the finding.
/// </summary>
public class CommentDensityPassTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>15 runs an arm, 60 runs a suite, which is what the specification costed at $36.</summary>
    private static int RunsPerArm =>
        int.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_DENSITY_RUNS"), out var n) && n > 0 ? n : 15;

    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 36.00m;

    [DensityFact]
    public async Task Measure_comment_density_by_where_the_rule_sits()
    {
        var suites = SuitesUnderTest.All(Paths);
        var ledger = new SpendLedger(Ceiling);
        var stamp = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var measured = new List<(DiscoveredSuite Suite, IReadOnlyList<ArmStats> Stats)>();
        DensityOutcome? stopped = null;

        output.WriteLine($"comment-density pass, {RunEnvironment.Current}, {RunsPerArm} runs/arm, ceiling ${Ceiling:0.00}");
        output.WriteLine($"{suites.Count} suite(s): {string.Join(", ", suites.Select(s => s.Name))}");

        foreach (var found in suites)
        {
            if (stopped is not null)
            {
                output.WriteLine($"{found.Name,-20} not run, the pass already stopped");
                continue;
            }

            var journalPath = Path.Combine(found.RunRecords, $"comment-density-{stamp}.jsonl");

            DensityOutcome outcome;
            using (var journal = new DensityJournal(journalPath))
            {
                outcome = await new CommentDensityPass(Paths, found, RunsPerArm)
                    .RunAsync(journal, ledger, output.WriteLine, CancellationToken.None);
            }
            if (outcome.Stopped) stopped = outcome;

            // A suite that declared no coding task ran nothing and wrote no journal worth rendering.
            if (outcome.Runs == 0)
            {
                output.WriteLine($"{found.Name,-20} {outcome.Reason}");
                continue;
            }

            var stats = ArmStats.FromJournal(journalPath);
            var reportPath = Path.ChangeExtension(journalPath, ".md");
            File.WriteAllText(reportPath, DensityMarkdown.Render(found, stats, outcome, ledger, journalPath));

            output.WriteLine(File.ReadAllText(reportPath));
            output.WriteLine($"{found.Name}: report written to {reportPath}");
            measured.Add((found, stats));
        }

        Assert.True(stopped is null, $"pass stopped early: {stopped?.Reason}. The journal holds every run that finished.");
        Assert.NotEmpty(measured);

        foreach (var (found, stats) in measured)
        {
            // Every arm needs a reading or there is nothing to compare. An arm that voided every run
            // is a broken arm, not a clean one, and averaging the survivors would hide that.
            Assert.Equal(CommentArm.All.Count, stats.Count);
            foreach (var arm in stats)
                Assert.True(arm.Mean is not null, $"{found.Name}: arm {arm.Arm} produced no valid reading in {arm.Runs} runs");
        }
    }
}

/// <summary>4 arms x 15 runs is 60 paid runs. It needs its own lock, on top of the project's.</summary>
public sealed class DensityFactAttribute : Xunit.FactAttribute
{
    public DensityFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls; set SKILL_HARNESS_LIVE=1";
        else if (Environment.GetEnvironmentVariable("SKILL_HARNESS_DENSITY") != "1")
            Skip = "the comment-density pass is 60 runs an arm-set; set SKILL_HARNESS_DENSITY=1";
    }
}
