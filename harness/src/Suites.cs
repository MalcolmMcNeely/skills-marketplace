namespace Harness;

/// <summary>
/// Issue #24. One suite, as found on disk: the name, the parsed file, and every path the suite owns.
///
/// A layer asks this for a path and never assembles one. That is the whole point of the type: the
/// suite folder shape is known in one place, so the next layout change is one file.
/// </summary>
public sealed record DiscoveredSuite
{
    /// <summary>The folder name, which is also the declared suite name. Discovery refuses a disagreement.</summary>
    public required string Name { get; init; }
    public required string Root { get; init; }
    public required SuiteFile Suite { get; init; }

    /// <summary>The loadable plugin root, for <c>--plugin-dir</c>.</summary>
    public required string Plugin { get; init; }

    /// <summary>The folder holding the SKILL.md under test.</summary>
    public required string Skill { get; init; }

    /// <summary>
    /// Issue #30. What a firing run loads BESIDE the distractor set, so the skill under test is
    /// in the listing layer 3 decides from. Empty when the distractors already declare it.
    ///
    /// The distractor set carries a description-only stub of every fixture skill, so the first
    /// suite needed nothing here and the requirement was invisible. A catalogue skill is read from
    /// plugins/ and never copied, so nothing puts it among the distractors: a run that loaded the
    /// distractors alone would show the model twelve skills unrelated to the prompt, miss every time,
    /// and report it as a description that will not fire.
    /// </summary>
    public required IReadOnlyList<string> ListingPlugins { get; init; }

    public string SkillFile => Path.Combine(Skill, SuiteDiscovery.SkillFileName);
    public string Breaks => Path.Combine(Root, SuiteDiscovery.BreaksFolder);

    /// <summary>
    /// Does this suite declare break overlays at all? #6's differential has nothing to lay without
    /// them. An empty <c>breaks/</c> folder declares none: the arms name groups inside it, and a
    /// folder holding no group answers every one of them with nothing.
    /// </summary>
    public bool DeclaresBreaks => Directory.Exists(Breaks) && Directory.GetDirectories(Breaks).Length > 0;

    /// <summary>Where a pass writes its journal and its rendered report. Created by the pass, not by discovery.</summary>
    public string RunRecords => Path.Combine(Root, SuiteDiscovery.RunsFolder);

    /// <summary>
    /// A break overlay, by the identifier an arm names. Unknown throws, for the reason the overlay
    /// builder throws on a path that matches nothing: a break that quietly resolves to the wrong
    /// material runs the wrong fixture and reports it under this suite's name.
    ///
    /// #26 grouped the overlays by break, so `breaks/` now has an inner level and an identifier can
    /// land on a GROUP rather than an overlay. That is the same bug wearing a new hat, so a group is
    /// refused here and named, rather than left to fail later as a pile of unmatched files.
    /// </summary>
    public string BreakOverlay(string id)
    {
        var resolved = Path.GetFullPath(Path.Combine(Breaks, id));

        if (!HarnessPaths.Inside(Breaks, resolved) || !Directory.Exists(resolved))
            throw new InvalidOperationException(
                $"suite '{Name}' has no break overlay '{id}'. Overlays are resolved inside {Breaks}.");

        // An overlay is laid over a plugin, so its top level is a plugin's: skills/, and nothing else
        // the harness overlays. A folder without one holds overlays rather than being one.
        if (!Directory.Exists(Path.Combine(resolved, SuiteDiscovery.SkillsFolder)))
            throw new InvalidOperationException(
                $"suite '{Name}': break overlay '{id}' has no {SuiteDiscovery.SkillsFolder}/ in it, so it groups "
                + $"overlays rather than being one. Name one of: {string.Join(", ", BreakOverlaysIn(id))}");

        return resolved;
    }

    /// <summary>
    /// Issue #27. Every overlay inside one break group, by the identifier <see cref="BreakOverlay"/>
    /// takes. A screen then runs over the candidates a suite DECLARES rather than a list typed into a
    /// test, so a second skill screens its own without anyone editing shared code.
    ///
    /// A group no suite declares lists nothing. It is the one place here where emptiness is an answer:
    /// a suite is not obliged to declare candidates, and a screen with nothing to screen is a fact.
    /// </summary>
    public IReadOnlyList<string> BreakOverlaysIn(string group)
    {
        var resolved = Path.GetFullPath(Path.Combine(Breaks, group));

        if (!HarnessPaths.Inside(Breaks, resolved))
            throw new InvalidOperationException(
                $"suite '{Name}': break group '{group}' resolves outside {Breaks}.");

        if (!Directory.Exists(resolved)) return [];

        return [.. Directory.GetDirectories(resolved).Order(StringComparer.Ordinal)
            .Select(d => $"{group}/{Path.GetFileName(d)}")];
    }
}

/// <summary>
/// Issue #24. Folder to suite. Every layer, every long pass and every free test that reads a suite
/// comes through here, so the set of skills under test is decided by the filesystem rather than by a
/// filename typed into fourteen test files.
///
/// STRICT on purpose. A folder with no suite file, a declared name that disagrees with its folder, a
/// suite with no cases and a suite naming an assertion that does not exist are each an error rather
/// than a skip. Every one of them is a silent pass waiting to happen, which is the failure this
/// harness exists to prevent.
/// </summary>
public sealed class SuiteDiscovery(string suitesRoot, string shippedCatalogue, string distractors)
{
    public const string SuiteFileName = "suite.json";
    public const string SkillFileName = "SKILL.md";
    public const string PluginFolder = "plugin";
    public const string BreaksFolder = "breaks";
    public const string RunsFolder = "runs";
    /// <summary>A plugin's skills live here, and so do an overlay's. It is what tells an overlay from a group.</summary>
    public const string SkillsFolder = "skills";

    public static SuiteDiscovery For(HarnessPaths paths) =>
        new(paths.Suites, paths.ShippedCatalogue, paths.Distractors);

    public IReadOnlyList<DiscoveredSuite> Discover()
    {
        // A root that is not there is a misconfiguration, and returning nothing would read as a green
        // gate over zero skills. An empty root is a different thing and is allowed to be empty.
        if (!Directory.Exists(suitesRoot))
            throw new DirectoryNotFoundException($"suites root not found: {suitesRoot}");

        // #30. The distractor set decides what each suite has to load, and Catalogue.Load reads
        // a missing folder as no skills. Left unchecked, a renamed shared/distractors/ would hand every
        // suite its own plugin, run layer 3 against a listing of one, and pass.
        if (!Directory.Exists(distractors))
            throw new DirectoryNotFoundException($"distractor set not found: {distractors}");

        return [.. Directory.GetDirectories(suitesRoot).Order(StringComparer.Ordinal).Select(Read)];
    }

    /// <summary>
    /// Issue #26. One suite by name, for a layer or a test that measures a named skill. It exists so
    /// that naming a suite is the only thing a caller does: nobody spells out a folder, and a name
    /// that is not on disk says so here rather than by way of a file-not-found three calls later.
    /// </summary>
    public DiscoveredSuite One(string name)
    {
        var all = Discover();
        return all.SingleOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"no suite named '{name}' under {suitesRoot}. Found: {(all.Count == 0 ? "nothing" : string.Join(", ", all.Select(s => s.Name)))}.");
    }

    public static DiscoveredSuite One(HarnessPaths paths, string name) => For(paths).One(name);

    private DiscoveredSuite Read(string dir)
    {
        var folder = Path.GetFileName(dir);
        var file = Path.Combine(dir, SuiteFileName);

        if (!File.Exists(file))
            throw new InvalidOperationException($"suite folder '{folder}' has no {SuiteFileName}: {dir}");

        var suite = SuiteFile.Load(file);

        if (!string.Equals(suite.Suite, folder, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"suite folder '{folder}' holds a suite declaring the name '{suite.Suite}'. A suite is named by its "
                + $"folder, so one of the two is a copy-paste: {file}");

        var cases = suite.Firing.ShouldFire.Count + suite.Firing.ShouldNotFire.Count
                  + suite.Firing.Watch.Count + suite.Contract.Count;
        if (cases == 0)
            throw new InvalidOperationException(
                $"suite '{folder}' has no cases. An empty suite passes every layer by running nothing: {file}");

        foreach (var c in suite.Contract) CheckAssertions(folder, c, file);

        // A fixture skill lives inside its own suite, so a deliberately broken one can never be
        // mistaken for catalogue content. A catalogue skill is read from plugins/ at run time and
        // never copied, because a copy drifts the moment the original is edited.
        var (plugin, skill) = suite.Source switch
        {
            SkillSource.Fixture => Locate(suite, folder, Path.Combine(dir, PluginFolder), [Path.Combine(dir, PluginFolder)]),
            SkillSource.Catalogue => Locate(suite, folder, shippedCatalogue, PluginsIn(shippedCatalogue)),
            _ => throw new NotSupportedException($"suite '{folder}': unknown source '{suite.Source}'"),
        };

        // Asked of the DISTRACTORS, not of the source. A fixture suite whose skill nobody stubbed
        // needs its plugin in the listing for the same reason a catalogue suite does, and keying on
        // the source would answer that one wrongly.
        var stubbed = Catalogue.Load(distractors)
            .Any(s => string.Equals(s.Name, suite.SkillUnderTest, StringComparison.Ordinal));

        return new DiscoveredSuite
        {
            Name = folder,
            Root = dir,
            Suite = suite,
            Plugin = plugin,
            Skill = skill,
            ListingPlugins = stubbed ? [] : [plugin],
        };
    }

    /// <summary>#23 item 29. A typo here costs a second. Found at layer 4 it costs a paid pass.</summary>
    private static void CheckAssertions(string name, ContractCase c, string file)
    {
        try
        {
            AssertionCatalogue.Resolve(c);
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                $"suite '{name}' case '{c.Id}' names the assertion set '{c.Assertions}', which does not resolve "
                + $"({ex.Message}). Known sets: {string.Join(", ", AssertionCatalogue.Known)}. {file}", ex);
        }
    }

    private static IReadOnlyList<string> PluginsIn(string root) =>
        Directory.Exists(root) ? Directory.GetDirectories(root) : [];

    /// <summary>
    /// The skill under test, found by name among the plugins the source offers. Both sources go
    /// through the catalogue loader, so "the skill named X" means one thing in this harness: the
    /// name its frontmatter declares, not the name of the folder somebody put it in.
    /// </summary>
    private static (string Plugin, string Skill) Locate(
        SuiteFile suite, string name, string where, IReadOnlyList<string> plugins)
    {
        List<(string Plugin, string Skill)> found =
            [.. plugins.SelectMany(p => Catalogue.Load(p)
                .Where(s => string.Equals(s.Name, suite.SkillUnderTest, StringComparison.Ordinal))
                .Select(s => (Plugin: p, Skill: Path.GetDirectoryName(s.File)!)))];

        return found.Count switch
        {
            1 => found[0],
            0 => throw new InvalidOperationException(
                $"suite '{name}' declares source {suite.Source.ToString().ToLowerInvariant()}, but nothing in "
                + $"{where} declares a skill named '{suite.SkillUnderTest}'."),
            _ => throw new InvalidOperationException(
                $"suite '{name}': more than one plugin in {where} declares a skill named '{suite.SkillUnderTest}' "
                + $"({string.Join(", ", found.Select(f => Path.GetFileName(f.Plugin)))}), so the suite cannot say which it tests."),
        };
    }
}

/// <summary>
/// Issue #27. The list every layer and every long pass runs over.
///
/// A layer that loaded ONE suite widened nothing when a folder landed beside it, and nothing went red
/// to say so. No layer names a suite now: each enumerates this, so adding a folder widens every layer
/// at once with no further wiring.
///
/// It sits in the source project rather than in a test project because the FREE layer has to be able
/// to check it. The paid layers live in an assembly the free gate never loads, so a data source
/// declared over there is one nothing on CI can read.
/// </summary>
public static class SuitesUnderTest
{
    public static IReadOnlyList<DiscoveredSuite> All(HarnessPaths? paths = null) =>
        SuiteDiscovery.For(paths ?? new HarnessPaths()).Discover();

    /// <summary>
    /// Theory rows, one per suite. A row carries the suite's NAME rather than the suite, because a row
    /// is written into the test's name and read back to re-run that one case, and a name survives the
    /// trip where a whole object does not.
    ///
    /// An empty suites root yields no rows, and a theory that finds no data fails. That is the point:
    /// a layer with nothing to run must not read as a layer that passed.
    /// </summary>
    public static IEnumerable<object[]> Rows() => [.. All().Select(s => new object[] { s.Name })];
}
