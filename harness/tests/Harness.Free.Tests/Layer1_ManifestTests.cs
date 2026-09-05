using System.Diagnostics;
using Harness;
using Xunit;

namespace Harness.Free.Tests;

/// <summary>Layer 1: claude plugin validate --strict. Free, and it gates every PR.</summary>
public class Layer1_ManifestTests
{
    [Fact]
    public void The_shipped_marketplace_validates_strictly()
    {
        var repo = Path.GetFullPath(Path.Combine(new HarnessPaths().Root, ".."));
        var psi = new ProcessStartInfo("claude")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "plugin", "validate", ".", "--strict" }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"validate --strict failed\n{stdout}\n{stderr}");
    }
}
