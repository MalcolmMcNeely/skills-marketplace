---
name: new-skill
description: Scaffold a new skill for the shared catalogue and check it against the house rules before it lands.
argument-hint: "What should the skill do, and who starts it?"
disable-model-invocation: true
---

Write a new skill into `plugins/core/skills/<name>/SKILL.md`.

Call the Skill tool with "skill-authoring" for the rules that decide the shape of it.

## Steps

1. **Ask what starts it.** A developer typing it, or the model reaching for it on its own. That answer decides engine or entry point, and everything else follows from it.

2. **Count the engines first.** If this is an engine, run `ls plugins/core/skills/` and check how many are already model-invocable. The cap is 12. If adding this one breaks the cap, say so and ask whether an existing engine should become an entry point instead.

3. **Write the frontmatter.** `name` matches the folder. `description` says when it applies and ends with a negative boundary. An entry point also gets `disable-model-invocation: true` and an `argument-hint`.

4. **Write the body.** Short, imperative, one idea per step. Compose other skills in prose, like this:

```
Call the Skill tool with "the-skill-you-need".
```

5. **Run the checks.**

```
claude plugin validate ./plugins/core
dotnet test harness/tests/free
```

Both must pass before it lands.

## Do not

- Reuse a name from `harness/shared/distractors/skills/`. Those are the distractor set and the integrity tests fail if a shipped skill shadows one.
- Write `/name` to make one skill call another. That is a developer action, not a model one.
