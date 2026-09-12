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
| 2. Integrity, budget and coverage | Does every composition reference resolve? Is the catalogue inside the engine cap, and every description under 1024 characters? Does any skill reach an engine by slash form? Does every engine have a suite, and does every harness path the documents quote exist? | Free | Every push and pull request |
| 3. Firing accuracy | Given a natural prompt, does the right set of skills fire from the description alone? | About `$0.23` a run | On demand, locally |
| 4. Contract | Invoked by name, does the skill's body do what it promises? | About `$0.24` a run | On demand, locally |

Layers 3 and 4, both long passes and both screens run over every suite discovery returns. A skill
folder landing in `skills/` widens all of them by being there, with nothing to wire in. A free test
holds them to it, because a layer that narrowed back to one skill would stay green while it stopped
measuring the rest.

Each of them reports what a suite has not declared rather than failing on it. A suite with no
should-fire case, no contract case or no break overlays says so in the run output and the pass moves
on, so a suite that measures one half of a skill does not take the other suites down with it.

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

**This assertion was red until [#30](https://github.com/MalcolmMcNeely/skills-marketplace/issues/30).**
`skill-authoring` shipped with no suite for four days, and the gate said so on every push for
nothing, in about a second. That is what the rule is for: ship a model-invocable skill with no tests
and it reddens again the next time somebody pushes.

The catalogue's other skill, `new-skill`, is an entry point and stays exempt. A developer types it,
so there is no firing decision to measure and a firing case for it could never fail.

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
dotnet test harness/tests/free      # layers 1 and 2. Free. ~1 second.
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/model   # layers 3 and 4. Costs money.
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
| `src/` | Stream parsing, verdicts, pooling, the two run shapes |
| `tests/free/` | Layers 1 and 2, plus offline parser tests against captured streams |
| `tests/model/` | Layers 3 and 4. Real `claude -p` runs |
| `skills/<name>/` | One folder per skill under test. Everything that skill is tested on |
| `shared/distractors/` | Twelve description-only stubs for layer 3 |
| `shared/repo/` | The bare .NET repo from `docs/baseline-test-first.md` |
| `shared/streams/` | Real stream-json, so the parser tests need no model call |
| `tools/probe/` | One-shot debugging runner |

## What a suite folder holds

`SuiteDiscovery` scans `skills/` and returns one suite per folder. Nothing else decides which skills
are under test, no layer spells out a path inside one, and no layer names one. Adding a folder is
all there is to adding a skill. Every layer, both long passes and the screen widen to cover it, with
nothing to wire in.

`skills/csharp-new-class/` is the worked example. It carries all four parts, so read it alongside
this section. Copy its `suite.json` for the case shapes, but not its `"source"`: it is a `fixture`
suite testing a skill nobody ships, and a suite for a real skill needs `catalogue`. See
**Choosing a source** below. `skills/skill-authoring/` is the shorter example of a catalogue suite:
a `suite.json` and nothing else, because the skill it tests is read from `plugins/`.

| Path | Required | What |
|---|---|---|
| `suite.json` | Yes | The cases. Data |
| `plugin/` | Fixture suites only | The skill under test as a loadable plugin, for `--plugin-dir`. A `.claude-plugin/plugin.json` and `skills/<name>/SKILL.md` |
| `breaks/` | No | #6's break overlays, grouped by the break. Sparse trees laid over a base at run time |
| `runs/` | No | Journals and rendered reports. Written by the paid passes, not by hand |

The folder name is the suite name, and `suite.json` has to declare the same one. Discovery refuses a
disagreement rather than guessing, because the two disagreeing is a copy-paste.

### `suite.json`

| Field | Required | What |
|---|---|---|
| `suite` | Yes | The suite's name. Must equal the folder name |
| `skillUnderTest` | Yes | The `name:` the skill's frontmatter declares, not the folder somebody put it in |
| `source` | Yes | `fixture` or `catalogue`. No default. `SuiteFile.Load` refuses a suite that does not say |
| `pGood` | No | The per-run rate `Pooling.GateK` computes the gate from. Defaults to `0.67` |
| `firing` | No | Layer 3's cases: `shouldFire`, `shouldNotFire` and `watch` |
| `contract` | No | Layer 4's cases |

`firing` and `contract` are each optional, but a suite with no cases at all is refused. An empty
suite passes every layer by running nothing, which is the silent green this harness exists to stop.

Set `pGood` from the **lower bound** of a calibration pass's interval, never from its point estimate.
`csharp-new-class` carries `0.940` because #12 scored 60 of 60, and a gate built on that 1.000 would
demand a flawless run every time. The default of `0.67` is the untuned value a suite starts on before
anyone has paid for a calibration pass. [calibration.md](../docs/calibration.md) carries the working.

A `shouldFire` case takes an `id`, a `prompt` and an `expect` list, and is graded on an exact set
match. A `shouldNotFire` or `watch` case takes an `id`, a `prompt` and an optional `boundary` naming
what it is holding the skill back from. A `contract` case takes an `id`, a `task`, an `assertions`
name and any `assertionArgs` that name needs. Every case may set `runs` and `cap`; the defaults are 6
and 12 for a positive case and 5 and 10 for the rest.

Data in JSON, assertions in C#. `"assertions": "TestFirstFilesOnly"` names a class in
`AssertionCatalogue`. Ordering ("test written before class") cannot be expressed in JSON without
inventing a DSL, and a prompt list should not need a compiler to edit. A name that does not resolve
is refused at discovery, so a typo costs a second rather than a paid pass.

### Choosing a source

`"source"` decides where the skill under test is read from. The two values are not interchangeable.

- **`catalogue`** reads the shipped file from `plugins/` at run time and never copies it. A suite for
  a real skill wants this, and it is the only kind that satisfies the coverage rule above.
- **`fixture`** reads the skill from the suite's own `plugin/` folder, so a deliberately broken skill
  can never be mistaken for catalogue content. `csharp-new-class` is one. It tests a skill nobody
  ships, to measure the harness itself.

A fixture suite named after a shipped skill does not cover it. A copy drifts the moment the original
is edited, and then the suite tests text nobody ships.

### What a firing run puts in front of the model

Layer 3 decides from the **listing**, so the skill under test has to be in it. Discovery works that
out per suite. The distractor set always loads, and the suite's own plugin loads beside it
whenever the distractors do not already declare that name.

That was free while the only suite was a fixture one. The distractor set carries a
description-only stub of `csharp-new-class`, so the distractors and the skill under test arrived in
the same folder and nobody had to think about it. A catalogue skill is never copied, so nothing puts
it among the distractors. A run loading the distractors alone would show the model twelve skills
unrelated to the prompt, miss every time, and report it as a description that will not fire.

The question is asked of the distractors, not of the `"source"`. A fixture suite whose skill nobody
stubbed needs its plugin loaded for the same reason a catalogue suite does, and loading a plugin the
distractors already speak for would put two skills of one name in the listing.

## The exit-code trap, closed by construction

Scoring functions take a `ValidRun`. The only way to get one is `RunOutcome.TryGetValid`,
which applies the validity gate. There is no code path that scores an unchecked run.

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
