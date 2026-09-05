using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>Offline. No model calls. These run in milliseconds and gate every PR.</summary>
public class ParserTests
{
    private static string Captured(string name) =>
        File.ReadAllText(Path.Combine(new HarnessPaths().Captured, name));

    [Fact]
    public void Records_every_skill_invocation_not_just_the_first()
    {
        var (t, _) = StreamParser.Parse(Captured("two-skills-fired.jsonl"));
        Assert.Equal(["csharp-new-class", "data-sql"], t.FiredSkills);
    }

    [Fact]
    public void A_budget_abort_is_void_and_never_reads_as_a_miss()
    {
        var stream = Captured("budget-abort.jsonl");
        var (t, subtype) = StreamParser.Parse(stream);
        Assert.Empty(t.FiredSkills);            // a naive parser stops here and calls it a miss
        Assert.Equal("error_max_budget_usd", subtype);

        var outcome = Outcome(stream, exit: 1, subtype, StopMode.Completion);
        Assert.False(outcome.TryGetValid(out _));

        var score = Scoring.ScoreFiring(outcome, ["csharp-new-class"]);
        Assert.Equal(Verdict.Void, score.Verdict);   // NOT Verdict.Missed
    }

    [Fact]
    public void A_real_miss_on_a_valid_run_is_scored_as_a_miss()
    {
        var stream = Captured("nothing-fired-success.jsonl");
        var (_, subtype) = StreamParser.Parse(stream);
        var outcome = Outcome(stream, exit: 0, subtype, StopMode.Completion);

        Assert.Equal(Verdict.Missed, Scoring.ScoreFiring(outcome, ["csharp-new-class"]).Verdict);
    }

    [Fact]
    public void Ordering_survives_a_bash_heredoc()
    {
        var (t, _) = StreamParser.Parse(Captured("heredoc-both-files.jsonl"));
        Assert.Equal(CreationRoute.Bash, t.FileCreations[0].Route);
        // Both files land in one command, and the order inside the command string is still readable.
        Assert.True(t.FirstCreationOrdinal("src/Discount.cs") <= t.FirstCreationOrdinal("tests/DiscountTests.cs"));
    }

    [Fact]
    public void An_early_stop_that_reached_no_tool_decision_is_void()
    {
        // run_eval.py's defect: its timeout path returns "did not trigger".
        var outcome = new RunOutcome
        {
            ExitCode = -1, TerminalSubtype = null, Transcript = Empty(),
            WorkingDirectory = ".", Duration = TimeSpan.Zero, RawStream = "",
            StopMode = StopMode.FirstDecision, KilledAtDecision = false, Started = true,
        };
        Assert.Equal(Verdict.Void, Scoring.ScoreFiring(outcome, ["csharp-new-class"]).Verdict);
    }

    private static RunOutcome Outcome(string stream, int exit, string? subtype, StopMode mode)
    {
        var (t, _) = StreamParser.Parse(stream);
        return new RunOutcome
        {
            ExitCode = exit, TerminalSubtype = subtype, Transcript = t,
            WorkingDirectory = ".", Duration = TimeSpan.Zero, RawStream = stream,
            StopMode = mode, KilledAtDecision = false, Started = true,
        };
    }

    private static Transcript Empty() => new()
    {
        FiredSkills = [], FiredSkillsRaw = [], FileCreations = [], ShellCommands = [], SlashCommands = [], LoadedSkills = [],
        Model = null, OutputStyle = null, PermissionMode = null, ResultText = null, CostUsd = null,
    };
}

/// <summary>Parsed from a REAL run captured on this machine, not a synthetic stream.</summary>
public class RealCaptureTests
{
    private static readonly string Stream =
        File.ReadAllText(Path.Combine(new HarnessPaths().Captured, "real-layer4-held.jsonl"));

    [Fact]
    public void The_plugin_prefix_is_stripped_so_a_case_never_names_the_fixture()
    {
        var (t, _) = StreamParser.Parse(Stream);
        Assert.Equal(["harness-fixture-good:csharp-new-class"], t.FiredSkillsRaw);
        Assert.Equal(["csharp-new-class"], t.FiredSkills);
    }

    [Fact]
    public void Dotnet_test_in_the_skill_body_is_not_mistaken_for_running_it()
    {
        var (t, _) = StreamParser.Parse(Stream);
        Assert.Contains("dotnet test", Stream);                 // it IS in the stream, twice, as prose
        Assert.DoesNotContain(t.ShellCommands, c => c.Contains("dotnet test"));
    }

    [Fact]
    public void The_good_fixture_scores_Held_on_this_run()
    {
        var (t, subtype) = StreamParser.Parse(Stream);
        var workDir = Path.GetDirectoryName(t.FileCreations[0].Path)!;   // tests/ ... rebuilt below
        var outcome = new RunOutcome
        {
            ExitCode = 0, TerminalSubtype = subtype, Transcript = t,
            WorkingDirectory = workDir, Duration = TimeSpan.Zero, RawStream = Stream,
            StopMode = StopMode.Completion, KilledAtDecision = false, Started = true,
        };
        Assert.Equal("success", subtype);
        Assert.True(outcome.TryGetValid(out var run));

        // Transcript-only assertions: 6 (ordering) and 7 (no dotnet test). Disk guards are checked live.
        var results = new TestFirstFilesOnly("Discount").Evaluate(run!);
        Assert.True(results.Single(r => r.Number == 6).Passed, "test file written first");
        Assert.True(results.Single(r => r.Number == 7).Passed, "did not run dotnet test");
    }
}

/// <summary>
/// A REAL by-name run in which the CLI expanded the slash command inline and emitted NO Skill tool_use.
/// The body still reached the model and the rule was still obeyed. Gating layer 4 on the fired set
/// would have thrown this healthy run away.
/// </summary>
public class ByNameWithoutASkillCallTests
{
    private static readonly string Stream =
        File.ReadAllText(Path.Combine(new HarnessPaths().Captured, "real-layer4-no-skill-call.jsonl"));

    [Fact]
    public void No_Skill_tool_use_appears_but_the_fixture_did_load()
    {
        var (t, _) = StreamParser.Parse(Stream);
        Assert.Empty(t.FiredSkills);
        Assert.True(t.SkillIsAvailable("csharp-new-class"));
        Assert.Contains("harness-fixture-good:csharp-new-class", t.SlashCommands);
    }

    [Fact]
    public void It_scores_Held_on_the_two_signal_assertions()
    {
        var (t, subtype) = StreamParser.Parse(Stream);
        var outcome = new RunOutcome
        {
            ExitCode = 0, TerminalSubtype = subtype, Transcript = t,
            WorkingDirectory = ".", Duration = TimeSpan.Zero, RawStream = Stream,
            StopMode = StopMode.Completion, KilledAtDecision = false, Started = true,
        };
        Assert.True(outcome.TryGetValid(out var run));
        var results = new TestFirstFilesOnly("Discount").Evaluate(run!);
        Assert.True(results.Single(r => r.Number == 6).Passed, "test file written first");
        Assert.True(results.Single(r => r.Number == 7).Passed, "did not run dotnet test");
    }
}
