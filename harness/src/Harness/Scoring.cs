namespace Harness;

public static class Scoring
{
    /// <summary>Layer 3, positive case: exact set match (#10). Layer 3 has no contract, so a match is Held.</summary>
    public static RunScore ScoreFiring(RunOutcome outcome, IReadOnlyCollection<string> expected)
    {
        if (!outcome.TryGetValid(out var run))
            return new RunScore(Verdict.Void, outcome.VoidReason, [], [], outcome.Transcript.CostUsd);

        var fired = run.Transcript.FiredSet;
        if (fired.Count == 0 && expected.Count > 0)
            return new RunScore(Verdict.Missed, "nothing fired", [], [], run.Transcript.CostUsd);
        if (!fired.SetEquals(expected))
            return new RunScore(Verdict.WrongSet, $"fired {{{string.Join(", ", fired.Order())}}}, expected {{{string.Join(", ", expected.Order())}}}",
                [.. fired.Order()], [], run.Transcript.CostUsd);

        return new RunScore(Verdict.Held, "set matched", [.. fired.Order()], [], run.Transcript.CostUsd);
    }

    /// <summary>
    /// Layer 3, negative case: graded ONLY on whether the skill under test stayed quiet (#10).
    /// Gating on the decoy's set would import the decoy's own one-in-three miss rate.
    /// </summary>
    public static RunScore ScoreQuiet(RunOutcome outcome, string mustStayQuiet)
    {
        if (!outcome.TryGetValid(out var run))
            return new RunScore(Verdict.Void, outcome.VoidReason, [], [], outcome.Transcript.CostUsd);

        var fired = run.Transcript.FiredSet;
        var verdict = fired.Contains(mustStayQuiet) ? Verdict.Broken : Verdict.Held;
        return new RunScore(verdict, verdict == Verdict.Held ? $"{mustStayQuiet} stayed quiet" : $"{mustStayQuiet} FIRED",
            [.. fired.Order()], [], run.Transcript.CostUsd);
    }

    /// <summary>Layer 4, invoked by name. Firing cannot miss, so the only outcomes are Void, Held, Broken.</summary>
    public static RunScore ScoreContract(RunOutcome outcome, string skill, IContractAssertions assertions)
    {
        if (!outcome.TryGetValid(out var run))
            return new RunScore(Verdict.Void, outcome.VoidReason, [], [], outcome.Transcript.CostUsd);

        var fired = run.Transcript.FiredSet;
        // MEASURED, and it cost $2.28 to learn: a by-name run does NOT reliably emit a Skill tool_use.
        // Outside Git Bash the CLI expands the slash command inline, so the body reaches the model and
        // the rule is obeyed with no Skill call in the stream at all. Gating on the fired set here
        // voided ten healthy runs in a row. The real precondition is that the fixture LOADED, which the
        // init line's slash_commands proves for free.
        if (!run.Transcript.SkillIsAvailable(skill))
            return new RunScore(Verdict.Void, $"{skill} was not registered as a slash command; the --plugin-dir fixture did not load",
                [.. fired.Order()], [], run.Transcript.CostUsd);

        var results = assertions.Evaluate(run);
        var failed = results.Where(r => !r.Passed).ToList();
        return new RunScore(
            failed.Count == 0 ? Verdict.Held : Verdict.Broken,
            failed.Count == 0 ? "all assertions passed" : string.Join("; ", failed.Select(f => $"A{f.Number} {f.Kind} failed")),
            [.. fired.Order()], results, run.Transcript.CostUsd);
    }
}
