# The scorer, and where the pass mark goes

Written 2 September 2026 against Claude Code 2.1.248, model `claude-opus-5[1m]`. Every number below was either run on this machine or computed from a stated formula. Builds on [evals.md](evals.md) and [skill-targeting.md](skill-targeting.md); it does not re-litigate them.

## The short version

Four answers, one shape. Score each **run** into a small vocabulary of verdicts rather than a number. Throw away runs the harness broke. Pool the survivors across the whole suite instead of scoring case by case. Set the bar from a calibration pass on a known-good fixture, not from an assumption.

The single most valuable move is smaller than any of that. **Layer 4 should not wait for the skill to fire.** Invoke it by name. `claude -p "/skill-name <task>"` loaded the skill in 2 of 2 runs here and emitted the same `Skill` tool call the harness already parses. That removes firing noise from the contract result entirely, and it is the direct answer to the map's open worry about layer 3 noise drowning layer 4.

The arithmetic that forces the rest: a skill firing at the measured 2 runs in 3 passes a ten-query suite under `skill-creator`'s per-query rule **5 per cent of the time** at three runs, and 42 per cent of the time at fifteen. Scoring per case and requiring every case to pass is unusable at any run count we can afford. Pooling the runs fixes it.

## The four answers

| # | Question | Answer |
|---|---|---|
| 1 | Firing accuracy | A per-run verdict on the **set** that fired, with a separate verdict for a run the harness broke. Keep `skill-creator`'s trigger rate, drop its per-query 0.5 threshold. 0.5 is defensible as a rate and indefensible as a gate |
| 2 | Contract assertions | Firing is a **sampling gate, not a score component**. A non-firing run is `void`, not a zero. Better still, invoke the skill by name so nothing can fail to fire |
| 3 | Aggregation | Neither mean-of-case-scores nor pass-rate-over-cases. Pool every valid run in the suite. 60 should-fire runs per engine, 5 per contract case |
| 4 | The pass mark | A formula, not a number: the 5th percentile of `Binom(N, p_good)`, where `p_good` comes from a calibration pass. At the measured `p_good = 0.67` and `N = 60`, that is **0.567** |

## What the two reference scorers actually do

Neither was executed. `skill-creator` is Python and there is no Python on this machine. `claude plugin eval` is still gated. So everything in this section is a source reading or a quote from a published page, and is labelled as such.

| | `skill-creator` firing | `skill-creator` outcome | `claude plugin eval` |
|---|---|---|---|
| Unit scored | Query | Expectation | Case |
| Per-run value | Boolean: did the stub load | Boolean per expectation | Unknown |
| Within a case | Trigger rate, then thresholded at 0.5 into a boolean | `pass_rate = passed / total` | Unknown |
| Across runs | Not aggregated. The threshold consumes them | `mean` and `stddev` of the per-run pass rate | Unknown |
| Across cases | Count of queries that passed | Delta of means between arms | `--threshold` applies per case |
| Void runs | **Scored as a miss** | No concept of one | Unknown |

Three findings from that reading matter.

**`run_eval.py` scores its own failures as negatives.** The read loop is bounded by `while time.time() - start_time < timeout`. When the timeout expires the loop exits, the process is killed, and line 178 returns `triggered`, which is still `False`. `run_eval` also catches every exception and appends `False`. Stderr is sent to `DEVNULL`, so a crashed run is silent. This is exactly the mistake [skill-targeting.md](skill-targeting.md) caught in our own earlier pass:

> An earlier pass reported runs where nothing fired. Those were the harness timing out, not the model declining.

The reference implementation has that bug. Do not copy it.

**The outcome grader has no concept of a skill that did not fire.** `agents/grader.md` reads a transcript and marks each expectation pass or fail. A run where the skill never loaded produces the same artefact as a run where it loaded and broke its rule: a set of failed expectations. The grader is explicit that there is nothing between the two:

> **No partial credit**: Each expectation is pass or fail, not partial

and

> **When uncertain**: The burden of proof to pass is on the expectation.

That is issue #2's second question, sitting unanswered in the reference implementation.

**Anthropic's own CLI already separates the two, in one narrow place.** From `claude plugin eval --help`, verbatim:

```
under with-without, graders marked with-only, incl.
`tool_used: Skill`, are a plugin-fired indicator
rather than part of the score
```

So firing is treated as an indicator rather than a score component. That is the right instinct. It is applied to the ablation arm only, and it removes firing from the score rather than using it to select the sample. We take the instinct further.

## Answer 1: firing accuracy

### The verdict vocabulary

A run does not produce a number. It produces one of five verdicts, decided in this order.

| Verdict | Test | Firing score | Contract score |
|---|---|---|---|
| `void` | Exit code is not 0, **or** the last `result` line's `subtype` is not `success` | excluded, resample | excluded, resample |
| `missed` | Valid run, expected set is non-empty, fired set is empty | fail | **excluded** |
| `wrong-set` | Fired set is non-empty and differs from the expected set | fail | excluded |
| `held` | Fired set matches, contract assertions pass | pass | pass |
| `broken` | Fired set matches, a contract assertion fails | pass | fail |

Two details make this work in practice.

**Allowlist the success subtype, do not denylist the failures.** Forcing a run to abort here produced `"subtype":"error_max_budget_usd"`, `"is_error":true` and `"terminal_reason":"budget_exhausted"`, at exit 1, with no `Skill` call in the stream. A naive scorer records that as a miss. The full set of failure subtypes is not documented anywhere, so the only safe rule is: a run counts only when it exits 0 **and** its last `subtype` is exactly `success`.

**Score the set, not the skill.** [skill-targeting.md](skill-targeting.md) already settled this. Every case names the exact set of skills that should fire and the whole catalogue is installed. The harness records every `Skill` invocation in the run rather than stopping at the first, and compares sets. `wrong-set` will be rare, because no decoy fired in 44 runs there, but it is the verdict that protects the engine cap and it costs nothing to record.

### Is 0.5 defensible?

The threshold is real and published. From [optimizing descriptions](https://agentskills.io/skill-creation/optimizing-descriptions.md):

> A should-trigger query passes if its trigger rate is above a threshold (0.5 is a reasonable default). A should-not-trigger query passes if its trigger rate is below that threshold.

`run_eval.py` implements it as `trigger_rate >= trigger_threshold`, so the code says "at or above" where the page says "above". At an odd run count the two agree, because 0.5 is unreachable. At an even run count they disagree on a tie. That is a small reason to keep run counts odd, or to stop thresholding per query altogether, which is what we do.

**0.5 is defensible as a summary and indefensible as a gate.** As a summary of one query it is fine. As a gate it fails on the conjunction. The probability that a single should-fire query passes the rule, by true per-run rate and runs per query:

| True per-run rate | n=3 | n=5 | n=7 | n=9 | n=15 |
|---|---|---|---|---|---|
| 0.95 | 0.993 | 0.999 | 1.000 | 1.000 | 1.000 |
| 0.90 | 0.972 | 0.991 | 0.997 | 0.999 | 1.000 |
| 0.80 | 0.896 | 0.942 | 0.967 | 0.980 | 0.996 |
| **0.67** | **0.745** | 0.795 | 0.832 | 0.860 | 0.916 |
| 0.60 | 0.648 | 0.683 | 0.710 | 0.733 | 0.787 |

Now require all ten should-fire queries in a suite to pass:

| True per-run rate | n=3 | n=5 | n=7 | n=9 | n=15 |
|---|---|---|---|---|---|
| 0.95 | 0.930 | 0.988 | 0.998 | 1.000 | 1.000 |
| 0.90 | 0.753 | 0.918 | 0.973 | 0.991 | 1.000 |
| 0.80 | 0.333 | 0.551 | 0.712 | 0.821 | 0.958 |
| **0.67** | **0.053** | 0.101 | 0.159 | 0.222 | 0.417 |

At the rate we actually measured, that gate goes green one time in twenty. Fifteen runs per query, five times the cost, gets it to two times in five. There is no affordable run count at which "every query must pass" works. The problem is not the 0.5 and it is not the run count. It is scoring at the case level and then requiring a conjunction.

### The rule that survives

Two gates, joined by AND.

1. **Pooled**: the fraction of valid should-fire runs across the whole suite that returned `held` or `broken`, meaning the expected set fired, is at or above the calibrated floor.
2. **Floor**: no single should-fire case returns zero fires out of n, at n of 5 or more.

Rule 1 catches a diffuse regression. Rule 2 catches one case going dead while the others carry the average. Rule 2 needs n of at least 5 to be usable, because at the measured rate a healthy case scores 0 of 3 by chance often enough to poison the build:

| Runs per case | P(a healthy case scores 0) | P(any of 10 cases does) |
|---|---|---|
| 3 | 0.037 | **0.314** |
| 5 | 0.0041 | 0.040 |
| 7 | 0.0005 | 0.005 |
| 9 | 0.0001 | 0.001 |

Three runs per query is the reason the zero-floor cannot exist in `skill-creator`'s design. Five runs is the smallest count that buys it.

## Answer 2: contract assertions

### Firing is a sampling gate

The ticket states the requirement exactly: a run where nothing fired is not the same as a run where the skill fired and broke its rule. So do not score them the same, and do not score the first one at all.

A `missed` run carries **no information about the contract**. Scoring it 0 punishes the body for a description problem. Scoring it 1 hides a real break. The only correct treatment is to discard it and take another sample, which makes contract testing a negative-binomial design: keep running until you have `k` runs that actually fired, with a cap.

| Fired runs wanted | Cap on total runs | P(cap hit, at p_fire = 0.67) | Expected runs used |
|---|---|---|---|
| 3 | 6 | 0.100 | 4.5 |
| 3 | 9 | 0.008 | 4.5 |
| 5 | 10 | 0.077 | 7.5 |
| 5 | 15 | 0.002 | 7.5 |
| 10 | 20 | 0.038 | 15.0 |

Hitting the cap is reported as `insufficient-firings`. That is a **layer 3 failure, not a layer 4 failure**, and it must be labelled as such or the two tiers bleed into each other exactly as the map warns.

### Better: do not let it miss

Firing and contract are separable, and the CLI already separates them. `claude -p "/wf-probe <task>"` was run twice here. Both exited 0 with `"subtype":"success"`, both emitted `{"type":"tool_use","name":"Skill","input":{"skill":"wf-probe"}}`, and both put the contract token first in the final `result` field. `claude --help` says the same thing about the stricter mode. Its description of `--bare` reads "Skills still resolve via /skill-name."

This mirrors a design decision `skill-creator` already made in the other direction. `run_eval.py` writes a stub carrying **only the description** and never installs the body, because the body is not in context when the firing decision is made. The symmetric move is to invoke the body directly and never involve the description. One tier per mechanism.

What it buys, in detection power. The gate is "no run returned `broken`", and `b` is the true rate at which the skill breaks its rule:

| True break rate | 3 runs, by name | 3 runs, natural | 5 runs, by name | 5 runs, natural | 10 runs, by name |
|---|---|---|---|---|---|
| 0.70 | 0.973 | 0.848 | 0.998 | 0.957 | 1.000 |
| 0.50 | 0.875 | 0.704 | 0.969 | 0.868 | 0.999 |
| 0.30 | 0.657 | 0.488 | 0.832 | 0.672 | 0.972 |
| 0.20 | 0.488 | 0.349 | 0.672 | 0.511 | 0.893 |
| 0.10 | 0.271 | 0.187 | 0.410 | 0.292 | 0.651 |

Natural invocation costs roughly 15 to 20 points of detection at every break rate, and needs half as many runs again to reach the same number of usable ones. There is no argument for it at layer 4.

The map's stated purpose for layer 4 is to catch a **deliberately broken version**, where `b` is large. Five runs by name catches a half-the-time break 97 per cent of the time. That is the number to build to. It does not certify a healthy skill, and it is not meant to.

### What the contract assertion looks like

Deterministic, and cheap. The probe skill's whole body was one rule: always begin the reply with the exact token `WF-PROBE-OK` on its own line, before anything else. In all ten valid runs, the final `result` field began:

```
"result":"WF-PROBE-OK\n\nI would have OrderService
```

A regex on one JSON field. No judge, no transcript walk, no model call beyond the run itself. [evals.md](evals.md) already sorted the catalogue by whether a skill states something observable; this is what "observable" cashes out as at the wire level.

## Answer 3: aggregation and run count

### Pool the runs

The ticket frames the choice as mean of run scores against pass rate. The honest answer is that both are wrong, because both aggregate at the **case** level, and the case level is where the information is destroyed. The 0.33 granularity in the ticket is not a property of three runs. It is a property of computing a score per case. Pool every valid run in the suite and the granularity is 1/N.

| Aggregation | What it is | Verdict |
|---|---|---|
| Trigger rate per case, threshold at 0.5, then count cases that passed | `skill-creator`'s firing scorer, and the published guidance | Loses information twice. 5 per cent green at the measured rate |
| Mean of per-run expectation pass rates within a case, then mean across runs | `skill-creator`'s outcome scorer, via `aggregate_benchmark.py` | Correct shape for outcome. Wrong unit for firing |
| Pool every valid run in the suite, equal runs per case | Runs are the unit | **This one** |

With equal run counts per case, the pooled proportion equals the mean of the case means, so nothing is lost by keeping cases equal and nothing is gained by weighting them. Keep the per-case rates as a **diagnostic** that names which case broke. Do not gate on them individually, except through the zero-floor.

The published guidance aggregates the other way, and it is worth being clear that we are departing from it. From [optimizing descriptions](https://agentskills.io/skill-creation/optimizing-descriptions.md):

> **Select the best iteration** by its validation pass rate — the fraction of queries in the *validation set* that passed.

That is a fine objective for a description optimiser choosing between five candidates. It is not a gate, it never has to say no, and it is not required to be stable run to run. Ours is all three.

### How many runs

Gate on the pooled should-fire rate. Pick the gate `k` as the largest count for which a healthy skill fails at most 5 per cent of the time, then ask what regression that gate can catch. Healthy is taken as `p_good = 0.67`, from [skill-targeting.md](skill-targeting.md)'s 10 of 15.

| Pooled should-fire runs | Gate `k` | Gate as a rate | False-fail on a healthy skill | Power at 0.55 | Power at 0.50 | Power at 0.40 |
|---|---|---|---|---|---|---|
| 15 | 7 | 0.467 | 0.031 | 0.18 | 0.30 | 0.61 |
| 30 | 16 | 0.533 | 0.043 | 0.36 | 0.57 | 0.90 |
| 45 | 25 | 0.556 | 0.043 | 0.47 | 0.72 | 0.97 |
| **60** | **34** | **0.567** | **0.040** | 0.55 | **0.82** | 0.99 |
| 90 | 53 | 0.589 | 0.049 | 0.74 | 0.94 | 1.00 |
| 180 | 110 | 0.611 | 0.050 | 0.94 | 1.00 | 1.00 |

Thirty pooled runs, which is what the published recommendation of twenty queries at three runs actually delivers on the positive side, catches a drop from 0.67 to 0.50 barely more than half the time. Sixty catches it four times in five. That is the knee.

**The recommended shape, per engine:**

| Part | Cases | Runs each | Total runs |
|---|---|---|---|
| Should-fire | 10 | 6 | 60 |
| Should-not-fire, near-misses | 10 | 3 | 30 |
| Contract, invoked by name | per contract | 5 | 5 per case |

Six runs on the positive side, not five, because 60 pooled is the knee and 6 also satisfies the zero-floor's requirement of at least 5. Three on the negative side, because false firing is measured as near-deterministic and does not need the sample.

## Answer 4: the pass mark

### Set it by procedure, not by number

The map already fixed the principle. This is the mechanics.

1. **Freeze a known-good fixture.** The map's good version of the one throwaway skill.
2. **Run a calibration pass** off the pull-request path, at a high run count, with the catalogue the cases will run against. This yields `p_good`, pooled and per case.
3. **Derive the gate.** `gate_k = max { k : P(Binom(N, p_good) < k) <= 0.05 }`, where `N` is the CI run count. That is the 5th percentile of the healthy distribution. At `p_good = 0.67` and `N = 60`, `gate_k = 34`, a rate of 0.567.
4. **Recalibrate** whenever the model, the Claude Code version or the catalogue changes. None of those are properties of the skill, and all three move `p_good`.
5. **Never round the should-fire gate to 1.0.**

The 5 per cent is the only free parameter, and it is a policy choice about how often a healthy skill may redden the build. Pick it once and write it down, because the failure mode of getting it wrong is permanent: a team that learns to re-run CI until it goes green has destroyed the signal.

### The marks are asymmetric, and the measurement says so

| Side | Measured | Recommended mark | Why |
|---|---|---|---|
| Should fire | 10 of 15, and 8 of 8 here | Pooled rate at the calibrated floor, near 0.57 | Recall is genuinely poor and no wording fixed it |
| Should not fire | 0 decoy fires in 44 runs, 440 opportunities | At most one false fire in the negative suite | Precision is near-perfect but not provably perfect |
| Contract, by name | 10 of 10 held | No `broken` run, at 5 runs | Deterministic in every run observed |

The negative side deserves a caveat that is easy to get wrong. Zero events in 440 opportunities does not mean zero. The 95 per cent Wilson interval on that observation is [0.000, 0.009] per skill-opportunity. Over a negative suite of 30 runs against a twelve-skill catalogue, that is 360 chances to misfire, and a true rate at the top of the interval would produce roughly three. So a gate of exactly zero is not yet justified by the evidence, and "at most one" is the honest starting position. Record every false fire and re-derive after 200 negative runs.

### A canary, and what it is not for

Add one unchanged case to every firing run: a fixture nobody edits. If the canary falls outside its own calibrated band, mark the whole run **inconclusive** and do not fail the pull request. Flag the harness instead.

This is deliberately not a delta gate. [evals.md](evals.md) established that gating on a difference of two arms roughly multiplies the standard error by 1.4, and that conclusion holds here. The canary is a validity check on the environment, not a term in the score. It costs six runs and it is the only thing standing between a model upgrade and a week of red builds nobody can explain.

## What we measured here

Eleven runs against two throwaway stub skills in a scratch directory outside this repository, using `claude -p ... --output-format stream-json --verbose --disallowedTools Write Edit Bash NotebookEdit`. Every run's exit code and terminal `subtype` were checked before it was counted.

| Condition | Prompt | Runs | Result |
|---|---|---|---|
| By name | `/wf-probe` plus an explicit Kafka task | 2 | Fired 2 of 2. Contract held 2 of 2 |
| Natural, explicit | Names Kafka, C#, producer and consumer | 3 | Fired 3 of 3. Contract held 3 of 3 |
| Natural, implicit | Same need, never says Kafka or messaging | 5 | Fired 5 of 5. Contract held 5 of 5 |
| Forced abort | `--max-budget-usd 0.0001` | 1 | Exit 1, `error_max_budget_usd`, no `Skill` call |

The decoy skill, a Vue near-miss, fired in none of the ten valid runs.

**These numbers do not contradict [skill-targeting.md](skill-targeting.md), and the reason is the point.** Eight of eight and ten of fifteen are not distinguishable at these sample sizes:

| Observation | Wilson 95% interval |
|---|---|
| Explicit cross-stack, 12 skills installed, 10 of 15 | [0.417, 0.848] |
| Variant C, 12 of 15 | [0.548, 0.930] |
| This document, 2 skills installed, 8 of 8 | [0.676, 1.000] |
| Decoy fires per explicit run, 0 of 44 | [0.000, 0.080] |
| Decoy fires per skill-opportunity, 0 of 440 | [0.000, 0.009] |

Two intervals that wide, overlapping from 0.68 to 0.85, is the entire case for question 3 in one table. At fifteen runs you cannot tell a skill that fires two thirds of the time from one that fires every time.

The conditions differ in two ways that plausibly matter and were not isolated here: this run had 2 skills installed against 12, and one expected skill against a set of two. The likely reading is that the firing rate is a property of the case and the catalogue rather than of the skill, which is a further argument for calibrating the pass mark per case. It is a reading, not a result.

### Why skills miss, partly explained

[skill-targeting.md](skill-targeting.md) records under what it could not verify: "Nothing here explains the misses". There is a primary-source candidate. From [optimizing descriptions](https://agentskills.io/skill-creation/optimizing-descriptions.md):

> One important nuance: agents typically only consult skills for tasks that require knowledge or capabilities beyond what they can handle alone. A simple, one-step request like "read this PDF" may not trigger a PDF skill even if the description matches perfectly, because the agent can handle it with basic tools.

And from `skill-creator`'s own `SKILL.md`, blunter:

> Note: currently Claude has a tendency to "undertrigger" skills -- to not use them when they'd be useful.

Neither is a measurement of our runs, so this stays a candidate explanation. It does have a consequence for case design that is safe to act on now. A should-fire case must be substantive enough that the model would actually benefit from the skill. `skill-creator` says so directly:

> Simple queries like "read file X" are poor test cases — they won't trigger skills regardless of description quality.

A trivial should-fire case measures the model's self-assessment, not our description.

## Cost, measured

Ten valid runs, total cost `$0.60`, of which the first cold-cache run was `$0.195`.

| Statistic | Value |
|---|---|
| Per run, warm | `$0.032` to `$0.067` |
| Per run, median | about `$0.043` |
| Wall clock per run | 6 to 10 seconds |

These were full runs to completion. A firing run killed at the first tool call, as `run_eval.py` does, costs less; that was not measured.

Extrapolating at `$0.04` a run:

| Pass | Runs | Cost | Wall clock at 10 workers |
|---|---|---|---|
| One engine's firing suite | 90 | about `$3.60` | about 2 minutes |
| One contract case | 5 | about `$0.20` | seconds |
| Nightly, 12 engines | 1,080 | about `$43` | about 20 minutes |

**Superseded for contract runs.** The `$0.043` above was measured on read-only probes made with `--disallowedTools Write Edit Bash NotebookEdit`. [baseline-test-first.md](baseline-test-first.md) later measured a run that writes two files and runs `dotnet test` at a median of `$0.302`, roughly seven times more. One contract case at five runs is therefore about `$1.50`, not `$0.20`. Firing runs are unaffected, because they can still be killed at the first tool call.

That is the first real figure for the map's open question on nightly cost. It is model-specific. These runs used `claude-opus-5[1m]`, and `skill-creator` is explicit that the model must match production:

> Use the model ID from your system prompt (the one powering the current session) so the triggering test matches what the user actually experiences.

Set `--max-budget-usd` on every scheduled run regardless. It is a documented flag on `claude -p` and it works: it is how the void run above was produced.

## What this changes in the plan

Six items for [recommendation.md](recommendation.md) section 3. All are refinements of layers 3 and 4; nothing above them moves.

1. **Layer 4 invokes the skill by name.** `/skill-name` in the prompt. Layer 3 owns the description, layer 4 owns the body, and neither depends on the other. This closes the map's "whether layer 3 noise drowns the layer 4 signal" without needing the harness to run first.
2. **The harness allowlists `"subtype":"success"` at exit 0.** Every other outcome is `void` and is resampled, never scored. This extends [skill-targeting.md](skill-targeting.md)'s exit-code rule, which is necessary but not sufficient.
3. **Run counts change.** Should-fire moves from 3 to 6 per case, should-not-fire stays at 3, contract cases get 5 by-name runs. Roughly 90 runs per engine per pass.
4. **Aggregation is pooled across the suite, not per case.** Per-case rates are reported as diagnostics and gated only by the zero-floor.
5. **`~3 calls per case` at layer 4 is revised to 5.** Three by-name runs catch a break that happens 30 per cent of the time only 66 per cent of the time; five catch it 83 per cent of the time.
6. **The pass mark is a formula plus a calibration pass**, recomputed on any model, CLI or catalogue change, with a canary case marking runs inconclusive rather than red.

## Two gaps from evals.md, noted and not chased

Both were logged against `claude plugin eval` and both are now largely moot, because nothing blocking depends on that CLI.

| Gap | Status |
|---|---|
| What each grader type asserts | Still unknown for `plugin eval`. Irrelevant to this design, which uses none of them |
| How a case score aggregates across runs | Still unknown for `plugin eval`. **Now answered for `skill-creator`**: firing thresholds per query then counts queries; outcome takes the mean and standard deviation of a per-run expectation pass rate, in `aggregate_benchmark.py`. Neither is what we adopt |

## What we could not verify

- **Whether the pass mark transfers between cases.** Eight of eight here against ten of fifteen there, with overlapping intervals and two variables changed at once. The claim that the firing rate is a property of the case rather than the skill is a reading of two incompatible experiments, not a result. The calibration pass is designed to settle it, and until it runs the recommended gate rests on a single 15-run measurement.
- **The negative-side pass mark.** Zero false fires in 440 skill-opportunities bounds the rate below 0.9 per cent, which is not tight enough to justify a gate of exactly zero. "At most one" is a starting position, not a derived number.
- **What `p_good` is for a skill that is not this one.** Everything above takes 0.67 from one experiment on one pair of stub skills. Every gate value in this document moves if that number moves.
- **The cost of a run killed at the first tool call.** Ten full runs were measured at a median of `$0.043`. `run_eval.py` kills early and must be cheaper, but by how much is unknown, and it changes the nightly figure.
- **Whether `skill-creator`'s scripts run on Windows at all.** Two blockers, neither cleared. There is no Python on this machine, so nothing was executed. And `run_single_query` calls `select.select` on a subprocess pipe, which the Python documentation rules out: "File objects on Windows are not acceptable, but sockets are." Reading the source is not the same as running it, and the timeout-scored-as-negative defect above is a source reading too.
- **Whether by-name invocation behaves identically under `--bare`.** `claude --help` states that skills still resolve via a slash name in that mode. The two by-name runs here were in normal mode. Not tested.
- **How many valid runs the contract gate needs to certify a healthy skill.** Five by-name runs catch a deliberately broken fixture, which is the map's stated purpose. Certifying that a healthy skill holds its contract is a different question with a much larger answer: at ten held runs the 95 per cent lower bound is still only 0.72.
- **Whether pooling is safe when cases are genuinely heterogeneous.** Pooling assumes the cases share a firing rate. The zero-floor is the guard against that assumption failing, and the guard has not been tested against a real broken case.
- **Whether `claude plugin eval` would score any of this the same way.** `--help` was re-run here and still prints the full flag list. No real invocation was attempted, because it is gated and the ticket forbade it. Everything in the comparison table's third column is therefore inference from one help page.
