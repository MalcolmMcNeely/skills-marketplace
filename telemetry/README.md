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

What it cannot give: **`invocation_trigger`**. A transcript records which skill ran, never why. There is no way to tell a model-chosen activation from a typed slash command after the fact, so the script emits no trigger rather than guessing, and the donut panel excludes those rows. Backfill answers "what gets used". Only live events answer "did the description work".

Every backfilled row carries `origin="backfill"`.

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
