# Testing a skill we ship

Issue [#30](https://github.com/MalcolmMcNeely/skills-marketplace/issues/30). Measured on this
machine on 12 September 2026, against Claude Code 2.1.248 and `claude-opus-5[1m]`.

The harness had one suite and it tested a skill nobody ships. `csharp-new-class` is a fixture, built
to measure the harness itself. Meanwhile `skill-authoring` shipped model-invocable with nothing
checking it, and [layer 2](layer-2.md) had said so on every push since 8 September.

This is what it took to point the harness at the shipped file, and what the first runs said.

## A catalogue suite is not a fixture suite with the source changed

`"source": "catalogue"` reads the shipped `SKILL.md` from `plugins/` at run time and never copies it.
A copy drifts the moment the original is edited, so the coverage rule compares resolved paths rather
than names and a copy cannot answer for the real thing.

The suite folder ends up holding one file. No `plugin/`, because the plugin is the shipped one. No
`breaks/`, because a break overlay may only replace a file that already exists in the base it covers,
and the base for layer 3 is the distractor set, which has never heard of `skill-authoring`.

## The listing gap

Layer 3 decides from the listing. Twelve description-only distractors, a natural prompt, and the
question of which descriptions the model reaches for. The skill under test has to be in that listing
or the run cannot go right.

For the fixture suite that was true by accident. The distractor set carries a stub of
`csharp-new-class`, so the distractors and the skill under test arrived in the same folder and nobody
had to think about it. A catalogue skill is never copied, so nothing puts it among the distractors.
The first run of the new suite would have shown the model twelve skills unrelated to the prompt,
missed every time, and reported it as a description that will not fire.

This is the failure a fixture is good at hiding. The fixture had been right for a reason nobody wrote
down, and the first real skill walked straight into it.

Discovery works it out per suite now. The distractor set always loads, and the suite's own
plugin loads beside it whenever the distractors do not already declare that name. The question is
asked of the distractors rather than of the `"source"`, because a fixture suite whose skill nobody
stubbed has the same gap, and loading a plugin the distractors already speak for would put two skills
of one name in front of the model.

Five free tests hold it, three of them theories over every discovered suite, so a suite arriving with
the gap reddens the free gate in about a second rather than at the end of a paid pass. One of the
three is the rule read backwards: no name may appear twice in the listing a firing run loads, because
two skills of a name means the run was scored on whichever the CLI picked.

## What the first runs said

Layer 3's single positive case, `P1`, six valid runs each, graded on an exact set match. The listing
was thirteen entries for `skill-authoring`, the twelve distractors plus the shipped skill, and twelve
for `csharp-new-class`, whose stub is one of the twelve.

The cheap layer 3 test writes no journal. Only the long passes do, so this table is the record of
those twelve runs.

| Suite | Fired | Runs | Cost | Wall clock |
|---|---|---|---|---|
| `skill-authoring` | 6 of 6 | 6 | `$0.84` | 03:45 |
| `csharp-new-class` | 6 of 6 | 6 | `$1.39` | 04:28 |

Every run matched the set exactly, so nothing else in the listing came with it. The prompt was
*"I want to add a skill to the shared catalogue for our feature-flag process. Should it be an engine
or an entry point?"*

`skill-authoring` runs cheaper than the fixture, at a median of `$0.134` against `$0.227` over six
runs each. Its prompt asks how to write something rather than asking for a class to be written, so
the run ends sooner.

## What is still unmeasured

- **`p_good` is the untuned default of `0.67`.** The suite has had no calibration pass, so its gate
  is the one a suite starts on rather than one built from a measured lower bound.
  [calibration.md](calibration.md) has what a real pass produces, and why the bound sets the gate
  rather than the point estimate.
- **The 53-of-60 gate does not transfer.** It was measured against a listing of twelve stub skills
  and `csharp-new-class`'s prompts. `skill-authoring` competes against thirteen entries, one of them
  a real description with a real body, so its own gate has to come from its own pass.
- **Nine of the ten should-fire cases, all ten should-not-fire cases and all three watch cases have
  never run.** Layer 3's cheap test runs the first positive only. The rest cost a full pass.
- **Layer 4 has nothing to run.** The suite declares no contract case, so the body of
  `skill-authoring` is unchecked. Its rules are about text a developer writes next rather than files
  the run itself produces, so an assertion set for it is real work rather than a copy of
  `TestFirstFilesOnly`.
- **`new-skill` has no suite and needs none.** It carries `disable-model-invocation: true`, so a
  developer types it, it never enters the listing, and there is no firing decision to measure.
