# Who measures a skill catalogue, and how

Research date: 2026-09-11. This covers the measurement layer underneath [findings.md](../findings.md): not what companies built, but how they count whether it works.

Three siblings complete the set. [what-to-measure.md](what-to-measure.md) is the decision document and the place to start, [skill-usage-telemetry.md](skill-usage-telemetry.md) measures what Claude Code itself emits, and [buying-skill-telemetry.md](buying-skill-telemetry.md) asks whether any of this can be bought rather than built.

## The short answer

Nobody publishes per-skill usage telemetry. Twenty-seven companies were checked against their own primary sources. Not one publishes how many times an individual skill was invoked.

Three claim the mechanism and withhold the output. LinkedIn logs which playbook ran on every call. Stripe says its control plane shows usage per asset. DoorDash routes every run through a gateway it describes as a usage-tracking control point. All three publish inventory counts and no invocation counts.

One publishes a retirement practice, Anthropic's data science team, and the trigger is an eval rather than usage data. One states on the record that it has no retirement mechanism, Vercel, at about a hundred skills. Nobody retires a skill because it went unused, anywhere, in any source.

Everyone else steers on a person-level metric. Adoption percentage, weekly active users, pull requests merged, self-reported hours saved. The unit of measurement is the developer or the agent. It is never the skill.

Two findings sharpen that into something useful rather than merely disappointing.

The first is a deliberate rejection. Atlassian, which publishes the most rigorous measurement work in the survey, says it moved "from usage based metrics (e.g. active users, token consumption) to towards developer productivity impact metrics (e.g. PR throughput, hours saved)". The company that thought hardest about this walked away from usage counting on purpose. Counting invocations is a position that needs an argument, not a default.

The second is that the tooling moved first again. Claude Code shipped a per-skill usage report and a per-skill telemetry attribute during 2026, before any company published a practice that uses either. That is the same year-ahead gap [findings.md](../findings.md) records for distribution.

## The comparison table

A blank cell means the company publishes nothing on that question. The blanks are the finding.

| Company | Counts per skill? | What they steer on | Retirement signal | Owner and dashboard |
|---|---|---|---|---|
| LinkedIn | Yes, every playbook invocation, no numbers released | Which playbooks are used, where usage drops off, which workflows fail | | "Internal dashboards", no owner named |
| Anthropic (data science) | | Offline eval accuracy per domain slice, threshold about 90% | Prunes skill scaffolding as models improve. Eval-driven, not usage-driven | Domain owner per slice, dashboard reviewed weekly |
| Uber | | Cost per merged PR, revert rate, F1, MTTR, diffs landed, per managed agent | | Session analysis dashboard, zero opt-in |
| DoorDash | Gateway claims usage tracking, no per-playbook number | 300+ unique playbooks, 10,000+ invocations weekly, aggregate | | Agent Gateway is the control point |
| Stripe | AgentStudio claims per-asset usage data, no numbers released | 83% weekly active users, session depth | | Domain owners, AgentStudio control plane |
| Figma | | Precision on a rolling two-week lookback, recall against a 66-task corpus | Human review gates every policy change | Datadog and Slack, security engineering team |
| Monzo | | Ten to fifteen criteria: WAU, MAU, retention, AI-written code share, acceptance rate, satisfaction, cost | Quarterly reviews retire purchased tools, not skills | |
| Cloudflare | | 93% R&D adoption, merge requests per week | | |
| Block | | AI-authored code, reported time savings, automated PR count | | |
| Booking.com | | Daily active users against PR merge rate | | Three named owners, DX platform |
| Airbnb | | 97% weekly adoption, 65% higher PR throughput | | |
| Duolingo | | ~300 weekly active users, ~80% upvote rate | | |
| Shopify | | River sessions, coauthored PRs merged | | `river_sessions` domain table |
| Vercel | | Nothing published, described as anecdotal | States on the record that there is none | |
| Atlassian | No, and says it moved away from usage metrics on purpose | PR cycle time, PRs merged per developer per week, comment resolution rate | | DX, an outside framework |
| Coinbase | | Monthly to eng leaders: lead time, deploy frequency, bugs, incidents, token usage | | Eng leaders, monthly |
| Intuit | | 58% of AI-generated tests used unmodified, PR velocity | | Cost dashboards listed as future work |
| Salesforce | | Work items per developer, PRs merged per developer, an ML "Effective Output" score | | Engineering 360, predates skills |
| Zapier | | 97% self-reported survey adoption | | |
| Canva | | One agent answers correctly about 60% of the time, secondary source | | |
| Commonwealth Bank | | Merged PRs, adopters against non-adopters | | |
| Instacart | | Up to 20% time savings on one workflow | | |
| GitLab | | | | |
| AutoScout24 | | | | |
| Netflix | | | | |
| Notion | | | | |
| Mercado Libre | | | | |

## The one company that says it logs which skill ran

LinkedIn. From their own post, [Contextual Agent Playbooks and Tools](https://www.linkedin.com/blog/engineering/ai/contextual-agent-playbooks-and-tools-how-linkedin-gave-ai-coding-agents-organizational-context), published 27 January 2026 by Ajay Prakash and Nikhilesh Payyavuala. Verbatim:

> We needed a way to move beyond anecdotes and gut feelings. So from day one, we instrumented every CAPT tool and playbook invocation. For each call we log when it happened, which repository it ran in, whether it succeeded, and which tool or playbook was used.
>
> Those signals power internal dashboards that answer simple but critical questions:
>
> - Which playbooks are actually being used?
> - Which teams rely on CAPT the most?
> - Where does usage drop off?
> - Which workflows are failing frequently and need better guardrails?

Four fields per call: timestamp, repository, success, and which playbook. That is a schema we could copy tomorrow.

The first question on their own list is the one this research set out to answer, and LinkedIn never answers it in public. They publish "Over 500 playbooks have been authored across the company" and not one invocation figure, no distribution across those 500, no owner of the dashboard, and no retirement rule. The mechanism is described and the output is withheld.

It is still the only published statement that a company logs which skill ran. Everyone else logs that a skill ran.

## The one company that publishes a retirement practice

Anthropic's data science and data engineering team, in [How Anthropic enables self-service data analytics with Claude](https://claude.com/blog/how-anthropic-enables-self-service-data-analytics-with-claude), published 3 June 2026 by Josh Cherry, Clement Peng, Johanne Jiao, Justin Leder and Chen Chang.

The admission test for a skill is an eval, quoted verbatim:

> Without skills, Claude's ability to answer analytics questions accurately didn't exceed 21% on our evals.

> Adding skills gets these numbers consistently above 95% in aggregate and regularly around 99% in certain domains.

The change test is an ablation on every edit:

> Every meaningful skill edit gets a before / after run on the relevant eval slice, with the delta in the PR description.

The retirement rule is the only one published anywhere, and it arrives with its justification attached:

> Skill docs describe a data model that changes daily, so without active maintenance they're wrong within weeks. We also regularly prune skill scaffolding as models improve and previous failure modes no longer apply. A code-review hook flags any reporting-model change that doesn't touch a skill file.

Note what that is not. It is not "this skill fired zero times, delete it." It is "the model no longer needs this crutch, delete it." The trigger is model capability, not usage data. Nobody publishes a usage-triggered retirement rule.

Three supporting mechanisms are worth recording because they are cheap and copyable:

- A staleness gate in CI: "A code-review hook flags any reporting-model change that doesn't touch a skill file." The measured result is "Roughly 90% of our data-model PRs now include a skill change in the same diff."
- An owner gate: "A domain owner can't announce the agent to their stakeholders until their slice of the eval set clears some threshold (we initially used ~90%)."
- Two continuous production signals: "the share of agent queries that resolve through the semantic layer, and the share of responses that use correction language." Both "feed a dashboard reviewed weekly."

The post also records the decay that makes all this necessary: "We watched our offline accuracy drift from ~95% at launch to ~65% over a month."

## The one company that says it has no retirement mechanism

Vercel. In a vendor-hosted Q&A, [Vercel Claude Code case study](https://claude.com/customers/vercel-qa), Andrew Qu, Chief of Software at Vercel, says of a catalogue of about 100 skills powering their internal data science agent:

> We honestly haven't reached a point where skills collide and we have to remove one, though I'm sure we'll get there eventually.

That is a vendor-hosted page, so the framing is marketing, but the quote is attributed and first-person, and it is the most direct answer to the retirement question in the whole survey.

Two more things on that page bear on our design. Asked "With around 100 skills behind one agent, how does it know which skill to use when?", Qu answers "We haven't hit a ceiling where there are so many skills that the agent reaches for the wrong one," and attributes it to description quality and model capability: "Being very descriptive with the title and the description matters a lot," and "We've found that Opus, specifically Opus 4.8, is very good at matching intent to skills." That is one engineer's impression on a vendor page, with no measurement behind it, and it sits against our own measured result that a broad request pulled a mean of 6.0 skills out of 12 without a negative boundary. Our number is measured and theirs is not, so the cap stands, but it is the only published claim that a hundred-skill listing selects cleanly.

Vercel also runs skills.sh, a public registry, and Qu refers to skills "in the top 100 on skills.sh". A public install ranking is the nearest thing to per-skill usage data anyone publishes, and it measures installs on a public registry rather than invocations inside an organisation. It does not transfer to a private catalogue.

Secondary confirmation that Vercel publishes no measurement: [TechInformed, 29 June 2026](https://techinformed.com/how-vercel-built-an-ai-agent-ecosystem-on-claudes-open-skills-standard/) reports that "The public Vercel Q&A, however, does not provide audited measures for cost savings, cycle-time reduction, incident resolution or development quality," and that "Qu described the speed gains as anecdotal."

Vercel's own engineering post [We removed 80% of our agent's tools](https://vercel.com/blog/we-removed-80-percent-of-our-agents-tools) (22 December 2025, Andrew Qu) looks like a retirement precedent and is not one. The removal was an architectural decision benchmarked on five queries, reporting 3.5x faster runs, 37% fewer tokens and 42% fewer steps. No per-tool usage data drove it.

## The company that stopped counting usage on purpose

Atlassian publishes the most careful measurement work in the survey, and it is the strongest argument against the thing this research went looking for.

In [The AI-native SDLC is paying off](https://www.atlassian.com/blog/ai-at-work/ai-native-sdlc-paying-off-per-developer-per-week), 31 May 2026, by data scientists Robbie Geoghegan and Fan Jiang, they describe shifting "from usage based metrics (e.g. active users, token consumption) to towards developer productivity impact metrics (e.g. PR throughput, hours saved)".

That is a considered move by people who do this for a living, and it deserves to be taken seriously rather than argued around. A usage count tells you a skill fired. It does not tell you the firing helped.

Their published numbers show what they replaced it with. From [30.8% Faster PRs](https://www.atlassian.com/blog/artificial-intelligence/developer-productivity-improved-with-rovo-dev), a year-long online evaluation across 1,900+ internal repositories, accepted at ICSE'26: a 30.8% reduction in median PR cycle time, human-written review comments down 35.6%, and "38.70% of Rovo Dev-generated comments led directly to code changes" against 44.45% for human comments. That last pair is the shape of number this field actually publishes. It is a quality ratio, not a usage count, and it is benchmarked against humans doing the same job.

One label correction on that post, because the headline invites the mistake. The widely quoted 19% is about Atlassian's customers, not Atlassian: "repos that adopted Rovo Dev merged 19% more pull requests per month than similar repos that had not adopted it", across 3,400 repositories from 2,500 customers. Atlassian's own internal figure is stated separately as 2.15 Rovo Dev assisted merged PRs per developer per week, against 1.59 externally.

Atlassian does have skills. [Supercharge your security testing with Rovo Dev Skills](https://www.atlassian.com/blog/development/supercharge-your-security-with-rovo-dev-skills), 5 March 2026, names eight skills in an orchestrator and cites "more than 60 Rules" internally, with a context budget rule of thumb that a skill should "only handle 10-30k content characters". No invocation counts, no retirement, no dashboard.

## Uber, settled

[findings.md](../findings.md) marks the Uber row secondary "on everything except the headline counts". This pass confirms that split rather than overturning it.

Both numbers are in Uber's own post, [Running a Software Factory Efficiently at Uber Scale](https://www.uber.com/us/en/blog/efficient-software-factory/), published 27 August 2026 by Uday Kiran Medisetty, Distinguished Engineer. Verbatim:

> Engineers have built over 3,600 agent skills across the software development life cycle.

> executed more than 30K agent skill executions per day.

What is *not* in Uber's post is the rest of the row. The post contains no sentence with the word "eval", and none with "marketplace", "registry", "deprecate" or "retire". The registry framing and the "evaluation feedback" claim recorded in findings.md remain secondary and should stay labelled that way.

The 30K figure is an aggregate. Uber publishes no per-skill breakdown, so the distribution across 3,600 skills is unknown. At face value it averages about 8 executions per skill per day, but that is arithmetic on our side, not a number Uber published, and a long tail is the likelier shape.

What Uber does publish is a metrics framework, at five layers: portfolio, unit economics, model economics, driver decomposition, and managed agent outcomes. The last is the useful one. Per managed agent, Uber tracks:

> Outcome-denominated cost (cost per merged PR, cost per review, cost per alert, cost per cleanup); Quality signal (revert rate, F1, MTTR); Volume (diffs landed, reviews posted, alerts triaged)

The unit is the managed agent, not the skill. Uber also runs a session analysis dashboard that "requires zero setup or opt-in" and "flags 16 distinct anti-patterns across sessions, pairing each with its financial impact and a targeted remediation". That dashboard is about cost waste, not about which skill earns its listing slot.

## Duolingo's benchmark suite, scrutinised

The brief asked for the same scrutiny on Duolingo's "benchmark suite on every change". [Their post](https://blog.duolingo.com/aislackbot/) gives it two sentences:

> Evals keep agents from regressing. We maintain a benchmark suite that runs on every change, so quality drops get caught before users see them.

That is all. No case count, no pass criterion, no per-agent breakdown, no gate behaviour, no owner. The claim in findings.md is accurate as far as it goes, and there is nothing behind it to copy.

What Duolingo does publish is aggregate: "usage has grown to ~300 weekly active users—roughly 30% of the company" and "The Slack App's upvote rate has stabilized at ~80%." The upvote rate is the closest thing to a per-response quality signal anyone publishes at that granularity, and it is per response, not per agent.

## GitLab's public repo, read directly

[gitlab.com/gitlab-org/ai/skills](https://gitlab.com/gitlab-org/ai/skills) is public, so this is a direct reading rather than a summary. Created 11 March 2026, MIT licensed, 232 commits at time of reading.

Root contents: `.agents`, `.claude-plugin`, `.gitlab`, `.opencode`, `bin`, `doc`, `scripts`, `skills`, `tofu`, plus `.gitlab-ci.yml`, `AGENTS.md`, `CLAUDE.md`, `CONTEXT.md`, `Dangerfile`, `LICENSE`, `README.md`, `catalog.json`, `lefthook.yml`, `package.json`, `synced-skills.yml`.

The CI pipeline runs fourteen jobs. Every one is a structural validator or a distribution step: `verify-readme`, `validate-frontmatter`, `validate-skill-length`, `lint-shell`, `skill-tests`, `agent-file-sync`, four `validate-distribution-*` jobs for Claude Code, OpenCode, agents and pi, `verify-catalog`, `check-version-bump`, `sync-distribution-versions`, `guard-synced-skills` and `sync-skills`.

Not one job measures usage. There is no analytics job, no reporting job, no telemetry, and no deprecation job. The README has no metrics section and no deprecation section. `CONTEXT.md` defines eight terms of art (Skill, Environment, Distribution, Installation, SKILL.md, Reference file, Helper script, Synced Skill) and none of them concerns lifecycle, usage or retirement.

The most sophisticated public skills repo in the survey has a complete quality gate and no measurement layer whatsoever. That is the cleanest single piece of evidence for the headline finding.

## The tooling layer, which is ahead of the practice

This is the part that is directly actionable, because it is shipped and it is measurable on this machine.

[skill-usage-telemetry.md](skill-usage-telemetry.md) measures this surface properly, against the binary and against a local OTLP receiver. Three results from it bear directly on the gap above.

**The retirement signal no company publishes is a built-in command.** `/skill-doctor`, added at v2.1.261, "flags skills in the listing that have never been invoked" ([skills documentation](https://code.claude.com/docs/en/skills)). It reads a persistent per-skill counter, `skillUsage` in `~/.claude.json`, which holds a lifetime `usageCount` and a `lastUsedAt` per skill and is measured at 72 entries on this machine. The count spans sessions. What it does not do is aggregate: the report reaches the developer at their terminal, never the catalogue owner. Detection is solved and collection is not.

**Per-skill counting over OpenTelemetry works, including for a private catalogue.** `claude_code.skill_activated` carries `skill.name`, `skill.source` and `invocation_trigger`, and `OTEL_LOG_TOOL_DETAILS=1` removes the `custom_skill` placeholder. Measured on the wire, a private plugin's skill reported `skill.name: "probekit:probe-plugin"` with its private marketplace named alongside.

**Plugin delivery still costs one thing, permanently.** On `claude_code.cost.usage` and `claude_code.token.usage`, a third-party plugin's skill name is replaced with `"third-party"` by a different code path that has no escape hatch. So a private catalogue can be counted and cannot be costed over OpenTelemetry. The transcripts carry unredacted cost attribution as a workaround.

### The admin APIs do not carry skills

The [Claude Code Analytics Admin API](https://platform.claude.com/docs/en/manage-claude/claude-code-analytics-api) aggregates per user per day. Its fields are `num_sessions`, `lines_of_code.added` and `.removed`, `commits_by_claude_code`, `pull_requests_by_claude_code`, accepted and rejected counts for the Edit, MultiEdit, Write and NotebookEdit tools, and a per-model token and cost breakdown. There is no skill field, no plugin field and no command field.

The [usage analytics dashboard](https://support.claude.com/en/articles/12157520-claude-code-usage-analytics) gets one step closer with "Top commands: The Claude Code commands used most often across your organization," and mentions no skills.

The Claude Enterprise announcement of 2 July 2026, [Giving admins more visibility and control over Claude usage and spend](https://claude.com/blog/giving-admins-more-visibility-and-control-over-claude-usage-and-spend), is the one place a per-skill number is promised. Verbatim: "Skills report their own usage and cost, and new endpoints track plugin adoption and artifact creation," and the dashboard shows "usage and cost by group and by user, with output like artifacts created, files edited, skills and connectors used displayed directly next to their cost." The announcement also carries the sentence "What I actually want to see is which skills get run again and again across the org — that's the real signal of value." This is Claude Enterprise, which is a different surface from the Claude Code Admin API above, and it is a vendor announcement rather than a measured result.

### What the community asked for

Issue [anthropics/claude-code#35319](https://github.com/anthropics/claude-code/issues/35319), opened 17 March 2026 and since closed, requests exactly the thing this research was looking for. From the issue text:

> Today, session metadata tracks `"Skill": 3` (aggregate tool count) but not _which_ skill was invoked. Without per-skill invocation tracking, we cannot: Identify unused skills for deprecation, Measure adoption of newly created skills, Detect duplicate skills

The author reports "Skills with known usage data: 0 / 183 (0%)" across their own installation. This is a community issue, not an Anthropic statement, and no Anthropic reply is visible on it.

### Other vendors

- **Cursor** shipped team marketplaces for plugins ([changelog 2.6](https://cursor.com/changelog/2-6)), and its [team dashboard documentation](https://cursor.com/docs/account/teams/dashboard) lists only aggregate metrics: AI requests, model usage, resource consumption. No per-plugin or per-skill breakdown is documented.
- **Vercel's `npx skills` CLI** ([vercel-labs/skills](https://github.com/vercel-labs/skills)) collects install telemetry: "This CLI collects anonymous usage data to help improve the tool." Skill identifiers are sent for repositories GitHub confirms are public. Opt out with `DISABLE_TELEMETRY=1` or `DO_NOT_TRACK=1`. The data goes to Vercel, not to a catalogue owner, and there is no deprecation mechanism.
- **AWS Agent Registry** ([announcement, 31 August 2026](https://aws.amazon.com/blogs/machine-learning/manage-agents-tools-and-skills-at-scale-with-aws-agent-registry/)) has a lifecycle state for retirement: "Curator can deprecate a record when it's no longer needed, transitioning it to the DEPRECATED state." The usage side is roadmap, not shipped: "Observability metrics surfaced per agent and tool, combined with dependency graphs" appears under future work. Named customers are quoted with no numbers.

## What the analysts say

**Thoughtworks Technology Radar Vol. 34, April 2026.** The [Agent Skills entry](https://www.thoughtworks.com/radar/techniques/agent-skills), ring Trial, contains one sentence on the subject: "Plugin marketplaces are emerging as a way to version and share skills, and multiple efforts are exploring how to 'evaluate skill effectiveness'." The [Claude Code plugin marketplace entry](https://www.thoughtworks.com/en-us/radar/tools/claude-code-plugin-marketplace), also Trial, says nothing about measurement at all. It mentions "a more streamlined and governed way to share these artifacts" and stops there.

**DORA.** The [2025 State of AI-assisted Software Development](https://dora.dev/dora-report-2025/) landing page and the [ROI of AI-assisted Software Development report](https://dora.dev/ai/roi/report/) page, last updated 22 April 2026, mention nothing about prompt libraries, agent skills, playbooks or standardised AI instructions. There is no 2026 edition of the State of AI-assisted Software Development report; the 2026 publication is the ROI report, which is a follow-up.

The nearest DORA gets is the [AI-accessible internal data](https://dora.dev/capabilities/ai-accessible-internal-data/) capability, last updated 12 January 2026, which is about context engineering. Its measurement guidance is about the retrieval layer, naming retrieval event frequency, data source access rate, AI retrieval latency, query success and error rates, context-rich prompts and new developer onboarding. None of those is a per-asset usage count. On staleness it offers only a pitfall: "Indexing all code—including deprecated projects—means the AI learns bad habits as easily as good ones."

Caveat on provenance: both DORA findings come from the public landing and capability pages. The full report PDFs were not read.

## Everyone else, briefly

Each of these publishes real numbers. None of them is per skill.

**Cloudflare**, [How we built our internal AI engineering stack](https://blog.cloudflare.com/internal-ai-engineering-stack/). 93% adoption across R&D, 3,683 internal users, merge requests rising from about 5,600 per week to over 8,700 with a peak of 10,952, 47.95 million AI requests and 241.37 billion tokens. The Engineering Codex is audited by scoring "every requirement" as "COMPLIANT, PARTIAL, or NON-COMPLIANT", which measures compliance with a rule, not usage of one.

**AutoScout24**, [Designing a coding agent skills marketplace](https://tech.autoscout24.com/blog/posts/designing-a-coding-agent-skills-marketplace/). The closest published analogue to this repo, and it contains nothing on measurement. No invocation counts, no adoption figures, no dashboard, no retirement. The only number is inventory: "The marketplace today has 16 plugins contributed by multiple teams."

**DoorDash**, [Delegating engineering work to cloud-based agents](https://careersatdoordash.com/blog/delegating-engineering-work-to-cloud-based-agents/), 11 August 2026, Jeffrey Hwang and Siddarth Kodwani. Verbatim: "more than 25,000 automated code reviews each week, and more than 300 unique playbooks and 10,000-plus invocations used every week", and 130,000 engineering tasks automated in a single month. The capability exists at the gateway: "This gateway architecture gives us a centralized control point for authentication, authorization, observability, usage tracking, and policy enforcement." No per-playbook figure is published and no retirement rule is described. The 10,000 invocations over 300 playbooks averages about 33 per playbook per week, which is our arithmetic and not DoorDash's claim.

**Block**, [AI-assisted development at Block](https://engineering.block.xyz/blog/ai-assisted-development-at-block). "About 95% of our engineers are regularly using AI", and within three months of the Champions programme "AI-authored code jumped by 69%, reported time savings increased 37%, and automated PRs increased 21×". Nothing per recipe. The widely repeated "60% of 12,000 employees use Goose weekly" and "manual hours saved by AI" figures come from third-party write-ups and a Sequoia podcast, not from Block's engineering blog, and should be treated as secondary.

**Shopify**, [Under the River](https://shopify.engineering/under-the-river). "one in eight merged pull requests across Shopify is coauthored by it", and over 30 days "59,918 River sessions happened in 5,170 Slack channels" producing "3,536 River-coauthored pull requests merged". River writes to "the `river_sessions` domain table" every session, and patterns are "fed back into River's skills, prompts, and defaults", but no per-skill data is published.

**Stripe**, [Meet Stripe's Knowledge AI Platform](https://stripe.dev/blog/meet-stripes-knowledge-ai-platform), 30 July 2026, Anna Mason, Sharadh Krishnamurthy and Anupam Upadhyay. The mechanism claim is the interesting one: "AgentStudio surfaces usage data and quality signals alongside each asset, so domain owners can see what's working without asking the platform team." That is a per-asset usage claim, the closest to LinkedIn's. Like LinkedIn, Stripe publishes no numbers from it. Their agent Kai is "connected to 1,000+ skills and tools", and the published figures are platform-level: 83% weekly active users, 5,000+ data analysis sessions daily.

**Figma** publishes the hardest numbers of anyone, and the unit is the agent. From [How Figma stays ahead of vulnerabilities with agents](https://www.figma.com/blog/how-figma-stays-ahead-of-vulnerabilities-with-agents/), 23 July 2026: precision started at "only about 15% of findings (4 of 27) were valid", a 70% precision goal gated shipping developer-facing comments, and by December 2025 precision reached "80% on a two-week lookback" against an eval corpus of "66 tasks, each a real vulnerability". Median spend is "about $0.50" per PR review. From [How we secure Figma's internal systems with agents](https://www.figma.com/blog/how-we-secure-figmas-internal-systems-with-agents/), 29 July 2026: "around 70% reduction in time-to-resolution on complex alerts" and "20% reduction in on-call pages". Metrics live in Datadog and Slack. Figma's "steering memory" is a Markdown document loaded at the start of every run, which is architecturally skill-like, and no per-rule invocation data is published for it.

**Monzo**, [Building Agent Chip](https://monzo.com/blog/building-agent-chip), 13 August 2026, Fabien Deshayes and Oli Haley: "authoring ~10% of all merged PRs at Monzo" and "routinely running more than 1800 tasks every day". The 1,800 is a total, not a per-skill figure. A DX newsletter interview with Deshayes, [How Monzo runs data-driven AI experimentation](https://newsletter.getdx.com/p/how-monzo-runs-data-driven-ai-experimentation), 31 October 2025, is secondary and describes the richest published metric basket: success measured "across ten to fifteen criteria" including "weekly and monthly active users, retention, percentage of AI-written code, suggestion acceptance rate, satisfaction, and cost", with "Quarterly reviews adjust allocations based on usage trends." That quarterly review retires purchased tools, not skills. Separately, [The Monzo Ops Agent](https://monzo.com/blog/engineering-the-future-of-customer-operations-the-monzo-ops-agent), 4 June 2026, describes processes as human-readable Markdown with metadata about when to use them, taking inspiration from the Agent Skills standard. That is the closest architectural analogue to a catalogue in the survey, and it publishes one outcome number and no usage data.

**Booking.com** publishes the clearest owner structure and no skills at all. The source is a vendor case study, [Booking.com uses DX to measure AI's impact on developer productivity](https://getdx.com/customers/booking-uses-dx-to-measure-impact-of-genai/), which quotes named Booking staff: "daily active users had a 16% higher PR merge rate than non-users" (Leo Kraan, Director of Engineering), and adoption scaling from under 10% to around 70% across 3,500+ engineers. Their own tech blog carries a talk on measuring GenAI ROI and no post about agent skills or playbooks.

**Airbnb**, via the DX Annual session [Beyond the CLI](https://getdx.com/podcast/beyond-the-cli-agentic-ai-for-async-workloads-and-non-developers/) with Christopher Sanson and Madison Capps of Airbnb. Secondary in that it is a vendor platform, first-party in that the speakers are Airbnb staff: "97% of active engineers inside of Airbnb are using Agentic AI on a weekly basis so far, 90% daily" and "Our PR throughput inside of Airbnb is 65% higher than it was before we introduced agentic AI". Nothing per skill. Airbnb's own [Eval-driven development](https://airbnb.tech/ai-ml/eval-driven-development-lessons-from-evaluating-genai-at-scale/) post is about product GenAI features, not internal developer skills, and should not be read as evidence here.

**Zapier**. The often-quoted "800+ AI agents deployed internally" comes from a vendor case study, [Zapier builds an AI-first remote culture with Claude Enterprise](https://claude.com/customers/zapier), and it is an inventory count rather than an invocation count. Wade Foster's own post, [How Zapier rolled out AI org-wide and drove 97% adoption](https://zapier.com/blog/how-zapier-rolled-out-ai/), reports "97% of our team actively uses AI in their day-to-day work", measured by engagement survey, with no definition of active use. The vendor page says 89% where Zapier's own blog says 97%, which is a reason to cite the first-party figure.

**Netflix**. Nothing found on internal skills measurement. Their 2026 TechBlog output is model-serving infrastructure. A vendor-hosted webinar, [Scaling AI agent development at Netflix](https://www.anthropic.com/webinars/scaling-ai-agent-development-at-netflix), 20 November 2025, claims scale only, "3,000+ developers", and promises "rigorous evaluation frameworks" without publishing a metric.

**Salesforce** has the most explicit curated catalogue outside GitLab and publishes no number for it. From [How Salesforce engineering became truly agentic](https://www.salesforce.com/news/stories/how-engineering-became-agentic/), 27 May 2026, by Srinivas Tallapragada, President and Chief Engineering Officer: "We have also built an AI Expert Suite and the Salesforce Foundation Plugins, a curated, institutionalized library of AI skills built specifically for Salesforce engineering workflows". The only measurement attached to it is unquantified: "Our internal benchmark shows clear evidence that the curated skills improve accuracy and reliability on Salesforce-specific coding tasks while reducing unnecessary cost." No figure is given for that benchmark, no skill count, no usage. What they do publish is per-developer output: work items completed up 50.8% against April 2025, PRs merged per developer up 79%, an ML "Effective Output" score up 151.3% year on year, and "We crossed 90% adoption." A [follow-up of 9 September 2026](https://www.salesforce.com/news/stories/agentic-shift-for-salesforce-engineers/) restates those higher and adds cost work, including "Auto-compaction alone — context windows automatically compacting at 200,000 tokens — drove a 24.8% reduction in all-engineering spend". Their dashboard, [Engineering 360](https://engineering.salesforce.com/engineering-360-dashboard-transforming-complex-data-into-powerful-engineering-insights/), was published in October 2024 and predates agent skills entirely.

**Coinbase** states its steering metric more plainly than anyone. From [Tools for developer productivity at Coinbase](https://www.coinbase.com/blog/Tools-for-Developer-Productivity-at-Coinbase), 6 August 2025, Kyle Cesmat and Chitra Venkatramani: "To help drive adoption, we share a set of monthly metrics with eng leaders that includes lead-time-to-change, deployment frequency, bugs, incidents, and AI usage. For lack of a better metric, we initially measured % of code written by AI and are starting to focus on token usage, which we have found to be a good leading indicator of adoption." Token usage is the nearest anyone comes to a usage metric, and it is a proxy for adoption rather than a per-skill count. Their [Mux post](https://www.coinbase.com/blog/coding-had-a-concurrency-problem-how-mux-helped-solve-it), 11 May 2026, reports 5,068 merged PRs across 461 repositories in April 2026 and publishes its own selection-bias caveat: "Mux users likely skew toward engineers who were already high-output and AI-forward, so we don't claim Mux is the sole cause." Skills are named and never counted.

**Intuit** is the only company with a section headed "How we measure impact". From [A platform-centric approach to AI-assisted code generation at Intuit](https://medium.com/intuit-engineering/a-platform-centric-approach-to-ai-assisted-code-generation-at-intuit-03984a85558e), 12 June 2025, Deepa Soundararajan: "58% of AI-generated tests are used without modification, after review" and "Engineers using AI-assisted workflows merge PRs 56% faster". The 58% is the clearest acceptance rate in the survey. Their reusable unit is not a skill but a "golden repository", "a curated collection of high-quality, accurately-labeled code examples", and usage of an individual golden repository is never counted. Analytics are on the roadmap rather than shipped: "Enhanced integrations with CI/CD pipelines and cost dashboards" and "Real-time analytics for development value metrics" are listed as future work.

**Instacart** has playbooks and measures none of them. From [AI-driven development at Instacart](https://www.instacart.com/company/how-its-made/ai-driven-development-at-instacart-scaling-impact-and-increasing-velocity), 21 August 2025: "Playbooks: Living documentation capturing prompt engineering tips, model selection strategies, and feedback techniques." The measurement content of the whole post is "up to 20% time savings on frontend workflows" plus two anecdotes, and their own framing is honest about it: "Whenever possible, we tried to quantify the value." A widely circulating claim of 60% engineering adoption and 70,000 lines generated monthly is attached to this article by search engines and is not in its text. Do not cite it.

**Commonwealth Bank** publishes one cohort comparison and one unusually frank caveat. From [The evolution of AI software engineering](https://medium.com/commbank-technology/the-evolution-of-ai-software-engineering-75a8a5a02c14), 21 August 2025, Brent McKendrick: "The engineers who use these tools are showing up to 3x increase in the number of merged pull-requests, compared to the cohort who have not adopted these tools. Whilst these numbers are imperfect, they do provide some insight into the efficiency gains that we might expect". The companion post, [Project Coral](https://medium.com/commbank-technology/project-coral-how-were-orchestrating-ai-agents-for-development-at-scale-e0e11b9f0e2a), 27 August 2025, describes four named agents and contains no numbers at all. Both carry a disclaimer that they are the author's opinion rather than the group's.

**Canva** publishes nothing on this from engineering. The [engineering blog index](https://www.canva.dev/blog/engineering/) has no AI tooling post. The one company-published internal figure is from an interviewing post, [Yes, you can use AI in our interviews](https://www.canva.dev/blog/engineering/yes-you-can-use-ai-in-our-interviews/), 11 June 2025: "almost half of our frontend and backend engineers are daily active users of an AI assisted coding tool." The often-quoted per-agent success rate is secondary, from [Computer Weekly, 24 October 2025](https://www.computerweekly.com/news/366633425/Canva-to-save-30000-work-hours-with-agentic-AI), quoting Michael Denari, Canva's head of IT: an expense-reimbursement agent "attempts to answer the query, which it currently does successfully about 60% of the time." That unit is a Workato business-automation recipe, not an engineering agent skill, and 60% is a success rate rather than an invocation count.

**Mercado Libre**. Nothing found on measuring internal agent skills, reported as a clean negative. What they publish instead is production LLM eval work, such as [Tale of a prompt development](https://medium.com/mercadolibre-tech/tale-of-a-prompt-development-c133081bca1e), 10 April 2025, where precision moved from 38% to 65% and recall from 13% to 79% against a target of 95% precision at under $0.001 per request. Note that "skill" in Mercado Libre's Verdi system means a runtime node, not a developer agent skill. Several circulating figures attributed to Mercado Libre, including 500K PRs reviewed by agents, trace to a single social media post and could not be verified against any recording, transcript or company publication.

**Notion**. The cleanest negative. [How Notion uses Custom Agents](https://www.notion.com/blog/how-notion-uses-custom-agents), 24 February 2026, is a first-party post about internal agent use containing zero numbers. Its inventory claim is "tons of agents running across the company." A vendor case study, [Notion Claude Managed Agents](https://claude.com/customers/notion), describes a mechanism with no measurement attached, quoting Eric Liu, Product Manager: "Claude identifies lessons from finished tasks and updates the skills that future sessions draw from, so agent performance improves over time without manual maintenance." Note that page is about Notion's product and its numbers belong to Notion's customers, not to Notion.

## Corrections this research forces on findings.md

One, and it removes a citation.

Uber was checked first and needed no correction. [findings.md](../findings.md) already reads "the row above is secondary on everything except the headline counts", which is exactly right: both counts are verbatim in Uber's own post, quoted above, and the registry and evaluation-feedback framing is not.

**The Commonwealth Bank claim is unsupported.** [findings.md](../findings.md) says "Commonwealth Bank calls the split a core design principle", meaning central versus team-local. The nearest sentence in the primary source is not that split. It is a lead-team-first pattern inside each domain: "we have found that it is best for a leading team in each domain (or project) to first build rules and agent modes/configurations to clearly direct and constrain the agent, along with connecting the appropriate knowledge bases (RAG) and tools (MCP) so that the rest of the team can hit the ground running". No central catalogue is described anywhere in Commonwealth Bank's published material. The two-tier pattern still holds on LinkedIn, AutoScout24, Uber and GitLab. The Commonwealth Bank citation should come out.

One more claim needs rechecking rather than correcting, on skill listing truncation order. It is covered under "What we could not verify" below.

## What this means for us

Five things follow.

1. **Per-skill invocation counting is unpublished territory.** If we instrument it, we are not catching up. LinkedIn and Stripe both claim the mechanism and publish nothing from it. GitLab's repo, the most sophisticated public analogue, has fourteen CI jobs and none of them measures anything.

2. **But Atlassian's rejection has to be answered, not ignored.** A usage count tells you a skill fired, not that it helped. If we count invocations we should say what decision the count changes. The honest answer is narrow and still worth having: it is the input to a retirement decision, because a skill that never fires is pure listing cost. It is not evidence the skill works. That is what the harness is for.

3. **Plugin distribution costs us per-skill cost, not per-skill counts.** One environment variable buys back the counts. Nothing buys back the cost attribution on the OpenTelemetry metrics, where a third-party plugin's skills report as `"third-party"`. That is a real trade against the distribution benefit, and [skill-usage-telemetry.md](skill-usage-telemetry.md) measures both halves of it on this machine.

4. **The retirement signal already exists locally.** `/skill-doctor` flags skills in the listing that have never been invoked, over a lifetime counter that persists across sessions. It reports to one developer, not to the catalogue, so the aggregation is the open problem, not the detection.

5. **The only published retirement rule is eval-driven, not usage-driven.** Anthropic prunes when the model no longer needs the scaffolding, verified by a before and after eval run on every edit. Our `harness/` gate is closer to that practice than to anything else published, and Anthropic's staleness hook is the cheapest thing here to copy: a CI check that fails a change to the thing a skill documents when the skill file is untouched.

## What we could not verify

- A claim in [findings.md](../findings.md) that should be rechecked. It records that "When the budget overflows, Claude Code truncates the least-used descriptions first." The current skills documentation documents the 1,536 character per-skill truncation and documents no overflow ordering at all, least-used or otherwise. That is absence of evidence rather than a contradiction, and the cross-session usage tracking the claim implies does turn out to exist, in `skillUsage`. Whether the truncator reads it was not traced.
- Whether the Claude Enterprise "Skills report their own usage and cost" capability is per skill name or per skill category. The announcement does not say, and there is no API reference for it.
- Whether LinkedIn's or Stripe's per-asset usage data has ever been published anywhere. Neither post carries a figure.
- What DORA's full report PDFs say. Only the landing and capability pages were read.
- Whether any company retires a skill on a usage trigger. Nothing found, in any source, primary or secondary.
- Salesforce's internal benchmark for the Foundation Plugins. They assert it shows the curated skills improve accuracy and reduce cost, and publish no figure.
- Several numbers circulating about Mercado Libre, including 500K PRs reviewed by agents and a 90% autonomous coding target. They trace to a social media post and an image-only investor PDF, and could not be confirmed against any company publication.
- A claim of 60% engineering adoption and 70,000 lines generated monthly at Instacart. Search engines attach it to their August 2025 post and it is not in that post's text.
