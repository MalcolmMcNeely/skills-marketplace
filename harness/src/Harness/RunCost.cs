using System.Globalization;

namespace Harness;

/// <summary>Where a cost figure came from. A figure is never quoted without one.</summary>
public enum CostBasis
{
    /// <summary>The run's result line reported <c>total_cost_usd</c>.</summary>
    Measured,
    /// <summary>The run emitted no result line, so the fixed estimate stands in.</summary>
    Estimated,
}

/// <summary>
/// Issue #16. What one run charges the ledger, and which of the two kinds of figure that is.
///
/// A run killed before it finishes emits no result line and therefore reports no cost. Charging it
/// zero would take roughly forty per cent of a pass out of the runaway guard's view, and a resample
/// loop of killed runs could then go on forever inside a ledger that thinks it has spent nothing.
///
/// A run that reports nothing because it was REFUSED really did bill nothing, and this over-charges
/// it. That is the direction to be wrong in: the ledger is a guard, and a guard that under-counts
/// does not fire. A refusal stops the pass on the first run anyway, so it is charged once.
/// </summary>
public readonly record struct RunCost(decimal Usd, CostBasis Basis)
{
    /// <summary>
    /// MEASURED, on #8: ten void runs cost `$2.28` between them before the resample cap fired, so
    /// `$0.23` each. The nearest measured figure to what a run that reported nothing actually spent.
    /// </summary>
    public const decimal KilledRunEstimateUsd = 0.23m;

    public static readonly RunCost Estimate = new(KilledRunEstimateUsd, CostBasis.Estimated);

    /// <summary>What goes in the journal's costBasis. Lower case, because a journal is read by eye.</summary>
    public const string EstimatedLabel = "estimated";
    public const string MeasuredLabel = "measured";

    /// <summary>
    /// The one place a missing figure becomes a number.
    ///
    /// A REPORTED zero keeps its basis, and that is where this rule and <see cref="Throttle.Refused"/>
    /// deliberately part company. Throttle asks whether the run did any work, and null and zero both
    /// answer no. This asks whether the run said what it spent, and zero is an answer. The guard hole
    /// cannot open at zero either way: the only shape known to report a zero is a refusal, and a
    /// refusal stops the pass rather than being resampled.
    /// </summary>
    public static RunCost Of(decimal? reported) =>
        reported is { } usd ? new RunCost(usd, CostBasis.Measured) : Estimate;

    /// <summary>
    /// Reads a journal entry's two cost fields back. An entry written before #16 carries no basis:
    /// a figure on it was measured, and a missing figure was a run that reported nothing, which is
    /// exactly what the estimate is for.
    /// </summary>
    public static RunCost FromJournal(decimal? usd, string? basis) =>
        string.Equals(basis, EstimatedLabel, StringComparison.OrdinalIgnoreCase)
            ? new RunCost(usd ?? KilledRunEstimateUsd, CostBasis.Estimated)
            : Of(usd);

    public bool IsEstimated => Basis is CostBasis.Estimated;

    public string Label => IsEstimated ? EstimatedLabel : MeasuredLabel;

    /// <summary>Both bases are named. An unmarked figure would need the reader to know the convention.</summary>
    public override string ToString() =>
        "$" + Usd.ToString("0.000", CultureInfo.InvariantCulture) + " " + Label;
}

/// <summary>
/// A sum of run costs that keeps the two bases apart, so nothing can quote a blended figure as if
/// the whole of it had been billed.
/// </summary>
public readonly record struct SpendTotal(decimal MeasuredUsd, decimal EstimatedUsd, int EstimatedRuns)
{
    public static SpendTotal Zero => default;

    public decimal TotalUsd => MeasuredUsd + EstimatedUsd;

    public SpendTotal Plus(RunCost cost) => cost.IsEstimated
        ? this with { EstimatedUsd = EstimatedUsd + cost.Usd, EstimatedRuns = EstimatedRuns + 1 }
        : this with { MeasuredUsd = MeasuredUsd + cost.Usd };

    public SpendTotal Plus(SpendTotal other) => new(
        MeasuredUsd + other.MeasuredUsd,
        EstimatedUsd + other.EstimatedUsd,
        EstimatedRuns + other.EstimatedRuns);

    public static SpendTotal Of(IEnumerable<RunCost> costs) =>
        costs.Aggregate(Zero, (total, cost) => total.Plus(cost));

    public static SpendTotal Of(IEnumerable<SpendTotal> totals) =>
        totals.Aggregate(Zero, (running, total) => running.Plus(total));

    /// <summary>How much of this total was guessed. Reads alone and inside a longer line.</summary>
    public string BasisNote => EstimatedRuns == 0
        ? "all measured"
        : $"{Money(EstimatedUsd)} of it estimated over {EstimatedRuns} run{(EstimatedRuns == 1 ? "" : "s")}";

    public override string ToString() => $"{Money(TotalUsd)}, {BasisNote}";

    private static string Money(decimal value) =>
        "$" + value.ToString("0.00", CultureInfo.InvariantCulture);
}
