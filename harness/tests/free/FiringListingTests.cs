using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Issue #30. What a layer 3 run puts in front of the model.
///
/// Layer 3 decides from the LISTING, so the skill under test has to be in it. That was true for free
/// while the only suite was a fixture one: <c>shared/distractors/</c> carries a description-only stub
/// of csharp-new-class, so the distractors and the skill under test arrived in the same folder.
///
/// A catalogue suite is read from <c>plugins/</c> and never copied, so nothing puts it among the
/// distractors. A firing run that loaded the distractors alone would show the model twelve skills
/// that have nothing to do with the prompt, score every run a miss, and report it as a description
/// that will not fire. This is what keeps the listing complete, for nothing, before a run is paid for.
/// </summary>
public class FiringListingTests
{
    private static readonly HarnessPaths Paths = new();

    /// <summary>
    /// The rule, over every suite discovery returns. A suite landing in skills/ is held to it by being
    /// there, which is the same widening the paid layers take.
    /// </summary>
    [Theory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public void The_skill_under_test_is_in_the_listing_a_firing_run_loads(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var spec = new FiringRunner(Paths, found).SpecFor("build me a thing", CaseKind.ShouldFire);

        var listed = spec.PluginDirs.SelectMany(Catalogue.Load).Select(s => s.Name).ToList();

        Assert.Contains(found.Suite.SkillUnderTest, listed);
    }

    /// <summary>
    /// The distractors are the point of the measurement, so no suite may quietly drop them. A run
    /// against the skill under test alone asks whether one description beats nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public void Every_firing_run_still_loads_the_distractor_set(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var spec = new FiringRunner(Paths, found).SpecFor("build me a thing", CaseKind.ShouldFire);

        Assert.Equal(Paths.Distractors, spec.PluginDirs[0]);
    }

    /// <summary>
    /// One name, one description. The distractor set already declares the fixture skill, so
    /// loading the suite's own plugin as well would put two skills of that name in the listing and
    /// the run would be scored on whichever the CLI picked.
    /// </summary>
    [Fact]
    public void A_suite_the_distractors_already_declare_loads_nothing_extra()
    {
        var found = UnderTest.CsharpNewClass;

        Assert.Empty(found.ListingPlugins);
    }

    /// <summary>
    /// A catalogue suite's skill is only ever read from plugins/, so its plugin is what carries the
    /// description under test into the listing.
    ///
    /// One case over the catalogue suites rather than a theory row per suite that returns on the
    /// fixture ones. A row that skips silently reads as a row that passed, and this file exists
    /// because a layer 3 run reading as a pass over the wrong listing is the failure it prevents.
    /// </summary>
    [Fact]
    public void Every_catalogue_suite_carries_its_shipped_plugin_into_the_listing()
    {
        var catalogue = SuitesUnderTest.All(Paths)
            .Where(s => s.Suite.Source == SkillSource.Catalogue).ToList();

        // The shipped catalogue is never empty and every engine in it needs a catalogue suite, so a
        // sweep that found none would be this test measuring nothing.
        Assert.NotEmpty(catalogue);

        foreach (var found in catalogue)
        {
            Assert.Equal([found.Plugin], found.ListingPlugins);
            Assert.True(HarnessPaths.Inside(Paths.ShippedCatalogue, found.SkillFile),
                $"{found.Name} declares source catalogue, so its skill file must be the shipped one: {found.SkillFile}");
        }
    }

    /// <summary>
    /// One name, one description, said from the other side. The rule that keeps the suite's plugin
    /// out when the distractors already stub it is only worth anything if the listing it produces
    /// really does hold each name once: two skills of a name and the run is scored on whichever the
    /// CLI picked, with nothing in the stream to say which that was.
    /// </summary>
    [Theory]
    [MemberData(nameof(SuitesUnderTest.Rows), MemberType = typeof(SuitesUnderTest))]
    public void No_name_appears_twice_in_the_listing_a_firing_run_loads(string suiteName)
    {
        var found = SuiteDiscovery.One(Paths, suiteName);
        var spec = new FiringRunner(Paths, found).SpecFor("build me a thing", CaseKind.ShouldFire);

        var duplicated = spec.PluginDirs.SelectMany(Catalogue.Load)
            .CountBy(s => s.Name, StringComparer.Ordinal)
            .Where(p => p.Value > 1)
            .Select(p => p.Key)
            .ToList();

        Assert.True(duplicated.Count == 0,
            $"{suiteName}: the listing declares {string.Join(", ", duplicated)} more than once, "
            + $"across {string.Join(", ", spec.PluginDirs)}");
    }
}
