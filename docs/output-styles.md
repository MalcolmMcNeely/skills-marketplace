# Output styles, and how to force one

Can we set one voice for every developer in the company, and stop them changing it? Yes. This is how, and what it costs.

Written September 2026 against Claude Code 2.1.248. Everything below is either quoted from the docs or was run on this machine.

## The short version

Managed settings beat everything else, so enforcement is a solved problem. The open questions are which style to force and whether forcing one is a good idea at all.

Two mechanisms reach every machine. One ships the style in a plugin and is versioned in git. The other pushes a loose Markdown file by MDM. We want the first.

## What an output style is

A Markdown file that Claude Code adds to its system prompt. It changes role, tone and format. It does not add project knowledge, which is what `CLAUDE.md` is for.

Five built-in styles ship today: Default, Proactive, Concise, Explanatory and Learning. Concise needs 2.1.237 or later.

The `/output-style` command was deprecated in 2.1.73 and removed in 2.1.91. Use `/config`, or set `outputStyle` in a settings file.

### Frontmatter

| Field | Effect | Default |
|---|---|---|
| `name` | Style name, if not the file name | File name |
| `description` | Shown in the `/config` picker | None |
| `keep-coding-instructions` | Keep Claude Code's built-in software engineering instructions | `false` |
| `force-for-plugin` | Plugin styles only. Applies the style whenever the plugin is enabled, with no user selection | `false` |

`keep-coding-instructions` is the trap. It defaults to **false**, so a custom style silently drops Claude Code's instructions on scoping changes, writing comments and verifying work. Any style we ship for engineers must set it to `true`.

`force-for-plugin` is the enforcement lever. Verbatim from the docs: "apply this style automatically whenever the plugin is enabled, without requiring users to select it. Overrides the user's `outputStyle` setting. If multiple enabled plugins set this, Claude Code uses the first one loaded."

### Where a style file can live

| Level | Path |
|---|---|
| User | `~/.claude/output-styles/` |
| Project | `.claude/output-styles/` |
| Managed policy | `.claude/output-styles/` inside the managed settings directory |
| Plugin | `output-styles/` in the plugin root |

Project styles load from every `.claude/output-styles/` between the working directory and the repository root. The one closest to the working directory wins.

In `plugin.json` the path override field is `outputStyles`, camelCase, against a kebab-case directory.

## Plugins can ship them, and we tested it

Verbatim: "Plugins can also ship output styles in an `output-styles/` directory."

Tested here. A throwaway plugin holding `output-styles/ELI5.md`, plus a marketplace entry pointing at it:

```
claude plugin validate ./plugins/style      -> Validation passed
claude plugin validate .                    -> Validation passed with warnings
```

The only warning was a missing marketplace description in the test fixture. The output style directory itself raised nothing.

Note that Anthropic's own `explanatory-output-style` plugin does **not** use this directory. It injects the text through a `SessionStart` hook and its README calls the Explanatory style deprecated. The current docs still list Explanatory as a built-in style, so that README looks stale. Ship the directory, not the hook.

## Enforcement

### Precedence

| Rank | Level | File |
|---|---|---|
| 1 | Managed settings | `managed-settings.json`, MDM, or the claude.ai console |
| 2 | Command line | `claude --settings` |
| 3 | Project local | `.claude/settings.local.json` |
| 4 | Shared project | `.claude/settings.json` |
| 5 | User | `~/.claude/settings.json` |

Verbatim: "Claude Code applies them above every other level, so no user, project, local, or `--settings` value overrides them, apart from a few security-sensitive exceptions where a stricter value from a lower level still counts."

An output style is not security-sensitive, so managed wins outright. This matters because `/config` writes a developer's choice to `.claude/settings.local.json`, which is rank 3.

### Where the policy file goes

| OS | Path |
|---|---|
| macOS | `/Library/Application Support/ClaudeCode/managed-settings.json` |
| Linux and WSL | `/etc/claude-code/managed-settings.json` |
| Windows | `C:\Program Files\ClaudeCode\managed-settings.json` |

Claude Code no longer reads the legacy Windows path `C:\ProgramData\ClaudeCode\managed-settings.json`. Anyone carrying an old policy there is running unmanaged.

A `managed-settings.d/` directory sits beside the file. Claude Code merges `managed-settings.json` first, then every `*.json` in the directory in alphabetical order. Numeric prefixes control the order. Use this to let each team own its slice of the policy.

### The keys

| Key | Scope | What it does |
|---|---|---|
| `outputStyle` | Any file | Names the style to use |
| `enabledPlugins` | Any file | Turns individual plugins on or off |
| `extraKnownMarketplaces` | Any file | Registers marketplaces for a repository or an organisation |
| `strictKnownMarketplaces` | Managed only | Allowlists which marketplaces users can add and install from |
| `strictPluginOnlyCustomization` | Managed only | Blocks skills, agents, hooks and MCP servers from user and project sources. `true` locks all four, an array names which |
| `disableSideloadFlags` | Managed only | Rejects `--plugin-dir`, `--plugin-url`, `--agents` and `--mcp-config` at startup. Needs 2.1.193 or later |

The first three carry scope "Any file", so a managed source can set them.

One caveat that does not apply to us: `extraKnownMarketplaces` waits for workspace trust when it comes from a repository's `.claude/settings.json`. That gate is documented under committed project keys, not managed ones.

## The two routes

| | A. Plugin | B. Loose file by MDM |
|---|---|---|
| Style lives in | `plugins/<name>/output-styles/` | `.claude/output-styles/` in the managed directory |
| Enforced by | `force-for-plugin: true`, plugin force-enabled by policy | `outputStyle` in `managed-settings.json` |
| Versioned in git | Yes | No |
| Needs a file push per machine | No, only the policy | Yes, the policy and the style |
| Update path | Marketplace auto-update | Another MDM push |

Take route A. It is the same argument the rest of this repo makes: a merge here should reach every developer without anyone asking. Route B reintroduces the copy-paste drift we are trying to kill, and our own [findings](findings.md) note that nobody in the surveyed companies pushed loose skill files by MDM.

Route A still needs a managed file, but that file only names the marketplace and the plugin. It changes once. The style changes as often as we like.

## Limits worth knowing before anyone commits

**Subagents ignore it.** An output style applies to the main conversation only. A subagent runs its own system prompt. A forced style therefore produces one voice in chat and a different one from every subagent. A fork is the exception, because it inherits the parent's full system prompt.

**It needs a restart.** The style is part of the system prompt, which Claude Code reads once at session start. A change lands after `/clear` or a new session, not immediately.

**It costs tokens both ways.** Instructions in the system prompt raise input tokens, though caching absorbs most of that after the first request. Output tokens move with the style: Explanatory and Learning make responses longer by design, Concise makes them shorter.

**Styles that are not about coding belong in a subagent.** Anthropic's own guidance says so: output styles that involve tasks besides software development are better expressed as subagents, because a subagent replaces the system prompt while a `SessionStart` hook only adds to it.

## The recommendation

Enforce the mechanism, not a personality.

If we force a style, force one that survives contact with hard work. `Concise` is a defensible org default because it shortens prose while keeping the engineering behaviour intact. A style tuned to one person's preferred reading level is not an organisational standard, however pleasant it is to use.

Whatever we pick must set `keep-coding-instructions: true`. Without it we quietly delete Claude Code's engineering instructions for the entire company.

Before rollout, add one question to the IT list already in [recommendation.md](recommendation.md): who owns `managed-settings.json`, and what is the turnaround on changing it? If the answer is measured in weeks, route A matters more, because only the policy file is slow.

## What we could not verify

- Whether `force-for-plugin` beats a managed `outputStyle` value. The docs say it overrides "the user's" setting and say nothing about a managed one. Untested.
- The exact combined path for a managed style file. The docs say `.claude/output-styles` "inside the managed settings directory" without spelling out the joined path, and we did not write to `C:\Program Files\ClaudeCode` to check.
- What an output style change does to the prompt cache. The docs point at a prompt-caching page we have not read.
- Whether the Explanatory style was un-deprecated or Anthropic's plugin README is simply stale. The two sources disagree.
