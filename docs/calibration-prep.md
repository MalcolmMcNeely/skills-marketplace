# Before the calibration pass

What #12's three preconditions turned out to be, and what building them found.

Date: 2026-09-07. Claude Code 2.1.248. Everything marked **measured** was run on this machine.

## Why any of this exists

[#12](https://github.com/MalcolmMcNeely/skills-marketplace/issues/12) runs [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10)'s
23-case suite against the frozen good fixture and produces `p_good`, the number every gate value on
the map is derived from. That is 125 runs and roughly 83 minutes of continuous calling.

The comment on #12 listed three things to settle first. All three are about the same risk: an 83-minute
pass has one shot, and a pass that produces a wrong number is worse than one that produces none.

## 1. The model is pinned

`RunSpec.Model` was null, so every run took whatever the CLI defaulted to. Every figure on this map
says `claude-opus-5[1m]` and nothing held it there.

`RunSpec.Model` now defaults to `RunEnvironment.ResolveModel()`, not null, so a run cannot go out
unpinned by omission. `SKILL_HARNESS_MODEL` overrides it when the point is to measure drift.

**Measured.** `claude -p --model 'claude-opus-5[1m]'` is accepted, and the `init` line reports
`"model":"claude-opus-5[1m]"` back verbatim. So the pin is checkable, not just requested:
`RunOutcome.ModelHeld` compares what the run asked for against what the session resolved to, the
journal records both, and the calibration test fails if any run drifted.

Two things to know about the same stream:

| Where | What it says |
|---|---|
| `init` line, `model` | `claude-opus-5[1m]`, the pinned id |
| `assistant` line, `message.model` | `claude-opus-5`, without the suffix |

`StreamParser` reads the model from the `init` line only, which is the one that answers "what did this
session resolve to".

**Measured, and worth knowing separately:** field order in the terminal `result` line is not fixed.
The captured fixture has `"type":"result","subtype":"success"` adjacent; a fresh run puts
`total_cost_usd` first and `subtype` near the end. Nothing may grep for two fields being next to each
other. `StreamParser` parses the JSON, so it is unaffected.

## 2. A throttled run is void, is never resampled, and stops the pass

The harness voids any run that does not exit 0 with `"subtype":"success"`, and resamples it. A usage
limit produces that shape. Left alone, a limit would burn the resample cap and the pass would report
`insufficient-firings`, which is a layer 3 result: the skill would be blamed for the machine refusing
to answer.

The decision:

- **Void** is right. The harness did break the run, and it carries no signal about the skill.
- **Never resampled** is right. A usage limit does not lift between attempts. Retrying spends the cap
  to reach the same wall, then labels the wall as a skill that would not fire.
- **Stops the pass** is right. The journal holds every completed run, so a stopped pass is resumable.
  A mislabelled one is not.

`Sample.Failure` checks `throttled` before `suite-budget-exhausted` and `insufficient-firings`, so a
limit that also empties the ledger still reads as a limit.

**Not measured.** A usage limit cannot be provoked on demand, so the markers in `Throttle.Markers` are
read from the shapes the CLI is known to emit, not observed here. Detection is narrow in two ways, so
that being wrong about the markers is survivable:

1. It only looks at a run that already failed the validity gate, so it can never turn a real result
   into a throttle.
2. It only reads the CLI's own words, the `result` line's text and stderr. It never reads the
   transcript, because the model writes arbitrary text and `429` appears in ordinary code. Matching
   the whole stream would let the model throttle its own pass.

A false positive can therefore stop a healthy pass early, which the journal makes cheap. A false
negative leaves the old behaviour, which is what happens today.

One more thing came out of looking. The old code redirected stderr and never read it, which fills the
pipe and deadlocks a chatty run. It now drains on its own task.

## 3. The journal, so a pass cut short keeps what it got

#11 set generous ceilings so a pass would not die at run 110 of 125 with no `p_good` to show. No
ceiling prevents a usage limit. The journal does.

Every completed run is appended to ndjson and flushed before the next one starts: verdict, cost,
duration, model asked for, model resolved to, CLI version, the fired set, and every `Skill` call with
its ordinal. A pass that buffers and writes at the end loses everything to exactly the failure it is
meant to survive.

Two things fall out of writing per run:

- **The pass is resumable.** Point `SKILL_HARNESS_JOURNAL` at an existing journal and any case with
  enough valid runs on disk is skipped.
- **The report is a pure function of the journal.** A pass stopped at run 110 still reports on 110,
  with a header saying it did not finish and a note that the intervals are wider than planned.

**Found by a test, not by reasoning.** On Windows a plain `StreamWriter` locks the file, and the
resume path reads the journal while the pass that owns it is still open. A resumed pass would have
thrown on its first case. Both ends now open with `FileShare.ReadWrite`.

## What else the build turned up

`FiredSkills` deduplicates and the new `Transcript.SkillCalls` does not. **Measured** against the
existing `two-skills-fired.jsonl` capture: three `Skill` calls, two distinct skills, the third a repeat
of the first.

That gap is the whole of #12 output 6. #11 parked a question here: the `FirstDecision` stop rule kills
a run at the first `Skill` call, which is safe for negatives only if `csharp-new-class` never fires
after another skill already has. Answering it needs positions, and a deduplicated set does not have
them.

## What we could not verify

- **Whether a real usage limit matches any marker.** See above. The path is unexercised and will stay
  that way until a pass meets one.
- **Whether `p_good` survives a model change.** The pin makes drift visible, not absent. Nothing here
  says how far the number moves across models or CLI versions, and every gate value moves with it.
- **What a negative run costs.** Still unmeasured. The `$0.196` median came from positive prompts, and
  a negative prompt asks the model to build a Vue component or a Python class. The report keeps the two
  medians apart so the pass answers this.

## Where the code is

| Thing | File |
|---|---|
| Model pin and CLI version | `harness/src/RunEnvironment.cs` |
| Throttle detection | `harness/src/Throttle.cs` |
| The journal | `harness/src/RunJournal.cs` |
| The pass and its seven outputs | `harness/src/Calibration.cs` |
| The report | `harness/src/CalibrationMarkdown.cs` |
| Guards for all of it | `harness/tests/free/CalibrationPrepTests.cs` |

The free suite is 51 tests, no network, about a second.

To run the pass:

```
SKILL_HARNESS_LIVE=1 SKILL_HARNESS_CALIBRATE=1 dotnet test harness/tests/model
```

To resume one a limit cut short, add `SKILL_HARNESS_JOURNAL=<path>` from the run that stopped.
