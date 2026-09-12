using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #27. The free layer's hold on the paid ones.
///
/// Every layer used to load one suite, so a folder landing beside it widened nothing and nothing
/// went red to say so. Now each enumerates <see cref="SuitesUnderTest"/>, and these tests are what
/// keeps them enumerating it.
///
/// Half of this is text over source files, the shape <see cref="CiWorkflowTests"/> uses for the
/// workflow. It earns its place for the same reason: the paid layers sit in an assembly the free
/// gate never loads, so nothing the gate can RUN can see how they choose their suites.
///
/// What it cannot reach. A paid test that enumerates the source and then measures only the first row
/// reads as green here. The executable half below closes the other side of that, by pinning what the
/// source yields, and the rest is a code review.
/// </summary>
public class EveryLayerTests
{
    private static readonly HarnessPaths Paths = new();
    private static readonly string ModelTests = Paths.PaidTests;

    /// <summary>The attributes that mark a test as one that spends money. A paid test is one of these.</summary>
    private static readonly string[] PaidAttributes =
        ["[LiveFact]", "[LiveTheory]", "[CalibrationFact]", "[BreakageFact]", "[LadderFact]", "[DensityFact]"];

    private static IEnumerable<string> Sources =>
        Directory.GetFiles(ModelTests, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    public static IEnumerable<object[]> PaidFiles() =>
        [.. Sources.Where(f => PaidAttributes.Any(a => File.ReadAllText(f).Contains(a, StringComparison.Ordinal)))
            .Select(f => new object[] { Path.GetFileName(f) })];

    private static string Text(string file) => File.ReadAllText(Path.Combine(ModelTests, file));

    /// <summary>
    /// The floor under everything else here. A harness that discovers nothing runs nothing, and every
    /// layer would then be green over an empty catalogue.
    /// </summary>
    [Fact]
    public void Discovery_yields_at_least_one_suite()
    {
        Assert.NotEmpty(SuitesUnderTest.All(Paths));
    }

    [Fact]
    public void The_shared_source_yields_one_row_per_discovered_suite()
    {
        var rows = SuitesUnderTest.Rows().Select(r => (string)r[0]).Order(StringComparer.Ordinal);

        Assert.Equal(SuitesUnderTest.All(Paths).Select(s => s.Name).Order(StringComparer.Ordinal), rows);
    }

    /// <summary>
    /// Named one by one, not swept. A layer that vanished from the project would pass a sweep by not
    /// being there, which is the failure this ticket exists to stop.
    /// </summary>
    [Theory]
    [InlineData("Layer3_FiringTests.cs")]
    [InlineData("Layer4_ContractTests.cs")]
    public void Each_paid_layer_is_a_theory_over_every_discovered_suite(string file)
    {
        var text = Text(file);

        Assert.Contains("[LiveTheory]", text, StringComparison.Ordinal);
        Assert.Contains(
            $"MemberData(nameof({nameof(SuitesUnderTest)}.{nameof(SuitesUnderTest.Rows)}), MemberType = typeof({nameof(SuitesUnderTest)}))",
            text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A long pass is one test that loops, not a theory. Both hold one ledger across every suite they
    /// measure, and a theory would hand each suite a fresh ceiling and let one pass spend the budget
    /// #11 fixed once per suite.
    /// </summary>
    [Theory]
    [InlineData("CalibrationPassTests.cs")]
    [InlineData("BreakagePassTests.cs")]
    [InlineData("CommentDensityPassTests.cs")]
    public void Each_long_pass_plans_its_work_from_every_discovered_suite(string file)
    {
        Assert.Contains($"{nameof(SuitesUnderTest)}.{nameof(SuitesUnderTest.All)}(", Text(file), StringComparison.Ordinal);
    }

    /// <summary>Every paid test, including the screens. A new one that loads a single suite fails here.</summary>
    [Theory]
    [MemberData(nameof(PaidFiles))]
    public void Every_paid_test_enumerates_the_shared_source(string file)
    {
        Assert.Contains(nameof(SuitesUnderTest), Text(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// #26 took the suite filename out of fourteen test files. This keeps the suite NAME out of them,
    /// which is the same fault one level up: a paid test naming a skill measures that one and widens
    /// for nobody.
    /// </summary>
    [Fact]
    public void No_paid_test_names_a_suite_by_string_literal()
    {
        foreach (var suite in SuitesUnderTest.All(Paths))
            foreach (var file in Sources)
                Assert.DoesNotContain($"\"{suite.Name}\"", File.ReadAllText(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// The locks, still on. Rewiring the passes is exactly the kind of edit that takes one out, and a
    /// pass that runs without being asked costs hours and real money.
    /// </summary>
    [Theory]
    [InlineData("CalibrationPassTests.cs", "SKILL_HARNESS_CALIBRATE")]
    [InlineData("BreakagePassTests.cs", "SKILL_HARNESS_BREAK")]
    [InlineData("CommentDensityPassTests.cs", "SKILL_HARNESS_DENSITY")]
    public void Each_long_pass_keeps_its_own_lock_on_top_of_the_projects(string file, string lockName)
    {
        var text = Text(file);

        Assert.Contains("SKILL_HARNESS_LIVE", text, StringComparison.Ordinal);
        Assert.Contains(lockName, text, StringComparison.Ordinal);
    }
}
