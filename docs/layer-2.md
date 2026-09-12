# What layer 2 asserts

Layer 2 is the free tier: referential integrity and budget, no model calls and no network. This is what
it checks and why, settled on [#5](https://github.com/MalcolmMcNeely/skills-marketplace/issues/5).

Date: 2026-09-07. Claude Code 2.1.248. Every number below was measured on this machine.

## What the repo actually contained

Before deciding anything, I counted.

| | Measured |
|---|---|
| Skills in `.claude/skills/` | 26 |
| Engines, meaning model-invocable | 12 |
| Entry points, carrying `disable-model-invocation: true` | 15 |
| Skills in `plugins/core/skills/` at the time | 0 |
| Longest description | 488 characters |
| All 12 engine descriptions together | 2,519 characters |

## Scope: the catalogue only

Layer 2 runs over `plugins/*/skills/`. It does not run over `.claude/skills/`.

Those two folders are different things. `.claude/skills/` is a vendored copy of someone else's toolkit
that we edit for our own use. A gate that reddens a pull request because an upstream phrasing changed is
noise, and the phrasing there is not ours to control.

The cost of that choice is real and worth naming: the catalogue was empty, so layer 2 had nothing to
check. Two things answer that.

- The catalogue is no longer empty. `skill-authoring` and `new-skill` are in it.
- The rules are unit-tested against the phrasings measured in the wild, and the catalogue sweep runs
  the same code. This is the pattern the stream parser already used: `ParserTests` feeds it captured
  strings rather than waiting for a live run.

`The_catalogue_is_not_empty` is now a test. Before #5, the description budget test looped over zero
files and passed green having checked nothing.

## The composition idiom is not one string

**The rule: every quoted string on a line mentioning the Skill tool is a reference, and every reference
must resolve to a catalogue skill.**

**Measured** across the 15 real examples in `.claude/skills/`: the idiom appears in **12 different
phrasings**. This rule finds **19 references on 14 of the 15 lines**, and all 19 resolve. A single regex
built on one phrasing found 2 of 11 and silently passed the rest.

The line it does not cover is in `handoff`:

> call the Skill tool for whichever skills the `## Notes` block names

There is no name there to resolve. A line with no quoted string produces no reference, so **the dynamic
case needs no exemption mechanism**. That is the point of extracting rather than matching: an
un-namable reference is absent, not wrong.

**Rejected: enforcing the one canonical phrasing.** `CLAUDE.md` mandates `Call the Skill tool with
"name"`, so a test could fail anything else. The measurement kills it. This is a real line from a real
skill:

> call the Skill tool twice, for "grilling" and "domain-modeling"

Two names in one instruction is a sentence people legitimately want to write. Banning it buys style
consistency at too high a price.

## A slash form is only wrong when it points at an engine

**The rule: `/name` fails only when `name` resolves to an engine.**

The hard part was telling a delegation instruction from prose describing a command a developer types.
**Measured** in `.claude/skills/`: **102 backticked slash forms across 28 distinct names**, and 13 of
those uses are not skills at all.

| Not a skill | Times |
|---|---:|
| `/compact` | 6 |
| `/clear` | 5 |
| `/tmp` | 1 |
| `/settings` | 1 |

A blanket grep fails on every one of them.

Resolving the name against the engine list settles it with no denylist to maintain:

- An **engine** is model-invoked. A developer never types one. So `/engine` in prose is always a mistake.
- An **entry point** exists to be typed. So `/entry-point` is correct.
- A built-in resolves to nothing and is ignored.

The same move solved both traps. Resolving names against the catalogue is what makes the rules simple.

## Two exclusions, and one of them cost a red build

Both rules skip:

- **Path segments.** A slash preceded by a word character, a dot, a dash or another slash is part of a
  path. Without this, `plugins/core/skills/skill-authoring/SKILL.md` reads as a reference to the
  `skill-authoring` engine.
- **Fenced code blocks.** An example is not an instruction.

The fence rule was not a prediction. The gate went red the first time it swept a real catalogue, on
`new-skill`, which documented the idiom with a placeholder:

```
Call the Skill tool with "name"
```

`name` is not a skill. A skill that teaches the idiom has to be able to show it. So the house rule both
checks share is: **examples go in fences, instructions go in prose.**

That failure is the most useful thing to come out of this ticket. Layer 2 caught a real defect in a real
file within seconds of first meeting one.

## The budget

**The rule: at most 12 engines, and every description under 1,024 characters.**

Both are exact and countable. Neither needs an estimate.

**A discrepancy worth recording.** The repo carries two description limits from two sources, and they
are not in conflict:

| Limit | Source |
|---|---|
| 1,024 | agentskills.io specification hard limit |
| 1,536 | Claude Code's `skillListingMaxDescChars` default, in [findings.md](findings.md) |

1,024 is tighter, so it binds.

**Rejected: a derived character total.** The listing budget is 1% of the context window, which is a
fraction of tokens. Asserting against it means picking a window size and a characters-per-token ratio,
neither of which we control. Two guesses do not make a gate.

**What that leaves unproven, stated rather than hidden.** Twelve engines at 1,024 characters each is
about 12,300 characters, roughly 3,000 tokens. A 200k window gives the listing about 2,000. So the
rules bound the catalogue but **do not prove it fits**. Today's 12 engines are 2,519 characters, about
630 tokens, so there is a lot of room. The gap only matters if descriptions grow toward the cap.

## Engines do not need callers

**The rule: none. Layer 2 checks one direction only.**

**Measured**: 5 distinct skills are called via the Skill tool in `.claude/skills/`. There are 12
engines. So **7 of 12 engines have no caller**, and every one is healthy. An engine fires on its own
description. That is what "engine" means.

Requiring a caller would need an exemption list longer than the rule. When a check needs more
exceptions than cases, the check is wrong.

## What layer 2 asserts, in full

| Assertion | Kind |
|---|---|
| The catalogue is not empty | Guard against a vacuous pass |
| Every skill declares a name and a single-line description | Integrity |
| Every folder name matches the skill it declares | Integrity |
| Every referenced skill exists | Integrity |
| No skill uses a slash form to reach an engine | Integrity |
| At most 12 engines | Budget |
| Every description under 1,024 characters | Budget |
| No fixture skill shares a name with a shipped skill | Isolation, from [#8](https://github.com/MalcolmMcNeely/skills-marketplace/issues/8) |
| `marketplace.json` never mentions the harness | Isolation, from #8 |
| Every engine has a suite folder under `harness/skills/` | Coverage, from [#28](https://github.com/MalcolmMcNeely/skills-marketplace/issues/28) |
| Every harness path a document quotes is on disk | Coverage, from [#29](https://github.com/MalcolmMcNeely/skills-marketplace/issues/29) |

81 tests, no network, no model calls, under half a second, measured on this machine on 2026-09-07.
The last two rows arrived later: 272 tests in about a second, measured on this machine on
2026-09-12 against Claude Code 2.1.248.

### The coverage row was red for four days, and that is the assertion working

`skill-authoring` is model-invocable and shipped with no suite, so from 2026-09-08 the run was
**260 of 261**. Nothing else in the repo noticed that, because every other rule here reads the
catalogue alone and every discovery rule reads the suites alone. This one reads both.

Writing the missing suite cost model runs, which is why it was separate work on
[#30](https://github.com/MalcolmMcNeely/skills-marketplace/issues/30) rather than part of the
assertion. The red was the reminder for as long as it took, and the rule has not changed: ship a
model-invocable skill with nothing measuring it and the free gate says so on the next push, for
nothing, in about a second.

`new-skill`, the catalogue's other skill, stays exempt. It carries `disable-model-invocation: true`,
so a developer types it and there is no firing decision to measure.

### Why the gate reads documents at all

[#23](https://github.com/MalcolmMcNeely/skills-marketplace/issues/23) moved four top-level harness
folders into one per skill, and six documents quoted the old names. Nothing went red, because no rule
here read prose. A document pointing at a folder that is not there is worse than no document: it
reads as current, and the reader loses the time before working out that it is not.

The sweep reads every Markdown file in the repo except those under `.claude/`. That is the same
exclusion this document argues for above. Vendored tooling is somebody else's prose, and a gate that
reddens on an upstream phrasing is one people learn to ignore.

A `<name>` placeholder ends a match, so the document explaining the suite folder shape is checked
only as far as the shape it describes.

**A suite's `runs/` folder is exempt, and this is the subtle one.** A paid pass renders a report
naming the folders it read. A report written before a layout change therefore describes the layout
of its own day, correctly, for good. Sweeping those would redden the free gate for a record being
accurate about history, and the only way back to green would be to falsify the record. The
breakage report of 9 September 2026 already names the top-level captured folder that
[#23](https://github.com/MalcolmMcNeely/skills-marketplace/issues/23) deleted, and it is right to.

That leaves one cost worth naming, because this document paid it. A document explaining the rule
cannot quote a dead path as an example, since the sweep reads prose and fences alike and cannot
tell an example from a claim. Describe the dead folder instead of typing it. The alternative is an
escape hatch, and an escape hatch in a gate is a hole.

```
dotnet test harness/tests/free
```

## What we could not verify

- **Whether the rules hold at catalogue scale.** They were measured against 26 vendored skills and
  proven against 2 shipped ones plus purpose-built broken fixtures. A catalogue of 12 engines and fifty
  entry points has not existed yet.
- **Whether the engine descriptions fit the listing budget.** See above. The rules bound the count and
  the length. Nothing here measures tokens, and `/doctor` reporting the real cost was not run.
- **Whether 12 is the right cap.** `CLAUDE.md` says "about 12" and the test now says at most 12. That
  tightening is a decision, not a measurement. [skill-targeting.md](skill-targeting.md) tested at
  twelve because that was the cap, so the cap has never been tested against an alternative.
