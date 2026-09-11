using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #17. One run shape for every firing case charged the whole pass the price of the slowest
/// stop rule. #8 measured `FirstDecision` at about 9.5 seconds a run against about 40, and 50 of a
/// 125-run pass are negatives, so the rule the case kind can safely take is the largest saving on
/// the board. Time is the constraint on this work, not money.
/// </summary>
public class StopRuleTests
{
    private static readonly HarnessPaths Paths = new();

    private static RunSpec Spec(CaseKind kind) => new FiringRunner(Paths).SpecFor("build me a thing", kind);

    /// <summary>
    /// #11 parked this on the grounds that a decoy firing first would hide a later fire of the skill
    /// under test. #12 output 6 measured it: across all 65 negative and watch-list runs the skill
    /// under test never fired after another skill. The premise #11 rejected is now evidence.
    /// </summary>
    [Fact]
    public void A_should_not_fire_case_is_killed_at_the_first_decision()
    {
        Assert.Equal(StopMode.FirstDecision, Spec(CaseKind.ShouldNotFire).StopMode);
    }

    /// <summary>#10 grades a positive on an exact set match, so a truncated set scores a wrong run green.</summary>
    [Fact]
    public void A_should_fire_case_runs_to_completion()
    {
        Assert.Equal(StopMode.Completion, Spec(CaseKind.ShouldFire).StopMode);
    }

    /// <summary>Ungated, but recorded, and the record is the full fired set.</summary>
    [Fact]
    public void A_watch_case_runs_to_completion()
    {
        Assert.Equal(StopMode.Completion, Spec(CaseKind.Watch).StopMode);
    }

    /// <summary>
    /// The whole table in one place, so wiring a kind to the wrong rule fails here in a second
    /// rather than in a two-hour pass. A fourth kind with no rule of its own fails here too.
    /// </summary>
    [Fact]
    public void Every_case_kind_declares_its_own_stop_rule()
    {
        var expected = new Dictionary<CaseKind, StopMode>
        {
            [CaseKind.ShouldFire] = StopMode.Completion,
            [CaseKind.ShouldNotFire] = StopMode.FirstDecision,
            [CaseKind.Watch] = StopMode.Completion,
        };

        Assert.Equal(expected.Keys.Order(), Enum.GetValues<CaseKind>().Order());
        foreach (var (kind, mode) in expected)
            Assert.Equal(mode, Spec(kind).StopMode);
    }

    /// <summary>Only the stop rule moves. The catalogue, the budget and the working directory do not.</summary>
    [Fact]
    public void The_kind_changes_the_stop_rule_and_nothing_else()
    {
        var positive = Spec(CaseKind.ShouldFire);
        var negative = Spec(CaseKind.ShouldNotFire);

        Assert.NotEqual(positive.StopMode, negative.StopMode);
        Assert.Equal(positive.Prompt, negative.Prompt);
        Assert.Equal(positive.WorkingDirectory, negative.WorkingDirectory);
        Assert.Equal(positive.PluginDirs, negative.PluginDirs);
        Assert.Equal(positive.MaxBudgetUsd, negative.MaxBudgetUsd);
        Assert.Equal(positive.Timeout, negative.Timeout);
        Assert.Equal(positive.Model, negative.Model);
    }
}

/// <summary>The plan is where a case's kind is decided, and the runner is where it is spent.</summary>
public class PlannedStopRuleTests
{
    private static readonly HarnessPaths Paths = new();

    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;
    private static SuiteFile Suite => Found.Suite;

    [Fact]
    public void Every_step_of_a_full_plan_carries_the_stop_rule_its_kind_asks_for()
    {
        var runner = new FiringRunner(Paths);
        var plan = new CalibrationPass(Paths, Suite).Plan().ToList();

        Assert.Contains(plan, s => s.Kind == CaseKind.ShouldFire);
        Assert.Contains(plan, s => s.Kind == CaseKind.ShouldNotFire);
        Assert.Contains(plan, s => s.Kind == CaseKind.Watch);

        foreach (var step in plan)
        {
            var expected = step.Kind == CaseKind.ShouldNotFire ? StopMode.FirstDecision : StopMode.Completion;
            Assert.Equal(expected, runner.SpecFor(step.Prompt, step.Kind).StopMode);
        }
    }

    /// <summary>The saving, counted, and it is what makes this worth doing at all.</summary>
    [Fact]
    public void The_early_stop_covers_every_should_not_fire_run_and_no_other()
    {
        var suite = Suite;
        var plan = new CalibrationPass(Paths, suite).Plan().ToList();
        var total = plan.Sum(s => s.Runs);

        var early = plan.Where(s => FiringRunner.StopRuleFor(s.Kind) == StopMode.FirstDecision).Sum(s => s.Runs);
        Assert.Equal(suite.Firing.ShouldNotFire.Sum(c => c.Runs), early);

        // Counted off the suite rather than pinned, so adding a case does not redden the build for
        // a reason that has nothing to do with the stop rule. As the suite stands it is 50 of 125.
        Assert.True(early * 3 >= total, $"{early} of {total} runs take the early stop");
    }
}

/// <summary>
/// #16 charges a killed run an estimate rather than zero. #17 is what makes that matter: from here
/// on, two runs in five of a pass are killed and report nothing.
/// </summary>
public class KilledNegativeLedgerTests
{
    [Fact]
    public void A_killed_negative_run_still_charges_the_suite_ledger()
    {
        var killed = Fake.Outcome(stop: StopMode.FirstDecision, killed: true, subtype: null, seconds: 9.5,
            skills: [("csharp-modify-class", 3)]);
        var score = Scoring.ScoreQuiet(killed, "csharp-new-class");

        // The kill is the run ENDING, not the run breaking. It scores like any other quiet run.
        Assert.Equal(Verdict.Held, score.Verdict);

        var ledger = new SpendLedger(5.00m);
        ledger.Record(score);

        Assert.Equal(RunCost.KilledRunEstimateUsd, ledger.Spent);
        Assert.Equal(1, ledger.Total.EstimatedRuns);
    }

    [Fact]
    public async Task A_resample_loop_of_early_stop_runs_still_exhausts_the_ledger()
    {
        // The shape the guard exists for. An early-stop run that reached no decision is void and is
        // resampled, and it reports no cost either. Charged zero, that loop runs to the cap inside a
        // ledger that believes it has spent nothing.
        var ledger = new SpendLedger(2.00m);
        var attempts = 0;

        var sample = await Resampler.CollectAsync(wanted: 5, cap: 50, once: _ =>
        {
            attempts++;
            var noDecision = Fake.Outcome(stop: StopMode.FirstDecision, exit: 1, subtype: null, seconds: 9.5);
            return Task.FromResult(Scoring.ScoreQuiet(noDecision, "csharp-new-class"));
        }, ledger);

        Assert.Equal("suite-budget-exhausted", sample.Failure);
        Assert.True(attempts < 50, $"{attempts} attempts, {ledger.Report()}");
        Assert.True(ledger.Spent >= 2.00m, ledger.Report());
    }
}
