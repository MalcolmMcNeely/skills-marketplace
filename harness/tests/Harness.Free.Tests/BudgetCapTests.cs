using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #18. Layer 3 carried a cap of its own at $0.40, under the $0.60 per-run ceiling #11 fixed
/// for the whole harness, and three passes measured that too tight. The figures are in
/// breakage.md section 7 and calibration.md. The two caps are one number now, and this is what holds
/// them there. Raising it changes which runs are VOID, not what a valid run does, so no gate moves.
/// </summary>
public class BudgetCapTests
{
    private static readonly HarnessPaths Paths = new();

    [Fact]
    public void A_layer_3_firing_run_is_capped_at_the_per_run_ceiling()
    {
        var spec = new FiringRunner(Paths, UnderTest.CsharpNewClass).SpecFor("build me a thing", CaseKind.ShouldFire);
        Assert.Equal(RunSpec.PerRunCeilingUsd, spec.MaxBudgetUsd);
    }

    /// <summary>#17 gave the kind one thing to decide, and money is not it.</summary>
    [Fact]
    public void No_case_kind_carries_a_cap_of_its_own()
    {
        var runner = new FiringRunner(Paths, UnderTest.CsharpNewClass);
        foreach (var kind in Enum.GetValues<CaseKind>())
            Assert.Equal(RunSpec.PerRunCeilingUsd, runner.SpecFor("build me a thing", kind).MaxBudgetUsd);
    }

    /// <summary>A run shape that says nothing about money still gets the ceiling.</summary>
    [Fact]
    public void A_run_that_names_no_cap_takes_the_ceiling()
    {
        Assert.Equal(RunSpec.PerRunCeilingUsd, new RunSpec { Prompt = "p", WorkingDirectory = "." }.MaxBudgetUsd);
    }

    /// <summary>
    /// The figure itself, pinned. This does not read running-the-paid-layers.md, so it cannot catch
    /// that document drifting on its own. It makes the number impossible to move by accident. A
    /// deliberate change has to come through here, and here is where the document is remembered.
    /// </summary>
    [Fact]
    public void The_per_run_ceiling_is_the_figure_number_11_fixed()
    {
        Assert.Equal(0.60m, RunSpec.PerRunCeilingUsd);
    }
}
