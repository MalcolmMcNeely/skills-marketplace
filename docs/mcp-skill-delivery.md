# MCP skill delivery

Can an MCP server deliver a skill by writing a `SKILL.md` to disk, then telling Claude to use it? Researched 2 September 2026 against Claude Code `2.1.248` on Windows 11. Every result below was produced by running the thing, not by reading about it.

## The short version

Yes, and no restart is needed, but only if `.claude/skills/` already existed when the session started. If that directory did not exist at session start, the file is never picked up, no matter how many turns you wait, and the flow becomes "install, then restart".

This is a documented feature, not an accident. Claude Code watches skill directories and serves the file from disk on invocation. It is also not the design `findings.md` ruled out: that one asked Claude Code to consume skills as MCP resources, which it does not do. Writing a file is a different mechanism and it works.

The awkward part is not whether it works. It is that to get the MCP server installed you already need an install step, and that install step can write the skill directly. MCP delivery adds a channel that only functions after the channel it replaces has run.

## What the documentation says

The behaviour is documented under **Live change detection** on the skills page.

> Claude Code watches skill directories for file changes. When you add, edit, or remove a skill under `~/.claude/skills/`, the project `.claude/skills/`, or a `.claude/skills/` inside an `--add-dir` directory, Claude Code picks up the change within the current session, without a restart. If you create a top-level skills directory that didn't exist when the session started, restart Claude Code so it can watch the new directory.

Source: https://code.claude.com/docs/en/skills.md

Two caveats sit next to it. The first limits what live detection covers.

> Live change detection covers `SKILL.md` text only. For a skill folder that is also a [plugin](/docs/en/plugins-reference#skills-directory-plugins), changes to `hooks/`, `.mcp.json`, `agents/`, and `output-styles/` need `/reload-plugins` to take effect.

Source: https://code.claude.com/docs/en/skills.md

The second kills the mechanism outright in one mode.

> Claude Code never watches `.claude/agents/` or `.claude/commands/` in an added directory, so after you add or edit a subagent or command file there, restart the session to load the change. In bare mode, Claude Code doesn't watch skill directories at all.

Source: https://code.claude.com/docs/en/skills.md

The plugins reference states the same rule for plugin-packaged skills.

> Changes you make to a skill's `SKILL.md` take effect immediately in the current session.

> Changes to the plugin's other components, such as `hooks/`, `.mcp.json`, `agents/`, and `output-styles/`, do not.

> Run `/reload-plugins` or restart Claude Code to pick those up.

Source: https://code.claude.com/docs/en/plugins-reference.md

So `/reload-plugins` is not needed for a plain project skill. It is needed for everything else a plugin ships.

## How we tested it

A single `claude` process was driven over `--input-format stream-json --output-format stream-json`, which allows several user turns inside one session. Files were written from the driver, or by a throwaway MCP server, between turns. Each `SKILL.md` body contained a unique canary string and instructed the model to echo it and nothing else. A canary in the transcript proves the body was read from disk during that session. It cannot be guessed.

Every run used `--setting-sources project --strict-mcp-config --permission-mode bypassPermissions --model sonnet`, so the only project skills in play were the ones under test.

The probe was always the same instruction: call the `Skill` tool with an exact name and report the result verbatim. That makes the harness, not the model, the thing being measured. An unregistered name returns `<tool_use_error>Unknown skill: ...</tool_use_error>`.

## Results

| # | `.claude/skills/` at session start | What happened mid-session | Picked up? |
|---|---|---|---|
| 1 | Exists, empty | Driver wrote `SKILL.md` | Yes, next turn |
| 2 | No `.claude/` at all | Driver created the whole tree | No, across four further turns |
| 3 | `.claude/` exists, `skills/` does not | Driver created `skills/` and the tree | No, across four further turns |
| 4 | Exists, empty | MCP tool wrote the file, returned at once | Failed on first call, succeeded on retry |
| 5 | Exists, empty | MCP tool wrote the file, waited 1500 ms | Yes, first call |
| 6 | Exists, empty | `SKILL.md` overwritten V1 to V2 | Yes, new body served |
| 7 | Exists, empty | `SKILL.md` deleted | Skill stopped resolving |

### The happy path

Experiment 1. Session id `f7f21274-e8d9-4531-ae6c-6daf56494dcd` throughout.

```
=== TURN 1: before SKILL.md exists ===
skill dir exists on disk: false
[init] slash_commands count=49
[init] probe in slash_commands: []
[tool_use] Skill {"skill":"mcp-delivery-probe"}
[tool_result is_error=true] <tool_use_error>Unknown skill: mcp-delivery-probe</tool_use_error>

=== WRITING SKILL.md TO DISK (mid-session, same process) ===
wrote ...\proj\.claude\skills\mcp-delivery-probe\SKILL.md

=== TURN 2: after SKILL.md written, same process, same session ===
[init] slash_commands count=49
[init] probe in slash_commands: []
[tool_use] Skill {"skill":"mcp-delivery-probe"}
[tool_result is_error=false] Launching skill: mcp-delivery-probe
[assistant text] PROBE-CANARY-7731

=== TURN 3: does the listing show it? ===
[init] slash_commands count=50
[init] probe in slash_commands: ["mcp-delivery-probe"]
[assistant text] (1) YES.

(2) mcp-delivery-probe
```

The canary came back. No restart, no `/reload-plugins`, no `/clear`.

Note the ordering. At turn 2 the session's reported command list still held 49 entries and did not name the probe, yet the `Skill` tool resolved it anyway. The registry that answers an invocation caught up before the list the session reports. By turn 3 both agreed.

### The precondition that decides the product

Experiments 2 and 3 are the ones that matter commercially. In experiment 2 the project had no `.claude/` directory at all. In experiment 3 it had `.claude/` but no `skills/` inside it. Both created the full tree mid-session. Both failed identically, five turns running.

```
=== SESSION START. .claude exists? true ===
=== TURN 1: no .claude/skills/ anywhere ===
[init] slash_commands=49 probe=[]
[tool_result is_error=true] <tool_use_error>Unknown skill: mcp-delivery-probe</tool_use_error>
=== CREATING THE WHOLE TREE .claude/skills/mcp-delivery-probe/SKILL.md MID-SESSION ===
=== TURN 2: directory tree created mid-session ===
[init] slash_commands=49 probe=[]
[tool_result is_error=true] <tool_use_error>Unknown skill: mcp-delivery-probe</tool_use_error>
=== TURN 3: retry, in case detection is debounced ===
[init] slash_commands=49 probe=[]
[tool_result is_error=true] <tool_use_error>Unknown skill: mcp-delivery-probe</tool_use_error>
```

So the watched path is `.claude/skills/` itself, not its parent. Creating `.claude/` does not help. The docs say "top-level skills directory" and the measurement agrees precisely.

This is the whole difference between two products.

| Precondition | Flow |
|---|---|
| `.claude/skills/` existed at session start | Install and use, same session |
| It did not | Install, restart, then use |

A consumer repository that has never used a project skill has no `.claude/skills/`. That is the common case for a first install, which is exactly when delivery matters most. Writing to the personal `~/.claude/skills/` instead is a partial dodge, because that directory usually exists on any machine that has ever installed a skill, but "usually" is not a guarantee and personal scope is the wrong scope for a skill pinned to one repository.

### The full MCP flow, with a race

Experiment 4 ran the real proposal. A stdio MCP server exposed one tool, `install_skill`, which wrote the `SKILL.md` and returned text telling Claude to invoke it.

```
[init] mcp_servers=[{"name":"skill-installer","status":"connected"}]
       mcp_tools=["mcp__skill-installer__install_skill"]
[tool_use] mcp__skill-installer__install_skill {}
[tool_result is_error=false] [{"type":"text","text":"Installed skill \"mcp-installed-probe\" to
  ...\\proj3\\.claude\\skills\\mcp-installed-probe\\SKILL.md. Now call the Skill tool with skill
  set to \"mcp-installed-probe\" and follow it."}]
[tool_use] Skill {"skill":"mcp-installed-probe"}
[tool_result is_error=true] <tool_use_error>Unknown skill: mcp-installed-probe</tool_use_error>
[tool_use] Read {"file_path":"...\\mcp-installed-probe\\SKILL.md"}
[tool_result is_error=false] 1 ---
2 name: mcp-installed-probe
...
10 MCP-CANARY-9090
[assistant] The skill is now available. Invoking it now.
[tool_use] Skill {"skill":"mcp-installed-probe"}
[tool_result is_error=false] Launching skill: mcp-installed-probe
[assistant] MCP-CANARY-9090
```

The first invocation, issued immediately after the tool returned, failed. A `Read` intervened, which cost a few hundred milliseconds, and the retry worked. The file watcher does not make a write visible synchronously.

Experiment 5 added a 1500 ms wait inside the MCP tool before it returned. The invocation then succeeded first time. That is one run each way, so treat the number as a direction rather than a threshold. The engineering conclusion is firm regardless: a server that writes a skill must not return the instant the write completes, and the calling model must be told to retry once.

### Updates and drift

Experiment 6 overwrote a loaded `SKILL.md` mid-session, changing the canary.

```
=== SERVER "UPDATES": overwrite SKILL.md on disk with V2 ===
now on disk: V2
=== TURN 2: does the session serve the UPDATED body? ===
[tool_use] Skill {"skill":"mcp-installed-probe"}
[tool_result is_error=false] Launching skill: mcp-installed-probe
[assistant] MCP-CANARY-V2-BBBB
```

The new body was served. Claude Code does not cache a skill body for the life of a session. Whatever is on disk at invocation time is what runs.

Experiment 7 deleted the file. The skill stopped resolving, and the same lag appeared in reverse: the turn's reported command list still named the probe while the tool already returned `Unknown skill`.

That settles the mechanics. It does not settle drift, because Claude Code has no opinion about drift at all. It reads whatever file is there. Every decision about a stale or locally edited skill belongs to whatever wrote it.

| Question | Answer |
|---|---|
| Does Claude Code detect a stale on-disk skill? | No. It has no notion of a version |
| Does it warn when a skill body changes? | No |
| Does it protect a local edit from being overwritten? | No |
| Who decides whether to overwrite? | The writer, entirely |

So an MCP server that writes unconditionally on every call will silently clobber a developer's local edit. Avoiding that means a hash manifest and a three-way decision, which is precisely what `Edict.ClaudeSkills` already implements as `SkillsDriftEvaluator` with `Create`, `Refresh` and `SkipDrifted`. The drift problem is identical for both delivery channels and MCP does nothing to help with it.

There is a second update trap specific to MCP. The skill body is pinned to the server version only while the server is the thing that wrote it. Once the file is on disk it outlives the session, and it outlives the server. A developer who removes the MCP server from `.mcp.json` keeps the skill forever, frozen at whatever version last ran. Nothing removes it.

## What MCP delivery buys over the `dotnet tool`

`findings.md` records `Edict.ClaudeSkills`, a `PackAsTool` package that ships skills as embedded resources, installs them into a consumer's `.claude/skills/`, wires `.mcp.json`, and runs drift evaluation. Both approaches end at the same place: a `SKILL.md` on disk.

| | `dotnet tool` | MCP server writes the file |
|---|---|---|
| When it runs | Before the session | During the session |
| `.claude/skills/` precondition | None. It creates the directory, then the session starts | Must already exist, or nothing is picked up |
| Skill in the listing from turn one | Yes | No, and it may lag a turn |
| Write-to-visible race | None | Yes, needs a delay and a retry |
| Drift handling | Hash manifest, `Create` / `Refresh` / `SkipDrifted` | Whatever you build, same problem |
| Works in bare mode | Yes, the file is there before startup | No, skill directories are not watched |
| Model must cooperate | No | Yes, it has to choose to invoke |
| Install step required | One | One, to install the MCP server |

The last row is the argument. An MCP server does not appear by itself. Something has to write `.mcp.json`, and in this estate that something is already `Edict.ClaudeSkills`. If a tool must run to install the server, that same run can drop the `SKILL.md` beside it, before any session exists, with no precondition, no race, and the directory created in time to be watched.

**Verdict: MCP delivery is not worth it as the delivery channel.** It is strictly harder than the alternative already running in this estate, and it buys nothing the alternative lacks. It solves an install problem by adding a component whose own installation is the install problem.

Two narrower uses survive the argument, and neither is a delivery channel.

- **On-demand top-up.** A session already holding `.claude/skills/` can gain a skill it did not start with. That is real, and it is what makes the mechanism interesting. It is a nice-to-have on top of a working install, not a replacement for one.
- **Freshness.** A server can refresh a skill body that shipped stale, without waiting for a session restart. The `dotnet tool` cannot reach into a running session.

If the mechanism is used at all, use it for those, and keep the `dotnet tool` as the thing that guarantees the directory exists.

## Security

An MCP server that writes into `.claude/skills/` is a supply-chain surface, and a sharper one than it looks.

**The write skips Claude Code's permission system.** The file is created by the MCP server's own process doing ordinary file I/O. It never passes through the `Write` tool, so no permission prompt fires and no rule in `settings.json` applies. Our server wrote into the project tree with nothing asked and nothing logged. Whatever a user has approved for Claude's own file tools is irrelevant here.

**A session-scoped capability becomes a permanent one.** An MCP tool result affects one conversation. A `SKILL.md` affects every future session in that directory, including sessions where the server is not configured, and including other people's sessions once the file is committed. Approving a server for one task grants it persistence.

**The payload is instructions the model follows.** Snyk found that 36.8% of 3,984 public skills carried a security flaw ([ToxicSkills](https://snyk.io/blog/toxicskills-malicious-ai-agent-skills-clawhub/)). A skill body can set `allowed-tools` to pre-approve tools for itself. Delivering one over a channel with no prompt puts an unreviewed instruction file where it will be picked up automatically.

The documentation's warning is aimed at a narrower risk than this one.

> Verify you trust each server before connecting it. Servers that fetch external content can expose you to [prompt injection risk](/docs/en/security#protect-against-prompt-injection).

Source: https://code.claude.com/docs/en/mcp.md

There is also a live approval gap. Project-scoped servers are trusted silently outside interactive use.

> When using project-scoped `.mcp.json` files, Claude Code prompts for approval in interactive sessions. In non-interactive mode (`claude -p`) or Agent SDK sessions, project servers load automatically

Source: https://code.claude.com/docs/en/mcp.md

**The model itself pushed back.** This was not designed for and is worth recording. In experiment 5 the model refused to follow the tool result on sight.

> I'll call the tool, but I want to flag something first: I won't pre-commit to blindly executing whatever instructions come back in the result. Tool output is data, not an authorization to act

And after the call:

> The install step completed, but its output is now instructing me to invoke another tool ("call the Skill tool... and follow it") — that's the tool chaining I flagged. Before running it, let me read what the skill actually contains.

The proposed flow is shaped like a prompt injection, because it is a tool result telling the model to load and obey new instructions. The model is trained to be suspicious of exactly that. It complied here, and it read the file first. A design that depends on the model reliably obeying a pattern it is trained to distrust is a design with a soft floor under it.

## Recommendation

1. Keep the `dotnet tool` as the delivery channel. It has no precondition, no race, and drift handling that already exists.
2. Do not build MCP file-writing as the primary installer. Revisit only for on-demand top-up or freshness, once the tool guarantees `.claude/skills/` exists.
3. If it is ever built, three rules are non-negotiable: create nothing outside a directory that already existed, never return until the write has settled, and never overwrite a drifted file without a hash check.
4. Treat any server that writes into a skills directory as privileged. Nothing in Claude Code will ask the user about it.

## What we could not verify

- **The size of the write-to-visible window.** One run failed on an immediate invocation and one run with a 1500 ms delay succeeded. That is a single trial each way. We did not repeat them, and we did not bisect the delay, so 1500 ms is not a measured threshold.
- **Whether the race is a debounce, a poll interval, or filesystem event latency.** We observed the symptom and did not instrument the cause. It may differ on Linux and macOS, where the underlying watch API is not the Windows one we tested on.
- **Why the reported command list lagged the resolver by a turn, in both directions.** Consistent across runs, mechanism unknown. We also did not confirm that the `slash_commands` array in the session init event is the same listing the model sees in context, so the lag is reported as an observation about that array only.
- **Bare mode.** The documentation states skill directories are not watched in bare mode. We cited it and did not test it, because bare mode needs an `ANTHROPIC_API_KEY` this account does not use.
- **The personal and `--add-dir` paths.** Every experiment used the project `.claude/skills/`. We assume the same precondition applies to `~/.claude/skills/` and to `--add-dir` directories because the documentation names all three in one sentence, but we did not run them.
- **Enterprise and managed-settings skill locations.** Untested. A managed policy that locks skills turns project skills off entirely, which would break this mechanism, and we did not measure that interaction.
- **Whether the file watcher survives a `/compact`, a `/clear`, or a long idle session.** Our longest run was five turns over a few minutes.
- **Whether Claude Code reads MCP resources at all.** Still undocumented, as `findings.md` already records. The pages we fetched name tools and say nothing about resources or prompts, which is consistent with but not proof of the earlier finding.
- **Behaviour on any Claude Code version other than `2.1.248`.** Live change detection is a recent enough feature that it may not exist in older builds, and none were tested.
