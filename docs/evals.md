# Evals, and how to verify a skill change

Written 2 September 2026 against Claude Code 2.1.248. Everything below is either quoted from a primary source or was run on this machine.

## The short version

"Eval" is not one thing. Anthropic's own docs split it in two, and we should copy that split exactly: does the skill **fire** when it should, and does it **improve the outcome** once it has fired. The first is cheap, applies to every model-invocable skill, and needs no LLM judge. The second is expensive and only pays where the skill specifies something observable.

The owner's instinct was right and one word was wrong. The question "did my change make it better" is a **version-versus-version** comparison. `claude plugin eval --ablation with-without` does not answer it, because it compares plugin against no plugin. Only `skill-creator`'s Improve mode compares two versions.

Two things break the plan in [recommendation.md](recommendation.md) section 3 as written. `claude plugin eval` is **gated on this machine today** and refuses even to scaffold, so layers 3 and 4 cannot be required checks. And its defaults, three runs against a threshold of 1.0, fail a healthy skill about a quarter of the time.

`grilling` is more evaluable than it looks. Four of its six instructions are assertable from a single transcript with no model judging at all.

## The framing, judged

The owner asked four things. Here is the verdict on each.

| Thread | Verdict |
|---|---|
| How do skill evals work in Claude Code? | Two separate machines, not one. Documented for `skill-creator`, undocumented for `claude plugin eval` |
| How do you eval a conversational skill? | You assert its output **contract**, not its output quality. `grilling` has a contract |
| Are some skills unsuitable? | Yes, but the dividing line is not conversation versus artefact |
| Is the framing wrong? | The word is wrong, the need is right, and the tool everyone reaches for answers a different question |

### The split is Anthropic's, not ours

Verbatim from [skills.md](https://code.claude.com/docs/en/skills.md):

> Seeing a skill trigger tells you Claude found it, not that it did what you intended. To know a skill is working, measure two things separately: whether Claude invokes it on the prompts it should, and whether the output matches what you expect when it does.

This is not a stylistic preference. Anthropic ships two different systems for the two halves, with different file formats, different case counts and costs that differ by two orders of magnitude. Treating them as one thing called "evals" is what makes the problem look intractable.

### Where the framing goes wrong

The word "eval" carries an assumption borrowed from model benchmarking: one score, higher is better, compare the number before and after. Skills do not work like that, for three reasons.

**A skill has no single score.** Firing accuracy and outcome quality move independently. Broadening a description to fix a missed trigger usually costs precision elsewhere. A single number hides the trade.

**The comparison you want is not the one the tool makes.** Ablation asks "is this skill better than nothing". That is an admission gate for a new engine. It is the wrong instrument for "is v2 better than v1", which is what a pull request actually changes.

**Most breakage is not statistical.** A skill that names a skill that no longer exists, an entry point whose delegation target was renamed, a description pushed over budget. None of that needs a model call, and all of it is more common than a subtle quality regression.

The reframe offered in the brief, artefact versus conversation, is close but cuts in the wrong place. The evidence points at **contract versus disposition**. A skill is gradeable to the extent that it specifies something observable: a format, a required tool, a forbidden action, a file that must exist. `grilling` produces only conversation and is gradeable, because it mandates a question format and forbids acting before confirmation. `codebase-design` produces prose and is not gradeable, because it supplies vocabulary and no contract.

## What actually runs here

### `claude plugin eval` is gated

[findings.md](findings.md) records `claude plugin eval` and `claude plugin eval init --bare` as **Available**. That is no longer true on this machine. Every invocation exits 1.

```
$ claude plugin eval init --bare firing
`plugin eval` is currently in early access
EXIT=1

$ claude plugin eval init firing2
`plugin eval` is currently in early access
EXIT=1
```

Tested from a plugin root holding a valid `.claude-plugin/plugin.json` and a skill, from a bare directory, and from the repository root. Tested with `COPILOT_*` and `CLAUDECODE` stripped from the environment. Same result every time. No scaffold was produced, so the requested paste of a real `--bare` case layout does not exist and the schema below comes from `--help` only.

`--help` still prints, because the argument parser runs before the entitlement check. That is the entire public specification of the feature: there is **no eval page** in [llms.txt](https://code.claude.com/docs/llms.txt), and `plugins-reference.md`, `plugins.md` and `skills.md` contain no mention of graders, ablation or case files.

| Tool | Status here | Cost |
|---|---|---|
| `claude plugin validate [--strict]` | Works | Free |
| `claude plugin details <name>` | Works. Reports projected token cost | Free |
| `claude plugin eval` | **Gated.** Exit 1, early access | n/a |
| `claude plugin eval init --bare` | **Gated.** Exit 1, early access | n/a |
| `skill-creator` scripts | Public source on GitHub. Runnable now | Model calls |
| `claude -p --output-format stream-json` | Works | Model calls |

### The eval CLI, from `--help`

Cases live at `<eval dir>/**/case.yaml`, or `prompt.md` plus `graders/*.md`. The directory is `evals/` unless overridden by `--eval-dir` or by `experimental.evals` in `plugin.json`.

Flags worth knowing, all verified against `claude plugin eval --help` on 2.1.248:

| Flag | Default | Note |
|---|---|---|
| `--ablation <mode>` | `with-without` when a plugin resolves | Not opt-in. It is already on |
| `--runs <n>` | `case.runs ?? 3` | Three runs per case per arm |
| `--threshold <0..1>` | **1.0** | "Exit 1 if any case score is below this threshold" |
| `--judge-model` | `haiku` | The LLM judge is Haiku unless overridden |
| `--case <glob>`, `--tag <tag...>` | none | Narrow a run |
| `--max-cost-usd <usd>` | none | Aborts with exit 2. Overrun bounded to one agent run |
| `--allow-tools <tools...>` | none | Gated tools stay off until granted |
| `--mocks <mode>` | `record` | Stand-ins for MCP servers from `<eval dir>/mocks/` |
| `--scaffold` | **off** | Runs `scaffold_script` as you. Needed to build fixtures |
| `--json [path]`, `--report <path>` | none | Machine-readable results and a self-contained HTML report |
| `--no-publish` | off | **The HTML report publishes to claude.ai by default** |

Two defaults deserve attention before anyone wires this into CI. The report publishes externally unless told not to. And the threshold is 1.0, which is discussed below.

The ablation line is the load-bearing one, so here it is verbatim from the help output:

```
  --ablation <mode>         Run a no-plugin baseline arm and report the score
                            delta (none | with-without; default: with-without
                            whenever a plugin resolves — by name, or from the
                            target path — and none when nothing does; under
                            with-without, graders marked with-only, incl.
                            `tool_used: Skill`, are a plugin-fired indicator
                            rather than part of the score)
```

The `with-only` marker is the interesting discovery. `tool_used: Skill` is automatically treated as a **firing indicator** rather than as part of the score. The tool itself already separates "did it fire" from "did it help". That is the same split again, built into the grader model.

### What `validate` cannot see

`validate --strict` passed a skill whose only instruction points at a skill that does not exist, and which also uses the banned slash idiom:

```
$ cat broken/skills/thin/SKILL.md
---
name: thin
description: A thin entry point that delegates.
disable-model-invocation: true
---

Call the Skill tool with "this-skill-does-not-exist".
Also see /grilling which is the wrong idiom.

$ claude plugin validate broken/skills --strict
Validating components in: ...\broken\skills
✔ Validation passed
EXIT=0
```

It also passed `plugins/core`, whose `skills/` directory is empty. Manifest validation is worth having and it stops nowhere near far enough. Everything in this repo currently passes:

```
$ claude plugin validate . --strict
Validating marketplace manifest: C:\Projects\skills-marketplace\.claude-plugin\marketplace.json
✔ Validation passed
```

## Four tiers

| # | Tier | Answers | Model calls | Applies to | Runs |
|---|---|---|---|---|---|
| 0 | Static | Is it well formed and internally consistent? | None | Every skill | Every PR |
| 1 | Firing | Does it fire when it should and stay quiet otherwise? | Tiny. One turn, killed early | Every model-invocable skill | Every PR touching a description |
| 2 | Outcome | Does it improve the result once fired? | Full agent runs | Only skills with a contract | Merge and nightly |
| 3 | Human | Is it any good? | n/a | Everything | Every PR, by a second person |

Tier 3 is not a fallback. The Agent Skills guidance is explicit that it carries the load the other tiers cannot:

> Not everything needs an assertion. Some qualities — writing style, visual design, whether the output "feels right" — are hard to decompose into pass/fail checks. These are better caught during human review. Reserve assertions for things that can be checked objectively.

That passage is on [agentskills.io](https://agentskills.io/skill-creation/evaluating-skills), the Agent Skills spec site, not in the Claude Code docs. `skills.md` links out to it and it does not appear in the Claude Code docs index, which is why it is easy to miss.

### Tier 0 is the one we can build today

Free, deterministic, and it needs no early access, no auth and no network. It catches the failure class this repository is most exposed to, because our whole design leans on prose composition between skills.

What it should assert, beyond `validate --strict`:

- Every skill named in a delegation instruction resolves to a real skill.
- Every engine has at least one caller, or is deliberately exempt.
- Every description is under 1024 characters, the [specification](https://agentskills.io/specification) hard limit.
- The sum of shipped descriptions stays inside the listing budget.
- Names carry the right prefix.

This is the pattern [findings.md](findings.md) records from `Edict.AgenticTooling.Architecture.Tests`, where `EveryRegisteredMcpTool_HasAtLeastOneSkillCaller` and `EverySkillMcpToolReference_ResolvesToRegisteredTool` enforce referential integrity in both directions for nothing. Ours does the same between skills instead of between skills and tools.

One warning that [recommendation.md](recommendation.md) does not currently account for. The composition idiom is **not one string**. Measured across `.claude/skills`:

| Idiom | Count |
|---|---|
| `Call the Skill tool with "X"` | 5 |
| `call the Skill tool twice, for "X" and "Y"` | 4 |
| `call the Skill tool for whichever skills the ... block names` | 2 |

Seven files mention the Skill tool in four different phrasings. A test built on one regex would have found two references and silently passed the rest. Extract every quoted string on a line mentioning the Skill tool, then resolve all of them.

A second warning. The rule "never write `/name`" cannot be a blanket grep. `` `/grill-with-docs` `` appears 11 times and `` `/handoff` `` 9 times as legitimate prose describing commands a developer types. Ban the slash form only where it is a **delegation instruction**, meaning an imperative sentence telling the model to invoke another skill.

### Tier 1 is cheaper than anyone expects

The mechanism is in the open. `skill-creator`'s `run_eval.py` does not install the skill under test at all. It writes a stub command file into `.claude/commands/<name>-skill-<uuid>.md` carrying **only the description**, runs `claude -p` with `--output-format stream-json --include-partial-messages`, and watches the stream for the first `content_block_start` of type `tool_use`. If it is `Skill` or `Read` naming the stub, the query triggered. Anything else, or `message_stop` first, and it did not. Then it kills the process.

Three consequences follow.

**It tests the description in isolation from the body.** That is correct: the body is never in context at the moment the decision is made.

**It costs one turn.** No task is completed. The run dies at the first tool call or at a 30 second timeout, ten queries in parallel by default.

**The random suffix is deliberate.** A fresh name per run stops the real installed skill, and any prior naming, from contaminating the result. Echoing the docs on why authoring context poisons a test:

> A fresh session matters because leftover context from authoring the skill will mask gaps in the written instructions.

Defaults, read from the source of `run_eval.py`:

| Setting | Default |
|---|---|
| `--runs-per-query` | 3 |
| `--trigger-threshold` | 0.5 |
| `--timeout` | 30 seconds |
| `--num-workers` | 10 |

A should-trigger query passes when its trigger rate reaches 0.5. A should-not-trigger query passes when it stays below. `run_loop.py` splits the query set 60/40 into train and held-out test, iterates up to five times, and selects `best_description` by test score to avoid overfitting.

**Entry points are exempt by construction.** `grill-me` and `grill-with-docs` carry `disable-model-invocation: true`, which keeps their descriptions out of the model's context entirely. There is nothing to trigger and nothing to test. They cannot misfire. Tier 1 applies to engines only, which is another reason the engine cap matters.

## What is assertable: contract, not artefact

Sort skills by what they mandate, not by what they emit.

| Skill | Contract it states | Assertable at tier 2 |
|---|---|---|
| `unslop` | 31 named patterns. No em dashes, no curly quotes, sentence-case headings | **Fully, by script.** No judge needed |
| `resolving-merge-conflicts` | End state is a completed merge. "Always resolve; never `--abort`" | **Fully.** No conflict markers, clean tree, `--abort` absent from the trace |
| `tdd` | "Red before green." Failing test precedes implementation | **Largely.** This is exactly what `tool_order` is for |
| `research` | Captures findings as a Markdown file in the repo | **Partly.** `file_exists`, then a judge on content |
| `grilling` | A question format, one at a time, no action before confirmation | **Partly.** See below |
| `domain-modeling` | ADR and CONTEXT formats in reference files | **Partly.** Format yes, judgement no |
| `codebase-design` | None. Supplies vocabulary to other skills | **No.** Nothing to assert |

`unslop` is the strongest case in the catalogue and it is worth saying why: its output is prose, the thing the brief suggested was hard to grade. It grades perfectly, because every rule names an observable. Meanwhile `codebase-design` is ungradeable despite operating on code. Artefact versus conversation predicts both wrongly. Contract versus disposition predicts both correctly.

## Grilling, specifically

`grilling` is 275 words and states six things. Taking them one at a time:

| # | Instruction | Assertable from one transcript? | How |
|---|---|---|---|
| 1 | Format as `❓ **Q1** - **title**: body` then `➡️ recommendation` | **Yes** | `regex`. Literal marker check |
| 2 | "Ask questions one at a time" | **Yes** | Count of `❓` in the turn is exactly 1 |
| 3 | Every question carries a recommended answer | **Yes** | Count of `❓` equals count of `➡️` |
| 4 | "Finding facts is your job, never the user's" | **Yes** | Given a prompt whose fact is in the repo, the trace must show a read or a sub-agent, not a question |
| 5 | "Do not act on it until the user confirms" | **Yes** | No `Write` or `Edit` in the first turn |
| 6 | "The session is done when the frontier is empty" | **No** | Needs a multi-turn conversation |

Five of six are gradeable, and all five need **zero LLM judging**. Four are regex or transcript counts against a single turn. That makes `grilling` cheaper to eval than most artefact-producing skills, which is the opposite of the intuition in the question.

Instruction 5 is the valuable one. "Does it ask questions rather than jumping to writing code" is the failure mode that actually matters for a Socratic skill, and it is a negative tool assertion, the cheapest and most reliable check available.

### What is not evaluable, and what to do instead

Two things fall outside the tooling entirely.

**Whether the questions are any good.** Whether the grilling was penetrating, whether it accepted the first answer, whether it found the assumption the user was hiding. No grader reaches this.

**Whether it terminates.** The frontier emptying is a property of a whole conversation.

Both fail for the same structural reason: **every harness available is single-prompt.** `plugin eval` takes one `prompt.md`. `skill-creator` takes one `prompt` per eval. Neither simulates a user who answers a question so the skill can ask the next one. There is no multi-turn eval mechanism in Claude Code, and no documented plan for one.

So do not try. Instead:

1. Assert the five contract items in CI, at tier 1 and tier 2 cost.
2. Keep a **fixed scenario script** in the repository: one plan of known weakness, with three seeded assumptions a good grilling should surface. When someone changes `grilling`, they run that scenario by hand and record which of the three it found.
3. Treat the scenario transcript as the artefact a reviewer reads. That is tier 3, and for this skill it is the tier that carries the quality signal.

A hand-run scenario sounds weak next to a number. It is the honest answer, it is what the Agent Skills guidance points at for qualities that resist pass and fail, and it costs one person ten minutes.

## Ablation, and what a delta is worth

[findings.md](findings.md) says `--ablation with-without` "is the only mechanism that answers whether a skill helps at all". Half right, and the half that is wrong matters.

**Right:** it is the only mechanism in `plugin eval` that compares against a no-skill baseline. `skill-creator`'s Benchmark mode does the same thing outside the CLI, writing `with_skill` and `without_skill` arms into `benchmark.json`.

**Wrong in three ways:**

It is not the only mechanism. `skill-creator` Benchmark mode does it too, is publicly documented, and is not gated.

It is not opt-in. The help text says the default is `with-without` whenever a plugin resolves. Anyone who has run this has already been paying for two arms.

**It answers the wrong question for a pull request.** Ablation compares plugin against no plugin. A PR changes v1 into v2. Both arms of an ablation run contain v2, so a regression from v1 is invisible unless it is bad enough to drop below the no-skill floor. For the owner's stated need, version against version, the only instrument is `skill-creator`'s Improve mode, which records `parent`, `expectation_pass_rate` and `grading_result` of `won`, `lost` or `tie` per iteration in `history.json`, and picks `current_best`.

Two further limits on the delta itself. First, the delta is only as good as the discriminating power of the assertions:

> Remove or replace assertions that always pass in both configurations. These don't tell you anything useful ... They inflate the with-skill pass rate without reflecting actual skill value.

Second, a positive delta on quality can hide a negative delta on cost. `benchmark.json` reports `time_seconds` and `tokens` alongside `pass_rate` precisely because a skill that buys two points of pass rate for double the tokens is a loss. The rule in `recommendation.md` section 3, "no delta, no engine", should read "no delta net of token cost, no engine".

### The arithmetic nobody publishes

Nobody publishes guidance on significance, or on telling a regression from noise. On sample size there is exactly one line, and it only sets a floor. Both statements below are from agentskills.io, on two different pages.

From [optimizing descriptions](https://agentskills.io/skill-creation/optimizing-descriptions):

> Model behavior is nondeterministic — the same query might trigger the skill on one run but not the next. Run each query multiple times (3 is a reasonable starting point) and compute a **trigger rate**: the fraction of runs where the skill was invoked.

From [evaluating skills](https://agentskills.io/skill-creation/evaluating-skills):

> Standard deviation (`stddev`) is only meaningful with multiple runs per eval ... the statistical measures become useful as you expand the test set and run each eval multiple times.

"A reasonable starting point" is the whole of it. Nothing states what delta is significant at three runs, and nothing states when to stop.

Their own schema treats variance as a prompt for a human, not as a test. A sample note in `benchmark.json` reads "Eval 3 shows high variance (50% ± 40%) - may be flaky or model-dependent". That is the state of the art.

So the following is **our arithmetic**, not published guidance. It follows from the two verified defaults, three runs and a threshold of 1.0.

At three runs a case score can only take four values: 0, 0.33, 0.67, 1. The smallest change the instrument can see is 0.33. A threshold of 1.0 therefore means one flaky run out of three fails the build.

| True per-run pass rate | P(case passes at threshold 1.0) | P(a 4-case suite is green) |
|---|---|---|
| 0.99 | 0.97 | 0.89 |
| 0.95 | 0.86 | 0.54 |
| 0.90 | 0.73 | 0.28 |
| 0.80 | 0.51 | 0.07 |

A skill that genuinely does the right thing nine times in ten fails a default-configured gate more often than not across four cases. Anyone who makes this a required check without changing the defaults will teach the team to re-run CI until it goes green, which destroys the signal permanently.

The delta is worse. Treating a run as a Bernoulli trial, the standard error on a proportion at n=3 is at best 0.29, and on a difference of two arms about 0.41. A 95 per cent interval on the delta is roughly plus or minus 0.8. Pooling four cases into twelve runs per arm brings that to about plus or minus 0.4.

| Runs per arm | Approximate 95% interval on the delta |
|---|---|
| 3 | ±0.80 |
| 12 (4 cases × 3) | ±0.40 |
| 48 (4 cases × 12) | ±0.20 |

Two conclusions. Only a delta near total is meaningful at the defaults, which is fine for the admission gate, since a new engine that does not obviously beat nothing should not ship. And no realistic run count resolves the small improvements a typical PR makes, so **do not gate a pull request on a score delta.** Gate on firing accuracy and contract assertions, which are near-deterministic, and use the delta as an admission gate only.

## Two systems, compared

| | `claude plugin eval` | `skill-creator` |
|---|---|---|
| Available here | **No.** Early access, exit 1 | Yes. Public source |
| Documented | No public page at all | `skills.md` plus two agentskills.io pages |
| Case format | `case.yaml`, or `prompt.md` + `graders/*.md` | `evals/evals.json` |
| Graders | `regex`, `tool_used`, `tool_order`, `file_exists`, `llm`, `baseline` | Expectation list graded by an agent, plus scripts |
| Baseline arm | `--ablation with-without` | Benchmark mode, `with_skill` / `without_skill` |
| **Version against version** | **No** | **Yes.** Improve mode, `history.json` |
| Blind A/B | No | Yes. `agents/comparator.md` judges without knowing which is which |
| Description tuning | No | Yes. `improve_description.py` plus `run_loop.py` |
| Trigger accuracy | Indirectly, via `tool_used: Skill` as a with-only indicator | Directly, `run_eval.py` with a trigger rate |
| Human review step | HTML report | HTML viewer, feedback recorded and fed to the next iteration |
| CI shape | Exit codes and `--json`. Built for it | Scripts. Usable, not designed for it |

What `skill-creator` gives that `plugin eval` does not, in one line each: version-versus-version with a blind judge, an automated description optimiser with a train and test split, and cost accounting in tokens and seconds alongside quality.

What `plugin eval` gives that `skill-creator` does not: a single command with an exit code, cheap deterministic graders, MCP mocking, and a cost ceiling.

They are complements. The gate belongs to `plugin eval` when we can run it. The improvement loop belongs to `skill-creator` today.

## How many cases

[findings.md](findings.md) says "Anthropic's published guidance asks for 3 to 5 cases per skill, covering should-fire, should-not-fire and one ambiguous case". No primary source says that. The real numbers are different for the two halves, and the trigger half is much larger than we assumed.

| Purpose | Guidance | Source |
|---|---|---|
| Outcome quality | "Start with 2-3 test cases." Expand later | agentskills.io, evaluating-skills |
| Outcome quality | "come up with 2-3 realistic test prompts" | `skill-creator/SKILL.md` |
| Firing accuracy | "Aim for about 20 queries: 8-10 that should trigger and 8-10 that shouldn't" | agentskills.io, optimizing-descriptions |
| Firing accuracy | 3 runs per query, trigger threshold 0.5 | `run_eval.py` defaults |
| Description iterations | "Five iterations is usually enough" | agentskills.io |

So a full trigger pass is 20 queries at 3 runs, meaning 60 single-turn invocations per engine, not 3 to 5 cases. The budget line in `recommendation.md` section 3 understates the firing work by roughly an order of magnitude and overstates the outcome work.

On negative cases, the guidance is emphatic that easy ones are worthless. The most valuable are **near-misses**, queries sharing keywords with the skill that need something else. "Write a fibonacci function" as a negative for a PDF skill "is too easy" and "doesn't test anything".

One more correction. `findings.md` reports that Anthropic "warns that skill authors should not review their own work". The nearest primary text is about **context contamination**, not peer review: a fresh session is required because authoring context masks gaps, and `skill-creator` notes that running cases yourself is "less rigorous than independent subagents (you wrote the skill and you're also running it, so you have full context)". Keeping "authors do not review their own skills" as a house rule is defensible. Attributing it to Anthropic's eval guidance is not.

## The failure no per-skill eval sees

Every mechanism above tests one skill. Our catalogue's most likely failure is a property of the **set**.

The skill listing gets 1 per cent of the context window by default, and Claude Code truncates the least-used descriptions when it overflows. Two engines with adjacent descriptions fire interchangeably, which `findings.md` already records as a documented failure mode. Both are collective.

`run_eval.py` makes this worse in a specific way: it tests a description in a scratch project against a stub, with no other skills present. A description can score 20 out of 20 in isolation and still lose every contested query to a sibling engine in production.

So extend the trigger suite beyond what Anthropic ships. Run it with **the whole catalogue installed**, and assert two things rather than one:

- The expected engine fired.
- **No other engine fired.**

That is the only check that protects the engine cap, and it is the check that turns adding an engine from a local decision into a measurable one. It costs nothing extra: the same 60 invocations, one more assertion on each.

## CI

| Tier | Auth needed | Runtime per engine | Blocking |
|---|---|---|---|
| 0. Static | None | Seconds | Yes, every PR |
| 1. Firing | Yes | About 3 minutes at 10 workers, 60 invocations | Yes, when a description changes |
| 2. Outcome | Yes | Tens of minutes. 2 arms × 3 runs × 3 cases | No. Merge and nightly |
| 3. Human | n/a | Ten minutes of a person | Yes, by review |

Tier 0 is the only one that runs on a bare runner with no secret. Everything else needs a credential for `claude -p`, which is a real prerequisite and not a detail: it means an org decision about a CI identity and a spend limit before any model-calling check can be required.

Three practical notes. Set `--no-publish` if `plugin eval` is ever enabled, because the HTML report goes to claude.ai by default. Set `--max-cost-usd` on any scheduled run. And never gate on the default `--threshold 1.0` at `--runs 3`, for the reasons in the arithmetic above.

## Proposal for recommendation.md section 3

Replace the four-layer table with this. The change in substance: tier 0 grows, firing separates cleanly from outcome, nothing blocking depends on a gated CLI, and version comparison gets an owner.

| Layer | Tool | Cost | Runs | Blocking |
|---|---|---|---|---|
| 1. Manifests | `claude plugin validate . --strict` | Free | Every PR | Yes |
| 2. Referential integrity and budget | xUnit | Free | Every PR | Yes |
| 3. Firing accuracy, catalogue-wide | Our harness, modelled on `run_eval.py` | ~60 calls per engine | PRs touching a description | Yes |
| 4. Contract assertions | Same harness, regex and trace checks | ~3 calls per case | PRs touching a body | Yes |
| 5. Admission delta | `skill-creator` Benchmark, or `plugin eval --ablation` if enabled | High | New engines only | Yes, once |
| 6. Version comparison | `skill-creator` Improve mode, blind A/B | High | On request, by the author | No |
| 7. Human review | A second person, plus fixed scenario scripts | Minutes | Every PR | Yes |

Order of adoption:

1. **Build layers 1 and 2 now.** Free, no auth, no early access, and they catch the breakage our prose-composition design invites. Extract every quoted skill name on a line mentioning the Skill tool, not one regex. Assert description length against 1024 and the shipped total against the listing budget.
2. **Write the fixed scenario script for `grilling` now.** One plan with three seeded weaknesses. It costs an hour and it is the only quality signal that skill will ever have.
3. **Build layer 3 with the first engine, not before.** Copy `run_eval.py`'s stub-command technique. Add the "no other engine fired" assertion from day one, because retrofitting it later means rewriting every case.
4. **Add layer 4 in the same pull request as layer 3.** The contract assertions for `grilling`, `unslop` and `tdd` need no LLM judge, so they cost three calls each and are near-deterministic.
5. **Layer 5 only at admission.** One ablation run when an engine is proposed. Require the delta to be large, and require it net of token cost. Do not re-run it per PR.
6. **Layer 6 stays a tool, not a gate.** An author reaching for blind A/B when they are unsure is right. Making it mandatory buys noise at high cost.
7. **Revisit `plugin eval` when the entitlement lands.** It replaces our harness for layers 3 to 5 and is better built for CI. Until then it cannot be a required check, so do not design around it.

Amend two existing rules while you are there. "No delta, no engine" becomes "no delta net of token cost, no engine". And the budget line, "twelve engines at four cases is about 288 runs per full pass", is wrong in both directions: firing is roughly 720 single-turn invocations, outcome is roughly 216 full agent runs.

## What we could not verify

- **The `case.yaml` schema.** Never seen. `init --bare` is gated, so no scaffold was produced and no key list exists. Everything above about case layout comes from one sentence of `--help`.
- **What each grader type can assert.** The six names appear only in `findings.md`. The help text names none of them except `tool_used: Skill` in passing. There is no public page, so their config keys, weighting and scoring are unknown.
- **How a case score is computed across runs.** Mean of run scores, or pass rate? The threshold arithmetic above holds either way at n=3, but the exact aggregation is undocumented.
- **How early access is granted.** Not documented anywhere: not in `env-vars`, not in the changelog, not in `plugins-reference`. A web search surfaced an environment variable name that appears in no primary source, so it is not repeated here. There is no documented way to check or request enablement.
- **Whether the entitlement was ever present on this account.** `findings.md` records the command as working. It does not now. Whether that is a revocation, a rollout change, or an error in the earlier record, we cannot tell.
- **Whether `skill-creator`'s scripts run unmodified on Windows.** `run_eval.py` writes into `.claude/commands/` and shells out to `claude`, and the docs show `open` for the HTML report. Not executed here, because that spends model calls.
- **Cost per trigger invocation in tokens.** The mechanism kills the run at the first tool call, so it should be small, but no number was measured.
- **Whether `tool_order` can express "a failing test ran before the implementation was written".** It is the obvious fit for `tdd`, and with the CLI gated we could not test it.
