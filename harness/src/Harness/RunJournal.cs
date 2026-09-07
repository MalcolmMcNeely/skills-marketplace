using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harness;

/// <summary>One completed run, as it goes to disk.</summary>
public sealed record JournalEntry
{
    [JsonPropertyName("at")] public required DateTimeOffset At { get; init; }
    [JsonPropertyName("case")] public required string CaseId { get; init; }
    [JsonPropertyName("layer")] public required int Layer { get; init; }
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("throttled")] public bool Throttled { get; init; }
    [JsonPropertyName("costUsd")] public decimal? CostUsd { get; init; }
    [JsonPropertyName("seconds")] public double Seconds { get; init; }
    [JsonPropertyName("modelAsked")] public string? ModelAsked { get; init; }
    [JsonPropertyName("modelGot")] public string? ModelGot { get; init; }
    [JsonPropertyName("modelHeld")] public bool ModelHeld { get; init; }
    [JsonPropertyName("cliVersion")] public string? CliVersion { get; init; }
    [JsonPropertyName("firedSet")] public IReadOnlyList<string> FiredSet { get; init; } = [];
    /// <summary>#12 output 6: where each Skill call sat in the run.</summary>
    [JsonPropertyName("skillCalls")] public IReadOnlyList<JournalSkillCall> SkillCalls { get; init; } = [];
    [JsonPropertyName("exitCode")] public int ExitCode { get; init; }
    [JsonPropertyName("subtype")] public string? Subtype { get; init; }
}

public sealed record JournalSkillCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ordinal")] int Ordinal);

/// <summary>
/// Append-only ndjson, flushed after every run.
///
/// #12 comment point 1: the constraint on this pass is a 125-run, roughly 83-minute window, and a
/// usage limit can end it partway. #11's ceilings were set so a pass would not die at run 110 with no
/// p_good to show, but no ceiling prevents a limit. The journal does: every run that finished is on
/// disk before the next one starts, so a stopped pass has lost nothing but its remaining runs.
///
/// Written per run rather than per pass on purpose. A pass that buffers and writes at the end loses
/// everything to exactly the failure it is meant to survive.
/// </summary>
public sealed class RunJournal : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    public RunJournal(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        // FileShare.ReadWrite, not the default. On Windows a plain StreamWriter locks the file, and
        // the resume path reads the journal while the pass that owns it is still open. Without this
        // a resumed pass throws on its first case instead of skipping the work already done.
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };
    }

    public string Path { get; }
    public int Count { get; private set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public void Append(string caseId, int layer, RunOutcome outcome, RunScore score)
    {
        var entry = new JournalEntry
        {
            At = DateTimeOffset.UtcNow,
            CaseId = caseId,
            Layer = layer,
            Verdict = score.Verdict.ToString(),
            Detail = score.Detail,
            Throttled = score.Throttled,
            CostUsd = score.CostUsd,
            Seconds = Math.Round(outcome.Duration.TotalSeconds, 1),
            ModelAsked = outcome.RequestedModel,
            ModelGot = outcome.Transcript.Model,
            ModelHeld = outcome.ModelHeld,
            CliVersion = RunEnvironment.Current.CliVersion,
            FiredSet = [.. outcome.Transcript.FiredSkills],
            SkillCalls = [.. outcome.Transcript.SkillCalls.Select(c => new JournalSkillCall(c.Name, c.Ordinal))],
            ExitCode = outcome.ExitCode,
            Subtype = outcome.TerminalSubtype,
        };

        lock (_gate)
        {
            _writer.WriteLine(JsonSerializer.Serialize(entry, Options));
            Count++;
        }
    }

    /// <summary>Read a journal back. Lets a stopped pass report on what it did get.</summary>
    public static IReadOnlyList<JournalEntry> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var entries = new List<JournalEntry>();
        foreach (var line in ReadLinesShared(path))
        {
            if (line.Trim().Length == 0) continue;
            // A pass killed mid-write leaves a torn last line. Losing it beats losing the file.
            try
            {
                if (JsonSerializer.Deserialize<JournalEntry>(line) is { } e) entries.Add(e);
            }
            catch (JsonException) { }
        }
        return entries;
    }

    /// <summary>Reads a journal an open pass is still writing to.</summary>
    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    public void Dispose() => _writer.Dispose();
}
