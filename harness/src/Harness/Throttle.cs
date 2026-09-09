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
/// MEASURED on 9 September 2026, and the markers were not enough. #6's pass hit a real wall at
/// 11:21 UTC: 89 consecutive runs came back exit 1 with subtype "success", $0.00 and 1.7 seconds.
/// No marker text, and the old guard returned false on the subtype before it read stderr at all, so
/// the Resampler retried 89 times and reported the wall as insufficient-firings. Two arms were lost.
///
/// So detection now has two halves. The MARKERS below name a limit in the CLI's own words. The
/// SHAPE below that needs no words: a run that failed its gate, billed nothing and returned in under
/// five seconds did no work, and no number of retries will change that.
///
/// Marker detection is narrow on purpose, and narrow in two ways:
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
    public static bool Detect(bool isValid, string? resultText, string? standardError)
    {
        // Keyed on the VALIDITY GATE, not on the subtype. #6 measured a wall that reported subtype
        // "success" and still exited 1, and the old subtype guard returned false before reading a
        // single word of stderr. A run is a candidate for a limit exactly when it is not a result.
        if (isValid) return false;
        return Matches(resultText) || Matches(standardError);
    }

    /// <summary>MEASURED: a refused run returned in 1.6 to 1.8 seconds. A real one takes 35 to 65.</summary>
    public static readonly TimeSpan RefusalCeiling = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A wall with no words for it. The machine refused before doing any work, so the run billed
    /// nothing and came back at once. Retrying spends the cap to reach the same wall, which is the
    /// same reason a named limit stops the pass.
    ///
    /// A budget abort is not this: it billed for the work it did before the CLI cut it off.
    /// </summary>
    public static bool Refused(bool isValid, decimal? costUsd, TimeSpan duration) =>
        !isValid && costUsd is null or 0m && duration < RefusalCeiling;

    public static bool Matches(string? text) =>
        !string.IsNullOrEmpty(text)
        && Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
