using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harness;

/// <summary>One run of one arm, as it goes to disk.</summary>
public sealed record DensityEntry
{
    [JsonPropertyName("at")] public required DateTimeOffset At { get; init; }
    [JsonPropertyName("suite")] public required string Suite { get; init; }
    [JsonPropertyName("arm")] public required string Arm { get; init; }
    [JsonPropertyName("attempt")] public required int Attempt { get; init; }
    [JsonPropertyName("valid")] public required bool Valid { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("throttled")] public bool Throttled { get; init; }
    [JsonPropertyName("files")] public int Files { get; init; }
    [JsonPropertyName("codeLines")] public int CodeLines { get; init; }
    [JsonPropertyName("narrationLines")] public int NarrationLines { get; init; }
    [JsonPropertyName("docLines")] public int DocLines { get; init; }
    [JsonPropertyName("blockLines")] public int BlockLines { get; init; }
    [JsonPropertyName("trailingComments")] public int TrailingComments { get; init; }
    [JsonPropertyName("density")] public double? Density { get; init; }
    [JsonPropertyName("narrationDensity")] public double? NarrationDensity { get; init; }
    [JsonPropertyName("costUsd")] public decimal? CostUsd { get; init; }
    [JsonPropertyName("seconds")] public double Seconds { get; init; }
    [JsonPropertyName("modelAsked")] public string? ModelAsked { get; init; }
    [JsonPropertyName("modelGot")] public string? ModelGot { get; init; }
    [JsonPropertyName("modelHeld")] public bool ModelHeld { get; init; }
    [JsonPropertyName("cliVersion")] public string? CliVersion { get; init; }
}

/// <summary>
/// Append-only ndjson, flushed per run, for the reason <see cref="RunJournal"/> is: a usage limit can
/// end a pass partway, and a pass that buffers loses everything to exactly the failure it must survive.
/// A separate record from the firing journal because the measurement is a RATIO, not a verdict, and
/// forcing a continuous figure into a verdict field would lose it.
/// </summary>
public sealed class DensityJournal : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    public DensityJournal(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true };
    }

    public string Path { get; }

    public void Append(DensityEntry entry)
    {
        lock (_gate) _writer.WriteLine(JsonSerializer.Serialize(entry, Options));
    }

    public static IReadOnlyList<DensityEntry> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var entries = new List<DensityEntry>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            try { if (JsonSerializer.Deserialize<DensityEntry>(line) is { } e) entries.Add(e); }
            catch (JsonException) { }
        }
        return entries;
    }

    public void Dispose() => _writer.Dispose();
}

public sealed record DensityOutcome(bool Stopped, string? Reason, int Runs);

/// <summary>
/// The paid pass. One fixed task, four arms, N runs an arm, and nothing varying between arms except
/// which file the instruction sits in.
///
/// The task comes from the suite's own contract case rather than a literal typed in here, so a second
/// skill folder widens the experiment by being there, the way every other layer widens. The skill
/// itself is NOT loaded: no --plugin-dir, because a skill body is a third position for an instruction
/// and would confound the three under test.
/// </summary>
public sealed class CommentDensityPass(HarnessPaths paths, DiscoveredSuite suite, int runsPerArm)
{
    public async Task<DensityOutcome> RunAsync(
        DensityJournal journal, SpendLedger ledger, Action<string> log, CancellationToken ct = default)
    {
        // A suite with no contract case declares no task to write code from. Reported, never guessed at.
        if (suite.Suite.Contract.Count == 0)
            return new DensityOutcome(Stopped: false, Reason: "suite declares no contract case, so it names no coding task", Runs: 0);

        var task = suite.Suite.Contract[0].Task;
        var arms = CommentArm.All;
        var runs = 0;

        log($"{suite.Name}: {arms.Count} arms x {runsPerArm} runs, task: {task}");

        foreach (var arm in arms)
        {
            for (var attempt = 1; attempt <= runsPerArm; attempt++)
            {
                if (ledger.Exhausted)
                    return new DensityOutcome(true, $"suite budget exhausted: {ledger.Report()}", runs);
                ct.ThrowIfCancellationRequested();

                var repo = paths.NewScratchRepo();
                var preexisting = arm.Prepare(repo);

                var outcome = await ClaudeCli.RunAsync(new RunSpec
                {
                    Prompt = task,
                    WorkingDirectory = repo,
                    // baseline-test-first.md: an allowlist, not bypassPermissions, which nudges the
                    // model into heredocs and would write files the Write tool never touched.
                    AllowedTools = ["Write", "Edit", "Read", "Bash", "Glob", "Grep"],
                    Settings = arm.SettingsJson,
                    StopMode = StopMode.Completion,
                }, ct);

                runs++;
                var valid = outcome.TryGetValid(out _);
                // Measured even on an invalid run. A run that died after writing its files still has a
                // reading, and throwing it away would bias the arms towards whichever one crashed less.
                var reading = CommentDensity.Measure(repo, preexisting);

                journal.Append(new DensityEntry
                {
                    At = DateTimeOffset.UtcNow,
                    Suite = suite.Name,
                    Arm = arm.Id,
                    Attempt = attempt,
                    Valid = valid,
                    Detail = valid ? reading.Summary : outcome.VoidReason,
                    Throttled = outcome.Throttled,
                    Files = reading.Files,
                    CodeLines = reading.CodeLines,
                    NarrationLines = reading.NarrationLines,
                    DocLines = reading.DocLines,
                    BlockLines = reading.BlockLines,
                    TrailingComments = reading.TrailingComments,
                    Density = reading.Density,
                    NarrationDensity = reading.NarrationDensity,
                    CostUsd = outcome.Transcript.CostUsd,
                    Seconds = Math.Round(outcome.Duration.TotalSeconds, 1),
                    ModelAsked = outcome.RequestedModel,
                    ModelGot = outcome.Transcript.Model,
                    ModelHeld = outcome.ModelHeld,
                    CliVersion = RunEnvironment.Current.CliVersion,
                });

                ledger.Record(new RunScore(
                    valid ? Verdict.Held : Verdict.Void,
                    valid ? reading.Summary : outcome.VoidReason,
                    [], [], RunCost.Of(outcome.Transcript.CostUsd), outcome.Throttled));

                log($"  {arm.Id,-14} {attempt,2}/{runsPerArm}  {(valid ? reading.Summary : "VOID: " + outcome.VoidReason)}");

                // #12's rule, and it holds here for the same reason: a usage limit does not lift
                // between attempts. Continuing spends the ceiling to reach the same wall.
                if (outcome.Throttled)
                    return new DensityOutcome(true, $"throttled on {arm.Id} attempt {attempt}", runs);
            }
        }

        return new DensityOutcome(false, null, runs);
    }
}

/// <summary>Per-arm figures, read back from a journal so a stopped pass still reports what it got.</summary>
public sealed record ArmStats(
    string Arm, int Runs, int Valid, IReadOnlyList<double> Densities, IReadOnlyList<double> Narration, decimal CostUsd)
{
    public double? Mean => Densities.Count == 0 ? null : Math.Round(Densities.Average(), 1);
    public double? Median => Percentile(Densities, 0.5);
    public double? Min => Densities.Count == 0 ? null : Densities.Min();
    public double? Max => Densities.Count == 0 ? null : Densities.Max();
    public double? NarrationMean => Narration.Count == 0 ? null : Math.Round(Narration.Average(), 1);

    private static double? Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToList();
        return Math.Round(sorted[(int)Math.Floor(p * (sorted.Count - 1))], 1);
    }

    public static IReadOnlyList<ArmStats> FromJournal(string path)
    {
        var entries = DensityJournal.Read(path);
        return
        [
            .. entries.GroupBy(e => e.Arm).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new ArmStats(
                g.Key,
                g.Count(),
                g.Count(e => e.Valid),
                [.. g.Where(e => e.Valid && e.Density is not null).Select(e => e.Density!.Value)],
                [.. g.Where(e => e.Valid && e.NarrationDensity is not null).Select(e => e.NarrationDensity!.Value)],
                g.Sum(e => e.CostUsd ?? 0m))),
        ];
    }
}

public static class DensityMarkdown
{
    public static string Render(
        DiscoveredSuite suite, IReadOnlyList<ArmStats> stats, DensityOutcome outcome, SpendLedger ledger, string journalPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Comment density by instruction position: {suite.Name}");
        sb.AppendLine();
        sb.AppendLine($"{RunEnvironment.Current}. Journal: `{Path.GetFileName(journalPath)}`. Runs: {outcome.Runs}. Spend: {ledger.Report()}.");
        if (outcome.Stopped) sb.AppendLine($"\n**Pass stopped early: {outcome.Reason}.** The figures below cover the runs that completed.");
        sb.AppendLine();
        sb.AppendLine("Task, taken from the suite's contract case:");
        sb.AppendLine();
        sb.AppendLine($"> {(suite.Suite.Contract.Count > 0 ? suite.Suite.Contract[0].Task : "none declared")}");
        sb.AppendLine();
        sb.AppendLine("Density is comment lines per 100 code lines. Narration excludes XML doc comments.");
        sb.AppendLine();
        sb.AppendLine("| Arm | Runs | Valid | Mean | Median | Min | Max | Narration mean | Cost |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var s in stats)
            sb.AppendLine($"| {s.Arm} | {s.Runs} | {s.Valid} | {F(s.Mean)} | {F(s.Median)} | {F(s.Min)} | {F(s.Max)} | {F(s.NarrationMean)} | ${s.CostUsd:0.00} |");
        sb.AppendLine();
        sb.AppendLine("## Every reading");
        sb.AppendLine();
        foreach (var s in stats)
            sb.AppendLine($"- **{s.Arm}**: {(s.Densities.Count == 0 ? "no valid reading" : string.Join(", ", s.Densities.Select(d => d.ToString("0.0"))))}");
        sb.AppendLine();
        sb.AppendLine("## What each arm is");
        sb.AppendLine();
        foreach (var arm in CommentArm.All) sb.AppendLine($"- **{arm.Id}**: {arm.Label}");
        return sb.ToString();
    }

    private static string F(double? value) => value is null ? "-" : value.Value.ToString("0.0");
}
