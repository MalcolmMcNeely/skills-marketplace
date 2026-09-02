# Default test-first behaviour, measured

Written 2 September 2026 against Claude Code 2.1.248, model `claude-opus-5[1m]`. Every number below came from a run on this machine. Resolves [#9](https://github.com/MalcolmMcNeely/skills-marketplace/issues/9). Builds on [scoring.md](scoring.md) and the rule settled in [#4](https://github.com/MalcolmMcNeely/skills-marketplace/issues/4).

## The short version

**Adopt the rule.** With no skill installed, Claude wrote the test file first in **0 of 15 valid runs**. The acceptance bar in #9 was under 30 per cent. The measured rate is 0 per cent, and the Wilson 95 per cent interval tops out at 20.4 per cent.

Three findings arrived with it, and two of them change #4.

1. **Assertions 3, 4 and 5 do not discriminate.** All three passed 15 of 15 with no skill installed. Claude always wrote the class, always wrote a test file, and always put a literal `[Fact]` in it. Only assertion 6, the ordering, carries signal.
2. **The rule already contains a second discriminating check that #4 left out of the assertion table.** Every run ran `dotnet test`, 15 of 15. The rule forbids it. That is a second independent break, as strong as ordering, and it should become assertion 7.
3. **The permission mode changes how files get made.** Under `--permission-mode bypassPermissions` the model wrote both files with a single Bash heredoc in one assistant message. Under an `--allowedTools` allowlist it used two `Write` calls in two separate assistant messages, 15 of 15. Assertion 6 is cheap to parse in the second mode and awkward in the first.

## The acceptance test

From #9, verbatim:

> Adopt the rule only if default obedience is **under 30 per cent**. If it is higher, the rule from #4 has to change, and #6 must not be built until it does.

The reason it matters is stated in [evals.md](evals.md), quoting the published guidance:

> Remove or replace assertions that always pass in both configurations. These don't tell you anything useful ... They inflate the with-skill pass rate without reflecting actual skill value.

## The fixture

Bare, per #4. A solution, an empty class library at `src/`, an empty xUnit project at `tests/`, and **no worked example pair**. Rebuild it with:

```
dotnet new sln -n Fixture
dotnet new classlib -n Fixture.Core -o src
dotnet new xunit -n Fixture.Tests -o tests
rm src/Class1.cs tests/UnitTest1.cs
dotnet sln add src/Fixture.Core.csproj tests/Fixture.Tests.csproj
dotnet add tests/Fixture.Tests.csproj reference src/Fixture.Core.csproj
```

That leaves four files: `Fixture.slnx`, `.gitignore`, `src/Fixture.Core.csproj`, `tests/Fixture.Tests.csproj`. The fixture was committed to a local git repo, and each run got a fresh copy of it in its own working directory.

## The prompt and the command

The task prompt, which #9 left open, is now fixed. Exact wording:

```
Add a Discount class that applies a percentage discount to an order total.
```

It names one class, so the ordering is unambiguous. Claude named the files `src/Discount.cs` and `tests/DiscountTests.cs` in 15 of 15 runs, so the assertion paths are predictable.

The command, run with the working directory set to a fresh copy of the fixture:

```
claude -p "Add a Discount class that applies a percentage discount to an order total." \
  --output-format stream-json --verbose \
  --allowedTools Write Edit Read Bash Glob Grep \
  --max-budget-usd 0.60 \
  --settings '{"outputStyle":"default"}'
```

The `--settings` override is load bearing. This machine has an `ELI5` output style set at user level, and a child `claude -p` inherits it. The override pins the arm to the default style so the measurement is not a measurement of this machine's preferences.

The `init` line of every run confirmed the arm: `permissionMode: default`, `outputStyle: default`, `model: claude-opus-5[1m]`, and no `csharp-new-class` skill in the loaded set. The only user-level skill on this machine is `comment-sweep`, which has nothing to say about tests.

## Results

Fifteen runs. All fifteen exited 0 with a terminal `"subtype":"success"`, so none was `void` under [scoring.md](scoring.md)'s rule.

| Assertion | Check | Evidence | Passed with no skill |
|---|---|---|---|
| 3 | `src/Discount.cs` exists | disk | **15 of 15** |
| 4 | `tests/DiscountTests.cs` exists | disk | **15 of 15** |
| 5 | Test file contains `[Fact]` | disk | **15 of 15** |
| 6 | Test written before the class | transcript | **0 of 15** |
| Proposed 7 | The run did not execute `dotnet test` | transcript | **0 of 15** |

Full obedience, meaning assertions 3 to 6 together, was **0 of 15**.

| Statistic | Value |
|---|---|
| Default obedience, point estimate | **0.00** |
| Wilson 95 per cent interval | [0.000, 0.204] |
| Clopper-Pearson two-sided 95 per cent upper bound | 0.218 |

Ten runs would have given a point estimate of 0 as well, but a Clopper-Pearson upper bound of 0.309, which sits the wrong side of the 30 per cent bar. Fifteen runs put both interval methods under it, and fifteen also matches the run count used in [skill-targeting.md](skill-targeting.md).

The behaviour was not merely rare. It was uniform. Every one of the fifteen runs followed the same shape: explore the repo, write `src/Discount.cs`, write `tests/DiscountTests.cs`, then run `dotnet test`.

## The three things #9 asked to record

| Item | Answer |
|---|---|
| Default obedience rate | 0 of 15, a rate of 0.00 |
| Measured cost per run | Median `$0.302`, range `$0.269` to `$0.475`, `$4.73` for fifteen runs |
| Both `Write` calls in one assistant message or two? | **Two**, in 15 of 15 runs, always adjacent, class first |

Wall clock was a median of 64 seconds per run. Nine runs in parallel finished in about four minutes.

## What this changes in #4

**Keep assertion 6 and add assertion 7.** Ordering is the discriminating check, and it discriminates completely at this sample size. The `dotnet test` prohibition is a second one, free, from the same transcript, and it is already part of the rule as #4 wrote it. It is simply missing from the assertion table.

**Reclassify assertions 3, 4 and 5 as guards, not signal.** #4 already knew assertion 3 was load bearing for a different reason:

> Assertion 3 is load bearing. Without it a run that wrote only a test file would satisfy assertion 6 vacuously.

That reasoning holds and is the right reason to keep all three. The wrong reason to keep them is that they measure the skill, because with no skill installed they pass every time. They belong in the harness as preconditions for a meaningful assertion 6, and they must not be counted towards a pass rate. Counting them would report 3 of 5 assertions passing on a run with no skill at all, which is exactly the inflation the guidance warns about.

**A small risk on assertion 5.** Every run wrote both `[Fact]` and `[Theory]` attributes, with at least two literal `[Fact]` occurrences each. A strict `[Fact]` regex was safe in 15 of 15. It is still worth widening to `[Fact]` or `[Theory]`, because the model reaches for `[Theory]` freely and a `[Theory]`-only test file is a perfectly good test file that would score as a break.

## The permission-mode confound

One pilot run was made under `--permission-mode bypassPermissions` before the arm was fixed. It behaved differently in a way the harness needs to know about.

| Arm | How the files were made | Assistant messages | Order |
|---|---|---|---|
| `--permission-mode bypassPermissions`, 1 run | One Bash command, two `cat > file <<'EOF'` heredocs | 1 | class, then test |
| `--allowedTools` allowlist, 15 runs | Two `Write` calls | 2 | class, then test |

Bypass mode carries harness guidance telling the model to prefer the Bash tool wherever it can do the job. That guidance is not part of the skill under test, and it changed the shape of the evidence rather than the answer. Both arms broke the rule.

#4 already anticipated this and is vindicated:

> Content is read from disk because a model that creates a file by some route other than `Write`, such as a shell heredoc, leaves a transcript that a naive parser reads as a miss. That would surface as a fake regression.

Two consequences for the harness.

1. **Run layer 4 with `--allowedTools`, not `--permission-mode bypassPermissions`.** The allowlist arm gives non-interactive writes without the Bash nudge, and it makes assertion 6 readable from `Write` calls alone.
2. **Parse shell commands for file creation anyway.** Ordering must survive a heredoc, because nothing guarantees the model will not reach for one. The scorer used here matched `Write` file paths and also matched redirect and `Set-Content` targets inside `Bash` and `PowerShell` commands, then compared first-creation positions. Both files landing in a single command is a real outcome, and the order inside the command string is still readable.

## Cost, and what it changes in scoring.md

[scoring.md](scoring.md) measured about `$0.043` per run and priced a contract case at about `$0.20` for five runs. Those runs were read-only, made with `--disallowedTools Write Edit Bash NotebookEdit`, and produced a single reply. A layer 4 contract run does real work.

| Run shape | Measured median cost |
|---|---|
| Read-only probe, scoring.md | `$0.043` |
| Writes two files and runs `dotnet test`, here | `$0.302` |

That is roughly seven times more. A contract case at five runs costs about **`$1.50`**, not `$0.20`. The firing suite is unaffected, because firing runs can still be killed at the first tool call. The nightly figure in scoring.md needs the contract line recomputed, and the difference is small in absolute terms because contract runs are a small share of the total.

## What we could not verify

- **Whether the same numbers hold with the skill installed and invoked by name.** This was the baseline arm only. Nothing here ran `csharp-new-class`, because it does not exist yet. The with-skill arm is #6's job.
- **Whether the two `Write` calls stay in two separate assistant messages once the skill fires.** A skill that states an ordering rule may cause the model to batch both writes into one message. The order would still be readable from block sequence, but that was not run, and it is the same gap #4 logged.
- **Whether the twin decoy `csharp-new-test` can fire on this prompt.** Not tested here. That is a layer 3 question.
- **The cost of a by-name contract run.** A skill that tells the model exactly what to do may explore less and cost less than this baseline. Unknown.
- **Whether the result transfers to another model or CLI version.** One model, one CLI version, one machine. This is the same drift worry the map already records under "Not yet specified".
- **The bypass-mode arm is a single run.** The difference in file-creation route is clear enough to act on, but one run is not a rate.
- **Whether `[Theory]`-only test files occur.** Not observed in 15 runs, all of which also contained `[Fact]`. The recommendation to widen the regex is a precaution, not a measured need.
