using System.Diagnostics;
using System.Text;

namespace Harness;

public sealed record RunSpec
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> PluginDirs { get; init; } = [];
    /// <summary>baseline-test-first.md: use an allowlist, NOT bypassPermissions, which nudges the model into heredocs.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> DisallowedTools { get; init; } = [];
    public decimal MaxBudgetUsd { get; init; } = 0.60m;
    /// <summary>This machine has a user-level output style. Pin it or the run measures the machine, not the skill.</summary>
    public string Settings { get; init; } = """{"outputStyle":"default"}""";
    public StopMode StopMode { get; init; } = StopMode.Completion;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>
    /// Pinned by default, NOT null. #12: a null model takes whatever the CLI defaults to, so a
    /// default change would move every number on the map with no trace in the record.
    /// Set SKILL_HARNESS_MODEL to measure a different one on purpose.
    /// </summary>
    public string? Model { get; init; } = RunEnvironment.ResolveModel();
}

public static class ClaudeCli
{
    public static async Task<RunOutcome> RunAsync(RunSpec spec, CancellationToken ct = default)
    {
        var args = new List<string> { "-p", spec.Prompt, "--output-format", "stream-json", "--verbose" };
        foreach (var dir in spec.PluginDirs) { args.Add("--plugin-dir"); args.Add(dir); }
        if (spec.AllowedTools.Count > 0) { args.Add("--allowedTools"); args.AddRange(spec.AllowedTools); }
        if (spec.DisallowedTools.Count > 0) { args.Add("--disallowedTools"); args.AddRange(spec.DisallowedTools); }
        args.Add("--max-budget-usd"); args.Add(spec.MaxBudgetUsd.ToString(System.Globalization.CultureInfo.InvariantCulture));
        args.Add("--settings"); args.Add(spec.Settings);
        if (spec.Model is not null) { args.Add("--model"); args.Add(spec.Model); }

        var psi = new ProcessStartInfo("claude")
        {
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        var buffer = new StringBuilder();
        var started = false;
        var killed = false;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start claude");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(spec.Timeout);

        // Drained on its own task. stderr was redirected and never read, which fills the pipe and
        // deadlocks a chatty run, and #12 needs these words: a usage limit names itself here.
        var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            while (await proc.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                buffer.Append(line).Append('\n');
                if (line.Contains("\"subtype\":\"init\"")) started = true;

                if (spec.StopMode == StopMode.FirstDecision && IsFiringDecision(line))
                {
                    killed = true;
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    break;
                }
            }
            if (!killed) await proc.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        sw.Stop();
        var stream = buffer.ToString();
        var (transcript, subtype) = StreamParser.Parse(stream);
        var standardError = await SafeStderr(stderrTask);

        var outcome = new RunOutcome
        {
            ExitCode = killed ? 0 : SafeExitCode(proc),
            TerminalSubtype = subtype,
            Transcript = transcript,
            WorkingDirectory = spec.WorkingDirectory,
            Duration = sw.Elapsed,
            RawStream = stream,
            StopMode = spec.StopMode,
            KilledAtDecision = killed,
            Started = started,
            StandardError = standardError,
            RequestedModel = spec.Model,
        };

        // Decided AFTER the outcome exists, because both halves key on the validity gate and the
        // gate reads the exit code and the subtype together. #6 lost two arms to a guard that read
        // the subtype alone and returned false on a run that had exited 1.
        return outcome with
        {
            Throttled = Throttle.Detect(outcome.IsValid, transcript.ResultText, standardError)
                     || Throttle.Refused(outcome.IsValid, transcript.CostUsd, sw.Elapsed),
        };
    }

    /// <summary>
    /// The firing decision is visible once the model either picks a skill or starts writing files.
    /// MEASURED on this machine: a natural-language firing run opens with Bash or Glob to look around,
    /// so stopping at the first tool call of any kind reports a healthy run as a miss.
    /// </summary>
    private static bool IsFiringDecision(string line)
    {
        if (!line.Contains("\"type\":\"tool_use\"")) return false;
        return line.Contains("\"name\":\"Skill\"")
            || line.Contains("\"name\":\"Write\"")
            || line.Contains("\"name\":\"Edit\"");
    }

    /// <summary>A killed process can leave the reader faulted. No stderr is not a reason to lose the run.</summary>
    private static async Task<string> SafeStderr(Task<string> reader)
    {
        try { return await reader.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { return ""; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.HasExited ? p.ExitCode : -1; } catch { return -1; }
    }
}
