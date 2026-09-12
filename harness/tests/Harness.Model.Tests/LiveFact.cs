using Xunit;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #8 item 2: the free half stays fast because the paying half is a SEPARATE PROJECT
/// that also refuses to run unless SKILL_HARNESS_LIVE=1. Two locks, because one is forgettable.
///
/// This lock is for a paid test that walks the discovered suites ITSELF, the shape both long passes
/// take so that one ledger covers every suite. A test that wants a case per suite takes
/// <see cref="LiveTheoryAttribute"/> instead.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls cost money; set SKILL_HARNESS_LIVE=1";
    }
}

/// <summary>
/// Issue #27. The same lock, for a layer that runs once per discovered suite. Adding a skill folder
/// widens the layer by being there, which a fact over one named suite could never do.
///
/// The lock is read before the rows are. A skipped theory reports as one skipped test and its data is
/// never enumerated, so nothing here reddens when a suite folder is malformed. The free gate is what
/// catches that, which is why it owns the assertion that this layer runs over every discovered suite.
/// </summary>
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls cost money; set SKILL_HARNESS_LIVE=1";
    }
}
