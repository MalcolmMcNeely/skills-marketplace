namespace Harness;

/// <summary>What a layer said about an arm. NotRun is a real answer and must not read as green.</summary>
public enum LayerOutcome { NotRun, Green, Red }

/// <summary>
/// Issue #6, one arm of the differential. An arm is a fixture plus what each layer should say about it.
///
/// The expectation is declared HERE, next to the break, rather than asserted in a test. #6 item 3 asks
/// whether the two failures are DISTINGUISHABLE, and that is a claim about the whole matrix: it cannot
/// be checked one arm at a time. Writing the expectation down makes the matrix the thing under test.
/// </summary>
public sealed record BreakageArm
{
    public required string Id { get; init; }
    public required string What { get; init; }

    /// <summary>
    /// An overlay identifier, named relative to the suite that owns it and resolved through
    /// <see cref="DiscoveredSuite.BreakOverlay"/>. Laid over shared/catalogue for layer 3; null runs
    /// the unbroken catalogue. #26 made these relative so a second skill can declare its own breaks
    /// without editing this plan.
    /// </summary>
    public string? FiringOverlay { get; init; }
    public FiringPlanShape FiringShape { get; init; } = FiringPlanShape.PositivesOnly;
    public bool RunFiring { get; init; } = true;

    /// <summary>Laid over the suite's own plugin for layer 4. Null runs the unbroken plugin.</summary>
    public string? ContractOverlay { get; init; }
    public int ContractRuns { get; init; } = 5;
    public int ContractCap { get; init; } = 10;

    public required LayerOutcome ExpectFiring { get; init; }
    public required LayerOutcome ExpectContract { get; init; }

    /// <summary>
    /// The four arms, in the order they run. Cheapest confirmations first would be nicer, but a
    /// usage limit ends a pass where it stands, so the two arms that answer #6 go before the controls.
    /// </summary>
    public static IReadOnlyList<BreakageArm> Plan =>
    [
        new()
        {
            Id = "description",
            What = "description broken, body correct",
            FiringOverlay = "description/catalogue",
            FiringShape = FiringPlanShape.PositivesOnly,
            ContractOverlay = "description/plugin",
            ExpectFiring = LayerOutcome.Red,
            ExpectContract = LayerOutcome.Green,
        },
        new()
        {
            Id = "body",
            What = "body broken, description correct",
            // The same overlay serves layer 3. Its description is byte-identical to the good one, so
            // layer 3 must not move; six runs is enough to show a non-event, and an hour is not worth
            // spending to confirm one at full width.
            FiringOverlay = "body/plugin",
            FiringShape = FiringPlanShape.ShortPositives,
            ContractOverlay = "body/plugin",
            ContractRuns = 8,
            ContractCap = 14,
            ExpectFiring = LayerOutcome.Green,
            ExpectContract = LayerOutcome.Red,
        },
        new()
        {
            Id = "control",
            What = "prose reworded, both rules intact",
            FiringOverlay = "control/catalogue",
            FiringShape = FiringPlanShape.PositivesOnly,
            ContractOverlay = "control/plugin",
            ExpectFiring = LayerOutcome.Green,
            ExpectContract = LayerOutcome.Green,
        },
        new()
        {
            Id = "good",
            What = "the frozen fixture, unchanged",
            // Layer 3 is not re-run. #12 measured it at 60 of 60 six days ago and every gate on the
            // map rests on that journal. The control arm above re-measures the same description on
            // 60 fresh runs, which is the same-session replication a re-run would have bought.
            RunFiring = false,
            ContractOverlay = null,
            ExpectFiring = LayerOutcome.NotRun,
            ExpectContract = LayerOutcome.Green,
        },
    ];
}

/// <summary>
/// Runs one arm: the firing half against an overlaid catalogue, then the contract half.
///
/// #26. It takes the DISCOVERED suite, not the parsed file, because an arm names its overlays and its
/// base plugin relative to the suite that owns them. Handed a bare file, the pass would have to know
/// where that suite's material sits, and the plan would then hold paths only one skill could satisfy.
/// </summary>
public sealed class BreakagePass(HarnessPaths paths, DiscoveredSuite suite)
{
    public const int ContractLayer = 4;

    /// <summary>The case data. The suite itself is the folder around it, and the pass needs both.</summary>
    private SuiteFile Cases => suite.Suite;

    public async Task<CalibrationOutcome> RunArmAsync(
        BreakageArm arm, RunJournal journal, SpendLedger ledger,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var builder = new FixtureBuilder(paths);
        var started = DateTimeOffset.UtcNow;
        log?.Invoke($"arm {arm.Id}: {arm.What}");

        if (arm.RunFiring)
        {
            var catalogue = builder.Build(paths.StubCatalogue, Overlay(arm.FiringOverlay));
            var pass = new CalibrationPass(paths, Cases, new FiringRunner(paths, catalogue), arm.FiringShape);
            var firing = await pass.RunAsync(journal, ledger, log, ct);
            if (firing.Stopped) return firing with { Started = started };
        }
        else log?.Invoke($"{arm.Id,-12} layer 3 not run by design");

        var contract = await RunContractAsync(arm, builder, journal, ledger, log, ct);
        return contract with { Started = started };
    }

    private async Task<CalibrationOutcome> RunContractAsync(
        BreakageArm arm, FixtureBuilder builder, RunJournal journal, SpendLedger ledger,
        Action<string>? log, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var plugin = builder.Build(suite.Plugin, Overlay(arm.ContractOverlay));
        var runner = new ContractRunner(paths);

        foreach (var c in Cases.Contract)
        {
            var assertions = AssertionCatalogue.Resolve(c);
            var done = CalibrationPass.ValidRunsByCase(journal.Path).GetValueOrDefault(c.Id);
            if (done >= arm.ContractRuns)
            {
                log?.Invoke($"{c.Id,-4} skipped, {done} valid runs already journalled");
                continue;
            }

            var sample = await Resampler.CollectAsync(arm.ContractRuns - done, arm.ContractCap, async token =>
            {
                var outcome = await runner.RunAsync(Cases.SkillUnderTest, c.Task, plugin, token);
                var score = Scoring.ScoreContract(outcome, Cases.SkillUnderTest, assertions);
                journal.Append(c.Id, ContractLayer, outcome, score);
                return score;
            }, ledger, ct);

            log?.Invoke($"{c.Id,-4} {sample.Valid}/{arm.ContractRuns - done} valid, {ledger.Report()}, {sample.Failure ?? "ok"}");

            if (sample.Failure is "throttled" or "suite-budget-exhausted")
                return new CalibrationOutcome(true, sample.Failure, started, DateTimeOffset.UtcNow);
        }

        return new CalibrationOutcome(false, null, started, DateTimeOffset.UtcNow);
    }

    private string? Overlay(string? name) => name is null ? null : suite.BreakOverlay(name);
}
