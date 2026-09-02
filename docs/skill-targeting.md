# Targeting: one skill, both skills, or none

Should the catalogue split by technology? When a task spans two technologies, does one skill fire, or both? And can an eval test that?

Written 2 September 2026 against Claude Code 2.1.248. The numbers below were measured on this machine, not reasoned about.

## The short version

Both skills fire, reliably, and the split is right. A task naming EF Core and React fired exactly the C# skill and the React skill, three times out of three, with ten decoy skills installed and none of them firing.

The thing that decides what fires is **how the developer phrases the task**, not how we design the catalogue. That is the finding, and it is not the one we expected.

| Prompt | Skills fired, out of 12 installed |
|---|---|
| Names both technologies | Exactly the right 2, every run |
| Vague, and says "do the whole thing end to end" | 5, 5 and 11 |
| Vague, no breadth cue | 0, 1 and 0 |

So there are two failure modes, not one, and they pull in opposite directions. Under-firing on an ordinary vague request is the common one. Over-firing happens when the prompt signals breadth.

One rule in [recommendation.md](recommendation.md) is wrong and has to go.

## The rule that is wrong

Section 2 currently ends:

> If two skills could plausibly fire on the same request, one is wrong.

That is false. A full-stack change legitimately needs the C# skill and the React skill together, and the mechanism handles it correctly. The rule as written would force us to merge skills that should stay apart, or to narrow descriptions until cross-stack work loses its guidance.

The defensible version is about **overlap**, not co-firing:

> Two skills may fire on one request when the request genuinely spans both. What must not happen is two skills firing because their descriptions describe the same work. Test for the second, not the first.

## How this was measured

Twelve stub skills in a scratch directory, outside this repository so its own vendored skills could not interfere. Each body does nothing but print a unique marker, so the transcript says exactly which loaded.

The two under test:

| Skill | Description |
|---|---|
| `acme-data-csharp` | Use when writing or changing EF Core entities, migrations or queries against SQL Server in a C# project. |
| `acme-web-react` | Use when adding or changing a React component, hook or form in the web front end. |

The ten decoys covered Kafka, raw SQL, Vue, gRPC, Terraform, Playwright, OIDC, OpenTelemetry, SwiftUI and Python batch. Vue is deliberately a near miss for React.

Runs used `claude -p` with `--output-format stream-json`, with `Write`, `Edit`, `Bash` and `NotebookEdit` disallowed so no run could do real work. Three runs per prompt.

**One correction to our own method.** An earlier pass reported runs where nothing fired. Those were the harness timing out, not the model declining. Every number in this document comes from a run that exited 0 and emitted `"subtype":"success"`. Runs killed by the timeout were discarded, not counted as zero. Any trigger suite we build must check the exit code before it scores a run, or it will read its own timeouts as negative results.

## Question 1: should the split be by technology?

Yes, and the evidence is the precision result. With twelve skills installed, an explicit cross-stack prompt fired exactly two and left ten alone, three times out of three. Vue never fired on a React task. Technology-named skills with "use when" descriptions discriminate well.

Making skills language-agnostic does not fix the vague-prompt problem. It converts it into a permanent one. A skill that matches any task fires on every task and carries irrelevant content each time. Splitting is what buys the precision seen above.

The plan's existing shape survives contact with the evidence and should stand:

> Engines stay technology-neutral. Entry points carry the technology.

One addition. The layer that does the real work here is not description matching, it is **per-repo enablement**. In a C#-only repository the React skill is not installed, so it cannot misfire however the request is phrased. The vague-prompt failure only bites in a genuinely full-stack repository, where both skills are correctly enabled. That narrows the problem considerably and is an argument for keeping `enabledPlugins` tight per repo rather than shipping the whole catalogue everywhere.

## Question 2: does one fire, or both?

Both. Reliably, when the request names the work.

| Catalogue | Prompt | Result |
|---|---|---|
| 2 skills | Names EF Core and React | Both, 3 of 3 |
| 2 skills | React only | React only, 2 of 2 |
| 2 skills | EF Core only | C# only, 2 of 2 |
| 12 skills | Names EF Core and React | Both and only both, 3 of 3 |

Co-invocation is not a workaround. It is the documented idiom, and this repository already depends on it: `.claude/skills/grill-with-docs/SKILL.md` is one line instructing the model to call the Skill tool twice. Auto-compaction also carries multiple invoked skills forward, sharing a 25,000 token budget, which only makes sense if several load at once.

### The vague-prompt result

This is where it gets interesting, and where the risk actually lives.

| Prompt | Runs | Skills fired |
|---|---|---|
| "Let users set a display name on their profile. Do the whole thing end to end." | 3 | 11, 5, 5 |
| "Let users set a display name on their profile." | 3 | 0, 1, 0 |

Same feature, same catalogue, one sentence of difference. The first phrasing pulled in gRPC, OIDC, Playwright, Kafka, SwiftUI and Vue on a task that needed none of them. The second fired almost nothing.

Two consequences.

**Under-firing is the common failure.** A developer who types an ordinary short request gets no guidance at all. That is worse for us than over-firing, because it is silent. Nobody notices a skill that did not fire.

**"Do everything" is a breadth cue.** The model reads it as licence to load broadly. Descriptions that lean on generic verbs will be swept up by it.

Neither is fixed by catalogue design. Both are properties of the prompt.

## Question 3: can an eval test this?

Yes, and it needs one change to the design already in [evals.md](evals.md).

That document proposes a tier 1 firing suite that asserts a skill fires on should-fire queries and stays quiet on should-not-fire queries. That is a per-skill assertion, and it cannot see either failure above. A cross-stack case has two correct answers at once, and a vague case has a correct answer that is a set.

**Assert the set, not the skill.** Every trigger case names the exact set of skills that should fire, and the whole catalogue is installed when it runs. Three assertion shapes:

| Case type | Assertion |
|---|---|
| Single technology | Exactly one named skill fired |
| Cross-stack | Exactly the named set fired, no more |
| Near miss | The set is empty |

`skill-creator`'s technique still applies. It writes a stub carrying only the description and kills the run at the first tool call, so a firing case is cheap. The change is that the harness records **every** Skill invocation in the run rather than stopping at the first, and compares the set.

Add cross-stack cases from the start. Retrofitting them means re-running every case, because a case scored against one skill carries no record of what else fired.

Three cases per pair of technologies that plausibly meet: one naming both, one naming neither, one naming only the first. The middle one is the valuable case and the one nobody writes.

Do not gate on the vague-prompt case yet. At three runs the spread above, 11 then 5 then 5, is too wide to threshold. Record it and watch it.

## What this changed in the plan

All four are applied in [recommendation.md](recommendation.md) as of this commit.

1. The "one is wrong" rule in section 2 is replaced by the overlap version above.
2. Per-repo enablement is named as the answer to cross-stack noise, in place of description tuning.
3. Section 3 gains a layer 3b: assert the set of skills a query fires, with cross-stack cases required for any two technologies that meet in a real repository.
4. The harness must check a run's exit code before scoring it.

## What we could not verify

- **Whether this holds at catalogue scale.** Twelve skills is the engine cap, so it is the right number to test, but a developer with our catalogue plus their own project skills will exceed it. The listing budget is 1% of the context window and drops descriptions starting with the least-invoked skills, so behaviour past the cap is a different experiment.
- **Whether description wording changes the vague-prompt result.** Only one wording per skill was tested. Whether tighter descriptions reduce the over-firing spread is untested and is the obvious next experiment.
- **Significance.** Three runs per prompt. The explicit results were unanimous, so they are safe to lean on. The vague results, 11 then 5 then 5, are directional only.
- **Whether the ordering of skills in the listing matters.** Not varied.
- **How this interacts with `relevance` suggestions.** Nothing was installed through a marketplace here; all twelve were project skills.
