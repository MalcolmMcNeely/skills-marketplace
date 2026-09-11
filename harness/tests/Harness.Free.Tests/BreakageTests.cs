using System.Globalization;
using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #6, the free half. Everything here runs before a penny is spent, because a break that is
/// not the break you meant produces a confident, expensive, wrong number.
/// </summary>
public class BreakOverlayTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;
    private static string Skill => Found.Suite.SkillUnderTest;

    private static string Good => Found.SkillFile;
    private static string Stub => Path.Combine(Paths.StubCatalogue, "skills", Skill, "SKILL.md");
    private static string Overlay(string id) =>
        Path.Combine(Found.BreakOverlay(id), "skills", Skill, "SKILL.md");

    /// <summary>
    /// The description break must change the DESCRIPTION and nothing else. If it moved the body too,
    /// a layer 3 result would be measuring two changes and attributing them to one.
    /// </summary>
    [Fact]
    public void The_description_break_moves_only_the_description()
    {
        Assert.NotEqual(FixtureBuilder.DescriptionOf(Stub), FixtureBuilder.DescriptionOf(Overlay("description/catalogue")));
        Assert.Equal(FixtureBuilder.BodyOf(Stub), FixtureBuilder.BodyOf(Overlay("description/catalogue")));

        // The layer 4 half of the same break: the broken description, over a body that still works.
        Assert.Equal(
            FixtureBuilder.DescriptionOf(Overlay("description/catalogue")),
            FixtureBuilder.DescriptionOf(Overlay("description/plugin")));
        Assert.Equal(FixtureBuilder.BodyOf(Good), FixtureBuilder.BodyOf(Overlay("description/plugin")));
    }

    /// <summary>
    /// The body break must leave the description byte-identical. That is what makes layer 3's
    /// non-result on this arm a measurement rather than a coincidence.
    /// </summary>
    [Fact]
    public void The_body_break_moves_only_the_body()
    {
        Assert.Equal(FixtureBuilder.DescriptionOf(Good), FixtureBuilder.DescriptionOf(Overlay("body/plugin")));
        Assert.NotEqual(FixtureBuilder.BodyOf(Good), FixtureBuilder.BodyOf(Overlay("body/plugin")));
    }

    /// <summary>#4 asked for a SOFT break: the rule reversed and still argued for, not deleted.</summary>
    [Fact]
    public void The_body_break_is_soft_and_still_asks_for_both_files()
    {
        var body = FixtureBuilder.BodyOf(Overlay("body/plugin"));

        // Still asks for both files and a [Fact], so guards A3, A4 and A5 can still pass.
        Assert.Contains("src/Foo.cs", body, StringComparison.Ordinal);
        Assert.Contains("tests/FooTests.cs", body, StringComparison.Ordinal);
        Assert.Contains("[Fact]", body, StringComparison.Ordinal);

        // Reversed, not deleted: the class comes first and the test run is now required.
        Assert.Contains("Start with the class", body, StringComparison.Ordinal);
        Assert.Contains("Run `dotnet test`", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Write the test file first", body, StringComparison.Ordinal);
    }

    /// <summary>A control changes prose and nothing else. A control that moved a rule is a third break.</summary>
    [Theory]
    [InlineData("control/catalogue")]
    [InlineData("control/plugin")]
    public void A_control_keeps_the_description_byte_identical(string overlay)
    {
        var baseline = overlay == "control/catalogue" ? Stub : Good;
        Assert.Equal(FixtureBuilder.DescriptionOf(baseline), FixtureBuilder.DescriptionOf(Overlay(overlay)));
        Assert.NotEqual(FixtureBuilder.BodyOf(baseline), FixtureBuilder.BodyOf(Overlay(overlay)));
    }

    /// <summary>The layer 4 control must still carry both rules, or it is a body break wearing a label.</summary>
    [Fact]
    public void The_layer_4_control_keeps_both_rules()
    {
        var body = FixtureBuilder.BodyOf(Overlay("control/plugin"));

        Assert.Contains("must exist", body, StringComparison.Ordinal);
        Assert.Contains("before `src/Foo.cs`", body, StringComparison.Ordinal);
        Assert.Contains("Do not invoke `dotnet test`", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Start with the class", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Built, not merely found. An identifier can resolve to a folder that is real and still wrong,
    /// and #26's grouping added a second way for that to happen. Building each overlay over the base
    /// its own arm runs it on is the only check that catches both, and it costs milliseconds.
    /// </summary>
    [Fact]
    public void Every_arm_in_the_plan_applies_over_the_base_that_arm_runs_it_on()
    {
        var builder = new FixtureBuilder(Paths);

        foreach (var arm in BreakageArm.Plan)
        {
            if (arm.FiringOverlay is { } firing)
                builder.Build(Paths.StubCatalogue, Found.BreakOverlay(firing));
            if (arm.ContractOverlay is { } contract)
                builder.Build(Found.Plugin, Found.BreakOverlay(contract));
        }
    }

    /// <summary>
    /// #6 needs exactly one arm per layer to redden, or the separation question has no answer to give.
    /// Checked on the PLAN, before any run, because a plan that cannot separate is not worth paying for.
    /// </summary>
    [Fact]
    public void The_plan_expects_one_break_per_layer_and_two_quiet_controls()
    {
        var plan = BreakageArm.Plan;

        var description = plan.Single(a => a.Id == "description");
        Assert.Equal(LayerOutcome.Red, description.ExpectFiring);
        Assert.Equal(LayerOutcome.Green, description.ExpectContract);

        var body = plan.Single(a => a.Id == "body");
        Assert.Equal(LayerOutcome.Green, body.ExpectFiring);
        Assert.Equal(LayerOutcome.Red, body.ExpectContract);

        foreach (var control in plan.Where(a => a.Id is "control" or "good"))
        {
            Assert.NotEqual(LayerOutcome.Red, control.ExpectFiring);
            Assert.NotEqual(LayerOutcome.Red, control.ExpectContract);
        }
    }
}

public class FixtureBuilderTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;

    private static string MakeBase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "harness-overlay-test", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "skills", "x"));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "skills", "x", "SKILL.md"), "original");
        return dir;
    }

    private static string MakeOverlay(params (string Relative, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "harness-overlay-test", Guid.NewGuid().ToString("N")[..8]);
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return dir;
    }

    [Fact]
    public void A_null_overlay_returns_the_base_untouched()
    {
        var baseDir = MakeBase();
        Assert.Equal(baseDir, new FixtureBuilder(Paths).Build(baseDir, null));
    }

    [Fact]
    public void An_overlay_replaces_the_files_it_names_and_keeps_the_rest()
    {
        var baseDir = MakeBase();
        var overlay = MakeOverlay((Path.Combine("skills", "x", "SKILL.md"), "broken"));

        var built = new FixtureBuilder(Paths).Build(baseDir, overlay);

        Assert.Equal("broken", File.ReadAllText(Path.Combine(built, "skills", "x", "SKILL.md")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(built, "plugin.json")));
        // The frozen fixture is never touched, so a pass cannot leave a break behind on disk.
        Assert.Equal("original", File.ReadAllText(Path.Combine(baseDir, "skills", "x", "SKILL.md")));
    }

    /// <summary>
    /// The dangerous failure. A mistyped overlay path applies nothing, and the pass then measures the
    /// GOOD fixture while reporting it under a break's name. It has to throw, not shrug.
    /// </summary>
    [Fact]
    public void An_overlay_path_that_matches_nothing_in_the_base_throws()
    {
        var baseDir = MakeBase();
        var overlay = MakeOverlay((Path.Combine("skills", "typo", "SKILL.md"), "broken"));

        var ex = Assert.Throws<InvalidOperationException>(() => new FixtureBuilder(Paths).Build(baseDir, overlay));
        Assert.Contains("matches nothing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overlay_that_applies_no_files_throws()
    {
        var baseDir = MakeBase();
        var overlay = MakeOverlay(("README.md", "documentation only"));

        Assert.Throws<InvalidOperationException>(() => new FixtureBuilder(Paths).Build(baseDir, overlay));
    }

    /// <summary>Every real overlay must apply cleanly over its declared base. Checked before any run.</summary>
    [Theory]
    [InlineData("description/catalogue", false)]
    [InlineData("control/catalogue", false)]
    [InlineData("description/plugin", true)]
    [InlineData("body/plugin", true)]
    [InlineData("control/plugin", true)]
    public void Every_real_overlay_applies_over_its_base(string overlay, bool overSuitePlugin)
    {
        var builder = new FixtureBuilder(Paths);
        var baseDir = overSuitePlugin ? Found.Plugin : Paths.StubCatalogue;
        var skill = Path.Combine("skills", Found.Suite.SkillUnderTest, "SKILL.md");

        var built = builder.Build(baseDir, Found.BreakOverlay(overlay));

        Assert.True(File.Exists(Path.Combine(built, ".claude-plugin", "plugin.json")));
        Assert.Equal(
            File.ReadAllText(Path.Combine(Found.BreakOverlay(overlay), skill)),
            File.ReadAllText(Path.Combine(built, skill)));
    }

    /// <summary>The body break doubles as a layer 3 overlay, so it has to apply over the catalogue too.</summary>
    [Fact]
    public void The_body_break_applies_over_the_stub_catalogue_as_well()
    {
        var built = new FixtureBuilder(Paths).Build(Paths.StubCatalogue, Found.BreakOverlay("body/plugin"));

        Assert.Equal(12, Directory.GetDirectories(Path.Combine(built, "skills")).Length);
        Assert.Equal(
            FixtureBuilder.DescriptionOf(Found.SkillFile),
            FixtureBuilder.DescriptionOf(Path.Combine(built, "skills", Found.Suite.SkillUnderTest, "SKILL.md")));
    }
}

/// <summary>
/// The differential itself, on synthetic journals. The expensive pass produces one matrix and one
/// chance to read it, so the reading is tested against journals that cost nothing.
/// </summary>
public class BreakageReportTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;
    private static SuiteFile Suite => Found.Suite;

    private static string Journal(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "harness-breakage-test", $"{Guid.NewGuid():N}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Firing(string caseId, string verdict) =>
        $$"""{"at":"2026-09-09T00:00:00+00:00","case":"{{caseId}}","layer":3,"verdict":"{{verdict}}","detail":"","costUsd":0.20}""";

    private static string Contract(string verdict, params (int N, string Kind, bool Passed)[] assertions)
    {
        var parts = assertions.Select(a =>
            $$"""{"n":{{a.N}},"kind":"{{a.Kind}}","passed":{{(a.Passed ? "true" : "false")}}}""");
        return $$"""
            {"at":"2026-09-09T00:00:00+00:00","case":"C1","layer":4,"verdict":"{{verdict}}","detail":"","costUsd":0.30,"assertions":[{{string.Join(",", parts)}}]}
            """;
    }

    private static readonly (int, string, bool)[] AllPass =
    [
        (3, "Guard", true), (4, "Guard", true), (5, "Guard", true), (6, "Signal", true), (7, "Signal", true),
    ];

    private static readonly (int, string, bool)[] RuleReversed =
    [
        (3, "Guard", true), (4, "Guard", true), (5, "Guard", true), (6, "Signal", false), (7, "Signal", false),
    ];

    private static readonly (int, string, bool)[] GuardsDown =
    [
        (3, "Guard", true), (4, "Guard", false), (5, "Guard", false), (6, "Signal", false), (7, "Signal", true),
    ];

    private static ArmReport Arm(string id, string[] firing, string[] contract)
    {
        var spec = BreakageArm.Plan.Single(a => a.Id == id);
        return ArmReport.FromJournal(spec, Journal([.. firing, .. contract]), Suite);
    }

    private static string[] Positives(int passed, int total)
    {
        var ids = Suite.Firing.ShouldFire.Select(c => c.Id).ToList();
        return [.. Enumerable.Range(0, total)
            .Select(i => Firing(ids[i % ids.Count], i < passed ? "Held" : "Missed"))];
    }

    [Fact]
    public void The_gate_comes_from_the_calibrated_p_good_not_from_the_arms_own_rate()
    {
        // A broken arm scores 6 of 60. A gate derived from its own interval would fall to meet it and
        // call the break healthy; the gate must stay where #12's 0.940 put it.
        var arm = Arm("description", Positives(6, 60), []);

        Assert.Equal(Pooling.GateK(60, Suite.PGood), arm.FiringGate);
        Assert.True(arm.FiringGate > 6, "a gate at or below the broken rate would pass the break");
        Assert.Equal(LayerOutcome.Red, arm.Firing);
    }

    [Fact]
    public void A_description_break_reddens_layer_3_and_leaves_layer_4_alone()
    {
        var arm = Arm("description", Positives(4, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)]);

        Assert.Equal(LayerOutcome.Red, arm.Firing);
        Assert.Equal(LayerOutcome.Green, arm.Contract);
        Assert.Equal([3], arm.RedLayers);
        Assert.True(arm.AsExpected);
    }

    [Fact]
    public void A_body_break_reddens_layer_4_and_leaves_layer_3_alone()
    {
        var arm = Arm("body", Positives(6, 6), [.. Enumerable.Repeat(Contract("Broken", RuleReversed), 8)]);

        Assert.Equal(LayerOutcome.Green, arm.Firing);
        Assert.Equal(LayerOutcome.Red, arm.Contract);
        Assert.Equal([4], arm.RedLayers);
        Assert.True(arm.AsExpected);
    }

    /// <summary>One broken run is a break. A contract is not a rate.</summary>
    [Fact]
    public void A_single_broken_contract_run_reddens_layer_4()
    {
        var arm = Arm("control", Positives(60, 60),
            [.. Enumerable.Repeat(Contract("Held", AllPass), 4), Contract("Broken", RuleReversed)]);

        Assert.Equal(LayerOutcome.Red, arm.Contract);
        Assert.False(arm.AsExpected);
    }

    [Fact]
    public void The_two_breaks_are_reported_as_separable_when_they_redden_different_layers()
    {
        var report = new BreakageReport(new RunEnvironment("m", "v"),
        [
            Arm("description", Positives(4, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)]),
            Arm("body", Positives(6, 6), [.. Enumerable.Repeat(Contract("Broken", RuleReversed), 8)]),
        ]);

        Assert.True(report.Separates);
    }

    /// <summary>
    /// The design flaw #6 exists to find. If a body break also reddens layer 3, the harness cannot
    /// tell a targeting fault from a behaviour fault and must say so rather than report a pass.
    /// </summary>
    [Fact]
    public void Two_breaks_that_redden_the_same_layer_are_not_separable()
    {
        var report = new BreakageReport(new RunEnvironment("m", "v"),
        [
            Arm("description", Positives(4, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)]),
            Arm("body", Positives(0, 6), [.. Enumerable.Repeat(Contract("Broken", RuleReversed), 8)]),
        ]);

        Assert.False(report.Separates);
    }

    [Fact]
    public void A_control_that_moved_is_reported_as_noise()
    {
        var quiet = new BreakageReport(new RunEnvironment("m", "v"),
            [Arm("control", Positives(58, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)])]);
        Assert.True(quiet.ControlsQuiet);

        var noisy = new BreakageReport(new RunEnvironment("m", "v"),
            [Arm("control", Positives(20, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)])]);
        Assert.False(noisy.ControlsQuiet);
    }

    /// <summary>A break that takes the guards down proves nothing, and the report has to say so.</summary>
    [Fact]
    public void A_break_that_takes_the_guards_down_is_called_out()
    {
        var report = new BreakageReport(new RunEnvironment("m", "v"),
            [Arm("body", Positives(6, 6), [.. Enumerable.Repeat(Contract("Broken", GuardsDown), 8)])]);

        Assert.Single(report.GuardCasualties);
        Assert.Contains("A4", report.GuardCasualties[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_zero_floor_breach_reddens_layer_3_even_when_the_pool_clears_the_gate()
    {
        var ids = Suite.Firing.ShouldFire.Select(c => c.Id).ToList();
        // Nine cases perfect, one case dead. The pool is 54 of 60, over the gate of 53.
        var runs = ids.Take(9).SelectMany(id => Enumerable.Repeat(Firing(id, "Held"), 6))
            .Concat(Enumerable.Repeat(Firing(ids[9], "Missed"), 6))
            .ToArray();

        var arm = Arm("control", runs, []);

        Assert.True(arm.FiringPassed >= arm.FiringGate, "the pooled rate should clear the gate on its own");
        Assert.Single(arm.ZeroFloorBreaches);
        Assert.Equal(LayerOutcome.Red, arm.Firing);
    }

    [Fact]
    public void A_void_run_counts_towards_neither_score()
    {
        var arm = Arm("control",
            [.. Positives(60, 60), Firing("P1", "Void")],
            [.. Enumerable.Repeat(Contract("Held", AllPass), 5), Contract("Void")]);

        Assert.Equal(60, arm.FiringValid);
        Assert.Equal(5, arm.ContractValid);
        Assert.Equal(2, arm.VoidRuns);
    }

    [Fact]
    public void The_markdown_names_the_layer_each_break_reddened()
    {
        var report = new BreakageReport(new RunEnvironment("claude-opus-5[1m]", "2.1.248"),
        [
            Arm("description", Positives(4, 60), [.. Enumerable.Repeat(Contract("Held", AllPass), 5)]),
            Arm("body", Positives(6, 6), [.. Enumerable.Repeat(Contract("Broken", RuleReversed), 8)]),
        ]);

        var markdown = BreakageMarkdown.Render(report, TimeSpan.FromMinutes(150), [("description", "a.jsonl")]);

        Assert.Contains("A6 0/8", markdown, StringComparison.Ordinal);
        Assert.Contains("A3 8/8", markdown, StringComparison.Ordinal);
        Assert.Contains("02:30:00", markdown, StringComparison.Ordinal);
        Assert.Contains("The two failures are distinguishable | yes", markdown, StringComparison.Ordinal);
    }
}

/// <summary>The narrower plan shapes #6 buys, so a pass cannot quietly cost four times what it says.</summary>
public class FiringPlanShapeTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;
    private static SuiteFile Suite => Found.Suite;

    private static int Runs(FiringPlanShape shape) =>
        new CalibrationPass(Paths, Suite, null, shape).Plan().Sum(s => s.Runs);

    [Fact]
    public void Full_is_still_issue_10s_125_run_pass()
    {
        Assert.Equal(125, Runs(FiringPlanShape.Full));
        Assert.Equal(23, new CalibrationPass(Paths, Suite).Plan().Count());
    }

    [Fact]
    public void Positives_only_is_60_runs_and_every_case_expects_a_set()
    {
        var plan = new CalibrationPass(Paths, Suite, null, FiringPlanShape.PositivesOnly).Plan().ToList();

        Assert.Equal(10, plan.Count);
        Assert.Equal(60, plan.Sum(s => s.Runs));
        Assert.All(plan, s => Assert.Equal(CaseKind.ShouldFire, s.Kind));
    }

    [Fact]
    public void Short_positives_is_6_runs_and_is_a_probe_not_a_measurement()
    {
        var plan = new CalibrationPass(Paths, Suite, null, FiringPlanShape.ShortPositives).Plan().ToList();

        Assert.Equal(CalibrationPass.ShortCases, plan.Count);
        Assert.Equal(6, plan.Sum(s => s.Runs));
        // Below the zero-floor minimum on purpose: two runs cannot condemn a case.
        Assert.All(plan, s => Assert.True(s.Runs < Pooling.MinRunsForZeroFloor));
    }

    /// <summary>The whole of #6, priced before it is bought.</summary>
    [Fact]
    public void The_planned_pass_is_the_size_the_ticket_was_costed_at()
    {
        var firing = BreakageArm.Plan.Where(a => a.RunFiring).Sum(a => Runs(a.FiringShape));
        var contract = BreakageArm.Plan.Sum(a => a.ContractRuns);

        Assert.Equal(126, firing);
        Assert.Equal(23, contract);
        Assert.Equal(149, firing + contract);
    }
}

/// <summary>
/// Issue #15. A plan step says which of the three kinds a case is. Before this, the kind was read
/// back off <c>Expect</c>, which separates a should-fire case from a quiet one but leaves the two
/// quiet kinds indistinguishable — and that is the one difference the stop rule needs.
/// </summary>
public class CaseKindTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly DiscoveredSuite Found = UnderTest.CsharpNewClass;
    private static SuiteFile Suite => Found.Suite;
    private static CalibrationPass Pass => new(Paths, Suite);
    private static List<CalibrationPass.Step> FullPlan => [.. Pass.Plan()];

    [Fact]
    public void Every_step_in_the_full_plan_declares_one_of_the_three_kinds()
    {
        var counted = FullPlan.CountBy(s => s.Kind).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(10, counted[CaseKind.ShouldFire]);
        Assert.Equal(10, counted[CaseKind.ShouldNotFire]);
        Assert.Equal(3, counted[CaseKind.Watch]);
    }

    /// <summary>The whole point of the ticket: both are quiet, so Expect cannot tell them apart.</summary>
    [Fact]
    public void A_should_not_fire_step_is_told_apart_from_a_watch_step_without_inspecting_expect()
    {
        var quiet = FullPlan.Where(s => s.Kind is not CaseKind.ShouldFire).ToList();
        Assert.All(quiet, s => Assert.Empty(s.Expect));

        Assert.Equal(CaseKind.ShouldNotFire, FullPlan.Single(s => s.Id == "N1").Kind);
        Assert.Equal(CaseKind.Watch, FullPlan.Single(s => s.Id == "W1").Kind);
    }

    [Fact]
    public void A_narrower_shape_keeps_the_kind_it_narrowed()
    {
        var shortened = new CalibrationPass(Paths, Suite, null, FiringPlanShape.ShortPositives).Plan();
        Assert.All(shortened, s => Assert.Equal(CaseKind.ShouldFire, s.Kind));
    }

    [Fact]
    public void A_should_fire_step_without_an_expected_set_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new CalibrationPass.Step("P1", "add a class", 6, 12, CaseKind.ShouldFire, []));
    }

    [Fact]
    public void A_quiet_step_carrying_an_expected_set_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new CalibrationPass.Step("N1", "add tests", 5, 10, CaseKind.ShouldNotFire, ["csharp-new-class"]));
    }

    /// <summary>Grading is unchanged: a set match for should-fire, silence for both quiet kinds.</summary>
    [Fact]
    public void The_kind_picks_the_scorer_and_the_scorers_are_the_same_two_as_before()
    {
        var fired = Fake.Outcome(skills: [("csharp-new-class", 0)]);

        Assert.Equal(Verdict.Held, Pass.Score(FullPlan.Single(s => s.Id == "P1"), fired).Verdict);
        Assert.Equal(Verdict.Broken, Pass.Score(FullPlan.Single(s => s.Id == "N1"), fired).Verdict);
        Assert.Equal(Verdict.Broken, Pass.Score(FullPlan.Single(s => s.Id == "W1"), fired).Verdict);

        var silent = Fake.Outcome(skills: [("data-sql", 0)]);

        Assert.Equal(Verdict.WrongSet, Pass.Score(FullPlan.Single(s => s.Id == "P1"), silent).Verdict);
        Assert.Equal(Verdict.Held, Pass.Score(FullPlan.Single(s => s.Id == "N1"), silent).Verdict);
        Assert.Equal(Verdict.Held, Pass.Score(FullPlan.Single(s => s.Id == "W1"), silent).Verdict);
    }
}
