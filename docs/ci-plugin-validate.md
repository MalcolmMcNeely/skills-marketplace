# Does `claude plugin validate` need a login on a CI runner?

No. It exits 0 with no credential of any kind on the machine. Resolves
[#19](https://github.com/MalcolmMcNeely/skills-marketplace/issues/19), which is the first task of
[#14](https://github.com/MalcolmMcNeely/skills-marketplace/issues/14).

Date: 2026-09-09. Claude Code 2.1.248. Measured on a GitHub-hosted `ubuntu-24.04` runner, not on this
machine and not read from a source. The run is
[34414475003](https://github.com/MalcolmMcNeely/skills-marketplace/actions/runs/34414475003), on pull
request [#22](https://github.com/MalcolmMcNeely/skills-marketplace/pull/22), against commit `db58e09`.

## Why it mattered

`Layer1_ManifestTests` starts `claude plugin validate . --strict` as a real subprocess and asserts exit
code 0. It had only ever run on a laptop, where a logged-in session is always sitting there. If validate
reached for that session, layer 1 could not gate CI, and #14's workflow would have needed a second
manifest check written in-process to cover the manifests instead.

Two outcomes were acceptable. This is the preferred one.

## What was on the runner

The probe installed `@anthropic-ai/claude-code@2.1.248` from npm, on top of Node 22 and the .NET 10 SDK.
The job was given no secret of its own. GitHub's own `GITHUB_TOKEN` is there as it always is, and checkout
uses it, but no Anthropic credential could reach the job through the environment.

| Checked | Found |
|---|---|
| `ANTHROPIC_API_KEY` | unset |
| `CLAUDE_CODE_OAUTH_TOKEN` | unset |
| Any environment variable matching `anthropic` or `claude` | no line printed |
| Any `claude` file or folder in `$HOME` | no line printed |
| `claude --version` | `2.1.248 (Claude Code)` |

Nobody had ever started the CLI on that machine, so it had no onboarding state either. Validate did not
ask for any. The two searches printed nothing at all, which is what an empty result looks like here, so
read those rows as "the check found nothing" rather than as a positive report of absence.

## The result

I tried four forms. `CLAUDE.md` tells a human to run two commands without `--strict`, and layer 1 runs
the first of them with `--strict`. All four had to hold.

| Command | Exit code | Output |
|---|---|---|
| `claude plugin validate .` | 0 | `✔ Validation passed` |
| `claude plugin validate . --strict` | 0 | `✔ Validation passed` |
| `claude plugin validate ./plugins/core` | 0 | `✔ Validation passed` |
| `claude plugin validate ./plugins/core --strict` | 0 | `✔ Validation passed` |

The first call took about a second. The three after it took between 0.31 and 0.47 seconds. The whole job,
installs included, took 32 seconds.

The same job ran the free suite too. It passed 152 of 152 in one second, so the
`net10.0` projects build on a runner as well. That covers the rest of what #14's workflow will need.

## What this does not prove

- It says nothing about layers 3 and 4. Those make real model calls, they do need a credential, and #11
  and #7 keep them off CI on purpose.
- It is a statement about 2.1.248. A version bump is the moment to re-run it, alongside the calibration
  and breakage passes the same pin already holds.
- Validate passed here on manifests that are correct. The probe never handed it a broken one, so the
  failing exit code is still something I have only seen on a laptop.

## What happens to #14's workflow

Nothing changes. The workflow installs the pinned CLI and runs the same three commands `CLAUDE.md` gives
a human, layer 1's subprocess included. No credential, no secret, no fallback.

The workflow that measured this was throwaway on purpose. It ran on a pull request branch that never
landed, and only this document came back to `main`, so nothing under `.github/` reached `main` from the
probe. The workflow that lands there is #14's.
