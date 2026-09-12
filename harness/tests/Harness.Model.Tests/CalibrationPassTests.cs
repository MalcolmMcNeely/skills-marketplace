using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #12. The whole firing suite, in one run. The measured pass took 133 runs and 02:08:14 of
/// continuous calling against one skill.
///
/// Triple-locked, because this is the expensive one. SKILL_HARNESS_LIVE=1 gets you into the project;
/// SKILL_HARNESS_CALIBRATE=1 gets you into this test. The single-case layers are for checking the
/// wiring, and nobody should trip the full pass by running the project.
///
/// #27. One pass per discovered suite, planned from what discovery returns rather than from one
/// loaded file, so a skill folder landing in skills/ is calibrated by being there. It stays ONE test
/// that loops rather than a theory: the ledger is shared across every suite, and a theory would hand
/// each suite a fresh copy of the ceiling #11 fixed once.
///
/// Resumable. Point SKILL_HARNESS_JOURNAL at an existing journal and the suite that journal sits
/// under skips every case with enough valid runs on disk, so a pass a usage limit cut short is
/// continued rather than restarted.
/// </summary>
public class CalibrationPassTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>#11 fixed the suite ceiling at $50. Notional, but it still bounds a runaway.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 50.00m;

    [CalibrationFact]
    public async Task Calibrate_the_gate_against_the_good_fixture()
    {
        var suites = SuitesUnderTest.All(Paths);
        var resume = ResumeOverride.Resolve(
            Environment.GetEnvironmentVariable("SKILL_HARNESS_JOURNAL"), suites, Paths.Root);
        // One ledger across every suite. A ledger each would let the pass spend what #11 allowed once
        // per suite, while every suite reported itself as within budget.
        var ledger = new SpendLedger(Ceiling);
        var stamp = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var measured = new List<(DiscoveredSuite Suite, CalibrationReport Report)>();
        CalibrationOutcome? stopped = null;

        output.WriteLine($"calibration pass, {RunEnvironment.Current}, {suites.Count} suite(s): {string.Join(", ", suites.Select(s => s.Name))}");

        foreach (var found in suites)
        {
            if (stopped is not null)
            {
                output.WriteLine($"{found.Name,-20} not run, the pass already stopped");
                continue;
            }

            // A suite graded on its body alone has no p_good to measure. Discovery allows one, and a
            // firing plan with no positives in it would journal nothing and report a zero.
            if (found.Suite.Firing.ShouldFire.Count == 0)
            {
                output.WriteLine($"{found.Name,-20} declares no should-fire case, so there is no p_good to calibrate");
                continue;
            }

            // The override resumes the suite whose run records it sits under, and only that one.
            // Every other suite in the pass starts a journal of its own.
            var journalPath = ResumeOverride.PathFor(
                resume, found, Path.Combine(found.RunRecords, $"calibration-{stamp}.jsonl"));

            CalibrationOutcome outcome;
            using (var journal = new RunJournal(journalPath))
            {
                outcome = await new CalibrationPass(Paths, found)
                    .RunAsync(journal, ledger, output.WriteLine, CancellationToken.None);
            }
            if (outcome.Stopped) stopped = outcome;

            var report = CalibrationReport.FromJournal(journalPath, found.Suite);
            var reportPath = Path.ChangeExtension(journalPath, ".md");
            File.WriteAllText(reportPath, CalibrationMarkdown.Render(report, outcome, found.Suite, journalPath));

            output.WriteLine(File.ReadAllText(reportPath));
            output.WriteLine($"{found.Name}: report written to {reportPath}");
            measured.Add((found, report));
        }

        // The pass is a MEASUREMENT, not a gate. It fails only when it measured nothing usable,
        // because a low p_good is a finding about the fixture and must still reach the report.
        Assert.True(stopped is null,
            $"pass stopped early: {stopped?.Reason}. Journals are kept beside the suites they measured; re-run with SKILL_HARNESS_JOURNAL set to resume.");

        // Every suite skipped is a pass with no p_good in it, and the loop below would sweep an
        // empty list and read as green. The gate on the map rests on this number existing.
        Assert.NotEmpty(measured);

        foreach (var (found, report) in measured)
        {
            Assert.True(report.PositiveValid > 0, $"{found.Name}: no valid should-fire runs, so there is no p_good to report");
            Assert.Equal(0, report.ModelDriftRuns);
        }
    }
}

/// <summary>The full pass is 125 runs a suite. It needs its own lock, on top of the project's.</summary>
public sealed class CalibrationFactAttribute : Xunit.FactAttribute
{
    public CalibrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls; set SKILL_HARNESS_LIVE=1";
        else if (Environment.GetEnvironmentVariable("SKILL_HARNESS_CALIBRATE") != "1")
            Skip = "the full calibration pass measured 133 runs and 02:08:14; set SKILL_HARNESS_CALIBRATE=1";
    }
}
