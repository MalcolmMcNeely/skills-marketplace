using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harness;

/// <summary>
/// Issue #8 item 5. A case is DATA (prompt, expected set, run count) in JSON, because it is edited
/// far more often than the harness and reviewers read it without a compiler.
/// A contract ASSERTION is CODE, named from the case, because "test written before class" cannot be
/// expressed in JSON without inventing a DSL. Hybrid on purpose.
/// </summary>
public sealed record SuiteFile
{
    [JsonPropertyName("suite")] public required string Suite { get; init; }
    [JsonPropertyName("skillUnderTest")] public required string SkillUnderTest { get; init; }
    [JsonPropertyName("pGood")] public double PGood { get; init; } = 0.67;
    [JsonPropertyName("firing")] public FiringSuite Firing { get; init; } = new();
    [JsonPropertyName("contract")] public IReadOnlyList<ContractCase> Contract { get; init; } = [];

    public static SuiteFile Load(string path) =>
        JsonSerializer.Deserialize<SuiteFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidOperationException($"empty suite file: {path}");

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
/// Issue #15. Which of #10's three kinds a firing case is, said out loud rather than inferred.
/// Two of the three are graded on silence, so an expected set cannot tell them apart, and the
/// stop rule needs exactly that difference.
///
/// It sits with the case data, not with the pass that plans the cases or the runner that spends
/// them, because both of those read it and neither owns it.
/// </summary>
public enum CaseKind
{
    /// <summary>Graded on an exact set match. Carries the set it expects.</summary>
    ShouldFire,
    /// <summary>Graded on the silence of the skill under test, and gated on it.</summary>
    ShouldNotFire,
    /// <summary>Graded on silence, run and recorded, never gated. #10's murky three.</summary>
    Watch,
}

public sealed record FiringSuite
{
    [JsonPropertyName("shouldFire")] public IReadOnlyList<PositiveCase> ShouldFire { get; init; } = [];
    [JsonPropertyName("shouldNotFire")] public IReadOnlyList<NegativeCase> ShouldNotFire { get; init; } = [];
    /// <summary>Run and recorded, never gated. #10's murky three.</summary>
    [JsonPropertyName("watch")] public IReadOnlyList<NegativeCase> Watch { get; init; } = [];
}

public sealed record PositiveCase
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    /// <summary>Graded on an EXACT set match.</summary>
    public required IReadOnlyList<string> Expect { get; init; }
    public int Runs { get; init; } = 6;
    public int Cap { get; init; } = 12;
}

public sealed record NegativeCase
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    public string? Boundary { get; init; }
    public int Runs { get; init; } = 5;
    public int Cap { get; init; } = 10;
}

public sealed record ContractCase
{
    public required string Id { get; init; }
    public required string Task { get; init; }
    /// <summary>Names a C# assertion set. Resolved by <see cref="AssertionCatalogue"/>.</summary>
    public required string Assertions { get; init; }
    public Dictionary<string, string> AssertionArgs { get; init; } = [];
    public int Runs { get; init; } = 5;
    public int Cap { get; init; } = 10;
}

public static class AssertionCatalogue
{
    public static IContractAssertions Resolve(ContractCase c) => c.Assertions switch
    {
        "TestFirstFilesOnly" => new TestFirstFilesOnly(c.AssertionArgs["className"]),
        _ => throw new NotSupportedException($"unknown assertion set '{c.Assertions}'"),
    };

    public static IReadOnlyList<string> Known => ["TestFirstFilesOnly"];
}
