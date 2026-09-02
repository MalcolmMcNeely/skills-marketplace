using Xunit;

namespace Harness.Model.Tests;

/// <summary>
/// Issue #8 item 2: the free half stays fast because the paying half is a SEPARATE PROJECT
/// that also refuses to run unless SKILL_HARNESS_LIVE=1. Two locks, because one is forgettable.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKILL_HARNESS_LIVE") != "1")
            Skip = "live model calls cost money; set SKILL_HARNESS_LIVE=1";
    }
}
