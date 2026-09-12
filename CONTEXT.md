# Skills marketplace

A catalogue of agent skills shared across an engineering organisation, and a harness that measures
whether each one fires when it should and does what it promises.

These are the words this repo uses. Where two words exist for one thing, the word here is the one to
use and the rest sit under _Avoid_. A word earns an entry by appearing in prose a human reads. Names
that live only in the C# stay out.

## The catalogue

**Catalogue**:
The skills we ship to developers, under `plugins/core/skills/`. One sense only. The twelve stubs a
firing run loads are the distractor set, not a catalogue.
_Avoid_: library, registry, skill pack

**Engine**:
A skill the model may choose on its own. Every engine spends listing context, so we cap a catalogue
at about twelve.
_Avoid_: model-invocable skill, automatic skill

**Entry point**:
A skill carrying `disable-model-invocation: true`. A developer types its name, the model never
chooses it, and it spends no listing context.
_Avoid_: command, manual skill, slash skill

**Description**:
The `description:` line in a skill's frontmatter. It is the only part of a skill the model reads when
deciding whether that skill fires.
_Avoid_: summary, blurb

**Boundary**:
The closing clause of a description, naming the nearest technologies it excludes. A should-not-fire
case carries one as well, for what it holds the skill back from.
_Avoid_: exclusion, negative example

**Composition**:
One skill reaching another, written in prose as `Call the Skill tool with "name"` and never as a
slash form.
_Avoid_: chaining, delegation, hand-off

**Listing**:
The skill names and descriptions in front of the model at the moment it decides. Nothing else decides
a firing run.
_Avoid_: manifest, index, available skills

**Firing**:
The model choosing a skill from the listing.
_Avoid_: triggering, activating, matching

## Measuring a skill

**Harness**:
The repo's quality gate, under `harness/`. We never ship it, and nothing inside it is a real skill.
_Avoid_: test rig, eval framework

**Layer**:
One of four checks, numbered cheapest first. Layers 1 and 2 are free. Layers 3 and 4 call a real
model and cost money.
_Avoid_: stage, tier, phase

**Suite**:
Everything one skill is tested on, in one folder under `harness/skills/`. Discovery finds suites by
scanning, so a folder is all there is to adding a skill.
_Avoid_: test project, spec, case file

**Source**:
The `suite.json` field that says where a run reads the skill under test from. `catalogue` reads the
shipped file and is the only kind that covers an engine. `fixture` reads the suite's own copy.
_Avoid_: origin, kind, mode

**Fixture**:
A copy of a skill nobody ships, written to measure the harness itself. It lives in its suite's
`plugin/` folder, so nobody can mistake it for shipped content.
_Avoid_: mock skill, dummy skill, sample skill

**Distractor**:
One of the description-only stubs a firing run loads so the model has something to choose between.
The twelve together are the **distractor set**, in `harness/shared/distractors/`.
_Avoid_: stub catalogue, distractor catalogue, fixture catalogue

**Bare repo**:
The empty C# solution a contract run writes into, in `harness/shared/repo/`. Each run gets its own
copy, so nothing a run writes survives it.
_Avoid_: fixture repo, scratch repo

**Coverage**:
The rule that every engine in the catalogue has a catalogue suite measuring it. An entry point is
exempt, because a developer types it and there is no firing decision to measure.
_Avoid_: test coverage, code coverage

**Case**:
One prompt and the number of runs it gets. A *should-fire* case names the skills it expects, a
*should-not-fire* case names the boundary it holds a skill back from, and a *watch* case is a prompt
whose right answer nobody has settled.
_Avoid_: test, scenario, query

**Contract case**:
A case that invokes a skill by name and checks the work against a named assertion.
_Avoid_: behaviour test, body test

**Run**:
One `claude -p` call. No verdict rests on one.
_Avoid_: invocation, trial

**Valid run**:
A run that finished and reported its own cost. Scoring refuses anything else.
_Avoid_: successful run, clean run

**Pool**:
Adding a suite's runs together to reach one verdict, instead of scoring each prompt on its own.
_Avoid_: aggregate, average

**Gate**:
The pooled score below which a layer is red. It comes from the lower bound of a calibration pass's
interval, never from the pass's own score.
_Avoid_: threshold, bar

**Verdict**:
What a pooled set of runs says. Green or red.
_Avoid_: result, outcome

**Pass**:
One long measurement run over every suite, costing money and hours. Calibration and breakage are the
two.
_Avoid_: sweep, campaign, batch

**Break**:
A fault put into a skill on purpose, to prove a layer notices it.
_Avoid_: mutation, regression, bug

**Overlay**:
A sparse tree of files laid over a base at run time. An overlay may only replace a file the base
already holds, and a path matching nothing throws.
_Avoid_: patch, diff, variant

**Arm**:
One broken version a breakage pass measures. It runs at both layers, so a red result names which half
of the skill broke.
_Avoid_: variant, branch

**Screen**:
A short cheap pass that chooses between candidates before a full pass pays for the winner.
_Avoid_: pilot, pre-test

**Comment density**:
The share of lines in the C# a run wrote that are whole-line comments. The unit the comment-steering
research measures in.
_Avoid_: comment ratio, verbosity
