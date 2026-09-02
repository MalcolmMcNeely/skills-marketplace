using System.Diagnostics.CodeAnalysis;

namespace Harness;

/// <summary>How the run was allowed to end. Decides which validity rule applies.</summary>
public enum StopMode
{
    /// <summary>Ran to completion. Validity is exit 0 AND terminal subtype "success".</summary>
    Completion,
    /// <summary>
    /// Killed as soon as the firing decision is visible: the first Skill call (fired), or the first
    /// file-writing call (work has begun, so no skill is coming). MEASURED: killing at the first tool
    /// call of ANY kind, which is what run_eval.py does, scores a firing run as a miss, because the
    /// model opens with Bash or Glob to look around before it picks a skill.
    /// </summary>
    FirstDecision,
}

/// <summary>
/// The raw result of one <c>claude -p</c> invocation. Deliberately NOT scoreable.
/// Scoring takes a <see cref="ValidRun"/>, and the only way to get one is <see cref="TryGetValid"/>,
/// which enforces the validity gate. Issue #8 item 4: the trap is closed by construction.
/// </summary>
public sealed record RunOutcome
{
    public required int ExitCode { get; init; }
    /// <summary>Subtype of the LAST result line. Allowlisted against "success"; failures are never denylisted.</summary>
    public required string? TerminalSubtype { get; init; }
    public required Transcript Transcript { get; init; }
    public required string WorkingDirectory { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string RawStream { get; init; }
    public required StopMode StopMode { get; init; }
    /// <summary>True when the harness killed the process because the firing decision was visible.</summary>
    public required bool KilledAtDecision { get; init; }
    /// <summary>True when the init line was seen, so the CLI really started.</summary>
    public required bool Started { get; init; }

    public bool IsValid => StopMode switch
    {
        // A killed run has no exit code and no result line, so the completion rule cannot apply.
        // run_eval.py's defect is exactly here: its timeout path returns "did not trigger".
        // A kill only counts when the model actually reached a tool decision.
        StopMode.FirstDecision => Started && (KilledAtDecision || (ExitCode == 0 && TerminalSubtype == "success")),
        _ => ExitCode == 0 && TerminalSubtype == "success",
    };

    public string VoidReason => IsValid
        ? "not void"
        : StopMode == StopMode.FirstDecision && !KilledAtDecision
            ? $"early-stop run reached no firing decision (started={Started}, exit={ExitCode}, subtype={TerminalSubtype ?? "<none>"})"
            : $"exit={ExitCode} subtype={TerminalSubtype ?? "<none>"}";

    public bool TryGetValid([NotNullWhen(true)] out ValidRun? run)
    {
        run = IsValid ? new ValidRun(this) : null;
        return run is not null;
    }
}

/// <summary>A run that passed its validity gate. Cannot be constructed any other way.</summary>
public sealed class ValidRun
{
    internal ValidRun(RunOutcome outcome) => Outcome = outcome;
    public RunOutcome Outcome { get; }
    public Transcript Transcript => Outcome.Transcript;
    public string WorkingDirectory => Outcome.WorkingDirectory;
}
