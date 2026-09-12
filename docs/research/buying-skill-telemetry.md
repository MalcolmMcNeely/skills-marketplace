# Buy or build: products that count agent-skill usage

Researched 2026-09-11. This is the buy-versus-build sweep for one question: **which product on the market today tells an engineering org how often each individual agent skill fired, and whether it helped?** Three siblings complete the set. [what-to-measure.md](what-to-measure.md) is the decision document and the place to start, [skill-usage-telemetry.md](skill-usage-telemetry.md) measures what Claude Code itself emits, and [who-measures-skill-usage.md](who-measures-skill-usage.md) records what companies publish about their internal measurement. This one covers products you could buy or install.

Every claim carries a link to the primary source. Vendor marketing claims are labelled as marketing inline. Where a capability could not be confirmed from a vendor's own documentation, it says "not verified" rather than guessing. Star counts and repository dates were read from the GitHub API on 2026-09-11.

## The short answer

The expected answer was "nobody does this, you must build it". That answer is wrong. Per-skill counting is a solved, purchasable problem, because Claude Code emits a dedicated `claude_code.skill_activated` event and Anthropic ships a per-skill analytics endpoint. Seven products and six open-source tools reach it.

Counting them, with the field name each one uses:

| | Product | The field |
|---|---|---|
| 1 | Anthropic Claude Enterprise Analytics API | `skill_name`, `invocation_count` |
| 2 | Braintrust | `metadata.skill_name` |
| 3 | LangSmith | `metadata.ls_skill_name` |
| 4 | DX | Claude Code's OTel logs signal |
| 5 | Honeycomb | `gen_ai.skill.names`, name unverified |
| 6 | Sleuth Skills / `sx` | `asset_name` |
| 7 | Cursor, for a different agent | `skill_name`, `usage` |

What no product does is the second half. Every one of them counts invocations. Two, Braintrust and LangSmith, can attach a model-graded score to the trace an invocation produced. **None joins a named skill to an outcome measured in the repository** such as whether the PR merged or the change was reverted.

## The priority lead: it was Braintrust, not Braintree

The recalled speaker worked at **Braintrust** (braintrust.dev), the LLM eval and observability platform. The PayPal/Braintree reading was dropped.

**I could not locate the specific talk.** Braintrust runs a one-day user conference called [Trace](https://www.braintrust.dev/trace) and published a [keynote recap on 25 February 2026](https://www.braintrust.dev/blog/trace-keynote). That recap announces Brainstore, the Braintrust CLI, Topics, Loop and the Braintrust Gateway, and says nothing about agent-skill usage. The [TRACE 2026 keynote video](https://www.youtube.com/watch?v=VP6bfmQvbiA) exists but its description could not be read programmatically, so what is demonstrated on stage in it is not verified. Ankur Goyal's [AI Engineer speaker page](https://ai.engineer/speakers/ankur-goyal) lists three talks, all about evals, none about coding agents or skills.

Finding the video matters less than what the docs already settle. **The capability is shipped product, not a stage demo.** It is documented, the plugin is public on GitHub, and the field names are in open source. That is the question the owner actually needed answered, and the answer is yes.

## The fact that decides which products work

Three different pipelines can carry a skill name, and they redact differently. A private plugin marketplace is a **third-party** marketplace in Claude Code's taxonomy, so the redaction rules land directly on this repo. The sibling document covers what Claude Code emits in depth; the two lines below are reproduced because they decide whether a given product can see anything at all.

From [Monitoring usage](https://code.claude.com/docs/en/monitoring-usage), read 2026-09-11:

**The `claude_code.skill_activated` event.** "Logged when a skill is invoked, whether Claude calls it through the Skill tool or you run it as a `/` command."

> `skill.name`: Name of the skill. For user-defined and third-party plugin skills the value is the placeholder `"custom_skill"` unless `OTEL_LOG_TOOL_DETAILS=1`

It also carries `invocation_trigger` (`"user-slash"`, `"claude-proactive"`, `"nested-skill"`), `skill.source` (`"bundled"`, `"userSettings"`, `"projectSettings"`, `"plugin"`), `skill.kind`, and `plugin.name` plus `marketplace.name` under the same flag.

That event is the one to build on. `invocation_trigger` splits exactly along this repo's engine and entry-point line: `claude-proactive` is an engine the model chose, `user-slash` is an entry point a developer typed.

**The `claude_code.cost.usage` and `claude_code.token.usage` metric attribute.** Same name, different rule:

> `skill.name`: Skill active for the request, set by the Skill tool, a `/` command, or inherited by a spawned subagent. Built-in, bundled, user-defined, and official-marketplace plugin skill names appear verbatim. **Third-party plugin skill names are replaced with `"third-party"`.** Absent when no skill is active.

No `OTEL_LOG_TOOL_DETAILS` escape is documented for that attribute. `marketplace.name` on these metrics is "only emitted for official-marketplace plugins. Absent otherwise."

**The consequence.** For a private marketplace, per-skill counts come from the `skill_activated` **log** event with `OTEL_LOG_TOOL_DETAILS=1`. They do not come from the `skill.name` **metric** label, which collapses every skill in the catalogue into one `third-party` bucket. A product that reads only the metrics signal will show one row no matter how many skills ship. A product that reads the logs signal will show them all.

Two routes sidestep the question entirely. Braintrust reads Claude Code's hook payloads, not its OTel export, so no redaction applies. Sleuth's `sx` observes assets it installed itself. Both see real names with no flag.

## Anthropic's own two APIs

Anthropic ships two analytics APIs with two key types, and only one counts skills. [Analytics APIs](https://platform.claude.com/docs/en/manage-claude/analytics-api) is the page that tells them apart.

### Claude Code Analytics API: no skill dimension

`GET /v1/organizations/usage_report/claude_code`, documented at [Claude Code Analytics API](https://platform.claude.com/docs/en/manage-claude/claude-code-analytics-api). Admin API key, free, available to every organisation with Admin API access.

One record per user per day. The complete dimension and metric list:

- Dimensions: `date`, `actor` (`user_actor.email_address` or `api_actor.api_key_name`), `organization_id`, `customer_type`, `terminal_type`
- Core: `num_sessions`, `lines_of_code.added`, `lines_of_code.removed`, `commits_by_claude_code`, `pull_requests_by_claude_code`
- Tool actions: `edit_tool`, `multi_edit_tool`, `write_tool`, `notebook_edit_tool`, each with `accepted` and `rejected`
- Model breakdown: `model`, `tokens.input/output/cache_read/cache_creation`, `estimated_cost.amount`, `estimated_cost.currency`

There is no skill field, no plugin field, and no Skill tool in the tool-action list. The four tools it counts are the four that edit files. This API answers "which developer used Claude Code and did they keep its edits". It does not answer the question.

It is daily aggregate only, not real time, and covers only Claude Code on the Claude API. Bedrock, Vertex, Microsoft Foundry and Claude Platform on AWS are excluded.

This matters beyond Anthropic, because several vendors resell this feed and nothing else. Any product whose only Claude Code source is this endpoint is structurally incapable of per-skill reporting.

### Claude Enterprise Analytics API: this is the one

Analytics API key created by the primary owner at [claude.ai, Organization settings, API](https://claude.ai/admin-settings/api-access), scope `read:analytics`, Claude Enterprise plan only, data available from 2026-01-01 onwards. Reference: [Analytics API reference](https://platform.claude.com/docs/en/api/admin/analytics).

**`GET /v1/organizations/analytics/skills`** returns per-skill usage for a day or a date range, one row per skill.

| Field | What it is |
|---|---|
| `skill_name` | Name of the skill |
| `skill_display_name` | Human-readable name where `skill_name` is an opaque id |
| `invocation_count` | "Total number of times this skill was invoked on the requested day" |
| `distinct_user_count` | Distinct users who used the skill |
| `claude_code_metrics.distinct_session_skill_used_count` | Distinct Claude Code sessions in which the skill was used |
| `chat_metrics.distinct_conversation_skill_used_count` | Same, for claude.ai chat |
| `cowork_metrics`, `office_metrics` | Same, for Cowork and per Office product |
| `estimated_overage_spend` | Overage spend attributed to this skill, decimal string in cents |
| `attributed_list_price` | List-price value of the requests that involved this skill |
| `enable_count` | Distinct accounts that enabled the skill, claude.ai only |
| `share_status` | `private`, `organization` or `public`, claude.ai only |

`filter[]` supports `product`, `rbac_group_id`, `share_status`, `skill_name` and `user_id`, where `product` is one of `chat`, `claude_code`, `cowork`, `office_agent`. `group_by[]` supports `product`, `rbac_group_id` and `user_id`. So `filter[]=product:claude_code` with `group_by[]=rbac_group_id` gives per-skill, per-team Claude Code usage, ranked by `order_by` on a metric.

The counting rule is stated precisely, and it decides how a catalogue should be designed:

> A skill counts as used only when it is explicitly activated, the model (or the user, via the skill's slash command) invokes it, reading its instructions into context as part of that activation. Skills that are merely installed or listed as available, or whose content reaches the context without an activation (preloaded, hook-injected, or read as a plain file), are not counted.

Both halves of a `disable-model-invocation` catalogue survive that rule. A model-invoked engine counts because the model invokes it; a typed entry point counts because slash-command activation counts. Content injected by a hook does not count, and neither does a skill that is merely listed.

**The server-side pipeline does not apply the client-side redaction.** `skill_display_name` resolves for "organization-shared skills and skills delivered by the organization's own plugins (its plugin marketplaces and its library)", with the `plugin:` prefix stripped. It is null for private user-defined skills, for members' personal-plugin skills, and for Anthropic-provided plugin skills. A skill shipped through the org's own marketplace resolves by name here, even though the same skill is redacted to `third-party` on the OTel cost metric. The two pipelines disagree, and the Enterprise one is the friendlier of the two for a private catalogue.

**`GET /v1/organizations/analytics/plugins`** is the marketplace-level companion: `plugin_name`, `plugin_id` (for example `serena@claude-plugins-official`), `install_count`, `invocation_count`, `distinct_user_count`, and `claude_code_metrics.distinct_session_plugin_used_count`. `install_count` is the adoption number a marketplace owner wants. Two catches: `plugin_id` is "null for third-party Claude Code plugins (redacted at the source)", and a `third-party` row is an aggregate bucket for activity where the client sent no plugin name, so an org's own plugin can contribute to both its named row and that bucket.

Freshness is a 1-day lag, typically available from about 17:00 UTC the following day, with values revised by a few percent afterwards. The rate limit is 60 requests per minute per organisation. Claude Code used through Amazon Bedrock is not returned.

There is no quality score anywhere in this API. It gives count and cost per skill. Whether the skill helped is not a question it answers.

### The in-product dashboard

The [Analytics dashboard](https://claude.ai/analytics/activity) in claude.ai is the UI over the same Enterprise data. Anthropic's [announcement of 2 July 2026](https://claude.com/blog/giving-admins-more-visibility-and-control-over-claude-usage-and-spend) says "Skills report their own usage and cost, and new endpoints track plugin adoption and artifact creation", and describes usage and cost by group and by user with "skills and connectors used displayed directly next to their cost". The productivity-lift and annual-value figures on that page are marketing, not documented metric definitions.

For the Claude Platform side, the [Claude Code analytics dashboard](https://platform.claude.com/claude-code) in the Console shows the Claude Code Analytics API data, which as established above has no skill dimension.

## Braintrust

### What it ingests

Braintrust takes Claude Code sessions through a Claude Code plugin, announced [23 December 2025](https://www.braintrust.dev/blog/claude-code-braintrust-integration) and documented at [Integrations, developer tools, Claude Code](https://www.braintrust.dev/docs/integrations/developer-tools/claude-code).

```
claude plugin marketplace add braintrustdata/braintrust-claude-plugin
bt trace enable claude --project <your-project>
```

The plugin is [braintrustdata/braintrust-claude-plugin](https://github.com/braintrustdata/braintrust-claude-plugin), 20 stars, created 2025-12-20, last pushed 2026-09-02. It is generated from [braintrustdata/braintrust-coding-agent-plugins](https://github.com/braintrustdata/braintrust-coding-agent-plugins), which also carries plugins for Codex, Grok, OpenCode, pi and Google Antigravity. That monorepo's `LICENSE` file currently reads `TODO Apache 2.0 (placeholder)`, so the licence is not actually set.

The mechanism decides what can be counted. Braintrust does not proxy Claude Code and does not read its OTel export. The [plugin README](https://github.com/braintrustdata/braintrust-claude-plugin) says it "contains only a fail-open hook forwarder"; every registered lifecycle event is forwarded to `bt trace hook --source claude-code`, and a background Rust daemon builds the spans and delivers them. Reading hook payloads is why the third-party redaction does not apply.

There is a second, older route. Braintrust publishes a knowledge-base article for [Claude Code with OTel tracing](https://www.braintrust.dev/docs/kb/setup-claude-code-with-otel-tracing-1781641599984), pointing `OTEL_EXPORTER_OTLP_ENDPOINT` at `https://api.braintrust.dev/otel`. Braintrust's own guidance is to prefer the plugin and use OTel only when plugins cannot be installed.

### Whether a span carries a skill name

Yes, and the docs say so before the source proves it. The docs page lists what a Claude Code trace holds:

> Session spans with the session ID, workspace, hostname, username, operating system, Claude Code version, model, and Git repository metadata. Turn spans with prompts and final responses. Model call spans with prompts, completions, token metrics, and errors. Tool spans with inputs, outputs, approval state, and tool names. **Skill metadata when a turn loads skills.** Subagent spans nested under the turn that started the subagent.

The exact field names are in the open-source translator, `bt-daemon/src/translate/claude.rs` on `main`, read 2026-09-11:

| Where | Field | Value |
|---|---|---|
| Tool span, when `tool_name == "Skill"` | `metadata.tool_kind` | `"skill"` |
| | `metadata.skill_name` | the invoked skill's name |
| | `metadata.skill_load_trigger` | `"explicit"` |
| | span name | `skill: <name>` |
| Turn span | `metadata.loaded_skill_names` | array of names loaded during the turn |
| | `metadata.loaded_skills` | array of `{ "name": ... }` |

The turn-level capture comes from the `UserPromptExpansion` hook, so a skill the developer types as a slash command is recorded too, not only one the model chose. The daemon cross-checks the typed name against the session transcript's skill listing before accepting it.

### Whether the UI groups by it

Yes. [Build charts](https://www.braintrust.dev/docs/observe/dashboards/build-charts) documents a **Group by** option that splits a chart "by a SQL dimension, such as `metadata.model`", and a **Top list** chart type that ranks groups by a metric over a timeframe. `metadata.skill_name` is the same shape of dimension as `metadata.model`, so a top-list of skills by invocation count is a chart you configure, not a feature you wait for. The same data is reachable as SQL: [query structure](https://www.braintrust.dev/docs/reference/sql/query-structure) documents `SELECT metadata.model ... GROUP BY` over `project_logs()`, callable from the REST API or the Braintrust MCP server's `sql_query` tool.

One constraint sets the price. Per [Plans and limits](https://www.braintrust.dev/docs/plans-and-limits), custom dashboards and custom charts are Pro and Enterprise only. The free Starter tier gets the built-in read-only "Cost and quality" dashboard, which carries no skill dimension. Counting skills on a chart therefore starts at Pro.

### Whether it closes the loop to "did it help"

This is the part no other product in the sweep matches. [Score production traces](https://www.braintrust.dev/docs/evaluate/score-online) documents online scoring: scorers run asynchronously against production traces as they are logged, write a score span into the trace, and are configured per project as automation rules with a `sampling_rate` and a trace, span or group scope. Rules can be created in the UI or via `POST /v1/project_score`.

Applied here, a scorer attached to spans where `metadata.skill_name` is set gives a quality number per skill, sitting in the same data as the invocation count. That is closer to the owner's actual question than anything else on the market. It is still a model judging a transcript, not an outcome measured in the repository.

[Loop](https://www.braintrust.dev/docs/loop) still exists under that name and generates charts from natural language. [Topics](https://www.braintrust.dev/docs/observe/topics) clusters traces automatically.

### Price and deployment

From [Plans and limits](https://www.braintrust.dev/docs/plans-and-limits) and the [pricing page](https://www.braintrust.dev/pricing):

| Plan | Platform fee | Processed data | Scores | Retention | Custom dashboards |
|---|---|---|---|---|---|
| Starter | $0 | 1 GB/mo, then $4/GB | 10k/mo, then $2.50/1k | 14 days | No |
| Pro | $249/mo | 5 GB/mo, then $3/GB | 50k/mo, then $1.50/1k | 30 days | Yes |
| Enterprise | Custom | Custom | Custom | Custom | Yes |

[Self-hosting](https://www.braintrust.dev/docs/admin/self-hosting/index) is Enterprise only, and splits a customer-run data plane from a Braintrust-run control plane. BYOC is the middle option.

### Shipped, demo, or internal?

Shipped, and a customer can buy it today. The evidence is not a screenshot: it is a public GitHub repository, a documented install command, a documented span schema, and field names in open source. What is not verified is whether the specific dashboard seen on stage was assembled from these fields or was internal Braintrust tooling. It does not change the answer, because the fields are public and the chart is user-configurable.

Two gaps to plan around. The plugin does not support the Cowork tab, which runs hooks inside a separate VM without the host's `bt` installation. And coverage is opt-in per developer machine: a developer who never runs `bt trace enable claude` contributes nothing, so Braintrust measures the sessions that enrolled, not the org. Managed settings can push the plugin, but the `bt` login is per machine.

## The other LLM observability platforms

Ten platforms were checked. One pattern runs through all of them: **every vendor's documented Claude Code path is its own plugin, hook or proxy, not `CLAUDE_CODE_ENABLE_TELEMETRY`.** Honeycomb is the only one whose docs tell you to use Claude Code's own OTLP exporter. So each vendor has its own idea of what a skill is, and nothing validated on one path transfers to another.

### LangSmith: yes, and the field name is published

LangChain ships [langchain-ai/langsmith-claude-code-plugins](https://github.com/langchain-ai/langsmith-claude-code-plugins), 67 stars, MIT, last pushed 2026-09-11. Its README states it plainly:

> Tool runs include the tool name, inputs, and output content. Skill tool runs additionally set `ls_skill_name`, the invoked skill's name, read from the tool's `skill` input, so per-skill usage is queryable in run stats.

That field is queryable because [LangSmith dashboards](https://docs.langchain.com/langsmith/dashboards) document custom-chart group-by dimensions as "Run Name, Run Type, Tag, Project, Metadata (with a path such as `metadata.ls_model_name`), and Feedback Label", ranked by frequency and capped at the top 20, one group-by per chart. So `metadata.ls_skill_name` is a chartable dimension.

Worth noting that the field appears in the plugin README but not on the [docs page](https://docs.langchain.com/langsmith/trace-claude-code), which lists "user messages, tool calls, compaction, subagent runs, and assistant responses" and does not mention skills. The README is the vendor's own repository, so it counts as a primary source, but the two disagree.

[Online evaluations](https://docs.langchain.com/langsmith/online-evaluations) give real-time LLM-as-judge feedback on production traces, filterable by metadata, with a sampling rate and a weekly spend limit. Combined with `ls_skill_name` that is the same "count plus score per skill" shape Braintrust offers, and like Braintrust the combination is not documented anywhere. Pricing: Developer $0 with 1 seat and 5k base traces a month, Plus $39 per seat per month, Enterprise custom.

### Honeycomb: documented, but the attribute name does not match

Honeycomb is the only vendor whose docs name a skill attribute and tell you to group by it. From [Honeycomb MCP use cases](https://docs.honeycomb.io/integrations/mcp/use-cases), verbatim:

> Group token usage by `gen_ai.request.model` or `gen_ai.skill.names` to see which models are doing the work and which skills are loading most frequently.

The same page gives the Claude Code setup, and it is the OTel one: `CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1` to enable the traces beta, with `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at Honeycomb. Honeycomb's design claim is that "a user may group or filter on any attribute, no matter how high its cardinality", with no pre-indexing, so grouping is genuinely unconstrained. Graphs cap at the top 50 groups.

**The attribute name is wrong, or at least undocumented.** Claude Code's monitoring reference uses exactly six `gen_ai.*` attributes: `gen_ai.request.attempt`, `gen_ai.request.model`, `gen_ai.response.finish_reasons`, `gen_ai.response.id`, `gen_ai.system` and `gen_ai.tool.call.id`. `gen_ai.skill.names` is not among them. The documented spellings are `skill_name` on the `claude_code.tool` span, gated on `OTEL_LOG_TOOL_DETAILS`, and `skill.name` on the metrics and events. Whether Honeycomb's plural name is a third spelling, a rename, or an error is not verified and would take twenty minutes against a real endpoint to settle.

Honeycomb has no evaluation feature at all. It can show that a skill fired and that the tool call succeeded via `tool.outcome`, and nothing scores whether the outcome was good. Free up to 20M events a month, Pro from $150 a month up to 750M events, Enterprise variable. Honeycomb's own blog post on measuring Claude Code adoption is marketing, and notably builds its boards on `tool_name`, `user.account_uuid`, `model` and `success`, never on a skill name.

### The rest

| Product | Claude Code path | Per-skill | Notes | Price |
|---|---|---|---|---|
| [Datadog LLM Observability](https://docs.datadoghq.com/llm_observability/monitoring/metrics/) | [Agent Console](https://docs.datadoghq.com/ai_agents_console/setup/) sets `OTEL_LOGS_EXPORTER` and `OTEL_METRICS_EXPORTER`, no traces exporter | Yes, but you build it | Built-in metrics carry a fixed tag set, and the docs say "Other tags set on spans are not available as tags on Agent Observability metrics." You define a span-based metric yourself | $160/mo annual for 100k LLM spans, then $3.50 per 10k |
| [Arize AX](https://arize.com/docs/ax/integrations/platforms/claude-code/claude-code-tracing) | Hook-based, 16 Claude Code hook events to OpenInference spans | Not verified | Documented attributes are `session_id`, `user.id`, `agent_id`, tool names and arguments. No skill attribute documented. Widgets group by attribute paths, but the docs never say the dimension picker accepts any key, and [custom metrics](https://arize.com/docs/ax/observe/projects/custom-metrics-api) explicitly have no `GROUP BY` | Free 25k spans/mo, Pro $50/mo for 50k |
| [Arize Phoenix](https://arize.com/docs/phoenix/integrations/coding-agents/claude-code) | Same hook plugin | No | Arbitrary attributes are fully filterable but there is no documented way to put one on a chart axis. The project metrics dashboard is fixed. No online eval either | Free, self-hosted |
| [W&B Weave](https://docs.wandb.ai/weave/guides/integrations/claude_code) | Plugin using GenAI conventions, spans `invoke_agent claude-code`, `execute_tool <tool_name>` | Not verified | No skill attribute documented. The [Agents view](https://docs.wandb.ai/weave/guides/tracking/view-agent-activity) breaks down "top tools by usage", which for Claude Code is one bar for the `Skill` tool. [Monitors](https://docs.wandb.ai/weave/guides/evaluation/monitors) score root traces only, not intermediate spans | Free 1 GB/mo, Pro from $60/mo |
| [Langfuse](https://langfuse.com/integrations/developer-tools/claude-code) | Hook-based, plus a [Claude Code plugin marketplace](https://github.com/langfuse/Claude-Observability-Plugin) | No, not without remapping | The blocker is documented: unmapped OTel attributes land under `metadata.attributes`, and "Langfuse only supports filtering on top-level keys within the `metadata` of an event". A raw `skill_name` arrives unqueryable. A Collector transform would fix it | Free Hobby, Core $29, Pro $199, Enterprise $2,499/mo; self-host free |
| [New Relic AI Monitoring](https://docs.newrelic.com/docs/ai-monitoring/intro-to-ai-monitoring/) | None found | Not verified | AI Monitoring proper requires an APM agent and does not mention OpenTelemetry. OTel GenAI data lands as generic `Span` events queried with NRQL, outside the AI Monitoring UI. `FACET skill_name` should work in principle; no vendor example does it | 100 GB/mo free then $0.40/GB; Pro $349/user/mo |
| [Helicone](https://docs.helicone.ai/integrations/anthropic/claude-code) | A proxy on `ANTHROPIC_BASE_URL`, and the page says the method "is maintained but no longer actively developed" | No | A proxy sees HTTP calls to the Anthropic API, never Claude Code's spans, so a skill name cannot reach it. Its grouping machinery works on `Helicone-Property-*` headers, which nothing populates with a skill | Hobby $0, Pro $79, Team $799/mo |
| OpenLLMetry | None, wrong layer | n/a | An SDK you embed in your own application. Claude Code already exports its own OTLP | Free |
| [Traceloop](https://www.traceloop.com/docs/openllmetry/integrations/traceloop) | None found | Not verified | Whether it accepts generic OTLP rather than only OpenLLMetry is not verified. Free tier has 24-hour retention, which rules out "how often did each skill fire this quarter" | Free 50k spans/mo, Enterprise custom |

**Prompt registries are a dead end across the board.** Eight of these platforms have one: Braintrust, LangSmith, Langfuse, Phoenix, Arize AX Prompt Hub, Weave, Helicone and Traceloop. Every one does versioning, tagging and deployment. **Not one publishes per-prompt usage counts.** If the mental model for measuring a skill catalogue is "the prompt registry will tell me which prompts get used", it will not.

## Engineering-intelligence platforms

Two of these reach per-skill. The rest stop at per developer per day, and several resell the Claude Code Analytics API, which as established above cannot go lower.

### DX: yes, through the OTel logs signal

DX publishes three Claude Code connectors, the most thorough coverage of any vendor here.

- [Claude Code (OTel)](https://docs.getdx.com/connectors/claude-code-otel/): push-based OTLP ingest at `POST /api/otel/v1/metrics` and `POST /api/otel/v1/logs`, with a DX-issued ingest token and a managed-settings JSON. Attribution via `OTEL_RESOURCE_ATTRIBUTES: "user.email=...,user.name=..."`. Works with Bedrock and Vertex, where no analytics API exists.
- [Claude Code (Anthropic Console)](https://docs.getdx.com/connectors/claude-code/): pulls `GET /v1/organizations/usage_report/claude_code` and `GET /v1/organizations/users`.
- [Claude Code (Enterprise)](https://docs.getdx.com/connectors/claude-code-enterprise/): pulls `/v1/organizations/analytics/users`, `/user_usage_report` and `/user_cost_report`. Data from 1 January 2026, three-day delay.

The OTel connector states it plainly: "DX can track which Claude Code skills your developers use, and how often, by capturing skill data through the OTel **logs** signal", gated on `OTEL_LOG_TOOL_DETAILS: "1"`. Its endpoint table labels `/api/otel/v1/logs` as "Logs (skill usage events)".

That is the right signal and the right flag. Reading the logs signal with that flag lifts the `custom_skill` placeholder, so a private marketplace's skill names do arrive. DX's documentation does not discuss the redaction rules at all, so how it handles the `third-party` metric label is not verified, but the route it describes is the one that works.

For "did it help", DX is survey-led. The [AI Measurement Framework](https://getdx.com/blog/ai-measurement-framework-guide/) sits alongside [DX Core 4](https://docs.getdx.com/dx-core-4/), and the AI Impact report covers PR Throughput, Change Failure Rate, the Developer Experience Index and Percentage of Time Spent on Feature Development. AI-user versus non-user cohort comparison and PR revert rates as a quality signal are marketing claims from the launch blog. All of it attaches to a developer or a PR, never to a named skill.

Price is not published. Contact sales.

### Sleuth Skills and `sx`: yes, and it sidesteps telemetry entirely

Sleuth has moved off DORA into **Sleuth Skills**, a control plane for AI assets: skills, rules, agents, commands, hooks, MCP servers and Claude Code plugins. The distribution mechanism is an open-source CLI, [`sx`](https://github.com/sleuth-io/sx), Apache 2.0, 302 stars, created 2025-12-16, last pushed 2026-09-09.

Because `sx` installs the asset, it observes the asset running, so it depends on neither Anthropic's OTel export nor its analytics API and is subject to neither redaction rule. From [Usage metrics](https://help.sleuth.io/sleuth-skills/govern/metrics.md):

> `sx` records a usage event every time it installs an asset. Clients that integrate with `sx` (Claude Code, Cursor, and others per the compatibility matrix) emit events when an installed asset runs, a skill loading in a session, a command being invoked, an MCP tool call, a hook firing.

The event shape, verbatim:

```json
{
  "ts": "2026-04-17T10:04:12.445Z",
  "actor": "alice@acme.com",
  "asset_name": "code-reviewer",
  "asset_version": "1.2.3",
  "asset_type": "skill"
}
```

Asset-specific payloads add `tool_name`, `duration_ms` and `success`. Four prebuilt dashboards: AI Metrics, Adoption, Usage, Leaderboards. "Top assets by usage" is "raw invocation count per asset"; "Top assets by token cost" is "cumulative tokens consumed, useful for cost attribution". Adoption is broken out per user, per team and per repository. Private git-backed vaults work with normal git credentials.

Per skill, per version, per actor, with token cost attribution, for a private catalogue. The `success` and `duration_ms` fields are execution facts, not outcome facts, so this counts and costs but does not judge.

`sx` is free and Apache 2.0. The hosted Sleuth Skills product is "Custom", request a demo.

### The rest: no

| Vendor | What it reads | Why it cannot reach a skill |
|---|---|---|
| [Swarmia](https://help.swarmia.com/settings/integrations/ai-coding-tool-integrations/claude-code-integration) | Claude Code Analytics API plus [OTel](https://www.swarmia.com/changelog/2026-03-13-claude-code-otel/) | Consumes the aggregate metrics, not the tool-details logs. Skills, slash commands and subagents appear nowhere in its docs |
| [Faros AI](https://www.faros.ai/blog/claude-code-analytics) | Analytics APIs, OTel, git, 60+ sources | Passes the Analytics API schema through: daily, per user, acceptance rates by tool type |
| [Jellyfish](https://jellyfish.co/platform/claude-code-dashboard/) | git, planning systems, CI/CD, AI tool usage | Derives AI signals from git and workflow data, which is exactly why it cannot see a skill. Granularity stated at team, repo and org |
| [LinearB](https://linearb.helpdocs.io/article/b1lep7a7cr-configuring-claude-code-open-telemetry) | Claude Code OTel, Enterprise Analytics API, git | 20 AI metrics across adoption, throughput, delivery and quality, drilled down by team, repo and tool. No skill dimension documented |
| [Harness SEI](https://www.harness.io/products/software-engineering-insights) | GitHub Copilot and Gemini Code Assist cohorts | No Claude Code integration found; several doc URLs 404 mid-reorganisation. Not verified |
| [Opsera](https://opsera.ai/solutions/copilot-dashboard/) | Copilot and Windsurf dashboards | Its Claude Code presence is a plugin pointing the other way, reporting "only anonymous usage metadata" |
| Pluralsight Flow | git and code review | Acquired by Appfire and being retired. Do not evaluate |
| Code Climate Velocity | git | Discontinued as a self-serve product. Do not evaluate |
| [Uplevel](https://resources.uplevelteam.com/gen-ai-for-coding) | git, calendar, Copilot | A research house in this lane. Its 800-developer Copilot study is a useful prior on effect sizes, not a product |
| Atlassian Compass | git, Jira | Nothing published on AI tool telemetry. Not verified |

Swarmia and LinearB are the only two with published prices. Swarmia is free under 10 developers, then $45 per developer per month Standard and $55 Enterprise billed annually, with an AI adoption and cost add-on at $5 per developer per month. LinearB is $29 per user per month with a 50-developer minimum, or $59 with a 100-developer minimum. Everyone else in the table is contact-sales, and that is itself a finding when the question is whether this is worth buying.

## Open source

Six tools count named skills. The two routes have different failure modes.

| Repo | Stars | Data source | What it counts | Licence | Last push |
|---|---|---|---|---|---|
| [PackmindHub/skillsight](https://github.com/PackmindHub/skillsight) | 9 | OTLP `skill_activated`, or pulled from Loki | Named skill activations split by `invocation_trigger` and user, in a skill/plugin/marketplace model, with triage states | Apache-2.0 | 2026-06-16 |
| [getagentseal/codeburn](https://github.com/getagentseal/codeburn) | 10,966 | JSONL transcripts, 37 other agents too | Cost, tokens, cache, per-tool calls, per-named-skill turns and cost | MIT | 2026-09-11 |
| [Arindam200/cc-lens](https://github.com/Arindam200/cc-lens) | 597 | JSONL transcripts | Sessions, tokens, cost, tool rankings, `skill_counts` per named skill, MCP servers | MIT | 2026-06-27 |
| [neeltom92/claude-code-observability](https://github.com/neeltom92/claude-code-observability) | 3 | OTel Collector to Prometheus and Loki to Grafana, docker-compose | A dedicated Skills Usage dashboard: invocations by name, by trigger, tokens and cost by skill | Apache-2.0 | 2026-06-22 |
| [KubeRocketCI/claude-code-telemetry](https://github.com/KubeRocketCI/claude-code-telemetry) | 1 | Same, plus Helm | Tokens and cost by model, agent, skill, tool, command; a panel titled "Skill activations (real names, incl. project skills)" | Apache-2.0 | 2026-07-28 |
| [elastic/integrations](https://github.com/elastic/integrations) `packages/claude_code` | 333 repo-wide | OTLP logs into Elasticsearch | Typed ECS mappings for every event including `skill.name`, `skill.source`, `invocation_trigger` | NOASSERTION | 2026-09-11 |
| [ccusage/ccusage](https://github.com/ccusage/ccusage) | 18,498 | JSONL transcripts | Tokens and cost only. No tool counts, no skill counts | MIT in LICENSE, NOASSERTION via API | 2026-09-11 |
| [ColeMurray/claude-code-otel](https://github.com/ColeMurray/claude-code-otel) | 495 | OTel to Prometheus, Loki, Grafana | Per-tool usage frequency and success rates. No skills; last push 2025-06-17, predates `skill_activated` | MIT | 2025-06-17 |
| [foyzulkarim/claude-lens](https://github.com/foyzulkarim/claude-lens) | 245 | JSONL, tailed | Tokens, cost, cache, tool calls, sessions | MIT | 2026-08-05 |
| [Maciek-roboblog/Claude-Code-Usage-Monitor](https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor) | 8,699 | JSONL plus local config | Tokens, cost, message count, burn rate. No tools, no skills | MIT | 2026-07-05 |

**Skillsight is the only one built for this exact question.** Its README states the problem in the words a marketplace owner would use: "Claude Code emits an OpenTelemetry event for every skill activation, but it ships **no dashboard for the team rolling skills out**. Generic observability stacks ingest those events fine, but they don't understand the skill to plugin to marketplace model." It ingests OTLP directly or pulls from an existing Loki, correlates against private git-backed marketplaces, and triages skills through `to_review` to `approved`, `removed` or `denied`. Self-hosted, Postgres, one `docker compose up`, no outbound telemetry. It is also nine stars and four months old with one maintainer.

**neeltom92** and **KubeRocketCI** are copyable Grafana dashboards rather than products. Both are Apache-2.0. neeltom92 queries `sort_desc(sum by (skill_name) (count_over_time({service_name=...})))` against Loki for invocations by name and `sum by (invocation_trigger) (...)` for activations by trigger, and its settings patch sets `OTEL_LOG_TOOL_DETAILS=1`. KubeRocketCI is the more deployable of the two, with Helm, namespace-scoped RBAC and external secrets, at one star.

**cc-lens and codeburn take the other route**, parsing `~/.claude/projects/**/*.jsonl` for `tool_use` blocks where `name === "Skill"` and reading `input.skill`. No flag, no collector, no redaction, because the transcript carries the real name regardless of provenance. The cost is that this is per machine, not per fleet, and it misses `/` command invocations that never route through the Skill tool. Note that codeburn ships opt-in anonymous telemetry of its own that transmits skill names with bucketed call counts; the README documents it and it can be switched off.

**ccusage is not the answer**, despite being the best-known tool in the space at 18,498 stars. It reports tokens and cost per day, week, month and session. Its README does not mention tool-call or skill counting.

**There is no official Anthropic reference implementation.** No monitoring, otel, dashboard or observability repository exists under `anthropics` or `anthropic-experimental`. Anthropic publishes the documentation page and nothing else. The nearest vendor-published equivalents are the Elastic integration above and [SigNoz's Claude Code monitoring docs](https://signoz.io/docs/claude-code-monitoring/), which document `skill_activated` and the `skill.name` cost breakdown but ship no open-source dashboard repository.

## Competing agent platforms, for comparison

Fourteen coding-agent vendors were checked against their own API references. **One reports per named artefact: Cursor.** Everywhere else the finest dimension is a product surface, a language, a model, an IDE, or a user.

### Cursor: yes, and its schema is worth copying

The [Analytics API](https://cursor.com/docs/account/teams/analytics-api), separate from the Admin API, has a section headed "Skills Adoption" at `/analytics/team/skills`. Its own description: "Get metrics on Skills adoption across your team. Returns daily adoption counts broken down by skill name." The documented response, verbatim:

```json
{ "event_date": "2025-01-15", "skill_name": "react-best-practices", "usage": 53 }
```

`/analytics/by-user/skills` mirrors it. Two adjacent endpoints do the same job for other artefacts: `/analytics/team/commands` returns `event_date`, `command_name`, `usage`, and `/analytics/team/mcp` returns `event_date`, `tool_name`, `mcp_server_name`, `usage`. Cursor [Skills](https://cursor.com/docs/skills) are `SKILL.md` files loaded from `.agents/skills/` and `.cursor/skills/`, the same artefact format this catalogue ships.

Three caveats. The docs never say whether `usage` counts invocations or unique users, which is exactly the ambiguity to settle before copying the schema. There is no per-rule breakdown for `.cursor/rules` and no per-custom-mode. And it is API only: the [team analytics dashboard](https://cursor.com/docs/account/teams/analytics) does not surface skills. Availability is stated on the endpoint as "Only for enterprise teams"; Teams is $40 per seat per month, Enterprise is custom.

### GitHub Copilot: no, and the enum proves it

The [Copilot metrics API](https://docs.github.com/en/rest/copilot/copilot-metrics) breaks down by `totals_by_ide`, `totals_by_feature`, `totals_by_language_feature`, `totals_by_model_feature`, `totals_by_language_model` and `totals_by_3rd_party_agent`, over dimensions `ide`, `feature`, `language`, `model`, `phase`.

The complete `feature` enum is `code_completion`, `chat_inline`, `chat_panel_ask_mode`, `chat_panel_edit_mode`, `chat_panel_agent_mode`, `chat_panel_plan_mode`, `chat_panel_custom_mode`, `chat_panel_unknown_mode`, `agent_edit`, `copilot_cli`, `copilot_app`, `others`. Every custom agent in an organisation collapses into `chat_panel_custom_mode`. Nothing names a `.github/prompts/*.prompt.md` file, a `copilot-instructions.md`, or a `.github/agents/*.md`. `totals_by_3rd_party_agent` carries `agent_id` and `agent_name`, but the [example schema](https://docs.github.com/en/copilot/reference/copilot-usage-metrics/example-schema) values are `"Claude (Anthropic)"` and `"Codex (OpenAI)"`, so it names the vendor agent app, not a repository artefact.

GitHub's own documented workaround is the clearest admission in the sweep: "To monitor the usage of custom agents in your organization, filter your organization's audit log by `actor:Copilot`." ([source](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/test-custom-agents)) That returns events, not counts per agent name.

Copilot Business is $19 per seat per month, Enterprise $39.

### The rest

| Product | Finest granularity | Per named artefact | Notes |
|---|---|---|---|
| [Windsurf](https://docs.devin.ai/desktop/accounts/analytics) | `language`, `ide`, `version`, `model_id`, `tool` | No | `cascade_tool_usage.tool` holds built-in ids such as `CODE_ACTION`, not workflow or rule names. Enterprise only |
| [Sourcegraph Amp](https://ampcode.com/security) | Not verified | Not verified | No analytics or metrics page in the [manual](https://ampcode.com/manual) or [docs](https://ampcode.com/docs). Only a "data management API" framed for security scanning. `ampcode.com/api-docs` is behind sign-in |
| [Devin](https://docs.devin.ai/api-reference/v2/consumption/usage-metrics) | `sessions_count`, `searches_count`, `prs_opened/closed/merged` | Partial | [Session Insights](https://docs.devin.ai/product-guides/session-insights) has a Knowledge Usage tab splitting "Useful Knowledge" from "Misleading Knowledge" by item. Per session only, no aggregation, no API, no counts. Playbooks get nothing |
| [Gemini Code Assist](https://docs.cloud.google.com/gemini/docs/codeassist/monitor-gemini-code-assist) | `client_name`, `programming_language`, `initiation_method`, `method`, `product` | No | `.gemini/styleguide.md` lives on the GitHub review surface, which Google's own [logging page](https://docs.cloud.google.com/gemini/docs/log-gemini) says "doesn't support logging with Cloud Logging". Not merely unreported, unlogged |
| [Amazon Q Developer](https://docs.aws.amazon.com/amazonq/latest/qdeveloper-ug/dashboard.html) | `ProgrammingLanguage`, `SuggestionState`, `customizationArn` | No | `customizationArn` names an artefact but it is a fine-tuned model, not an authored instruction file. Pro, $19/mo/user |
| [Kiro](https://kiro.dev/docs/enterprise/monitor-and-track/dashboard/) | `Client_Type`, `Subscription_Tier`, per-model messages | No | Steering files, specs, hooks and agents are all invisible to its own analytics. No acceptance metric at all |
| [JetBrains AI](https://www.jetbrains.com/help/ide-services/ai-analytics-api.html) | `tool` enum: `junie`, `aia`, `claude_code`, `code_completion`, `nes` | No | Richest did-it-help set in the category, including "AI contribution rate" over committed lines. Nothing names `.junie/guidelines.md` or a rules file |
| [Tabnine](https://docs.tabnine.com/main/administering-tabnine/managing-your-team/reporting) | `Languages`, `IDEs`, IDE Agent vs CLI Agent | No | Ships both Agent Skills (`SKILL.md`) and org-pushed Agent Guidelines, and reports on neither |
| [Augment Code](https://docs.augmentcode.com/analytics/analytics-api) | `editor`, `language`, `model_name`, `user_type`, `bot_name` | No | Most complete non-Cursor schema. `.augment/rules/` and CLI Agent Skills go uncounted. Enterprise preview |
| [Zed](https://zed.dev/docs/business/organizations) | None | No | "The dashboard shows your members, roles, and billing." [Admin controls](https://zed.dev/docs/business/admin-controls) are five on/off toggles. No analytics of any kind |
| [Qodo](https://docs.qodo.ai/administration/management-portal) | Not verified | No | Advertises Analytics, publishes no metric or dimension names. Best Practices uncounted |
| [Warp](https://docs.warp.dev/enterprise/enterprise-features/analytics-api/) | `message_type`, `tool_type` | No | The sharpest negative here. Warp shares Workflows, Notebooks, Prompts, Plans and Rules as first-class server-side objects and reports on none of them |

Three patterns are worth carrying forward.

**The category measures the channel and the actor, never the instruction.** Every vendor counts where the output came from and who triggered it.

**Artefact governance and artefact measurement are decoupled everywhere.** Tabnine lets an admin push org-wide guidelines that override local ones and gives no way to see whether they fired. Warp shares named Rules as server-side objects and reports nothing per object. GitHub lets you define org-wide custom agents and then tells you to grep the audit log. Controlling the artefact is solved across the board; measuring it is not.

**Acceptance measurement is common and artefact-blind.** Around ten of the fourteen publish an acceptance rate or accepted-lines figure. All attribute the outcome to a user or a channel. Devin's `prs_merged` and JetBrains' AI contribution rate over committed lines are the only two that reach a real outcome, and neither can be sliced by artefact.

## Comparison

| Product | What it ingests | Finest granularity it reports | Per-skill | Price tier |
|---|---|---|---|---|
| Anthropic Claude Enterprise Analytics API | Server-side Claude Code, chat, Cowork, Office activity | Per skill per day, grouped by user, RBAC group or product | **Yes** | Claude Enterprise, custom |
| Anthropic Claude Code Analytics API | Claude Code on the Claude API | Per user per day, per edit-tool accept and reject | No | Free with Admin API |
| Braintrust | Claude Code hook events via its plugin; OTLP as a fallback | Per Skill tool span, `metadata.skill_name`, plus an online quality score | **Yes** | $0 Starter, $249/mo Pro, Enterprise custom |
| LangSmith | Claude Code plugin; OTLP at `api.smith.langchain.com/otel` | Per run, `metadata.ls_skill_name` chartable, plus online LLM-as-judge | **Yes** | $0 Developer, $39/seat/mo Plus, Enterprise custom |
| Honeycomb | Claude Code's own OTLP exporter, traces beta | Per event, grouped by any attribute, top 50 charted | **Yes**, but the attribute name it documents does not match Claude Code's | Free to 20M events/mo, Pro from $150/mo |
| Datadog LLM Observability | Claude Code OTLP logs and metrics via Agent Console | Per span, split by up to three tags | **Yes**, after you define a span-based metric yourself | $160/mo annual for 100k LLM spans |
| DX | Claude Code OTLP metrics and logs; Admin API; Enterprise Analytics API | Per user per day, plus per skill via the logs signal | **Yes**, needs `OTEL_LOG_TOOL_DETAILS=1` | |
| Sleuth Skills / `sx` | Its own CLI's asset-run events | Per asset invocation, per version, per actor, with token cost | **Yes** | `sx` free Apache-2.0; hosted custom |
| Skillsight (OSS) | OTLP `skill_activated`, or Loki | Per skill, per trigger, per user, per plugin, per marketplace | **Yes** | Free, Apache-2.0 |
| neeltom92 / KubeRocketCI dashboards (OSS) | OTel Collector to Prometheus and Loki | Per skill name and trigger | **Yes** | Free, Apache-2.0 |
| codeburn, cc-lens (OSS) | JSONL transcripts | Per named skill, count and cost | **Yes** | Free, MIT |
| Elastic `claude_code` integration | OTLP logs to Elasticsearch | Per skill via typed ECS fields | **Yes** | Elastic licensing |
| Cursor Analytics API (different agent) | Cursor editor and agent telemetry | Per skill per day: `event_date`, `skill_name`, `usage` | **Yes** | Enterprise only; Teams $40/seat/mo |
| GitHub Copilot metrics API (different agent) | IDE and github.com telemetry, PR outcomes | Per `ide`, `feature`, `language`, `model`, `phase` | No | Business $19, Enterprise $39 per seat/mo |
| Swarmia | Claude Code Analytics API and OTel metrics | Per user per day, per tool | No | Free under 10 devs, $45 or $55 per dev/mo, AI add-on $5 |
| LinearB | Claude Code OTel, Enterprise Analytics API, git | Per team, repo and tool, 20 AI metrics | No | $29 per user/mo (min 50), $59 (min 100) |
| Faros AI | Analytics APIs, OTel, git, 60+ sources | Per user per day, per tool | No | |
| Jellyfish | git, planning, CI/CD, AI tool usage | Team, repo, org | No | |
| Langfuse | Claude Code hooks and its own plugin | Per observation, turn, session | No, unmapped attributes are not filterable | Free Hobby, $29 Core, $199 Pro, $2,499 Enterprise; self-host free |
| Arize AX / Phoenix | Claude Code hook plugin | Per span (AX), fixed dashboard (Phoenix) | Not verified; no skill attribute documented | AX free to 25k spans, $50/mo Pro; Phoenix free |
| W&B Weave | Claude Code plugin, GenAI conventions | Per agent, per conversation, top tools | Not verified; "top tools" gives one bar for `Skill` | Free 1 GB/mo, Pro from $60/mo |
| Helicone | Proxy on `ANTHROPIC_BASE_URL` | Per request, per custom property | No, a proxy cannot see a skill name | $0 Hobby, $79 Pro, $799 Team |
| New Relic AI Monitoring | No Claude Code integration found | Per span via NRQL `FACET` | Not verified | 100 GB/mo free then $0.40/GB |
| ccusage (OSS) | JSONL transcripts | Per session and per day, tokens and cost | No | Free |
| ColeMurray/claude-code-otel (OSS) | OTel to Grafana | Per tool | No | Free, MIT |
| Harness SEI | Copilot and Gemini Code Assist | Per developer cohort | No | |
| Opsera | Copilot and Windsurf | Per developer, per tool | No | |

Blank price cells are contact-sales vendors with nothing published.

## The verdict: buy the count, build the judgement

**You can buy the count today. You cannot buy the judgement.**

The count is solved, and by more products than expected. If the org is on Claude Enterprise, `GET /v1/organizations/analytics/skills` gives per-skill invocation counts and per-skill cost attribution across the whole org with no client-side configuration, no agent on any machine, and no redaction of the catalogue's own skill names. That is a `curl` and a cron job away, and it is the cheapest route by a wide margin. If the org is not on Claude Enterprise, Braintrust's plugin, LangSmith's plugin, DX's OTel logs connector, Sleuth's `sx`, or any of six open-source tools will do it, at prices from zero to $249 a month.

The judgement is not solved anywhere. Every product in this sweep counts invocations. Braintrust and LangSmith can attach a model-graded score to the trace an invocation produced, and neither documents the combination of that score with a skill dimension, so even that is assembly rather than purchase. None joins a named skill to an outcome measured in the repository: did the PR merge, did it get reverted, did the build stay green, did review take less time. Devin names individual knowledge items as useful or misleading but only inside one session, with no aggregation and no API. JetBrains reaches committed lines but cannot slice by artefact. The gap between "skill X fired 400 times" and "skill X was worth writing" is the whole question, and it is unowned by the category.

Two smaller cautions for whoever buys. Every vendor except Honeycomb ships its own plugin, hook or proxy rather than consuming Claude Code's OTLP export, so each has a different idea of what a skill is and nothing validated on one path transfers to another. And prompt registries are a red herring: eight platforms here have one, all do versioning and deployment, and not one publishes per-prompt usage counts.

There is a second reason to build rather than only buy. Anthropic's counting rule excludes anything a hook injects and anything merely listed. Braintrust's plugin requires each developer to run `bt trace enable claude`. Both measure activation, not effect. This repo already has the stronger instrument: `harness/` measures whether a skill fires against a frozen fixture, gated at 53 of 60 pooled should-fire runs. That is a controlled measurement of the thing a description is supposed to do, and no purchase on this list replaces it. Production counts tell you a skill is reachable in the wild. The harness tells you the description works. They answer different questions and both are needed.

### The cheapest realistic build

One OpenTelemetry Collector and a file. Push `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_LOGS_EXPORTER=otlp` and `OTEL_LOG_TOOL_DETAILS=1` through managed settings so a developer cannot opt out, point the collector at a file or ClickHouse, and keep only `claude_code.skill_activated`. That one event carries `skill.name`, `invocation_trigger`, `skill.source`, `plugin.name` and `marketplace.name`, which is a per-skill count split by whether the model chose the skill or a developer typed it, which is precisely the engine-versus-entry-point split the catalogue is built around. Lift the Grafana panels from [neeltom92/claude-code-observability](https://github.com/neeltom92/claude-code-observability) or [KubeRocketCI/claude-code-telemetry](https://github.com/KubeRocketCI/claude-code-telemetry), both Apache-2.0, rather than writing queries from scratch, or run [Skillsight](https://github.com/PackmindHub/skillsight) if a skill-to-plugin-to-marketplace model and a triage workflow are wanted for free. Do not build on the `skill.name` label of `claude_code.token.usage` or `cost.usage`: for a private marketplace that label reads `third-party` for every skill, and no documented flag lifts it. For the judgement half, set `CLAUDE_CODE_ENABLE_FEEDBACK_SURVEY_FOR_OTEL` so the session rating prompt emits `claude_code.feedback_survey` events, and join a session's rating to the skills that fired in it by session id. That is a crude outcome signal, it is free, and it is one join more than any product on this list offers.

