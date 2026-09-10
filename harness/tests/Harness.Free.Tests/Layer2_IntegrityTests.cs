using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Layer 2: referential integrity and budget, over the shipped catalogue. Free, deterministic,
/// no model calls and no network. What it asserts was decided on issue #5.
///
/// The rules are unit-tested below against the phrasings measured in the wild, and this class sweeps
/// the catalogue with the same code. #5 decided that split: the catalogue is small and the rules have
/// to be proven on real material, which the unit tests supply.
/// </summary>
public class Layer2_IntegrityTests
{
    private static readonly HarnessPaths Paths = new();
    private static IReadOnlyList<CatalogueSkill> Shipped => Catalogue.Load(Paths.ShippedCatalogue);

    [Fact]
    public void The_catalogue_is_not_empty()
    {
        // Without this the sweeps below pass having checked nothing, which is how they passed before #5.
        Assert.NotEmpty(Shipped);
    }

    [Fact]
    public void Every_skill_declares_a_name_and_a_description()
    {
        foreach (var skill in Shipped)
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Name), $"{skill.File}: no name");
            // Also catches a folded YAML description, which the single-line reader returns as empty.
            Assert.False(string.IsNullOrWhiteSpace(skill.Description), $"{skill.File}: no single-line description");
        }
    }

    [Fact]
    public void Every_folder_name_matches_the_skill_it_declares()
    {
        foreach (var skill in Shipped)
            Assert.Equal(Path.GetFileName(Path.GetDirectoryName(skill.File)), skill.Name);
    }

    [Fact]
    public void Every_referenced_skill_exists()
    {
        var known = Shipped.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var skill in Shipped)
            foreach (var reference in Composition.References(skill.Body))
                Assert.True(known.Contains(reference.Name),
                    $"{skill.Name} line {reference.Line} references \"{reference.Name}\", which is not in the catalogue: {reference.Text}");
    }

    [Fact]
    public void No_skill_uses_a_slash_form_to_reach_an_engine()
    {
        var engines = Shipped.Where(s => s.IsEngine).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var skill in Shipped)
            foreach (var slash in SlashForms.InProse(skill.Body).Where(s => engines.Contains(s.Name)))
                Assert.Fail($"{skill.Name} line {slash.Line} writes /{slash.Name} for an engine. "
                            + $"Write: Call the Skill tool with \"{slash.Name}\". Line: {slash.Text}");
    }

    [Fact]
    public void The_catalogue_stays_under_the_engine_cap()
    {
        var engines = Shipped.Where(s => s.IsEngine).Select(s => s.Name).Order().ToList();
        Assert.True(engines.Count <= Catalogue.MaxEngines,
            $"{engines.Count} engines, cap is {Catalogue.MaxEngines}: {string.Join(", ", engines)}");
    }

    [Fact]
    public void Every_description_is_under_the_character_limit()
    {
        foreach (var skill in Shipped)
            Assert.True(skill.Description.Length < Catalogue.MaxDescriptionChars,
                $"{skill.Name}: {skill.Description.Length} characters, limit is {Catalogue.MaxDescriptionChars}");
    }

    /// <summary>Issue #8 item 1: a fixture must never be mistaken for catalogue content.</summary>
    [Fact]
    public void No_fixture_skill_leaks_into_the_shipped_catalogue()
    {
        var fixtures = Catalogue.Load(Paths.Fixtures).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(fixtures);

        foreach (var skill in Shipped)
            Assert.DoesNotContain(skill.Name, fixtures);
    }

    [Fact]
    public void The_marketplace_manifest_does_not_reference_the_harness()
    {
        var manifest = File.ReadAllText(Paths.MarketplaceManifest);
        Assert.DoesNotContain("harness", manifest, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The extraction rule, against the phrasings measured in .claude/skills/. #5's first trap: the idiom
/// is not one string, and a test built on one regex found 2 of 11 references and passed the rest.
/// </summary>
public class CompositionExtractionTests
{
    [Theory]
    // Every one of these is a real line, copied from a real skill.
    [InlineData("Call the Skill tool with \"grilling\"", "grilling")]
    [InlineData("call the Skill tool with \"codebase-design\" for the vocabulary", "codebase-design")]
    [InlineData("Call the Skill tool with \"codebase-design\" and use its design-it-twice parallel sub-agent pattern", "codebase-design")]
    [InlineData("call the Skill tool with \"domain-modeling\" to keep the domain model current as you go:", "domain-modeling")]
    public void It_finds_a_single_name_whatever_the_phrasing(string line, string expected)
    {
        Assert.Equal([expected], Composition.References(line).Select(r => r.Name));
    }

    [Fact]
    public void It_finds_both_names_when_one_instruction_names_two()
    {
        var line = "Call the Skill tool twice, for \"grilling\" and \"domain-modeling\"";
        Assert.Equal(["grilling", "domain-modeling"], Composition.References(line).Select(r => r.Name));
    }

    [Fact]
    public void A_dynamic_reference_yields_nothing_rather_than_failing()
    {
        // The one line in 15 that the rule cannot resolve. There is no name in it to resolve, so it
        // produces no reference. #5 decided this is why no exemption mechanism is needed.
        var line = "call the Skill tool for whichever skills the `## Notes` block names";
        Assert.Empty(Composition.References(line));
    }

    [Fact]
    public void A_quoted_string_on_an_unrelated_line_is_not_a_reference()
    {
        Assert.Empty(Composition.References("The user said \"grilling\" was too aggressive."));
    }

    [Fact]
    public void A_fenced_example_is_not_a_reference()
    {
        // This one is not hypothetical. The first real catalogue skill the sweep met documents the
        // idiom with a placeholder, and "name" is not a skill. The gate went red on it.
        var body = "Compose in prose, like this:\n\n```\nCall the Skill tool with \"name\".\n```\n\nThat is all.";
        Assert.Empty(Composition.References(body));
    }

    [Fact]
    public void An_instruction_outside_a_fence_is_still_checked()
    {
        var body = "```\nCall the Skill tool with \"example\".\n```\n\nCall the Skill tool with \"tdd\".";
        Assert.Equal(["tdd"], Composition.References(body).Select(r => r.Name));
    }

    [Fact]
    public void It_reports_the_line_so_a_failure_can_be_found()
    {
        var body = "intro\nmore prose\nCall the Skill tool with \"tdd\".";
        var reference = Assert.Single(Composition.References(body));
        Assert.Equal(3, reference.Line);
    }
}

/// <summary>
/// The slash-form rule. #5's second trap: slash forms appear about a hundred times as legitimate
/// prose about commands a developer types, so a blanket grep is useless.
/// </summary>
public class SlashFormTests
{
    [Fact]
    public void It_finds_a_backticked_command()
    {
        Assert.Equal(["grill-with-docs"], SlashForms.InProse("Start with `/grill-with-docs` to sharpen it.").Select(s => s.Name));
    }

    [Fact]
    public void It_finds_a_bare_command()
    {
        Assert.Equal(["handoff"], SlashForms.InProse("Then /handoff out.").Select(s => s.Name));
    }

    [Fact]
    public void A_path_segment_is_not_a_command()
    {
        // Without this, any path containing an engine's name reads as a slash reference to it.
        Assert.Empty(SlashForms.InProse("Write it to plugins/core/skills/skill-authoring/SKILL.md"));
        Assert.Empty(SlashForms.InProse("See harness/fixtures/catalogue and docs/evals.md"));
    }

    [Fact]
    public void A_fenced_example_is_not_an_instruction()
    {
        // A skill teaching the rule has to be able to show the wrong form.
        var body = "Never write the slash form:\n\n```\n/skill-authoring\n```\n\nWrite the Skill tool instruction instead.";
        Assert.Empty(SlashForms.InProse(body));
    }

    [Fact]
    public void Built_ins_are_found_but_resolve_to_nothing()
    {
        // The rule reports every slash form. The catalogue test filters to engines, which is what
        // makes /clear and /compact harmless without a denylist.
        var found = SlashForms.InProse("Use `/clear` between tickets, or `/compact` at a boundary.");
        Assert.Equal(["clear", "compact"], found.Select(s => s.Name));
    }
}

/// <summary>
/// The sweeps, against a catalogue built to be broken. The unit tests above prove the rules detect;
/// these prove the wiring from a folder on disk to a failure actually connects. A green gate that has
/// never gone red proves nothing, and #1's whole point is proving the instrument.
/// </summary>
public class Layer2CatchesBreakageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cat-{Guid.NewGuid():N}");

    private void Write(string name, string frontmatterExtra, string body)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: {name}\ndescription: Use when testing.{frontmatterExtra}\n---\n\n{body}\n");
    }

    private IReadOnlyList<CatalogueSkill> Load() => Catalogue.Load(_root);

    [Fact]
    public void A_reference_to_a_skill_that_does_not_exist_is_found()
    {
        Write("alpha", "", "Call the Skill tool with \"beta\".");

        var known = Load().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var dangling = Load().SelectMany(s => Composition.References(s.Body)).Where(r => !known.Contains(r.Name)).ToList();

        Assert.Equal("beta", Assert.Single(dangling).Name);
    }

    [Fact]
    public void A_reference_that_does_exist_is_left_alone()
    {
        Write("alpha", "", "Call the Skill tool with \"beta\".");
        Write("beta", "", "Does a thing.");

        var known = Load().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(Load().SelectMany(s => Composition.References(s.Body)), r => Assert.Contains(r.Name, known));
    }

    [Fact]
    public void A_slash_form_pointing_at_an_engine_is_found()
    {
        Write("alpha", "", "Then run `/beta` to finish.");
        Write("beta", "", "An engine.");

        var engines = Load().Where(s => s.IsEngine).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var offences = Load().SelectMany(s => SlashForms.InProse(s.Body)).Where(s => engines.Contains(s.Name)).ToList();

        Assert.Equal("beta", Assert.Single(offences).Name);
    }

    [Fact]
    public void A_slash_form_pointing_at_an_entry_point_is_correct()
    {
        Write("alpha", "", "Then run `/beta` to finish.");
        Write("beta", "\ndisable-model-invocation: true", "An entry point a developer types.");

        var engines = Load().Where(s => s.IsEngine).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(Load().SelectMany(s => SlashForms.InProse(s.Body)), s => engines.Contains(s.Name));
    }

    [Fact]
    public void Entry_points_do_not_count_against_the_engine_cap()
    {
        for (var i = 0; i < 20; i++) Write($"entry{i}", "\ndisable-model-invocation: true", "Typed.");
        Assert.Equal(0, Load().Count(s => s.IsEngine));

        for (var i = 0; i <= Catalogue.MaxEngines; i++) Write($"engine{i}", "", "Model-invoked.");
        Assert.True(Load().Count(s => s.IsEngine) > Catalogue.MaxEngines);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

/// <summary>The frontmatter reader, which decides engine from entry point.</summary>
public class CatalogueParsingTests
{
    private const string Engine = """
        ---
        name: skill-authoring
        description: Use when writing a catalogue skill.
        ---

        Body here.
        """;

    private const string EntryPoint = """
        ---
        name: new-skill
        description: Scaffold a new skill.
        disable-model-invocation: true
        ---

        Body here.
        """;

    [Fact]
    public void A_skill_with_no_flag_is_an_engine()
    {
        var skill = Catalogue.Parse(Engine)!;
        Assert.True(skill.IsEngine);
        Assert.Equal("skill-authoring", skill.Name);
        Assert.Equal("Use when writing a catalogue skill.", skill.Description);
    }

    [Fact]
    public void Disable_model_invocation_makes_an_entry_point()
    {
        var skill = Catalogue.Parse(EntryPoint)!;
        Assert.True(skill.IsEntryPoint);
        Assert.False(skill.IsEngine);
    }

    [Fact]
    public void A_file_with_no_frontmatter_is_not_a_skill()
    {
        Assert.Null(Catalogue.Parse("# Just a heading\n\nSome prose."));
    }

    [Fact]
    public void The_body_excludes_the_frontmatter()
    {
        // Or the description's own words would read as instructions to the integrity rules.
        Assert.DoesNotContain("description:", Catalogue.Parse(Engine)!.Body);
    }
}
