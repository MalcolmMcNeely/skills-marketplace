using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// The free gate itself, read as a file. #20. Layers 1 and 2 gate nothing unless a workflow runs them,
/// and a workflow is the one thing here no test can exercise by running it. So these are text
/// assertions over YAML, and they earn their place. They are all that stands between the gate and a
/// plausible-looking edit.
///
/// One thing they cannot reach. A commit that deletes the gate's own `dotnet test` step stops this class
/// running on the runner at all, and then only a laptop or a required status check on `main` catches it.
/// No assertion inside the suite the gate runs can close that, so it is stated rather than papered over.
/// </summary>
public class CiWorkflowTests
{
    /// <summary>Both pinned by #20, for two different reasons the gate's own header records.</summary>
    private const string RunnerImage = "ubuntu-24.04";
    private const string CliPin = "2.1.248";

    private static readonly string WorkflowDir = new HarnessPaths().Workflows;
    private static readonly string FreeGate = Path.Combine(WorkflowDir, "free-gate.yml");

    private static string Text => File.ReadAllText(FreeGate);

    /// <summary>A comment cannot make a gate do anything, so no assertion below is allowed to read one.</summary>
    private static string StripComments(string yaml) =>
        string.Join('\n', yaml.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

    private static string Yaml => StripComments(Text);

    /// <summary>The commands the job actually runs, one per single-line <c>run:</c>.</summary>
    private static IReadOnlyList<string> Commands =>
        Yaml.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- run: ", StringComparison.Ordinal) || l.StartsWith("run: ", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("run: ", StringComparison.Ordinal) + 5)..].Trim())
            .ToList();

    /// <summary>What fires the job, and nothing else. Read on its own so a comment cannot trip the trigger tests.</summary>
    private static string Triggers
    {
        get
        {
            var lines = Yaml.Split('\n').Select(l => l.TrimEnd()).ToList();
            var start = lines.FindIndex(l => l == "on:");
            Assert.True(start >= 0, "the workflow declares no on: block");
            return string.Join('\n', lines.Skip(start + 1).TakeWhile(l => l.Length == 0 || l.StartsWith(' ')));
        }
    }

    [Theory]
    [InlineData("claude plugin validate .")]
    [InlineData("claude plugin validate ./plugins/core")]
    [InlineData("dotnet test harness/tests/Harness.Free.Tests")]
    public void The_gate_runs_each_of_the_three_free_commands(string command)
    {
        // Whole-command equality, not substring. "validate ." sits inside "validate ./plugins/core", so a
        // substring assertion would stay green after somebody dropped the marketplace manifest.
        Assert.Contains(command, Commands);
    }

    [Fact]
    public void The_gate_fires_on_a_push_to_main_and_on_a_pull_request()
    {
        Assert.Contains("push:", Triggers);
        Assert.Contains("branches: [main]", Triggers);
        Assert.Contains("pull_request:", Triggers);
    }

    [Fact]
    public void The_runner_image_and_the_cli_are_both_pinned()
    {
        Assert.Contains($"runs-on: {RunnerImage}", Yaml);
        Assert.Contains($"@anthropic-ai/claude-code@{CliPin}", Yaml);
    }

    [Fact]
    public void The_gate_fails_when_the_installed_cli_is_not_the_pin()
    {
        // A pin that is never compared against what installed is a comment, and the first draft of this
        // test was exactly that: it read the install line, so deleting the whole comparison left it green.
        // It reads the comparison now, which is the only part of the step that can fail the job.
        var lines = Yaml.Split('\n').Select(l => l.Trim()).ToList();
        var opens = lines.FindIndex(l => l.StartsWith("case ", StringComparison.Ordinal));
        Assert.True(opens >= 0, "the gate never compares claude --version against the pin");

        var comparison = string.Join('\n', lines.Skip(opens).TakeWhile(l => l != "esac"));
        Assert.Contains("claude --version", Yaml);
        Assert.Contains($"{CliPin}*)", comparison);
        Assert.Contains("exit 1", comparison);
    }

    [Fact]
    public void The_gate_fails_when_the_cli_is_missing_rather_than_skipping_layer_1()
    {
        // A silent skip that reads as a pass is worse than no layer 1, so the guard has to exit, not warn.
        var guard = Assert.Single(Yaml.Split('\n'), l => l.Contains("command -v claude", StringComparison.Ordinal));
        Assert.Contains("exit 1", guard);
    }

    [Theory]
    [InlineData("workflow_dispatch")]
    [InlineData("schedule")]
    [InlineData("repository_dispatch")]
    public void No_paid_layer_trigger_appears_in_the_gate(string trigger)
    {
        // #11 settled that layers 3 and 4 are button-only on a laptop, and #7 deleted the dispatch
        // workflow an earlier session built. This test catches the rebuild.
        Assert.DoesNotContain(trigger, Triggers);
    }

    [Theory]
    [InlineData("Harness.Model.Tests")]
    [InlineData("SKILL_HARNESS_LIVE")]
    [InlineData("SKILL_HARNESS_CALIBRATE")]
    [InlineData("SKILL_HARNESS_BREAK")]
    [InlineData("ANTHROPIC_API_KEY")]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("secrets.")]
    public void No_workflow_names_a_paid_layer_or_a_credential(string forbidden)
    {
        // Every workflow, not just this one. A second file is how the paid layers would arrive, and #20
        // rules them off CI whatever they are called. An unrelated workflow is free to land beside it.
        foreach (var file in Directory.GetFiles(WorkflowDir, "*.y*ml"))
            Assert.DoesNotContain(forbidden, StripComments(File.ReadAllText(file)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("continue-on-error")]
    [InlineData("if:")]
    public void No_step_in_the_gate_can_be_excused_or_conditioned(string escape)
    {
        // Both satisfy "the workflow runs all three commands" while the gate stops blocking on them.
        // The job needs neither, so the cheapest protection is to allow neither.
        Assert.DoesNotContain(escape, Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_the_paid_layers_stay_local_is_recorded_next_to_the_gate()
    {
        // Adding layers 3 and 4 later has to be a decision, not a drift. Whoever edits the gate is
        // reading the gate, so the reason lives in its header.
        var header = string.Join('\n',
            Text.Split('\n').TakeWhile(l => l.TrimStart().StartsWith('#') || l.Trim().Length == 0));

        Assert.Contains("docs/running-the-paid-layers.md", header);
        Assert.Contains("#11", header);
    }
}
