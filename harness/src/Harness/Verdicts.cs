namespace Harness;

/// <summary>scoring.md's five-verdict vocabulary. Decided in this order.</summary>
public enum Verdict
{
    /// <summary>Harness broke the run. Excluded from both scores, resampled.</summary>
    Void,
    /// <summary>Valid run, expected set non-empty, nothing fired. Firing: fail. Contract: EXCLUDED.</summary>
    Missed,
    /// <summary>Something fired, but not the expected set. Firing: fail. Contract: excluded.</summary>
    WrongSet,
    /// <summary>Expected set fired and every contract assertion passed.</summary>
    Held,
    /// <summary>Expected set fired and a contract assertion failed.</summary>
    Broken,
}

public sealed record RunScore(
    Verdict Verdict,
    string Detail,
    IReadOnlyList<string> FiredSet,
    IReadOnlyList<AssertionResult> Assertions,
    decimal? CostUsd)
{
    public bool CountsForFiring => Verdict is not Verdict.Void;
    public bool FiringPassed => Verdict is Verdict.Held or Verdict.Broken;
    public bool CountsForContract => Verdict is Verdict.Held or Verdict.Broken;
    public bool ContractPassed => Verdict is Verdict.Held;
}
