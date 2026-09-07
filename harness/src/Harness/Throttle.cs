namespace Harness;

/// <summary>
/// #12 comment point 2: a throttled run may be indistinguishable from a void one. The harness voids
/// any run that does not exit 0 with "subtype":"success" and RESAMPLES it. If a usage limit produces
/// that shape, the resample cap burns and the pass reports insufficient-firings, which is a layer 3
/// failure rather than a harness problem.
///
/// DECIDED: a throttled run is void, is never resampled, and stops the pass.
///
/// Void is the right verdict, because the harness did break the run and it carries no signal about
/// the skill. Resampling is the wrong response, because a usage limit does not lift between attempts:
/// retrying spends the cap to reach the same wall, then mislabels the wall as a missed skill.
/// Stopping is the right response, because the journal holds every completed run, so a stopped pass
/// is resumable and a mislabelled one is not.
///
/// NOT MEASURED. A usage limit cannot be provoked on demand, so these markers are read from the
/// shapes the CLI is known to emit, not observed on this machine. Detection is therefore narrow on
/// purpose, and narrow in two ways:
///
/// 1. It only looks at a run that ALREADY failed the validity gate, so it can never turn a real
///    result into a throttle.
/// 2. It only reads the CLI's OWN words: the result line's text and stderr. It never reads the
///    transcript, because the model writes arbitrary text and a marker like "429" appears in ordinary
///    code. Matching the whole stream would let the model throttle its own pass.
/// </summary>
public static class Throttle
{
    /// <summary>Substrings, matched case-insensitively, against the CLI's own error text only.</summary>
    public static readonly string[] Markers =
    [
        "usage limit",
        "rate_limit_error",
        "rate limit",
        "rate-limit",
        "overloaded_error",
        "quota",
        "too many requests",
        "429",
    ];

    /// <summary>
    /// True only for a run that failed its validity gate AND whose own error text names a limit.
    /// A run that never reached an init line is included, because a limit refused at session start
    /// looks exactly like that.
    /// </summary>
    public static bool Detect(string? terminalSubtype, string? resultText, string? standardError)
    {
        if (terminalSubtype == "success") return false;
        return Matches(resultText) || Matches(standardError);
    }

    public static bool Matches(string? text) =>
        !string.IsNullOrEmpty(text)
        && Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
