# telemetry

A throwaway rig for watching Claude Code report which skills fire. Answers one question: is a per-skill dashboard worth building for the org?

Nothing here ships. See [docs/research/what-to-measure.md](../docs/research/what-to-measure.md) for what it is testing and why.

## Requirements

- podman (tested on 5.8.2, WSL machine). No compose provider needed
- Node (tested on v24.16.0), for the raw sink only
- PowerShell 7+

## Step 1: see what Claude Code actually sends

- Start the sink: `node telemetry/sink/raw-sink.mjs`
- In a second window: `. .\telemetry\session-env.ps1 sink`
- Run `claude` in that second window and use a skill
- Watch the sink window print each `skill_activated` as it arrives
- Ctrl-C the sink for a summary
- Raw bodies land in `telemetry/sink/capture/*.jsonl`, one JSON per export

## Step 2: the dashboard

- `.\telemetry\stack\up.ps1` (first run is slow, see gotchas)
- In the window you want to measure: `. .\telemetry\session-env.ps1 stack`
- Run `claude` in that window
- Open <http://localhost:3000/d/claude-skill-usage>
- Stop with `.\telemetry\stack\down.ps1`, or `-Purge` to delete the stored data too

## Step 3: fill it with your real history

Live telemetry only counts from the moment you switch it on. Your transcripts already hold about 30 days of real usage.

- `node telemetry/backfill/backfill-transcripts.mjs --dry-run` to see the counts first
- `node telemetry/backfill/backfill-transcripts.mjs` to push them into Loki
- `--days 7` narrows the window
- Measured on this machine: 1,313 transcripts, **797 activations across 42 skills**
- Run it once. Running it twice double-counts. Use `up.ps1 -Reset` to start clean

What backfill gives that live events do not: `repo` and `git.branch`, read from the transcript's `cwd` and `gitBranch`.

It also emits an `api_request` event per attributed request, carrying `input_tokens`, `output_tokens`, `cache_read_tokens` and `cache_creation_tokens` from the transcript's `usage` block, keyed by `attributionSkill`. Measured here: **36,578 requests, 4.0G tokens**. That is per-skill token attribution for a plugin catalogue, which the OpenTelemetry path redacts.

Two things it does not do. It attributes a whole request to whichever skill was active, which is how Claude Code's own cost metric works and is association, not cause. And it emits no money figure, because transcripts carry token counts and no prices.

A skill reaches the transcript by two routes, and reading one of them alone is badly wrong. Reading only `Skill` tool calls reported `/implement` as never used when a developer had typed it **100 times**.

| Route | Shape in the transcript | Trigger |
|---|---|---|
| Skill tool call | assistant `tool_use` named `Skill` | not recoverable, emitted blank |
| Typed slash command | user message with `<command-name>/foo</command-name>` | known, emitted as `user-slash` |

The two do not overlap. A typed command produces no `Skill` tool call.

So backfill can prove a developer typed a skill, and cannot prove the model chose one. `claude-proactive` only ever comes from live telemetry, which is why the donut excludes backfilled rows.

Claude Code's own commands are filtered out by name. Without that, `/clear` alone adds 199 phantom activations.

Every backfilled row carries `origin="backfill"`.

## Step 4: the per-skill report

The dashboard answers "is anyone using skills". This answers "what do I do about skill number seven", which is the question a maintainer actually has.

- `node telemetry/report/skill-report.mjs`
- `--days 7` narrows the window, `--sort uses|cost|name`, `--json` for a machine
- Reads every `SKILL.md` under `.claude/skills/` and `plugins/*/skills/`, then joins it against Loki

One row per skill, with the columns that drive a decision:

| Column | What it tells you |
|---|---|
| `USES` | activations in the window |
| `TOOL` | the model called it, or another skill did |
| `TYPED` | a developer typed the slash command |
| `SESS` | distinct sessions, so one power user does not look like broad adoption |
| `REPO` | how many repositories, so you can see if a technology-specific skill stayed in its lane |
| `OUT` | output tokens generated while the skill was attributed. The expensive kind |
| `TOTAL` | every token of those requests. Cache reads dominate it and bill at a fraction, so read it as volume |
| `CTX` | approximate tokens the listing costs **every turn**, used or not |
| `NOTE` | only where something looks off |

**Why this cannot come from the dashboard.** A query can only return skills that fired. The zero rows are the whole point of the report, and they need the list of skills on disk to exist at all.

Measured here: 28 skills on disk, 18 fired, 10 did not, about 1,235 listing tokens per turn of which 350 went to skills that never fired.

A zero row is a prompt to look, never a delete order. A merge-conflict skill with no activations may only mean a quiet month.

## What the stack is

- Two containers. Loki on 3100, Grafana on 3000
- **No collector.** Loki ingests OTLP directly at `/otlp/v1/logs`
- Grafana starts with the datasource and dashboard already provisioned
- Anonymous admin access, no login. Local prototype only

## What you get

- Count per skill, by real name, including from a private catalogue
- `invocation_trigger` splits `claude-proactive` (the model chose it) from `user-slash` (a developer typed it). This is the engine versus entry point measurement
- `skill.source` splits `plugin` from `projectSettings`
- Which skills never fired in the window

## What you do not get

- Whether a skill helped. Every number here counts activations
- Per-skill cost. That redaction has no escape hatch for a plugin catalogue
- A "never fired" list on its own. The dashboard shows what fired; compare it against the catalogue yourself

## Does this pollute your Claude setup?

- No settings file is written. `session-env.ps1` sets variables in one window only
- Close the window and it is undone
- This does not change what Claude Code sends to Anthropic. Separate pipe, already on, skill names redacted
- `~/.claude.json` and your transcripts already record skill usage today, with or without this

## Gotchas, all hit during the build

- **Skill events are logs, not metrics.** `OTEL_METRICS_EXPORTER` alone gives nothing per-skill
- **`OTEL_LOG_TOOL_DETAILS=1` is the line that matters.** Without it every skill reports as `custom_skill`
- **Loki's first start took about four minutes** on a fresh volume, answering `/ready` with 503 throughout. `up.ps1` now waits up to six
- **Attribute names lose their dots in Loki.** Query `skill_name`, not `skill.name`
- **Git Bash mangles podman paths, arguments as well as mounts.** `-config.file=/etc/loki/...` became `C:/Program Files/Git/etc/loki/...` and Loki exited. Run the scripts in PowerShell, or prefix with `MSYS_NO_PATHCONV=1`
- **Loki refuses old data twice, for two different reasons.** `reject_old_samples` blocks anything over a week. Separately the ingester blocks entries far behind the newest one already in the stream. Backfill needs both lifted, which `up.ps1` now does
- **Grafana drops `legendFormat` on Loki instant queries.** Every label-driven panel showed `Value #A` instead of the skill name. Name the series with `fieldConfig.defaults.displayName` set to `${__field.labels.<label>}` instead
- **`count()` is not the `count` reducer.** Counting distinct skills needs `count(sum by (skill_name) (...))` in the query. A stat panel reducing with `count` counts datapoints per series, which is always 1
- **Only `service_name` is an index label.** Everything else, `skill_name` included, is structured metadata. It filters and groups fine, but `label_values()` cannot list it, so a dropdown of skill names is not possible. The skill filter is a regex textbox. A `label_values` query with a pipeline in it fails with "only label matchers are supported"
- **`user.email` is on by default.** Expand a row in the Raw activations panel and you will see it. Decide about that before any org rollout

## Measured field names

From a real capture on 2026-09-11, Claude Code 2.1.248:

| Field | Example | Loki name |
|---|---|---|
| `service.name` (resource) | `claude-code` | `service_name` (stream label) |
| `event.name` | `skill_activated` | `event_name` |
| `skill.name` | `unslop` | `skill_name` |
| `invocation_trigger` | `claude-proactive` | `invocation_trigger` |
| `skill.source` | `projectSettings` | `skill_source` |
| `session.id` | a UUID | `session_id` |

The log record `body` is `claude_code.skill_activated`, but the `event.name` attribute is the bare `skill_activated`. Query the attribute.

## If you want more panels

Two Apache-2.0 Grafana dashboards cover the same events in more depth:

- `neeltom92/claude-code-observability`
- `KubeRocketCI/claude-code-telemetry`
