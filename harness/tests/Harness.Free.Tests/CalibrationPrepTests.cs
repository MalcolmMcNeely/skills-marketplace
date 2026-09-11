using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// The three things #12 asked for before the pass runs: a pinned model, a decided answer for a
/// throttled run, and a journal that survives a pass being cut short.
/// </summary>
public class ThrottleTests
{
    [Fact]
    public void A_failed_run_whose_error_names_a_limit_is_throttled()
    {
        Assert.True(Throttle.Detect(isValid: false, "Claude AI usage limit reached", null));
        Assert.True(Throttle.Detect(isValid: false, null, "API Error: 429 rate_limit_error"));
    }

    [Fact]
    public void A_successful_run_is_never_throttled()
    {
        // Belt and braces: the words come from the CLI, but a success is a result whatever it says.
        Assert.False(Throttle.Detect(isValid: true, "explained the 429 rate limit to the user", null));
    }

    [Fact]
    public void The_model_cannot_throttle_its_own_pass()
    {
        // The transcript is model-authored. A failed run whose only limit words are in the model's
        // output is a plain void run, because "429" appears in ordinary code.
        var outcome = Fake.Outcome(exit: 1, subtype: "error_during_execution",
            stream: """{"type":"assistant","message":{"content":[{"type":"text","text":"return 429 rate limit"}]}}""");
        Assert.False(outcome.Throttled);
    }

    /// <summary>
    /// The wall #6 actually hit, on 9 September 2026 at 11:21 UTC. Exit 1 with subtype "success",
    /// $0.00, 1.7 seconds, and no words anywhere naming a limit. The old guard read the subtype,
    /// saw "success", and returned false before looking at stderr, so 89 runs were resampled into it.
    /// </summary>
    [Fact]
    public void A_wall_that_names_itself_nowhere_is_still_caught_by_its_shape()
    {
        // No marker text, so the words find nothing.
        Assert.False(Throttle.Detect(isValid: false, null, null));
        // The shape finds it anyway: no result, nothing billed, back in under two seconds.
        Assert.True(Throttle.Refused(isValid: false, costUsd: 0m, TimeSpan.FromSeconds(1.7)));
    }

    /// <summary>Exit 1 and subtype "success" together. Not valid, whatever the subtype says.</summary>
    [Fact]
    public void Subtype_success_with_a_failed_exit_code_is_not_a_result()
    {
        var outcome = Fake.Outcome(exit: 1, subtype: "success");

        Assert.False(outcome.IsValid);
        Assert.True(Throttle.Detect(outcome.IsValid, "Claude AI usage limit reached", null),
            "the old guard returned false here on the subtype alone");
    }

    [Fact]
    public void A_real_run_is_never_refused_by_shape()
    {
        // Valid runs are excluded outright, and so is any run that billed or took real time.
        Assert.False(Throttle.Refused(isValid: true, costUsd: 0m, TimeSpan.FromSeconds(1.7)));
        Assert.False(Throttle.Refused(isValid: false, costUsd: 0.21m, TimeSpan.FromSeconds(1.7)));
        Assert.False(Throttle.Refused(isValid: false, costUsd: 0m, TimeSpan.FromSeconds(40)));
    }

    /// <summary>A shape refusal has no words behind it, so the journal must not invent any.</summary>
    [Fact]
    public void An_inferred_refusal_is_not_reported_as_a_named_limit()
    {
        var refused = Fake.Outcome(exit: 1, subtype: "success", seconds: 1.7);

        Assert.True(refused.Throttled);
        Assert.StartsWith("REFUSED:", refused.VoidReason, StringComparison.Ordinal);
        Assert.DoesNotContain("usage limit", refused.VoidReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_budget_abort_is_not_a_throttle()
    {
        Assert.False(Throttle.Detect(isValid: false, "run exceeded --max-budget-usd", null));
        // And not by shape either: a budget abort billed for the work it did before the cut-off.
        Assert.False(Throttle.Refused(isValid: false, costUsd: 0.40m, TimeSpan.FromSeconds(48)));
    }
}

public class ResamplerThrottleTests
{
    [Fact]
    public async Task A_throttled_run_stops_the_sample_instead_of_burning_the_cap()
    {
        var attempts = 0;
        var sample = await Resampler.CollectAsync(6, 12, _ =>
        {
            attempts++;
            return Task.FromResult(new RunScore(Verdict.Void, "THROTTLED", [], [], RunCost.Of(null), Throttled: true));
        });

        Assert.Equal(1, attempts);
        Assert.Equal("throttled", sample.Failure);
    }

    [Fact]
    public void Throttled_beats_the_other_failures_in_the_report()
    {
        // A limit that also empties the ledger must still read as a limit, not as a spend problem.
        var sample = new Sample([], CapHit: true, BudgetExhausted: true, Throttled: true);
        Assert.Equal("throttled", sample.Failure);
    }

    [Fact]
    public async Task An_ordinary_void_run_is_still_resampled()
    {
        var attempts = 0;
        var sample = await Resampler.CollectAsync(2, 5, _ =>
        {
            attempts++;
            return Task.FromResult(attempts < 3
                ? new RunScore(Verdict.Void, "exit=1", [], [], RunCost.Of(null))
                : new RunScore(Verdict.Held, "set matched", [], [], RunCost.Of(0.2m)));
        });

        Assert.Null(sample.Failure);
        Assert.Equal(4, attempts);
    }
}

public class ModelPinTests
{
    [Fact]
    public void A_run_spec_is_pinned_by_default()
    {
        var spec = new RunSpec { Prompt = "x", WorkingDirectory = "." };
        Assert.False(string.IsNullOrWhiteSpace(spec.Model));
    }

    [Fact]
    public void Every_figure_on_this_map_was_taken_on_the_default()
    {
        Assert.Equal("claude-opus-5[1m]", RunEnvironment.DefaultModel);
    }

    [Fact]
    public void A_run_that_resolved_to_another_model_is_flagged()
    {
        Assert.False(Fake.Outcome(model: "claude-sonnet-5", requested: "claude-opus-5[1m]").ModelHeld);
        Assert.True(Fake.Outcome(model: "claude-opus-5[1m]", requested: "claude-opus-5[1m]").ModelHeld);
    }
}

public class SkillCallOrdinalTests
{
    private static readonly HarnessPaths Paths = new();

    [Fact]
    public void Every_skill_call_keeps_its_position_in_the_run()
    {
        var stream = File.ReadAllText(Paths.Stream("two-skills-fired.jsonl"));
        var (t, _) = StreamParser.Parse(stream);

        // MEASURED against this capture: three Skill calls, two distinct skills, the third a repeat
        // of the first. FiredSkills deduplicates and SkillCalls does not, which is the difference
        // #12 output 6 needs: a repeat late in the run is invisible in the set.
        Assert.Equal(3, t.SkillCalls.Count);
        Assert.Equal(2, t.FiredSkills.Count);
        Assert.Equal(["csharp-new-class", "data-sql", "csharp-new-class"], t.SkillCalls.Select(c => c.Name));
        Assert.True(t.SkillCalls.Zip(t.SkillCalls.Skip(1)).All(p => p.First.Ordinal < p.Second.Ordinal));
    }

    [Fact]
    public void A_skill_that_fired_first_has_nothing_before_it()
    {
        var stream = File.ReadAllText(Paths.Stream("two-skills-fired.jsonl"));
        var (t, _) = StreamParser.Parse(stream);

        Assert.Equal(0, t.SkillsFiredBefore(t.FiredSkills[0]));
        Assert.Equal(1, t.SkillsFiredBefore(t.FiredSkills[1]));
        Assert.Null(t.SkillsFiredBefore("never-fired"));
    }
}

public class WilsonTests
{
    [Fact]
    public void It_reproduces_the_published_baseline_bound()
    {
        // baseline-test-first.md: test written first in 0 of 15 runs, Wilson upper bound 0.204.
        var (low, high) = Pooling.Wilson(0, 15);
        Assert.Equal(0, low, 3);
        Assert.Equal(0.204, high, 3);
    }

    [Fact]
    public void A_rate_at_the_top_of_the_range_still_has_a_bound_below_one()
    {
        var (low, high) = Pooling.Wilson(60, 60);
        Assert.True(low is > 0.9 and < 1.0);
        Assert.Equal(1.0, high, 6);
    }

    [Fact]
    public void No_runs_means_no_information()
    {
        Assert.Equal((0.0, 1.0), Pooling.Wilson(0, 0));
    }
}

public class JournalTests
{
    [Fact]
    public void A_run_is_on_disk_before_the_next_one_starts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                journal.Append("P1", 3, Fake.Outcome(skills: [("csharp-new-class", 4)]),
                    new RunScore(Verdict.Held, "set matched", ["csharp-new-class"], [], RunCost.Of(0.196m)));

                // Readable while the writer is still open, which is the whole point.
                var midPass = RunJournal.Read(path);
                Assert.Single(midPass);
                Assert.Equal("P1", midPass[0].CaseId);
                Assert.Equal(4, midPass[0].SkillCalls[0].Ordinal);
                Assert.Equal(0.196m, midPass[0].CostUsd);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_torn_last_line_does_not_lose_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.1m)));
            File.AppendAllText(path, "{\"case\":\"P2\",\"verd");

            Assert.Single(RunJournal.Read(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_journal_reads_as_empty_rather_than_throwing()
    {
        Assert.Empty(RunJournal.Read(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.jsonl")));
    }
}

public class CalibrationResumeTests
{
    [Fact]
    public void Void_runs_do_not_count_towards_a_case_being_done()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.1m)));
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Void, "exit=1", [], [], RunCost.Of(null)));
                journal.Append("P2", 3, Fake.Outcome(), new RunScore(Verdict.Missed, "nothing fired", [], [], RunCost.Of(0.1m)));
            }

            var done = CalibrationPass.ValidRunsByCase(path);
            Assert.Equal(1, done["P1"]);
            Assert.Equal(1, done["P2"]);
        }
        finally { File.Delete(path); }
    }
}

public class CalibrationReportTests
{
    private static readonly HarnessPaths Paths = new();

    private static SuiteFile Suite => SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));

    [Fact]
    public void P_good_pools_only_the_should_fire_runs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                for (var i = 0; i < 4; i++)
                    journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.2m)));
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Missed, "nothing fired", [], [], RunCost.Of(0.2m)));
                // A negative that stayed quiet must not inflate p_good.
                journal.Append("N1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "quiet", [], [], RunCost.Of(0.3m)));
            }

            var report = CalibrationReport.FromJournal(path, Suite);
            Assert.Equal(5, report.PositiveValid);
            Assert.Equal(4, report.PositivePassed);
            Assert.Equal(0.8, report.PGood, 6);
            Assert.Equal(0, report.FalseFires);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// #12's one judgement call. A perfect pass must NOT set a perfect gate, or a single flaky run
    /// reddens the build. The gate comes from the lower bound of the interval, never from PGood.
    /// </summary>
    [Fact]
    public void A_perfect_pass_does_not_set_a_perfect_gate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                for (var i = 0; i < 60; i++)
                    journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.2m)));
            }

            var report = CalibrationReport.FromJournal(path, Suite);
            Assert.Equal(1.0, report.PGood, 6);
            Assert.Equal(0.940, report.PGoodInterval.Low, 3);

            // The point estimate would give 60 of 60. The lower bound gives 53, which survives a flake.
            Assert.Equal(60, Pooling.GateK(60, report.PGood));
            Assert.Equal(53, report.GateK(60));
            Assert.Equal(8, report.GateK(10));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_two_cost_medians_are_kept_apart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.196m)));
                journal.Append("N1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "quiet", [], [], RunCost.Of(0.400m)));
            }

            var report = CalibrationReport.FromJournal(path, Suite);
            Assert.Equal(0.196m, report.PositiveMedianCost);
            Assert.Equal(0.400m, report.NegativeMedianCost);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_skill_firing_after_a_decoy_makes_the_early_stop_rule_unsafe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                var late = Fake.Outcome(skills: [("csharp-modify-class", 2), ("csharp-new-class", 9)]);
                journal.Append("N2", 3, late, new RunScore(Verdict.Broken, "csharp-new-class FIRED", [], [], RunCost.Of(0.3m)));
            }

            var report = CalibrationReport.FromJournal(path, Suite);
            Assert.False(report.FirstDecisionSafeForNegatives);
            Assert.Single(report.LateFires);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_skill_firing_first_leaves_the_early_stop_rule_safe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
            {
                var early = Fake.Outcome(skills: [("csharp-new-class", 2), ("csharp-modify-class", 9)]);
                journal.Append("N2", 3, early, new RunScore(Verdict.Broken, "csharp-new-class FIRED", [], [], RunCost.Of(0.3m)));
            }

            Assert.True(CalibrationReport.FromJournal(path, Suite).FirstDecisionSafeForNegatives);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_report_renders_from_a_partial_pass()
    {
        var path = Path.Combine(Path.GetTempPath(), $"journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new RunJournal(path))
                journal.Append("P1", 3, Fake.Outcome(), new RunScore(Verdict.Held, "ok", [], [], RunCost.Of(0.2m)));

            var stopped = new CalibrationOutcome(true, "throttled", DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow);
            var markdown = CalibrationMarkdown.Render(CalibrationReport.FromJournal(path, Suite), stopped, Suite, path);

            Assert.Contains("This pass did not finish", markdown);
            Assert.Contains("What we could not verify", markdown);
            Assert.Contains(RunEnvironment.DefaultModel, markdown);
        }
        finally { File.Delete(path); }
    }
}

internal static class Fake
{
    public static RunOutcome Outcome(
        int exit = 0,
        string? subtype = "success",
        string stream = "",
        string? model = null,
        string? requested = null,
        (string Name, int Ordinal)[]? skills = null,
        double seconds = 40,
        decimal? cost = null,
        StopMode stop = StopMode.Completion,
        bool killed = false)
    {
        var (parsed, parsedSubtype) = StreamParser.Parse(stream);
        var calls = skills ?? [];
        var transcript = parsed with
        {
            Model = model,
            CostUsd = cost ?? parsed.CostUsd,
            SkillCalls = [.. calls.Select(s => new SkillCall(s.Name, s.Name, s.Ordinal))],
            FiredSkills = [.. calls.Select(s => s.Name).Distinct(StringComparer.Ordinal)],
        };

        var outcome = new RunOutcome
        {
            ExitCode = exit,
            TerminalSubtype = stream.Length > 0 ? parsedSubtype : subtype,
            Transcript = transcript,
            WorkingDirectory = ".",
            Duration = TimeSpan.FromSeconds(seconds),
            RawStream = stream,
            StopMode = stop,
            KilledAtDecision = killed,
            Started = true,
            RequestedModel = requested,
        };

        return outcome with
        {
            Throttled = Throttle.Detect(outcome.IsValid, transcript.ResultText, null)
                     || Throttle.Refused(outcome.IsValid, transcript.CostUsd, outcome.Duration),
        };
    }
}
