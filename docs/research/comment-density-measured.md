# Where a comment rule sits, measured

60 runs on 2026-09-12, `claude-opus-5[1m]` against Claude Code 2.1.248, $18.38 and 58 minutes. Every figure here was **measured** on this machine. The pass is `CommentDensityPassTests`, the journal and report sit in `harness/skills/csharp-new-class/runs/`, and the run is reproducible with the command at the end.

This tests the prediction made in [steering how Claude writes code](steering-code-style.md). One of the two claims it makes survived. The other was backwards.

## The design

One coding task, taken from the suite's own contract case so the experiment widens when a suite folder lands beside it:

> Add a Discount class that applies a percentage discount to an order total.

One rule, in the phrasing Anthropic's prompting guide recommends, positive and scoped rather than banned outright:

> Comments record why, never what. Add one only where the reason is invisible in the code itself: a hidden constraint, a subtle invariant, or a workaround for a specific defect. Leave the comments in code you did not change exactly as they are.

Four arms, 15 runs each. The rule is identical wherever it appears, so the variable is **where an instruction sits**, not how it is worded. No skill is loaded, because a skill body would be a fourth position and would confound the three under test.

| Arm | Where the rule sits | Output style |
|---|---|---|
| A | Nowhere. The control | Default |
| B | `CLAUDE.md`, a user message after the system prompt | Default |
| C | A custom output style, `keep-coding-instructions: true` | Custom |
| D | The same style, `keep-coding-instructions: false` | Custom |

C and D differ by one frontmatter line. That is the whole difference between them.

## What happened

Density is comment lines per 100 code lines. Blank lines and brace-only lines count as neither.

| Arm | n | Mean | Median | Min | Max | Doc lines/run | Narration lines/run | Code lines/run |
|---|---|---|---|---|---|---|---|---|
| A. no rule | 15 | 25.7 | 26.2 | 10.0 | 40.0 | 9.2 | 1.2 | 40.8 |
| B. CLAUDE.md | 15 | 11.4 | 5.1 | 0.0 | 35.9 | 3.5 | 1.2 | 38.9 |
| C. style, defaults kept | 15 | 10.1 | 5.3 | 3.8 | 26.2 | 2.3 | 2.2 | 43.9 |
| D. style, defaults dropped | 15 | 3.5 | 4.5 | 0.0 | 5.3 | 0.0 | 1.5 | 42.8 |

All 60 runs were valid. Every run wrote exactly two files. No run produced a single trailing comment.

## The effect is binary, and it is entirely about XML doc comments

The means hide the real shape. Sort each arm's runs and they fall into two clusters with a clean gap between about 9 and about 14, and the cluster a run lands in is decided by one thing:

| Arm | Runs writing no XML doc comments | Runs with density under 10 |
|---|---|---|
| A | 0 of 15 | 0 of 15 |
| B | 10 of 15 | 10 of 15 |
| C | 10 of 15 | 10 of 15 |
| D | 15 of 15 | 15 of 15 |

Those two columns are identical for every arm, in all 60 runs. A run either writes `///` blocks on its public members or it does not, and nothing else moves the number.

**The rule never reduced narration.** Whole-line `//` comments averaged 1.2 lines a run under the control and 1.2, 2.2 and 1.5 under the three arms carrying the rule. The arm with the most narration was C, which also carried the rule. On a task this size the model barely narrates at all, with or without instruction.

So the behaviour the rule actually controls is XML documentation, not the explain-the-obvious commenting the complaint is usually about. Anyone writing a rule to stop `//` narration on a task like this is aiming at something that was not happening.

## Position did not matter. Removing the default did

This is the finding that changes the recommendation.

B and C both scored **10 of 15**. Same rule, same words. In B it sits in a user message after the system prompt; in C it sits inside the system prompt, in the position the documentation calls "replace or extend default". Moving the identical sentence from the weaker position to the stronger one changed nothing measurable (Fisher's exact, p = 1.0).

D scored **15 of 15**. D is C with one frontmatter line flipped, which drops Claude Code's built-in `# Doing tasks` section from the system prompt.

| Comparison | Result | Fisher's exact, one-tailed |
|---|---|---|
| A vs B, rule versus no rule | 0 of 15 → 10 of 15 | p = 0.0001 |
| B vs C, CLAUDE.md versus output style | 10 of 15 → 10 of 15 | p = 1.0 |
| C vs D, the same style with the defaults dropped | 10 of 15 → 15 of 15 | p = 0.021 |

Two conclusions, and the second is the useful one.

**Writing the rule at all is what buys the large step.** Going from no rule to a rule took a third of the density off. The reports that a `CLAUDE.md` comment rule is ignored are not what this measured: it worked two times in three.

**The remaining third was not bought by position. It was bought by deletion.** What stopped the last five runs writing doc blocks was not an instruction sitting higher up, it was the absence of the instructions that were already there.

That is worth stating plainly, because it is the opposite of the mechanism the earlier document reasoned toward. The default system prompt contains "Default to writing no comments", and under that instruction the control arm wrote 9.2 doc lines a run, every run, without exception. Remove the section holding it and the doc comments stop entirely. Whatever produces them is in the default instruction set, and the "no comments" line does not govern it. The most likely reading is that the model does not treat an XML doc block as a comment.

## The prediction that was wrong

[steering-code-style.md](steering-code-style.md) predicted arm D would be the **worst** arm, on the reasoning that dropping `# Doing tasks` deletes "Default to writing no comments" and leaves a weaker rule in its place. It was the best arm, by a clear margin, and the most consistent: its whole range was 0.0 to 5.3, against 0.0 to 35.9 for the `CLAUDE.md` arm.

The mechanical claim behind the prediction was correct. The section really is dropped, and that really is visible in the shipped binary. The inference from it was wrong, because the section was not doing the job its wording implies.

## What D costs, and what this did not measure

D is not a free win, and the number here should not be read as a recommendation on its own.

Dropping `keep-coding-instructions` removes the whole `# Doing tasks` section, which also carries the instructions on scoping a change, on not adding features beyond the task, and on verifying work. **This experiment measured comment density and nothing else.** Whether D's code is worse in ways the metric cannot see is untested, and a 60-run pass on one small task is the wrong instrument for finding out.

What can be said is narrow. Every arm wrote two files. Mean code lines ranged from 38.9 to 43.9, so D did not achieve its score by writing less. Reading the files by hand, a documented and an undocumented `Discount` carry the same argument validation and the same behaviour:

```csharp
// A-style, XML docs stripped from this excerpt for comparison
public Discount(decimal percentage)
{
    ArgumentOutOfRangeException.ThrowIfLessThan(percentage, 0m);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(percentage, 100m);
    Percentage = percentage;
}

// D-style, as written
public Discount(decimal percentage)
{
    ArgumentOutOfRangeException.ThrowIfNegative(percentage);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(percentage, 100m);
    Percentage = percentage;
}
```

That is one pair out of sixty and proves nothing on its own. It is recorded because the alternative, asserting D's code is fine without looking, would be worse.

## What to do with this

1. **Write the rule.** It is worth two thirds of the available effect and it costs nothing. The claim that a `CLAUDE.md` comment rule is simply ignored did not survive contact with 15 runs.
2. **Do not move the rule to an output style expecting more compliance.** The same words in the stronger position scored identically. If an output style is used, that is not the reason to use it.
3. **Aim the rule at doc comments if doc comments are the problem.** A rule saying "comments record why, never what" left `///` blocks on two thirds of runs, because the model does not appear to count them. Name them.
4. **Treat `keep-coding-instructions: false` as a real option with an unpriced cost.** It is the only arm that reached full compliance, and it is also the arm that deletes the product's instructions on scope and verification. Nobody has measured that trade.

## What this does not establish

- One task, one suite, one file shape. A task with genuine complexity might narrate where this one did not.
- 15 runs an arm resolves the large steps and nothing subtle. The B-versus-C null is a null at this sample size, not a proof of no difference.
- Every figure is dated. The system prompt changes between releases, so 2.1.249 may move all four arms.
- The metric is a line classifier, not a parser. It counts whole-line comments and records trailing ones separately without ratioing them. There were none to worry about here: zero trailing comments in 60 runs.

## Running it again

```
SKILL_HARNESS_LIVE=1 SKILL_HARNESS_DENSITY=1 dotnet test harness/tests/model
```

Double-locked like the other long passes. `SKILL_HARNESS_DENSITY_RUNS` sets runs per arm (default 15) and `SKILL_HARNESS_CEILING_USD` bounds the spend (default $36). The measured pass came in at $18.38.
