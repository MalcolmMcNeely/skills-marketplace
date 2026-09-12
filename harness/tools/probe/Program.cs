using Harness;

var paths = new HarnessPaths(@"C:\Projects\skills-marketplace\harness");
var found = SuiteDiscovery.One(paths, "csharp-new-class");
var workDir = paths.NewScratchRepo();
Console.WriteLine($"workdir: {workDir}");
Console.WriteLine($"plugin : {found.Plugin}  exists={Directory.Exists(found.Plugin)}");

var outcome = await ClaudeCli.RunAsync(new RunSpec
{
    Prompt = $"/{found.Suite.SkillUnderTest} Add a Discount class that applies a percentage discount to an order total.",
    WorkingDirectory = workDir,
    PluginDirs = [found.Plugin],
    AllowedTools = ["Write", "Edit", "Read", "Bash", "Glob", "Grep"],
    MaxBudgetUsd = RunSpec.PerRunCeilingUsd,
    StopMode = StopMode.FirstDecision,
});

Directory.CreateDirectory(found.RunRecords);
var dump = Path.Combine(found.RunRecords, "probe-dump.jsonl");
File.WriteAllText(dump, outcome.RawStream);
Console.WriteLine($"dump   : {dump}");
Console.WriteLine($"exit={outcome.ExitCode} subtype={outcome.TerminalSubtype} killed={outcome.KilledAtDecision} started={outcome.Started}");
Console.WriteLine($"fired: [{string.Join(", ", outcome.Transcript.FiredSkillsRaw)}]");
Console.WriteLine($"lines: {outcome.RawStream.Split('\n').Length}");
