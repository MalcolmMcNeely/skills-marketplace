using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// Reads a <c>--output-format stream-json</c> ndjson stream into a <see cref="Transcript"/>.
/// Records EVERY Skill invocation, not the first, because a cross-stack case has two right answers.
/// </summary>
public static class StreamParser
{
    public static (Transcript Transcript, string? TerminalSubtype) Parse(string stream)
    {
        var fired = new List<string>();
        var firedRaw = new List<string>();
        var creations = new List<FileCreation>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shell = new List<string>();
        var loadedSkills = new List<string>();
        var slashCommands = new List<string>();
        string? model = null, outputStyle = null, permissionMode = null, resultText = null, subtype = null;
        decimal? cost = null;
        var ordinal = 0;

        foreach (var line in stream.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '{') continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(trimmed); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                var type = Str(root, "type");

                if (type == "system" && Str(root, "subtype") == "init")
                {
                    model = Str(root, "model");
                    outputStyle = Str(root, "output_style") ?? Str(root, "outputStyle");
                    permissionMode = Str(root, "permissionMode") ?? Str(root, "permission_mode");
                    if (root.TryGetProperty("slash_commands", out var sc) && sc.ValueKind == JsonValueKind.Array)
                        slashCommands.AddRange(sc.EnumerateArray().Select(SkillName).Where(x => x is not null)!);
                    foreach (var key in new[] { "skills", "loaded_skills" })
                        if (root.TryGetProperty(key, out var s) && s.ValueKind == JsonValueKind.Array)
                            loadedSkills.AddRange(s.EnumerateArray().Select(SkillName).Where(x => x is not null)!);
                }
                else if (type == "assistant" && root.TryGetProperty("message", out var msg)
                         && msg.TryGetProperty("content", out var content)
                         && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        ordinal++;
                        if (Str(block, "type") != "tool_use") continue;
                        var name = Str(block, "name");
                        if (!block.TryGetProperty("input", out var input)) continue;

                        if (name == "Skill")
                        {
                            var skill = Str(input, "skill") ?? Str(input, "name") ?? Str(input, "command");
                            if (skill is null) continue;
                            if (!firedRaw.Contains(skill, StringComparer.Ordinal)) firedRaw.Add(skill);
                            var bare = Unqualify(skill);
                            if (!fired.Contains(bare, StringComparer.Ordinal)) fired.Add(bare);
                        }
                        else if (name is "Write" or "NotebookEdit")
                        {
                            Record(Str(input, "file_path") ?? Str(input, "notebook_path"), CreationRoute.Write);
                        }
                        else if (name is "Bash" or "PowerShell")
                        {
                            var cmd = Str(input, "command");
                            if (cmd is null) continue;
                            shell.Add(cmd);
                            var route = name == "Bash" ? CreationRoute.Bash : CreationRoute.PowerShell;
                            foreach (var path in ShellFileTargets(cmd)) Record(path, route);
                        }
                    }
                }
                else if (type == "result")
                {
                    subtype = Str(root, "subtype");
                    resultText = Str(root, "result");
                    if (root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number)
                        cost = c.GetDecimal();
                }
            }

            void Record(string? path, CreationRoute route)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var norm = path.Replace('\\', '/').Trim();
                if (!seenPaths.Add(norm)) return;
                creations.Add(new FileCreation(norm, ordinal, route));
            }
        }

        var transcript = new Transcript
        {
            FiredSkills = fired,
            FiredSkillsRaw = firedRaw,
            FileCreations = creations,
            ShellCommands = shell,
            Model = model,
            OutputStyle = outputStyle,
            PermissionMode = permissionMode,
            SlashCommands = slashCommands,
            LoadedSkills = loadedSkills,
            ResultText = resultText,
            CostUsd = cost,
        };
        return (transcript, subtype);
    }

    // baseline-test-first.md: a model may create a file with a heredoc rather than Write.
    // Ordering must survive that, or a fake regression is reported.
    private static readonly Regex[] ShellWriters =
    [
        new(@"(?:^|\||;|&&)\s*(?:cat|echo|printf|tee)\b[^>|;&\n]*>>?\s*(?<p>[^\s;&|>]+)", RegexOptions.Compiled),
        new(@">>?\s*(?<p>[A-Za-z0-9_./\\-]+\.[A-Za-z0-9]+)", RegexOptions.Compiled),
        new(@"Set-Content\s+(?:-Path\s+)?(?<p>[^\s;|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Out-File\s+(?:-FilePath\s+)?(?<p>[^\s;|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"New-Item\b[^\n]*?-Path\s+(?<p>[^\s;|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    public static IEnumerable<string> ShellFileTargets(string command)
    {
        var hits = new List<(int Index, string Path)>();
        foreach (var rx in ShellWriters)
            foreach (Match m in rx.Matches(command))
                hits.Add((m.Index, m.Groups["p"].Value.Trim('"', '\'')));
        // Order inside one command string is still readable, so keep source order.
        return hits.OrderBy(h => h.Index).Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>"plugin-name:skill-name" becomes "skill-name". Measured against a real stream.</summary>
    public static string Unqualify(string skill)
    {
        var i = skill.LastIndexOf(':');
        return i >= 0 && i < skill.Length - 1 ? skill[(i + 1)..] : skill;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? SkillName(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "name");
}
