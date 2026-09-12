using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #27. Which suite a resume override continues.
///
/// A long pass now runs over every discovered suite, so the single path the environment hands it has
/// to say which suite it belongs to. #26 already answered that by putting every journal under the
/// suite it measured, so the folder the path sits in is the answer.
///
/// The cost of getting this wrong is the reason it is tested: a path that resolved to nothing would
/// leave a finished suite looking unrun, and #12's pass was 133 runs and 02:08:14.
/// </summary>
public class ResumeTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly IReadOnlyList<DiscoveredSuite> Suites = SuitesUnderTest.All(Paths);
    private static readonly DiscoveredSuite Found = Suites[0];

    [Fact]
    public void Nothing_set_resumes_nothing()
    {
        Assert.Null(ResumeOverride.Resolve(null, Suites, Paths.Root));
        Assert.Null(ResumeOverride.Resolve("", Suites, Paths.Root));
    }

    [Fact]
    public void A_path_under_a_suites_run_records_resumes_that_suite()
    {
        var journal = Path.Combine(Found.RunRecords, "calibration-20260911-120000.jsonl");

        var point = ResumeOverride.Resolve(journal, Suites, Paths.Root);

        Assert.Equal(Found.Name, point!.Suite.Name);
        Assert.Equal(journal, point.Path);
    }

    /// <summary>
    /// MEASURED the hard way on #26: the test host runs from its own build output, so a relative
    /// override put a whole pass's journals under bin/Debug where nobody would look for them.
    /// </summary>
    [Fact]
    public void A_relative_path_resolves_against_the_harness_root_not_the_working_directory()
    {
        var relative = Path.Combine("skills", Found.Name, "runs", "breakage-20260911-120000");

        var point = ResumeOverride.Resolve(relative, Suites, Paths.Root);

        Assert.Equal(Found.Name, point!.Suite.Name);
        Assert.Equal(Path.Combine(Paths.Root, relative), point.Path);
    }

    /// <summary>
    /// Two suites, one path. The folder decides, and the suite that did not write it starts fresh
    /// rather than being skipped on somebody else's runs.
    /// </summary>
    [Fact]
    public void The_run_records_folder_decides_which_of_several_suites_is_resumed()
    {
        var root = Path.Combine(Path.GetTempPath(), "harness-resume-test", Guid.NewGuid().ToString("N")[..8]);
        // Path logic only, so no folder is built: what a pass creates is not what this resolves.
        DiscoveredSuite[] two =
            [Found with { Name = "first", Root = Path.Combine(root, "first") },
             Found with { Name = "second", Root = Path.Combine(root, "second") }];

        var point = ResumeOverride.Resolve(Path.Combine(two[1].RunRecords, "break-description.jsonl"), two, root);

        Assert.Equal("second", point!.Suite.Name);
    }

    /// <summary>
    /// A path under no suite is the silent failure this guard exists for. Left alone it resumes
    /// nothing, every suite reads as unrun, and the pass pays for hours of runs already on disk.
    /// </summary>
    [Fact]
    public void A_path_under_no_suite_throws_and_says_where_it_should_have_been()
    {
        var stray = Path.Combine(Paths.Root, "runs", "calibration.jsonl");

        var ex = Assert.Throws<InvalidOperationException>(() => ResumeOverride.Resolve(stray, Suites, Paths.Root));

        Assert.Contains(stray, ex.Message, StringComparison.Ordinal);
        Assert.Contains(Found.RunRecords, ex.Message, StringComparison.Ordinal);
    }
}
