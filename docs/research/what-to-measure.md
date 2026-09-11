# What to measure, and how

Researched 11 September 2026. This is the decision document. The three siblings hold the working: [skill-usage-telemetry.md](skill-usage-telemetry.md) measures what Claude Code emits, [who-measures-skill-usage.md](who-measures-skill-usage.md) records what other companies publish, and [buying-skill-telemetry.md](buying-skill-telemetry.md) sweeps the products. This one answers two questions and nothing else. What has anyone found worth measuring, and by what mechanism would we measure it here.

Two tables do the work. The first is what a machine can count on its own. The second is what has to be computed from git, from a survey, or from an eval.

The split between them is the point. **Nothing in the first table tells you a skill helped.** Every mechanism in it counts activations. Every published claim that an agent improved anything comes from the second table, and not one of those claims is attributed to an individual skill by anybody.

## What others have found worth measuring

Twenty-seven companies were checked against their own primary sources. Not one publishes a per-skill invocation count. What they publish instead falls into six families, ordered here by how much evidence sits behind each.

| What they measure | Best published figure | Who | Does it survive scrutiny? |
|---|---|---|---|
| Pull request cycle time | Median down 30.8% | [Atlassian](https://www.atlassian.com/blog/artificial-intelligence/developer-productivity-improved-with-rovo-dev), year-long study across 1,900+ internal repositories, accepted at ICSE'26 | Yes. The strongest result in the survey, and the only one with a controlled design and a peer-reviewed venue |
| Suggestion acceptance ratio | 38.70% of agent review comments led directly to a code change, against 44.45% for human comments | Atlassian, same study | Yes, and it is the best-shaped metric of the lot. A quality ratio, benchmarked against a human doing the same job |
| | 58% of AI-generated tests used without modification | [Intuit](https://medium.com/intuit-engineering/a-platform-centric-approach-to-ai-assisted-code-generation-at-intuit-03984a85558e) | Yes, and it is the clearest single acceptance number published |
| Eval pass rate on a frozen set | 21% without skills, above 95% with them | [Anthropic's data science team](https://claude.com/blog/how-anthropic-enables-self-service-data-analytics-with-claude) | Yes. The only published practice that ties a skill to an outcome, and the closest analogue to `harness/` |
| | Precision from about 15% to 80% on a rolling two-week lookback, against a 66-task corpus | [Figma](https://www.figma.com/blog/how-figma-stays-ahead-of-vulnerabilities-with-agents/) | Yes. Hardest numbers of anyone, and the unit is the agent, not the skill |
| Pull requests merged per developer | 2.15 agent-assisted per developer per week | Atlassian | Yes, as a level. Not as proof of cause |
| | Up to 3x more merged PRs for adopters than non-adopters | [Commonwealth Bank](https://medium.com/commbank-technology/the-evolution-of-ai-software-engineering-75a8a5a02c14) | Weakly. Their own post says "these numbers are imperfect" |
| Cost per outcome | Cost per merged PR, per review, per alert, per cleanup, with revert rate, F1 and MTTR alongside | [Uber](https://www.uber.com/us/en/blog/efficient-software-factory/), per managed agent | Yes as a framework. Uber publishes the shape and not the values |
| Adoption | 93% of R&D | [Cloudflare](https://blog.cloudflare.com/internal-ai-engineering-stack/) | As a rollout metric only. Atlassian moved off this family on purpose |
| | 97% weekly, 90% daily | Airbnb, via a [DX session](https://getdx.com/podcast/beyond-the-cli-agentic-ai-for-async-workloads-and-non-developers/) with Airbnb staff | |
| | 83% weekly active users | [Stripe](https://stripe.dev/blog/meet-stripes-knowledge-ai-platform) | |
| Satisfaction | Upvote rate stable at about 80% | [Duolingo](https://blog.duolingo.com/aislackbot/) | As a per-response signal. It is per response, never per skill |
| | Token usage as a leading indicator of adoption | [Coinbase](https://www.coinbase.com/blog/Tools-for-developer-productivity-at-coinbase) | They say themselves it is "for lack of a better metric" |

Three results shape everything below.

**Atlassian walked away from usage counting deliberately.** They describe moving "from usage based metrics (e.g. active users, token consumption) to towards developer productivity impact metrics (e.g. PR throughput, hours saved)" ([source](https://www.atlassian.com/blog/ai-at-work/ai-native-sdlc-paying-off-per-developer-per-week)). The people who have thought hardest about this stopped doing the thing we set out to do. That does not make counting useless, but it does mean a count needs a stated purpose.

**Nobody retires a skill for going unused.** Anthropic's team publishes the only retirement rule anywhere, and the trigger is an eval rather than a usage figure: "We also regularly prune skill scaffolding as models improve and previous failure modes no longer apply." Vercel, at roughly a hundred skills, states plainly that they have no mechanism at all.

**Two companies log which skill ran and publish nothing from it.** LinkedIn instruments "every CAPT tool and playbook invocation" with four fields, then publishes only "over 500 playbooks". Stripe says "AgentStudio surfaces usage data and quality signals alongside each asset" and publishes no figure. So the mechanism is not novel. Publishing the output would be.

## Table 1: what we can measure in code

Everything here counts activations. Each row was measured on this machine against Claude Code 2.1.248 unless the verdict says otherwise. [skill-usage-telemetry.md](skill-usage-telemetry.md) carries the payloads and the binary evidence.

| Mechanism | What it gives you | Reaches | Build | Verdict |
|---|---|---|---|---|
| **`claude_code.skill_activated` over OTLP**, with `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_LOGS_EXPORTER=otlp` and **`OTEL_LOG_TOOL_DETAILS=1`** | Per-skill count by real name, split by `invocation_trigger` into model-chosen and human-typed, plus `skill.source`, `plugin.name` and `marketplace.name` | The whole fleet, through managed settings a developer cannot switch off | A collector and a dashboard. Grafana panels exist to copy under Apache-2.0 | **Start here.** One variable unlocks a private catalogue's real skill names. Measured on the wire |
| **`PostToolUse` hook, matcher `Skill`** | `.tool_input.skill` unredacted, plus `duration_ms`, `cwd` and `transcript_path` | The whole fleet, shipped in a plugin's `hooks/hooks.json` or managed policy | One line of JSON and a `jq` command | **The cheapest fleet-wide counter.** No collector, no plan tier, no API key. Sees names OTel redacts on the cost metrics |
| **`skillUsage` in `~/.claude.json`** | Lifetime `usageCount` and `lastUsedAt` per skill, both delivery routes, unredacted | One machine | Nothing to read it. A scheduled snapshot to trend it | Free and already running. 72 entries on this machine. A 60-second per-name write throttle means it undercounts bursts, and it is a lifetime total with no window |
| **Session transcripts**, `~/.claude/projects/**/*.jsonl` | `input.skill` for invocations and `attributionSkill` for the active skill, both unredacted, plus repository, branch, timestamp and the token `usage` block | One machine, 30-day default window | A `grep`, or one of six open-source parsers | **The only route to per-skill token and cost for a plugin catalogue.** Raise `cleanupPeriodDays` first or the window closes on you |
| **`/skill-doctor`** | Unused-skill audit and per-skill context cost, over the same lifetime counter | One developer's terminal | Nothing. Needs Claude Code 2.1.261 or later | The retirement signal no company publishes, shipped as a command. It has no aggregation path, which is the open problem |
| **`GET /v1/organizations/analytics/skills`** | Per-skill `invocation_count`, `distinct_user_count` and `estimated_overage_spend` per day, filterable to `product:claude_code` and groupable by RBAC group | The whole organisation, server-side, with nothing installed anywhere | A `curl` and a cron job | **Cheapest by a wide margin if you qualify.** Claude Enterprise only, and it resolves real names only for marketplaces Anthropic knows about. Not verified against a private git remote |
| **`CLAUDE_CODE_ENABLE_FEEDBACK_SURVEY_FOR_OTEL`** | `claude_code.feedback_survey` events, joinable to the skills that fired by `session.id` | The fleet, same collector | One variable and a join | A crude outcome signal, and the only one on this table. Free. No product on the market offers the join |
| **`harness/`** | Whether a description fires against a frozen fixture, gated at 53 of 60 pooled should-fire runs | Nothing in production. A controlled measurement | Already built | Answers a different question, and the better one. Production counts prove a skill is reachable; the harness proves the description works |
| ~~`skill.name` on `claude_code.cost.usage` and `token.usage`~~ | Reads the literal `third-party` for every skill in a private plugin catalogue | | | **Do not build on this.** The redaction has no escape hatch. Measured in the function and on the wire |
| ~~`/usage_report`, `/cost_report`, Claude Code Analytics API~~ | Per user, per model, per product. No skill or plugin dimension at all | | | Any vendor reselling only this feed cannot report per skill, whatever the sales page says |

On buying rather than building, and on the scepticism about hitting an endpoint: the endpoint is real and several vendors already consume it. DX reads the OTel **logs** signal with the same `OTEL_LOG_TOOL_DETAILS=1` flag, Braintrust reads Claude Code's hook payloads and stamps `metadata.skill_name` on the span, LangSmith publishes `ls_skill_name`, and Sleuth's `sx` observes the assets it installed. Thirteen products and open-source tools reach a named skill. So per-skill counting is purchasable, not unprecedented. What no product does is the second table.

## Table 2: what is measured by other means

None of these come from Claude Code. Each needs git, a survey, a review system, or an eval. The last column is the one that matters, and it is the same answer almost every time.

| Metric | Computed from | Who publishes it | Can it be attributed to one skill? |
|---|---|---|---|
| Pull request cycle time | Git and review timestamps, median over a window | Atlassian, 30.8% reduction | No. Atlassian needed a year across 1,900 repositories to attribute it to a whole product |
| Pull requests merged per developer per week | Git, divided by an active-developer count | Atlassian 2.15, Salesforce up 79%, Airbnb up 65% | No |
| Revert rate | Git, reverts as a share of merged commits | Uber, as a quality signal per managed agent | No, but it is the cheapest quality signal on this table and it needs no new pipe |
| Suggestion acceptance ratio | Review data: did the comment lead to a code change | Atlassian 38.70% against 44.45% for humans | **Partly, and this is the opening.** Pick one skill with an observable output and count how often that output survives into the merged commit |
| Generated tests used unmodified | Review data | Intuit, 58% | Partly, on the same argument |
| Cost per merged PR | Join a cost figure to a git outcome | Uber publishes the framework, not the values | Only where the cost is attributable, which for a plugin catalogue means the transcripts rather than OTel |
| Eval pass rate on a frozen set | An eval harness, run before and after each change | Anthropic 21% to 95%, Figma 15% to 80% precision | **Yes.** The only mechanism anywhere that attributes an outcome to one skill. It is what `harness/` already does |
| Staleness | A CI check that fails a change to the documented thing when the skill file is untouched | Anthropic, "roughly 90% of our data-model PRs now include a skill change in the same diff" | Yes, per skill, and it is cheap. The single most copyable practice in the survey |
| Weekly and daily active developers | Telemetry or a survey | Cloudflare 93%, Airbnb 97%, Stripe 83%, Zapier 97% | No. A rollout metric. Atlassian moved off it on purpose |
| Self-reported hours saved | A survey | Block, Zapier, Monzo as part of a basket of ten to fifteen criteria | No |
| Satisfaction | A thumbs-up control in the product | Duolingo, about 80% | Per response, never per skill |
| AI-written share of code | Git attribution | Coinbase, who say it is a stopgap; Monzo about 10% of merged PRs | No |

## What I would do here

Three things, in this order. Each is small and each answers a question the one before it raises.

**One. Ship the hook counter, not the collector.** A `PostToolUse` hook matching `Skill` is a single line in `hooks/hooks.json`, ships inside the plugin itself, sees names that OpenTelemetry redacts, and needs no collector, no API key and no plan tier. It gives the count and the retirement signal. Do the OTLP build only when a fleet actually exists to point it at, and when it happens use the `skill_activated` log event, never the cost metric.

**Two. Copy Anthropic's staleness gate.** A CI check that fails a pull request touching the thing a skill documents when the skill file is untouched. It is per skill, it is free, it runs in the free gate alongside the three commands already there, and it attacks the failure mode this repository already has evidence for, which is drift rather than disuse.

**Three. Leave the judgement to `harness/`.** Atlassian's rejection is right and we should say so out loud rather than argue past it. A count tells you a skill fired. It does not tell you the firing helped. So the count earns its place as the input to one decision, whether a skill is worth its listing context, and nothing more. Everything about whether a skill works stays in the harness, which is a controlled measurement against a frozen fixture and is closer to Anthropic's published practice than to anything else in the survey.

The honest boundary is worth stating plainly. If somebody asks whether skill number seven was worth writing, no mechanism on either table answers that on its own. The nearest thing to an answer is Atlassian's acceptance ratio applied to one skill at a time: pick a skill whose output is observable, count how often that output survives into the merged commit, and compare it against the same work done without the skill. That is expensive and it is the only route anyone has published that gets there.
