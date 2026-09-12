using System.Text.RegularExpressions;

namespace Harness;

/// <summary>A harness path a document quotes, where it says it, and whether it is on disk.</summary>
public sealed record QuotedPath(string Document, int Line, string Path, bool Exists)
{
    public override string ToString() => $"{Document}:{Line} quotes {Path}";
}

/// <summary>
/// Issue #29. Every harness path the repo's documents quote, held to the disk.
///
/// #23 moved four top-level folders into one per skill, and six documents quoted the old names.
/// Nothing in the repo noticed, because every other rule here reads code or catalogue and no rule
/// read prose. A document pointing at a folder that is not there is worse than no document: it is
/// read as current, and the reader loses the time before working out that it is not.
///
/// It costs milliseconds and no model call, so the next layout change reddens the gate rather than
/// rotting the docs quietly.
/// </summary>
public static partial class DocumentedPaths
{
    /// <summary>
    /// Folders holding no prose of ours. Three kinds, and the last is the one that bites.
    ///
    /// `.claude` is vendored dev tooling and any worktree parked beside it, which is somebody else's
    /// prose: a gate reddening on an upstream phrasing is one people learn to ignore. `.git`, `bin`
    /// and `obj` hold no prose at all.
    ///
    /// A suite's <see cref="SuiteDiscovery.RunsFolder"/> is OUTPUT. A paid pass renders a report
    /// naming the folders it read, so a report written before a layout change describes the layout
    /// of its own day, correctly, forever. Sweeping those would redden the free gate for a record
    /// being accurate about history, and the fix would be to falsify the record.
    /// </summary>
    private static readonly string[] Skipped = [".git", ".claude", "bin", "obj", SuiteDiscovery.RunsFolder];

    /// <summary>
    /// A path-shaped run of characters. Deliberately narrow: no <c>&lt;</c>, so a <c>&lt;name&gt;</c>
    /// placeholder ends the match and only the prefix before it is asked of the disk.
    /// </summary>
    [GeneratedRegex(@"[A-Za-z0-9_][A-Za-z0-9_./-]*", RegexOptions.Compiled)]
    private static partial Regex PathShaped();

    /// <summary>
    /// Every harness path quoted anywhere in the repo's own documents. A caller asserts on this as
    /// well as on the dead ones, because a rule that matched nothing would report nothing dead and
    /// read as a pass. That is the vacuous green <c>The_catalogue_is_not_empty</c> guards elsewhere.
    /// </summary>
    public static IReadOnlyList<QuotedPath> Quoted(HarnessPaths paths)
    {
        // The folders the harness owns, asked of the disk rather than listed here, so this cannot
        // disagree with SuiteFolderTests about what they are.
        var owned = Directory.GetDirectories(paths.Root)
            .Select(d => Path.GetFileName(d)!)
            .Where(f => !f.StartsWith('.'))
            .ToHashSet(StringComparer.Ordinal);

        return [.. Documents(paths.RepoRoot).SelectMany(doc => PathsIn(paths, owned, doc))];
    }

    public static IReadOnlyList<QuotedPath> Dead(HarnessPaths paths) => Dead(Quoted(paths));

    /// <summary>The dead ones out of a sweep already taken, so a caller asserting on both sweeps once.</summary>
    public static IReadOnlyList<QuotedPath> Dead(IEnumerable<QuotedPath> quoted) =>
        [.. quoted.Where(q => !q.Exists)];

    private static IEnumerable<string> Documents(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(repoRoot, f)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => Skipped.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal);

    private static IEnumerable<QuotedPath> PathsIn(HarnessPaths paths, HashSet<string> owned, string doc)
    {
        // A README inside harness/ names paths relative to the harness root, because that is the root
        // it describes: `shared/catalogue/` in the harness README means one folder and no other.
        //
        // A README only, not every document under harness/. A fixture SKILL.md sits there too and is
        // MATERIAL rather than prose about the harness: its `src/Foo.cs` addresses the bare repo a
        // contract run writes into, and holding that to this disk reddens a fixture for being one.
        var describesTheHarness = HarnessPaths.Inside(paths.Root, doc)
            && string.Equals(Path.GetFileName(doc), "README.md", StringComparison.OrdinalIgnoreCase);

        var name = Path.GetRelativePath(paths.RepoRoot, doc)
            .Replace(Path.DirectorySeparatorChar, '/');
        var lines = File.ReadAllLines(doc);

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in PathShaped().Matches(lines[i]))
            {
                // A full stop closing the sentence is not part of the path. Without this the rule
                // reddens every sentence that ends on a folder, which is most of them.
                var quoted = match.Value.TrimEnd('.');
                var root = RootFor(paths, owned, quoted, describesTheHarness);

                if (root is null) continue;

                var resolved = Path.Combine(
                    root, quoted.Replace('/', Path.DirectorySeparatorChar));

                // The TRIMMED path, because that is the one asked of the disk. Reporting the raw
                // match would name a path with a full stop the check never used.
                yield return new QuotedPath(
                    name, i + 1, quoted, File.Exists(resolved) || Directory.Exists(resolved));
            }
        }
    }

    /// <summary>
    /// Which root a quoted path is measured from, or null for one this rule does not claim. Prose is
    /// full of path-shaped things: a file name, a dotted identifier, the tail of a URL. A rule that
    /// guessed at those would redden on sentences rather than on paths.
    ///
    /// A relative path is claimed only when its first segment is a folder the harness owns. Rename
    /// one of those and this check quietly stops looking rather than going red, which is why
    /// SuiteFolderTests pins the names. The two rules cover each other.
    /// </summary>
    private static string? RootFor(HarnessPaths paths, HashSet<string> owned, string quoted, bool describesTheHarness)
    {
        var first = quoted.Split('/')[0];

        if (first == Path.GetFileName(paths.Root)) return paths.RepoRoot;
        if (describesTheHarness && quoted.Contains('/') && owned.Contains(first)) return paths.Root;
        return null;
    }
}
