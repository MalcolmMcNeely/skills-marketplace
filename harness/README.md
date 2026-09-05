# Skill test harness — PROTOTYPE

Throwaway. Built to answer [#8](https://github.com/MalcolmMcNeely/skills-marketplace/issues/8):
what shape does the harness take? It is not shipped, it is not referenced by
`.claude-plugin/marketplace.json`, and nothing under `fixtures/` is a real skill.

## Run it

```
dotnet test harness/tests/Harness.Free.Tests      # layers 1 and 2. Free. ~1 second.
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/Harness.Model.Tests   # layers 3 and 4. Costs money.
```

The paying half is a separate project **and** refuses to run without `SKILL_HARNESS_LIVE=1`.
Two locks, because one is forgettable.

## Layout

| Path | What |
|---|---|
| `src/Harness/` | Stream parsing, verdicts, pooling, the two run shapes |
| `tests/Harness.Free.Tests/` | Layers 1 and 2, plus offline parser tests against captured streams |
| `tests/Harness.Model.Tests/` | Layers 3 and 4. Real `claude -p` runs |
| `cases/` | Case files. Data |
| `fixtures/good/` | The good `csharp-new-class` plugin, loaded with `--plugin-dir` |
| `fixtures/catalogue/` | Twelve description-only stubs for layer 3 |
| `fixtures/repo/` | The bare .NET fixture repo from `docs/baseline-test-first.md` |
| `captured/` | Real stream-json, so the parser tests need no model call |
| `tools/probe/` | One-shot debugging runner |

## The exit-code trap, closed by construction

Scoring functions take a `ValidRun`. The only way to get one is `RunOutcome.TryGetValid`,
which applies the validity gate. There is no code path that scores an unchecked run.

## What a case looks like

Data in JSON, assertions in C#. `"assertions": "TestFirstFilesOnly"` names a class in
`AssertionCatalogue`. Ordering ("test written before class") cannot be expressed in JSON
without inventing a DSL, and a prompt list should not need a compiler to edit.

## Measured on this machine

Claude Code 2.1.248, `claude-opus-5[1m]`, 2 September 2026.

| Run shape | Runs | Median cost | Wall clock |
|---|---|---|---|
| Layer 3, natural prompt, to completion | 3 | `$0.196` | ~40s |
| Layer 4, by name, to completion | 3 | `$0.207` | ~35s |
| Layer 3, killed at the first decision | 4 | not reportable | ~9.5s |

A killed run emits no `result` line, so it cannot report its own cost. That is why firing
runs are allowed to finish.

## Three things a real run disagreed with

1. **The stream reports a qualified skill name**, `harness-fixture-good:csharp-new-class`.
   The prefix is the fixture's name, so a case must never contain it. `StreamParser.Unqualify`
   strips it and `FiredSkillsRaw` keeps the original for diagnostics.
2. **Killing at the first tool call reads a healthy run as a miss.** The model opens with
   `Bash` or `Glob` to look around before it picks a skill. `run_eval.py`'s trick does not
   transfer to a natural prompt in a real repository.
3. **A by-name run does not reliably emit a `Skill` tool_use.** Outside Git Bash the CLI
   expands the slash command inline, the body reaches the model, and the rule is obeyed with
   no `Skill` call in the stream. Layer 4 checks the init line's `slash_commands` instead.
