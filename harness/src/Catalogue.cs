using System.Text.RegularExpressions;

namespace Harness;

/// <summary>One SKILL.md, read off disk.</summary>
public sealed record CatalogueSkill(string Name, string Description, bool IsEngine, string Body, string File)
{
    /// <summary>An engine is model-invocable, so its description sits in the listing on every request.</summary>
    public bool IsEntryPoint => !IsEngine;
}

/// <summary>A name a skill referenced, and where it said it.</summary>
public sealed record SkillReference(string Name, int Line, string Text);

/// <summary>
/// Issue #5. Layer 2 is the free, deterministic tier: referential integrity and budget, no model calls.
///
/// SCOPE, decided on #5: the shipped catalogue under <c>plugins/*/skills/</c> only. The vendored dev
/// tooling in <c>.claude/skills/</c> is someone else's toolkit that we edit for our own use, and
/// reddening a pull request because an upstream phrasing changed would be noise.
/// </summary>
public static class Catalogue
{
    /// <summary>#5 and CLAUDE.md: engines compete for the listing, so the catalogue caps them.</summary>
    public const int MaxEngines = 12;

    /// <summary>
    /// agentskills.io's hard limit. NOT Claude Code's <c>skillListingMaxDescChars</c>, which
    /// findings.md records as 1,536. Two limits from two sources; this is the tighter one, so it binds.
    /// </summary>
    public const int MaxDescriptionChars = 1024;

    public static IReadOnlyList<CatalogueSkill> Load(string root)
    {
        if (!Directory.Exists(root)) return [];
        return [.. Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories)
            .Select(f => Parse(System.IO.File.ReadAllText(f), f))
            .Where(s => s is not null)!];
    }

    public static CatalogueSkill? Parse(string text, string file = "")
    {
        var match = Regex.Match(text, @"\A---\r?\n(?<fm>.*?)\r?\n---\r?\n(?<body>.*)\z", RegexOptions.Singleline);
        if (!match.Success) return null;

        var frontmatter = match.Groups["fm"].Value;
        var name = Field(frontmatter, "name") ?? Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
        var description = Field(frontmatter, "description") ?? "";
        // Anything but a literal true leaves the skill model-invocable, which is the CLI's own default.
        var isEngine = Field(frontmatter, "disable-model-invocation") is not "true";

        return new CatalogueSkill(name, description, isEngine, match.Groups["body"].Value, file);
    }

    /// <summary>Single-line values only. A folded description reads as empty, which the budget test catches.</summary>
    private static string? Field(string frontmatter, string key)
    {
        var m = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}:\s*(?<v>.+)$", RegexOptions.Multiline);
        return m.Success ? m.Groups["v"].Value.Trim().Trim('"') : null;
    }
}

/// <summary>
/// How one skill says it uses another. Decided on #5.
///
/// THE RULE: every quoted string on a line that mentions the Skill tool is a reference, and every
/// reference must resolve to a catalogue skill.
///
/// MEASURED across the 15 real examples in .claude/skills/: the idiom appears in 12 different
/// phrasings, and this rule finds 19 references on 14 of the 15 lines. All 19 resolve. A single
/// regex built on one phrasing found 2 of 11 and silently passed the rest.
///
/// The one line it does not cover says "call the Skill tool for whichever skills the ## Notes block
/// names". There is no name there to resolve, and a line with no quoted string yields no reference,
/// so the dynamic case needs no exemption mechanism. That is the point of extracting rather than
/// matching: an un-namable reference is absent, not wrong.
///
/// Rejected: enforcing the one canonical phrasing. CLAUDE.md mandates
/// `Call the Skill tool with "name"`, but the measurement kills a test on it.
/// `call the Skill tool twice, for "grilling" and "domain-modeling"` names two skills in one
/// instruction and is a sentence people legitimately want to write.
///
/// Fenced blocks are skipped, for the same reason <see cref="SlashForms"/> skips them and found the
/// hard way: the first real catalogue skill this ran against documents the idiom with a placeholder,
/// `Call the Skill tool with "name"`, and "name" is not a skill. A skill that teaches the idiom has
/// to be able to show it. The house rule both checks share: examples go in fences, instructions go
/// in prose.
/// </summary>
public static partial class Composition
{
    [GeneratedRegex(@"""(?<n>[a-z][a-z0-9-]*)""")]
    private static partial Regex QuotedName();

    public static IReadOnlyList<SkillReference> References(string body) =>
        [.. Markdown.ProseLines(body)
            .Where(l => l.Text.Contains("skill tool", StringComparison.OrdinalIgnoreCase))
            .SelectMany(l => QuotedName().Matches(l.Text)
                .Select(m => new SkillReference(m.Groups["n"].Value, l.Number, l.Text.Trim())))];
}

/// <summary>Reading a skill body the way both layer 2 rules agree to read it.</summary>
public static class Markdown
{
    public sealed record Line(string Text, int Number);

    /// <summary>
    /// Every line outside a fenced code block, numbered from 1. A fence is an example, and an example
    /// is not an instruction: both layer 2 rules need to let a skill show the wrong form.
    /// </summary>
    public static IEnumerable<Line> ProseLines(string body)
    {
        var inFence = false;
        var lines = body.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence) yield return new Line(lines[i], i + 1);
        }
    }
}

/// <summary>
/// The slash-form rule. Decided on #5.
///
/// THE RULE: a slash form is an error only when the name resolves to an ENGINE.
///
/// The hard part was telling a delegation instruction from prose describing a command a developer
/// types. MEASURED in .claude/skills/: 102 backticked slash forms across 28 distinct names, and 13
/// of those uses are not skills at all (`/compact` 6, `/clear` 5, `/tmp`, `/settings`). A blanket
/// grep fails on every one of them.
///
/// Resolving the name against the engine list settles it with no denylist to maintain. An engine is
/// model-invoked and a developer never types one, so `/engine` in prose is always a mistake. An entry
/// point exists to be typed, so `/entry-point` is correct. A built-in resolves to nothing and is
/// ignored.
///
/// Two exclusions, both deliberate:
/// - A slash preceded by a word character, a dot, a dash or another slash is part of a path.
///   `plugins/core/skills/skill-authoring` must not read as a reference to the skill-authoring engine.
/// - Fenced code blocks are examples, not instructions. A skill teaching the rule has to be able to
///   show the wrong form.
/// </summary>
public static partial class SlashForms
{
    [GeneratedRegex(@"(?<![\w./-])/(?<n>[a-z][a-z0-9-]+)")]
    private static partial Regex SlashName();

    public static IReadOnlyList<SkillReference> InProse(string body) =>
        [.. Markdown.ProseLines(body)
            .SelectMany(l => SlashName().Matches(l.Text)
                .Select(m => new SkillReference(m.Groups["n"].Value, l.Number, l.Text.Trim())))];
}
