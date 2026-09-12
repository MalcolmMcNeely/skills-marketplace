# Rolling the catalogue out, and measuring what it does

Written 12 September 2026 against Claude Code `2.1.248`. This is the plan for getting the catalogue onto every developer's machine and finding out what it costs once it is there.

[recommendation.md](recommendation.md) decides what a skill should say and how the catalogue is shaped. This document starts where that one stops: delivery, instrumentation and the order to build them in. The numbers come from three research lines already in `docs/`, and the links sit where the numbers do.

**Measured** means a command ran on this machine and we kept the output. Anything else says where it was read from.

## The three moves

**One. Deliver by policy.** IT pushes one managed settings file. It registers the marketplace, enables the plugins and sets the telemetry variables. Managed settings sit above every other layer, so a developer cannot switch any of it off and cannot drift.

**Two. Count skills from the log event, never the cost metric.** `claude_code.skill_activated` carries the real skill name once `OTEL_LOG_TOOL_DETAILS=1` is set. The cost metric looks like it does the same job and does not, for the reason below.

**Three. Take money from the transcripts.** A skill delivered in a private plugin reports its cost as the literal `third-party`, and that redaction has no escape hatch. Per-skill tokens come off the session files on disk instead.

## How you ship decides what you can see

Two identical skills ran in one session, one loaded from a skills folder and one from a plugin. Same body, same session, different route. Only one could be costed **[measured, [skill-usage-telemetry.md](research/skill-usage-telemetry.md)]**.

| Delivery route | `skill_activated`, default | With `OTEL_LOG_TOOL_DETAILS=1` | `cost.usage` and `token.usage` |
|---|---|---|---|
| A skills folder, any scope | `custom_skill` | Real name | **Real name** |
| Managed enterprise skills folder | `custom_skill` | Real name | **Real name**, same code path, not verified |
| Plugin, a private marketplace | `custom_skill` | Real name | **`third-party`** |
| Plugin, an Anthropic marketplace | Real name | Real name | Real name |

This is not a reason to abandon plugins. A plugin carries versioning, auto-update, per-repo enablement and shipped hooks, and a managed skills folder carries none of them. Keep the plugin and read cost off the transcripts.

## The one real decision

Everything below follows from where the catalogue lives.

| | Private git remote | The claude.ai organisation console |
|---|---|---|
| Catalogue stays on our infrastructure | Yes | No. It uploads to Anthropic |
| First install | Needs a seed directory or one command | Mark the plugin `Required` and it installs itself |
| Per-skill cost | We build the scraper | One endpoint, Enterprise plan only |
| Skill names resolve server-side | No | Yes |
| Plan tier | None | Team or Enterprise |

**Take the private git remote.** The console route buys a cheaper rollout and better telemetry, and costs control of the catalogue. Start private, because moving to the console later is possible and un-uploading is not. If the organisation is already on Enterprise and nobody objects to the upload, take the console route and delete half the work below.

## What a developer does

Nothing, on a managed machine. The settings file does it.

Elsewhere it is two commands, once:

```
/plugin marketplace add git@git.yourorg.internal:platform/skills-marketplace.git
/plugin install core@skills-marketplace
```

Then the plugin sets itself up. Three of the four pieces need no administrator at all.

| The plugin ships | What it does | Needs an admin |
|---|---|---|
| `plugins/core/hooks/hooks.json` | A `PostToolUse` hook matching `Skill`, running the counter below | No |
| `plugins/core/hooks/count-skill.mjs` | Appends one JSON line per activation to `~/.claude/skill-usage.jsonl` | No |
| An entry point that checks the environment | Reads the developer's own settings, names the missing variable, offers to write it | No |
| A `relevance` block per plugin | Matches a regex against the repo's manifest and suggests the right plugin | Yes, one key |

### The hook is the part to ship first

It reads `.tool_input.skill` out of the `PostToolUse` payload, unredacted. The same invocation that read `probekit:probe-plugin` in a hook read `third-party` on `claude_code.cost.usage` in the same session **[measured]**. A hook sees what the telemetry pipe hides.

Four properties earn it the first slot. It needs no collector, no API key and no plan tier. It ships inside the plugin, so it arrives with the skills. `PostToolUse` cannot block, so a broken counter cannot break a session. And it carries `duration_ms` and `cwd`, so usage cuts by repository.

The counter is a Node script rather than the obvious `jq` one-liner, because `jq` is not on a Windows machine by default and the fleet is mixed. Every failure path in it is silent and exits 0. It costs about 325 ms a spawn on this machine, which is Node's cold start on Windows **[measured]**. That is per `Skill` call, not per turn.

## What IT pushes

[managed-settings.example.json](managed-settings.example.json) is the file, ready to copy. It goes in the managed directory:

| Platform | Path |
|---|---|
| Windows | `C:\Program Files\ClaudeCode\managed-settings.json` |
| macOS | `/Library/Application Support/ClaudeCode` |
| Linux and WSL | `/etc/claude-code` |

Claude Code does not read the legacy Windows path under `ProgramData`.

Three settings decide whether a merge ever reaches anybody. Miss one and the rollout looks healthy and delivers nothing.

| Setting | Why it is not optional |
|---|---|
| `"autoUpdate": true` | A third-party marketplace has auto-update **off** by default |
| `FORCE_AUTOUPDATE_PLUGINS=1` | IT often sets `DISABLE_AUTOUPDATER`, which stops plugin updates too |
| A version bump in CI on merge | A pinned version never updates until somebody bumps it. This is the step that fails silently |

Four traps sit around that file, all measured **[[mcp-and-plugins.md](mcp-and-plugins.md)]**.

**A marketplace registers under the name in its own manifest, not the key it was declared under.** Declaring one as `authmp` produced `urlmp`. Our manifest says `skills-marketplace`, so `enabledPlugins` must say `core@skills-marketplace`.

**Managed settings enable a plugin. They do not install it.** Naming an uninstalled plugin wrote a record pointing at a directory that was never created. Two things close the gap: a read-only seed directory in the machine image with `CLAUDE_CODE_PLUGIN_SEED_DIR` pointing at it, which installs on first launch with no git and no network; or the console route with the plugin marked `Required`.

**Auto-update is slower than it sounds, and short sessions never get it.** Updates landed about seven and eight minutes into a session, behind a random delay of up to ten minutes. A twenty-two-second run stayed on the old commit.

**Auto-update never delivers a plugin nobody has yet.** It refreshes what is installed. A new plugin in the catalogue still needs the bootstrap.

The example file uses the `github` source form, which is the one quoted in [recommendation.md](recommendation.md). An internal host declares a `git` source with a URL instead. The docs carry that form and nobody exercised it here, so confirm it on one machine before it goes to the fleet.

Also confirm IT leaves `disableCommandPluginSources` off, or step two of the shipping plan closes.

## What we can measure, and what is blocked

| Question | Where the answer comes from | Build | State |
|---|---|---|---|
| Skill X fired N times last week, by real name, across the fleet | `skill_activated` over OTLP with `OTEL_LOG_TOOL_DETAILS=1` | A collector and a dashboard | Measured |
| Did the model choose it, or did a developer type it | The same event, grouped by `invocation_trigger` | A group-by | Measured |
| A fleet counter with no collector | The `PostToolUse` hook, reading `.tool_input.skill` | Shipped in this plugin | Measured |
| Per-skill tokens for a plugin catalogue | `attributionSkill` and the `usage` block in the session transcripts | `telemetry/backfill/backfill-transcripts.mjs`. 36,578 requests and 4.0G tokens read here | Measured |
| Which skills never fired at all | The skills on disk, joined against the count | `telemetry/report/skill-report.mjs` | Measured |
| What the listing costs every turn, used or not | The same report. Here: 28 skills, 18 fired, about 1,235 tokens a turn, 350 of them on skills that never fired | Nothing new | Measured |
| Per-skill uses, users and spend, server-side | `GET /v1/organizations/analytics/skills` | A curl and a cron job | Enterprise only, **not verified** |
| Per-skill money over OpenTelemetry, from a plugin | Hard-coded to `third-party`, no escape hatch in the function | | **Blocked** |
| A skill was listed, read, and the model declined it | Nothing emits this. Only an activation counts | | **Blocked** |
| Whether the skill helped | Nothing on this table | | **Blocked** |

Two gotchas from building the prototype **[[telemetry/README.md](../telemetry/README.md)]**. Skill events are logs, so a metrics exporter alone gives nothing per skill. And attribute names lose their dots in Loki, so the query is `skill_name`.

## What a count cannot tell you

The survey checked twenty-seven companies against their own sources and not one of them publishes a per-skill invocation count **[[who-measures-skill-usage.md](research/who-measures-skill-usage.md)]**. Atlassian ran the largest study anyone has published and then moved off usage counting deliberately, towards pull request throughput.

So the count earns its place as the input to one decision, whether a skill still deserves its listing slot, and nothing more. Four other instruments answer the rest.

| Question | Instrument | Cost | State |
|---|---|---|---|
| Does the description fire when it should | `harness/`, a frozen fixture gated at 53 of 60 pooled runs | Built. About $30 and two hours a pass | Built |
| Has the skill gone stale | A CI check that fails a pull request touching the documented thing when the skill file is untouched | Free, in the gate we already run | To build |
| Did the session go well | `CLAUDE_CODE_ENABLE_FEEDBACK_SURVEY_FOR_OTEL`, joined to the skills that fired by `session.id` | Free. One variable and a join | To build |
| Did this skill make the code better | Pick a skill with an observable output, count how often it survives into the merged commit, compare against the same work without it | Expensive. The only published route that reaches it | Later, one skill at a time |

Say this out loud when you present the dashboard, or somebody will read a large number as proof of value. Production counts prove a skill is reachable. The harness proves the description works. Neither proves the skill helped.

## Six phases

Each one works alone. Stop at any point and something is still running.

**0. Decide two things.** Pick the delivery route. Then decide about `user.email`, which rides on every telemetry event by default with no switch, so our own dashboard will show who ran what **[measured]**. That sentence needs to reach legal before the collector exists, not after.

**1. Ship the plugin with the hook inside.** Done, in `plugins/core/hooks/`. From the first install there is a per-skill count with a real name, a repository and a timestamp.

**2. Hand IT the settings file.** [managed-settings.example.json](managed-settings.example.json). Add the CI version bump in the same week, because without it nobody ever receives an update.

**3. Stand up the collector and the dashboard.** Keep only `skill_activated`. Two Apache-2.0 Grafana dashboards already cover these events, so lift the panels. `telemetry/stack/` is the working prototype.

**4. Add the cost scraper.** A scheduled job over the transcripts. Raise `cleanupPeriodDays` first or the thirty-day window closes. Transcripts carry tokens and no prices, so keep a price table beside it.

**5. Wire the judgement in.** The staleness gate and the feedback survey join, both free. Then run the harness on any pull request that changes a description.

## What already exists

| Piece | Where | State |
|---|---|---|
| Marketplace and plugin manifests | `.claude-plugin/` and `plugins/core/` | Done |
| Free quality gate, 295 tests, about a second | `harness/tests/free` | Done |
| Firing harness, gated at 53 of 60 | `harness/skills/` | Done |
| The hook counter | `plugins/core/hooks/` | Done |
| Managed settings example | [managed-settings.example.json](managed-settings.example.json) | Done |
| Collector, dashboard, backfill, per-skill report | `telemetry/` | Prototype. Promote the last two |
| Environment-check entry point | | To build |
| Staleness gate in CI | | To build |
| The catalogue itself | `plugins/core/skills/`, two skills | The actual work |

`telemetry/` is marked throwaway and two pieces of it should not be thrown away. The backfill script is the only route to per-skill tokens for a plugin catalogue. The report script is the only route to naming a skill that never fired, because a query can only return what fired.

## What was not verified

- The Enterprise analytics endpoint. Nobody here had an Enterprise plan, so every field comes from the reference.
- Whether a private git marketplace resolves to readable names inside that endpoint. The route that does resolve is the same one that uploads the catalogue.
- The `git` source form for `extraKnownMarketplaces` on an internal host.
- The claude.ai console organisation plugin sync, and its `Required` distribution state.
- Behaviour after `2.1.248`. Two relevant features landed in the twenty-one releases that followed, so re-measure after any upgrade.
