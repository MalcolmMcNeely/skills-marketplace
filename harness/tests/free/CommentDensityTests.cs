using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// The metric, before it is trusted with $36 of runs.
///
/// A miscounted denominator moves all four arms together and the comparison still looks sane, so the
/// classifier is pinned against hand-counted samples rather than checked by eye on a real run.
/// </summary>
public class CommentDensityTests
{
    private static string Sample(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "density-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Subject.cs"), content.ReplaceLineEndings("\n"));
        return dir;
    }

    private static CommentDensity.Reading Read(string content) => CommentDensity.Measure(Sample(content), []);

    [Fact]
    public void A_whole_line_slash_comment_is_narration()
    {
        var r = Read("""
            // work out the total
            var total = 1;
            """);

        Assert.Equal(1, r.NarrationLines);
        Assert.Equal(1, r.CodeLines);
        Assert.Equal(100.0, r.Density);
    }

    [Fact]
    public void An_xml_doc_line_is_counted_apart_from_narration()
    {
        var r = Read("""
            /// <summary>The total.</summary>
            var total = 1;
            """);

        Assert.Equal(1, r.DocLines);
        Assert.Equal(0, r.NarrationLines);
        Assert.Equal(0.0, r.NarrationDensity);
        Assert.Equal(100.0, r.Density);
    }

    [Fact]
    public void A_block_comment_counts_every_line_it_spans()
    {
        var r = Read("""
            /*
             * three lines
             */
            var total = 1;
            """);

        Assert.Equal(3, r.BlockLines);
        Assert.Equal(1, r.CodeLines);
    }

    [Fact]
    public void A_single_line_block_comment_does_not_swallow_the_rest_of_the_file()
    {
        var r = Read("""
            /* short */
            var a = 1;
            var b = 2;
            """);

        Assert.Equal(1, r.BlockLines);
        Assert.Equal(2, r.CodeLines);
    }

    /// <summary>The case a naive `Contains("//")` gets wrong, and the reason the classifier counts quotes.</summary>
    [Fact]
    public void Slashes_inside_a_string_literal_are_code()
    {
        var r = Read("""
            var url = "https://example.com";
            """);

        Assert.Equal(1, r.CodeLines);
        Assert.Equal(0, r.NarrationLines);
        Assert.Equal(0, r.TrailingComments);
    }

    [Fact]
    public void A_comment_after_code_is_recorded_but_stays_out_of_the_ratio()
    {
        var r = Read("""
            var total = 1; // the total
            """);

        Assert.Equal(1, r.CodeLines);
        Assert.Equal(1, r.TrailingComments);
        Assert.Equal(0, r.NarrationLines);
        Assert.Equal(0.0, r.Density);
    }

    [Fact]
    public void Brace_only_lines_count_as_neither_code_nor_comment()
    {
        var r = Read("""
            public sealed class Subject
            {
                public int Total()
                {
                    return 1;
                }
            }
            """);

        Assert.Equal(4, r.StructuralLines);
        Assert.Equal(3, r.CodeLines);
    }

    [Fact]
    public void Blank_lines_are_ignored_entirely()
    {
        var r = Read("var a = 1;\n\n\nvar b = 2;\n");

        Assert.Equal(2, r.CodeLines);
        Assert.Equal(0, r.StructuralLines);
    }

    /// <summary>
    /// A run that wrote no code has no density. Zero would read as a spotless run and average in
    /// beside real ones, which is how a void arm flatters itself.
    /// </summary>
    [Fact]
    public void A_file_with_no_code_has_no_density_rather_than_zero()
    {
        var r = Read("// nothing but a comment\n");

        Assert.Equal(0, r.CodeLines);
        Assert.Null(r.Density);
        Assert.Null(r.NarrationDensity);
    }

    [Fact]
    public void Files_present_before_the_run_are_excluded()
    {
        var dir = Sample("// pre-existing\nvar a = 1;\n");
        var preexisting = CommentDensity.CsharpFilesIn(dir);

        var r = CommentDensity.Measure(dir, preexisting);

        Assert.Equal(0, r.Files);
        Assert.Equal(0, r.CodeLines);
    }

    [Fact]
    public void Bin_and_obj_are_the_compilers_not_the_models()
    {
        var dir = Sample("var a = 1;\n");
        var obj = Path.Combine(dir, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "Generated.cs"), "// generated\nvar b = 2;\n");

        var r = CommentDensity.Measure(dir, []);

        Assert.Equal(1, r.Files);
        Assert.Equal(0, r.NarrationLines);
    }

    [Fact]
    public void A_missing_directory_reads_as_nothing_rather_than_throwing()
    {
        var r = CommentDensity.Measure(Path.Combine(Path.GetTempPath(), "density-tests", "not-there"), []);

        Assert.Equal(0, r.Files);
        Assert.Null(r.Density);
    }
}

/// <summary>
/// The arms, held to the one property the experiment rests on: they differ by POSITION, not wording.
/// An arm whose rule text drifted from the others would measure two changes and report one.
/// </summary>
public class CommentArmTests
{
    [Fact]
    public void There_are_four_arms_with_distinct_ids()
    {
        var arms = CommentArm.All;

        Assert.Equal(4, arms.Count);
        Assert.Equal(4, arms.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_control_carries_the_rule_nowhere()
    {
        var control = CommentArm.All[0];

        Assert.Null(control.ClaudeMd);
        Assert.Null(control.StyleName);
        Assert.Contains("\"outputStyle\":\"default\"", control.SettingsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_arm_that_carries_the_rule_carries_the_same_words()
    {
        foreach (var arm in CommentArm.All.Skip(1))
            Assert.Contains(CommentArm.Rule, arm.ClaudeMd ?? arm.StyleBody!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The falsifiable half. Exactly one arm drops the default coding instructions, and it is the
    /// arm predicted to be worst. Two arms dropping them, or none, is not this experiment.
    /// </summary>
    [Fact]
    public void Exactly_one_arm_drops_the_default_coding_instructions()
    {
        var dropped = CommentArm.All
            .Where(a => a.StyleBody is not null)
            .Where(a => a.StyleBody!.Contains("keep-coding-instructions: false", StringComparison.Ordinal))
            .ToList();

        Assert.Single(dropped);
        Assert.Equal(CommentArm.All[^1].Id, dropped[0].Id);
    }

    [Fact]
    public void The_two_style_arms_differ_by_one_frontmatter_line_and_their_name()
    {
        var keep = CommentArm.All.Single(a => a.StyleBody?.Contains("keep-coding-instructions: true", StringComparison.Ordinal) == true);
        var drop = CommentArm.All.Single(a => a.StyleBody?.Contains("keep-coding-instructions: false", StringComparison.Ordinal) == true);

        var normalised = (string body, string name) => body.Replace(name, "NAME", StringComparison.Ordinal)
            .Replace("keep-coding-instructions: true", "FLAG", StringComparison.Ordinal)
            .Replace("keep-coding-instructions: false", "FLAG", StringComparison.Ordinal);

        Assert.Equal(normalised(keep.StyleBody!, keep.StyleName!), normalised(drop.StyleBody!, drop.StyleName!));
    }

    [Fact]
    public void Each_style_arm_names_its_own_style_in_its_settings()
    {
        foreach (var arm in CommentArm.All.Where(a => a.StyleName is not null))
            Assert.Contains($"\"outputStyle\":\"{arm.StyleName}\"", arm.SettingsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_lays_the_instruction_files_where_the_cli_reads_them()
    {
        foreach (var arm in CommentArm.All)
        {
            var repo = Path.Combine(Path.GetTempPath(), "density-arms", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(repo);

            arm.Prepare(repo);

            Assert.Equal(arm.ClaudeMd is not null, File.Exists(Path.Combine(repo, "CLAUDE.md")));
            Assert.Equal(
                arm.StyleName is not null,
                arm.StyleName is not null && File.Exists(Path.Combine(repo, ".claude", "output-styles", $"{arm.StyleName}.md")));
        }
    }
}
