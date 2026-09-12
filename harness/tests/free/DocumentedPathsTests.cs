using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #29. The rule, against documents built to be wrong.
///
/// The sweep in <see cref="Layer2_IntegrityTests"/> runs this same code over the repo's real
/// documents and is green, so it can never prove the rule's red half. Only a document nobody ships
/// can do that. Both halves matter: a rule that finds nothing gates nothing, and this one is green
/// the day it lands.
/// </summary>
public class DocumentedPathsTests : IDisposable
{
    private readonly string _repo =
        Path.Combine(Path.GetTempPath(), "harness-docs-test", Guid.NewGuid().ToString("N")[..8]);

    private readonly HarnessPaths _paths;

    public DocumentedPathsTests()
    {
        _paths = new HarnessPaths(Path.Combine(_repo, "harness"));
        Directory.CreateDirectory(_paths.Suites);
        Directory.CreateDirectory(_paths.Shared);
    }

    /// <summary>A document at a path relative to the repo root.</summary>
    private string Doc(string relative, string content)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_document_quoting_a_harness_folder_that_is_not_there_is_reported()
    {
        Doc("docs/breakage.md", "The overlays sit in `harness/fixtures/breaks/`.");

        var dead = Assert.Single(DocumentedPaths.Dead(_paths));

        Assert.Equal("docs/breakage.md", dead.Document);
        Assert.Equal("harness/fixtures/breaks/", dead.Path);
        Assert.Equal(1, dead.Line);
    }

    [Fact]
    public void A_document_quoting_a_harness_folder_that_is_there_is_not_reported()
    {
        Doc("docs/breakage.md", "The suites sit in `harness/skills/`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// A path in a fenced command is a path. The run commands both READMEs carry are fenced, and
    /// they are the lines a reader copies, so leaving a fence unchecked would miss the ones that
    /// cost someone a confused minute at a terminal.
    /// </summary>
    [Fact]
    public void A_path_inside_a_fenced_command_is_checked_too()
    {
        Doc("README.md", "```\ndotnet test harness/tests/Nope.Tests\n```\n");

        Assert.Equal("harness/tests/Nope.Tests", Assert.Single(DocumentedPaths.Dead(_paths)).Path);
    }

    /// <summary>
    /// A placeholder is not a path. `skills/&lt;name&gt;/` is the shape a suite folder takes, and
    /// asking the filesystem for it would redden the document that explains the shape correctly.
    /// The check truncates at the placeholder and holds the prefix to the disk instead.
    /// </summary>
    [Fact]
    public void A_placeholder_segment_is_checked_only_as_far_as_the_placeholder()
    {
        Doc("harness/README.md", "The passes write into `harness/skills/<name>/runs/`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// Trailing sentence punctuation is not part of the path. Without this the rule reddens every
    /// sentence that ends on a folder, which is most of them.
    /// </summary>
    [Fact]
    public void A_full_stop_after_a_path_is_not_part_of_it()
    {
        Doc("docs/notes.md", "Everything lives under harness/skills.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// A document inside `harness/` names paths relative to the harness root, because that is the
    /// root it describes. `shared/distractors/` in the harness README means `harness/shared/distractors/`
    /// and nothing else, so the rule resolves it there.
    /// </summary>
    [Fact]
    public void A_harness_document_resolves_its_own_relative_paths_against_the_harness_root()
    {
        Directory.CreateDirectory(Path.Combine(_paths.Shared, "distractors"));
        Doc("harness/README.md", "The distractors are `shared/distractors/` and the streams are `shared/streams/`.");

        var dead = Assert.Single(DocumentedPaths.Dead(_paths));

        Assert.Equal("shared/streams/", dead.Path);
    }

    /// <summary>
    /// Only a relative path whose first segment is a folder the harness owns. `plugin/` appears in
    /// the break overlay notes meaning "the suite's plugin folder", which is not a path from the
    /// harness root and must not be read as one.
    ///
    /// A rename of one of those five folders would make this check quietly stop looking rather than
    /// go red, which is why <see cref="SuiteFolderTests.The_harness_owns_five_folders_and_the_old_ones_are_not_among_them"/>
    /// pins the five names. The two rules cover each other.
    /// </summary>
    [Fact]
    public void A_relative_path_that_is_not_a_folder_the_harness_owns_is_left_alone()
    {
        Doc("harness/skills/alpha/breaks/README.md", "Laid over the suite's `plugin/` at run time.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// A fixture SKILL.md sits under `harness/` and is MATERIAL, not prose about the harness. Its
    /// `src/Foo.cs` addresses the bare repo a contract run writes into, so holding it to this disk
    /// would redden a fixture for being a fixture. Only a README describes the folder it sits in.
    /// </summary>
    [Fact]
    public void Fixture_material_under_the_harness_is_not_read_as_prose_about_it()
    {
        Doc(Path.Combine("harness", "skills", "alpha", "plugin", "skills", "alpha", "SKILL.md"),
            "Write the test in `tests/FooTests.cs` before the class in `src/Foo.cs`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// A relative path outside `harness/` is somebody else's. `docs/` in the root README is a repo
    /// path, and resolving it against the harness root would invent a folder nobody claimed.
    /// </summary>
    [Fact]
    public void A_document_outside_the_harness_has_its_relative_paths_left_alone()
    {
        Doc("README.md", "The plan is in `docs/` and the tests are in `tests/`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// Vendored dev tooling is skipped, along with any worktree parked under `.claude/`. A gate that
    /// reddens because an upstream phrasing mentions a folder we do not have is a gate people learn
    /// to ignore. This is the exclusion `docs/layer-2.md` already argues for the catalogue sweeps.
    /// </summary>
    [Fact]
    public void A_document_under_dot_claude_is_not_swept()
    {
        Doc(Path.Combine(".claude", "skills", "borrowed", "SKILL.md"), "See `harness/fixtures/good/`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>
    /// A suite's `runs/` folder is OUTPUT, not prose. A paid pass renders a report naming the
    /// folders it read, so a report written before a layout change describes the layout of its own
    /// day, correctly, forever. Sweeping those would redden the free gate for a record being
    /// accurate about history, and the only way back to green would be to falsify the record.
    ///
    /// This is not hypothetical. `runs/breakage-20260909-090853/breakage.md` names
    /// `harness/captured/`, which #23 deleted, and it is right to.
    /// </summary>
    [Fact]
    public void A_rendered_report_under_a_suites_runs_folder_is_not_swept()
    {
        Doc(Path.Combine("harness", "skills", "alpha", "runs", "breakage-1", "breakage.md"),
            "Read from `harness/captured/breakage-1/break-good.jsonl`.");

        Assert.Empty(DocumentedPaths.Dead(_paths));
    }

    /// <summary>Every dead path, not the first one, so one run names the whole job.</summary>
    [Fact]
    public void Every_dead_path_is_reported_rather_than_the_first()
    {
        Doc("docs/a.md", "`harness/captured/` and `harness/fixtures/`.");
        Doc("docs/b.md", "`harness/suites/`.");

        Assert.Equal(
            ["harness/captured/", "harness/fixtures/", "harness/suites/"],
            DocumentedPaths.Dead(_paths).Select(d => d.Path).Order());
    }

    public void Dispose()
    {
        if (Directory.Exists(_repo)) Directory.Delete(_repo, recursive: true);
    }
}
