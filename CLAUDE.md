# skills-marketplace

A Claude Code plugin marketplace for sharing agent skills across teams. Today the repo holds research, a plan and a prototype harness. The shipped catalogue, `plugins/core/skills/`, has two skills in it: `skill-authoring` and `new-skill`. Filling it is the work.

## Two skill folders, and they are not the same thing

| Path | What it is | Ships to developers |
|---|---|---|
| `.claude/skills/` | Borrowed dev tooling, used while working in this repo | No |
| `plugins/*/skills/` | The catalogue | Yes |

`.claude/skills/` is a vendored copy of someone else's toolkit, flattened and edited. An edit there changes how we work, not what we ship. A new shipped skill goes in `plugins/core/skills/`.

## Commands

```
claude plugin validate .                        # marketplace manifest
claude plugin validate ./plugins/core           # plugin manifest
dotnet test harness/tests/Harness.Free.Tests    # 27 tests, no network, ~1s
```

Run all three before you commit.

One suite spends money on real `claude -p` calls, and it is double-locked:

```
SKILL_HARNESS_LIVE=1 dotnet test harness/tests/Harness.Model.Tests
```

Run it only when the user asks for it, and report the cost the run prints.

## `harness/` is a prototype

Throwaway, built to answer one design question. `marketplace.json` does not reference it and nothing under `harness/fixtures/` is a real skill. Read `harness/README.md` before changing anything in there.

## Writing a catalogue skill

[docs/recommendation.md](docs/recommendation.md) is the plan. These four rules are measured, not stylistic:

- **Engines** are model-invocable and each one spends listing context. Cap at about 12. **Entry points** carry `disable-model-invocation: true`, spend nothing, and the developer types them.
- Compose skills in prose: `Call the Skill tool with "name"`. Write it that way every time, never `/name`.
- Split the catalogue by technology: `acme-eventing-kafka`, `acme-data-sql`. Keep engines technology-neutral and put the technology in the entry point.
- Write a description by *when* it applies, and end it with a negative boundary naming the nearest technologies it excludes. Measured: a broad request pulled a mean of 6.0 skills out of 12 without one, and 1.2 with one.

## Research

A finding is only real once it is a committed Markdown file in `docs/`, indexed from `README.md`. Say the date, say the Claude Code version, and say whether the number was measured on this machine or read from a source.

## Conventions

- British English. `catalogue`, `behaviour`, `licence`.
- Commit messages are imperative and sentence case, with no prefix: "Correct layer 3's cost", not "fix: layer 3 cost".
- Solo project. Commit straight to `main`.
- Prose a human reads gets the `unslop` pass before it lands.
