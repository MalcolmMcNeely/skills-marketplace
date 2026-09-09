namespace Harness;

/// <summary>
/// A suite-wide running total with a hard stop. Per-run --max-budget-usd does not bound a SUITE:
/// ten void runs at $0.23 cost $2.28 before the resample cap fired, because each one was individually
/// within budget. Measured, on this ticket.
///
/// #16: a run that reports no cost is charged <see cref="RunCost.KilledRunEstimateUsd"/> rather than
/// zero, so the guard still sees a resample loop made of killed runs. The two kinds of figure are
/// kept apart all the way to <see cref="Report"/>, because a guard may spend an estimate but a report
/// may not quote one as a bill.
/// </summary>
public sealed class SpendLedger(decimal ceilingUsd)
{
    private readonly Lock _gate = new();
    private SpendTotal _total;

    public decimal CeilingUsd { get; } = ceilingUsd;
    public SpendTotal Total { get { lock (_gate) return _total; } }
    public decimal Spent => Total.TotalUsd;
    public bool Exhausted => Spent >= CeilingUsd;

    public void Record(RunScore score)
    {
        lock (_gate) _total = _total.Plus(score.Cost);
    }

    public string Report() => $"${Spent:0.00} of ${CeilingUsd:0.00}, {Total.BasisNote}";
}
