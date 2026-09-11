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

    public string Fixtures => Path.Combine(Root, "fixtures");
    public string GoodPlugin => Path.Combine(Fixtures, "good");
    /// <summary>#6's break overlays. Sparse trees laid over a base fixture, never plugins in their own right.</summary>
    public string Breaks => Path.Combine(Fixtures, "breaks");
    public string BreakOverlay(string name) => Path.Combine(Breaks, name);
    public string Cases => Path.Combine(Root, "cases");

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
    /// Real run records, written by the paid passes. NOT test data: #25 left these where they were,
    /// because a long pass resumes by reading back what it wrote and a wrong path costs money to find.
    /// </summary>
    public string Captured => Path.Combine(Root, "captured");
    /// <summary>Issue #24. One folder per skill under test, scanned by <see cref="SuiteDiscovery"/>.</summary>
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
