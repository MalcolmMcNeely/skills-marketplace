# Targeting: one skill, both skills, or none

Should the catalogue split by technology? When a task spans two technologies, does one skill fire, or both? And can an eval test that?

Written 2 September 2026 against Claude Code 2.1.248. The numbers below were measured on this machine, not reasoned about.

## The short version

Both skills fire, reliably, and the split is right. A task naming EF Core and React fired exactly the C# skill and the React skill, and no decoy fired in any of 44 runs across four description variants. But it only fired at all in about two runs of three. The rest fired nothing.

The thing that decides what fires is **how the developer phrases the task**, not how we design the catalogue. That is the finding, and it is not the one we expected.

| Prompt | Skills fired, out of 12 installed |
|---|---|
| Names both technologies | Exactly the right 2 in 6 runs of 7. Never a decoy |
| Vague, and says "do the whole thing end to end" | 11, 5, 5, 5, 4 |
| Vague, no breadth cue | 0, 1, 0 |

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

Runs used `claude -p` with `--output-format stream-json`, with `Write`, `Edit`, `Bash` and `NotebookEdit` disallowed so no run could do real work. Between three and seven runs per prompt; each table states its own n.

**One correction to our own method.** An earlier pass reported runs where nothing fired. Those were the harness timing out, not the model declining. Every number in this document comes from a run that exited 0 and emitted `"subtype":"success"`. Runs killed by the timeout were discarded, not counted as zero. Any trigger suite we build must check the exit code before it scores a run, or it will read its own timeouts as negative results.

## Question 1: should the split be by technology?

Yes, and the evidence is the precision result. With twelve skills installed, an explicit cross-stack prompt fired exactly two and left ten alone in 10 runs of 15. No decoy fired in any of the 44 explicit runs across all four variants. A run that failed fired nothing at all rather than firing the wrong thing. Vue never fired on a React task. Technology-named skills with "use when" descriptions discriminate well.

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
| 12 skills | Names EF Core and React | Both and only both, 10 of 15. No decoys in 44 runs |

Co-invocation is not a workaround. It is the documented idiom, and this repository already depends on it: `.claude/skills/grill-with-docs/SKILL.md` is one line instructing the model to call the Skill tool twice. Auto-compaction also carries multiple invoked skills forward, sharing a 25,000 token budget, which only makes sense if several load at once.

### The vague-prompt result

This is where it gets interesting, and where the risk actually lives.

| Prompt | Runs | Skills fired |
|---|---|---|
| "Let users set a display name on their profile. Do the whole thing end to end." | 5 | 11, 5, 5, 5, 4 |
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

Do not gate on the vague-prompt case yet. Across five runs the spread was 11, 5, 5, 5, 4, which is too wide to threshold. Record it and watch it.

## The description experiment

Does description wording change what fires? Yes, in one direction only, and the first pass at this got the answer wrong.

Four variants of the same twelve skills. Only the `description` line changed; names, bodies and markers were identical. Two factors, crossed.

| Variant | Wording | Negative boundary | Example, `acme-web-react` |
|---|---|---|---|
| A | Intent | No | Use when adding or changing a React component, hook or form in the web front end. |
| B | Artefact | Yes | Use when editing a .tsx or .jsx file that defines a React component or hook. Not for Vue files, backend code or end to end tests. |
| C | Intent | Yes | Use when adding or changing a React component, hook or form in the web front end. Not for Vue files, backend code or end to end tests. |
| D | Artefact | No | Use when editing a .tsx or .jsx file that defines a React component or hook. |

"Intent" describes the moment of use. "Artefact" describes the file being edited. A negative boundary is a trailing "Not for X" sentence.

### Results

A run on the explicit prompt is correct when exactly `acme-data-csharp` and `acme-web-react` fired and no decoy did.

| Variant | Explicit prompt, correct | Vague with breadth cue, mean skills fired |
|---|---|---|
| A. Intent, no boundary | 10 of 15 | **6.0** |
| B. Artefact, boundary | 4 of 7 | 1.4 |
| C. Intent, boundary | 12 of 15 | 1.2 |
| D. Artefact, no boundary | 6 of 7 | 0.8 |

Per-run vague counts: A fired 11, 5, 5, 5, 4. B fired 2, 2, 2, 0, 1. C fired 0, 3, 2, 1, 0. D fired 1, 1, 0, 0, 2.

### What this shows

**Either change alone suppresses over-firing.** A is the only variant that over-fires, and it is the only one with neither change. Adding a negative boundary does it. Switching to artefact wording does it. Doing both does not add much. The effect is large and consistent, so it is safe to act on.

**Neither change costs recall, and the earlier claim that they did was wrong.** That claim rested on comparing A at 6 of 7 against B at 4 of 7. At fifteen runs A is 10 of 15, so the 6 of 7 was a small-sample fluke and the gap it implied does not exist. C, the variant this document previously advised against, scored highest of the two arms with fifteen runs.

**No difference in recall between variants is significant here.** 10 of 15 against 12 of 15 is well inside noise. The claim is only that C is not worse than A, not that it is better.

**Decoys never fired.** Zero decoy invocations across all 44 explicit runs, in every variant. Whatever else is uncertain, the catalogue does not misfire on a well-specified request. That is the strongest single result in this document.

**The real problem is not which variant wins.** Every variant missed the explicit prompt between 14% and 33% of the time. A well-specified, unambiguous, cross-stack request fails to load the right skills roughly a fifth to a third of the time, whatever the description says. That matters more than the choice between wordings, and no description edit tested here fixes it.

### What to adopt

Variant C. Keep intent wording, and add a trailing negative boundary naming the nearest technologies the skill is not for.

It keeps the rule [recommendation.md](recommendation.md) already sets, "scope the description by when, not what", so nothing else in the plan has to change. It captures nearly all of the precision gain, cutting mean over-firing from 6.0 skills to 1.2. And it costs nothing measurable in recall.

D scored well too and is worth a longer look, but adopting it would mean reversing the "when, not what" rule on seven runs of evidence. C gets the same benefit without that argument.

**Design the trigger suite around the miss rate, not the wording.** A skill that loads two times in three on the request it was written for is the finding that should shape layer 3. Set the should-fire threshold from measured behaviour rather than from an expectation of 100%, and run enough repeats to see it.
## What this changed in the plan

All six are applied in [recommendation.md](recommendation.md) as of this commit.

1. The "one is wrong" rule in section 2 is replaced by the overlap version above.
2. Per-repo enablement is named as the answer to cross-stack noise, in place of description tuning.
3. Section 2 now requires a trailing negative boundary on every description, naming the nearest technologies the skill is not for.
4. Section 3 gains a layer 3b: assert the set of skills a query fires, with cross-stack cases required for any two technologies that meet in a real repository.
5. Section 3 sets the should-fire threshold from measured behaviour rather than from an expectation of 100%, because a well-specified request loaded the right skills only two times in three.
6. The harness must check a run's exit code before scoring it.

## What we could not verify

- **Whether this holds at catalogue scale.** Twelve skills is the engine cap, so it is the right number to test, but a developer with our catalogue plus their own project skills will exceed it. The listing budget is 1% of the context window and drops descriptions starting with the least-invoked skills, so behaviour past the cap is a different experiment.
- **Why any variant misses.** Every wording tested failed the explicit prompt between 14% and 33% of the time, firing nothing rather than the wrong thing. Nothing here explains the misses or shows a description that removes them.
- **Significance on recall.** Fifteen runs for A and C, seven for B and D. No recall difference between variants is significant at those numbers. Only the over-firing effect, a mean of 6.0 against about 1.0, is large enough to lean on.
- **Whether artefact wording is genuinely better.** Variant D scored highest on both measures but only over seven runs, and adopting it would reverse the plan's "when, not what" rule. It deserves a longer run before anyone acts on it.
- **Whether the ordering of skills in the listing matters.** Not varied.
- **How this interacts with `relevance` suggestions.** Nothing was installed through a marketplace here; all twelve were project skills.
