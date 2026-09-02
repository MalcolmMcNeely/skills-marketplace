using Harness;

var paths = new HarnessPaths(@"C:\Projects\skills-marketplace\harness");
var workDir = paths.NewScratchRepo();
Console.WriteLine($"workdir: {workDir}");
Console.WriteLine($"plugin : {paths.GoodPlugin}  exists={Directory.Exists(paths.GoodPlugin)}");

var outcome = await ClaudeCli.RunAsync(new RunSpec
{
    Prompt = "/csharp-new-class Add a Discount class that applies a percentage discount to an order total.",
    WorkingDirectory = workDir,
    PluginDirs = [paths.GoodPlugin],
    AllowedTools = ["Write", "Edit", "Read", "Bash", "Glob", "Grep"],
    MaxBudgetUsd = 0.60m,
    StopMode = StopMode.FirstDecision,
});

File.WriteAllText(@"C:\Projects\skills-marketplace\harness\captured\probe-dump.jsonl", outcome.RawStream);
Console.WriteLine($"exit={outcome.ExitCode} subtype={outcome.TerminalSubtype} killed={outcome.KilledAtDecision} started={outcome.Started}");
Console.WriteLine($"fired: [{string.Join(", ", outcome.Transcript.FiredSkillsRaw)}]");
Console.WriteLine($"lines: {outcome.RawStream.Split('\n').Length}");
