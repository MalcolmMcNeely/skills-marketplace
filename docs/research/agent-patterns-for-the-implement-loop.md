# Agent patterns for the implement loop

Researched 2026-09-08 against Claude Code 2.1.248. Anything labelled **measured** was run on this machine (Windows 11 Pro 26200, Git Bash) on that date. Anything else was read from the source cited beside it.

## The question

The loop today is `/grill-with-docs`, `/to-spec`, `/to-tickets`, then a manual `/implement` in a fresh session per ticket. Human attention concentrates in the grilling stage and thins after it. By the time tickets exist the work is mechanical and the human is only a launcher.

Two shapes could automate the launcher. A **central orchestrator** holds the plan, dispatches a worker per ticket and synthesises the results. A **daisy chain** ends each implement run by starting the next, with no coordinator above it.

The answer is the chain, and the reason is more interesting than the preference.

## A code-driven chain is not a multi-agent system

Anthropic draws its root distinction not between one agent and many, but between workflows and agents ([Building effective agents](https://www.anthropic.com/engineering/building-effective-agents)):

> **Workflows** are systems where LLMs and tools are orchestrated through predefined code paths.
>
> **Agents** are systems where LLMs dynamically direct their own processes and tool usage, maintaining control over how they accomplish tasks.

A script that reads the next unblocked ticket from a dependency graph and launches a fresh context with a templated prompt is a workflow. It sits on the predefined-code-paths side of that line, and the published guidance points there first: "find the simplest solution possible, and only increasing complexity when needed"; "add multi-step agentic systems only when simpler solutions fall short"; "consider adding complexity *only* when it demonstrably improves outcomes" (same source).

The rule is restated in [When to use multi-agent systems (and when not to)](https://claude.com/blog/building-multi-agent-systems-when-and-how-to-use-them), 23 January 2026: "Start with the simplest approach that works, and add complexity only when evidence supports it." That post also warns that "teams invest months building elaborate multi-agent architectures only to discover that improved prompting on a single agent achieved equivalent results."

## The orchestrator is defined by a condition this loop does not meet

Orchestrator-workers exists to solve one specific problem, and the source is explicit about which ([Building effective agents](https://www.anthropic.com/engineering/building-effective-agents)):

> the key difference from parallelization is its flexibility — subtasks aren't pre-defined, but determined by the orchestrator based on the specific input

Use it "for complex tasks where you can't predict the subtasks needed".

`/to-tickets` predicts them. It produces vertical slices with declared blocking edges, quizzes the human on granularity, and publishes them in dependency order. The decomposition an orchestrator exists to perform has already happened, one stage earlier, with human review attached. An orchestrator's token premium buys a capability the pipeline does not need.

Prompt chaining's stated condition is the mirror image: it is "ideal for situations where the task can be easily and cleanly decomposed into fixed subtasks". A reviewed ticket DAG is exactly that decomposition, written down.

## The published anti-condition names this case

From [How we built our multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system):

> some domains that require all agents to share the same context or involve many dependencies between agents are not a good fit for multi-agent systems today

and immediately after:

> most coding tasks involve fewer truly parallelizable tasks than research, and LLM agents are not yet great at coordinating and delegating to other agents in real time

Dependency-linked tickets are, by construction, "many dependencies between agents". The source names coding specifically.

The January 2026 post lists decomposition boundaries that work and boundaries that do not. The bad list includes "Sequential phases of the same work. Planning, implementation, and testing of the same feature share too much context." The good list includes "Separate components with clean interfaces" and "Blackbox verification."

That cuts a useful line. Splitting a *single ticket* across a planner, an implementer and a tester is the wrong split. Splitting *across tickets*, where each ticket owns its own implementation and its own tests, is the sanctioned one: "an agent handling a feature should also handle its tests, because it already possesses the necessary context." One fresh context per ticket is already the right boundary. The loop just launches it by hand.

Current model-specific guidance says the same thing in a sentence ([Prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices)):

> For simple tasks, sequential operations, single-file edits, or tasks where you need to maintain context across steps, work directly rather than delegating.

That page also warns that recent models over-delegate: "Claude Opus 5 also delegates to subagents more readily than prior models." An orchestrator built on a model with that bias will spawn more than intended, and suppressing it costs prompt effort.

## What the orchestrator costs

| Comparison | Multiplier | Source |
|---|---|---|
| Agent vs chat interaction | about 4x more tokens | [multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system) |
| Multi-agent vs chat interaction | about 15x more tokens | same |
| Multi-agent vs single agent, equivalent task | 3-10x more tokens | [when to use multi-agent](https://claude.com/blog/building-multi-agent-systems-when-and-how-to-use-them) |

The premium buys parallelism. Dependency-ordered tickets cannot collect it. [Multi-agent coordination patterns](https://claude.com/blog/multi-agent-coordination-patterns), 10 April 2026, states the trap plainly:

> Unless explicitly parallelized, subagents run one after another, meaning the system incurs multi-agent token costs without the speed benefit.

An orchestrator over a linear ticket chain pays the bill and collects none of the goods.

## What is genuinely lost without a coordinator

The honest case against the chain. An orchestrator's real job is not dispatch, it is holding a coherent view of the goal and synthesising results. Drop it and four things go unowned.

1. **Drift from the spec.** Ticket 7 can satisfy its own acceptance criteria while walking away from what the spec asked for. Nothing compares the accumulated result against the plan.
2. **Cross-ticket reconciliation.** Two tickets introduce competing abstractions for one concept. Each passes in isolation.
3. **Replanning.** If ticket 3 proves the spec wrong, a chain has no mechanism to revise tickets 4 onward. It implements them as written.
4. **A final report.** Less damaging here than in research, because the deliverable is code and git already holds it.

The fix is not to reinstate a coordinator. It is to make synthesis a step *in* the chain: a verifier per ticket, and one drift check at the end. Blackbox verification is on the sanctioned-split list, because "the verifier does not need to understand why the artifact was built as it was. It only needs to determine whether the artifact meets the specified criteria." Its stated failure mode is worth heeding: "A verifier told only to check whether output is good, with no further criteria, will rubber-stamp the generator's output." Give it the ticket's acceptance criteria and a test command.

## The fork that decides whether this works: who writes the next prompt

"Inject the right prompt from the right ticket" has two readings, and they are not equivalent.

**(a) A script reads ticket N+1 and templates the prompt.** Control flow is deterministic, inspectable and re-runnable. Nothing the model emits can redirect the pipeline. This is a workflow.

**(b) Run N writes run N+1's prompt in prose.** Control flow is model-directed. An error in run 3's handoff note silently steers runs 4 through 12, and there is no coordinator to absorb it.

Take (a). The completed run's contribution to the next run is not the prompt. It is an append to durable state on disk: a commit, an updated progress file, a ticket closed on the tracker. The prompt is derived from the ticket, every time, by code.

This matters more than it looks, because a fresh subagent inherits nothing implicitly. From the [Agent SDK subagent docs](https://code.claude.com/docs/en/agent-sdk/subagents): "The only content you pass from parent to subagent is the Agent tool's prompt string, so include any file paths, error messages, or decisions the subagent needs directly in that prompt." Whatever ticket N+1 needs is either in its prompt or discoverable on disk. Nowhere else.

The ticket prompt should carry pointers, not payloads. [Effective context engineering for AI agents](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents) calls this just-in-time retrieval: agents "maintain lightweight identifiers (file paths, stored queries, web links, etc.) and use these references to dynamically load data into context at runtime". Ticket id, spec reference, blocker ids, progress file path. Let the run fetch the rest.

## Anthropic built this exact chain and published it

[Effective harnesses for long-running agents](https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents), 26 November 2025, describes a harness that built a claude.ai clone with over 200 features. It is a daisy chain, not an orchestrator.

An **initialiser agent** runs once and creates `init.sh`, a progress file and a first git commit. A **coding agent** runs every session after that. Sessions hand off through git history, a progress file and a structured feature list. Quoted: "each new session begins with no memory of what came before", and "the key insight here was finding a way for agents to quickly understand the state of work when starting with a fresh context window."

Each session orients the same way: run `pwd`, read the git log and progress file, read the feature list, pick the highest-priority item. Then commit with a descriptive message and update the progress file, which "allows the model to use git to revert bad code changes and recover working states."

Their observed failure modes map straight onto a ticket loop. Agents "left the environment in a state with bugs". "Claude marks features as done prematurely." Claude "would fail to recognize that the feature didn't work end-to-end". The stated fix: "Only mark features as 'passing' after careful testing", plus a real end-to-end verification tool.

Fresh-context-per-ticket is stated as guidance, not just habit ([Prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices)):

> When a context window is cleared, consider starting with a brand new context window rather than using compaction. Claude's latest models are extremely effective at discovering state from the local filesystem.

## Four mechanisms, and which ones actually run

Two of the four were tested here.

### 1. Stop hook blocks and injects the next instruction, measured

A `Stop` hook returns `{"decision":"block","reason":"..."}` and the `reason` becomes the model's next instruction. Same session, so context accumulates across tickets.

**Measured.** A headless run given "Reply with exactly: OK", with a one-shot Stop hook returning a block and a fresh instruction, returned `BANANA`. `is_error: false`, `num_turns: 2`, `total_cost_usd` 0.0901215.

Safety cap: after 8 consecutive blocks with no progress, Claude Code overrides the hook and lets the stop through. The hook's stdin carries `stop_hook_active`, which goes true as the cap approaches, and `CLAUDE_CODE_STOP_HOOK_BLOCK_CAP` raises the ceiling to a maximum of 60 ([hooks guide](https://code.claude.com/docs/en/hooks-guide)). Both read from the source, not measured.

The flaw is structural rather than mechanical. Context grows ticket by ticket, which is the opposite of what the loop wants. It also buries control flow inside a hook, where it is hard to inspect and impossible to resume from the middle.

### 2. Stop hook launches a new session, measured

A `Stop` hook shells out to a fresh `claude -p` for the next ticket. Each ticket gets a genuinely empty context.

**Measured, and it contradicts a widely-cited claim.** The published concern is that a nested `claude` refuses to start because it inherits `CLAUDECODE=1` ([claude-agent-sdk-python#573](https://github.com/anthropics/claude-agent-sdk-python/issues/573)). On this machine, on 2.1.248, that guard did not fire.

- A shell spawned by the Bash tool does see `CLAUDECODE=1`, alongside `CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_EXECPATH` and others.
- A nested `claude -p` run from that shell, with `CLAUDECODE=1` inherited and untouched, **succeeded**. `total_cost_usd` 0.216267.
- The same command under `env -u CLAUDECODE` also succeeded, at `total_cost_usd` 0.0118075.
- A `Stop` hook that shells out to `claude -p` **succeeded**. The parent returned `PARENT` at 0.0428615; the child wrote `CHILDRAN` and exited 0.

Two things follow. Hook-launched chaining is available today and needs no workaround, though `env -u CLAUDECODE` is cheap insurance given the guard has existed. And the cost gap between those two nested calls is not about the env var: the first paid 21,521 cache-creation tokens to build the system-prompt cache cold, the second read the same cache warm. Roughly 18x on an identical trivial prompt. **A chain that starts each ticket as a brand-new process pays a cold-cache toll per ticket.** Budget for it.

The caveat that matters: the hook ran synchronously. The parent waited for the child, which is why the hook's `timeout` was raised to 240 seconds for the test. A real chain needs `async: true` on the command hook, or a detached launch. Not tested.

### 3. An external driver loop, the documented shape

A script outside Claude Code walks the DAG and calls `claude -p` once per ticket.

```bash
while next=$(next-unblocked-ticket); do
  [ -z "$next" ] && break
  claude -p --permission-mode acceptEdits "/implement $next"
done
```

Every piece is documented: `-p`, `--output-format json`, `--permission-mode`, `--max-turns`, and session ids returned in the JSON result ([headless mode](https://code.claude.com/docs/en/headless)). User-invoked skills expand inside a `-p` prompt, so `/implement 42` works. Terminal-only built-ins such as `/login` do not.

Control flow lives in code. The DAG is inspectable. A failure stops the loop where a human can see it. This is option (a) from the fork above, and it is the recommendation.

### 4. The Agent SDK, the same shape in a real language

`@anthropic-ai/claude-agent-sdk` or `claude-agent-sdk` driving a loop of `query()` calls, each a fresh session ([TypeScript](https://code.claude.com/docs/en/agent-sdk/typescript), [Python](https://code.claude.com/docs/en/agent-sdk/python)). Same determinism as the shell loop, with typed results, in-process hooks, a `canUseTool` callback and per-run budget caps.

One trap: `settingSources` controls whether the SDK loads `.claude/settings.json` and the repo's skills at all. Set it deliberately, or an SDK run will not see the skills an interactive session takes for granted.

## Recommendation

Build the chain as a workflow.

1. **A deterministic driver holds the DAG.** Code picks the next unblocked ticket. The model never decides what runs next.
2. **One fresh context per ticket.** Not compaction, not a subagent inside a growing parent.
3. **The handoff is state on disk.** Git commits, a progress file, the ticket closed on the tracker. Never run N authoring run N+1's prompt.
4. **Prompts carry pointers.** Ticket id, spec reference, blocker ids, paths. Not pasted content.
5. **Verify per ticket with rules, not judgment.** Tests are the acceptance criteria. Say explicitly that a ticket is not done until they run and pass.
6. **One synthesis pass at the end, not a coordinator throughout.** A final agent reads the spec and the accumulated diff and reports drift. That recovers the orchestrator's real benefit for the cost of one step.
7. **Design for mid-chain failure.** A failure at ticket N invalidates N onward. Small tickets, a commit per ticket, resume from a git ref, a spend cap per ticket.
8. **Parallelise only where the DAG permits.** Independent branches are a legitimate fan-out. Dependent chains stay sequential.

## Failure semantics worth knowing before building

Claude Code's own `Workflow` tool ships a `pipeline()` call that is a daisy chain in code, and its documented resume behaviour is the clearest available statement of what a chain costs when it breaks ([dynamic workflows](https://code.claude.com/docs/en/workflows)). A completed step returns its cached result; a step that failed "runs again, and so does every agent that started after it, even ones that completed". Stated consequence: "If a script starts A, B, C, and D in that order and B fails, relaunching returns A from cache and runs B, C, and D again."

For tickets that is usually the behaviour you want. If ticket 3's implementation changed, tickets 4 onward deserve reconsideration. But a failure at ticket 3 of 12 is expensive, which is the argument for small tickets and a git-ref resume point rather than a position in a script.

Non-determinism is the other standing cost ([multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system)): "Agents make dynamic decisions and are non-deterministic between runs, even with identical prompts. This makes debugging harder." And a specific argument against the orchestrator for anything you intend to tune: "small changes to the lead agent can unpredictably change how subagents behave." In a chain, a prompt change affects one step.

## Third-party corroboration, labelled as such

[Why Do Multi-Agent LLM Systems Fail?](https://arxiv.org/abs/2503.13657) (arXiv 2503.13657, UC Berkeley and collaborators, submitted 17 March 2025, revised 26 October 2025) analysed 150 traces to build a 14-mode failure taxonomy, with inter-annotator agreement of kappa 0.88 and a corpus of over 1,600 annotated traces across 7 open-source multi-agent frameworks. Reported failure rates run from 41% to 86.7%.

The frequencies that bear on a ticket chain: step repetition 15.7%, reasoning-action mismatch 13.2%, unaware of termination conditions 12.4%, incorrect verification 9.1%, no or incomplete verification 8.2%, task derailment 7.4%, premature termination 6.2%, loss of conversation history 2.8%.

Two readings. The largest cluster is handoff and verification failures, not reasoning failures, which argues for spending effort on the state file and the verifier rather than on a coordinator. And a chain driven by code sidesteps the inter-agent-misalignment category almost entirely, which is 6 of the 14 modes.

Caveat: the frameworks studied are 2024-2025 open-source systems on older models. The absolute rates should not be read forward.

## What was not tested

- `async: true` on a command hook, so a Stop hook could launch the next ticket without blocking the parent.
- Whether `/implement <n>` expands correctly under `claude -p` in this repo. The mechanism is documented; the skill was not exercised.
- Whether the cold-cache toll measured on a trivial prompt holds at real ticket size.
- Resume-from-failure in a real chain. Every failure semantic above is read from a source.
