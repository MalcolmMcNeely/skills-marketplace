using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #26. The real suite folder, as opposed to the temp ones <see cref="SuiteDiscoveryTests"/>
/// builds. Discovery proves the RULE; this proves the material on disk obeys it, which is the half a
/// temp-directory test can never reach.
///
/// What the folder is for: everything csharp-new-class is tested on used to sit in four top-level
/// folders, joined only by a filename typed into fourteen test files. Opening one folder now answers
/// the question, and these tests are what keeps it answering it.
/// </summary>
public class SuiteFolderTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    /// <summary>
    /// The one test in the repo that spells the folder names out. A pin has to: asserting
    /// <c>RunRecords</c> equals <c>Root</c> plus <see cref="SuiteDiscovery.RunsFolder"/> restates the
    /// implementation and checks nothing. Everywhere else asks the vocabulary instead.
    /// </summary>
    [Fact]
    public void Everything_the_suite_is_tested_on_sits_in_one_folder()
    {
        Assert.Equal(Path.Combine(Paths.Root, "skills", "csharp-new-class"), Found.Root);
        Assert.Equal(Path.Combine(Found.Root, "plugin"), Found.Plugin);
        Assert.Equal(Path.Combine(Found.Root, "breaks"), Found.Breaks);
        Assert.Equal(Path.Combine(Found.Root, "runs"), Found.RunRecords);

        Assert.True(File.Exists(Path.Combine(Found.Root, "suite.json")));
        Assert.True(File.Exists(Path.Combine(Found.Plugin, ".claude-plugin", "plugin.json")));
        Assert.True(File.Exists(Found.SkillFile));
        Assert.True(Directory.Exists(Found.Breaks));
        Assert.True(Directory.Exists(Found.RunRecords));
    }

    /// <summary>
    /// Grouped by the BREAK, not by the base it overlays. Two folders called `description-catalogue`
    /// and `description-plugin` read as two breaks; one `description` folder holding two shapes reads
    /// as what it is, which is one break measured at two layers.
    /// </summary>
    [Fact]
    public void The_overlays_are_grouped_by_the_break_they_perform()
    {
        var breaks = Directory.GetDirectories(Found.Breaks);
        Assert.Equal(["body", "candidates", "control", "description"], breaks.Select(Path.GetFileName).Order());

        var description = breaks.Single(d => Path.GetFileName(d) == "description");
        Assert.Equal(["catalogue", "plugin"], Directory.GetDirectories(description).Select(Path.GetFileName).Order());
    }

    /// <summary>
    /// Grouping gave `breaks/` an inner level, and with it a new way for an identifier to resolve to
    /// real material that is not an overlay. It is the mistyped-path bug in a new shape, so the free
    /// layer has to refuse it rather than hand a pass a folder full of files that match nothing.
    /// </summary>
    [Fact]
    public void An_identifier_that_lands_on_a_group_rather_than_an_overlay_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Found.BreakOverlay("description"));

        Assert.Contains("groups", ex.Message, StringComparison.Ordinal);
        Assert.Contains("description/catalogue", ex.Message, StringComparison.Ordinal);
        Assert.Contains("description/plugin", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two description shapes are not copies of each other. Layer 3 never reads a body, so the
    /// catalogue shape stays a description-only stub; layer 4 reads nothing else, so the plugin shape
    /// carries the full body. One file serving both would test a stub body at layer 4.
    /// </summary>
    [Fact]
    public void The_catalogue_shape_is_a_stub_and_the_plugin_shape_carries_the_full_body()
    {
        var skill = Path.Combine("skills", Found.Suite.SkillUnderTest, "SKILL.md");
        var catalogue = FixtureBuilder.BodyOf(Path.Combine(Found.BreakOverlay("description/catalogue"), skill));
        var plugin = FixtureBuilder.BodyOf(Path.Combine(Found.BreakOverlay("description/plugin"), skill));

        Assert.Equal(FixtureBuilder.BodyOf(Path.Combine(Paths.StubCatalogue, skill)), catalogue);
        Assert.Equal(FixtureBuilder.BodyOf(Found.SkillFile), plugin);
        Assert.True(plugin.Length > catalogue.Length * 2, "the plugin shape should carry a real body, not a stub");
    }

    /// <summary>
    /// A long pass resumes by reading back what it wrote, so its records belong with the suite it
    /// measured. Before #26 they sat in a shared `captured/` folder where one skill's run history read
    /// as everyone's.
    /// </summary>
    [Fact]
    public void The_records_of_this_suites_paid_passes_sit_under_the_suite()
    {
        Assert.True(Directory.Exists(Path.Combine(Found.RunRecords, "breakage-20260909-090853")));
        Assert.True(File.Exists(Path.Combine(Found.RunRecords, "calibration-run-1.jsonl")));
        Assert.True(File.Exists(Path.Combine(Found.RunRecords, "calibration-run-1.md")));
    }

    /// <summary>
    /// Resume, against the journal that moved rather than a synthetic one. A long pass resumes by
    /// reading back what it wrote, so the move had exactly one way to go wrong: a journal that no
    /// longer reads is a pass paid for twice, and #12's was 133 runs and 02:08:14.
    /// </summary>
    [Fact]
    public void A_pass_resumed_off_the_moved_journal_has_nothing_left_to_run()
    {
        var journal = Path.Combine(Found.RunRecords, "calibration-run-1.jsonl");
        var done = CalibrationPass.ValidRunsByCase(journal);

        Assert.NotEmpty(done);
        foreach (var step in new CalibrationPass(Paths, Found.Suite).Plan())
            Assert.True(done.GetValueOrDefault(step.Id) >= step.Runs,
                $"{step.Id}: {done.GetValueOrDefault(step.Id)} valid runs on disk, {step.Runs} wanted");
    }

    /// <summary>
    /// Every folder the harness owns, named. Material read from two places at once drifts, and the
    /// copy nobody edits is the one a pass measures, so a sixth folder appearing is a question rather
    /// than a detail. This is also what catches `cases/`, `fixtures/` or `captured/` coming back.
    ///
    /// Dot-folders are skipped. An editor drops `.idea` or `.vs` next to the source and neither is a
    /// claim about where the harness keeps its material.
    /// </summary>
    [Fact]
    public void The_harness_owns_five_folders_and_the_old_ones_are_not_among_them()
    {
        Assert.Equal(
            ["shared", "skills", "src", "tests", "tools"],
            Directory.GetDirectories(Paths.Root).Select(Path.GetFileName)
                .Where(f => !f!.StartsWith('.')).Order());
    }

    /// <summary>
    /// #27. A group lists what it holds, so the ladder screens the candidates this suite declares
    /// rather than a list typed into the test. A second skill then screens its own without anyone
    /// editing shared code.
    /// </summary>
    [Fact]
    public void A_break_group_lists_the_overlays_it_holds()
    {
        var candidates = Found.BreakOverlaysIn("candidates");

        Assert.Equal(
            ["candidates/boundary-inverted", "candidates/vague-label", "candidates/wrong-subject"],
            candidates);
        foreach (var id in candidates) Assert.True(Directory.Exists(Found.BreakOverlay(id)));
    }
}
