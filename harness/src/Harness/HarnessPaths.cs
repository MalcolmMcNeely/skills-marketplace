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
    public string StubCatalogue => Path.Combine(Fixtures, "catalogue");
    public string GoodPlugin => Path.Combine(Fixtures, "good");
    public string BrokenPlugin(string name) => Path.Combine(Fixtures, "broken", name);
    public string FixtureRepo => Path.Combine(Fixtures, "repo");
    public string Cases => Path.Combine(Root, "cases");
    public string Captured => Path.Combine(Root, "captured");
    public string ShippedCatalogue => Path.Combine(Root, "..", "plugins");

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
