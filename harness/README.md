# Skill test harness

The repo's quality gate for `plugins/core/skills/`. Four layers, cheapest first. Layers 1 and 2 are
free and run on every push and every pull request. Layers 3 and 4 call a real model, cost money and
run on a laptop when somebody asks for them.

The harness is not shipped. `.claude-plugin/marketplace.json` does not reference it, and nothing
under `skills/` or `shared/` is a real skill.

## What each layer checks

| Layer | Question | Cost | Where it runs |
|---|---|---|---|
| 1. Manifests | Does `claude plugin validate --strict` accept the marketplace and the plugin? | Free | Every push and pull request |
| 2. Integrity, budget and coverage | Does every composition reference resolve? Is the catalogue inside the engine cap, and every description under 1024 characters? Does any skill reach an engine by slash form? Does every engine have a suite? | Free | Every push and pull request |
| 3. Firing accuracy | Given a natural prompt, does the right set of skills fire from the description alone? | About `$0.23` a run | On demand, locally |
| 4. Contract | Invoked by name, does the skill's body do what it promises? | About `$0.24` a run | On demand, locally |

Layers 3 and 4, both long passes and both screens run over every suite discovery returns. A skill
folder landing in `skills/` widens all of them by being there, with nothing to wire in. A free test
holds them to it, because a layer that narrowed back to one skill would stay green while it stopped
measuring the rest.

Layers 3 and 4 are independent on purpose. Layer 3 owns the description and layer 4 owns the body,
so a red layer names which half broke. [breakage.md](../docs/breakage.md) measured that separation:
a broken description reddened layer 3 at 25 of 60 while layer 4 held at 5 of 5, and a broken body
reddened layer 4 at 0 of 8 while layer 3 held at 6 of 6.

## Every engine needs a suite

Layer 2 reads the shipped catalogue and the suite folders together, and fails naming any engine
nothing measures. An engine is model-invocable: it fires on its own judgement, so untested it is
behaviour nobody has checked. An entry point carries `disable-model-invocation: true`, a developer
types it by name, and there is no firing decision to measure, so it is exempt.

A suite covers an engine when the file it resolves is the shipped file, which only a `catalogue`
suite can be. A fixture suite of the same name tests a copy, and a copy drifts.

**This assertion is red today.** `skill-authoring` ships with no suite, and writing one costs model
runs, so [#30](https://github.com/MalcolmMcNeely/skills-marketplace/issues/30) tracks it. The red is
the point: ship a model-invocable skill with no tests and the free gate says so on the next push, for
nothing, in about a second.

## The gate

**53 of 60 pooled should-fire runs.** Below that, layer 3 is red.

The number comes from two passes against the frozen good fixture, 120 runs in total. #12 scored 60 of
60, dated 8 September 2026 in [calibration.md](../docs/calibration.md), and #6's control scored 60 of
60 again on 9 September 2026. The gate is not 60. A perfect score must not set a perfect gate, so the
bar comes from the lower bound of #12's interval, 0.940, rather than its point estimate of 1.000. That
bound is computed on that pass's 60 runs; the second pass replicates it rather than widening it.
That doc carries the formula and the working.

## Run it

```
dotnet test harness/tests/Harness.Free.Tests      # layers 1 and 2. Free. ~1 second.
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/Harness.Model.Tests   # layers 3 and 4. Costs money.
```

The paying half is a separate project **and** refuses to run without `SKILL_HARNESS_LIVE=1`.
Two locks, because one is forgettable.

The long passes carry a third lock each, so none is tripped by running the project:
`SKILL_HARNESS_CALIBRATE=1` for #12 (133 runs, 02:08:14 measured), `SKILL_HARNESS_BREAK=1` for #6
(256 runs, 03:19:34 measured), `SKILL_HARNESS_LADDER=1` for #6's screen, which is 9 runs a suite, and
`SKILL_HARNESS_DENSITY=1` for the comment-density pass (60 runs, 58:02 and $18.38 measured).
All four figures come from one skill, and each pass now runs over every suite discovery returns.

The density pass answers a different question from the rest: not whether a skill fires or obeys its
rule, but how heavily commented the C# a run writes is, across four arms that move one comment rule
between `CLAUDE.md` and an output style. It takes `SKILL_HARNESS_DENSITY_RUNS` for runs per arm
(default 15) and writes a `comment-density-*.jsonl` beside the suite. It does not resume: at 15 runs
an arm a stopped pass is cheap to start again, and a half-filled arm must never be averaged with a
full one. The findings are in `docs/research/comment-density-measured.md`.

The two long passes write into `skills/<name>/runs/`, beside the suite they measured, and both
resume: point `SKILL_HARNESS_JOURNAL` or `SKILL_HARNESS_BREAK_DIR` at the run that stopped. The path
also says WHICH suite it continues, by the suite folder it sits under. Every other suite in the pass
starts fresh, and the pass refuses a path under no suite rather than guessing. A relative path
resolves against `harness/`, not against the test host's working directory, which is the build
output folder.

## Layout

| Path | What |
|---|---|
| `src/Harness/` | Stream parsing, verdicts, pooling, the two run shapes |
| `tests/Harness.Free.Tests/` | Layers 1 and 2, plus offline parser tests against captured streams |
| `tests/Harness.Model.Tests/` | Layers 3 and 4. Real `claude -p` runs |
| `skills/<name>/` | One folder per skill under test. Everything that skill is tested on |
| `shared/catalogue/` | Twelve description-only stubs for layer 3 |
| `shared/repo/` | The bare .NET fixture repo from `docs/baseline-test-first.md` |
| `shared/streams/` | Real stream-json, so the parser tests need no model call |
| `tools/probe/` | One-shot debugging runner |

## What a suite folder holds

`SuiteDiscovery` scans `skills/` and returns one suite per folder. Nothing else decides which skills
are under test, no layer spells out a path inside one, and no layer names one.

| Path | What |
|---|---|
| `suite.json` | The cases. Data |
| `plugin/` | The skill under test as a loadable plugin, for `--plugin-dir`. Fixture suites only |
| `breaks/` | #6's break overlays, grouped by the break. Sparse trees laid over a base at run time |
| `runs/` | Journals and rendered reports written by this suite's paid passes |

A suite declares where its skill lives, so `plugin/` is not always there. A `catalogue` suite reads
the skill from `plugins/` at run time instead. See **What a case looks like** below.

## The exit-code trap, closed by construction

Scoring functions take a `ValidRun`. The only way to get one is `RunOutcome.TryGetValid`,
which applies the validity gate. There is no code path that scores an unchecked run.

## What a case looks like

Data in JSON, assertions in C#. `"assertions": "TestFirstFilesOnly"` names a class in
`AssertionCatalogue`. Ordering ("test written before class") cannot be expressed in JSON
without inventing a DSL, and a prompt list should not need a compiler to edit.

`"source"` says where the skill under test lives, and takes one of two values. `fixture` reads it
from the suite's own material, so a deliberately broken skill can never be mistaken for catalogue
content. `catalogue` reads it from `plugins/` at run time and never copies it, because a copy drifts
the moment the original is edited and then tests text nobody ships. There is no default: a suite
that does not say is refused by `SuiteFile.Load`.

## Measured on this machine

Claude Code 2.1.248, `claude-opus-5[1m]`, 2 September 2026.

| Run shape | Runs | Median cost | Wall clock |
|---|---|---|---|
| Layer 3, natural prompt, to completion | 3 | `$0.196` | ~40s |
| Layer 4, by name, to completion | 3 | `$0.207` | ~35s |
| Layer 3, killed at the first decision | 4 | not reportable | ~9.5s |

Two later passes are larger samples. #12, on 8 September 2026, journalled 133 runs for `$30.01` at a
positive median of `$0.231`. #6, on 9 September 2026, journalled 256 for `$46.99`, at layer 3 medians
of `$0.226` to `$0.305` by arm and a layer 4 median of `$0.244`.

A killed run emits no `result` line, so it cannot report its own cost. That is why a case
graded on its fired set runs to the end. A should-not-fire case is graded on one skill
staying quiet, so it takes the kill and the 9.5 seconds instead. That is 50 runs of a
125-run pass ([#17](https://github.com/MalcolmMcNeely/skills-marketplace/issues/17)).
It is also why the suite ledger charges a killed run a flat `$0.23` rather
than zero, marked `estimated` in the journal. A resample loop of killed runs would
otherwise hide from the runaway guard. See
[running-the-paid-layers.md](../docs/running-the-paid-layers.md).

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
