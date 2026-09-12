# Measuring a skill catalogue

What an organisation can actually count once a catalogue ships, and by what mechanism. Researched 11 September 2026 against Claude Code `2.1.248`.

Three siblings complete the set. [what-to-measure.md](what-to-measure.md) is the decision document and the place to start, [who-measures-skill-usage.md](who-measures-skill-usage.md) records what other companies publish, and [buying-skill-telemetry.md](buying-skill-telemetry.md) asks whether any of this can be bought rather than built.

Three labels run through this document. **Measured** means a command was run on this machine (Windows 11 Pro 26200) on 2026-09-11 and its output is quoted. **[binary]** means the string was read out of the installed `claude.exe`, a 226 MB compiled bundle whose embedded JavaScript is readable with `grep -a`. Everything else is read from the cited page. The binary is the implementation rather than a description of it, so it outranks the docs, and in one place below it settles a question the docs leave ambiguous.

The newest release is 2.1.269. This machine is pinned to 2.1.248, twenty-one releases back, and two relevant features landed in between. Both are flagged where they appear.

Total cost of the measured runs: $0.33 across three `claude -p` sessions.

## The short version

A per-skill dimension exists, and it is better than expected. Claude Code emits an OpenTelemetry event called `claude_code.skill_activated` on every skill activation, and `skill.name` is an attribute on it. A dashboard can answer "skill X fired N times last week" today, with no new build beyond a collector.

The catch is redaction, and it lands exactly on the case this repository cares about. By default `skill.name` reads `custom_skill` for every skill that is not shipped by Anthropic, including a skill in your own plugin. One environment variable removes that. A second, separate redaction on the cost and token metrics cannot be removed at all, and it is triggered by plugin delivery specifically.

The sharpest finding is that **the delivery route changes what you can measure**. Two identical skills were invoked in one session. The one loaded from `.claude/skills/` reported `skill.name: "probe-local"` on `claude_code.cost.usage`. The one loaded from a plugin reported `skill.name: "third-party"`. Same skill body, same session, different route, and only one of them can be costed.

## 1. OpenTelemetry

Source: [Monitoring](https://code.claude.com/docs/en/monitoring-usage). All env-var names, metric names and event names below are verbatim.

### Turning it on

| Variable | Default | Purpose |
|---|---|---|
| `CLAUDE_CODE_ENABLE_TELEMETRY` | disabled | Required. Nothing exports without it |
| `OTEL_METRICS_EXPORTER` | none | `console`, `otlp`, `prometheus`, `none` |
| `OTEL_LOGS_EXPORTER` | none | `console`, `otlp`, `none`. **Events are logs. Without this there is no per-skill data at all** |
| `OTEL_TRACES_EXPORTER` | none | `console`, `otlp`, `none` |
| `CLAUDE_CODE_ENHANCED_TELEMETRY_BETA` | disabled | Required for spans |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | none, must set | `grpc`, `http/json`, `http/protobuf` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Collector endpoint for all signals |

Per-signal overrides exist for protocol, endpoint and headers (`OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` and the logs and traces equivalents). mTLS is `OTEL_EXPORTER_OTLP_CLIENT_KEY` and `_CERTIFICATE` for gRPC, `CLAUDE_CODE_CLIENT_CERT` and `CLAUDE_CODE_CLIENT_KEY` for the http protocols.

### Export intervals

| Signal | Default | Variable |
|---|---|---|
| Metrics | 60,000 ms | `OTEL_METRIC_EXPORT_INTERVAL` |
| Logs and events | 5,000 ms | `OTEL_LOGS_EXPORT_INTERVAL` |
| Traces | 5,000 ms | `OTEL_TRACES_EXPORT_INTERVAL` |

Metrics temporality defaults to `delta`, changed with `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE`.

### Cardinality controls

| Variable | Default | Adds |
|---|---|---|
| `OTEL_METRICS_INCLUDE_SESSION_ID` | `true` | `session.id` |
| `OTEL_METRICS_INCLUDE_ACCOUNT_UUID` | `true` | `user.account_uuid`, `user.account_id` |
| `OTEL_METRICS_INCLUDE_VERSION` | `false` | `app.version` |
| `OTEL_METRICS_INCLUDE_ENTRYPOINT` | `false` | `app.entrypoint` |
| `OTEL_METRICS_INCLUDE_RESOURCE_ATTRIBUTES` | `true` | Keys from `OTEL_RESOURCE_ATTRIBUTES` as datapoint labels |
| `OTEL_METRICS_INCLUDE_REPOSITORY` | not in 2.1.248 | `vcs.*` attributes. Changelog dates it to **2.1.269** |

Session id is on by default and is the highest-cardinality attribute in the set. The docs' only cardinality warning is about custom attributes: "Each custom key becomes a label on every metric series, so high-cardinality values increase storage cost in your metrics backend." The advice is to use bounded values such as department or team, or to set `OTEL_METRICS_INCLUDE_RESOURCE_ATTRIBUTES=false` so custom keys ride in the resource block only.

There is no cardinality control for `skill.name`. It is either present or redacted, per the rules below.

### The metrics, complete

Eight, and the binary confirms there are no others. Grepping `claude_code\.[a-z_.]*` returns exactly these plus the span names **[binary]**:

| Metric | Unit | Attributes beyond the standard set |
|---|---|---|
| `claude_code.session.count` | none | `start_type` (`fresh`/`resume`/`continue`/`agents_view`) |
| `claude_code.lines_of_code.count` | none | `type` (`added`/`removed`), `model` |
| `claude_code.pull_request.count` | none | none |
| `claude_code.commit.count` | none | none |
| `claude_code.cost.usage` | USD | `model`, `query_source`, `speed`, `effort`, **`skill.name`**, `agent.name`, `plugin.name`, `marketplace.name`, `mcp_server.name`, `mcp_tool.name` |
| `claude_code.token.usage` | tokens | the same, plus `type` (`input`/`output`/`cacheRead`/`cacheCreation`) |
| `claude_code.code_edit_tool.decision` | none | `tool_name`, `decision`, `source`, `language` |
| `claude_code.active_time.total` | s | `type` (`user`/`cli`) |

**There is no `claude_code.skill.count` metric.** A skill invocation is not a counter. It is an event, and it is attribution on the cost and token counters.

Nine span names also exist in the bundle, gated behind `CLAUDE_CODE_ENHANCED_TELEMETRY_BETA` **[binary]**: `claude_code.tool.execution`, `claude_code.llm_request`, `claude_code.subagent.spawn`, `claude_code.hook`, `claude_code.compaction`, `claude_code.bash.subprocess`, `claude_code.interaction`, `claude_code.mcp.rpc`, `claude_code.tool.blocked_on_user`.

### The events, complete

Every event passes through one emitter **[binary]**:

```js
async function So(e,t={},r){ ... let d={timestamp:c,observedTimestamp:c,
  body:`claude_code.${e}`,attributes:o, ...}
```

So the log record's `body` is `claude_code.<name>` and the `event.name` attribute is the bare name. A backend querying `event.name` must match `skill_activated`, not `claude_code.skill_activated`. That distinction is not in the docs and is worth knowing before writing a dashboard query.

Grepping the emitter's call sites gives the full event list **[binary]**: `user_prompt`, `assistant_response`, `tool_result`, `tool_decision`, `tool`, `api_request`, `api_error`, `api_refusal`, `api_retries_exhausted`, **`skill_activated`**, `plugin_loaded`, `plugin_installed`, `mcp_server_connection`, `hook_registered`, `hook_execution_start`, `hook_execution_complete`, `hook_plugin_metrics`, `subagent_completed`, `compaction`, `permission_mode_changed`, `retention_sweep`, `system_prompt`, `summary`, `at_mention`, `auth`, `internal_error`, `feedback_survey`.

`skill_activated` is not on the monitoring documentation page. It was found by grepping the binary and then confirmed on the wire.

### Standard attributes on everything

Captured verbatim from a real OTLP payload **[measured]**:

| Attribute | Example from the capture |
|---|---|
| `user.id` | `a68801ea…` (random anonymous id, persisted in `~/.claude.json`) |
| `session.id` | `739044cd-ce53-455d-b189-b8946926330a` |
| `app.version` | `2.1.248` |
| `organization.id` | `14451454-16e1-4f42-8288-6b7f6130f659` |
| `user.email` | the signed-in address, verbatim |
| `user.account_uuid` | `784e9f9a-…` |
| `user.account_id` | `user_01FrehyoAK5cBuRdy4emMxDV` |
| `terminal.type` | `windows-terminal` |
| `event.name`, `event.timestamp`, `event.sequence` | per event |
| `prompt.id` | UUID joining every event from one user prompt |

`user.email` is present by default and has no include/exclude switch in the table above. An organisation piping this to a shared dashboard is publishing who ran what.

`prompt.id` is the join key that makes skill analysis possible: it links a `user_prompt` event to every `skill_activated`, `tool_result` and `api_request` that followed from it.

### Is there a per-skill dimension? Yes, with two redactions

`claude_code.skill_activated`, captured on the wire with default settings **[measured]**:

```json
{"event.name":"skill_activated","skill.name":"custom_skill",
 "invocation_trigger":"claude-proactive","skill.source":"projectSettings"}
{"event.name":"skill_activated","skill.name":"custom_skill",
 "invocation_trigger":"nested-skill","skill.source":"plugin"}
```

Both skills redacted. The predicate **[binary]**:

```js
k = o==="builtin" || o==="bundled" || o==="plugin"&&rp(p) || Va();
So("skill_activated",{"skill.name":k?e:"custom_skill", invocation_trigger:r,
  ...o&&{"skill.source":o}, ...t?.kind&&{"skill.kind":t.kind},
  ...k&&u&&{"plugin.name":u.pluginManifest.name},
  ...k&&p&&{"marketplace.name":p}})
```

`rp(p)` tests the marketplace name against a hard-coded set of Anthropic marketplaces (`claude-code-marketplace`, `claude-plugins-official`, `anthropic-plugins`, `agent-skills` and nine more) **[binary]**. `Va()` is the escape hatch, and it is one line **[binary]**:

```js
function Va(){return a.OTEL_LOG_TOOL_DETAILS}
```

Setting it removes the redaction completely. Same fixture, same two skills, `OTEL_LOG_TOOL_DETAILS=1` **[measured]**:

```json
{"event.name":"skill_activated","skill.name":"probe-local",
 "invocation_trigger":"claude-proactive","skill.source":"projectSettings"}
{"event.name":"skill_activated","skill.name":"probekit:probe-plugin",
 "invocation_trigger":"nested-skill","skill.source":"plugin",
 "plugin.name":"probekit","marketplace.name":"privateprobe"}
```

The private plugin name and the private marketplace name both appear. **This is the answer to the headline question.** Set `OTEL_LOG_TOOL_DETAILS=1` alongside a logs exporter and a dashboard can count skill activations by name, including skills from a private catalogue.

`skill.name` for a plugin skill is namespaced `<plugin>:<skill>`. Parse on the colon.

The attribute values **[binary]**, all four confirmed present as literals:

| Attribute | Values |
|---|---|
| `invocation_trigger` | `claude-proactive`, `user-slash`, `nested-skill`, `agent-preload` |
| `skill.source` | `builtin`, `bundled`, `userSettings`, `projectSettings`, `plugin`, `memoryStore`, `syncedSkills` |
| `skill.kind` | present when the skill declares one |

`claude-proactive` is the model choosing the skill. `user-slash` is a human typing it. That single attribute separates "the description worked" from "somebody had to ask", which is the measurement the catalogue's description rules actually need.

`OTEL_LOG_TOOL_DETAILS=1` also unredacts `tool_result` **[measured]**:

```json
{"event.name":"tool_result","tool_name":"Skill","success":"true","duration_ms":"4",
 "tool_parameters":"{\"skill_name\":\"probekit:probe-plugin\"}",
 "tool_input":"{\"skill\":\"probekit:probe-plugin\"}"}
```

Two independent signals for the same fact. `tool_result` also carries `duration_ms`, which `skill_activated` does not.

### The second redaction, which has no switch

`skill.name` also rides on `claude_code.cost.usage`, `claude_code.token.usage`, `api_request`, `api_error` and `api_refusal`. That is a different code path with a different rule **[binary]**:

```js
if(u!==void 0){                       // u = attributionPlugin
  if(k.has(u)){                       // k = firstPartyPlugins
    C.attributionPlugin=u; if(o!==void 0) C.attributionSkill=o }
  else { C.attributionPlugin=pv; if(o!==void 0) C.attributionSkill=pv }
} else if(o!==void 0) C.attributionSkill=o;   // no plugin: verbatim
```

`pv` is the literal `"third-party"`. There is no `Va()` call anywhere in this function, and the measured run with `OTEL_LOG_TOOL_DETAILS=1` confirms it: `skill_activated` unredacted in the same session where `api_request` still said `third-party`.

The metrics, captured from the sink, one session, two skills **[measured]**:

```
claude_code.cost.usage  USD  {"model":"claude-opus-5[1m]","query_source":"main",
                              "effort":"xhigh","skill.name":"probe-local"}
claude_code.cost.usage  USD  {"model":"claude-opus-5[1m]","query_source":"main",
                              "effort":"xhigh","skill.name":"third-party",
                              "plugin.name":"third-party"}
```

| Delivery route | `skill_activated` default | `skill_activated` with `OTEL_LOG_TOOL_DETAILS=1` | `cost.usage` / `token.usage` |
|---|---|---|---|
| `.claude/skills/`, any scope | `custom_skill` | real name | **real name** |
| Managed enterprise skills dir | `custom_skill` | real name | **real name**, not verified but same code path |
| Plugin, private marketplace | `custom_skill` | real name | **`third-party`** |
| Plugin, Anthropic marketplace | real name | real name | real name |

Read that table before choosing a delivery route. Shipping the catalogue as a plugin costs you per-skill cost and token attribution permanently. Shipping the same skill bodies into a skills directory keeps it. The repository's current plan is a plugin, and this is a real, measured cost of that choice that was not previously on the table.

`plugin_loaded` is redacted the same way and adds one useful field **[measured]**:

```json
{"event.name":"plugin_loaded","plugin.name":"third-party","marketplace.name":"third-party",
 "plugin.scope":"user-local","enabled_via":"user-install","plugin_id_hash":"4078cc1a552e21c5",
 "has_hooks":false,"has_mcp":false,"skill_path_count":1}
```

`plugin_id_hash` is not anonymous. The hash function is **[binary]**:

```js
var fWt="claude-plugin-telemetry-v1";
function eR(e){return mWt("sha256").update(e+fWt).digest("hex").slice(0,16)}
```

An unkeyed SHA-256 over `<plugin>@<marketplace>` with a hard-coded public salt. Verified **[measured]**: `sha256("probekit@privateprobe" + "claude-plugin-telemetry-v1")` truncated to 16 hex characters is `4078cc1a552e21c5`, an exact match for the captured value. So an organisation can precompute the hashes of its own plugins and join redacted rows back to real names on its own dashboard. So can anyone else holding a candidate name list, which is the other half of that fact.

`plugin.scope` took the value `user-local`. The classifier returns `default-bundle`, `official`, `community`, `org` or `user-local` **[binary]**, and only the first two count as first-party. **`org` does not**, so even a plugin pushed through the claude.ai organisation console is redacted to `third-party` on the cost metrics.

## 2. The binary

Version grepped: `2.1.248 (Claude Code)` **[measured]**, at `C:\Users\malco\AppData\Local\Microsoft\WinGet\Packages\Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe\claude.exe`.

[mcp-and-plugins.md](../mcp-and-plugins.md) already records `plugin_name_redacted` and `"custom_skill"` under "What leaks". This section only adds what is new.

**A skill invocation produces a locally observable event with the skill's name on it, and it goes to three places.**

### Where a skill invocation is recorded

| Sink | Carries the skill name? | Reaches |
|---|---|---|
| `claude_code.skill_activated` via OTLP | Only with `OTEL_LOG_TOOL_DETAILS=1` | The org's collector |
| The same event via the first-party exporter | Redacted to `custom_skill` | Anthropic |
| `~/.claude.json` key `skillUsage` | **Always, verbatim** | Nowhere. Local file |
| `~/.claude/projects/**/*.jsonl` | **Always, verbatim** | Nowhere. Local file |
| `tengu_skill_tool_invocation` and siblings | Name hashed | Anthropic |

The event emitter routes to the org's logger when one is attached and to a first-party logger otherwise, both under the instrumentation scope `com.anthropic.claude_code.events` **[binary]**.

The first-party analytics events carry a hashed name rather than a cleartext one **[binary]**:

```js
function Ven(e){return {skill_name_hash:Lo(eR(e))}}
```

Same `eR` salt as above, so the same reversibility applies. The `tengu_` events touching skills are `tengu_skill_tool_invocation`, `tengu_skill_loaded`, `tengu_skill_authored`, `tengu_skill_file_changed`, `tengu_plugin_skills_dir_loaded`, `tengu_propose_skills`, `tengu_dynamic_skills_changed`, `tengu_skills_dashboard_enabled` and six `tengu_skills_sync_*` variants **[binary]**. These go to Anthropic and an organisation cannot read them.

### The local usage counter nobody mentions

This is the finding with the lowest cost to act on, and it is not documented anywhere.

Claude Code keeps a persistent per-skill counter in `~/.claude.json` under the key `skillUsage` **[binary, confirmed measured]**:

```js
function Slt(e,t,r){ ... _e((g)=>{let k=g.skillUsage?.[t];
  return {...g, skillUsage:{...g.skillUsage,[t]:{usageCount:(k?.usageCount??0)+1,lastUsedAt:u}}}},r)}
```

Real content from this machine **[measured]**, 72 entries:

| Key | usageCount | lastUsedAt |
|---|---|---|
| `tdd` | 313 | 1788627189374 |
| `harness-fixture-catalogue:csharp-new-class` | 202 | 1788986374857 |
| `grill-with-docs` | 103 | 1788419546772 |
| `unslop` | 94 | 1789032643561 |
| `code-review` | 91 | 1789032693729 |

Plugin skills are keyed `<plugin>:<skill>`, bare skills by name. `lastUsedAt` is epoch milliseconds. The probe skills created for this research appear as `probe-local` count=3 and `probekit:probe-plugin` count=3 after three sessions **[measured]**, so the counter is live at 2.1.248 and counts both routes verbatim.

There is a sibling key `pluginUsage`, keyed `<plugin>@<marketplace>`, with `usageCount`, `lastUsedAt` and `lastUsedNumStartups`.

Four limits, all read from the implementation:

- **A 60-second write throttle per skill name** `[binary]`, `Ekn=60000`. Invoking the same skill twice inside a minute counts once. `usageCount` undercounts bursts.
- **It is a lifetime total, not a window.** Only `lastUsedAt` carries recency. Week-on-week adoption needs snapshots taken over time.
- **No project, session or user dimension.** One global counter per skill per machine.
- **It never leaves the machine.** Collecting it across a fleet is a build, albeit a very small one.

The binary carries its own warning about the sibling key, worth quoting because it saves an obvious mistake **[binary]**:

> `pluginUsage` entries are SEEDED with `lastUsedAt` = now on install/enable and at session-start backfill ... for a zero-count plugin it is just the seed time. `skillUsage` has no seeding: skill `lastUsedAt` is written only on real dispatch and stays trustworthy.

### The report already built on top of it

The bundle contains a rendered skill usage table with the columns name, source, context, **7d tokens**, **uses**, **last used**, plus groupings `unusedOwned`, `unusedFromPlugins`, `unusedFromMcp`, `unusedSynced`, `disusedPlugins` **[binary]**. Its help text, verbatim **[binary]**:

> context = this skill's one-line listing in the system prompt, included every turn
> (dash = not in the current listing, costs nothing; full SKILL.md loads only when it runs)
> 7d tokens = tokens attributed to the skill over the last 7 days of sessions on this machine

That is an unused-skill audit and a per-skill token cost, computed locally, for free. On 2.1.248 `/skills` is registered as `description:"List available skills"` **[binary]** and the report above ships as `/skill-doctor`, which the changelog dates to **2.1.261**: "Added `/skill-doctor` to show which loaded skills go unused and what they cost in context." The underlying `skillUsage` data is present and correct on 2.1.248 regardless.

It is gated. `"Skill usage reports are not available on this connection."` **[binary]** fires on some session types; which ones was not traced.

## 3. Session transcripts

Claude Code writes every session to `~/.claude/projects/<slugged-cwd>/<session-uuid>.jsonl`, one JSON object per line, in plaintext. Subagent sidechains go to `<session-uuid>/subagents/agent-<id>.jsonl`.

**A Skill tool call appears in the transcript with the skill name, unredacted.** One real record from this machine, trimmed of its `usage` block, otherwise verbatim **[measured]**:

```json
{"parentUuid":"c0890eb4-…","isSidechain":false,
 "message":{"model":"claude-opus-5","id":"msg_011CeeoAS9V237S6TtuRF1T7","type":"message",
   "role":"assistant",
   "content":[{"type":"tool_use","id":"toolu_01EAt7jnkYt1D63phjpVUBqL",
               "name":"Skill","input":{"skill":"grilling"},"caller":{"type":"direct"}}],
   "stop_reason":"tool_use"},
 "requestId":"req_011CeeoAQajFig821C2YHMgQ",
 "attributionSkill":"wayfinder",
 "type":"assistant","uuid":"c71df0d8-…","timestamp":"2026-09-02T14:48:23.182Z",
 "effort":"xhigh","session_id":"01096b39-…","userType":"external","entrypoint":"cli",
 "cwd":"C:\\Projects\\skills-marketplace","sessionId":"01096b39-…",
 "version":"2.1.248","gitBranch":"main"}
```

Two fields carry a skill name and they mean different things.

| Field | Path | Meaning |
|---|---|---|
| `skill` | `message.content[].input.skill` where `.name == "Skill"` | The skill being invoked. `<plugin>:<skill>` for plugin skills |
| `attributionSkill` | top level of the record | The skill that was already active when this request was made |

`attributionSkill` is the unredacted source of the `skill.name` OTel attribute. It survives in the transcript even for plugin skills that OTel redacts to `third-party`. Counting distinct `attributionSkill` values across a project's transcripts gives per-skill token and cost attribution that the OTel path will not give you for a plugin catalogue.

The record also carries `cwd`, `gitBranch`, `version`, `sessionId`, `timestamp` and `requestId`, so a scraper gets repository, branch and time dimensions the OTel events do not have.

A count across this machine's own history **[measured]**, `attributionSkill` occurrences in 30 files of one project:

```
481 "attributionSkill":"implement"
251 "attributionSkill":"wayfinder"
204 "attributionSkill":"unslop"
159 "attributionSkill":"prototype"
101 "attributionSkill":"code-review"
```

That is a working usage report produced by `grep` over files that already exist.

### Retention

Transcripts rotate. [Data usage](https://code.claude.com/docs/en/data-usage):

> Local caching: Claude Code clients store session transcripts locally in plaintext under `~/.claude/projects/` for 30 days by default to enable session resumption. Adjust the period with `cleanupPeriodDays`.

Transcripts of sessions last continued in Claude Desktop or Cowork are exempt from that limit by default. The sweep emits a `retention_sweep` event with `period_days`, `used_default`, `transcripts_deleted`, `transcripts_exempted_desktop`, `session_files_deleted` and `artifacts_deleted` **[binary]**, so a collector already sees when it runs.

Confirmed on this machine **[measured]**: `cleanupPeriodDays` is not set in `~/.claude/settings.json`, the oldest surviving transcript is dated 2026-08-13 and the newest 2026-09-11, a 29-day spread against a 30-day default. 514.1 MB across 1,287 files.

`cleanupPeriodDays` has a floor of 1. Setting 0 is rejected **[binary]**: "To keep transcripts for a long time, set a large number (e.g. 3650 for ~10 years). To disable transcript writes entirely, remove this setting and use the `--no-session-persistence` CLI flag."

So a transcript scraper must run inside the retention window or raise it first. Thirty days is the default budget.

## 4. Hooks as a counter

[hooks-as-guardrails.md](hooks-as-guardrails.md) covers which events exist and what a hook can change. The only new question here is the payload on a Skill call.

**`PreToolUse` and `PostToolUse` both fire with `matcher: "Skill"`, and the JSON carries the skill name.** Measured directly by running a session with a hook that appended its stdin to a file. Both records verbatim, one skill from `.claude/skills/` and one from a plugin **[measured]**:

```json
{"session_id":"e854d282-…",
 "transcript_path":"C:\\Users\\malco\\.claude\\projects\\…\\e854d282-….jsonl",
 "cwd":"…\\telemetry-probe\\proj","prompt_id":"3b0537fa-…",
 "permission_mode":"bypassPermissions","effort":{"level":"xhigh"},
 "hook_event_name":"PreToolUse","tool_name":"Skill",
 "tool_input":{"skill":"probe-local"},"tool_use_id":"toolu_0177MY213CpRRgevFDEhttcf"}
```

```json
{"session_id":"e854d282-…","transcript_path":"…","cwd":"…","prompt_id":"3b0537fa-…",
 "permission_mode":"bypassPermissions","effort":{"level":"xhigh"},
 "hook_event_name":"PostToolUse","tool_name":"Skill",
 "tool_input":{"skill":"probekit:probe-plugin"},
 "tool_response":{"success":true,"commandName":"probekit:probe-plugin"},
 "tool_use_id":"toolu_018dvswJtd46zGbmL6W7KnoM","duration_ms":4}
```

The field is **`.tool_input.skill`** on both events. `PostToolUse` adds `.tool_response.commandName` carrying the same string, plus `.tool_response.success` and `.duration_ms`.

**No redaction.** The plugin skill reads `probekit:probe-plugin` in the hook payload while the same invocation reads `third-party` on `claude_code.cost.usage` in the same session. A hook sees what the telemetry pipeline hides.

So the one-line counter is real:

```json
{ "hooks": { "PostToolUse": [ { "matcher": "Skill", "hooks": [
  { "type": "command",
    "command": "jq -r '[now|todate, .tool_input.skill, .cwd] | @csv' >> ~/.claude/skill-usage.csv" } ] } ] } }
```

Four properties make this the best DIY option:

- It runs whether the model wants it to or not, and it needs no API access, no collector and no plan tier.
- It is distributable. A plugin ships hooks in `hooks/hooks.json`, and managed policy settings ship them to a fleet that cannot switch them off.
- `PostToolUse` cannot block, so a broken counter cannot break a developer's session.
- It gets `duration_ms`, `cwd` and `transcript_path`, so usage can be cut by repository and joined back to the full transcript.

Two limits. `PostToolUse` fires whether the skill helped or not, so this counts invocations and never outcomes. And a hook is a process spawn on every Skill call, so keep the command cheap.

The `if` field narrows further. `if` is supported on `PreToolUse` and `PostToolUse`, using permission-rule syntax, so `"if": "Skill(acme-*)"` would scope the counter to one catalogue. **Not verified** for the `Skill` tool specifically; the mechanism is documented for `Bash` and was not exercised here.

## 5. Server-side and admin

### There is a per-skill analytics endpoint

`GET /v1/organizations/analytics/skills`, from [the Analytics API reference](https://platform.claude.com/docs/en/api/beta/organization/analytics/skills):

> Get per-skill usage for a given day, with cursor-based pagination. ... Available to organizations on a Claude Enterprise plan. Requires an API key with the `read:analytics` scope.

The fields that answer the question:

| Field | What it gives |
|---|---|
| `skill_name` | "Name of the skill" |
| `skill_display_name` | Resolved human-readable name where `skill_name` is an opaque id |
| `invocation_count` | "Total number of times this skill was invoked on the requested day ... the true '# of uses'" |
| `distinct_user_count` | Exact distinct users, recomputed over the window, never summed |
| `claude_code_metrics.distinct_session_skill_used_count` | Distinct Claude Code sessions. HLL approximate in range mode, typical error under 2% |
| `attributed_list_price` | Rate-card value of requests attributed to this skill, minor units |
| `estimated_overage_spend` | Allocated overage spend, same cost basis as the Cost and Usage API |

`group_by[]` accepts `product`, `rbac_group_id`, `user_id`. `filter[]` accepts `product`, `rbac_group_id`, `share_status`, `skill_name`, `user_id`. `product` takes `chat`, `claude_code`, `cowork`, `office_agent`. Date range mode via `starting_date` and `ending_date`, at most 366 days, nothing earlier than 2026-01-01, with roughly a one-day lag.

The counting rule is stated identically on every field and is the exact definition a catalogue wants:

> A skill counts as used only when it is explicitly activated, the model (or the user, via the skill's slash command) invokes it, reading its instructions into context as part of that activation. Skills that are merely installed or listed as available, or whose content reaches the context without an activation (preloaded, hook-injected, or read as a plain file), are not counted.

`GET /v1/organizations/analytics/plugins` is the sibling, per plugin, `claude_code` and `cowork` only, groupable the same three ways, with `install_count` and `invocation_count`.

### Whether your own catalogue resolves to a name

This is the question that decides whether the endpoint is useful, and the docs answer it directly. From `skill_display_name`:

> Human-readable display name for rows whose `skill_name` is an opaque skill id (user/organization skill types and plugin-delivered skills, user-defined names are withheld from the analytics pipeline). **Organization-shared skills and skills delivered by the organization's own plugins (its plugin marketplaces and its library) resolve**; plugin skill names are shown without their 'plugin:' prefix. ... Null for private (user-defined) skills and members' personal-plugin skills, those names are not disclosed to analytics-key holders.

And the plugins endpoint explains where the redacted rows land:

> The `plugin_name` value `third-party` is an aggregate bucket, not a plugin: it collects plugin activity, from either surface, for which the reporting client did not provide a plugin name, so an organization's own plugins can contribute both to their own named rows and to this bucket.

That is coherent with everything measured above. The client sends an opaque id or the literal `third-party`, and the server resolves it by joining against marketplaces the organisation has registered with Anthropic.

The load-bearing phrase is **the organisation's own plugin marketplaces**, which means a marketplace Anthropic knows about, which means the claude.ai organisation console route. A catalogue served from an internal git remote that Anthropic has never seen has nothing to join against. **Not verified**: no Enterprise plan was available to exercise the endpoint, and no test distinguished a console-registered marketplace from a purely private one. Treat "our private git catalogue resolves to readable names here" as unproven, and note that the route which does resolve is the same one that uploads the catalogue to Anthropic, as [mcp-and-plugins.md](../mcp-and-plugins.md) already records.

### Everything else the admin gets

`GET /v1/organizations/analytics/users` returns `claude_code_metrics` per user per day: `distinct_session_count`, `commit_count`, `pull_request_count`, `lines_of_code.added_count` and `.removed_count`, `artifacts_created_count`, and `tool_actions` accept/reject counts for `edit_tool`, `write_tool`, `multi_edit_tool` and `notebook_edit_tool`. No skill or plugin dimension on this endpoint.

`/usage_report` and `/cost_report` group by `model`, `product`, `context_window`, `inference_geo`, `speed`, `rbac_group_id`, `cost_type`, `token_type` and the Claude-tag dimensions. **No skill, plugin, agent or subagent dimension on either.** Refreshed roughly every four hours, not final until about 30 days after the usage date, 31-day maximum range.

So the granularity ladder is: per-user, per-model, per-product, per-RBAC-group everywhere; per-skill and per-plugin only on the two dedicated Enterprise endpoints; per-session nowhere except as a distinct count.

### What does not reach Anthropic

The repository already established that on a private git, seed or managed-settings route nothing about the catalogue reaches Anthropic, that a private plugin's name is redacted to `third-party` and that skills report `custom_skill`. This pass adds three things to that picture.

1. The redaction is not anonymisation. `plugin_id_hash` is an unkeyed SHA-256 with a published salt and was reversed here in one line of Node **[measured]**.
2. The first-party `tengu_skill_*` events carry `skill_name_hash` computed with the same salt, so skill names are pseudonymous to Anthropic rather than absent.
3. `user.email` and `organization.id` are default-on standard attributes on every OTel event **[measured]**, so the org's own collector receives identified data by default even where the Anthropic-bound path is redacted.

`DISABLE_TELEMETRY=1` stops the Anthropic-bound metrics. `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` stops that plus feature-flag evaluation. Neither affects the OTLP export to your own collector, which is configured separately.

## 6. The honest gap

### Today, with no new build

| Question | Mechanism | Where |
|---|---|---|
| How many times has each skill run on this machine, ever, and when last? | `skillUsage` in `~/.claude.json` | Local, per developer |
| Which loaded skills are unused, and what do they cost in listing context? | `/skill-doctor`, needs 2.1.261+ | Local, per developer |
| Which skill was invoked, when, in which repo, on which branch? | `grep` over `~/.claude/projects/**/*.jsonl` | Local, 30-day window |
| Which skill was active for a given request, unredacted? | `attributionSkill` in the transcript | Local, 30-day window |
| Sessions, commits, PRs, lines of code, edit accept and reject rates per user per day | `/v1/organizations/analytics/users` | Admin API |
| Invocations, distinct users, attributed spend per skill per day | `/v1/organizations/analytics/skills` | Admin API, **Enterprise only** |

### With a small build

| Question | Build |
|---|---|
| Skill X fired N times last week, across the fleet, by name | Collector plus `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_LOGS_EXPORTER=otlp`, **`OTEL_LOG_TOOL_DETAILS=1`**, then count `event.name = skill_activated` by `skill.name` |
| Did the model choose the skill, or did a human have to type it? | Same, grouped by `invocation_trigger` |
| A fleet-wide counter with no collector at all | A `PostToolUse` hook matching `Skill`, shipped through managed policy settings, appending `.tool_input.skill` to a file or an HTTP endpoint |
| Per-skill token and cost for a plugin catalogue, which OTel redacts | Scrape `attributionSkill` and the `usage` block out of the transcripts |
| Week-on-week adoption from the local counter | Snapshot `skillUsage` on a schedule, diff the snapshots |

The measured cost of the OTel option is one environment variable. Nothing else in this document buys as much for as little.

### Not available

- **No `claude_code.skill.count` metric.** Skill usage is an event and an attribution, never a counter. Anything counting skills is `count(event)` in your backend.
- **Per-skill cost and tokens for a plugin-delivered skill, over OTel.** `skill.name` is hard-coded to `third-party` on `cost.usage`, `token.usage`, `api_request`, `api_error` and `api_refusal`, and `OTEL_LOG_TOOL_DETAILS` does not lift it. Confirmed in the redaction function, which contains no escape hatch, and measured on the wire.
- **Any skill or plugin dimension on `/usage_report` or `/cost_report`.** Per-skill spend exists only on the Enterprise `/skills` endpoint, and it is explicitly "an estimate, not a billing number".
- **Whether a skill helped.** Nothing in any mechanism above measures outcome. Every signal counts invocations. The nearest proxies are `claude_code.code_edit_tool.decision` accept versus reject rates and the `/analytics/users` commit and PR counts, neither of which can be attributed to a skill. The existing [mcp-and-plugins.md](../mcp-and-plugins.md) note on Vercel's evals stands: measuring whether a skill helps still means running an eval, and `claude plugin eval` is the tool for that, not telemetry.
- **A non-invocation signal.** A skill whose description is loaded, read by the model and rejected produces nothing. The analytics docs say so explicitly: skills "merely installed or listed as available" are not counted. So the metric that would tell you a description is misfiring, listed but never chosen, does not exist. The closest available answer is a zero row in `/skill-doctor` or a missing key in `skillUsage`.
- **Per-session skill history, server side.** `distinct_session_skill_used_count` is a distinct count and HLL-approximate in range mode. Session-level detail exists only in the local transcripts.

### What was not verified

- The `/skills` and `/skill-doctor` report rendering. The command was not run interactively; its columns and help text are read from the bundle, and the underlying `skillUsage` data is measured.
- Which connection types trip "Skill usage reports are not available on this connection."
- Any Analytics API call. No Enterprise plan was available. Every field above is quoted from the reference.
- Whether a private git marketplace resolves in `skill_display_name`, as opposed to one registered through the claude.ai organisation console.
- `"if": "Skill(name-*)"` as a hook narrowing filter.
- Behaviour after 2.1.248. `OTEL_METRICS_INCLUDE_REPOSITORY` at 2.1.269 and `/skill-doctor` at 2.1.261 are the two known gaps; the same changelog-versus-binary method would find any others and was not run over everything else.

## Reproducing the measurements

The three measured sessions used a temporary project with two identical probe skills, one in `.claude/skills/` and one in a plugin installed from a local file marketplace, a `PostToolUse` hook dumping stdin, and a 40-line Node HTTP server on `127.0.0.1:4318` writing every OTLP `http/json` body to a file. The plugin and marketplace were uninstalled afterwards; `claude plugin list` confirms neither remains **[measured]**.
