using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #28. The coverage rule, against catalogues built to be uncovered.
///
/// The sweep in <see cref="Layer2_IntegrityTests"/> runs this rule over the real catalogue and is red
/// on purpose, so it can never prove the rule's green half. Only a catalogue nobody ships can do
/// that, and only a temp one can be edited case by case. Both halves matter: a rule that fails
/// everything gates nothing once somebody writes the suite it asked for.
///
/// Each case builds a whole small repo, harness beside plugins, because <see cref="HarnessPaths"/> is
/// what joins the two roots and a test that bypassed it would prove the join nowhere.
/// </summary>
public class CatalogueCoverageTests : IDisposable
{
    private readonly string _repo =
        Path.Combine(Path.GetTempPath(), "harness-coverage-test", Guid.NewGuid().ToString("N")[..8]);

    private readonly HarnessPaths _paths;

    public CatalogueCoverageTests()
    {
        _paths = new HarnessPaths(Path.Combine(_repo, "harness"));

        // Discovery refuses a missing suites root, which is a misconfiguration rather than an empty
        // catalogue. A repo shipping skills and testing none of them has the folder, empty.
        Directory.CreateDirectory(_paths.Suites);

        // It refuses a missing distractor catalogue for the same reason. Coverage says nothing about
        // distractors, but it comes through the same discovery, so the folder has to be there.
        Directory.CreateDirectory(_paths.StubCatalogue);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>One skill in the shipped catalogue. An entry point carries the flag; an engine does not.</summary>
    private void Ship(string name, bool entryPoint = false)
    {
        var flag = entryPoint ? "\ndisable-model-invocation: true" : "";
        Write(Path.Combine(_paths.ShippedCatalogue, "core", "skills", name, "SKILL.md"),
            $"---\nname: {name}\ndescription: Use when testing.{flag}\n---\n\nBody here.\n");
    }

    /// <summary>A suite folder, of either source. A fixture suite gets the copy it tests.</summary>
    private void Suite(string name, string skillUnderTest, string source = "catalogue")
    {
        var dir = Path.Combine(_paths.Suites, name);
        Write(Path.Combine(dir, "suite.json"), $$"""
            {
              "suite": "{{name}}",
              "skillUnderTest": "{{skillUnderTest}}",
              "source": "{{source}}",
              "firing": { "shouldFire": [ { "id": "P1", "runs": 6, "expect": ["{{skillUnderTest}}"], "prompt": "Do the thing." } ] },
              "contract": []
            }
            """);

        if (source == "fixture")
            Write(Path.Combine(dir, "plugin", "skills", skillUnderTest, "SKILL.md"),
                $"---\nname: {skillUnderTest}\ndescription: Use when testing.\n---\n\nBody here.\n");
    }

    [Fact]
    public void An_engine_with_no_suite_is_reported_with_the_folder_it_wants()
    {
        Ship("alpha");

        var uncovered = Assert.Single(CatalogueCoverage.EnginesWithNoSuite(_paths));

        Assert.Equal("alpha", uncovered.Skill);
        Assert.Equal(Path.Combine(_paths.Suites, "alpha"), uncovered.SuiteFolder);
    }

    /// <summary>
    /// An entry point carries <c>disable-model-invocation: true</c>, so a developer types it and it has
    /// no firing behaviour to measure. Asking one for firing tests asks for tests it can never fail.
    /// </summary>
    [Fact]
    public void An_entry_point_with_no_suite_is_exempt()
    {
        Ship("beta", entryPoint: true);

        Assert.Empty(CatalogueCoverage.EnginesWithNoSuite(_paths));
    }

    [Fact]
    public void A_catalogue_where_every_engine_has_a_suite_is_covered()
    {
        Ship("alpha");
        Ship("gamma");
        Ship("beta", entryPoint: true);
        Suite("alpha", "alpha");
        Suite("gamma", "gamma");

        Assert.Empty(CatalogueCoverage.EnginesWithNoSuite(_paths));
    }

    [Fact]
    public void One_engine_covered_and_one_not_reports_only_the_one_that_is_not()
    {
        Ship("alpha");
        Ship("gamma");
        Suite("alpha", "alpha");

        Assert.Equal("gamma", Assert.Single(CatalogueCoverage.EnginesWithNoSuite(_paths)).Skill);
    }

    /// <summary>
    /// Coverage is decided on the FILE a suite resolves, not on a name two folders happen to share. A
    /// fixture suite tests its own copy, and a copy drifts the moment the shipped skill is edited, so
    /// it says nothing about the text developers get. Matching on the name alone would let a stale
    /// copy answer for the real one.
    /// </summary>
    [Fact]
    public void A_fixture_suite_of_the_same_name_does_not_cover_the_shipped_engine()
    {
        Ship("alpha");
        Suite("alpha", "alpha", source: "fixture");

        Assert.Equal("alpha", Assert.Single(CatalogueCoverage.EnginesWithNoSuite(_paths)).Skill);
    }

    /// <summary>A suite named for one skill and testing another covers the skill it tests.</summary>
    [Fact]
    public void A_suite_covers_the_skill_it_tests_rather_than_the_one_it_is_named_after()
    {
        Ship("alpha");
        Suite("alpha-firing", "alpha");

        Assert.Empty(CatalogueCoverage.EnginesWithNoSuite(_paths));
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo)) Directory.Delete(_repo, recursive: true);
    }
}
