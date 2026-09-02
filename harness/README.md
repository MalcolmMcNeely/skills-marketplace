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
