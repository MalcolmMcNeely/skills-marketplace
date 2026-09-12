namespace Harness;

/// <summary>An engine nothing measures, and the suite folder the harness wants for it.</summary>
public sealed record UncoveredEngine(string Skill, string SuiteFolder);

/// <summary>
/// Issue #28. The join between what the repo SHIPS and what the harness MEASURES.
///
/// Every other layer 2 rule reads the catalogue alone and every discovery rule reads the suites
/// alone, so an engine could ship with nothing testing it and no rule was looking at both halves at
/// once to notice. This is that rule, and it costs a second because neither half calls a model.
///
/// WHY ENGINES ONLY. An engine is model-invocable: it fires on its own judgement and its description
/// sits in the listing on every request. Ship one untested and developers get behaviour nobody has
/// checked. An entry point carries <c>disable-model-invocation: true</c>, so a developer types it by
/// name and there is no firing decision to measure. Asking one for firing tests asks for a test it
/// cannot fail.
///
/// WHY THE FILE AND NOT THE NAME. A suite covers an engine when the file it resolves IS the shipped
/// file. Matching on names would let a fixture suite of the same name answer for the real skill, and
/// a fixture is a copy that drifts the moment the shipped text is edited. Comparing the resolved
/// paths says the tested text is the shipped text, which is the whole point of the catalogue source.
/// </summary>
public static class CatalogueCoverage
{
    public static IReadOnlyList<UncoveredEngine> EnginesWithNoSuite(HarnessPaths paths)
    {
        // Through the shared source, not the discovery class under it. A layer that named its own
        // suites is the fault #27 removed, and this is a layer 2 rule enumerating suites.
        var measured = SuitesUnderTest.All(paths).Select(s => s.SkillFile).ToList();

        return [.. Catalogue.Load(paths.ShippedCatalogue)
            .Where(s => s.IsEngine && !measured.Any(m => HarnessPaths.SameFile(m, s.File)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new UncoveredEngine(s.Name, Path.Combine(paths.Suites, s.Name)))];
    }
}
