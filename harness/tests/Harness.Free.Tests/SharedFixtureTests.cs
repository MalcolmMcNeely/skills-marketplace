using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #25. Three pieces of test material are borrowed by every suite rather than owned by one.
/// They sit under <c>harness/shared/</c> so a reader can tell shared material from per-skill material
/// by where it sits, and nothing else has to be read to know which is which.
/// </summary>
public class SharedFixtureTests
{
    private static readonly HarnessPaths Paths = new();

    [Theory]
    [InlineData("catalogue")]
    [InlineData("repo")]
    [InlineData("streams")]
    public void Every_borrowed_fixture_sits_directly_under_shared(string folder)
    {
        var expected = Path.Combine(Paths.Shared, folder);
        Assert.True(Directory.Exists(expected), $"missing shared fixture: {expected}");
    }

    [Fact]
    public void The_path_vocabulary_points_at_the_shared_copies()
    {
        Assert.Equal(Path.Combine(Paths.Shared, "catalogue"), Paths.StubCatalogue);
        Assert.Equal(Path.Combine(Paths.Shared, "repo"), Paths.FixtureRepo);
        Assert.Equal(Path.Combine(Paths.Shared, "streams"), Paths.Streams);
    }

    [Fact]
    public void The_distractor_catalogue_still_holds_the_twelve_skills_layer_3_measures()
    {
        Assert.Equal(12, Catalogue.Load(Paths.StubCatalogue).Count);
    }

    [Fact]
    public void The_scratch_repo_is_still_copyable_and_still_bare()
    {
        var copy = Paths.NewScratchRepo();

        Assert.True(Directory.Exists(Path.Combine(copy, "src")));
        Assert.True(Directory.Exists(Path.Combine(copy, "tests")));
    }

    /// <summary>The streams the offline parser tests read. Test data, not one skill's run history.</summary>
    [Theory]
    [InlineData("two-skills-fired.jsonl")]
    [InlineData("budget-abort.jsonl")]
    [InlineData("nothing-fired-success.jsonl")]
    [InlineData("heredoc-both-files.jsonl")]
    [InlineData("real-layer4-held.jsonl")]
    [InlineData("real-layer4-no-skill-call.jsonl")]
    public void Every_stream_the_parser_tests_read_sits_in_shared_streams(string name)
    {
        Assert.True(File.Exists(Paths.Stream(name)), $"missing captured stream: {Paths.Stream(name)}");
        Assert.StartsWith(Paths.Streams + Path.DirectorySeparatorChar, Paths.Stream(name), StringComparison.Ordinal);
    }

    /// <summary>
    /// #25 draws one line and no more. A run record is written by a paid pass, and the long passes
    /// resume by reading back what they wrote, so moving one costs money to discover.
    /// </summary>
    [Fact]
    public void Real_run_records_are_untouched_and_still_sit_in_captured()
    {
        Assert.Equal(Path.Combine(Paths.Root, "captured"), Paths.Captured);
        Assert.True(Directory.Exists(Path.Combine(Paths.Captured, "breakage-20260909-090853")));
        Assert.True(File.Exists(Path.Combine(Paths.Captured, "calibration-run-1.jsonl")));
    }
}
