# The calibration pass

`p_good`, the gate derived from it, and what 133 runs against the frozen good fixture found. Settled on
[#12](https://github.com/MalcolmMcNeely/skills-marketplace/issues/12).

Date: 2026-09-08. Claude Code 2.1.248, model `claude-opus-5[1m]`. Every number here was measured on this
machine. The raw journal is `harness/captured/calibration-run-1.jsonl`, one line per run.

## What the pass was

[#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10)'s 23-case suite, run serially
against the frozen `csharp-new-class` fixture. Ten should-fire cases at six valid runs each, ten
should-not-fire cases at five, and three watch-list cases at five.

| | Measured |
|---|---|
| Runs journalled | 133 |
| Wall clock | 02:08:14 |
| Reported cost | $30.010 |
| Runs on the wrong model | 0 |
| Runs detected as throttled | 0 |
| Void runs | 8 |

The estimate on the ticket was 125 runs at about $24.50 in roughly 83 minutes. The pass took 133 runs,
$30.01 and 128 minutes. The extra eight runs are resamples for the eight Void ones, and the extra time
is those resamples plus a slower median than [harness-skeleton.md](harness-skeleton.md) recorded.

## 1. `p_good`, pooled

**60 of 60** valid should-fire runs matched their expected set.

| | |
|---|---|
| `p_good` | 1.000 |
| Wilson 95% interval | 0.940 to 1.000 |

The map carried **0.67**, taken from one 15-run measurement on a different pair of stub skills in
[skill-targeting.md](skill-targeting.md). That number is now superseded for this fixture.

The jump is large enough to be worth explaining. 0.67 was measured on a broad, vague prompt against
twelve competing stub skills. This pass runs ten prompts written to name the work plainly, against a
catalogue built for the test. Both numbers are real. They measure different things, and the map was
wrong to treat the first as general.

## 2. Per-case should-fire rates

Every case scored 6 of 6.

| Case | Valid | Matched | Rate |
|---|---:|---:|---:|
| P1 | 6 | 6 | 1.00 |
| P2 | 6 | 6 | 1.00 |
| P3 | 6 | 6 | 1.00 |
| P4 | 6 | 6 | 1.00 |
| P5 | 6 | 6 | 1.00 |
| P6 | 6 | 6 | 1.00 |
| P7 | 6 | 6 | 1.00 |
| P8 | 6 | 6 | 1.00 |
| P9 | 6 | 6 | 1.00 |
| P10 | 6 | 6 | 1.00 |

**The zero floor never tripped.** No healthy case scored 0 out of 5 or more valid runs, so rule 2 in
[scoring.md](scoring.md) cost nothing here. That is what it should do on a good fixture. It stays in
because it catches the one thing pooling hides, a single dead case buried inside a healthy pooled rate.

## 3. The derived gate

`gate_k = max { k : P(Binom(N, p) < k) <= 0.05 }`, the 5th percentile of the healthy distribution.

**`p` is the lower bound, 0.940, not the point estimate 1.000.** It is the one judgement call here, so
the reasoning is worth setting out.

A gate built on the point estimate demands whatever the pass happened to score. A perfect pass therefore
sets a perfect gate: 10 of 10, 60 of 60, no room for one flaky run. That is not a strict gate, it is a
broken one. [evals.md](evals.md) made the same point about a threshold of 1.0 on a three-run case.

1.000 is the ceiling of what 60 runs can show. It is not evidence that the true rate is 1. The interval
says the same measurement is consistent with a true rate as low as 0.940, and building the gate on that
is reading the measurement honestly rather than flattering it.

| Runs in a pass | `gate_k` | Pass needs |
|---:|---:|---|
| 10 | 8 | 8 of 10 matched |
| 20 | 17 | 17 of 20 matched |
| 30 | 26 | 26 of 30 matched |
| 60 | **53** | **53 of 60 matched** |

At the 60-run count the harness uses, the gate is **53 of 60**, a pooled rate of 0.883. The map carried
0.567, derived from `p_good = 0.67`. That is a much higher bar, and the fixture earned it.

`CalibrationReport.GateK` now takes `PGoodInterval.Low`. The change is in the code, not applied by hand
to this table, so the next pass derives its gate the same way without anyone remembering to.

## 4. The negative side

**Zero false fires.** `csharp-new-class` stayed quiet in all 50 valid should-not-fire runs.

| Case | Boundary defended | Valid | Stayed quiet | False fires |
|---|---|---:|---:|---:|
| N1 | test files | 5 | 5 | 0 |
| N2 | existing classes | 5 | 5 | 0 |
| N3 | existing classes | 5 | 5 | 0 |
| N4 | test files | 5 | 5 | 0 |
| N5 | existing classes | 5 | 5 | 0 |
| N6 | non-C# | 5 | 5 | 0 |
| N7 | non-C# | 5 | 5 | 0 |
| N8 | existing classes | 5 | 5 | 0 |
| N9 | test files | 5 | 5 | 0 |
| N10 | existing classes | 5 | 5 | 0 |

[scoring.md](scoring.md) allowed "at most one" false fire. That was a starting position with no
measurement under it. **Zero is the measurement**, and the right gate for a fixture this clean is zero.

Worth naming what this does and does not show. Every negative case here names a technology or a file
kind the description explicitly excludes. It tests the negative boundary the description was written to
carry. It does not test a request that sits genuinely between two skills, because the stub catalogue has
no near neighbour for `csharp-new-class` to be confused with.

## 5. The watch list

Reported, never gated. These three feed the map's open question.

| Case | Question | Valid | Stayed quiet | Fired |
|---|---|---:|---:|---:|
| W1 | new class **and** a test file | 5 | 5 | 0 |
| W2 | a **record**, not a class | 5 | 0 | **5** |
| W3 | an **interface**, not a class | 5 | 0 | **5** |

All three came back unanimous, 5 to 0. That is the most useful shape an ambiguous case can take, because
there is nothing marginal left to argue about.

**The model treats a record and an interface as a class.** It fired 5 times out of 5 on each. Whether
that is correct is a decision about what `csharp-new-class` is for, not a defect in the skill or the
harness. The measurement is now on the record either way.

W1 is the quieter result and the more reassuring one. A request naming both a new class and a test file
did not fire, 5 times out of 5, which matches the N1, N4 and N9 boundary rather than the positive cases.

## 6. Where in the run the skill fired

[#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11) parked this measurement here. The
`FirstDecision` stop rule kills a run at the first `Skill` call, which is only safe for negatives if
`csharp-new-class` never fires **after** some other skill has already fired. If it did, a run killed at
the first call would score as quiet when it was about to fire.

**Safe.** Across all 65 negative and watch-list runs, `csharp-new-class` never fired after another
skill. `FirstDecision` can be switched on for the negative half.

**Switched on** in [#17](https://github.com/MalcolmMcNeely/skills-marketplace/issues/17), for should-not-fire
cases only. A watch case is graded on silence too, but its record is the full fired set, so it keeps
running to the end.

## 7. What the pass cost

| | |
|---|---|
| Median, positive run | $0.231 |
| Median, negative run | $0.180 |
| Whole pass | $30.010 |

A negative run is the cheaper one, by about 22 per cent. The ticket expected the opposite, on the
reasoning that a negative prompt asks for a SwiftUI view and the model will go and build one. It does
build one. A positive run still costs more, because it fires the skill, reads the body and then does
what the body says.

**These figures are notional.** The pass ran on a subscription, where no cash moves and the runs draw on
usage limits instead. The `result` line reports the same `total_cost_usd` either way and nothing in the
harness can tell the two apart. Read them as a size comparison between run shapes, not as a bill. See
[running-the-paid-layers.md](running-the-paid-layers.md).

## The Void runs, and the one cap that is too tight

The harness threw eight runs away, every one for the same reason: `error_max_budget_usd`, the CLI's own
abort when a run passes `--max-budget-usd`.

| Case | Void runs |
|---|---:|
| P7 | 4 |
| P1 | 1 |
| P10 | 1 |
| N7 | 1 |
| W1 | 1 |

Layer 3 ran at a $0.40 per-run cap, lower than the $0.60 suite default. #11 set it there after
measuring 0.20 as too tight. At $0.40 the pass voided 6 per cent of its runs, which cost about $3 and 12
minutes of resampling and changed no verdict.

**P7 is the finding.** Four of the eight Voids are one case, so P7 needed ten runs to produce six. That
is not random. Something about P7's prompt makes the model work longer, and a cap set from the median
will keep clipping it. Raising layer 3 to $0.60 would probably remove all eight, at the price of letting
a runaway run cost half as much again before the CLI stops it.

I left the cap alone. #11 chose that number on purpose, and none of it changed a figure in this document.

**Raised on 9 September 2026.** [#6](https://github.com/MalcolmMcNeely/skills-marketplace/issues/6)
measured the same clipping again, so
[#18](https://github.com/MalcolmMcNeely/skills-marketplace/issues/18) moved layer 3 to $0.60. That is
the per-run ceiling #11 fixed for every other run shape. A cap decides which runs are void, not what
a valid run does, so every figure above still stands and no gate moved.

## What we could not verify

- **Whether these numbers survive a model change.** Every run was pinned to `claude-opus-5[1m]` on
  Claude Code 2.1.248, and the journal proves no run drifted. Nothing here says how far `p_good` moves
  across models or CLI versions, and every gate value moves with it.
- **Throttle detection.** The harness flagged no run as throttled, so the path built in
  [calibration-prep.md](calibration-prep.md) has still never run. Nothing here can provoke a usage limit
  on demand.
- **Whether `p_good` holds against a near neighbour.** The stub catalogue has no skill that competes
  with `csharp-new-class`. A perfect negative score against distant technologies is a weaker result than
  the same score against a skill one step away.
- **Whether 1.000 is the fixture or the skill.** The fixture is frozen and was written alongside the
  cases. A pass this clean is what a good fixture should produce, and it is also what an easy one would.
  [#6](https://github.com/MalcolmMcNeely/skills-marketplace/issues/6) is the answer to this: a broken
  fixture that fails to go red would settle it.
