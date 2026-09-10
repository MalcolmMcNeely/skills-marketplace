using System.Diagnostics;
using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>Layer 1: claude plugin validate --strict. Free, and it gates every PR.</summary>
public class Layer1_ManifestTests
{
    /// <summary>
    /// Both manifests, because CLAUDE.md names two commands and #20 made the gate run both. Nothing
    /// skips when the CLI is absent. A layer that reports a pass having run nothing is worse than no
    /// layer 1. Validate needs no login, on a laptop or on a runner. See docs/ci-plugin-validate.md.
    /// </summary>
    [Theory]
    [InlineData(".")]
    [InlineData("./plugins/core")]
    public void The_shipped_manifest_validates_strictly(string target)
    {
        var psi = new ProcessStartInfo("claude")
        {
            WorkingDirectory = new HarnessPaths().RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "plugin", "validate", target, "--strict" }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"validate {target} --strict failed\n{stdout}\n{stderr}");
    }
}
