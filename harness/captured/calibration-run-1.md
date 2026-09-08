# Calibration pass: csharp-new-class

Run on 2026-09-08, on this machine, against the frozen good fixture.

| | |
|---|---|
| Model | `claude-opus-5[1m]` |
| Claude Code | 2.1.248 (Claude Code) |
| Runs journalled | 133 |
| Void runs | 8 |
| Throttled runs | 0 |
| Runs on the wrong model | 0 |
| Wall clock | 02:07:29 |
| Reported cost | $30.010 |
| Journal | `C:/Projects/skills-marketplace/harness/captured/calibration-run-1.jsonl` |

## 1. `p_good`, pooled

**60 of 60** valid should-fire runs matched their expected set.

- `p_good` = **1.000**
- Wilson 95% interval = **0.940 to 1.000**

The map carried 0.67, borrowed from a different pair of stub skills. This number replaces it.

## 2. Per-case should-fire rates

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

The zero floor did not trip. No healthy case scored 0 out of 5 or more valid runs.

## 3. The derived gate

`gate_k = max { k : P(Binom(N, p) < k) <= 0.05 }`, the 5th percentile of the healthy distribution.

`p` is the **lower bound** of the interval above, 0.940, not the point estimate 1.000. A gate built on the point estimate demands whatever the pass happened to score, so a perfect pass sets a perfect gate and one flaky run reddens the build. The lower bound is the same measurement read honestly.

| Runs in a pass | `gate_k` | Pass needs |
|---:|---:|---|
| 10 | 8 | 8 of 10 matched |
| 20 | 17 | 17 of 20 matched |
| 30 | 26 | 26 of 30 matched |
| 60 | 53 | 53 of 60 matched |

## 4. The negative side

**0 false fires of `csharp-new-class`** across 50 valid should-not-fire runs.

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

scoring.md's "at most one" was a starting position, not a derived number. This is the measurement.

## 5. The watch list, reported and never gated

These three feed the map's open question on records, interfaces and new test classes.

| Case | Question | Valid | Stayed quiet | Fired |
|---|---|---:|---:|---:|
| W1 | new class AND test file | 5 | 5 | 0 |
| W2 | record, not class | 5 | 0 | 5 |
| W3 | interface, not class | 5 | 0 | 5 |

## 6. Where in the run the skill fired

#11 parked this here. The `FirstDecision` stop rule kills a run at the first `Skill` call. That is safe for negatives only if `csharp-new-class` never fires **after** another skill has already fired.

**Safe.** In no negative or watch run did `csharp-new-class` fire after another skill. The rule can be switched on for negatives, which would cut the cost of the negative half.

## 7. What the pass cost

| | |
|---|---|
| Median, positive run | $0.231 |
| Median, negative run | $0.180 |
| Whole pass | $30.010 |

**These figures are notional.** The pass ran on a subscription, where no cash moves and runs
draw on usage limits instead. The `result` line reports the same `total_cost_usd` either way,
so nothing in the harness can tell them apart. Read them as a size comparison between run
shapes, not as a bill. See [running-the-paid-layers.md](running-the-paid-layers.md).

## What we could not verify

- **Whether these numbers survive a model change.** Every run above was pinned to `claude-opus-5[1m]` on 2.1.248 (Claude Code). Nothing here says how far `p_good` drifts across models or CLI versions, and every gate value moves if it moves.
- **Throttle detection.** A usage limit cannot be provoked on demand, so the markers the harness matches on are read from the shapes the CLI is known to emit, not observed here.
  No run in this pass was detected as throttled, so the path is still unexercised.

