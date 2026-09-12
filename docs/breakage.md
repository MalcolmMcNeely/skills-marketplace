# The two broken versions

[#6](https://github.com/MalcolmMcNeely/skills-marketplace/issues/6). Measured on this machine on
9 September 2026, Claude Code 2.1.248, all 256 runs pinned to `claude-opus-5[1m]` with no drift.

The harness had never seen a failure. [#12](https://github.com/MalcolmMcNeely/skills-marketplace/issues/12)
scored the good fixture 60 of 60 and set the gate at 53, but a pass that clean is what a good fixture
should produce and also what an easy one would. This ticket breaks the fixture on purpose.

**It works.** Each break reddens one layer and not the other, the failures name themselves, and both
controls stayed green. The control's 60 of 60 also replicates #12 the day before, so `p_good` of
1.000 and the gate of 53 are now measured twice.

| Arm | What changed | Layer 3 | Layer 4 |
|---|---|---|---|
| `description` | description broken, body correct | **RED** 25/60, gate 53 | green 5/5 held |
| `body` | body broken, description correct | green 6/6 | **RED** 0/8 held |
| `control` | prose reworded, both rules intact | green 60/60 | green 5/5 held |
| `good` | the frozen fixture, unchanged | not run, see #12 | green 5/5 held |

The pass also hit a real usage wall and could not see it, which cost two arms and is the most useful
thing in this document. It is [section 6](#6-the-wall-the-harness-could-not-see).

## 1. What a break is here

Not a second copy of the fixture. Each break is an **overlay**: a sparse tree of files laid over a
base fixture in scratch at run time. The base supplies the manifest and every file the overlay does
not mention.

A duplicated fixture drifts the moment the original is edited, and a drifted fixture measures two
changes while reporting one. An overlay cannot drift. It has one failure mode of its own, and it is
the dangerous one: a mistyped path applies nothing, and the pass then measures the **good** fixture
while reporting it under a break's name. So an overlay file that matches nothing in its base throws,
and an overlay that applies no files throws.

Five overlays, because each break has to be shown at **both** layers or the separation claim is
untested.

| Overlay | Base | Description | Body |
|---|---|---|---|
| `description/distractors` | `shared/distractors` | broken | stub, unchanged |
| `description/plugin` | the suite's `plugin/` | broken | byte-identical to good |
| `body/plugin` | the suite's `plugin/` | byte-identical to good | rule reversed |
| `control/distractors` | `shared/distractors` | byte-identical to good | stub prose reworded |
| `control/plugin` | the suite's `plugin/` | byte-identical to good | prose reworded, rule intact |

`body/plugin` doubles as a layer 3 overlay. Its description is byte-identical to the good one, so
layer 3 must not move when only the body changed.

## 2. A description only stops firing when its subject changes

The first description break was not a break at all.

The good description reads `Use when adding a new C# class to this project. Not for changing
existing classes, test files, or non-C# code.` The obvious edit strips the trigger and leaves a
noun-phrase label, which is what a tidying pass produces. It fired three times out of three.

So the break itself needed choosing, and a 60-run pass is the wrong instrument for that. Nine runs
on P1, three per candidate, separated them for $2.21.

| Candidate | Description after the edit | Fired |
|---|---|---:|
| Trigger removed, subject kept | `Conventions for the C# class authoring workflow used by the platform team.` | 3/3 |
| Boundary inverted | `Use when changing an existing C# class in this project. Not for adding new classes, test files, or non-C# code.` | 2/3 |
| Subject replaced | `Use when editing the analyser rule set that the build pipeline enforces. Not for writing application code, tests, or project files.` | **0/3** |

Removing the trigger did nothing. Inverting the boundary is the most realistic careless edit of the
three, and it points the skill at the opposite half of its own space, yet firing only fell to 2 of 3.
That sits inside the ambient miss rate and would not be visible without a full pass.

That is the opposite of what I expected. [Targeting](skill-targeting.md)
measured that a negative boundary is worth more than any other wording change, but that result is
about **over**-firing: a boundary keeps a skill out of a broad request. Nothing in it says the
boundary holds up the skill's own targeting. These nine runs say it does not.

All three candidates sit in `harness/skills/csharp-new-class/breaks/candidates/`. The third is
the arm, because #6 asks for a description that stops the skill firing and only the third one does.

### The break shows as silence

Across the 60 valid runs of the broken arm:

| Outcome | Runs |
|---|---:|
| Fired anyway | 25 |
| Fired nothing | 34 |
| Fired something else | 1 |

34 of the 35 failures are silence, which is what [targeting](skill-targeting.md) predicted. The one
decoy reached for `csharp-new-test`, the nearest neighbour in the catalogue.

**Write assertions on a broken description for silence.** A check that looks for the wrong skill
firing would have caught 1 failure in 35.

## 3. The body break is soft, and the guards are what make it mean anything

[#4](https://github.com/MalcolmMcNeely/skills-marketplace/issues/4) asked for a 150 to 250 word body
so #6 could try a soft break rather than a deleted rule. The rule is not deleted. It is reversed, and
still argued for in the original's voice.

| | Good | Broken |
|---|---|---|
| Ordering | **Write the test file first.** | **Start with the class.** ... so the shape of the type is settled before anything depends on it |
| Test run | **Do not run the tests.** ... Never invoke `dotnet test` | **Check it builds before you stop.** Run `dotnet test` once both files are in place |

The broken body still asks for both files, and still wants a `[Fact]` in the test. That is what
keeps the guards up, and it is the whole design.

| Arm | Guards A3, A4, A5 | Signals A6, A7 |
|---|---|---|
| `description` | 5/5, 5/5, 5/5 | 5/5, 5/5 |
| `body` | 8/8, 8/8, 8/8 | **0/8, 0/8** |
| `control` | 5/5, 5/5, 5/5 | 5/5, 5/5 |
| `good` | 5/5, 5/5, 5/5 | 5/5, 5/5 |

All 8 broken runs wrote both files, put a `[Fact]` in the test, wrote them in the wrong order, and
ran `dotnet test`. A break that took the guards down with it would prove nothing. The run
would have done no work for the signals to judge, and A6 and A7 would fail vacuously.

## 4. The two failures are distinguishable

This was the design flaw worth finding, and it is not there.

| | Layer 3 | Layer 4 |
|---|---|---|
| `description` | **RED** | green |
| `body` | green | **RED** |

Neither break reddens both layers, and each reddens a different one. A targeting fault and a
behaviour fault are separable in the output.

The layer names alone would be a weak claim, so the journal now records **which assertion** fell over
rather than only that one did. `A6 0/8, A7 0/8` with the guards at `8/8` says the skill wrote both
files and then ignored its own ordering rule. "Score went down" would not have said that.

## 5. The controls stayed quiet, and one of them replicates #12

A change that should move nothing. Without one, a harness that reddens on any edit at all looks
sensitive when it is only noisy.

Both controls keep the description **byte-identical** and rewrite prose around an unchanged rule,
which is what most real skill edits are. Both stayed green.

The layer 3 control is the more valuable of the two. Its description is the good one, so its 60 runs
are a same-session re-measurement of #12's number:

| | #12, 8 September | #6 control, 9 September |
|---|---|---|
| Pooled | 60/60 | 60/60 |
| Per case | 6/6 on all ten | 6/6 on all ten |
| `p_good` | 1.000 | 1.000 |

Seven days apart, on the same CLI version and the same pinned model. `p_good` of 1.000 and the gate
of 53 are no longer a single afternoon's result. Zero model drift across all 256 runs.

## 6. The wall the harness could not see

At 11:21 UTC the machine stopped answering. Every run after that returned in **1.7 seconds** having
billed **$0.00**, and it lasted the rest of the pass. It had lifted by the time I resumed.

The harness retried into it **89 times** and reported it as `insufficient-firings`, which reads as
"the skill did not fire". It cost two arms.

[Before the calibration pass](calibration-prep.md) decided this exact case: a throttled run is void,
is never resampled, and stops the pass. That document also said the path had never run. It has now,
and it failed, for two reasons.

**The guard read the wrong field.** The wall reported exit code 1 with terminal subtype `"success"`.
Detection opened with `if (terminalSubtype == "success") return false;` and returned before reading a
word of stderr. But validity is exit code **and** subtype together, so a run can carry a success
subtype and still be no result at all. Detection now keys on the validity gate.

**The markers were guesses.** `calibration-prep.md` said so plainly. I took the words from shapes the
CLI is known to emit, and never observed one here. This wall named itself nowhere, so no marker list would
have caught it.

So the fix cannot depend on words. It depends on shape. A run that failed its gate, billed nothing
and returned in under five seconds did no work, and no number of retries changes that.

| | Refused run | Real run |
|---|---|---|
| Duration | 1.6 to 1.8 s | 35 to 65 s |
| Cost | $0.00 | $0.14 to $0.40 |

Five seconds sits between 1.8 and 35, so the threshold has room on both sides. This never catches a
budget abort, because a budget abort billed for the work it did before the cut-off.

The 89 wasted runs cost nothing, because they never reached the model. They cost two arms and an
hour instead. That is the argument for stopping rather than resampling.

## 7. The $0.40 layer 3 cap is too tight for P7, measured three times

Every budget abort in this pass was a layer 3 run hitting `--max-budget-usd`.

| Case | Aborts |
|---|---:|
| P7 | 15 |
| P2 | 1 |
| P6 | 1 |

P7's valid runs cost between $0.296 and $0.397 against a cap of $0.400. P7 is not an outlier. It is a prompt that
sits on the cap, so whether any given run finishes is close to a coin toss. #12 saw the
same thing, and [#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11) set the cap
there on purpose after measuring $0.20 as too tight.

I left the cap alone again. Moving it mid-pass would have made the description arm and the control
arm non-comparable, and it changed no verdict here.

**Raised on 9 September 2026.** Three measurements of the same clipping were enough, so
[#18](https://github.com/MalcolmMcNeely/skills-marketplace/issues/18) moved layer 3 to $0.60, the
per-run ceiling #11 fixed for every other run shape. The harness now has one per-run number, and a
test reddens the build if it splits in two again.

## 8. What it cost

| | |
|---|---|
| Runs | 256, of which 107 void |
| Wall clock | 03:19:34 across two sittings |
| Total | $46.99 |
| Median, layer 3 valid run | $0.226 to $0.305 by arm |
| Median, layer 4 valid run | $0.244 |

The nine-run screen added $2.21, and I ran the two-run smoke test twice for $0.99. The whole ticket
cost **$50.19**.

The description arm is the expensive one at a $0.305 median, because a broken description makes the
model look around the repository before giving up. The model reads a working description and obeys
it.

**These figures are notional.** The pass ran on a subscription, where no cash moves and the runs draw
on usage limits instead. See [running the paying layers](running-the-paid-layers.md).

## What we could not verify

- **Whether a subtler description break is catchable.** Only the subject-replacement break ran at full
  width. The boundary inversion measured 2 of 3 on one prompt, which projects to roughly 40 of
  60 and would redden the gate, but that is an extrapolation from three runs and not a measurement.
  It is the more realistic edit of the two and deserves its own arm.
- **Whether the refusal shape holds against a different wall.** One wall, one shape, 89 samples of
  it. Another outage might bill a fraction of a cent or take six seconds, and the five-second ceiling
  is drawn from this event alone.
- **Whether the harness catches a break it was not designed around.** Both breaks here were written
  against the assertions that judge them. A break invented by someone who had not read
  `TestFirstFilesOnly` is a harder test, and I did not run one.
- **Whether layer 3 would catch a body break at full width.** Six runs showed a non-event. I did not spend sixty to
  confirm one.
