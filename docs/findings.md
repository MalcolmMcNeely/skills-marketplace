# Findings

What the research turned up, and what we verified on this machine. Written September 2026. Everything below is either cited or was run locally.

## The short version

The tooling has run ahead of the practice by about a year. Roughly fifteen companies have published a real internal system for sharing agent skills, and about five describe it well enough to copy. Not one of them documents versioning, discovery, quality gating and evals together.

So there is no settled standard to follow. There is a well-built vendor mechanism, a handful of worked examples, and a set of failure modes that are better documented than the fixes.

## What companies actually do

Nobody used a package registry for skills. Nobody pushed loose skill files by MDM. The mechanisms in use are duller than that.

| Company | Mechanism | Scale |
|---|---|---|
| AutoScout24 | Git repo as a governed registry, semver, per-plugin CODEOWNERS, linter as a required CI check | 16 plugins |
| Cloudflare | Markdown compiled to one JSON config, served from a Worker at a `.well-known` URL | 3,000+ people |
| GitLab | One repo, five distribution channels, per-plugin semver, lefthook and CI gating | Public |
| Uber | Managed marketplace with quality checks and evaluation feedback | 3,600 skills, 30k runs a day |
| LinkedIn | Two-tier git: central playbooks and repo-local ones, MCP meta-tools for discovery | 500+ playbooks |
| DoorDash | One YAML per playbook behind a gateway | 300+ playbooks |
| Duolingo | `AgentDefinition` registry with owner as a first-class field, benchmark suite on every change | Not stated |
| Block | Goose recipes, two reviews per new MCP server, one from MCP experts and one from security | 60+ MCP servers |

Sources: [AutoScout24](https://tech.autoscout24.com/blog/posts/designing-a-coding-agent-skills-marketplace/), [Cloudflare](https://blog.cloudflare.com/internal-ai-engineering-stack/), [gitlab-org/ai/skills](https://gitlab.com/gitlab-org/ai/skills), [Uber](https://www.uber.com/us/en/blog/efficient-software-factory/), [LinkedIn](https://www.linkedin.com/blog/engineering/ai/contextual-agent-playbooks-and-tools-how-linkedin-gave-ai-coding-agents-organizational-context), [DoorDash](https://careersatdoordash.com/blog/delegating-engineering-work-to-cloud-based-agents/), [Duolingo](https://blog.duolingo.com/aislackbot/).

### Patterns that recur

- **Two tiers.** Central for cross-cutting work, team-local for the rest. Commonwealth Bank calls the split a core design principle. LinkedIn, AutoScout24, Uber and GitLab all do the same.
- **A linter as a required CI check** does the gating, not human review. AutoScout24 puts it plainly: the PR cannot merge unless the linter passes, regardless of who approved it.
- **Auto-install beats browse.** Duolingo built a browsable internal directory and killed it. One click of friction was too much.
- **Everyone started grassroots.** Nobody designed the registry first, then filled it.

### The counter-examples are worth reading too

Ramp decentralised on purpose and says so on the record. Spotify published the most quantified rollout available, 99% weekly adoption and 2.5 million automated PRs, and never mentions skill sharing at all; their answer is to standardise the codebase instead. OpenAI keeps everything repo-local by design.

That absence in the strongest case study tells you how unsettled this is.

## The failure modes, with evidence

Our starting concern is documented, not imagined.

| Failure | Source |
|---|---|
| Version drift from Confluence copy-paste | Thoughtworks Radar Vol. 34, *Claude Code plugin marketplace*, Trial |
| "100 slightly different fragile implementations" | LinkedIn, QCon AI |
| CLAUDE.md quality "varies widely across teams" | [Salesforce](https://www.salesforce.com/news/stories/how-engineering-became-agentic/) |
| Two similar descriptions fire interchangeably, behaviour turns non-deterministic | [O'Reilly Radar synthesis](https://www.oreilly.com/radar/), May 2026 |
| Instruction bloat: rules buried mid-context get ignored | Thoughtworks Radar, *Agent instruction bloat*, Caution |
| 36.8% of 3,984 public skills carry a security flaw | [Snyk ToxicSkills](https://snyk.io/blog/toxicskills-malicious-ai-agent-skills-clawhub/) |

Two of OWASP's ten Agentic Skills risks are *Update Drift* and *No Governance*. That project is still an Incubator, targeting v1.0 in Q4 2026.

One number puts the maturity in perspective: an empirical study of about 2,900 repositories found skills in **5.4%** of them.

## How Claude Code actually works

This is the part that decides our design, so it is all verified against current docs.

### Skill sources

Enterprise managed settings, personal `~/.claude/skills/`, project `.claude/skills/`, plugin `skills/`, and claude.ai sync. Precedence runs enterprise, then personal, then project.

**MCP is not a skill source.** An MCP server cannot deliver a `SKILL.md` to Claude Code. The feature request was closed as not planned. This single fact rules out the design most people reach for first.

### Frontmatter that matters

| Field | Effect |
|---|---|
| `disable-model-invocation: true` | Removes the description from the model's context. Stays available as a slash command |
| `user-invocable: false` | Only the model can invoke it. The description stays in context |
| `allowed-tools` | Pre-approves tools for that skill |

The first one is the load-bearing discovery. It makes a thin entry point cost nothing.

### Listing budget

| Setting | Default |
|---|---|
| `skillListingBudgetFraction` | 0.01, meaning 1% of the context window |
| `skillListingMaxDescChars` | 1,536 |
| `skillOverrides` | `on`, `name-only`, `user-invocable-only`, `off` |

When the budget overflows, Claude Code truncates the least-used descriptions first. `/doctor` reports the cost.

### Distribution and enforcement

`marketplace.json` catalogues plugins. Sources include `github`, `git-subdir`, `url`, `npm`, `archive` with a sha256, and `command`.

Admins enforce a catalogue through managed settings delivered by MDM, registry policy or a system file: `extraKnownMarketplaces`, `enabledPlugins`, `strictKnownMarketplaces`, `blockedMarketplaces`, `disableCommandPluginSources`.

The `command` source is the interesting one. Claude Code runs a binary and reads one absolute path from stdout. Timeout defaults to 60 seconds with a 600 second ceiling. It re-runs once per session and installs the output when the content hash changed, which means it ignores both the auto-update setting and `DISABLE_AUTOUPDATER`.

### Updates

Verbatim from the docs: "`claude-plugins-official` and most other official Anthropic marketplaces have auto-update enabled by default. Third-party and local development marketplaces have auto-update disabled by default."

We are third-party, so ours is off unless an admin sets `"autoUpdate": true` on the `extraKnownMarketplaces` entry.

The check runs after session start with a random delay of up to ten minutes. The running session keeps what it loaded at launch. A notification asks the user to run `/reload-plugins`, and otherwise the new version loads next launch.

Two traps. `DISABLE_AUTOUPDATER` stops plugin updates as a side effect, so pair it with `FORCE_AUTOUPDATE_PLUGINS=1`. A marketplace entry declaring `headersHelper` is skipped by auto-update entirely.

Version resolution reads the marketplace entry first, then `plugin.json`, then the resolved commit SHA. A pinned version that nobody bumps means nobody ever updates.

### Relevance

A `relevance` block on a marketplace entry suggests a plugin when the session matches a signal.

| Signal | Matches | Limit |
|---|---|---|
| `cwd` | Globs on the working directory. The only signal that fires at session start | 10 |
| `cli` | Command names Claude ran. Compound commands record only the leading token | 10 |
| `hosts` | Hostnames in URLs in Bash commands | 20 |
| `filesRead` | Globs on files Claude read, wrote or edited | 10 |
| `manifestDeps` | `{file, pattern}` regexes against a manifest's path and its **contents** | 10 |

Matching happens on the user's machine and reports nothing to Anthropic or to the marketplace operator. Claude Code never installs automatically. A suggestion appears at most once every three sessions and stops once installed. Nothing appears until an admin lists the marketplace under `pluginSuggestionMarketplaces`.

`manifestDeps` is the one that detects a stack without anyone declaring it, because it reads file contents.

### What Claude Code takes from an MCP server

Tools, named `mcp__server__tool`. Prompts, which appear as `/mcp__server__prompt`. Resource support is undocumented, and so is whether Claude Code acts on `list_changed`.

## Skills over MCP, and why we are not using it

There is an official MCP working group for serving skills over MCP, co-led by Anthropic. The draft spec is SEP-2640, which serves skills as `skill://` resources so an agent reads names cheaply and fetches bodies on demand.

It ships in FastMCP 3.0, in Microsoft Agent Framework as `AgentSkillsProviderBuilder.UseMcpSkills` at alpha, and in Azure Foundry toolboxes at preview.

It does not ship in Claude Code, which is where our developers work. That closes the question for now.

Only two products let an agent acquire a capability mid-session at all: Docker Dynamic MCP, experimental and session-scoped, and Kong's MCP Registry, tech preview. Both search a list an admin already approved.

## .NET specifics

`dnx` arrived in .NET 10 and works like `npx`. It runs an MCP server straight from NuGet and accepts `--source` for a private feed such as Azure Artifacts. NuGet recognises an `McpServer` package type and an embedded `.mcp/server.json`.

The official C# SDK publishes `ModelContextProtocol`, `.Core`, `.AspNetCore`, `.Extensions.Apps` and `.Extensions.Tasks`. Verified shapes:

```csharp
// stdio
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// HTTP, scalable behind a load balancer
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();
app.MapMcp();
```

Tools use `[McpServerToolType]` and `[McpServerTool]`. The docs state prompts and resources work the same way with `[McpServerPromptType]` and `[McpServerResourceType]`, but publish no sample.

Nobody ships agent **skills** as a NuGet package or `dotnet tool`. MCP **servers**, yes, extensively.

## Verification tooling, tested here

| Tool | Status on this machine |
|---|---|
| `claude plugin validate [--strict]` | Available. Free, deterministic |
| `claude plugin eval` | **Gated.** Every invocation exits 1 with "`plugin eval` is currently in early access". Only `--help` works |
| `claude plugin eval init --bare` | Gated, same message. No scaffold could be produced |
| `skill-creator` | Present in the official marketplace, ready to install |

**Corrected on 2 September 2026.** This section previously recorded `plugin eval` as available on this account. It is not. Whether that is a revocation, a rollout change or an error in the original record, we cannot tell. Nothing blocking may depend on it. [evals.md](evals.md) carries the full working out and the plan that replaces it.

`plugin eval` runs cases from `evals/<case>/prompt.md` plus `graders/*.md`, or `case.yaml`. Each case runs three times by default. The LLM judge defaults to Haiku. The six grader names recorded here earlier, `regex`, `tool_used`, `tool_order`, `file_exists`, `llm` and `baseline`, appear in no primary source we can now find, and with the CLI gated we cannot confirm them.

`--ablation with-without` runs a second arm with no plugin and reports the score delta. It is **on by default**, not opt-in. It answers whether a skill beats nothing, which is an admission question. It cannot tell you whether v2 beats v1, because both arms contain v2. Version comparison lives in `skill-creator`'s Improve mode.

`--threshold` exits 1 below a score. Its default is **1.0**, which against the default of three runs makes the out-of-the-box configuration unusable as a gate. `--case` and `--tag` narrow a run.

`skill-creator` adds what `plugin eval` lacks: blind A/B between two versions tracked in `history.json`, and `improve_description.py`, which generates should-trigger and should-not-trigger prompts, measures the hit rate, and proposes description edits.

No primary source asks for "3 to 5 cases per skill". That claim was wrong. The real figures differ by half: roughly twenty queries for firing accuracy, two or three cases per contract for outcome quality. The rule that authors should not review their own work is a sound house rule, but the nearest primary text is about context contamination in a fresh session, not about peer review, so do not attribute it to Anthropic.

## Composition works, and here is the proof

Skills have no version field and no declarative dependency mechanism. That is true, and it misled us at first.

Composition happens in prose. A skill names another skill and the model calls it. `mattpocock/skills`, the most-installed community plugin, is built this way. Measured from the copy in `.claude/skills/`:

| Skill | Words | Called by |
|---|---|---|
| `grill-me` | 6 | none |
| `grill-with-docs` | 9 | none |
| `grilling` | 275 | 5 skills |
| `domain-modeling` | 435 | 4 skills |
| `wayfinder` | 1,952 | none |

`grill-with-docs` is one line:

```
Call the Skill tool twice, for "grilling" and "domain-modeling".
```

Thin entry points, shared engines, per-skill reference files. Thirteen of the thirty-five upstream skills reference another skill. Upstream also excludes ten skills from `plugin.json`, which acts as a promotion gate.

Naming the Skill tool is better than writing `/name`. Upstream uses the slash form; our vendored copy uses the tool form, and the tool form is the one to keep.

## Prior art in this estate

`C:/Projects/Edict` already ships skills and an MCP server, both as `dotnet tool` packages, and ADR-0044 records the reasoning including a revisit on 2026-06-02 that considered plugins and rejected them.

| Piece | What it does |
|---|---|
| `Edict.ClaudeSkills` | `PackAsTool`, skills as embedded resources, installs into a consumer's `.claude/skills/` and wires `.mcp.json` |
| `Edict.Mcp` | MCP server over Roslyn, answers questions about handlers, route keys, silo wiring, glossary and ADRs |
| `SkillsDriftEvaluator` | Hash manifest driving `Create`, `Refresh` or `SkipDrifted` |
| `Edict.AgenticTooling.Architecture.Tests` | Deterministic tests over the skills themselves |

Two tests there are better than anything in the published research:

- `EveryRegisteredMcpTool_HasAtLeastOneSkillCaller`
- `EverySkillMcpToolReference_ResolvesToRegisteredTool`

Together they enforce referential integrity between prose and code in both directions, for free, with no model calls. `SkillPrescriptionTests` goes further and asserts that load-bearing instructions survive edits, using a regex and a 200-character proximity window.

ADR-0044 also carries two design rules worth reusing. Skills are carved by invocation moment, the *when*, not by type, the *what*, because five similarly-named type-based skills force the model to triage. And each MCP tool ships with a paired clause in a skill body telling the model when to reach for it, rather than relying on description matching alone.

Its reason for choosing NuGet over plugins does not transfer to us. Edict pins skill bodies to the consumer's library version by construction, and a git marketplace would lose that pin. We have no library to pin to.

## What we could not verify

- Whether Claude Code reads MCP resources at all, or acts on `list_changed`. Both undocumented.
- Verbatim C# samples for `[McpServerPrompt]` and `[McpServerResource]`. The docs assert they work like tools and show no code.
- Whether the MCP Registry has reached general availability. Every primary source still says preview.
- Uber's registry lifecycle detail rests on a vendor newsletter rather than Uber's own writing.
- Nubank's talk "We Vetted 2,000 AI Skills Before They Reached Developers" has no published content behind the title, and it looks like the most on-point case study in existence.
