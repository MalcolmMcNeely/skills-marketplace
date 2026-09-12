using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #6. Four arms. Planned at 149 runs; the measured pass took 256 and 03:19:34 of continuous
/// calling against one skill.
///
/// Triple-locked like the calibration pass, and for the same reason: SKILL_HARNESS_LIVE=1 gets you
/// into the project, SKILL_HARNESS_BREAK=1 gets you into this test.
///
/// #27. Every discovered suite, four arms each, planned from what discovery returns rather than from
/// one loaded file. The arm plan itself is shared and names its overlays relative to the suite that
/// owns them, so a suite takes part by declaring those breaks and says so loudly if it has not.
///
/// It stays ONE test that loops rather than a theory, because the ledger is shared: a theory would
/// hand each suite a fresh copy of the ceiling #11 fixed once.
///
/// Resumable per arm. Each arm keeps its own journal in the suite's run directory, and an arm whose
/// cases already have enough valid runs on disk is skipped. Point SKILL_HARNESS_BREAK_DIR at an
/// existing run directory to continue a pass a usage limit cut short, rather than paying twice.
/// </summary>
public class BreakagePassTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>#11 fixed the suite ceiling at $50. Notional, but it still bounds a runaway.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 50.00m;

    [BreakageFact]
    public async Task Break_the_fixture_two_ways_and_see_which_layer_notices()
    {
        var suites = SuitesUnderTest.All(Paths);
        // An override is resolved against the harness root, not the process working directory. MEASURED
        // the hard way: the test host runs from its own build output, so a relative override put an
        // entire pass's journals under bin/Debug where nobody would look for them. #27 added the second
        // half of the answer: the suite it resumes is the one whose run records it sits under.
        var resume = ResumeOverride.Resolve(
            Environment.GetEnvironmentVariable("SKILL_HARNESS_BREAK_DIR"), suites, Paths.Root);
        // One ledger across every arm of every suite. Separate ceilings would let the pass spend what
        // #11 allowed many times over while each arm reported itself as within budget.
        var ledger = new SpendLedger(Ceiling);
        var stamp = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var measured = new List<(DiscoveredSuite Suite, BreakageReport Report)>();
        CalibrationOutcome? stopped = null;

        output.WriteLine($"breakage pass, {RunEnvironment.Current}, {suites.Count} suite(s): {string.Join(", ", suites.Select(s => s.Name))}");

        foreach (var found in suites)
        {
            // Whole suites, not just the arms left in one. A stopped pass that carried on would leave
            // a run directory of empty journals under a suite it never measured.
            if (stopped is not null)
            {
                output.WriteLine($"{found.Name,-20} not run, the pass already stopped");
                continue;
            }

            // Under the suite this pass measured, per #26. A run record is evidence about one skill,
            // and a shared folder read as everyone's.
            var dir = ResumeOverride.PathFor(resume, found, Path.Combine(found.RunRecords, $"breakage-{stamp}"));
            Directory.CreateDirectory(dir);
            output.WriteLine($"{found.Name}: run directory {dir}");

            // Per suite, not per pass. One clock across the loop would bill every suite for the
            // time the ones before it took, and the report quotes that figure as a measurement.
            var started = DateTimeOffset.UtcNow;
            var pass = new BreakagePass(Paths, found);
            var journals = new List<(string Arm, string Journal)>();
            var reports = new List<ArmReport>();

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

                reports.Add(ArmReport.FromJournal(arm, journalPath, found.Suite));
            }

            var report = new BreakageReport(RunEnvironment.Current, reports);
            var reportPath = Path.Combine(dir, "breakage.md");
            File.WriteAllText(reportPath, BreakageMarkdown.Render(report, DateTimeOffset.UtcNow - started, journals));

            output.WriteLine(File.ReadAllText(reportPath));
            output.WriteLine($"{found.Name}: report written to {reportPath}");
            measured.Add((found, report));
        }

        // #6 asks a question, and both answers are findings. A harness that fails to catch its own
        // break is the single most valuable result this ticket can produce, so it must reach the
        // report rather than die as a red assertion. The pass fails only when it measured nothing.
        Assert.True(stopped is null,
            $"pass stopped early: {stopped?.Reason}. Journals are kept beside the suites they measured; re-run with SKILL_HARNESS_BREAK_DIR set to resume.");

        // A pass that measured no suite at all has answered #6's question about nothing, and the
        // assertions below would sweep an empty list and report it as four arms holding.
        Assert.NotEmpty(measured);

        foreach (var (found, report) in measured)
            foreach (var arm in report.Arms)
            {
                if (arm.Arm.RunFiring)
                    Assert.True(arm.FiringValid > 0, $"{found.Name}/{arm.Arm.Id}: no valid layer 3 runs, so layer 3 has no verdict to give");
                Assert.True(arm.ContractValid > 0, $"{found.Name}/{arm.Arm.Id}: no valid layer 4 runs, so layer 4 has no verdict to give");
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
