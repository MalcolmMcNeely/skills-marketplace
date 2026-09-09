using System.Globalization;
using System.Text;

namespace Harness;

/// <summary>
/// Renders a <see cref="CalibrationReport"/> as the Markdown that lands in docs/. House style:
/// British English, tables for anything comparative, and an honest note on what was not verified.
/// </summary>
public static class CalibrationMarkdown
{
    /// <summary>Run counts a gate might be evaluated at. #10's pass uses 60.</summary>
    private static readonly int[] GateRunCounts = [10, 20, 30, 60];

    public static string Render(CalibrationReport r, CalibrationOutcome outcome, SuiteFile suite, string journalPath)
    {
        var sb = new StringBuilder();
        var skill = suite.SkillUnderTest;

        sb.AppendLine($"# Calibration pass: {skill}");
        sb.AppendLine();
        sb.AppendLine($"Run on {DateTimeOffset.UtcNow:yyyy-MM-dd}, on this machine, against the frozen good fixture.");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Model | `{r.Environment.Model}` |");
        sb.AppendLine($"| Claude Code | {r.Environment.CliVersion} |");
        sb.AppendLine($"| Runs journalled | {r.TotalRuns} |");
        sb.AppendLine($"| Void runs | {r.VoidRuns} |");
        sb.AppendLine($"| Throttled runs | {r.ThrottledRuns} |");
        sb.AppendLine($"| Runs on the wrong model | {r.ModelDriftRuns} |");
        sb.AppendLine($"| Wall clock | {outcome.Elapsed:hh\\:mm\\:ss} |");
        sb.AppendLine($"| Cost | {r.Cost} |");
        sb.AppendLine($"| Journal | `{journalPath}` |");
        sb.AppendLine();

        if (outcome.Stopped)
        {
            sb.AppendLine($"**This pass did not finish. It stopped on `{outcome.Reason}`.**");
            sb.AppendLine();
            sb.AppendLine("Every number below is derived from the runs that did complete. Re-running the pass");
            sb.AppendLine("against the same journal resumes it: a case with enough valid runs on disk is skipped.");
            sb.AppendLine();
        }

        Section1(sb, r);
        Section2(sb, r);
        Section3(sb, r);
        Section4(sb, r, skill, suite);
        Section5(sb, r, suite);
        Section6(sb, r, skill);
        Section7(sb, r);
        Caveats(sb, r, outcome);

        return sb.ToString();
    }

    private static void Section1(StringBuilder sb, CalibrationReport r)
    {
        var (low, high) = r.PGoodInterval;
        sb.AppendLine("## 1. `p_good`, pooled");
        sb.AppendLine();
        sb.AppendLine($"**{r.PositivePassed} of {r.PositiveValid}** valid should-fire runs matched their expected set.");
        sb.AppendLine();
        sb.AppendLine($"- `p_good` = **{r.PGood:0.000}**");
        sb.AppendLine($"- Wilson 95% interval = **{low:0.000} to {high:0.000}**");
        sb.AppendLine();
        sb.AppendLine($"The map carried 0.67, borrowed from a different pair of stub skills. This number replaces it.");
        sb.AppendLine();
    }

    private static void Section2(StringBuilder sb, CalibrationReport r)
    {
        sb.AppendLine("## 2. Per-case should-fire rates");
        sb.AppendLine();
        sb.AppendLine("| Case | Valid | Matched | Rate |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var c in r.Positives)
            sb.AppendLine($"| {c.CaseId} | {c.Valid} | {c.Passed} | {c.Rate:0.00} |");
        sb.AppendLine();
        sb.AppendLine(r.ZeroFloorBreaches.Count == 0
            ? $"The zero floor did not trip. No healthy case scored 0 out of {Pooling.MinRunsForZeroFloor} or more valid runs."
            : $"**The zero floor tripped on a healthy case: {string.Join(", ", r.ZeroFloorBreaches)}.** "
              + "Either the fixture is not healthy or the floor is set wrong. Do not gate on this pass until that is settled.");
        sb.AppendLine();
    }

    private static void Section3(StringBuilder sb, CalibrationReport r)
    {
        sb.AppendLine("## 3. The derived gate");
        sb.AppendLine();
        sb.AppendLine("`gate_k = max { k : P(Binom(N, p) < k) <= 0.05 }`, the 5th percentile of the healthy distribution.");
        sb.AppendLine();
        sb.AppendLine($"`p` is the **lower bound** of the interval above, {r.PGoodInterval.Low:0.000}, not the point estimate "
                      + $"{r.PGood:0.000}. A gate built on the point estimate demands whatever the pass happened to score, so a "
                      + "perfect pass sets a perfect gate and one flaky run reddens the build. The lower bound is the same "
                      + "measurement read honestly.");
        sb.AppendLine();
        sb.AppendLine("| Runs in a pass | `gate_k` | Pass needs |");
        sb.AppendLine("|---:|---:|---|");
        foreach (var n in GateRunCounts)
        {
            var k = r.GateK(n);
            sb.AppendLine($"| {n} | {k} | {k} of {n} matched |");
        }
        sb.AppendLine();
    }

    private static void Section4(StringBuilder sb, CalibrationReport r, string skill, SuiteFile suite)
    {
        var boundaries = suite.Firing.ShouldNotFire.ToDictionary(c => c.Id, c => c.Boundary ?? "", StringComparer.Ordinal);
        sb.AppendLine("## 4. The negative side");
        sb.AppendLine();
        sb.AppendLine($"**{r.FalseFires} false fires of `{skill}`** across {r.NegativeValid} valid should-not-fire runs.");
        sb.AppendLine();
        sb.AppendLine("| Case | Boundary defended | Valid | Stayed quiet | False fires |");
        sb.AppendLine("|---|---|---:|---:|---:|");
        foreach (var c in r.Negatives)
            sb.AppendLine($"| {c.CaseId} | {boundaries.GetValueOrDefault(c.CaseId, "")} | {c.Valid} | {c.Passed} | {c.Valid - c.Passed} |");
        sb.AppendLine();
        sb.AppendLine("scoring.md's \"at most one\" was a starting position, not a derived number. This is the measurement.");
        sb.AppendLine();
    }

    private static void Section5(StringBuilder sb, CalibrationReport r, SuiteFile suite)
    {
        sb.AppendLine("## 5. The watch list, reported and never gated");
        sb.AppendLine();
        sb.AppendLine("These three feed the map's open question on records, interfaces and new test classes.");
        sb.AppendLine();
        sb.AppendLine("| Case | Question | Valid | Stayed quiet | Fired |");
        sb.AppendLine("|---|---|---:|---:|---:|");
        var boundaries = suite.Firing.Watch.ToDictionary(c => c.Id, c => c.Boundary ?? "", StringComparer.Ordinal);
        foreach (var c in r.Watch)
            sb.AppendLine($"| {c.CaseId} | {boundaries.GetValueOrDefault(c.CaseId, "")} | {c.Valid} | {c.Passed} | {c.Valid - c.Passed} |");
        sb.AppendLine();
    }

    private static void Section6(StringBuilder sb, CalibrationReport r, string skill)
    {
        sb.AppendLine("## 6. Where in the run the skill fired");
        sb.AppendLine();
        sb.AppendLine($"#11 parked this here. The `FirstDecision` stop rule kills a run at the first `Skill` call. "
                      + $"That is safe for negatives only if `{skill}` never fires **after** another skill has already fired.");
        sb.AppendLine();

        if (r.FirstDecisionSafeForNegatives)
        {
            sb.AppendLine($"**Safe.** In no negative or watch run did `{skill}` fire after another skill. "
                          + "The rule can be switched on for negatives, which would cut the cost of the negative half.");
        }
        else
        {
            sb.AppendLine($"**Not safe. `{skill}` fired after another skill in {r.LateFires.Count} run(s).** "
                          + "The rule stays off for negatives: killing at the first `Skill` call would hide these fires behind an earlier decoy.");
            sb.AppendLine();
            sb.AppendLine("| Case | Skill calls, in order |");
            sb.AppendLine("|---|---|");
            foreach (var e in r.LateFires)
                sb.AppendLine($"| {e.CaseId} | {string.Join(", ", e.SkillCalls.Select(c => $"{c.Name}@{c.Ordinal}"))} |");
        }
        sb.AppendLine();
    }

    private static void Section7(StringBuilder sb, CalibrationReport r)
    {
        sb.AppendLine("## 7. What the pass cost");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Median, positive run, measured | {Money(r.PositiveMedianCost)} |");
        sb.AppendLine($"| Median, negative run, measured | {Money(r.NegativeMedianCost)} |");
        sb.AppendLine($"| Whole pass | {r.Cost} |");
        sb.AppendLine();

        var estimated = r.Cost.EstimatedRuns;
        if (estimated > 0)
        {
            sb.AppendLine($"**{estimated} run{(estimated == 1 ? "" : "s")} reported no cost and {(estimated == 1 ? "was" : "were")} "
                        + $"charged a flat `${RunCost.KilledRunEstimateUsd:0.00}` estimate.** A run that does not finish emits no "
                        + "`result` line, so it cannot price itself. The estimate keeps such a run visible to the "
                        + "suite-wide spend guard. It is not a measurement, and the two medians above exclude it.");
            sb.AppendLine();
        }

        sb.AppendLine("**These figures are notional.** The pass ran on a subscription, where no cash moves and runs");
        sb.AppendLine("draw on usage limits instead. The `result` line reports the same `total_cost_usd` either way,");
        sb.AppendLine("so nothing in the harness can tell them apart. Read them as a size comparison between run");
        sb.AppendLine("shapes, not as a bill. See [running-the-paid-layers.md](running-the-paid-layers.md).");
        sb.AppendLine();
    }

    private static void Caveats(StringBuilder sb, CalibrationReport r, CalibrationOutcome outcome)
    {
        sb.AppendLine("## What we could not verify");
        sb.AppendLine();
        sb.AppendLine($"- **Whether these numbers survive a model change.** Every run above was pinned to "
                      + $"`{r.Environment.Model}` on {r.Environment.CliVersion}. Nothing here says how far `p_good` drifts "
                      + "across models or CLI versions, and every gate value moves if it moves.");
        sb.AppendLine("- **Throttle detection.** A usage limit cannot be provoked on demand, so the markers the harness "
                      + "matches on are read from the shapes the CLI is known to emit, not observed here.");
        if (r.ThrottledRuns == 0)
            sb.AppendLine("  No run in this pass was detected as throttled, so the path is still unexercised.");
        if (r.ModelDriftRuns > 0)
            sb.AppendLine($"- **{r.ModelDriftRuns} run(s) reported a different model than the pin.** Treat every number above as mixed.");
        if (outcome.Stopped)
            sb.AppendLine($"- **The pass stopped early on `{outcome.Reason}`.** The sample is smaller than planned and the intervals are wider than they would otherwise be.");
        sb.AppendLine();
    }

    private static string Money(decimal? value) =>
        value is null ? "not measured" : "$" + value.Value.ToString("0.000", CultureInfo.InvariantCulture);
}
