namespace Harness;

/// <summary>The suite a resume override belongs to, and the path it named.</summary>
public sealed record ResumePoint(DiscoveredSuite Suite, string Path);

/// <summary>
/// Issue #27. Which suite a resume override continues.
///
/// Both long passes take a path from the environment to pick up a run a usage limit cut short. While
/// a pass loaded one suite that path could only mean one thing. A pass now runs over every discovered
/// suite, so the same path has to say WHICH suite it resumes, or a finished suite is paid for twice.
///
/// A run record already answers it. #26 put every journal under the suite it measured, so the folder
/// the path sits in is the answer. Every other suite in the same pass starts fresh.
///
/// A path under no suite throws rather than resolving to nothing. Resuming nothing looks exactly like
/// a pass that has never run, and #12's pass was 133 runs and 02:08:14.
/// </summary>
public static class ResumeOverride
{
    /// <summary>
    /// Null when nothing is set. A relative path resolves against the harness ROOT, not the process
    /// working directory: the test host runs from its own build output, and #26 measured what that
    /// costs by putting a whole pass's journals under bin/Debug.
    /// </summary>
    public static ResumePoint? Resolve(string? value, IReadOnlyList<DiscoveredSuite> suites, string root)
    {
        if (value is not { Length: > 0 }) return null;

        var path = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
        List<DiscoveredSuite> owners = [.. suites.Where(s => HarnessPaths.Inside(s.RunRecords, path))];

        return owners.Count switch
        {
            1 => new ResumePoint(owners[0], path),
            0 => throw new InvalidOperationException(
                $"resume path '{path}' sits under no suite's run records, so no pass can tell which suite it "
                + $"continues. Put it under one of: "
                + $"{(suites.Count == 0 ? "nothing, because no suite was discovered" : string.Join(", ", suites.Select(s => s.RunRecords)))}."),
            _ => throw new InvalidOperationException(
                $"resume path '{path}' sits under the run records of more than one suite "
                + $"({string.Join(", ", owners.Select(s => s.Name))}), so no pass can tell which one it continues."),
        };
    }

    /// <summary>
    /// The record a suite continues, or the fresh one it should start. Both long passes ask this, so
    /// neither has to unpack the override and compare names itself.
    /// </summary>
    public static string PathFor(ResumePoint? resume, DiscoveredSuite suite, string fresh) =>
        resume?.Suite.Name == suite.Name ? resume.Path : fresh;
}
