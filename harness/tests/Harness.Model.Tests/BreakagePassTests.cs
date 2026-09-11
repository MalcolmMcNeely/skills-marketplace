using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #6. Four arms. Planned at 149 runs; the measured pass took 256 and 03:19:34 of continuous calling.
///
/// Triple-locked like the calibration pass, and for the same reason: SKILL_HARNESS_LIVE=1 gets you
/// into the project, SKILL_HARNESS_BREAK=1 gets you into this test.
///
/// Resumable per ARM. Each arm keeps its own journal in the run directory, and an arm whose cases
/// already have enough valid runs on disk is skipped. Point SKILL_HARNESS_BREAK_DIR at an existing
/// directory to continue a pass a usage limit cut short, rather than paying for it twice.
/// </summary>
public class BreakagePassTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    /// <summary>#11 fixed the suite ceiling at $50. Notional, but it still bounds a runaway.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 50.00m;

    /// <summary>
    /// Under the suite this pass measured, per #26. A run record is evidence about one skill, and a
    /// shared folder read as everyone's.
    ///
    /// An override is resolved against the harness root, not the process working directory. MEASURED
    /// the hard way: the test host runs from its own build output, so a relative override put an
    /// entire pass's journals under bin/Debug where nobody would look for them.
    /// </summary>
    private static string RunDirectory
    {
        get
        {
            var stamp = Path.Combine(Found.RunRecords, $"breakage-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            if (Environment.GetEnvironmentVariable("SKILL_HARNESS_BREAK_DIR") is not { Length: > 0 } d) return stamp;
            return Path.IsPathRooted(d) ? d : Path.GetFullPath(Path.Combine(Paths.Root, d));
        }
    }

    [BreakageFact]
    public async Task Break_the_fixture_two_ways_and_see_which_layer_notices()
    {
        var suite = Found.Suite;
        var dir = RunDirectory;
        Directory.CreateDirectory(dir);

        var pass = new BreakagePass(Paths, Found);
        // One ledger across every arm. Four separate ceilings would let the pass spend four times
        // what #11 allowed while each arm reported itself as within budget.
        var ledger = new SpendLedger(Ceiling);
        var started = DateTimeOffset.UtcNow;
        var journals = new List<(string Arm, string Journal)>();
        var reports = new List<ArmReport>();
        CalibrationOutcome? stopped = null;

        output.WriteLine($"breakage pass, {RunEnvironment.Current}, run directory {dir}");

        foreach (var arm in BreakageArm.Plan)
        {
            var journalPath = Path.Combine(dir, $"break-{arm.Id}.jsonl");
            journals.Add((arm.Id, journalPath));

            if (stopped is null)
            {
                using var journal = new RunJournal(journalPath);
                var outcome = await pass.RunArmAsync(arm, journal, ledger, output.WriteLine, CancellationToken.None);
                if (outcome.Stopped) stopped = outcome;
            }
            else output.WriteLine($"{arm.Id,-12} not run, the pass already stopped");

            reports.Add(ArmReport.FromJournal(arm, journalPath, suite));
        }

        var report = new BreakageReport(RunEnvironment.Current, reports);
        var markdown = BreakageMarkdown.Render(report, DateTimeOffset.UtcNow - started, journals);

        var reportPath = Path.Combine(dir, "breakage.md");
        File.WriteAllText(reportPath, markdown);
        output.WriteLine(markdown);
        output.WriteLine($"report written to {reportPath}");

        // #6 asks a question, and both answers are findings. A harness that fails to catch its own
        // break is the single most valuable result this ticket can produce, so it must reach the
        // report rather than die as a red assertion. The pass fails only when it measured nothing.
        Assert.True(stopped is null,
            $"pass stopped early: {stopped?.Reason}. Journals kept in {dir}; re-run with SKILL_HARNESS_BREAK_DIR set to resume.");

        foreach (var arm in report.Arms)
        {
            if (arm.Arm.RunFiring)
                Assert.True(arm.FiringValid > 0, $"{arm.Arm.Id}: no valid layer 3 runs, so layer 3 has no verdict to give");
            Assert.True(arm.ContractValid > 0, $"{arm.Arm.Id}: no valid layer 4 runs, so layer 4 has no verdict to give");
        }
    }
}

/// <summary>256 runs and 03:19:34 when it was measured. It needs its own lock, on top of the project's.</summary>
public sealed class BreakageFactAttribute : Xunit.FactAttribute
{
    public BreakageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls; set SKILL_HARNESS_LIVE=1";
        else if (Environment.GetEnvironmentVariable("SKILL_HARNESS_BREAK") != "1")
            Skip = "the full breakage pass measured 256 runs and 03:19:34; set SKILL_HARNESS_BREAK=1";
    }
}
