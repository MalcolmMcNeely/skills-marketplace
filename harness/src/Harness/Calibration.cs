namespace Harness;

/// <summary>How much of the firing suite a pass runs. #6 buys narrower shapes than #12 needed.</summary>
public enum FiringPlanShape
{
    /// <summary>#12: all 23 cases, 125 runs.</summary>
    Full,
    /// <summary>The 10 should-fire cases at their declared run counts. 60 runs.</summary>
    PositivesOnly,
    /// <summary>The first 3 should-fire cases at 2 runs each. 6 runs, and a probe, not a measurement.</summary>
    ShortPositives,
}

/// <summary>
/// Issue #15. Which of #10's three kinds a firing case is, said out loud rather than inferred.
/// Two of the three are graded on silence, so an expected set cannot tell them apart, and the
/// stop rule needs exactly that difference.
/// </summary>
public enum CaseKind
{
    /// <summary>Graded on an exact set match. Carries the set it expects.</summary>
    ShouldFire,
    /// <summary>Graded on the silence of the skill under test, and gated on it.</summary>
    ShouldNotFire,
    /// <summary>Graded on silence, run and recorded, never gated. #10's murky three.</summary>
    Watch,
}

/// <summary>
/// Issue #12. Runs #10's 23-case suite against the frozen good fixture and produces the numbers every
/// gate value on the map rests on. 125 runs: 10 positives at 6, 10 negatives at 5, 3 watch cases at 5.
///
/// Serial on purpose. The pass is bounded by a roughly 83-minute window and a usage limit, not by a
/// wallet, and running cases in parallel would race that limit while making every duration unreadable.
///
/// The pass writes a journal and nothing else. Every number is derived from the journal afterwards by
/// <see cref="CalibrationReport"/>, so a pass stopped at run 110 still reports on the 110 it got.
/// </summary>
public sealed class CalibrationPass(HarnessPaths paths, SuiteFile suite, FiringRunner? runner = null, FiringPlanShape shape = FiringPlanShape.Full)
{
    public const int FiringLayer = 3;

    /// <summary>Runs every firing case the journal does not already satisfy.</summary>
    public async Task<CalibrationOutcome> RunAsync(
        RunJournal journal,
        SpendLedger ledger,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var firing = runner ?? new FiringRunner(paths);
        var already = ValidRunsByCase(journal.Path);
        var started = DateTimeOffset.UtcNow;
        log?.Invoke($"calibration pass, {RunEnvironment.Current}, journal {journal.Path}");

        foreach (var step in Plan())
        {
            var done = already.GetValueOrDefault(step.Id);
            if (done >= step.Runs)
            {
                log?.Invoke($"{step.Id,-4} skipped, {done} valid runs already journalled");
                continue;
            }

            var remaining = step.Runs - done;
            var sample = await Resampler.CollectAsync(remaining, step.Cap, async token =>
            {
                var outcome = await firing.RunAsync(step.Prompt, token);
                var score = Score(step, outcome);
                journal.Append(step.Id, FiringLayer, outcome, score);
                return score;
            }, ledger, ct);

            log?.Invoke($"{step.Id,-4} {sample.Valid}/{remaining} valid, {ledger.Report()}, {sample.Failure ?? "ok"}");

            // A throttle or an exhausted ledger ends the pass. Neither is a fact about the skill,
            // and carrying on would only add runs the report has to discard.
            if (sample.Failure is "throttled" or "suite-budget-exhausted")
                return new CalibrationOutcome(true, sample.Failure, started, DateTimeOffset.UtcNow);
        }

        return new CalibrationOutcome(false, null, started, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One case, ready to run. The kind is declared, not read back off <see cref="Expect"/>, because
    /// a should-not-fire case and a watch case are both silent and Expect cannot separate them.
    /// The constructor refuses a step whose expected set contradicts its kind. A `with` expression
    /// copies fields and skips that check, so narrow a shape by Runs and Cap, never by Kind.
    /// </summary>
    public sealed record Step(string Id, string Prompt, int Runs, int Cap, CaseKind Kind, IReadOnlyList<string> Expect)
    {
        /// <summary>The set a should-fire case must match exactly. Empty for a case graded on silence.</summary>
        public IReadOnlyList<string> Expect { get; init; } = Validated(Id, Kind, Expect);

        private static IReadOnlyList<string> Validated(string id, CaseKind kind, IReadOnlyList<string> expect)
        {
            if (kind is CaseKind.ShouldFire && expect.Count == 0)
                throw new ArgumentException($"should-fire case '{id}' is graded on an exact set match and has no expected set");
            if (kind is not CaseKind.ShouldFire && expect.Count > 0)
                throw new ArgumentException($"{kind} case '{id}' is graded on silence and must carry no expected set");
            return expect;
        }
    }

    /// <summary>Which scorer a step is graded by. The kind decides, and only the kind.</summary>
    public RunScore Score(Step step, RunOutcome outcome) => step.Kind switch
    {
        CaseKind.ShouldFire => Scoring.ScoreFiring(outcome, step.Expect),
        _ => Scoring.ScoreQuiet(outcome, suite.SkillUnderTest),
    };

    /// <summary>
    /// Positives, then negatives, then the watch list.
    ///
    /// #6 needs two narrower shapes. PositivesOnly re-measures p_good against a broken description
    /// without paying for 50 negative runs that answer a different question. ShortPositives is the
    /// cheap "did this move at all" probe, for an arm whose layer 3 result is expected to be a
    /// non-event and would not be worth an hour to confirm at full width.
    /// </summary>
    public IEnumerable<Step> Plan() => shape switch
    {
        FiringPlanShape.PositivesOnly => Positives(),
        FiringPlanShape.ShortPositives => Positives().Take(ShortCases)
            .Select(s => s with { Runs = ShortRuns, Cap = ShortRuns * 2 }),
        _ => Positives()
            .Concat(Quiet(suite.Firing.ShouldNotFire, CaseKind.ShouldNotFire))
            .Concat(Quiet(suite.Firing.Watch, CaseKind.Watch)),
    };

    public const int ShortCases = 3;
    public const int ShortRuns = 2;

    private IEnumerable<Step> Positives() =>
        suite.Firing.ShouldFire.Select(c => new Step(c.Id, c.Prompt, c.Runs, c.Cap, CaseKind.ShouldFire, c.Expect));

    private static IEnumerable<Step> Quiet(IEnumerable<NegativeCase> cases, CaseKind kind) =>
        cases.Select(c => new Step(c.Id, c.Prompt, c.Runs, c.Cap, kind, []));

    /// <summary>Resume support: a case with enough valid runs on disk is not run again.</summary>
    public static Dictionary<string, int> ValidRunsByCase(string journalPath) =>
        RunJournal.Read(journalPath)
            .Where(e => e.Verdict != nameof(Verdict.Void))
            .GroupBy(e => e.CaseId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
}

public sealed record CalibrationOutcome(bool Stopped, string? Reason, DateTimeOffset Started, DateTimeOffset Ended)
{
    public TimeSpan Elapsed => Ended - Started;
}

public sealed record CaseReport(
    string CaseId,
    CaseKind Kind,
    int Valid,
    int Passed,
    double Rate,
    IReadOnlyList<decimal> Costs,
    IReadOnlyList<JournalEntry> Runs);

/// <summary>
/// Every one of #12's seven outputs, derived from the journal. A pure function of what is on disk,
/// so a partial pass reports honestly instead of reporting nothing.
/// </summary>
public sealed record CalibrationReport(
    RunEnvironment Environment,
    IReadOnlyList<CaseReport> Positives,
    IReadOnlyList<CaseReport> Negatives,
    IReadOnlyList<CaseReport> Watch,
    int TotalRuns,
    int VoidRuns,
    int ThrottledRuns,
    int ModelDriftRuns,
    decimal TotalCostUsd)
{
    /// <summary>Output 1: p_good, pooled over the should-fire runs.</summary>
    public int PositiveValid => Positives.Sum(c => c.Valid);
    public int PositivePassed => Positives.Sum(c => c.Passed);
    public double PGood => PositiveValid == 0 ? 0 : (double)PositivePassed / PositiveValid;
    public (double Low, double High) PGoodInterval => Pooling.Wilson(PositivePassed, PositiveValid);

    /// <summary>
    /// Output 3: the gate at a given run count.
    ///
    /// Derived from the LOWER BOUND of the interval, not from PGood itself. #12 measured 60 of 60,
    /// so PGood is 1.000 and a gate built on it demands a perfect pass at every run count: one flaky
    /// run reddens the build. 1.000 is the ceiling of what 60 runs can show, not evidence the true
    /// rate is 1. The lower bound is the same measurement read honestly, and it is what the interval
    /// is for. At 0.940 over 60 runs the gate is 53, not 60.
    /// </summary>
    public int GateK(int n) => Pooling.GateK(n, PGoodInterval.Low);

    /// <summary>Output 2: the zero floor, over cases with enough runs to judge one.</summary>
    public IReadOnlyList<string> ZeroFloorBreaches =>
        [.. Positives.Where(c => c.Valid >= Pooling.MinRunsForZeroFloor && c.Passed == 0).Select(c => c.CaseId)];

    /// <summary>Output 4: false fires of the skill under test across the negative runs.</summary>
    public int NegativeValid => Negatives.Sum(c => c.Valid);
    public int FalseFires => Negatives.Sum(c => c.Valid - c.Passed);

    /// <summary>
    /// Output 6, the measurement #11 parked here. FirstDecision is safe for negatives only if the
    /// skill under test never fires AFTER another skill has already fired. Killing at the first Skill
    /// call would otherwise hide the later fire behind the earlier decoy and under-report false fires.
    /// </summary>
    public IReadOnlyList<JournalEntry> LateFires { get; init; } = [];
    public bool FirstDecisionSafeForNegatives => LateFires.Count == 0;

    /// <summary>Output 7, kept apart. A negative prompt asks for real work and was never priced.</summary>
    public decimal? PositiveMedianCost => Median(Positives);
    public decimal? NegativeMedianCost => Median(Negatives);

    public static decimal? Median(IEnumerable<CaseReport> cases)
    {
        var costs = cases.SelectMany(c => c.Costs).Order().ToList();
        if (costs.Count == 0) return null;
        var mid = costs.Count / 2;
        return costs.Count % 2 == 1 ? costs[mid] : (costs[mid - 1] + costs[mid]) / 2m;
    }

    public static CalibrationReport FromJournal(string journalPath, SuiteFile suite)
    {
        var entries = RunJournal.Read(journalPath);
        var skill = suite.SkillUnderTest;

        var positives = Build(entries, suite.Firing.ShouldFire.Select(c => c.Id), CaseKind.ShouldFire);
        var negatives = Build(entries, suite.Firing.ShouldNotFire.Select(c => c.Id), CaseKind.ShouldNotFire);
        var watch = Build(entries, suite.Firing.Watch.Select(c => c.Id), CaseKind.Watch);

        var lateFires = negatives.Concat(watch)
            .SelectMany(c => c.Runs)
            .Where(e => FiredLate(e, skill))
            .ToList();

        return new CalibrationReport(
            RunEnvironment.Current,
            positives, negatives, watch,
            entries.Count,
            entries.Count(e => e.Verdict == nameof(Verdict.Void)),
            entries.Count(e => e.Throttled),
            entries.Count(e => !e.ModelHeld),
            entries.Sum(e => e.CostUsd ?? 0m))
        { LateFires = lateFires };
    }

    private static bool FiredLate(JournalEntry e, string skill)
    {
        var mine = e.SkillCalls.FirstOrDefault(c => c.Name.Equals(skill, StringComparison.Ordinal));
        if (mine is null) return false;
        return e.SkillCalls.Any(c => c.Ordinal < mine.Ordinal && !c.Name.Equals(skill, StringComparison.Ordinal));
    }

    private static IReadOnlyList<CaseReport> Build(
        IReadOnlyList<JournalEntry> entries, IEnumerable<string> ids, CaseKind kind) =>
        [.. ids.Select(id =>
        {
            var runs = entries.Where(e => e.CaseId == id).ToList();
            var valid = runs.Where(e => e.Verdict != nameof(Verdict.Void)).ToList();
            // Held is the pass for both shapes: a positive matched its set, a negative stayed quiet.
            var passed = valid.Count(e => e.Verdict == nameof(Verdict.Held));
            return new CaseReport(id, kind, valid.Count, passed,
                valid.Count == 0 ? 0 : (double)passed / valid.Count,
                [.. runs.Where(r => r.CostUsd is not null).Select(r => r.CostUsd!.Value)],
                runs);
        })];
}
