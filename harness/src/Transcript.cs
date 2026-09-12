namespace Harness;

/// <summary>How a file came to exist, and where in the run it happened.</summary>
public enum CreationRoute { Write, Bash, PowerShell }

public sealed record FileCreation(string Path, int Ordinal, CreationRoute Route);

/// <summary>
/// One Skill tool_use, at its position in the stream. NOT deduplicated, because #12 output 6 asks
/// where in the run each call happened: if csharp-new-class never fires after another skill has
/// already fired, #11's FirstDecision stop rule is safe for negatives and can be switched on.
/// </summary>
public sealed record SkillCall(string Name, string RawName, int Ordinal);

/// <summary>Everything the scorers are allowed to read out of one stream-json run.</summary>
public sealed record Transcript
{
    /// <summary>
    /// Every Skill invocation in the run, in order, deduplicated, with the plugin prefix stripped.
    /// Never just the first. MEASURED: the CLI reports "harness-fixture-good:csharp-new-class",
    /// so a case that expected the bare name would score wrong-set on a healthy run. The prefix is
    /// the fixture's name and changes between the good and broken fixtures, so it cannot be in a case.
    /// </summary>
    public required IReadOnlyList<string> FiredSkills { get; init; }

    /// <summary>The same invocations exactly as the stream reported them. Diagnostic only.</summary>
    public required IReadOnlyList<string> FiredSkillsRaw { get; init; }

    /// <summary>Every Skill call with its ordinal, in stream order, repeats included.</summary>
    public required IReadOnlyList<SkillCall> SkillCalls { get; init; }

    /// <summary>First creation of each path, ordered by position in the stream.</summary>
    public required IReadOnlyList<FileCreation> FileCreations { get; init; }

    /// <summary>Every shell command string the run executed, for prohibition checks.</summary>
    public required IReadOnlyList<string> ShellCommands { get; init; }

    public required string? Model { get; init; }
    public required string? OutputStyle { get; init; }
    public required string? PermissionMode { get; init; }
    /// <summary>The init line's slash_commands. Proves a --plugin-dir fixture actually loaded.</summary>
    public required IReadOnlyList<string> SlashCommands { get; init; }

    public required IReadOnlyList<string> LoadedSkills { get; init; }
    public required string? ResultText { get; init; }
    public required decimal? CostUsd { get; init; }

    public HashSet<string> FiredSet => new(FiredSkills, StringComparer.Ordinal);

    /// <summary>True when the CLI registered this skill as an invocable slash command.</summary>
    public bool SkillIsAvailable(string skill) =>
        SlashCommands.Any(c => StreamParser.Unqualify(c).Equals(skill, StringComparison.Ordinal));

    /// <summary>Ordinal of the first call to this skill, or null if it never fired.</summary>
    public int? FirstSkillOrdinal(string skill) =>
        SkillCalls.FirstOrDefault(c => c.Name.Equals(skill, StringComparison.Ordinal))?.Ordinal;

    /// <summary>How many distinct OTHER skills fired before this one. 0 means it fired first.</summary>
    public int? SkillsFiredBefore(string skill)
    {
        var at = FirstSkillOrdinal(skill);
        return at is null
            ? null
            : SkillCalls.Where(c => c.Ordinal < at).Select(c => c.Name).Distinct(StringComparer.Ordinal).Count();
    }

    public int? FirstCreationOrdinal(string path) =>
        FileCreations.FirstOrDefault(c => c.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase))?.Ordinal;
}
