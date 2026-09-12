using System.Text.RegularExpressions;

namespace Harness;

/// <summary>
/// #9's amendment: an assertion that passes with no skill installed is a precondition, not a measurement.
/// Guards keep a Signal assertion from passing vacuously. They never count towards a pass rate.
/// </summary>
public enum AssertionKind { Guard, Signal }

public enum Evidence { Disk, Transcript }

public sealed record AssertionResult(int Number, string Description, AssertionKind Kind, Evidence Evidence, bool Passed, string Detail);

public interface IContractAssertions
{
    string Name { get; }
    IReadOnlyList<AssertionResult> Evaluate(ValidRun run);
}

/// <summary>
/// csharp-new-class, rule "test file first, files only" (#4, amended by #9).
/// Content comes from disk; order comes from the transcript. Split on purpose.
/// </summary>
public sealed class TestFirstFilesOnly(string className) : IContractAssertions
{
    public string Name => "TestFirstFilesOnly";

    private string ClassFile => $"src/{className}.cs";
    private string TestFile => $"tests/{className}Tests.cs";

    public IReadOnlyList<AssertionResult> Evaluate(ValidRun run)
    {
        var dir = run.WorkingDirectory;
        var classPath = Path.Combine(dir, "src", $"{className}.cs");
        var testPath = Path.Combine(dir, "tests", $"{className}Tests.cs");

        var classExists = File.Exists(classPath);
        var testExists = File.Exists(testPath);
        var testBody = testExists ? File.ReadAllText(testPath) : "";
        // #9: widen to [Theory]. The model reaches for it freely and a Theory-only file is a real test file.
        var hasFact = Regex.IsMatch(testBody, @"\[\s*(Fact|Theory)\b");

        var classAt = run.Transcript.FirstCreationOrdinal(ClassFile);
        var testAt = run.Transcript.FirstCreationOrdinal(TestFile);
        var orderKnown = classAt is not null && testAt is not null;
        var testFirst = orderKnown && testAt < classAt;

        var ranTests = run.Transcript.ShellCommands.Any(c => Regex.IsMatch(c, @"\bdotnet\s+test\b", RegexOptions.IgnoreCase));

        return
        [
            new(3, $"{ClassFile} exists", AssertionKind.Guard, Evidence.Disk, classExists, classPath),
            new(4, $"{TestFile} exists", AssertionKind.Guard, Evidence.Disk, testExists, testPath),
            new(5, "test file contains [Fact] or [Theory]", AssertionKind.Guard, Evidence.Disk, hasFact, testExists ? "read" : "no test file"),
            new(6, "test file written before the class file", AssertionKind.Signal, Evidence.Transcript, testFirst,
                orderKnown ? $"test@{testAt} class@{classAt}" : "one or both creations not seen in transcript"),
            new(7, "the run did not execute dotnet test", AssertionKind.Signal, Evidence.Transcript, !ranTests,
                ranTests ? "ran dotnet test" : "clean"),
        ];
    }
}
