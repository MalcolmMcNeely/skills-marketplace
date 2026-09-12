# Steering how Claude writes code

Researched 2026-09-11 against Claude Code 2.1.248 (build `2026-08-27T19:29:54Z`, `GIT_SHA 8c9482ad`). Three labels run throughout. **Read** means taken from a cited source. **Local** means read off this machine today, including bytes pulled out of the shipped `claude.exe`. **Measured** keeps the meaning the harness gives it, a number produced by a run. Nothing here is measured, and that is the largest finding in the document.

## The question

Four complaints, all the same shape. Claude writes comments nobody wants. It invents names instead of using the project's own. It ignores an idiom the team prefers, C# extension methods being the example to hand. It puts every new file in one folder instead of the feature folder.

The obvious answer is to write a rule. Put it in `CLAUDE.md`, or in a skill, and the model will comply.

It does not. The reason is worth the rest of this document, and it is not the one I expected.

## The instruction is already there, and it already loses

Anthropic does not publish the Claude Code system prompt. The public `anthropics/claude-code` repository holds a changelog, plugins, examples and scripts, and no application source (**local**, `GET /repos/anthropics/claude-code/contents/`). But the prompt ships inside the binary, and on this machine that is a single 226 MB Bun-compiled executable at

```
C:\Users\malco\AppData\Local\Microsoft\WinGet\Packages\Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe\claude.exe
```

At byte 186391270 sits the builder for the prompt's `# Doing tasks` section. It contains, verbatim (**local**):

> Default to writing no comments. Only add one when the WHY is non-obvious: a hidden constraint, a subtle invariant, a workaround for a specific bug, behavior that would surprise a reader. If removing the comment wouldn't confuse a future reader, don't write it.

and immediately after:

> Don't explain WHAT the code does, since well-named identifiers already do that. Don't reference the current task, fix, or callers ("used by X", "added for the Y flow", "handles the case from issue #123"), since those belong in the PR description and rot as the codebase evolves.

That is the strongest position any instruction can occupy: the system prompt, loaded first, before anything the project or the user contributes. It is phrased about as forcefully as the problem allows. And Claude still over-comments.

This reframes the whole exercise. A `CLAUDE.md` rule saying "write fewer comments" is not a missing instruction being supplied. It is a weaker restatement of an instruction that is already present at the top of the stack and already losing. Adding a second copy lower down cannot be the fix, and the reports below show it is not.

Two caveats on that quote. It was read out of a compiled binary, not published by Anthropic, so treat it as true of 2.1.248 on this machine and nothing more. And the prompt changes between releases, so the finding is dated the moment 2.1.249 ships.

## The stack, and what each layer can actually do

Five places an instruction can live, and they are not interchangeable. The documented ordering (**read**, [output styles](https://code.claude.com/docs/en/output-styles), [modifying system prompts](https://code.claude.com/docs/en/agent-sdk/modifying-system-prompts)):

| Layer | What it does to the prompt | Documented wording |
|---|---|---|
| Custom output style | **Replaces** default instructions | "Replace or extend default" |
| `--append-system-prompt` | Appends to the system prompt | "Appends to the system prompt without removing anything" |
| `CLAUDE.md` | A user message *after* the system prompt | "Adds a user message after the system prompt" |
| Skill body | Loads on demand, as a user message at invocation | "Loads task-specific instructions when invoked or relevant" |
| Subagent | Its own separate system prompt | "Runs a subagent with its own system prompt, model, and tools" |

The memory docs are blunt about where `CLAUDE.md` sits (**read**, [memory](https://code.claude.com/docs/en/memory)):

> CLAUDE.md content is delivered as a user message after the system prompt, not as part of the system prompt itself. Claude reads it and tries to follow it, but there's no guarantee of strict compliance, especially for vague or conflicting instructions.

and on the same page:

> Claude treats them as context, not enforced configuration. To block an action regardless of what Claude decides, use a PreToolUse hook instead.

The weighting difference is stated outright (**read**, [modifying system prompts](https://code.claude.com/docs/en/agent-sdk/modifying-system-prompts)):

> Instructions in the user message carry marginally less weight than the same text in the system prompt, so Claude may rely on them less strongly.

So the hierarchy by strength is: system prompt, then appended system prompt, then `CLAUDE.md` and skill bodies, then nothing. Hooks sit outside it entirely, because they are not instructions the model weighs.

## The output style trap

Short of `--system-prompt`, an output style is the only thing that removes default text rather than adding to it. That makes it look like the root-cause fix. It is the opposite.

The docs say what gets removed (**read**, [output styles](https://code.claude.com/docs/en/output-styles)):

> Custom output styles leave out Claude Code's built-in software engineering instructions, such as how to scope changes, write comments, and verify work, unless `keep-coding-instructions` is set to `true`.

The shipped code confirms it is exactly the `# Doing tasks` section quoted above. The prompt assembly reads (**local**, byte ~186409920):

```js
[nHn(M),oHn(t),M===null||M.keepCodingInstructions===!0?sHn():null,iHn(),aHn(N),uHn()]
```

`M` is the active style and `sHn()` builds `# Doing tasks`. The section is emitted only when the style is Default (`M === null`) or the style sets `keepCodingInstructions === true`. Otherwise the slot is `null` and the "Default to writing no comments" text is gone from the session.

**Writing a custom output style to reduce comments, without `keep-coding-instructions: true`, deletes the only anti-comment instruction in the product and replaces it with a weaker one of your own.**

That is the mechanism, and it holds. The conclusion drawn from it below does not: 60 runs later, the arm that drops the section was the *least* commented and the most consistent, because the deleted instruction was not doing the job its wording implies. See [where a comment rule sits, measured](comment-density-measured.md).

Three further facts from the binary, none of them in the public docs:

- Default is not a style object. It is literally `null` in the built-in table (**local**, byte 184592499).
- All four shipped non-default styles (Proactive, Concise, Explanatory, Learning) set `keepCodingInstructions: !0`. No built-in can delete the section. Only a hand-written custom one can.
- Concise's prompt body is six rules about response prose. It never mentions code or comments. Selecting Concise will not reduce comment density, because it was not built to.

A style also swaps the opening role sentence (**local**, byte 186388953). With any non-default style active, "You are an interactive agent that helps users with software engineering tasks" becomes "You are an interactive agent that helps users according to your Output Style below". That swap happens even with `keep-coding-instructions: true`.

### A hazard on this machine

`C:\Users\malco\.claude\settings.json` sets `"outputStyle": "ELI5"` (**local**). `ELI5.md` sets `keep-coding-instructions: true`, so the no-comments section survives, but the role sentence is swapped in every interactive session in this repo.

The harness is already immune. `harness/src/Harness/ClaudeCli.cs` pins `{"outputStyle":"default"}` through `--settings`, with a comment saying why: "This machine has a user-level output style. Pin it or the run measures the machine, not the skill." Every paid run to date was made under Default. The existing numbers are clean.

### What this closes in `docs/output-styles.md`

That document already flags `keep-coding-instructions` as "the trap", but never connects it to comment verbosity. Three corrections it needs:

- "A Markdown file that Claude Code adds to its system prompt" is wrong for a custom style. It adds one section, swaps the role sentence, and may delete another section.
- "It needs a restart" is true on 2.1.248, but from v2.1.251 a mid-session *switch* applies from the next message. Editing a style *file* mid-session still needs a restart (**read**, [prompt caching](https://code.claude.com/docs/en/prompt-caching)).
- Its open question "does `force-for-plugin` beat a managed `outputStyle`?" is answered: yes. The forced-plugin style short-circuits and returns before the merged settings value is read at all (**local**, byte ~184599012).

## Others hit the same wall, and none of them found a way through

The pattern of an instruction holding briefly then decaying is reported precisely, not just grumbled about.

[anthropics/claude-code#61305](https://github.com/anthropics/claude-code/issues/61305) (**read**): a zero-comment rule in `CLAUDE.md`, a correction in the chat, Claude acknowledging the rule, then "Next Code-Writing Turn: Comments regressed despite the correction." The reporter's own summary:

> A correction holds for roughly one turn, then regresses.

[anthropics/claude-code#65961](https://github.com/anthropics/claude-code/issues/65961) (**read**), filed against 2.1.228, open, labelled `area:model`, no staff response:

> A clear, mandatory rule in `CLAUDE.md` does not reliably suppress it. Reinforcing the rule via the memory system does not stop it either... verbose commenting is the out-of-the-box default, and that default is strong enough to override explicit user instructions... Users shouldn't have to stack a CLAUDE.md rule + memory entries + enforcement hooks just to get clean code, and that still working partially.

Note what that reporter had already tried: rule, memory, and hooks. The classification matters too. It is filed as a model bug, not a configuration one, and the absence of any Anthropic reply is itself the state of play.

The bias is not Claude-specific. GitHub Copilot users report "Even with an instructions file that says not to include comments, Copilot sometimes ignores the instructions" ([community discussion](https://github.com/orgs/community/discussions/59697), **read**), and a Cursor thread carries the same complaint ([forum](https://forum.cursor.com/t/how-to-tell-the-model-not-write-unnecessary-comments/105136), **read**). Whatever produces it is common to coding models, not to one product.

**No published wording reliably suppresses it.** Two candidate rules appear on that Cursor thread and neither poster reported back that theirs worked. Nobody in any source found has published a phrasing with a confirmed, sustained success report.

### Why long rule files make it worse

The docs put a number on it (**read**, [memory](https://code.claude.com/docs/en/memory)):

> target under 200 lines per CLAUDE.md file. Longer files consume more context and reduce adherence.

and the best-practices page is less polite: "Bloated CLAUDE.md files cause Claude to ignore your actual instructions!" Two more from the same pages, both directly relevant to anyone stacking rules: "If Claude keeps skipping one instruction, add emphasis such as 'IMPORTANT' to that line alone. If you emphasize many lines, none of them stands out." And "If two rules contradict each other, Claude may pick one arbitrarily."

Compaction adds a second decay path. Root `CLAUDE.md` is re-read from disk and re-injected after `/compact`, but "detailed instructions from early in the conversation may be lost" ([how Claude Code works](https://code.claude.com/docs/en/how-claude-code-works), **read**). Anything said only in chat does not survive.

The nearest thing to a measurement is general, not ours. The IFScale benchmark ([arXiv:2507.11538](https://arxiv.org/abs/2507.11538), **read**) finds Claude Sonnet shows linear decay in instruction compliance as instruction count rises, with the best models reaching about 68 per cent accuracy at maximum density. It tested report generation, not code, and not Claude Code, so it explains the shape of the problem without measuring this instance of it.

## Positive phrasing, because Anthropic says so three times

Every rule written here should be a description of the wanted end state, not a prohibition. The claim is stated identically on three first-party pages (**read**):

> Positive examples showing how Claude can communicate with the appropriate level of concision tend to be more effective than negative examples or instructions that tell the model what not to do.

([prompting Claude Sonnet 5](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/prompting-claude-sonnet-5); repeated on the [Opus 5 page](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/prompting-claude-opus-5) and in [general best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices), whose worked example is: instead of "Do not use markdown in your response", try "Your response should be composed of smoothly flowing prose paragraphs".)

No page explains *why*. It is an empirical claim repeated, not a mechanism.

Anthropic's own suppression instruction, offered under "Overeagerness" for Opus 4.5/4.6, is worth copying verbatim because it is the vendor's own attempt at the problem (**read**, same best-practices page):

> Documentation: Don't add docstrings, comments, or type annotations to code you didn't change. Only add comments where the logic isn't self-evident.

Note what it scopes by. Not a global ban, but the code the change already touches. That shape is more useful than "no comments", because a reviewer can check it.

## The only pre-write gate that is not a request

A `PreToolUse` hook is the one mechanism that acts before the text lands and is not something the model may decline. The docs draw the line themselves (**read**, [best practices](https://code.claude.com/docs/en/best-practices)):

> Unlike CLAUDE.md instructions which are advisory, hooks are deterministic and guarantee the action happens.

The mechanics, confirmed against the raw doc (**read**, [hooks](https://code.claude.com/docs/en/hooks)):

- `PreToolUse` fires before the tool executes, so the hook sees what is about to be written, not what was written.
- `Write` carries `file_path` and `content`. `Edit` carries `file_path`, `old_string`, `new_string` and `replace_all`. The full prospective content is in the hook's stdin.
- Blocking contract: "Exit 2 means a blocking error. On events that can block, exit 2 blocks whether or not you print JSON." And "If your hook is meant to enforce a policy, use `exit 2`."
- The reason reaches the model, and the turn continues: "Claude sees the stderr message as the denial reason." So the model can correct and retry rather than the run dying.

A gate that rejects a `Write` whose comment-to-code ratio exceeds a threshold, with a stderr message naming the offending lines, is therefore buildable today. **Nobody appears to have built one.** Searches turned up no published example of a hook rejecting a write for being over-commented, and no report of what happens on retry: self-correct, loop, or give up. Hooks-as-enforcement is well documented for formatters and architecture rules and entirely absent for comment density.

One caveat before building it. Practitioners report that hooks firing on every edit inject a system reminder each time, which is itself context noise; the suggested mitigation is to gate at commit rather than per edit. That report is second-hand and weak, but the trade-off is real: a pre-write gate costs a retry, and a retry costs tokens.

Böckeler's framing is the best published version of the underlying argument ([sensors for coding agents](https://martinfowler.com/articles/sensors-for-coding-agents.html), **read**): guides are prose and soft, sensors are deterministic and hard, and sensors work best when "they produce signals optimized for LLM consumption". Her concrete result, on file placement rather than comments, is that the agent "violated the rules a handful of times after I introduced them, and then self-corrected based on dependency-cruiser feedback". She also warns against the duplication we would otherwise create: putting a rule in `CLAUDE.md` that a sensor already enforces is "unnecessary duplication".

Not everyone agrees the prose layer is hopeless. On the Hacker News thread about `CLAUDE.md`, one commenter answered the decay complaints with "I haven't experienced this a single time and I use it almost all day everyday" ([thread](https://news.ycombinator.com/item?id=46102048), **read**). The disagreement is unresolved, and file size may be the hidden variable.

## Naming, where a glossary does work

Comments are the hard case because the instruction already exists and already fails. Naming is the easy case, because the model has no idea what your words are until you tell it.

Evans' original claim is that the domain language is meant to reach the identifiers, not just the conversation: "The vocabulary of that UBIQUITOUS LANGUAGE includes the names of classes and prominent operations." (Evans, *Domain-Driven Design Reference*. The primary PDF at domainlanguage.com returned HTTP 403 to an automated fetch, so this is quoted from a [secondary source reproducing the book text](https://gist.github.com/danilobatistaqueiroz/f441e6a33e43b8bc47cf00d8eefd254b), **read**. Fowler's [bliki entry](https://martinfowler.com/bliki/UbiquitousLanguage.html) defines the term but stays at the level of conversation and does not make the code-naming claim.)

Anthropic's own advice points the same way without naming DDD. The common-workflows page tells you to "Request a glossary of project-specific terms" and to "Use domain language from the project" ([common workflows](https://code.claude.com/docs/en/common-workflows), **read**), though framed for searching a codebase rather than for naming new things in it.

### `CONTEXT.md` is not a standard

Worth stating plainly, because it is easy to assume otherwise. Anthropic's memory docs never mention `CONTEXT.md`. They document `CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md` by import or symlink, and auto-memory. `CONTEXT.md` is Matt Pocock's convention, from the skills library this repo vendored into `.claude/skills/` (**local** and **read**; the same skill text appears verbatim across several third-party skill directories attributing it to `mattpocock/skills`).

### The loop is half-built here already

`.claude/skills/domain-modeling/` builds and maintains `CONTEXT.md`, and prescribes its shape: a `## Language` section of `**Term**:` entries, each with a one or two sentence definition and an `_Avoid_:` list of banned synonyms. Its rules include "Be opinionated. When multiple words exist for the same concept, pick the best one and list the others under `_Avoid_`" and "CONTEXT.md should be totally devoid of implementation details... It is a glossary and nothing else." (**local**.)

But that skill says nothing about the vocabulary reaching the code. Its only code-facing rule runs the other way, checking code against the glossary. The consuming rule is copy-pasted, with variation, into other skills:

- `tdd/SKILL.md`: "read CONTEXT.md (if it exists) so test names and interface vocabulary match the project's domain language"
- `improve-codebase-architecture/SKILL.md`: "If CONTEXT.md defines 'Order,' talk about 'the Order intake module' — not 'the FooBarHandler,' and not 'the Order service.'"
- `setup-matt-pocock-skills/domain.md`: "When your output names a domain concept... use the term as defined in CONTEXT.md. Don't drift to synonyms the glossary explicitly avoids."

There is no single central rule saying "class and method names come from here". For a catalogue skill, that rule is one line and belongs in the engine.

### Show the names, do not describe them

The strongest technique found is not a rule at all. Umbraco's `src/Umbraco.Infrastructure/CLAUDE.md` reverse-engineers its own naming patterns and quotes them back with file and line references, for example "**Repository Naming** (from UserRepository.cs:30): `internal sealed class UserRepository : EntityRepositoryBase<Guid, IUser>, IUserRepository`", with matching sections for factories and services ([source](https://github.com/umbraco/Umbraco-CMS/blob/main/src/Umbraco.Infrastructure/CLAUDE.md), **read**). That is the positive-example advice from the prompting guide, applied to identifiers.

The one practitioner claim that states the mechanism baldly: "Give an agent clear, consistent terminology and it generates code with matching names and coherent structure. Give it a codebase where the same concept has four names, and it will invent a fifth." ([AI Pattern Book](https://aipatternbook.com/ubiquitous-language), **read**, a third-party site, uncorroborated).

**Nobody has measured this.** No published study shows that adding a glossary file changed specific identifiers an agent emitted. The claim rests on Evans' pre-LLM argument extended by analogy, plus practitioner anecdote.

## File placement: remove the choice

The default failure is the model inventing a folder shape. The published fix is not to describe the shape but to point at one and have it copied, or to scaffold the files so the model never chooses.

SSW's Vertical Slice Architecture template does both. Its `CLAUDE.md` names a canonical folder: "Reference slice: `src/WebApi/Features/Heroes/CreateHero/`. Copy its shape rather than reinventing." Its architecture rules add "Namespace mirrors the folder." Its `add-slice` skill scaffolds the five files, and instructs "Read the live reference first... If they disagree, the repo wins — follow it and fix the template." ([CLAUDE.md](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/blob/main/CLAUDE.md), [add-slice](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/blob/main/.claude/skills/add-slice/SKILL.md), **read**.)

The same repo closes the loop with architecture tests the agent must pass: every endpoint named `*Endpoint` in the right namespace, every request's validator in the same slice namespace, "no slice depends on another slice's types" ([testing rules](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/blob/main/.claude/rules/testing.md), **read**). The rule that guards the guard is the good bit: "Every test guards its match set with `Should().NotBeEmpty()` first. Without that, a filter that stops matching... turns the test green instead of red, and it silently stops enforcing anything."

A second .NET author lands on the same shape independently: one folder per feature under `Features/`, shared concerns in `Common/`, and nested `Services/` or `Repositories/` inside a feature marked explicitly as an anti-pattern ([dotnet-claude-kit vertical-slice skill](https://github.com/codewithmukesh/dotnet-claude-kit/blob/main/skills/vertical-slice/SKILL.md), **read**).

And the argument in one line, from someone who replaced a component generator with a skill: "Templates are more powerful than rules, since the skill knows to rely on them and it is more deterministic." ([dev.to](https://dev.to/mbarzeev/replacing-a-plop-react-component-generator-with-a-claude-code-skill-5do), **read**, single practitioner, qualitative.)

No source measures template-copying against prose instruction. Every claim here is a before-and-after impression.

## Idiom: name the construct, do not describe the taste

For something like "prefer extension methods", the working examples in the wild are specific to the point of quoting syntax. GitVersion's `CLAUDE.md` says "Extension members — use the new `extension(Type t) { }` block syntax for extension methods/properties", under a heading "Prefer new syntax where it improves clarity" ([source](https://github.com/GitTools/GitVersion/blob/main/CLAUDE.md), **read**). Csla's states existing idiom as fact rather than as a directive: "C# style: Allman braces, `var` everywhere, expression-bodied members preferred", "Field naming: `_fieldName` for instance fields" ([source](https://github.com/MarimerLLC/csla/blob/main/CLAUDE.md), **read**).

Nothing enforces a *preference* for an extension method over a static helper. No shipped analyser in the .NET ecosystem does it, and the preference has no crisp trigger a tool could fire on. That leaves prose, like it or not, so the Umbraco technique of showing a real call site is the best move available.

**A gap worth noting.** Of five real, established .NET repos with a `CLAUDE.md` (SSW, GitVersion, csla, RestSharp, Umbraco), four contain no comment rule at all. Production teams write agent rules about architecture, naming and testing far more than about comments. The comment problem is handled out of band, by skills and hooks, or not at all.

## The cure, and why it cannot be a linter

The fallback is a second pass that strips the comments after they are written. This repo already has one in `comment-sweep`.

No tool can replace it. Nothing in the .NET analyser ecosystem detects a comment that restates the code. StyleCop's documentation rules check that a comment exists and is well formed. Roslynator's nearest rule catches a literally repeated word. A Roslyn analyser sees syntax and semantics, not meaning, so "this comment says what the next line says" is outside what it can decide. The cure has to be a language model reading the diff.

The cost comparison nobody has published is the one that matters: is a cleanup pass cheaper or dearer than getting it right first time? A cleanup pass pays for generating the comments, then pays again to read and remove them. A pre-write gate pays for a rejected write and a retry. Neither number exists in public.

One practitioner runs both layers and is explicit that the first is insufficient: a `CLAUDE.md` rule for prevention plus a comment-cleanup skill, because prevention "doesn't eliminate" the problem and "the model still narrates the occasional tricky block, especially in long sessions" ([gist](https://gist.github.com/bavanws/123e0343f8a79cec825d9141124a0a83), **read**).

## The only numbers anyone has published

Three, and all three are weak. Recorded here so nobody goes looking again.

| Number | Source | Worth |
|---|---|---|
| Comment share 5-7% with no skill, 30-38% with v0.4.0, 7-13% with v0.5.0 | [chl03ks/shut-up-and-code](https://github.com/chl03ks/shut-up-and-code) | 4-6 runs per arm, benchmarked by the tool's own author, who says "the exact percentages aren't precise to the point". The only source found with a stated method. |
| "It follows maybe 60-70% on a good day" for 200+ lines of `CLAUDE.md` rules | [issue #32163](https://github.com/anthropics/claude-code/issues/32163) | A subjective impression. Not counted. |
| About 68% instruction compliance at maximum density; linear decay for Sonnet | [IFScale, arXiv:2507.11538](https://arxiv.org/abs/2507.11538) | Real benchmark, wrong target. Report generation, not code, not Claude Code. |

There is no published, methodologically transparent, Claude-Code-specific before-and-after on comments, naming, idiom or file placement. Treat any tighter number than the above as unsupported.

## What to do

In order of leverage, prevention first.

1. **Write the rule, and know that `keep-coding-instructions` is the lever the number moved on.** ~~Do not write a custom output style to suppress comments.~~ Measured after this document was written: a `CLAUDE.md` rule and the same rule in an output style both scored 10 of 15, and the style with `keep-coding-instructions: false` scored 15 of 15. The deletion helped rather than hurt. What it costs elsewhere is unmeasured. [The numbers](comment-density-measured.md).
2. **Write rules as positive descriptions, scoped to what the change touches.** Anthropic's own phrasing is "code you didn't change", not a global ban.
3. **Show the pattern, do not describe it.** A canonical file the agent copies (SSW), or a real call site quoted with file and line (Umbraco), beats an abstract rule. This works for naming, file placement and idiom alike.
4. **Scaffold what you can.** A skill that creates the five files in the feature folder removes the file-placement decision entirely.
5. **Put one line in the engine skill** pointing at `CONTEXT.md` for identifiers. The glossary exists here; the consuming rule is scattered across borrowed skills and absent from the catalogue.
6. **Keep the rule file short.** Under 200 lines per file, per the docs, and emphasise one line rather than ten.
7. **Keep the cleanup pass.** It is the only thing that catches what prevention misses, and no tool can replace it.
8. **Consider a `PreToolUse` comment-density gate.** It is the one prevention move nobody has tried. That makes it interesting and unproven in equal measure.

## The experiment, run

Specified here, then run the same day: 60 runs, four arms, $18.38, 58 minutes. The results are in [where a comment rule sits, measured](comment-density-measured.md), the pass is `CommentDensityPassTests`, and the journal sits beside the suite it measured.

Two things came back that this document had wrong.

**The rule is not ignored.** A `CLAUDE.md` comment rule took the density from 25.7 to 11.4 and worked on 10 runs of 15. The reports above say a rule in `CLAUDE.md` does nothing. On this task it did most of the available work.

**Arm D was the best arm, not the worst.** The prediction here was that dropping `# Doing tasks` deletes "Default to writing no comments" and must therefore make things worse. It scored 15 of 15 with the tightest spread of any arm. The mechanical claim was right and the inference from it was wrong, because the deleted instruction was not governing the behaviour its wording describes.

A third result belongs to neither half. The whole effect, in all 60 runs, was whether the model wrote XML doc comments. Whole-line `//` narration never moved: about one line a run in every arm, rule or no rule. The over-commenting this document is about was not what the model was doing.

## What was not established

- Whether any wording reliably suppresses comments. Nobody has published one, and the instruction with the best position in the stack already fails.
- Whether a `PreToolUse` density gate works, or loops.
- Whether a glossary file changes the identifiers an agent emits. Unmeasured by anyone.
- Whether a cleanup pass costs more or less than prevention. Unmeasured by anyone.
- Why negative instructions underperform. Anthropic states the effect on three pages and explains it on none.
