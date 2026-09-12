---
name: skill-authoring
description: Use when writing or editing a skill for the shared catalogue, or reviewing one someone else wrote. Covers what goes in a description, when a skill should be an engine rather than an entry point, and how skills call each other. Not for writing AGENTS.md or CLAUDE.md, not for prose a human reads, and not for skills that live in a project's own .claude/skills/ folder.
---

# Writing a catalogue skill

Four rules. Each one was measured, not argued.

## 1. Decide engine or entry point first

| | Engine | Entry point |
|---|---|---|
| Frontmatter | nothing extra | `disable-model-invocation: true` |
| Who starts it | the model, from the description | a developer, by typing it |
| Listing cost | every request, forever | none |
| How many | about 12 across the catalogue | as many as you like |

An engine competes for the skill listing, which gets 1% of the context window. When that overflows, Claude Code drops descriptions starting with the least-invoked skills. So an engine you add makes every other engine slightly less likely to fire.

An entry point costs nothing, because its description never enters the listing.

Ask one question: **does the model need to reach for this on its own?** If a developer will type it, make it an entry point.

## 2. Write the description by when it applies, and end with a boundary

A description is not a summary of what the skill does. It is the only thing the model reads when deciding whether to fire. So write the trigger, not the contents.

Start with "Use when", then name the situation.

Then end with a **negative boundary**: name the nearest technologies or situations this skill excludes. Not vague ones. The ones a reader might confuse it with.

```
description: Use when adding a new C# class to this project. Not for changing
  existing classes, test files, or non-C# code.
```

**Measured.** A broad request against a twelve-skill catalogue pulled a mean of **6.0** skills with no boundary, and **1.2** with one. Recall did not measurably suffer. Nothing else you can write in a description comes close to that.

Keep it under 1,024 characters. That is the spec's hard limit and skills over it are rejected.

## 3. Put the technology in the entry point, not the engine

Split the catalogue by technology at the entry point, and keep the engine technology-neutral:

```
acme-eventing-kafka     entry point, names the technology
acme-data-sql           entry point, names the technology
```

The engine behind them holds the reusable procedure. This is what keeps the engine count near 12 while the catalogue grows past it.

## 4. Compose in prose, never with a slash

To make one skill use another, write it as an instruction in the body:

```
Call the Skill tool with "codebase-design".      correct
/codebase-design                                 wrong
```

Write it that way every time. A slash form is what a **developer types**, so using one as a delegation instruction confuses a human action with a model one. The catalogue's integrity tests read the quoted name, and they fail on the slash form.

Two names in one instruction is fine:

```
Call the Skill tool twice, for "grilling" and "domain-modeling".
```

## Before you commit

The catalogue's own tests check this for you, with no model calls:

```
dotnet test harness/tests/free
```

They will fail if a name you referenced does not exist, if you wrote `/name` for an engine, if the catalogue has more than 12 engines, or if a description is over 1,024 characters.
