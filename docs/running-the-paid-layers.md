# Running the paying layers, and what they actually cost

Written 4 September 2026. Resolves [#7](https://github.com/MalcolmMcNeely/skills-marketplace/issues/7)
by ruling its CI half out of scope. Corrects the cost framing in
[recommendation.md](recommendation.md), [scoring.md](scoring.md) and
[#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11).

## The short version

There is no CI for layers 3 and 4, and no API key. They run on the developer's
machine against the Claude Code subscription, which is how every number on this
map was already produced.

**The dollar figures on this map are notional.** They are what the same work would
cost on the API. On a subscription no cash moves; the runs draw on usage limits
instead. So the `$150` to `$200` #11 priced for the rest of the map is `£0` in cash.

The constraint that replaces money is **time**. #12 is 125 runs at roughly 40
seconds, which is about 83 minutes of continuous calling. A usage limit can stop a
pass partway. A budget cannot.

## Why there is no CI

#7 asked for an API credential, a spend ceiling, and proof that CI could reach
`claude`. Every point of it was provisioning for runs that do not need to happen
off the laptop.

| Claim | Where it was already settled |
|---|---|
| Layer 3 gates nothing during this map | [#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11) |
| The paying layers were button-only, no nightly, no pull request trigger | #11 |
| An automatic gate would block the very pull requests #6 needs to land | #11 |

The destination is a measured number that can be trusted, not a pipeline. Nothing
between here and that number needs a hosted runner. Buying an API key to run in CI
what already runs locally for no cash is the whole of what #7 would have bought.

**What is not ruled out.** Layers 1 and 2 are free, need no credential, and make no
model call. A workflow for those still earns its place, and lands when `harness/`
lands on `main`. That is not #7's question.

## Where the money went before

This is worth stating plainly, because three documents quote dollars without saying
what kind.

| Figure | What it means |
|---|---|
| `$0.196` a firing run | The API-equivalent cost, read off the `result` line |
| `$24.50` a layer 3 pass | 125 of those, still API-equivalent |
| `$50` suite ceiling | A runaway guard in the same notional units |

The `result` line reports the same number whether the run was billed to an API key
or covered by a subscription. Nothing in the harness can tell the two apart, which
is why the distinction was invisible until someone asked to pay for it.

## The ceilings that survive

They are runaway guards, not budgets. Both still work, and both still matter,
because a resample loop that never terminates burns usage limits just as fast as
it would burn money.

| Level | Value | Mechanism |
|---|---|---|
| Per run | `$0.60` | `--max-budget-usd`, hardcoded in the harness `RunSpec` |
| Per suite | `$50` | The harness `SpendLedger`, set by `SKILL_HARNESS_CEILING_USD` |

Ten void runs cost `$2.28` on #8 before the resample cap fired, each one
individually inside its per-run budget. That is the failure the ledger exists for,
and it is unchanged by who is paying.

## Version pinning

Still load-bearing, now as a discipline rather than a CI variable.

Every gate value this map produces is calibrated against one CLI version.
[harness-skeleton.md](harness-skeleton.md) measured behaviour that differs between
shells on the same version, which is a smaller difference than a version bump.
So a CLI upgrade and a re-run of
[#12](https://github.com/MalcolmMcNeely/skills-marketplace/issues/12) are one
change, not two.

Record the version with every measurement. Claude Code `2.1.248` and
`claude-opus-5[1m]` produced everything on this map so far.

## How to run them

```
dotnet test harness/tests/Harness.Free.Tests                        # layers 1 and 2. Free.
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/Harness.Model.Tests  # layers 3 and 4.
```

`harness/` is on `main`. It was built on the throwaway branch
`prototype/harness-skeleton`, which is kept as the primary source.

## What we could not verify

- **What a pass costs in usage limits.** The `result` line reports notional dollars.
  Nothing reports how much of a five-hour window 125 runs consume, so whether #12
  completes in one sitting is unknown until it is tried.
- **Whether a throttled run is distinguishable from a failed one.** The harness
  voids a run that does not exit 0 with `"subtype":"success"` and resamples it. A
  usage limit hit partway through a pass may look like a void run and burn the
  resample cap. This has never happened, and the handling has never been tested.
- **The model the harness uses.** `RunSpec.Model` is null, so runs take whatever the
  CLI defaults to. Every figure so far says `claude-opus-5[1m]`, but nothing pins
  it, so a default change would move the numbers silently. #12 should pin it.
