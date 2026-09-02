# What we should build, and how

The reasoning behind every choice here lives in [findings.md](findings.md). This document is the plan.

## The goal

One catalogue of agent skills, shared across every team, installed by policy and versioned in git. A merge here reaches every developer without anyone asking.

| Goal | Mechanism |
|---|---|
| One source for shared skills | Every shared skill lives in this repo |
| Teams keep their autonomy | A team adds skills without asking the platform team |
| Updates reach everyone | Auto-update on our marketplace, version bumped by CI |
| Cheap to run | Entry points cost no context. Engines are capped |
| Right skill, right repo | Split by technology, detected and enforced four ways |
| Reviewable like code | Pull requests, CODEOWNERS, linter, evals |

## 1. The shape: engines and entry points

Two kinds of skill.

**Engines** are model-invocable. Claude picks one up when a task matches its description. Each does one job and other skills reuse it.

**Entry points** carry `disable-model-invocation: true`, which keeps their description out of the model's context entirely. The developer types them. Most delegate in one line.

```
Call the Skill tool with "acme-review", using "acme-kafka-context".
```

| | Engines | Entry points |
|---|---|---|
| Triggered by | Claude | The developer types it |
| Costs listing context | Yes | No |
| Owned by | Platform team | The team that wrote it |
| Cap | About 12 | None |

This split is the whole economic argument. The skill listing gets 1% of the context window and truncates the least-used descriptions when it overflows. Engines compete for that. Entry points never enter it, so a team can add fifty without slowing anyone down.

Carve engines by **invocation moment**, not by type. One `acme-authoring` engine with a decision tree beats five type-named skills that force the model to triage between them.

Composition is prose, so there is no dependency file and no build step. Always write `Call the Skill tool with "name"`. Never `/name`.

## 2. Targeting: the right skill in the right repo

One service is event-driven on Kafka, another is CRUD on SQL. A skill firing in the wrong repo is worse than no skill, because the developer trusts it.

Four layers. Use all four.

### Split the catalogue by technology

`acme-eventing-kafka`, `acme-data-sql`, `acme-web-react`. Never one `acme-standards` holding everything.

Engines stay technology-neutral. Entry points carry the technology.

### Detect the stack and suggest

```json
"relevance": {
  "topic": "Kafka",
  "signals": {
    "manifestDeps": [
      { "file": "[/\\\\].*\\.csproj$", "pattern": "Confluent\\.Kafka" }
    ]
  }
}
```

`manifestDeps` matches a regex against a manifest's contents, so the stack declares itself. Matching runs on the developer's machine and reports nothing back. Claude Code never auto-installs. Requires `pluginSuggestionMarketplaces` in managed settings.

### Let the repo opt in

```json
{
  "extraKnownMarketplaces": {
    "skills-marketplace": {
      "source": { "source": "github", "repo": "yourorg/skills-marketplace" }
    }
  },
  "enabledPlugins": { "acme-eventing-kafka@skills-marketplace": true }
}
```

This is the deterministic layer. Prefer it wherever the answer is already known.

### Scope the description by when, not what

| Bad | Good |
|---|---|
| Messaging patterns and conventions | Use when adding a Kafka producer or consumer in an event-driven service |
| Database guidance | Use when writing EF Core queries or migrations against SQL Server |

If two skills could plausibly fire on the same request, one is wrong.

## 3. Verification

Four layers, cheapest first.

| Layer | Tool | Cost | Runs | Blocking |
|---|---|---|---|---|
| 1. Manifests | `claude plugin validate . --strict` | Free | Every PR | Yes |
| 2. Referential integrity | xUnit | Free | Every PR | Yes |
| 3. Trigger accuracy | `claude plugin eval` | Model calls | Changed skills, every PR | Yes |
| 4. Does it help | `claude plugin eval --ablation` | Model calls | Merge and nightly | Yes |

Layers 1 and 2 catch most real breakage for nothing. Build them first.

Layer 2 ports the pattern from `Edict.AgenticTooling.Architecture.Tests`. Ours asserts that every `Call the Skill tool with "X"` resolves to a real skill, that every engine has at least one caller, that names carry the right prefix, and that nobody wrote `/name`.

An LLM eval earns its cost only when no cheaper check can answer the question. Two qualify: does the skill fire when it should and stay quiet when it should not, and does it improve the output at all. Do not LLM-grade general output quality.

> **The rule.** An engine joins the catalogue only when an ablation run shows a positive delta. No delta, no engine.

Write cases in both directions, including a should-not-fire prompt borrowed from another stack. Anthropic asks for 3 to 5 cases per skill and says authors should not review their own work.

Budget: each case runs three times, ablation doubles it. Twelve engines at four cases is about 288 runs per full pass. Use `--case` on pull requests, full suite on merge and overnight.

Start evals with the first engine, not before. Evals over an empty catalogue are theatre.

## 4. Updates

The defaults work against us. Three settings decide whether a merge reaches anyone.

| Setting | Why |
|---|---|
| `"autoUpdate": true` on our `extraKnownMarketplaces` entry | Third-party marketplaces have auto-update **off** by default |
| `FORCE_AUTOUPDATE_PLUGINS=1` | `DISABLE_AUTOUPDATER`, which IT often sets, stops plugin updates too |
| Version bump in CI on merge to main | A pinned version never updates until someone bumps it |

What a developer experiences:

| Moment | What happens |
|---|---|
| We merge, CI bumps | Nothing yet |
| Next session starts | Check runs after a random delay of up to 10 minutes |
| New version found | Downloads in background. Running session keeps what it launched with |
| They get told | Notification asks for `/reload-plugins` |
| They ignore it | Loads next launch |

The honest promise is next session, not instant.

Never declare `headersHelper` on a marketplace entry, because auto-update skips those plugins.

## 5. How it ships: three steps

| Step | Build | Deploy | Add it when |
|---|---|---|---|
| 1. Git only | Nothing | Nothing | Now |
| 2. C# CLI | One console app, packed as a dotnet tool | Nothing | Repos need different sets resolved at runtime |
| 3. MCP server | One ASP.NET Core app | One Azure Container App | The catalogue outgrows what is worth installing everywhere |

**Step one** does the whole job today. `marketplace.json` points at the plugins, managed settings push it, Intune or Group Policy delivers it. Developers install nothing by hand and cannot drift, because managed settings are read-only to them.

**Step two** adds a `"source": "command"` entry. Claude Code runs our binary and reads one absolute path from stdout. The binary reads the repo's `.csproj` or `package.json` and writes only the skills that repo should have, which makes targeting deterministic instead of suggested. It re-runs once per session regardless of the auto-update setting, so it is also the strongest freshness guarantee available. Keep it under two seconds.

`Edict.ClaudeSkills` is the working reference for this: `PackAsTool`, skills as embedded resources, an install command. Same trick, Roslyn instead of a regex.

**Step three** exposes `search_skills` and `get_skill` as MCP tools so Claude fetches a rare skill instead of every machine installing it. Two tool definitions cover any number of skills.

Do not build step three until step one or two hurts. Most companies in the research never got past step one.

## 6. Rules we keep

- Prefix shipped skill names. They land in one flat list beside personal, project and third-party skills.
- One composition idiom: `Call the Skill tool with "name".`
- Autonomy for updates, governance for additions. Any team may open a PR against any skill. The platform team gates new plugins only.
- CODEOWNERS per plugin. The linter gates the merge regardless of who approved.
- Cap the engines. Every engine costs listing budget on every request, forever.
- Keep `.claude/skills/` and `plugins/*/skills/` apart. Do not promote a dev tool by copying it.
- Authors do not review their own skills.

## 7. Sequence

| # | Task | Depends on |
|---|---|---|
| 1 | Port the interlock tests from Edict into an xUnit project | Nothing |
| 2 | CI: `validate --strict`, interlock tests, automatic version bump on merge | 1 |
| 3 | Decide what `core` ships, and write the first engine | Nothing |
| 4 | Write that engine's eval cases in the same PR | 3 |
| 5 | Add a second plugin, `acme-eventing-kafka`, with a real `relevance` block | 3 |
| 6 | Hand IT the managed-settings block | 2 |
| 7 | Revisit step two once repos genuinely need different sets | 5 |

Item 2 is the one that fails silently if skipped, because without the version bump nobody ever receives an update.

## 8. Three things to confirm with IT before rollout

- `autoUpdate: true` on our `extraKnownMarketplaces` entry.
- `FORCE_AUTOUPDATE_PLUGINS=1` if they set `DISABLE_AUTOUPDATER`.
- `disableCommandPluginSources` left off, so step two stays open.

Also confirm `claude plugin eval` is enabled for the target organisation before making it a required check. It runs here, but it is granted per organisation. Layers 1 and 2 are the only ones guaranteed everywhere.
