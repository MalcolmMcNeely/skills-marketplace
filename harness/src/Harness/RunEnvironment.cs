using System.Diagnostics;

namespace Harness;

/// <summary>
/// What produced a number. #12 comment point 3: RunSpec.Model was null, so every run took whatever
/// the CLI defaulted to. Every figure on this map says claude-opus-5[1m] and nothing held it there,
/// so a default change would move the numbers with no trace in the record.
///
/// The model is now pinned on both runners and recorded next to the CLI version. Override with
/// SKILL_HARNESS_MODEL when you deliberately want to measure drift across models.
/// </summary>
public sealed record RunEnvironment(string Model, string CliVersion)
{
    /// <summary>Every measurement on this map was taken on this model.</summary>
    public const string DefaultModel = "claude-opus-5[1m]";

    private static RunEnvironment? _current;
    private static readonly Lock Gate = new();

    /// <summary>Resolved once per process, because shelling out for --version on every run is waste.</summary>
    public static RunEnvironment Current
    {
        get
        {
            lock (Gate) return _current ??= new RunEnvironment(ResolveModel(), ReadCliVersion());
        }
    }

    public static string ResolveModel()
    {
        var pinned = Environment.GetEnvironmentVariable("SKILL_HARNESS_MODEL");
        return string.IsNullOrWhiteSpace(pinned) ? DefaultModel : pinned.Trim();
    }

    /// <summary>"2.1.248 (Claude Code)" on this machine. Never throws: an unknown version is recorded as unknown.</summary>
    public static string ReadCliVersion()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("claude", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (proc is null) return "unknown";
            var text = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            var line = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
            return string.IsNullOrEmpty(line) ? "unknown" : line;
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// The init line reports the model the session actually resolved to. A run whose init line
    /// disagrees with the pin is measuring something else, and the pass must say so rather than
    /// average it in.
    /// </summary>
    public bool Matches(Transcript transcript) =>
        transcript.Model is null || transcript.Model.Equals(Model, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"model={Model} cli={CliVersion}";
}
