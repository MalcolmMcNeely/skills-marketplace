namespace Harness;

/// <summary>
/// Issue #8 item 7: layer 3 and layer 4 are two different run shapes and must not be confused.
/// They are separate types so a case cannot be handed to the wrong one.
/// </summary>
public sealed class FiringRunner(HarnessPaths paths)
{
    /// <summary>Natural-language prompt against the description-only stub catalogue, killed at the first tool call.</summary>
    public Task<RunOutcome> RunAsync(string prompt, CancellationToken ct = default) =>
        ClaudeCli.RunAsync(new RunSpec
        {
            Prompt = prompt,
            WorkingDirectory = paths.FixtureRepo,
            PluginDirs = [paths.StubCatalogue],
            // Firing is decided before any work happens, so forbid the expensive tools.
            // NOT restricted here: --allowedTools only auto-approves, and disallowing Write made the
            // model read the repo until it blew the budget. The stop rule does the saving instead.
            MaxBudgetUsd = 0.40m,   // MEASURED: 0.20 aborted mid-run and voided the sample.
            StopMode = StopMode.FirstDecision,
            Timeout = TimeSpan.FromMinutes(3),
        }, ct);
}

public sealed class ContractRunner(HarnessPaths paths)
{
    /// <summary>By-name invocation against the real body, in a throwaway copy of the fixture repo.</summary>
    public async Task<RunOutcome> RunAsync(string skill, string task, string pluginDir, CancellationToken ct = default)
    {
        var workDir = paths.NewScratchRepo();
        return await ClaudeCli.RunAsync(new RunSpec
        {
            Prompt = $"/{skill} {task}",
            WorkingDirectory = workDir,
            PluginDirs = [pluginDir],
            // baseline-test-first.md: allowlist, not bypassPermissions.
            AllowedTools = ["Write", "Edit", "Read", "Bash", "Glob", "Grep"],
            MaxBudgetUsd = 0.60m,
            StopMode = StopMode.Completion,
            Timeout = TimeSpan.FromMinutes(5),
        }, ct);
    }
}

/// <summary>Resample a case until it has enough VALID runs, or give up and say so.</summary>
public sealed record Sample(IReadOnlyList<RunScore> Scores, bool CapHit)
{
    public int Valid => Scores.Count(s => s.Verdict != Verdict.Void);
    /// <summary>Hitting the cap is a LAYER 3 failure and must be reported as one, not as a contract break.</summary>
    public string? Failure => CapHit ? "insufficient-firings" : null;
}

public static class Resampler
{
    public static async Task<Sample> CollectAsync(
        int wanted, int cap, Func<CancellationToken, Task<RunScore>> once, CancellationToken ct = default)
    {
        var scores = new List<RunScore>();
        var valid = 0;
        var attempts = 0;
        while (valid < wanted && attempts < cap)
        {
            attempts++;
            var score = await once(ct);
            scores.Add(score);
            if (score.Verdict != Verdict.Void) valid++;
        }
        return new Sample(scores, valid < wanted);
    }
}
