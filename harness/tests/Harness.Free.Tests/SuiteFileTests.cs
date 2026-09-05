using Harness;
using Xunit;

namespace Harness.Free.Tests;

public class SuiteFileTests
{
    private static readonly HarnessPaths Paths = new();

    [Fact]
    public void The_case_file_loads_and_matches_issue_10()
    {
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));

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
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));
        foreach (var c in suite.Contract) Assert.NotNull(AssertionCatalogue.Resolve(c));
    }

    [Fact]
    public void No_positive_prompt_names_the_skill_or_says_test_first()
    {
        var suite = SuiteFile.Load(Path.Combine(Paths.Cases, "csharp-new-class.json"));
        foreach (var c in suite.Firing.ShouldFire)
        {
            Assert.DoesNotContain(suite.SkillUnderTest, c.Prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test first", c.Prompt, StringComparison.OrdinalIgnoreCase);
        }
    }
}
