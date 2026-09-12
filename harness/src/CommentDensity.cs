namespace Harness;

/// <summary>
/// How heavily commented the C# a run wrote is.
///
/// A LINE classifier, not a parser, and the choice is deliberate. The behaviour under measurement is
/// whole-line narration above a statement, which is what the reports in steering-code-style.md
/// describe and what a reader actually trips over. A line counts as a comment only when its TRIMMED
/// text opens one, so a `//` inside a string literal is code, which is the answer a parser would give
/// for the case that matters.
///
/// Two limits, stated rather than hidden. A block comment opened part-way along a code line counts
/// that line as code and every following line until the close as comment. A trailing comment on a
/// code line is counted separately and never enters <see cref="Reading.Density"/>, because it shares
/// its line with code and a per-line ratio cannot hold both.
/// </summary>
public static class CommentDensity
{
    /// <summary>What one run wrote, counted. Lines, not bytes: the unit the complaint is made in.</summary>
    public sealed record Reading
    {
        public required int Files { get; init; }
        /// <summary>Non-blank, non-structural lines that are not comments.</summary>
        public required int CodeLines { get; init; }
        /// <summary>Whole-line `//` narration. The behaviour under measurement.</summary>
        public required int NarrationLines { get; init; }
        /// <summary>Whole-line `///` XML documentation. Arguably earned on a public API, so counted apart.</summary>
        public required int DocLines { get; init; }
        /// <summary>Whole lines inside a `/* */` span.</summary>
        public required int BlockLines { get; init; }
        /// <summary>Code lines carrying a comment after the code. Reported, never in a ratio.</summary>
        public required int TrailingComments { get; init; }
        /// <summary>Lines holding only braces, parens or a semicolon. Excluded from both halves of every ratio.</summary>
        public required int StructuralLines { get; init; }

        public int CommentLines => NarrationLines + DocLines + BlockLines;

        /// <summary>Comment lines per 100 code lines. The headline figure.</summary>
        public double? Density => Ratio(CommentLines);

        /// <summary>Narration and block comments only, with XML docs set aside. The figure the arms are about.</summary>
        public double? NarrationDensity => Ratio(NarrationLines + BlockLines);

        public double? DocDensity => Ratio(DocLines);

        /// <summary>
        /// A run that wrote no code has no density. Zero would read as a perfectly clean run and
        /// average in beside real ones, so the absence is kept as an absence.
        /// </summary>
        private double? Ratio(int part) => CodeLines == 0 ? null : Math.Round(part * 100.0 / CodeLines, 1);

        public string Summary => CodeLines == 0
            ? $"{Files} file(s), no code lines"
            : $"{Files} file(s), {CodeLines} code, {CommentLines} comment ({NarrationLines} narration, {DocLines} doc, {BlockLines} block), density {Density:0.0}";
    }

    /// <summary>
    /// Everything under <paramref name="directory"/> that was not there before the run.
    ///
    /// The snapshot is taken rather than assumed. The bare fixture repo holds no .cs today, so a sweep
    /// would give the same answer, but a fixture that later ships a file would silently fold it into
    /// every arm's count and move all four numbers together, which is the kind of error a comparison
    /// between arms cannot see.
    /// </summary>
    public static Reading Measure(string directory, IReadOnlyCollection<string> excluding)
    {
        var files = CsharpFilesIn(directory).Where(f => !excluding.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();

        var code = 0; var narration = 0; var doc = 0; var block = 0; var trailing = 0; var structural = 0;

        foreach (var file in files)
        {
            var inBlock = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();

                if (inBlock)
                {
                    block++;
                    if (line.Contains("*/", StringComparison.Ordinal)) inBlock = false;
                    continue;
                }

                if (line.Length == 0) continue;

                if (line.StartsWith("///", StringComparison.Ordinal)) { doc++; continue; }
                if (line.StartsWith("//", StringComparison.Ordinal)) { narration++; continue; }

                if (line.StartsWith("/*", StringComparison.Ordinal))
                {
                    block++;
                    if (!line.Contains("*/", StringComparison.Ordinal)) inBlock = true;
                    continue;
                }

                if (IsStructural(line)) { structural++; continue; }

                code++;
                if (HasTrailingComment(line)) trailing++;
                // A code line may still OPEN a block that runs on. Counted as code, by the rule above.
                if (OpensUnclosedBlock(line)) inBlock = true;
            }
        }

        return new Reading
        {
            Files = files.Count,
            CodeLines = code,
            NarrationLines = narration,
            DocLines = doc,
            BlockLines = block,
            TrailingComments = trailing,
            StructuralLines = structural,
        };
    }

    /// <summary>The .cs a run could see. bin/ and obj/ are the compiler's, not the model's.</summary>
    public static IReadOnlyList<string> CsharpFilesIn(string directory) =>
        !Directory.Exists(directory)
            ? []
            : [.. Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Braces, parens and a bare semicolon carry no logic and no narration, so counting them inflates
    /// the denominator and flatters every arm equally. Excluded on both sides, as the metric says.
    /// </summary>
    private static bool IsStructural(string line) => line.All(c => c is '{' or '}' or '(' or ')' or ';' or ',');

    /// <summary>
    /// A `//` that is not inside a string. Quote counting is enough here: a code line holding an
    /// unbalanced quote is a compile error, so the parity is reliable on anything that builds.
    /// </summary>
    private static bool HasTrailingComment(string line) => IndexOfCommentOutsideString(line, "//") > 0;

    private static bool OpensUnclosedBlock(string line)
    {
        var open = IndexOfCommentOutsideString(line, "/*");
        return open >= 0 && !line[open..].Contains("*/", StringComparison.Ordinal);
    }

    private static int IndexOfCommentOutsideString(string line, string token)
    {
        var quotes = 0;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '\\') { i++; continue; }
            if (c == '"') { quotes++; continue; }
            if (quotes % 2 != 0) continue;
            if (line[i] == token[0] && line[i + 1] == token[1]) return i;
        }
        return -1;
    }
}
