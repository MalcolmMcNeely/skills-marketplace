namespace Harness;

/// <summary>
/// A suite-wide running total with a hard stop. Per-run --max-budget-usd does not bound a SUITE:
/// ten void runs at $0.23 cost $2.28 before the resample cap fired, because each one was individually
/// within budget. Measured, on this ticket.
///
/// Only works when runs are allowed to finish, because a killed run emits no result line and
/// therefore reports no cost.
/// </summary>
public sealed class SpendLedger(decimal ceilingUsd)
{
    private readonly Lock _gate = new();
    private decimal _spent;

    public decimal CeilingUsd { get; } = ceilingUsd;
    public decimal Spent { get { lock (_gate) return _spent; } }
    public bool Exhausted => Spent >= CeilingUsd;

    public void Record(RunScore score)
    {
        lock (_gate) _spent += score.CostUsd ?? 0m;
    }

    public string Report() => $"${Spent:0.00} of ${CeilingUsd:0.00}";
}
