namespace Harness;

/// <summary>
/// Issue #8 items 1 and 6. Everything the harness touches lives under harness/, never plugins/.
/// Fixtures load with --plugin-dir, which is session-only and repeatable, so no fixture can
/// leak into the shipped catalogue.
/// </summary>
public sealed class HarnessPaths
{
    public HarnessPaths(string? root = null)
    {
        Root = root ?? FindRoot();
        Scratch = Path.Combine(Path.GetTempPath(), "skill-harness", Guid.NewGuid().ToString("N")[..8]);
    }

    public string Root { get; }
    public string Scratch { get; }

    /// <summary>
    /// Issue #25. Material every suite borrows, kept apart from the material one skill owns. Three
    /// things qualify and nothing else does: the distractor catalogue layer 3 fires against, the bare
    /// repo a contract run writes into, and the streams the offline parser tests read.
    /// </summary>
    public string Shared => Path.Combine(Root, "shared");

    /// <summary>The twelve-skill distractor set. Descriptions only, so a run is decided on the listing.</summary>
    public string StubCatalogue => Path.Combine(Shared, "catalogue");

    /// <summary>The bare C# repo a contract run writes into, copied per run by <see cref="NewScratchRepo"/>.</summary>
    public string FixtureRepo => Path.Combine(Shared, "repo");

    public string Streams => Path.Combine(Shared, "streams");
    public string Stream(string name) => Path.Combine(Streams, name);

    /// <summary>
    /// Issue #24. One folder per skill under test, scanned by <see cref="SuiteDiscovery"/>. Everything
    /// one skill is tested on hangs off its own folder: the suite file, the fixture plugin, the break
    /// overlays and the records its paid passes wrote. Opening one folder answers what the skill is
    /// tested on, which four top-level folders and a filename typed into fourteen files did not.
    /// </summary>
    public string Suites => Path.Combine(Root, "skills");

    /// <summary>
    /// The repo the harness sits inside. Three callers were walking ".." by hand, which is the interface
    /// being wrong rather than the callers: everything the harness reads outside itself hangs off here.
    /// </summary>
    public string RepoRoot => Path.GetFullPath(Path.Combine(Root, ".."));

    public string ShippedCatalogue => Path.Combine(RepoRoot, "plugins");
    public string MarketplaceManifest => Path.Combine(RepoRoot, ".claude-plugin", "marketplace.json");
    /// <summary>#20's free gate. Not harness material, and the gate tests read it as a file.</summary>
    public string Workflows => Path.Combine(RepoRoot, ".github", "workflows");

    /// <summary>
    /// #27. The paid test project, read as SOURCE by the free one. Nothing the free gate can run sees
    /// how the paid layers choose their suites, because it never loads that assembly, so the one test
    /// that holds them to every discovered suite reads their files instead. It asks here for the
    /// folder rather than walking to it, for the reason <see cref="RepoRoot"/> exists.
    /// </summary>
    public string PaidTests => Path.Combine(Root, "tests", "model");

    /// <summary>
    /// #27. Is this path inside that folder? One rule, because three copies of it disagreed on case
    /// while guarding the same thing. A containment check that falsely ACCEPTS resolves real material
    /// under the wrong name and runs it, so the comparer is the filesystem's, not the language's.
    ///
    /// Both arguments are made absolute here. A caller that has already done so loses nothing.
    /// </summary>
    public static bool Inside(string folder, string path) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// #28. Are these two paths the same file?
    ///
    /// The comparer follows the PLATFORM, because the question is the filesystem's and the two
    /// filesystems answer it differently. The gate runs on a laptop and on ubuntu-24.04, where
    /// `Skill-Authoring/SKILL.md` and `skill-authoring/SKILL.md` are two files that can both exist.
    /// Ignoring case there would call them one, and a coverage check that falsely ACCEPTS reads an
    /// untested skill as tested. That is a silent green, which is the failure this harness exists to
    /// prevent, so the loose answer is never the safe default.
    /// </summary>
    public static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>A contract run writes files, so each one gets its own copy of the bare fixture repo.</summary>
    public string NewScratchRepo()
    {
        var dest = Path.Combine(Scratch, Guid.NewGuid().ToString("N")[..8]);
        CopyDirectory(FixtureRepo, dest);
        return dest;
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            if (dir.Contains($"{Path.DirectorySeparatorChar}bin") || dir.Contains($"{Path.DirectorySeparatorChar}obj")) continue;
            Directory.CreateDirectory(dir.Replace(from, to));
        }
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin") || file.Contains($"{Path.DirectorySeparatorChar}obj")) continue;
            File.Copy(file, file.Replace(from, to), overwrite: true);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HARNESS-ROOT")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("HARNESS-ROOT marker not found");
    }
}
