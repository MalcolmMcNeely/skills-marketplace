using System.Text.RegularExpressions;
using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>
/// Layer 2: referential integrity and budget. PLACEHOLDER SHAPE ONLY.
/// What layer 2 actually asserts is issue #5, still open. These three show the shape
/// and prove the free half needs no model call and no network.
/// </summary>
public class Layer2_IntegrityTests
{
    private static readonly HarnessPaths Paths = new();
    private static string ShippedSkills => Path.Combine(Paths.Root, "..", "plugins");

    [Fact]
    public void Every_shipped_description_is_under_the_1024_character_budget()
    {
        foreach (var (file, description) in Descriptions(ShippedSkills))
            Assert.True(description.Length < 1024, $"{file}: {description.Length} characters");
    }

    /// <summary>Issue #8 item 1: a fixture must never be mistaken for catalogue content.</summary>
    [Fact]
    public void No_fixture_skill_leaks_into_the_shipped_catalogue()
    {
        var fixtureNames = Directory.Exists(Paths.Fixtures)
            ? Directory.GetFiles(Paths.Fixtures, "SKILL.md", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(Path.GetDirectoryName(f))!).ToHashSet()
            : [];
        Assert.NotEmpty(fixtureNames);

        foreach (var (file, _) in Descriptions(ShippedSkills))
        {
            var name = Path.GetFileName(Path.GetDirectoryName(file))!;
            Assert.DoesNotContain(name, fixtureNames);
        }
    }

    [Fact]
    public void The_marketplace_manifest_does_not_reference_the_harness()
    {
        var manifest = File.ReadAllText(Path.Combine(Paths.Root, "..", ".claude-plugin", "marketplace.json"));
        Assert.DoesNotContain("harness", manifest, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string File, string Description)> Descriptions(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.GetFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            var m = Regex.Match(File.ReadAllText(file), @"^description:\s*(?<d>.+)$", RegexOptions.Multiline);
            if (m.Success) yield return (file, m.Groups["d"].Value.Trim());
        }
    }
}
