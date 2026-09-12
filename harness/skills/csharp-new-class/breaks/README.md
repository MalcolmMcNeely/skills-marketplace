# Break overlays, issue #6

Not plugins. Each leaf folder is an **overlay**: a sparse tree copied over a base fixture at run time
by `FixtureBuilder`. The base supplies `.claude-plugin/plugin.json` and every file the overlay
does not mention, so a break cannot drift from the fixture it breaks. An overlay file that
matches nothing in the base is a mistyped path and throws, because an overlay that silently
applies nothing would measure the good fixture while claiming to measure a break.

Grouped by the **break**, not by the base. One folder per break, holding one folder per layer shape
it is measured at. Two folders called `description-catalogue` and `description-plugin` read as two
breaks; `description/distractors` and `description/plugin` read as what they are.

| Overlay | Base | Description | Body | Should redden |
|---|---|---|---|---|
| `description/distractors/` | `shared/distractors` | broken | stub, unchanged | layer 3 |
| `description/plugin/` | the suite's `plugin/` | broken | byte-identical to good | nothing |
| `body/plugin/` | the suite's `plugin/` | byte-identical to good | rule inverted | layer 4 |
| `control/distractors/` | `shared/distractors` | byte-identical to good | stub prose reworded | nothing |
| `control/plugin/` | the suite's `plugin/` | byte-identical to good | prose reworded, rule intact | nothing |

`candidates/` holds the three descriptions #6's nine-run screen chose between. They are not arms.

## Why five and not two

Two breaks, but each has to be shown at **both** layers or the separation claim is untested. The two
shapes of the description break are not copies of each other. Layer 3 never reads a body, so the
distractor shape stays a description-only stub; layer 4 reads nothing else, so the plugin shape
carries the full working body. That is what lets layer 4 be shown not to catch a targeting fault.

The body break needs one shape only. Its description is byte-identical to the good one, so the same
overlay serves layer 3, where it proves layer 3 does not move when only the body changed.

## The break in each

**Description.** The trigger is removed, not the meaning: `Use when adding a new C# class...`
becomes a noun-phrase label naming the same subject. `docs/skill-targeting.md` measured a failing
run as firing **nothing** rather than the wrong thing, so expect silence, not a decoy.

**Body.** A soft break, per #4. The rule is not deleted, it is reversed and still argued for:
the class comes first and `dotnet test` is now required. Both files are still asked for, and the
test file still needs a `[Fact]`, so guards A3, A4 and A5 keep passing and only the signal
assertions A6 and A7 fall over. A break that took the guards down with it would prove nothing.

## The controls

A change that should move nothing. Without one, a harness that reddens on any edit at all looks
sensitive when it is only noisy. Both controls keep the description byte-identical and rewrite
prose around an unchanged rule, which is what most real skill edits are.
