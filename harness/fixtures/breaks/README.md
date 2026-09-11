# Break overlays — issue #6

Not plugins. Each folder is an **overlay**: a sparse tree copied over a base fixture at run time
by `FixtureBuilder`. The base supplies `.claude-plugin/plugin.json` and every file the overlay
does not mention, so a break cannot drift from the fixture it breaks. An overlay file that
matches nothing in the base is a mistyped path and throws, because an overlay that silently
applies nothing would measure the good fixture while claiming to measure a break.

| Overlay | Base | Description | Body | Should redden |
|---|---|---|---|---|
| `description-catalogue/` | `shared/catalogue` | broken | stub, unchanged | layer 3 |
| `description-plugin/` | `fixtures/good` | broken | byte-identical to good | nothing |
| `body-plugin/` | `fixtures/good` | byte-identical to good | rule inverted | layer 4 |
| `control-catalogue/` | `shared/catalogue` | byte-identical to good | stub prose reworded | nothing |
| `control-plugin/` | `fixtures/good` | byte-identical to good | prose reworded, rule intact | nothing |

## Why five and not two

Two breaks, but each has to be shown at **both** layers or the separation claim is untested.
`description-plugin` carries the broken description with a working body, so layer 4 can be shown
not to catch a targeting fault. `body-plugin` doubles as a layer 3 overlay: its description is
byte-identical to the good one, so layer 3 must not move when only the body changed.

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
