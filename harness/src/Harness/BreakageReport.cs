using System.Text;

namespace Harness;

public sealed record AssertionTally(int Number, string Kind, int Failed, int Seen);

/// <summary>One arm's numbers, derived from its journal. Pure, so a stopped arm still reports.</summary>
public sealed record ArmReport(
    BreakageArm Arm,
    int FiringValid,
    int FiringPassed,
    int FiringGate,
    IReadOnlyList<string> ZeroFloorBreaches,
    int ContractValid,
    int ContractHeld,
    int ContractBroken,
    IReadOnlyList<AssertionTally> Assertions,
    int VoidRuns,
    int TotalRuns,
    decimal CostUsd)
{
    public LayerOutcome Firing =>
        !Arm.RunFiring || FiringValid == 0 ? LayerOutcome.NotRun
        : FiringPassed >= FiringGate && ZeroFloorBreaches.Count == 0 ? LayerOutcome.Green
        : LayerOutcome.Red;

    /// <summary>
    /// Red on a SINGLE broken run, which is the rule layer 4 already gates on. A contract is not a
    /// rate: a skill that obeys its own body four times in five has still stopped obeying it.
    /// </summary>
    public LayerOutcome Contract =>
        ContractValid == 0 ? LayerOutcome.NotRun
        : ContractBroken > 0 ? LayerOutcome.Red
        : LayerOutcome.Green;

    public bool AsExpected => Firing == Arm.ExpectFiring && Contract == Arm.ExpectContract;

    /// <summary>The layers this arm reddened. What separation is a claim about.</summary>
    public IReadOnlyList<int> RedLayers =>
        [.. new[] { (3, Firing), (4, Contract) }.Where(x => x.Item2 == LayerOutcome.Red).Select(x => x.Item1)];

    /// <summary>A break that takes the guards down proves nothing: the run did no work to judge.</summary>
    public IReadOnlyList<AssertionTally> FailedGuards =>
        [.. Assertions.Where(a => a.Kind == nameof(AssertionKind.Guard) && a.Failed > 0)];

    public IReadOnlyList<AssertionTally> FailedSignals =>
        [.. Assertions.Where(a => a.Kind == nameof(AssertionKind.Signal) && a.Failed > 0)];

    public static ArmReport FromJournal(BreakageArm arm, string journalPath, SuiteFile suite)
    {
        var entries = RunJournal.Read(journalPath);
        var positiveIds = suite.Firing.ShouldFire.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        var firing = entries
            .Where(e => e.Layer == CalibrationPass.FiringLayer && positiveIds.Contains(e.CaseId))
            .ToList();
        var firingValid = firing.Where(e => e.Verdict != nameof(Verdict.Void)).ToList();
        var firingPassed = firingValid.Count(e => e.Verdict == nameof(Verdict.Held));

        var breaches = firingValid
            .GroupBy(e => e.CaseId, StringComparer.Ordinal)
            .Where(g => g.Count() >= Pooling.MinRunsForZeroFloor && g.All(e => e.Verdict != nameof(Verdict.Held)))
            .Select(g => g.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        var contract = entries.Where(e => e.Layer == BreakagePass.ContractLayer).ToList();
        var contractValid = contract
            .Where(e => e.Verdict is nameof(Verdict.Held) or nameof(Verdict.Broken))
            .ToList();

        var tallies = contractValid
            .SelectMany(e => e.Assertions)
            .GroupBy(a => (a.Number, a.Kind))
            .Select(g => new AssertionTally(g.Key.Number, g.Key.Kind, g.Count(a => !a.Passed), g.Count()))
            .OrderBy(t => t.Number)
            .ToList();

        return new ArmReport(
            arm,
            firingValid.Count,
            firingPassed,
            // The gate comes from #12's CALIBRATED p_good, never from this arm's own rate. A broken
            // arm scores low, and a gate derived from its own interval would move down to meet it.
            Pooling.GateK(firingValid.Count, suite.PGood),
            breaches,
            contractValid.Count,
            contractValid.Count(e => e.Verdict == nameof(Verdict.Held)),
            contractValid.Count(e => e.Verdict == nameof(Verdict.Broken)),
            tallies,
            entries.Count(e => e.Verdict == nameof(Verdict.Void)),
            entries.Count,
            entries.Sum(e => e.CostUsd ?? 0m));
    }
}

/// <summary>
/// #6 items 3 and 4, and the only place the whole question is answered. An arm at a time says
/// "this went red"; only the matrix says WHICH layer went red for WHICH break, and whether a change
/// that should move nothing moved nothing.
/// </summary>
public sealed record BreakageReport(RunEnvironment Environment, IReadOnlyList<ArmReport> Arms)
{
    public ArmReport? Arm(string id) => Arms.FirstOrDefault(a => a.Arm.Id == id);

    public bool AllArmsAsExpected => Arms.Count > 0 && Arms.All(a => a.AsExpected);

    /// <summary>
    /// Item 3. The two breaks are separable only if each reddens a DIFFERENT layer and neither
    /// reddens both. Two breaks that both read "score went down" would leave the harness unable to
    /// tell a targeting fault from a behaviour fault, which is a design flaw worth finding now.
    /// </summary>
    public bool Separates
    {
        get
        {
            var description = Arm("description");
            var body = Arm("body");
            if (description is null || body is null) return false;
            return description.Firing == LayerOutcome.Red
                && description.Contract != LayerOutcome.Red
                && body.Contract == LayerOutcome.Red
                && body.Firing != LayerOutcome.Red;
        }
    }

    /// <summary>Item 4. A harness that reddens on a reworded paragraph is noisy, not sensitive.</summary>
    public bool ControlsQuiet =>
        Arms.Where(a => a.Arm.Id is "control" or "good").All(a => a.RedLayers.Count == 0);

    /// <summary>A break must break the SIGNAL. Taking a guard down means the run did no work to judge.</summary>
    public IReadOnlyList<string> GuardCasualties =>
        [.. Arms.Where(a => a.FailedGuards.Count > 0)
            .Select(a => a.Arm.Id + ": " + string.Join(", ",
                a.FailedGuards.Select(g => $"A{g.Number} failed {g.Failed} of {g.Seen}")))];

    public decimal TotalCostUsd => Arms.Sum(a => a.CostUsd);
    public int TotalRuns => Arms.Sum(a => a.TotalRuns);
    public int VoidRuns => Arms.Sum(a => a.VoidRuns);
}

public static class BreakageMarkdown
{
    public static string Render(
        BreakageReport report, TimeSpan elapsed, IReadOnlyList<(string Arm, string Journal)> journals)
    {
        var s = new StringBuilder();
        s.AppendLine("# The two broken versions");
        s.AppendLine();
        s.AppendLine($"Issue #6. {report.Environment}. {report.TotalRuns} runs, {report.VoidRuns} void, "
                   + $"{Duration(elapsed)}, ${report.TotalCostUsd:0.00}.");
        s.AppendLine();

        s.AppendLine("## The matrix");
        s.AppendLine();
        s.AppendLine("| Arm | What changed | Layer 3 | Layer 4 | As expected |");
        s.AppendLine("|---|---|---|---|---|");
        foreach (var a in report.Arms)
        {
            var l3 = a.Firing == LayerOutcome.NotRun
                ? "not run"
                : $"{Word(a.Firing)} {a.FiringPassed}/{a.FiringValid}, gate {a.FiringGate}";
            var l4 = a.Contract == LayerOutcome.NotRun
                ? "not run"
                : $"{Word(a.Contract)} {a.ContractHeld}/{a.ContractValid} held";
            s.AppendLine($"| {Code(a.Arm.Id)} | {a.Arm.What} | {l3} | {l4} | {(a.AsExpected ? "yes" : "**NO**")} |");
        }
        s.AppendLine();

        s.AppendLine("## What #6 asked");
        s.AppendLine();
        s.AppendLine("| Question | Answer |");
        s.AppendLine("|---|---|");
        s.AppendLine($"| Layer 3 catches a description break, and layer 4 does not | {YesNo(DescriptionCaught(report))} |");
        s.AppendLine($"| Layer 4 catches a body break, and layer 3 does not | {YesNo(BodyCaught(report))} |");
        s.AppendLine($"| The two failures are distinguishable | {YesNo(report.Separates)} |");
        s.AppendLine($"| The controls stayed quiet | {YesNo(report.ControlsQuiet)} |");
        s.AppendLine();

        s.AppendLine("## Which assertion fell over");
        s.AppendLine();
        s.AppendLine("Passes out of valid contract runs, per assertion.");
        s.AppendLine();
        s.AppendLine("| Arm | Guards | Signals |");
        s.AppendLine("|---|---|---|");
        foreach (var a in report.Arms.Where(x => x.ContractValid > 0))
        {
            s.AppendLine($"| {Code(a.Arm.Id)} | {Tally(a.Assertions, nameof(AssertionKind.Guard))} "
                       + $"| {Tally(a.Assertions, nameof(AssertionKind.Signal))} |");
        }
        s.AppendLine();

        if (report.GuardCasualties.Count > 0)
        {
            s.AppendLine("**A guard fell over.** The break stopped the run doing the work the signal judges, "
                       + "so that signal result is vacuous:");
            s.AppendLine();
            foreach (var line in report.GuardCasualties) s.AppendLine($"- {line}");
            s.AppendLine();
        }

        s.AppendLine("## Journals");
        s.AppendLine();
        foreach (var (arm, path) in journals) s.AppendLine($"- {Code(arm)} — {Code(path)}");
        return s.ToString();
    }

    private static bool DescriptionCaught(BreakageReport r) =>
        r.Arm("description") is { Firing: LayerOutcome.Red, Contract: LayerOutcome.Green };

    private static bool BodyCaught(BreakageReport r) =>
        r.Arm("body") is { Contract: LayerOutcome.Red } b && b.Firing != LayerOutcome.Red;

    private static string Tally(IReadOnlyList<AssertionTally> all, string kind)
    {
        var of = all.Where(a => a.Kind == kind).ToList();
        return of.Count == 0
            ? "none recorded"
            : string.Join(", ", of.Select(a => $"A{a.Number} {a.Seen - a.Failed}/{a.Seen}"));
    }

    private static string Duration(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static string Code(string text) => "`" + text + "`";

    private static string Word(LayerOutcome o) => o switch
    {
        LayerOutcome.Green => "green",
        LayerOutcome.Red => "**RED**",
        _ => "not run",
    };

    private static string YesNo(bool b) => b ? "yes" : "**no**";
}
