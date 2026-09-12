using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #16. A killed run emits no result line, so it reports no cost. Charging it zero would hide
/// roughly forty per cent of a pass from the runaway guard.
/// </summary>
public class RunCostTests
{
    [Fact]
    public void A_run_that_reports_no_cost_is_charged_a_non_zero_estimate()
    {
        var cost = RunCost.Of(null);
        Assert.True(cost.IsEstimated);
        Assert.True(cost.Usd > 0m);
    }

    [Fact]
    public void A_run_that_reports_its_own_cost_is_charged_that_measured_figure()
    {
        var cost = RunCost.Of(0.196m);
        Assert.False(cost.IsEstimated);
        Assert.Equal(0.196m, cost.Usd);
    }

    [Fact]
    public void A_reported_zero_is_a_measurement_and_not_a_missing_figure()
    {
        // A run whose result line says $0.00 measured $0.00. Only a MISSING line is estimated.
        Assert.False(RunCost.Of(0m).IsEstimated);
    }

    [Fact]
    public void A_scored_run_with_no_result_line_carries_the_estimate()
    {
        var killed = Fake.Outcome(exit: 1, subtype: null, seconds: 9.5);
        Assert.True(Scoring.ScoreFiring(killed, ["csharp-new-class"]).Cost.IsEstimated);
    }

    [Fact]
    public void A_scored_run_that_billed_keeps_its_own_figure()
    {
        var score = Scoring.ScoreFiring(Fake.Outcome(cost: 0.207m, skills: [("csharp-new-class", 4)]), ["csharp-new-class"]);
        Assert.False(score.Cost.IsEstimated);
        Assert.Equal(0.207m, score.Cost.Usd);
    }

    [Fact]
    public async Task A_resample_loop_of_costless_runs_still_exhausts_the_ledger()
    {
        // The bite. #8 measured ten void runs spending $2.28 while the cap counted attempts and not
        // money. A ledger that charges a killed run zero never stops such a loop at all.
        var ledger = new SpendLedger(1.00m);
        var attempts = 0;

        var sample = await Resampler.CollectAsync(wanted: 5, cap: 100, once: _ =>
        {
            attempts++;
            return Task.FromResult(Scoring.ScoreFiring(Fake.Outcome(exit: 1, subtype: null, seconds: 9.5), ["x"]));
        }, ledger);

        Assert.Equal("suite-budget-exhausted", sample.Failure);
        Assert.True(attempts < 100, $"{attempts} attempts, {ledger.Report()}");
        Assert.True(ledger.Spent >= 1.00m, ledger.Report());
    }
}

/// <summary>Nothing may quote a figure without saying whether it was measured or estimated.</summary>
public class ReportedCostTests
{
    [Fact]
    public void A_single_run_figure_names_its_basis_both_ways()
    {
        Assert.Equal("$0.196 measured", RunCost.Of(0.196m).ToString());
        Assert.Equal("$0.230 estimated", RunCost.Of(null).ToString());
    }

    [Fact]
    public void A_refused_run_does_not_print_a_figure_it_never_reported()
    {
        // This text lands in the journal's detail, beside a costUsd the ledger ESTIMATED. A
        // fabricated "$0.00" there would put two disagreeing figures on one run.
        var refused = Fake.Outcome(exit: 1, subtype: "success", seconds: 1.7);

        Assert.True(refused.Throttled);
        Assert.Contains("no cost reported", refused.VoidReason, StringComparison.Ordinal);
        Assert.DoesNotContain("$0.00", refused.VoidReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_total_with_no_estimate_in_it_says_so()
    {
        var total = SpendTotal.Of([RunCost.Of(0.20m), RunCost.Of(0.30m)]);
        Assert.Equal(0.50m, total.TotalUsd);
        Assert.Equal(0, total.EstimatedRuns);
        Assert.Contains("measured", total.ToString());
    }

    [Fact]
    public void A_blended_total_names_the_estimated_part()
    {
        var total = SpendTotal.Of([RunCost.Of(0.20m), RunCost.Of(null)]);
        Assert.Equal(1, total.EstimatedRuns);
        Assert.Equal(0.20m + RunCost.KilledRunEstimateUsd, total.TotalUsd);
        Assert.Contains("estimated", total.ToString());
    }

    [Fact]
    public void The_ledger_report_says_which_it_is_quoting()
    {
        var ledger = new SpendLedger(1.00m);
        Assert.Equal("$0.00 of $1.00, all measured", ledger.Report());

        ledger.Record(new RunScore(Verdict.Void, "", [], [], RunCost.Of(null)));
        Assert.Equal("$0.23 of $1.00, $0.23 of it estimated over 1 run", ledger.Report());
    }

    [Fact]
    public void A_median_run_cost_is_taken_over_measured_runs_only()
    {
        // An estimate is the same fixed number every time. Letting it into a median would drag the
        // median towards the estimate and report the result as a price.
        var measured = new CaseReport("P1", CaseKind.ShouldFire, 2, 2, 1.0,
            [RunCost.Of(0.10m), RunCost.Of(0.20m), RunCost.Of(null), RunCost.Of(null)], []);
        Assert.Equal(0.15m, CalibrationReport.Median([measured]));
    }
}

public class JournalCostBasisTests
{
    [Fact]
    public void The_journal_records_whether_a_cost_was_measured_or_estimated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "", [], [], RunCost.Of(0.196m)));
                journal.Append("P2", 3, Fake.Outcome(exit: 1, subtype: null), new RunScore(Verdict.Void, "", [], [], RunCost.Of(null)));
            }

            var entries = RunJournal.Read(path);
            Assert.False(entries[0].Cost.IsEstimated);
            Assert.Equal(0.196m, entries[0].Cost.Usd);
            Assert.True(entries[1].Cost.IsEstimated);
            Assert.Equal(RunCost.KilledRunEstimateUsd, entries[1].Cost.Usd);

            // The basis is on the line, not inferred on the way back in.
            Assert.Contains("\"costBasis\":\"estimated\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_journal_written_before_16_still_reads_honestly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            // Two entries in the old shape: no costBasis field at all.
            File.WriteAllLines(path,
            [
                """{"at":"2026-09-02T10:00:00+00:00","case":"P1","layer":3,"verdict":"Held","detail":"","costUsd":0.196}""",
                """{"at":"2026-09-02T10:01:00+00:00","case":"P2","layer":3,"verdict":"Void","detail":"","costUsd":null}""",
            ]);

            var entries = RunJournal.Read(path);
            Assert.False(entries[0].Cost.IsEstimated);       // it billed, and the figure is the measurement
            Assert.True(entries[1].Cost.IsEstimated);        // it reported nothing, which is what the estimate is for
        }
        finally { File.Delete(path); }
    }
}
