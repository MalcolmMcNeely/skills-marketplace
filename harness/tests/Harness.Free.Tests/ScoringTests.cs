using Harness;
using Xunit;

namespace Harness.Free.Tests;

public class ScoringTests
{
    /// <summary>Reproduces scoring.md's gate table. If this drifts, the doc and the code disagree.</summary>
    [Theory]
    [InlineData(15, 7)]
    [InlineData(30, 16)]
    [InlineData(45, 25)]
    [InlineData(60, 34)]
    [InlineData(90, 53)]
    [InlineData(180, 110)]
    public void Gate_matches_the_published_table(int n, int expected) =>
        Assert.Equal(expected, Pooling.GateK(n, 0.67));

    [Fact]
    public void Pooled_gate_and_zero_floor_are_ANDed()
    {
        // Nine healthy cases carrying one dead case: pooled rate looks fine, zero-floor catches it.
        var cases = Enumerable.Range(1, 9).Select(i => Case($"P{i}", passed: 6, valid: 6)).ToList();
        cases.Add(Case("P10", passed: 0, valid: 6));

        var pool = Pooling.Pool(cases, pGood: 0.67);
        Assert.True(pool.PooledGatePassed);
        Assert.Equal(["P10"], pool.ZeroFloorBreaches);
        Assert.False(pool.Passed);
    }

    [Fact]
    public void A_negative_case_is_graded_only_on_the_skill_under_test_staying_quiet()
    {
        // The twin overslept and ours stayed quiet. That is a PASS, not a red build.
        var outcome = Valid(fired: []);
        Assert.Equal(Verdict.Held, Scoring.ScoreQuiet(outcome, "csharp-new-class").Verdict);

        var bad = Valid(fired: ["csharp-new-class"]);
        Assert.Equal(Verdict.Broken, Scoring.ScoreQuiet(bad, "csharp-new-class").Verdict);
    }

    [Fact]
    public void A_positive_case_needs_an_exact_set_match()
    {
        var extra = Valid(fired: ["csharp-new-class", "csharp-new-test"]);
        Assert.Equal(Verdict.WrongSet, Scoring.ScoreFiring(extra, ["csharp-new-class"]).Verdict);
    }

    private static CaseResult Case(string id, int passed, int valid) => new(id,
    [
        .. Enumerable.Repeat(new RunScore(Verdict.Held, "", [], [], 0.04m), passed),
        .. Enumerable.Repeat(new RunScore(Verdict.Missed, "", [], [], 0.04m), valid - passed),
    ]);

    private static RunOutcome Valid(string[] fired) => new()
    {
        ExitCode = 0, TerminalSubtype = "success", WorkingDirectory = ".",
        Duration = TimeSpan.Zero, RawStream = "", StopMode = StopMode.Completion,
        KilledAtDecision = false, Started = true,
        Transcript = new Transcript
        {
            FiredSkills = fired, FiredSkillsRaw = fired, FileCreations = [], ShellCommands = [], SlashCommands = [], LoadedSkills = [],
            Model = null, OutputStyle = null, PermissionMode = null, ResultText = null, CostUsd = 0.04m,
        },
    };
}
