# The harness skeleton, built and run

Written 2 September 2026 against Claude Code 2.1.248, model `claude-opus-5[1m]`. Resolves [#8](https://github.com/MalcolmMcNeely/skills-marketplace/issues/8). The prototype it describes lives on the throwaway branch [`prototype/harness-skeleton`](https://github.com/MalcolmMcNeely/skills-marketplace/tree/prototype/harness-skeleton) and is not merged. Builds on [scoring.md](scoring.md), [baseline-test-first.md](baseline-test-first.md) and the case suite in [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10).

## The short version

The shape works. One solution, three projects, four layers. The free half runs 27 tests in under a second with no model call and no network. Layer 3 fired 3 of 3 and layer 4 held 3 of 3 on real runs.

Three assumptions in the plan were wrong, and each was caught by running rather than by reading. The most expensive one cost `$2.28` in voided runs before the harness gave up.

| Assumption | What a real run showed |
|---|---|
| The stream reports the skill's own name | It reports `harness-fixture-good:csharp-new-class`. The prefix is the fixture's name |
| A firing run can be killed at the first tool call | The model opens with `Bash` or `Glob`. Killing there scores a healthy run as a miss |
| A by-name run emits a `Skill` tool call | Outside Git Bash the CLI expands the slash command inline. No `Skill` call appears at all |

## The eight answers

| # | Question | Answer |
|---|---|---|
| 1 | Project layout | `harness/` at the repo root, with a `HARNESS-ROOT` marker file. A layer 2 test asserts no fixture name appears in `plugins/`, and that `marketplace.json` never mentions the harness |
| 2 | One project or two | Three. `src/Harness` is the library. `Harness.Free.Tests` is layers 1 and 2. `Harness.Model.Tests` is layers 3 and 4, and it also refuses to run without `SKILL_HARNESS_LIVE=1` |
| 3 | Reading the stream | Every `Skill` invocation is recorded, deduplicated, with the plugin prefix stripped. `FiredSkillsRaw` keeps the originals |
| 4 | The exit-code trap | Closed by construction. Scoring takes a `ValidRun`, and the only way to obtain one is `RunOutcome.TryGetValid` |
| 5 | Case format | JSON for the data, a named C# class for the assertions |
| 6 | Fixture loading | `--plugin-dir`, one directory per fixture, under `harness/fixtures/` |
| 7 | Two run shapes | `FiringRunner` and `ContractRunner` are separate types, so a case cannot reach the wrong one |
| 8 | Verdicts and resampling | `Resampler.CollectAsync` runs until it has enough valid runs, capped, and now also stops on a suite-wide spend ceiling |

### Why the case format is a hybrid

Prompts are edited far more often than the harness, and a reviewer should not need a compiler to read the ten should-fire queries. So the case is JSON.

An assertion is not data. "The test file was written before the class file" reads an ordinal out of the transcript and compares two positions. Expressing that in JSON means inventing a DSL. So the case names a C# class instead:

```json
{ "id": "C1", "assertions": "TestFirstFilesOnly", "assertionArgs": { "className": "Discount" } }
```

`AssertionCatalogue.Resolve` maps the name to the class, and a free test asserts that every name in every case file resolves.

### Why the trap cannot reopen

`RunOutcome` is not scoreable. It carries the exit code and the terminal subtype, and nothing else can read them:

```csharp
public bool TryGetValid([NotNullWhen(true)] out ValidRun? run)
{
    run = IsValid ? new ValidRun(this) : null;
    return run is not null;
}
```

`ValidRun`'s constructor is `internal` and is called from one place. Every scoring method takes a `ValidRun`. A future contributor who forgets the check cannot compile.

## What the real runs showed

### The skill name carries the plugin's name

The prompt `/csharp-new-class ...` produced this in the stream, verbatim:

```
"name":"Skill","input":{"skill":"harness-fixture-good:csharp-new-class","args":"Add a Discount class ..."}
```

`harness-fixture-good` is the fixture plugin's name, and it changes between the good fixture and the broken ones that [#6](https://github.com/MalcolmMcNeely/skills-marketplace/issues/6) will build. A case that named the qualified skill would have to be edited for every fixture, and a case that named the bare skill would score `wrong-set` on a healthy run. The parser strips the prefix and keeps the original as a diagnostic.

### `run_eval.py`'s cheap trick does not transfer

[scoring.md](scoring.md) planned to kill a firing run at the first tool call, following `run_eval.py`, and noted the saving as unmeasured. Two runs on a natural-language prompt in the bare fixture repo:

| Run | Tool calls, in order |
|---|---|
| 1 | `Bash` |
| 2 | `Bash`, `Skill`, `Read`, `Read`, `Read` |

Both would have been killed at the `Bash` call, and both would have been recorded as the skill declining to fire. Run 2 fired. The model looks around before it picks a skill, because in a real repository there is something to look at. `run_eval.py` writes a stub with nothing to explore, which is why the trick works there and not here.

The prototype replaces it with a `FirstDecision` stop rule: kill at the first `Skill` call, or at the first `Write` or `Edit` call, whichever comes first. That rule is sound and it is measurably faster, at about 9.5 seconds a run against about 40.

**It is not used, and the reason is [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10).** A positive case is graded on an exact set match. Killing at the first `Skill` call truncates the set, so a second skill firing later in the run is invisible, and a cross-stack case would score green while being wrong. A killed run also emits no `result` line and therefore cannot report its own cost. Firing runs are allowed to finish.

### A by-name run does not reliably emit a `Skill` call

This is the one that cost money. [scoring.md](scoring.md) recorded, from two runs:

> `claude -p "/wf-probe <task>"` was run twice here. Both exited 0 with `"subtype":"success"`, both emitted `{"type":"tool_use","name":"Skill","input":{"skill":"wf-probe"}}`

The prototype's layer 4 therefore treated a missing `Skill` call as a harness fault and voided the run. Ten consecutive runs voided, at `$2.28`, before the resample cap fired.

The runs were healthy. Their streams were not kept, so the evidence comes from two runs reproduced afterwards under the same conditions. In one, killed early on purpose, the model narrated the rule it had just been given:

> Empty project ... Writing the test first per the rule.

The other ran to completion. It wrote `tests/DiscountTests.cs` first, then `src/Discount.cs`, and never executed `dotnet test`. In both, the `slash_commands` field of the `init` line listed `harness-fixture-good:csharp-new-class`, so the plugin loaded. The CLI had expanded the slash command inline rather than routing it through the `Skill` tool. Both captures are committed on the prototype branch under `harness/captured/`, and a free test scores the complete one `Held` on both signal assertions.

| Shell the CLI was launched from | `Skill` tool calls |
|---|---|
| Git Bash, 2 runs | 1 each |
| PowerShell 7, 1 run | 0 |
| .NET `Process` with `UseShellExecute=false`, 11 runs | 0 |

The mechanism behind the split is not established. What matters is that layer 4 must not depend on it. **Layer 4's precondition is now that the skill is registered as a slash command on the `init` line**, which proves the `--plugin-dir` fixture loaded, costs nothing, and is not a model behaviour at all.

There is a second reading worth stating plainly. Inline expansion means the body reaches the model without a `Skill` call, which is exactly what layer 4 wants to test. Requiring the call was testing the delivery mechanism, not the body.

### A per-run budget does not bound a suite

Each of the ten void runs stayed inside its own `--max-budget-usd 0.60`. The suite still spent `$2.28`, because the resample cap counts attempts and not money. The prototype adds a `SpendLedger`: a running total across the whole suite with a hard stop, reported as `suite-budget-exhausted` and distinct from `insufficient-firings`.

The ledger only works because firing runs are allowed to finish. A killed run reports no cost, so a suite of killed runs would spend without the ledger noticing.

## Cost, measured

Ten valid runs through the harness, plus manual probes alongside them.

| Run shape | Runs | Median cost | Wall clock |
|---|---|---|---|
| Layer 3, natural prompt, to completion | 3 | `$0.196` | ~40s |
| Layer 4, by name, to completion | 3 | `$0.207` | ~35s |
| Layer 3, killed at the first decision | 4 | not reportable | ~9.5s |

This is the third cost figure this repo has produced, and the spread matters.

| Source | Run shape | Median |
|---|---|---|
| [scoring.md](scoring.md) | Read-only probe, `--disallowedTools Write Edit Bash NotebookEdit` | `$0.043` |
| [baseline-test-first.md](baseline-test-first.md) | Writes two files, runs `dotnet test` | `$0.302` |
| Here | Layer 3, natural prompt, writes files | `$0.196` |

**A firing run is not cheap.** [scoring.md](scoring.md) assumed it was, and priced the nightly pass on that assumption:

> Firing runs are unaffected, because they can still be killed at the first tool call.

That is no longer true for our case shape. At `$0.196` a run, [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10)'s 125-run layer 3 pass costs about **`$24.50`** per skill, not the `$5` implied by `$0.04`. One contract case at five runs is about `$1.05`. A twelve-engine nightly is therefore nearer `$300` than `$43`, and that number belongs to [#7](https://github.com/MalcolmMcNeely/skills-marketplace/issues/7).

## What this changes elsewhere

| Document | Change |
|---|---|
| [scoring.md](scoring.md) | The "kill at the first tool call" saving does not apply. Firing runs cost about `$0.196`, and the nightly figure needs recomputing |
| [scoring.md](scoring.md) | By-name invocation cannot be detected from the `Skill` tool call. The two-run observation behind that claim does not generalise |
| [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10) | Expected sets stay bare. The harness strips the plugin prefix |
| [#7](https://github.com/MalcolmMcNeely/skills-marketplace/issues/7) | The budget it must provision is roughly six times the current estimate. A suite-wide ceiling exists in the harness and CI needs one above it |

## What we could not verify

- **Why the `Skill` call appears under Git Bash and not otherwise.** Fourteen runs split cleanly along that line, but the mechanism was not isolated and no hypothesis was tested. The fix does not depend on the answer, and the answer would not change it.
- **Whether the `slash_commands` precondition holds for a broken fixture.** It proves the plugin loaded. It does not prove the body was read. [#6](https://github.com/MalcolmMcNeely/skills-marketplace/issues/6) is the test of that, and until it runs, layer 4 is proven only against a fixture that passes.
- **The firing rate.** Three of three fired, on one prompt. That is not a rate, and it says nothing about the 0.67 the pass mark rests on. The calibration pass is still owed.
- **Whether the `FirstDecision` stop rule is safe for negative cases.** A negative case is graded only on one skill staying quiet, so truncation is harmless there and the saving is real. It was not run that way.
- **Cost stability.** Nineteen runs on one machine, one model, one CLI version, one afternoon. The `$0.196` and `$0.207` medians have no interval attached.
- **Layer 2's actual assertions.** Three placeholder tests show the shape. What layer 2 asserts is [#5](https://github.com/MalcolmMcNeely/skills-marketplace/issues/5), still open, and nothing here settles it.
- **Whether the twelve stubs behave like the twelve in [skill-targeting.md](skill-targeting.md).** They are new descriptions written for [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10), not the originals.
