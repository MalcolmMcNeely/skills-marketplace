namespace Harness;

/// <summary>
/// Issue #6. A break is an OVERLAY, not a second copy of a fixture.
///
/// The base plugin supplies the manifest and every file the overlay does not mention; the overlay
/// supplies only what the break changes. A duplicated fixture drifts the moment the original is
/// edited, and a drifted fixture measures two changes while reporting one.
///
/// Built into scratch, never in place. The fixtures on disk stay frozen, so a pass cannot leave a
/// broken skill behind for the next one to measure by accident.
/// </summary>
public sealed class FixtureBuilder(HarnessPaths paths)
{
    /// <summary>
    /// Copy <paramref name="basePlugin"/> to scratch and lay <paramref name="overlay"/> over it.
    /// A null overlay returns the base untouched, which is how the unbroken arm runs.
    /// </summary>
    public string Build(string basePlugin, string? overlay)
    {
        if (overlay is null) return basePlugin;
        if (!Directory.Exists(basePlugin)) throw new DirectoryNotFoundException($"base plugin not found: {basePlugin}");
        if (!Directory.Exists(overlay)) throw new DirectoryNotFoundException($"overlay not found: {overlay}");

        var dest = Path.Combine(paths.Scratch, "fixtures", Guid.NewGuid().ToString("N")[..8]);
        HarnessPaths.CopyDirectory(basePlugin, dest);

        var applied = 0;
        foreach (var file in Directory.GetFiles(overlay, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(overlay, file);
            if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;

            var target = Path.Combine(dest, relative);
            // An overlay may only REPLACE. A path that matches nothing in the base is a typo, and a
            // typo that silently applies nothing would run the good fixture under a break's name.
            if (!File.Exists(target))
                throw new InvalidOperationException(
                    $"overlay file '{relative}' matches nothing in base plugin '{basePlugin}'. An overlay replaces, it does not add.");

            File.Copy(file, target, overwrite: true);
            applied++;
        }

        if (applied == 0)
            throw new InvalidOperationException($"overlay '{overlay}' applied no files. An overlay that changes nothing is not a break.");

        return dest;
    }

    /// <summary>The frontmatter description line, for the tests that pin what a break did and did not touch.</summary>
    public static string DescriptionOf(string skillFile)
    {
        foreach (var line in File.ReadLines(skillFile))
        {
            if (line.StartsWith("description:", StringComparison.Ordinal))
                return line["description:".Length..].Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal)) break;
        }
        throw new InvalidOperationException($"no description in frontmatter: {skillFile}");
    }

    /// <summary>Everything after the frontmatter. What layer 4 reads and layer 3 never sees.</summary>
    public static string BodyOf(string skillFile)
    {
        var text = File.ReadAllText(skillFile).ReplaceLineEndings("\n");
        var end = text.IndexOf("\n---", StringComparison.Ordinal);
        if (!text.StartsWith("---", StringComparison.Ordinal) || end < 0)
            throw new InvalidOperationException($"no frontmatter: {skillFile}");
        return text[(end + 4)..].TrimStart('\n');
    }
}
