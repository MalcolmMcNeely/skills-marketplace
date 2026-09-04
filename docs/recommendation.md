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

**End every description with a negative boundary** naming the nearest technologies it is not for: "Not for Vue files, backend code or end to end tests." This is measured, not stylistic. Without one, a request that signals breadth pulled in a mean of 6.0 skills out of 12. With one, 1.2. It costs nothing detectable in recall, and it keeps the intent wording this section already requires.

Two skills may fire on one request when the request genuinely spans both. That is correct and measured: a task naming EF Core and React fired exactly those two skills in 10 runs of 15, with ten decoys installed and not one of them firing in any of 44 runs across four description variants. What must not happen is two skills firing because their descriptions cover the same work. Test for the overlap, not for the co-firing.

The layer that does the real work here is per-repo enablement, not description tuning. In a C#-only repository the React skill is not installed and cannot misfire however the request is phrased. Keep `enabledPlugins` tight per repo rather than shipping the whole catalogue everywhere.

One measured risk to plan around. What fires is decided by how the developer phrases the task, not by how we design the catalogue. A short, vague request fired almost nothing. The same request with "do the whole thing end to end" appended fired up to eleven of twelve skills. Under-firing is the more common failure and the more dangerous one, because nobody notices a skill that did not fire. [Targeting](skill-targeting.md) has the full numbers.

## 3. Verification

"Eval" is not one thing. Firing accuracy and outcome quality are separate machines with separate costs. [Evals](evals.md) works this out in full; this is the plan that falls out of it.

Seven layers, cheapest first.

| Layer | Tool | Cost | Runs | Blocking |
|---|---|---|---|---|
| 1. Manifests | `claude plugin validate . --strict` | Free | Every PR | Yes |
| 2. Referential integrity and budget | xUnit | Free | Every PR | Yes |
| 3. Firing accuracy | Our harness, modelled on `run_eval.py` | 125 calls per engine, about `$24.50` | PRs touching a description | Undecided, see below |
| 3b. Cross-stack firing | Same harness, asserting the **set** that fired | ~9 calls per pair | PRs touching a description | Undecided, see below |
| 4. Contract assertions | Same harness, invoking the skill **by name** | 5 calls per case, about `$1.51` | PRs touching a body | Undecided, see below |
| 5. Admission delta | `skill-creator` Benchmark | High | New engines only | Yes, once |
| 6. Version comparison | `skill-creator` Improve mode, blind A/B | High | On request, by the author | No |
| 7. Human review | A second person, plus fixed scenario scripts | Minutes | Every PR | Yes |

**Do not design around `claude plugin eval`.** It is gated on this account today and refuses even to scaffold. Its `--help` works, so the spec is readable, but every invocation exits 1 with "`plugin eval` is currently in early access". Nothing blocking can depend on it until the entitlement lands. When it does, it replaces our harness for layers 3 to 5.

Layers 1 and 2 catch most real breakage for nothing. Build them first.

Layer 2 ports the pattern from `Edict.AgenticTooling.Architecture.Tests`. Two warnings the earlier draft of this plan got wrong. The composition idiom is **four different phrasings** across seven files, so extract every quoted skill name on a line mentioning the Skill tool rather than matching one string. And `/name` cannot be a blanket grep, because slash forms appear about sixty times as legitimate prose describing commands a developer types. Ban the slash form only in a delegation instruction.

Layer 2 also owns the budget: every description under 1024 characters, and the shipped total inside the listing budget.

An LLM eval earns its cost only when no cheaper check can answer the question. Grade a skill's **contract**, meaning a format it mandates, a tool it requires, an action it forbids, a file that must exist. Do not LLM-grade general output quality. A skill with no contract is a human review problem, not an eval problem.

> **The rule.** An engine joins the catalogue only when an ablation run shows a positive delta **net of token cost**. No delta, no engine.

Ablation answers admission, not regression. It compares the skill against nothing, so it cannot tell you whether v2 beats v1. Both arms contain v2. Version comparison is layer 6 and lives in `skill-creator`'s Improve mode.

Tier 3 asserts the **set** of skills a query fires, not one skill, and runs with the whole catalogue installed. A cross-stack task has two right answers at once, so a per-skill assertion cannot see it. Write three cases for every two technologies that meet in a real repository: one naming both, one naming neither, one naming only the first. The middle case is the valuable one and the one nobody writes. Add these from the start, because a case scored against a single skill keeps no record of what else fired. The harness must also check the run's exit code before scoring it, or it will read its own timeouts as negative results.

**Set the should-fire threshold from measured behaviour, not from an expectation of 100%.** A well-specified, unambiguous cross-stack request loaded the right skills in only 10 runs of 15, and no description wording tested removed the misses. A failing run fired nothing rather than the wrong thing. A gate demanding a perfect trigger rate will fail on healthy skills. Calibrate against a baseline measured on the frozen good fixture, then set the bar as the highest value a good skill clears 95% of the time. At a good rate of 0.67 over 60 runs that puts the gate near 0.57, with a 4% false-fail rate and 82% power to catch a drop to 0.50. Recompute it on any model, CLI or catalogue change. [Scoring](scoring.md) carries the formula.

**Layer 4 invokes the skill by name.** Put `/skill-name` in the prompt rather than hoping the description matches. By-name invocation loaded the skill in 7 runs of 7, against 10 of 15 for natural language, and emits the same `Skill` tool call the harness already parses. Layer 3 then owns the description and layer 4 owns the body, with neither depending on the other. Without this, every contract result carries a hidden firing failure inside it.

**Score runs into verdicts, not numbers, and throw away the broken ones.** A run counts only if it exited 0 **and** emitted `"subtype":"success"`. Allowlist that; do not denylist failures. A forced budget abort exits 1 with `"subtype":"error_max_budget_usd"` and no `Skill` call, which a naive parser reads as "the skill declined to fire". Anything else is void and gets resampled, with a cap. Hitting the cap is a layer 3 failure and must be reported as one.

**Pool runs across the suite; do not score case by case.** Requiring every case to pass is unusable at any affordable run count: a skill firing at the measured 2 runs in 3 passes a ten-query suite under a per-query rule 5% of the time at three runs, and 42% at fifteen. Pool every valid run so granularity is 1/N, gate on the pooled rate, and keep per-case rates as diagnostics with a zero-floor guard.

Case counts: twenty-three cases per engine for firing, being ten should-fire, ten near-miss should-not-fire and three ambiguous watch-list cases that are run but never gated. Should-fire needs six runs each, should-not-fire five, per [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10). The negative side moved from three runs to five because the earlier zero-in-44 result was measured against distant decoys and ours are deliberately close. Contract cases get five by-name runs, because three catch a break that happens 30% of the time only 66% of the time, and five catch it 83%. Roughly 125 runs per engine per pass. Borrow a should-not-fire prompt from another stack. An author should not be the only reviewer of their own skill.

Budget per full pass, at twelve engines: a firing run measured about **$0.196** and roughly 40 seconds, so [#10](https://github.com/MalcolmMcNeely/skills-marketplace/issues/10)'s 125-run suite costs about **$24.50** per engine, putting a twelve-engine nightly nearer **$300** than $43. The earlier $0.043 was measured on read-only probes with writes disallowed, and does not apply to a run that creates files. [Harness skeleton](harness-skeleton.md) carries the measurement.

**Where layer 3 runs is not settled.** At $24.50 a pass, blocking every description pull request needs a decision it has not had, and the pass mark it would enforce still rests on an uncalibrated 0.67 borrowed from a different pair of stub skills. [#11](https://github.com/MalcolmMcNeely/skills-marketplace/issues/11) ruled that policy out of scope until there is a real catalogue to act on. Until then the paying layers run on demand only, with `--max-budget-usd 0.60` per run and a suite ceiling of $50.

Fixtures load with `--plugin-dir <path>`, which loads a plugin for one session only and is repeatable. Good and broken fixtures therefore live as separate directories and never enter the shipped catalogue.

Start layers 3 and 4 with the first engine, not before. Evals over an empty catalogue are theatre. Layers 1, 2 and 7 start now.

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
