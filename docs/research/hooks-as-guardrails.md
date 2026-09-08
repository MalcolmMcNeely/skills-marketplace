# Hooks as guardrails

Researched 2026-09-08 against Claude Code 2.1.248. Anything labelled **measured** was run on this machine (Windows 11 Pro 26200, Git Bash) on that date. Anything else was read from [the hooks guide](https://code.claude.com/docs/en/hooks-guide) and [the hooks reference](https://code.claude.com/docs/en/hooks) and cited where it matters.

Starting state on this machine, for the record: `~/.claude/settings.json` has no `hooks` key, this repo has no `.claude/settings.json`, and no plugin under `plugins/` ships a `hooks/` directory. Every hook described here would be new.

## Why hooks are the right layer for a guardrail

A skill is an instruction. The model reads it and usually complies. A hook is a program. It runs whether the model wants it to or not, and a `PreToolUse` hook returning `deny` blocks the tool **even under `bypassPermissions` or `--dangerously-skip-permissions`**. Hooks can tighten what permission rules allow; they cannot loosen it. A hook returning `allow` does not override a matching deny rule.

That asymmetry is the whole reason to reach for a hook rather than another paragraph in `CLAUDE.md`. If a rule must hold when the agent is running unattended at 3am, it belongs in a hook.

One consequence to plan around: hooks are collected when the session starts. Editing settings mid-session is picked up by a file watcher "normally... automatically", but this is not guaranteed and may lag. Restart the session when you change a guardrail and want certainty. `/hooks` shows what is loaded, read-only.

## When hooks fire

Claude Code 2.1.248 fires hooks at these points. "Blocks" means the hook can stop the thing happening.

### Tool execution

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `PreToolUse` | tool name | **yes** | The main guardrail. Fires before every tool call, before permission prompts |
| `PostToolUse` | tool name | no | Tool already ran. Observe, format, inject context |
| `PostToolUseFailure` | tool name | no | A tool call failed |
| `PostToolBatch` | none | **yes** | After a batch of parallel calls resolves, before the next model call |

### Permission

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `PermissionRequest` | tool name | decision only | About to ask the human. Can auto-allow, defer, or change session permission mode |
| `PermissionDenied` | tool name | no | Auto mode denied something. Can set `retry: true` |

### Conversation flow

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `UserPromptSubmit` | none | **yes** | Vet or enrich the prompt before Claude sees it |
| `UserPromptExpansion` | command name | **yes** | A typed `/command` is expanding into a prompt |
| `Stop` | none | **yes** | Claude finished a turn. Block to force more work |
| `StopFailure` | error type | no | Turn ended on an API error (`rate_limit`, `overloaded`, …) |
| `MessageDisplay` | none | no | Assistant text is being displayed |
| `Notification` | notification type | no | Desktop alerts, idle prompts, `agent_needs_input` |

### Session and agents

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `SessionStart` | `startup`/`resume`/`clear`/`compact`/`fork` | no | Inject context at the top of a session |
| `SessionEnd` | `clear`/`resume`/`logout`/… | no | Cleanup. Budget is 1.5s shared across all such hooks |
| `Setup` | `init`/`maintenance` | no | One-time CI or script preparation |
| `SubagentStart` | agent type | no | A subagent spawned |
| `SubagentStop` | agent type | **yes** | A subagent finished. Block to make it keep going |
| `TeammateIdle` | none | **yes** | A teammate is about to go idle |
| `TaskCreated` | none | **yes** | Block to roll back a task creation |
| `TaskCompleted` | none | **yes** | Block to refuse a completion |

### Environment and config

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `ConfigChange` | `user_settings`/`project_settings`/`local_settings`/`policy_settings`/`skills` | **yes**, except `policy_settings` | Audit or refuse config tampering mid-session |
| `FileChanged` | literal filenames split on `\|`, not regex | no | A watched file changed on disk |
| `CwdChanged` | none | no | Working directory changed |
| `DirectoryAdded` | `slash_command`/`register_repo_root` | no | A directory was added mid-session |
| `InstructionsLoaded` | `session_start`/`nested_traversal`/`path_glob_match`/`include`/`compact` | no | `CLAUDE.md` or a rules file loaded |

### Context and model

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `PreCompact` | `manual`/`auto` | **yes** | Before compaction |
| `PostCompact` | `manual`/`auto` | no | After compaction. Pairs with `SessionStart` matcher `compact` to re-inject context |
| `PreModelSwitch` | model name | **yes** | Refuse a model switch |
| `PostModelSwitch` | model name | no | A model switch happened |

### MCP and worktrees

| Event | Matcher | Blocks | What it is for |
|---|---|---|---|
| `Elicitation` | MCP server name | via result | An MCP server is asking the human for input |
| `ElicitationResult` | MCP server name | **yes** | Before the human's answer goes back to the server |
| `WorktreeCreate` | none | **yes**, on any non-zero exit | Replace or refuse default worktree creation |
| `WorktreeRemove` | none | no | A worktree is being removed |

## What a hook can change

Every hook reads a JSON object on stdin and answers with an exit code, stdout, or both.

**Exit codes.** `0` is success: valid JSON on stdout is honoured, otherwise flow continues. For `UserPromptSubmit`, `SessionStart` and `PostToolUse`, plain stdout on exit 0 is added to Claude's context as text. `2` is a blocking error: the action is prevented and stderr is fed back to Claude as the reason. Any other non-zero code is a non-blocking error and the action proceeds, with `WorktreeCreate` as the one exception where any non-zero exit aborts.

**JSON output** goes under `hookSpecificOutput`. The fields that matter:

| Field | Where | Effect |
|---|---|---|
| `permissionDecision` | `PreToolUse`, `PreModelSwitch` | `allow` / `deny` / `ask` / `defer` |
| `permissionDecisionReason` | same | Text shown with the decision |
| `updatedInput` | `PreToolUse`, `UserPromptSubmit` | **Rewrite the tool's arguments or the prompt** |
| `additionalContext` | `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse` | Inject text Claude reads |
| `decision` + `reason` | `Stop`, `UserPromptSubmit`, `PostToolBatch` | `block`, with the reason becoming the next instruction |
| `retry` | `PermissionDenied` | Tell the model it may retry |
| `updatedPermissions` | `PermissionRequest` | Change the session's permission mode |
| `systemMessage` | `PostToolUse` | Message to Claude |
| `ok` + `reason` | prompt-type hooks | `ok: false` blocks, with the reason as instruction |

Two capabilities here are easy to miss. `updatedInput` means a `PreToolUse` hook can **rewrite** a tool call, not only allow or deny it. And when several hooks match one event they run in parallel, with `PreToolUse` decisions resolved most-restrictive-first: `deny` beats `defer` beats `ask` beats `allow`. If two hooks both return `updatedInput` for the same call, the last to finish wins, which is non-deterministic. Do not have two hooks rewriting the same tool.

## Five kinds of hook, not one

This is the finding most likely to change what you build. A hook does not have to be a shell script.

| `type` | What it does | Default timeout |
|---|---|---|
| `command` | Runs a program. stdin JSON in, exit code and stdout out | 600s |
| `http` | POSTs to a URL. Needs the URL allowlisted in `allowedHttpHookUrls` | 600s |
| `mcp_tool` | Calls a tool on a connected MCP server, with `${tool_input.…}` interpolation | 600s |
| `prompt` | **Asks a model.** Returns `{"ok": false, "reason": "…"}` to block | 30s |
| `agent` | **Runs a sub-agent** that can read files and use tools to decide | 60s |

`prompt` and `agent` hooks are guardrails that exercise judgment. A deterministic script cannot answer "does this change match what the ticket asked for". An `agent` hook can read the ticket and the diff and say so. The cost is latency, money and non-determinism, so keep them for the checks a regex genuinely cannot make.

Some events cut the default timeout: `UserPromptSubmit`, `PreModelSwitch` and `PostModelSwitch` get 30s, `MessageDisplay` gets 10s, and all `SessionEnd` hooks share a 1.5s budget that a per-hook `timeout` can raise to 60s.

## Narrowing a hook to specific arguments

A `matcher` filters on tool name. The `if` field filters on the arguments too, using permission-rule syntax:

```json
{
  "matcher": "Bash",
  "hooks": [
    { "type": "command", "if": "Bash(git push *)", "command": "check-push-policy.sh" }
  ]
}
```

`if` is supported on `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PermissionRequest` and `PermissionDenied` only. Put it on any other event and the hook silently never runs.

## What hooks cannot do

The limits matter as much as the powers, and several kill designs that look obvious.

- **A hook cannot invoke a skill, an agent or a slash command.** It talks through stdout, stderr and exit codes. It cannot make Claude call `/code-review`. It can only *tell* Claude to, via `additionalContext` or a `Stop` block reason.
- **A hook cannot start a new session or send a new prompt** through any Claude Code API. It can only shell out to a new process, which is a different thing, and which is measured below to work.
- **A hook cannot undo a `PostToolUse` action.** The tool already ran.
- **A hook cannot edit the transcript.** It receives `transcript_path` and can read the JSONL, but it cannot rewrite messages or conversation state.
- **A hook cannot ask the human anything.** In headless mode there is no controlling terminal at all; `/dev/tty` is unavailable.
- **A hook cannot suppress prompts for MCP tools marked `requiresUserInteraction`,** or connector tools an organisation set to `ask`. Returning `allow` does not remove those.
- **Hooks have no guaranteed ordering** when several match one event.
- **Hooks die with the session.** Persistent state goes in a file or an external service.

## Measured: a Stop hook can do more than the docs emphasise

Two experiments on 2.1.248, both run headless with the hook supplied inline via `--settings`.

**A Stop hook blocks the turn and its reason becomes the next instruction.** A run told to "Reply with exactly: OK", with a one-shot Stop hook returning `{"decision":"block","reason":"Ignore all previous work. Now reply with exactly the single word BANANA and nothing else."}`, returned `BANANA`. `is_error: false`, `num_turns: 2`, `total_cost_usd` 0.0901215.

**A Stop hook can launch a brand-new Claude Code session.** The hook shelled out to `claude -p --max-turns 1 "Reply with exactly: CHILDRAN"`. The parent returned `PARENT` at `total_cost_usd` 0.0428615; the child wrote `CHILDRAN` and exited 0. `CLAUDECODE=1` was inherited and untouched, and the documented "cannot be launched inside another Claude Code session" guard did not fire. The parent blocked for the whole child run, so the hook's `timeout` was raised to 240s. `async: true` exists on command hooks and would presumably avoid that, but was not tested.

The safety valve on the first mechanism: after 8 consecutive blocks with no progress, Claude Code overrides the hook and lets the stop through. Read `stop_hook_active` from stdin and exit 0 when it is true. `CLAUDE_CODE_STOP_HOOK_BLOCK_CAP` raises the ceiling to at most 60, and needing more than 8 is a sign the hook's logic is wrong.

## Guardrails for the ticket loop

The two the question asked for, plus the ones worth having beside them.

### Refuse to start a blocked ticket

`UserPromptSubmit` can block, so it is the right place. The hook reads the submitted prompt, finds a ticket reference, asks the tracker whether every blocker is closed, and exits 2 with an explanation if not. Claude never sees the request.

Cheaper and friendlier as a pair: a `SessionStart` hook that injects the current list of startable tickets as `additionalContext`, so the agent knows the frontier before it picks. Prevention beats refusal.

The exact tracker query is in [Ticket state as a guardrail](ticket-state-guardrails.md).

### Enforce that the ticket gets updated

This is what `Stop` blocking is for, and it is measured to work. When the turn ends, check whether the ticket the session claimed has been closed with a comment. If not, block with a reason naming the omission. The model gets the instruction and finishes the job.

Set the guard on `stop_hook_active`, so a ticket that genuinely cannot be closed does not trap the session in a loop of 8.

### The rest, in one list

| Guardrail | Event | Mechanism |
|---|---|---|
| Block destructive shell commands | `PreToolUse` matcher `Bash` | Inspect `.tool_input.command`, exit 2 |
| Protect files (`.env`, lockfiles, `.git/`) | `PreToolUse` matcher `Edit\|Write` | Inspect `.tool_input.file_path`, exit 2 |
| Refuse a commit that skips hooks | `PreToolUse` with `if: "Bash(git commit *)"` | Match `--no-verify`, exit 2 |
| Auto-format after every edit | `PostToolUse` matcher `Edit\|Write` | Pipe the path into the formatter |
| Re-inject conventions after compaction | `SessionStart` matcher `compact` | Echo the reminder to stdout |
| Log every shell command | `PostToolUse` matcher `Bash` | Append `.tool_input.command` to a file |
| Audit configuration changes | `ConfigChange` | Append source and path to an audit log |
| Refuse a model downgrade | `PreModelSwitch` matcher on model name | `permissionDecision: "deny"` |
| Judge a change against its ticket | `Stop`, type `agent` | Sub-agent reads the ticket and the diff |
| Cap what an unattended run may touch | `PreToolUse` | `deny` holds even under `bypassPermissions` |

Everything in that table except the last two rows appears as a worked example in the hooks guide.

## Rolling this out to a business

The guardrails are only worth building if a developer cannot switch them off. Claude Code has three levers for that, all read from [the hooks guide](https://code.claude.com/docs/en/hooks-guide) rather than measured here.

**Precedence.** Hooks accumulate from every scope at once and all matching ones fire. The scopes, weakest first: `~/.claude/settings.json`, then the repo's `.claude/settings.json`, then `.claude/settings.local.json`, then enabled plugins, then **managed policy settings**, which an administrator controls and a developer cannot override. Skill and sub-agent frontmatter can also carry hooks that apply only while they run.

**Lockdown switches.** `allowManagedHooksOnly` makes managed policy the only source of hooks, ignoring everything a developer writes locally. `disableAllHooks` turns the lot off. `allowedHttpHookUrls` and `httpHookAllowedEnvVars` gate what an `http` hook may reach and which secrets it may carry.

**Distribution.** A plugin can ship hooks in `hooks/hooks.json` at its root, with the same shape as the settings block, and `${CLAUDE_PLUGIN_ROOT}` resolving to the install directory. That is how a company ships a guardrail alongside the skills that need it, through the same marketplace, versioned in git. It is the mechanism this repo already uses for skills, applied to policy.

The `ConfigChange` event closes the loop. It fires when settings change mid-session, matches on which scope changed, and can block every scope except `policy_settings`. So policy changes flow through, local tampering can be refused, and either way it is logged.

## Where the design tension sits

There is one real conflict between the two halves of this research. Guardrails want to block. A daisy chain wants to keep moving unattended. A `Stop` hook that blocks until the ticket is closed is exactly the same mechanism as a `Stop` hook that launches the next ticket, and if both are configured they run in parallel with no guaranteed ordering.

Resolve it by putting them at different events. Enforcement belongs on `Stop`, where blocking is the point. Chaining belongs in the external driver loop, outside Claude Code entirely, where a non-zero exit from `claude -p` is a signal the driver can act on. Keeping control flow out of hooks is the same conclusion the [agent patterns report](agent-patterns-for-the-implement-loop.md) reaches for a different reason.

## What was not tested

- `async: true` on a command hook.
- `prompt` and `agent` hook types. Their existence and shape are read from the source.
- `allowManagedHooksOnly`, `disableAllHooks` and plugin-shipped hooks.
- Whether the file watcher reliably picks up a mid-session hook change on Windows.
- Any guardrail in the table above, end to end. The mechanisms are documented and two Stop-hook mechanics are measured; the recipes are not.
