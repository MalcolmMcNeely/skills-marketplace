# Skills marketplace

A Claude Code plugin marketplace for sharing agent skills across an engineering organisation: one catalogue, versioned in git, installed by policy rather than by asking.

## Status: design and research. The catalogue has two skills in it.

`plugins/core/skills/` holds `skill-authoring` and `new-skill`, which is enough to write the next one and enough for the quality gate to have something real to check. It is a start, not a catalogue.

What the repo mostly holds is the reasoning, and the reasoning is the useful part. Thirteen documents work out how a shared skill catalogue should be built, what it costs to run, and how to tell whether a skill change made anything better. Most of the numbers were measured on a real machine against Claude Code 2.1.248 rather than reasoned about. The test harness in `harness/` is the quality gate. Breaking it on purpose proved it catches a failure.

So: if you want a full catalogue, it is not here yet. If you want to work out how to build one for your own company, start reading.

## What the research found

Seven results that changed the plan.

- **Per-skill usage is countable today, and the delivery route decides what you get.** `claude_code.skill_activated` carries `skill.name`, and `OTEL_LOG_TOOL_DETAILS=1` unredacts it for a private catalogue. Measured. But shipping as a plugin permanently forfeits per-skill cost attribution: on the cost and token metrics a third-party plugin's skill name is replaced with `third-party`, and that code path has no escape hatch. Meanwhile no company anywhere publishes a per-skill invocation count, and nobody retires a skill for going unused.

- **The harness catches its own breaks, and tells them apart.** Break the description and layer 3 reddens at 25 of 60 while layer 4 holds at 5 of 5. Break the body and layer 4 reddens at 0 of 8 while layer 3 holds. Two controls that reworded prose around an unchanged rule moved nothing, and one of them replicated the 60 of 60 the next day. But a description only stops firing when its **subject** changes: stripping the trigger word still fired 3 of 3, and inverting the boundary still fired 2 of 3.
- **A perfect score must not set a perfect gate.** The calibration pass scored 60 of 60 on the good fixture. Building the gate on that demands a flawless run every time, so the gate comes from the interval's lower bound instead: 53 of 60. The same 133 runs also produced zero false fires across 50 negative prompts, and showed the model treats a C# record and an interface as a class.
- **What fires is decided by how the developer phrases the task**, not by how the catalogue is designed. A short, vague request fired almost no skills. The same request with "do the whole thing end to end" appended fired eleven of twelve. Under-firing is the common failure and the dangerous one, because nobody notices a skill that did not fire.
- **A negative boundary in the description is worth more than any other wording change.** End a description by naming the nearest technologies it excludes, and a broad request pulls a mean of 1.2 skills out of 12 instead of 6.0. Recall does not measurably suffer.
- **"Eval" is two machines, not one.** Does the skill fire when it should, and does it improve the outcome once fired. The first is cheap and applies to every model-invocable skill. The second is expensive and only pays where the skill specifies something observable.
- **Score runs, then pool them.** A skill firing two times in three passes a ten-query suite 5 per cent of the time under per-query scoring at three runs. Pooling the runs across the suite makes the verdict usable at a run count anyone can afford.

## The documents

Start with the plan, then follow a link when you want the working.

| Document | What it answers |
|---|---|
| [What we should build, and how](docs/recommendation.md) | The plan. Start here |
| [Findings](docs/findings.md) | What eight companies actually do, and what we verified |
| [Evals](docs/evals.md) | How to verify a skill change helped, and where "eval" is the wrong word |
| [Scoring](docs/scoring.md) | How a run becomes a verdict, and where the pass mark goes |
| [Targeting](docs/skill-targeting.md) | What fires when a task spans two technologies |
| [Baseline test-first](docs/baseline-test-first.md) | What Claude does with no skill installed, and which assertions discriminate |
| [Harness skeleton](docs/harness-skeleton.md) | The shape the harness takes, and three assumptions a real run disproved |
| [Running the paying layers](docs/running-the-paid-layers.md) | Where layers 3 and 4 run, why there is no CI, and why every dollar here is notional |
| [Before the calibration pass](docs/calibration-prep.md) | Pinning the model, what a throttled run counts as, and why the pass writes a journal |
| [The calibration pass](docs/calibration.md) | 133 runs against the good fixture: `p_good`, the gate, and why a perfect score must not set a perfect gate |
| [The two broken versions](docs/breakage.md) | Breaking the fixture on purpose: which layer notices, and the wall the harness could not see |
| [What layer 2 asserts](docs/layer-2.md) | The free gate: how skills reference each other, and what the budget test can and cannot prove |
| [Does `plugin validate` need a login in CI?](docs/ci-plugin-validate.md) | Whether layer 1 can gate a runner with no credential |
| [MCP skill delivery](docs/mcp-skill-delivery.md) | Can an MCP server install a skill, and should it |
| [MCP and plugins](docs/mcp-and-plugins.md) | What each carries, where they collide, and how an org ships both |
| [Output styles](docs/output-styles.md) | How to set one voice across a company, and what it costs |

## Automating the loop

A second line of research, in `docs/research/`. The catalogue documents above ask what a skill should say. These ask what should run the skills, and what stops it going wrong.

| Document | What it answers |
|---|---|
| [Agent patterns for the implement loop](docs/research/agent-patterns-for-the-implement-loop.md) | Orchestrator or daisy chain, and which mechanisms actually run today |
| [Hooks as guardrails](docs/research/hooks-as-guardrails.md) | Every hook event in 2.1.248, what each can block, and what hooks cannot do |
| [Ticket state as a guardrail](docs/research/ticket-state-guardrails.md) | Whether GitHub's blocking edges are machine-readable, and three traps in the way |
| [Steering how Claude writes code](docs/research/steering-code-style.md) | Why a style rule gets ignored, which layer can replace an instruction rather than add one, and the output style that makes comments worse |

## Knowing whether a skill earns its slot

A third line, also in `docs/research/`. Once the catalogue ships, which skills does anyone actually use, and how would you know a skill was worth writing.

| Document | What it answers |
|---|---|
| [What to measure, and how](docs/research/what-to-measure.md) | The decision. Two tables: what a machine can count, and what needs git, a survey or an eval. Start here |
| [What we can count](docs/research/skill-usage-telemetry.md) | Every mechanism that carries a skill name, measured on the wire, and the one variable that unlocks it |
| [Who measures it](docs/research/who-measures-skill-usage.md) | Twenty-seven companies against their own sources, and why the blanks are the finding |
| [Buy or build](docs/research/buying-skill-telemetry.md) | Thirteen products that count skills, and the one thing none of them does |

## Two skill locations, and they are not the same thing

This trips people up.

| Path | What it is | Ships to anyone? |
|---|---|---|
| `.claude/skills/` | Dev tooling. What Claude uses while we work in this repo | No |
| `plugins/*/skills/` | The catalogue. What developers across the company install | Yes |

`.claude/skills/` holds 26 vendored skills so `/grill-with-docs`, `/unslop` and the rest are available while building. Borrowed toolkit, not the product.

## Layout

```
skills-marketplace/
  .claude/skills/               dev tools, borrowed. Not shipped
  .claude-plugin/
    marketplace.json            catalogue of plugins
  .github/workflows/
    free-gate.yml               layers 1 and 2, on every push and pull request
  docs/                         the plan and the findings
  harness/                      the quality gate. Not shipped
    skills/                     one folder per skill under test. One so far
    shared/                     fixture material every suite borrows
  plugins/
    core/
      .claude-plugin/plugin.json
      skills/                   the catalogue. Two skills so far
```

`harness/skills/` sits beside `plugins/core/skills/` on purpose. One entry against two shipped skills is the coverage gap, visible in a directory listing before any test reports it.

## Use it locally

Claude Code reads `.claude/skills/` when you open this repo. Nothing to install.

To load the catalogue the way a developer would receive it:

```
/plugin marketplace add C:/Projects/skills-marketplace
/plugin install core@skills-marketplace
```

Validate the manifests:

```
claude plugin validate .
claude plugin validate ./plugins/core
```

Run the free half of the harness. No network, no model calls, about a second:

```
dotnet test harness/tests/Harness.Free.Tests
```

Expect 260 of 261. `skill-authoring` is model-invocable and ships with no suite, and layer 2 reddens on exactly that. The red is the assertion working, and [#30](https://github.com/MalcolmMcNeely/skills-marketplace/issues/30) tracks the suite that turns it green. A second failure is a real one.

`.github/workflows/free-gate.yml` runs those three on every push to `main` and every pull request. It pins the CLI to the version every gate value was measured against, and the runner image to the one `plugin validate` was proved to need no login on. The paying layers stay on a laptop, and the header of that file says why.

The paying half makes real model calls and refuses to run without an opt-in:

```
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/Harness.Model.Tests
```

## Where the dev skills came from

Vendored from [mattpocock/skills](https://github.com/mattpocock/skills) by way of `C:/Projects/podium`, which flattened the upstream bucket folders and dropped the `agents/openai.yaml` files. This is our copy, not a mirror. Edit it freely.

`unslop` came from [cursor/plugins](https://github.com/cursor/plugins), with its description rewritten so Claude Code can match it as a trigger.

`code-review` and `setup-matt-pocock-skills` read `docs/agents/*.md` from the repo root. Those are per-repo config that `/setup-matt-pocock-skills` writes. Run it here if you need them.

## Licence

MIT. See [LICENSE](LICENSE).

Both upstream sources are MIT too, so nothing constrains us. Their copyright notices are kept in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
