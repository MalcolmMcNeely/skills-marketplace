using Xunit;
using Xunit.Abstractions;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #12. The whole 23-case pass, in one run. The measured pass took 133 runs and 02:08:14 of continuous calling.
///
/// Triple-locked, because this is the expensive one. SKILL_HARNESS_LIVE=1 gets you into the project;
/// SKILL_HARNESS_CALIBRATE=1 gets you into this test. The single-case tests above are for checking the
/// wiring, and nobody should trip the full pass by running the project.
///
/// Resumable. Point SKILL_HARNESS_JOURNAL at an existing journal and every case with enough valid runs
/// on disk is skipped, so a pass a usage limit cut short is continued rather than restarted.
/// </summary>
public class CalibrationPassTests(ITestOutputHelper output)
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    /// <summary>#11 fixed the suite ceiling at $50. Notional, but it still bounds a runaway.</summary>
    private static decimal Ceiling =>
        decimal.TryParse(Environment.GetEnvironmentVariable("SKILL_HARNESS_CEILING_USD"), out var c) ? c : 50.00m;

    private static string JournalPath =>
        Environment.GetEnvironmentVariable("SKILL_HARNESS_JOURNAL") is { Length: > 0 } p
            ? p
            : Path.Combine(Found.RunRecords, $"calibration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");

    [CalibrationFact]
    public async Task Calibrate_the_gate_against_the_good_fixture()
    {
        var suite = Found.Suite;
        var journalPath = JournalPath;
        var pass = new CalibrationPass(Paths, suite);
        var ledger = new SpendLedger(Ceiling);

        CalibrationOutcome outcome;
        using (var journal = new RunJournal(journalPath))
        {
            outcome = await pass.RunAsync(journal, ledger, output.WriteLine, CancellationToken.None);
        }

        var report = CalibrationReport.FromJournal(journalPath, suite);
        var markdown = CalibrationMarkdown.Render(report, outcome, suite, journalPath);

        var reportPath = Path.ChangeExtension(journalPath, ".md");
        File.WriteAllText(reportPath, markdown);
        output.WriteLine(markdown);
        output.WriteLine($"report written to {reportPath}");

        // The pass is a MEASUREMENT, not a gate. It fails only when it measured nothing usable,
        // because a low p_good is a finding about the fixture and must still reach the report.
        Assert.False(outcome.Stopped, $"pass stopped early: {outcome.Reason}. Journal kept at {journalPath}; re-run with SKILL_HARNESS_JOURNAL set to resume.");
        Assert.True(report.PositiveValid > 0, "no valid should-fire runs, so there is no p_good to report");
        Assert.Equal(0, report.ModelDriftRuns);
    }
}

/// <summary>The full pass is 125 runs. It needs its own lock, on top of the project's.</summary>
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
