using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #24. The seam that turns a folder into a suite. Every case here builds its suites in a temp
/// directory, the shape the layer 2 breakage tests already use, because a discovery rule that has
/// never seen a broken folder is a rule nobody has tested.
///
/// Discovery is STRICT by design. A half-finished suite folder is a silent pass waiting to happen,
/// and this harness exists to stop silent passes.
/// </summary>
public class SuiteDiscoveryTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "harness-discovery-test", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string SkillMd(string name) => $"""
        ---
        name: {name}
        description: Use when adding a new C# class.
        ---
        # body
        """;

    private static string SuiteJson(
        string suite = "csharp-new-class",
        string skillUnderTest = "csharp-new-class",
        string source = "fixture",
        string firing = """{ "shouldFire": [ { "id": "P1", "runs": 6, "expect": ["csharp-new-class"], "prompt": "Add a Discount class." } ] }""",
        string contract = "[]") =>
        $$"""
        {
          "suite": "{{suite}}",
          "skillUnderTest": "{{skillUnderTest}}",
          "source": "{{source}}",
          "firing": {{firing}},
          "contract": {{contract}}
        }
        """;

    /// <summary>A whole, well-formed fixture suite: the file, the skill it tests and one break overlay.</summary>
    private static string Suite(string root, string name = "csharp-new-class", string? json = null)
    {
        var dir = Path.Combine(root, name);
        Write(Path.Combine(dir, "suite.json"), json ?? SuiteJson(suite: name, skillUnderTest: name));
        Write(Path.Combine(dir, "plugin", "skills", name, "SKILL.md"), SkillMd(name));
        Write(Path.Combine(dir, "breaks", "description", "catalogue", "skills", name, "SKILL.md"), SkillMd(name));
        return dir;
    }

    private static SuiteDiscovery Discovery(string suitesRoot, string? shippedCatalogue = null) =>
        new(suitesRoot, shippedCatalogue ?? Path.Combine(suitesRoot, "no-catalogue"));

    [Fact]
    public void A_well_formed_folder_is_discovered_with_its_name_its_cases_and_its_paths()
    {
        var root = TempDir();
        var dir = Suite(root);

        var found = Assert.Single(Discovery(root).Discover());

        Assert.Equal("csharp-new-class", found.Name);
        Assert.Equal(dir, found.Root);
        Assert.Equal("P1", Assert.Single(found.Suite.Firing.ShouldFire).Id);
        Assert.Equal(Path.Combine(dir, "plugin"), found.Plugin);
        Assert.Equal(Path.Combine(dir, "plugin", "skills", "csharp-new-class"), found.Skill);
        Assert.Equal(Path.Combine(dir, "plugin", "skills", "csharp-new-class", "SKILL.md"), found.SkillFile);
        Assert.Equal(Path.Combine(dir, "runs"), found.RunRecords);
    }

    [Fact]
    public void Two_folders_yield_two_suites()
    {
        var root = TempDir();
        Suite(root, "csharp-new-class");
        Suite(root, "data-sql");

        var found = Discovery(root).Discover();

        Assert.Equal(["csharp-new-class", "data-sql"], found.Select(s => s.Name).Order());
    }

    [Fact]
    public void An_empty_suites_root_yields_nothing_rather_than_throwing()
    {
        Assert.Empty(Discovery(TempDir()).Discover());
    }

    /// <summary>A missing root is a misconfiguration, not an empty catalogue. It must not read as zero suites.</summary>
    [Fact]
    public void A_suites_root_that_does_not_exist_throws()
    {
        var root = Path.Combine(TempDir(), "typo");

        var ex = Assert.Throws<DirectoryNotFoundException>(() => Discovery(root).Discover());
        Assert.Contains(root, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_with_no_suite_file_throws_naming_the_folder()
    {
        var root = TempDir();
        Directory.CreateDirectory(Path.Combine(root, "half-finished"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("half-finished", ex.Message, StringComparison.Ordinal);
        Assert.Contains("suite.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_name_that_disagrees_with_the_folder_name_throws_naming_both()
    {
        var root = TempDir();
        Suite(root, "data-sql", json: SuiteJson(suite: "copied-from-somewhere-else", skillUnderTest: "data-sql"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("copied-from-somewhere-else", ex.Message, StringComparison.Ordinal);
        Assert.Contains("data-sql", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suite_with_no_firing_cases_and_no_contract_cases_throws()
    {
        var root = TempDir();
        Suite(root, "empty-suite", json: SuiteJson(suite: "empty-suite", skillUnderTest: "empty-suite", firing: "{}"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("empty-suite", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no cases", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A suite with contract cases only is a suite. Emptiness is about both halves, not one.</summary>
    [Fact]
    public void A_suite_with_contract_cases_only_is_discovered()
    {
        var root = TempDir();
        Suite(root, "contract-only", json: SuiteJson(
            suite: "contract-only", skillUnderTest: "contract-only", firing: "{}",
            contract: """[ { "id": "C1", "task": "Add a Discount class.", "assertions": "TestFirstFilesOnly", "assertionArgs": { "className": "Discount" } } ]"""));

        Assert.Single(Discovery(root).Discover());
    }

    [Fact]
    public void A_fixture_suite_resolves_its_skill_inside_its_own_folder()
    {
        var root = TempDir();
        var dir = Suite(root);

        var found = Assert.Single(Discovery(root).Discover());

        Assert.Equal(SkillSource.Fixture, found.Suite.Source);
        Assert.StartsWith(dir, found.Skill, StringComparison.Ordinal);
        Assert.True(File.Exists(found.SkillFile));
    }

    [Fact]
    public void A_fixture_suite_with_no_skill_in_its_folder_throws()
    {
        var root = TempDir();
        var dir = Suite(root);
        Directory.Delete(Path.Combine(dir, "plugin"), recursive: true);

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("csharp-new-class", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of the catalogue source: the tested text IS the shipped text. Never copied, because a
    /// copy drifts the moment the original is edited and then tests text nobody ships.
    /// </summary>
    [Fact]
    public void A_catalogue_suite_resolves_its_skill_under_the_shipped_catalogue()
    {
        var root = TempDir();
        var catalogue = TempDir();
        Write(Path.Combine(catalogue, "core", "skills", "skill-authoring", "SKILL.md"), SkillMd("skill-authoring"));
        Write(Path.Combine(root, "skill-authoring", "suite.json"),
            SuiteJson(suite: "skill-authoring", skillUnderTest: "skill-authoring", source: "catalogue"));

        var found = Assert.Single(Discovery(root, catalogue).Discover());

        Assert.Equal(SkillSource.Catalogue, found.Suite.Source);
        Assert.Equal(Path.Combine(catalogue, "core"), found.Plugin);
        Assert.Equal(Path.Combine(catalogue, "core", "skills", "skill-authoring"), found.Skill);
    }

    [Fact]
    public void A_catalogue_suite_naming_a_skill_that_is_not_shipped_throws()
    {
        var root = TempDir();
        var catalogue = TempDir();
        Write(Path.Combine(catalogue, "core", "skills", "skill-authoring", "SKILL.md"), SkillMd("skill-authoring"));
        Write(Path.Combine(root, "never-shipped", "suite.json"),
            SuiteJson(suite: "never-shipped", skillUnderTest: "never-shipped", source: "catalogue"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root, catalogue).Discover());
        Assert.Contains("never-shipped", ex.Message, StringComparison.Ordinal);
        Assert.Contains(catalogue, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Two plugins, one name. A suite that cannot say which one it tests must not pick one.</summary>
    [Fact]
    public void A_skill_two_plugins_both_declare_throws_naming_both_plugins()
    {
        var root = TempDir();
        var catalogue = TempDir();
        Write(Path.Combine(catalogue, "core", "skills", "skill-authoring", "SKILL.md"), SkillMd("skill-authoring"));
        Write(Path.Combine(catalogue, "extra", "skills", "skill-authoring", "SKILL.md"), SkillMd("skill-authoring"));
        Write(Path.Combine(root, "skill-authoring", "suite.json"),
            SuiteJson(suite: "skill-authoring", skillUnderTest: "skill-authoring", source: "catalogue"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root, catalogue).Discover());
        Assert.Contains("core", ex.Message, StringComparison.Ordinal);
        Assert.Contains("extra", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A skill is named by its frontmatter, which is the name the model reads and the name layer 2
    /// pins to the folder. A suite that matched on the folder alone would test a skill by one name
    /// and report it under another.
    /// </summary>
    [Fact]
    public void A_skill_whose_frontmatter_names_something_else_is_not_the_skill_under_test()
    {
        var root = TempDir();
        var dir = Suite(root, "csharp-new-class");
        Write(Path.Combine(dir, "plugin", "skills", "csharp-new-class", "SKILL.md"), SkillMd("something-else"));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("csharp-new-class", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A typo costs nothing here and a model run everywhere else. #23 item 29.</summary>
    [Fact]
    public void A_suite_naming_an_unknown_contract_assertion_fails_in_the_free_layer()
    {
        var root = TempDir();
        Suite(root, "csharp-new-class", json: SuiteJson(
            contract: """[ { "id": "C1", "task": "Add a Discount class.", "assertions": "TestFirstFilesOnl" } ]"""));

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).Discover());
        Assert.Contains("C1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("TestFirstFilesOnl", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #23 item 25. The path vocabulary is the only place that knows the folder name, so a layer asks
    /// it rather than spelling one out. Proven against a temp harness root, because the real
    /// harness/skills/ folder arrives with the move and this ticket moves nothing.
    /// </summary>
    [Fact]
    public void Discovery_reads_its_root_from_the_path_vocabulary()
    {
        var harness = TempDir();
        Suite(Directory.CreateDirectory(Path.Combine(harness, "skills")).FullName);
        var paths = new HarnessPaths(harness);

        Assert.Equal(Path.Combine(harness, "skills"), paths.Suites);
        Assert.Equal("csharp-new-class", Assert.Single(SuiteDiscovery.For(paths).Discover()).Name);
    }

    /// <summary>#26. Naming a suite is the only thing a caller does, so the name has to be checked.</summary>
    [Fact]
    public void One_returns_the_suite_with_that_name()
    {
        var root = TempDir();
        Suite(root, "csharp-new-class");
        Suite(root, "data-sql");

        Assert.Equal("data-sql", Discovery(root).One("data-sql").Name);
    }

    [Fact]
    public void One_throws_on_a_name_no_folder_declares_and_says_what_it_found()
    {
        var root = TempDir();
        Suite(root, "csharp-new-class");

        var ex = Assert.Throws<InvalidOperationException>(() => Discovery(root).One("csharp-new-clas"));
        Assert.Contains("csharp-new-clas", ex.Message, StringComparison.Ordinal);
        Assert.Contains("csharp-new-class", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_break_overlay_resolves_by_identifier()
    {
        var root = TempDir();
        var dir = Suite(root);

        var found = Assert.Single(Discovery(root).Discover());

        Assert.Equal(Path.Combine(dir, "breaks", "description", "catalogue"), found.BreakOverlay("description/catalogue"));
    }

    [Fact]
    public void An_unknown_break_overlay_identifier_throws()
    {
        var root = TempDir();
        Suite(root);

        var found = Assert.Single(Discovery(root).Discover());

        var ex = Assert.Throws<InvalidOperationException>(() => found.BreakOverlay("description/plugin"));
        Assert.Contains("description/plugin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("csharp-new-class", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same class of bug as the overlay that matches nothing: an identifier that climbs out of the
    /// suite resolves to real material under the wrong name, and the run reports it under this suite.
    /// </summary>
    [Fact]
    public void A_break_overlay_identifier_that_climbs_out_of_the_suite_throws()
    {
        var root = TempDir();
        Suite(root, "csharp-new-class");
        Suite(root, "data-sql");

        var found = Discovery(root).Discover().Single(s => s.Name == "csharp-new-class");

        Assert.Throws<InvalidOperationException>(() => found.BreakOverlay("../../data-sql/breaks/description/catalogue"));
    }

    /// <summary>
    /// #27. A suite is not obliged to declare candidates, and a screen with nothing to screen is a
    /// fact rather than a fault. It is the one place in discovery where emptiness is an answer.
    /// </summary>
    [Fact]
    public void A_break_group_no_suite_declares_lists_nothing()
    {
        var root = TempDir();
        Suite(root);

        Assert.Empty(Assert.Single(Discovery(root).Discover()).BreakOverlaysIn("candidates"));
    }

    [Fact]
    public void A_break_group_that_climbs_out_of_the_suite_throws()
    {
        var root = TempDir();
        Suite(root);

        Assert.Throws<InvalidOperationException>(
            () => Assert.Single(Discovery(root).Discover()).BreakOverlaysIn("../../elsewhere"));
    }
}
