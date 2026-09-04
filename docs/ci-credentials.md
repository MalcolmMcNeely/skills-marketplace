# CI credentials and the spend ceiling

Written 4 September 2026. Resolves [#7](https://github.com/MalcolmMcNeely/skills-marketplace/issues/7).
The ceiling values come from [#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11);
the cost figures behind them come from [harness-skeleton.md](harness-skeleton.md).

## The short version

Layers 3 and 4 spend real money, so CI needs a key of its own and a wall to stop
behind. The key is an API key in a workspace of its own. The wall is four
controls at four levels, and only the last of them is a genuine hard stop.

`.github/workflows/paid-harness.yml` runs on `workflow_dispatch` only. There is no
nightly and no pull request trigger, per #11.

## The credential

| Fact | Value |
|---|---|
| Kind | API key, not a subscription token. Metered, capped and revocable on its own |
| Workspace | `skills-marketplace-ci`, separate from the default workspace |
| Key name | `skills-marketplace-ci` |
| GitHub secret | `ANTHROPIC_API_KEY`, a repository secret |
| Read by | The `claude` CLI directly, as an environment variable |

The secret name is not a free choice. The CLI reads `ANTHROPIC_API_KEY` from the
environment, so the workflow only has to pass the secret through under that name.

[`scripts/setup-ci-credentials.sh`](../scripts/setup-ci-credentials.sh) walks a
human through the console steps and sets the secret. Run it again to rotate the key.

## The four ceilings

| Level | Value | Mechanism | Hard stop? |
|---|---|---|---|
| Per run | `$0.60` | `--max-budget-usd`, hardcoded in the harness `RunSpec` | Yes |
| Per suite | `$50` | The harness `SpendLedger`, set by `SKILL_HARNESS_CEILING_USD` | Yes |
| Per workflow run | about `$60` | `timeout-minutes: 210` | No, see below |
| Per month | `$100` | An email notification on the workspace | No |

**The per workflow run ceiling is a clock, not a wallet.** GitHub Actions has no
dollar cap. At the measured `$0.196` and roughly 40 seconds a run, 210 minutes is
roughly `$60`. That sits above the `$50` ledger on purpose, so the ledger reports
the overspend rather than the runner being killed mid-pass with no report. A
workflow that hangs without spending will burn the clock and stop; a workflow that
spends faster than 40 seconds a run will pass `$60` before the clock runs out.

A `concurrency` group stops two dispatches running at once. Each would carry its
own ledger and neither would see the other's spend.

**The only true hard stop is the workspace spend limit.** The console offers a
`Change Limit` cap alongside the `Add notification` alert. #11 chose the alert on
the grounds that a ledger tripping mid-pass wastes everything spent up to that
point. The wizard offers the hard cap as an optional backstop above the alert.

## Version pinning

The CLI version is pinned exactly, in one place:

```yaml
env:
  CLAUDE_CODE_VERSION: "2.1.248"
```

A separate step fails the run if the installed version does not match the pin, so
a silent npm resolution change is loud rather than invisible.

**Why exact and not a range.** Every gate value this map produces is calibrated
against one CLI version. [harness-skeleton.md](harness-skeleton.md) measured
behaviour that differs between shells on the same version, which is a smaller
difference than a version bump. Bumping the pin invalidates the calibration, so a
bump and a re-run of [#12](https://github.com/MalcolmMcNeely/skills-marketplace/issues/12)
are one change, not two.

The runner image is pinned for the same reason: `ubuntu-24.04`, not `ubuntu-latest`.

## Where the harness comes from

`harness/` is not on `main`. It lives on the throwaway branch
`prototype/harness-skeleton`, so the workflow takes a `harness_ref` input and
checks that branch out. The workflow file itself stays on `main`, because
`workflow_dispatch` only appears in the Actions tab for workflows on the default
branch.

## Cost of a pass

Nothing here changes the figures. They are restated so a later ticket does not
have to chase them.

| Pass | Runs | Cost |
|---|---|---|
| One firing run | 1 | `$0.196` |
| One contract run | 1 | `$0.207` |
| [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10)'s layer 3 suite | 125 | about `$24.50` |
| The rest of the map, three attempts | | `$150` to `$200` |

## What we could not verify

- **The cost of a full pass in CI.** Every figure above was measured on a laptop
  against a subscription. Nothing has yet run a paying layer against the CI API
  key, so whether the billed cost matches the reported cost is still open. #12 is
  the first pass that can answer it.
- **The workflow itself.** Neither mode had run when this was written. `smoke`
  installs the CLI, checks the pin, and makes one trivial call; `suite` runs
  `dotnet test` against the model half. The smoke result is recorded on #7 once it
  lands, and this file is corrected against it.
- **The model the suite uses.** The workflow takes a `model` input and the smoke
  step honours it. The harness does not: `RunSpec.Model` is null, so the suite
  takes whatever the CLI defaults to. Until that is made settable, a CI cost figure
  and the laptop's `$0.196` may not be measuring the same model.
- **Whether 210 minutes is the right clock.** It is `$60` divided by a median
  measured from three runs on one afternoon. It has no interval attached.
