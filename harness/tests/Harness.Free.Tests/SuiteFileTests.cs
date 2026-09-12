using Harness;
using Xunit;

namespace Harness.Free.Tests;

public class SuiteFileTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    private static string Written(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "harness-suite-file-test", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_case_file_loads_and_matches_issue_10()
    {
        var suite = Found.Suite;

        Assert.Equal(10, suite.Firing.ShouldFire.Count);
        Assert.Equal(10, suite.Firing.ShouldNotFire.Count);
        Assert.Equal(3, suite.Firing.Watch.Count);

        var runs = suite.Firing.ShouldFire.Sum(c => c.Runs)
                 + suite.Firing.ShouldNotFire.Sum(c => c.Runs)
                 + suite.Firing.Watch.Sum(c => c.Runs);
        Assert.Equal(125, runs);   // #10's stated pass size
    }

    [Fact]
    public void Every_contract_case_names_an_assertion_set_that_exists()
    {
        var suite = Found.Suite;
        foreach (var c in suite.Contract) Assert.NotNull(AssertionCatalogue.Resolve(c));
    }

    /// <summary>Issue #24. The suite says where its skill lives, and csharp-new-class is a fixture.</summary>
    [Fact]
    public void The_case_file_declares_where_its_skill_under_test_lives()
    {
        var suite = Found.Suite;

        Assert.Equal(SkillSource.Fixture, suite.Source);
    }

    [Fact]
    public void A_suite_file_that_declares_no_source_is_refused_naming_the_file()
    {
        var path = Written("""{ "suite": "x", "skillUnderTest": "x" }""");

        var ex = Assert.Throws<InvalidOperationException>(() => SuiteFile.Load(path));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        Assert.Contains("source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_source_that_is_neither_of_the_two_values_is_refused()
    {
        var path = Written("""{ "suite": "x", "skillUnderTest": "x", "source": "somewhere-else" }""");

        var ex = Assert.Throws<InvalidOperationException>(() => SuiteFile.Load(path));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_sources_are_read_from_the_lower_case_spelling_the_files_use()
    {
        Assert.Equal(SkillSource.Fixture,
            SuiteFile.Load(Written("""{ "suite": "x", "skillUnderTest": "x", "source": "fixture" }""")).Source);
        Assert.Equal(SkillSource.Catalogue,
            SuiteFile.Load(Written("""{ "suite": "x", "skillUnderTest": "x", "source": "catalogue" }""")).Source);
    }

    [Fact]
    public void No_positive_prompt_names_the_skill_or_says_test_first()
    {
        var suite = Found.Suite;
        foreach (var c in suite.Firing.ShouldFire)
        {
            Assert.DoesNotContain(suite.SkillUnderTest, c.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test first", c.Prompt, StringComparison.OrdinalIgnoreCase);
        }
    }
}
