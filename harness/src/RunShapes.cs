namespace Harness;

/// <summary>
/// Issue #8 item 7: layer 3 and layer 4 are two different run shapes and must not be confused.
/// They are separate types so a case cannot be handed to the wrong one.
/// </summary>
public sealed class FiringRunner(HarnessPaths paths, DiscoveredSuite suite, string? catalogueDir = null)
{
    // #6 lays a break overlay over the distractor set in scratch. Null is the unbroken distractor set.
    // Held as one instance so two specs from this runner differ only where they are meant to.
    //
    // #30: the suite's own listing plugins come after the distractors, because a skill the
    // distractors do not stub is otherwise absent from the listing the run is decided on.
    private readonly string[] _pluginDirs = [catalogueDir ?? paths.Distractors, .. suite.ListingPlugins];

    /// <summary>Natural-language prompt against the description-only distractor set.</summary>
    public Task<RunOutcome> RunAsync(string prompt, CaseKind kind, CancellationToken ct = default) =>
        ClaudeCli.RunAsync(SpecFor(prompt, kind), ct);

    /// <summary>
    /// Issue #17. The run shape for one case, and the only thing the kind decides is where the run
    /// stops. Separated from <see cref="RunAsync"/> so the rule can be read without paying for a run.
    /// </summary>
    public RunSpec SpecFor(string prompt, CaseKind kind) => new()
    {
        Prompt = prompt,
        WorkingDirectory = paths.BareRepo,
        PluginDirs = _pluginDirs,
        // Firing is decided before any work happens, so forbid the expensive tools.
        // NOT restricted here: --allowedTools only auto-approves, and disallowing Write made the
        // model read the repo until it blew the budget. The stop rule does the saving instead.
        // #18: layer 3 sits at the per-run ceiling #11 fixed and keeps no tighter cap of its own.
        // 0.20 aborted mid-run, and three passes then measured 0.40 clipping runs that were working:
        // the figures are in breakage.md section 7. A cap only decides which runs are VOID, so
        // raising it moves no gate.
        MaxBudgetUsd = RunSpec.PerRunCeilingUsd,
        StopMode = StopRuleFor(kind),
        // Unchanged by the kind. An early-stop run needs the same room to REACH its decision; what
        // it saves is the work after that decision, not the time before it.
        Timeout = TimeSpan.FromMinutes(3),
    };

    /// <summary>
    /// Where a run of this kind is allowed to stop. #8 measured `FirstDecision` at about 9.5 seconds
    /// a run against about 40, and 50 of a 125-run pass are should-not-fire cases, so this is the
    /// largest single saving on a pass bounded by time rather than by money.
    ///
    /// A case graded on its fired SET must run to the end. Killing at the first decision truncates
    /// that set, and #10 grades a positive on an exact match, so a second skill firing later would be
    /// invisible and a wrong run would score green. A watch case is ungated but recorded, and the
    /// record is the full set, so it runs to the end too.
    ///
    /// A should-not-fire case is graded only on one skill staying quiet, so truncation can only hide
    /// a fire that came after another skill. #11 parked the change on exactly that risk; #12 output 6
    /// then measured it, and across all 65 negative and watch-list runs the skill under test never
    /// fired after another skill. The premise #11 rejected is now evidence, so the rule switches on
    /// for negatives and for nothing else.
    /// </summary>
    public static StopMode StopRuleFor(CaseKind kind) => kind switch
    {
        CaseKind.ShouldFire => StopMode.Completion,
        CaseKind.Watch => StopMode.Completion,
        CaseKind.ShouldNotFire => StopMode.FirstDecision,
        // Listed one by one rather than defaulted, so a fourth kind has to say which it is instead
        // of inheriting a rule that might silently truncate the set it is graded on.
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no stop rule declared for this case kind"),
    };
}

public sealed class ContractRunner(HarnessPaths paths)
{
    /// <summary>By-name invocation against the real body, in a throwaway copy of the bare repo.</summary>
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
            MaxBudgetUsd = RunSpec.PerRunCeilingUsd,
            StopMode = StopMode.Completion,
            Timeout = TimeSpan.FromMinutes(5),
        }, ct);
    }
}

/// <summary>Resample a case until it has enough VALID runs, or give up and say so.</summary>
public sealed record Sample(
    IReadOnlyList<RunScore> Scores,
    bool CapHit,
    bool BudgetExhausted = false,
    bool Throttled = false)
{
    public int Valid => Scores.Count(s => s.Verdict != Verdict.Void);

    /// <summary>
    /// Hitting the cap is a LAYER 3 failure and must be reported as one, not as a contract break.
    /// A throttle is neither: it is the machine refusing to answer, and it is checked FIRST so a
    /// limit can never be reported as a skill that would not fire.
    /// </summary>
    public string? Failure => Throttled ? "throttled"
        : BudgetExhausted ? "suite-budget-exhausted"
        : CapHit ? "insufficient-firings"
        : null;
}

public static class Resampler
{
    public static async Task<Sample> CollectAsync(
        int wanted, int cap, Func<CancellationToken, Task<RunScore>> once,
        SpendLedger? ledger = null, CancellationToken ct = default)
    {
        var scores = new List<RunScore>();
        var valid = 0;
        var attempts = 0;
        while (valid < wanted && attempts < cap)
        {
            if (ledger?.Exhausted == true) return new Sample(scores, CapHit: false, BudgetExhausted: true);
            attempts++;
            var score = await once(ct);
            scores.Add(score);
            ledger?.Record(score);
            // #12: a usage limit does not lift between attempts. Retrying spends the cap to reach the
            // same wall and then reports the wall as insufficient-firings.
            if (score.Throttled) return new Sample(scores, CapHit: false, Throttled: true);
            if (score.Verdict != Verdict.Void) valid++;
        }
        return new Sample(scores, valid < wanted);
    }
}
