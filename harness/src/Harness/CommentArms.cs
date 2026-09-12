namespace Harness;

/// <summary>
/// The four arms of the comment-density experiment specified in docs/research/steering-code-style.md.
///
/// One rule, four positions. <see cref="Rule"/> is written once and every arm that carries it carries
/// the same words, so the variable under test is WHERE an instruction sits rather than how it is
/// phrased. Arm A carries it nowhere and is the control.
///
/// Arm D is the reason the pass is worth paying for. Reading the shipped 2.1.248 binary, the prompt
/// assembly drops its `# Doing tasks` section, which contains "Default to writing no comments", unless
/// the active output style is Default or sets `keep-coding-instructions: true`. Arm D omits the flag
/// and so should be the WORST arm, not the best. That is a falsifiable claim about shipped code, and
/// it is the only claim here a run can overturn.
/// </summary>
public sealed record CommentArm
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    /// <summary>Passed to `claude --settings`. Names the output style the run resolves.</summary>
    public required string SettingsJson { get; init; }
    /// <summary>Written to CLAUDE.md at the repo root, or absent when the arm puts the rule elsewhere.</summary>
    public string? ClaudeMd { get; init; }
    /// <summary>Written to .claude/output-styles/{StyleName}.md, or absent on a Default-style arm.</summary>
    public string? StyleName { get; init; }
    public string? StyleBody { get; init; }

    /// <summary>
    /// The one instruction under test, in the phrasing Anthropic's own prompting guide recommends:
    /// positive, and scoped to what the change touches rather than banned outright.
    /// </summary>
    public const string Rule =
        "Comments record why, never what. Add one only where the reason is invisible in the code itself: "
        + "a hidden constraint, a subtle invariant, or a workaround for a specific defect. "
        + "Leave the comments in code you did not change exactly as they are.";

    public static IReadOnlyList<CommentArm> All =>
    [
        new()
        {
            Id = "A-default",
            Label = "Default style, no rule anywhere. The control.",
            SettingsJson = """{"outputStyle":"default"}""",
        },
        new()
        {
            Id = "B-claude-md",
            Label = "Default style, the rule in CLAUDE.md. A user message after the system prompt.",
            SettingsJson = """{"outputStyle":"default"}""",
            ClaudeMd = $"# Conventions\n\n{Rule}\n",
        },
        new()
        {
            Id = "C-style-keep",
            Label = "Custom style carrying the rule, coding instructions KEPT.",
            SettingsJson = """{"outputStyle":"harness-comments-keep"}""",
            StyleName = "harness-comments-keep",
            StyleBody = Style("harness-comments-keep", keepCodingInstructions: true),
        },
        new()
        {
            Id = "D-style-drop",
            Label = "The same custom style, coding instructions DROPPED. The naive authoring mistake.",
            SettingsJson = """{"outputStyle":"harness-comments-drop"}""",
            StyleName = "harness-comments-drop",
            StyleBody = Style("harness-comments-drop", keepCodingInstructions: false),
        },
    ];

    /// <summary>
    /// C and D differ by ONE frontmatter line and nothing else. The body is the same rule the
    /// CLAUDE.md arm carries, with no added role or tone guidance, so the only thing separating the
    /// two paid arms is whether the default coding instructions survive alongside it.
    ///
    /// `keep-coding-instructions: false` is written out rather than omitted. False is the documented
    /// default, so the two files say the same thing either way, and a reader comparing the arms should
    /// not have to know the default to see what changed.
    /// </summary>
    private static string Style(string name, bool keepCodingInstructions) =>
        $"""
        ---
        name: {name}
        description: Harness fixture for the comment-density experiment. Not for human use.
        keep-coding-instructions: {(keepCodingInstructions ? "true" : "false")}
        ---

        {Rule}
        """;

    /// <summary>
    /// Lay this arm's instruction files into a scratch repo. Returns the .cs files already present,
    /// which <see cref="CommentDensity.Measure"/> excludes so the reading covers only what the run wrote.
    /// </summary>
    public IReadOnlyList<string> Prepare(string repoDir)
    {
        if (ClaudeMd is not null)
            File.WriteAllText(Path.Combine(repoDir, "CLAUDE.md"), ClaudeMd);

        if (StyleName is not null)
        {
            var dir = Path.Combine(repoDir, ".claude", "output-styles");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{StyleName}.md"), StyleBody);
        }

        return CommentDensity.CsharpFilesIn(repoDir);
    }
}
