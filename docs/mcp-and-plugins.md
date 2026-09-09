# MCP servers and plugins

What each one can carry, where they overlap, and how an organisation ships both. Researched 9 and 10 September 2026 against Claude Code `2.1.248` on Windows 11.

Three sources, labelled throughout. **Measured** means a command was run on this machine and its output is quoted. **Binary** means the string was extracted from the shipped `claude.exe`, a 226 MB compiled bundle whose embedded JavaScript is readable with `grep -a`. That is the implementation rather than a description of it, so it outranks the docs where the two disagree, and in one place they do. **Docs** means code.claude.com, cited by page.

## The short version

They are not alternatives. A plugin is a delivery vehicle; an MCP server is a capability. The plugin manifest has an `mcpServers` key, so the vehicle carries the capability, and Anthropic's own documentation recommends exactly that arrangement.

The stdio transport takes a bare `command` and `args` with no restriction on what the command is. A plugin can therefore ship a compiled .NET MCP server, and `${CLAUDE_PLUGIN_ROOT}` resolves to the installed plugin directory so the manifest can point at a binary sitting beside it. Verified by execution, not by reading.

Two findings cut against the usual advice. First, the "MCP tool definitions flood your context" argument no longer holds: tool search is on by default and loads names only, so a tool now costs less at session start than a skill description does. Second, an organisation can push a plugin marketplace to every machine but cannot push the plugin itself, which is the gap that decides how a rollout is actually built.

The two routes are also asymmetric in shape. Plugins arrive through settings keys that merge across five layers. MCP arrives through a separate file whose mere presence takes exclusive control of MCP.

## What each one carries

The plugin manifest has 28 top-level keys **[binary]**, assembled from sixteen partial schemas. The ones that ship something:

| Key | Ships | Counted by `plugin details` |
|---|---|---|
| `skills` | Skill directories | Yes |
| `commands` | Slash commands | Yes, folded into Skills |
| `agents` | Subagents | Yes |
| `hooks` | Lifecycle hooks | Yes |
| `mcpServers` | MCP server definitions | Partly, see below |
| `lspServers` | Language servers | Yes |
| `outputStyles` | Output styles | **No** |
| `themes` | Colour themes | No |
| `workflows` | Workflow `.js` scripts | No |
| `channels` | An MCP server bound as a message channel | No |
| `monitors` | Background watch processes | No |
| `settings` | Settings merged at the base layer | No |
| `userConfig` | Values prompted for at enable time | No |
| `binaries` | sha256-pinned downloads into `bin/` | No |
| `dependencies` | Other plugins | No |

An MCP server carries three primitives, and the spec is explicit about who controls each **[docs, modelcontextprotocol.io, revision 2026-07-28]**:

> **Prompts**: Pre-defined templates or instructions that guide language model interactions
> **Resources**: Structured data or content that provides additional context to the model
> **Tools**: Executable functions that allow models to perform actions or retrieve information

Only tools are model-controlled. Prompts are user-controlled and resources are application-controlled, and that distinction decides most of what follows.

### What Claude Code actually calls

A logging stdio server recorded the whole handshake **[measured]**:

```
CALL  initialize  id=0
CALL  notifications/initialized
CALL  tools/list  id=1
CALL  prompts/list  id=2
CALL  resources/list  id=3
```

Claude Code fetches all three primitives. It never calls these at startup: `resources/templates/list`, `completion/complete`, `logging/setLevel`.

The client capabilities Claude Code declares, verbatim from the `initialize` request **[measured]**:

```json
"capabilities": { "roots": { "listChanged": true }, "elicitation": {} }
```

Sampling is absent, and the word appears in none of the ten documentation pages fetched. A server cannot call back into the model.

That set is also shrinking. Under SEP-2577 the spec now lists roots, sampling and logging as deprecated, earliest removal "First revision released on or after 2027-07-28" **[docs, spec deprecated registry]**. Of the three client features, the one Claude Code omits is being removed anyway, and one of the two it implements is being removed as well. Do not build a rollout on client features.

## A plugin can ship a compiled MCP server

This is the question the Edict case turns on, and the answer is unambiguous.

The stdio arm of the transport union **[binary]**:

```js
CKe = m({type:F("stdio").optional(), command:i().min(1,"Command cannot be empty"),
         args:x(i()).default([]), env:Oe(i(),i()).optional(), ...})
```

`command` is constrained to being a non-empty string and nothing else. No extension check, no interpreter allowlist. The LSP schema next to it does carry a refinement, "Command should not contain spaces. Use args array for arguments." The MCP stdio arm has not even that.

Execution was then confirmed directly. A plugin declaring `{"runner":{"command":"cmd","args":["/c","echo","launched",">","EXEC_MARKER.txt"]}}` produced the file **[measured]**:

```
$ claude --plugin-dir <dir> mcp list
plugin:exec-probe:runner: cmd /c echo launched > EXEC_MARKER.txt - ✘ Failed to connect
$ cat EXEC_MARKER.txt
launched
```

The child process ran. Any executable works: a single-file `.exe`, `dotnet <path>.dll`, a global dotnet tool on `PATH`, a Go binary, a shell script.

### Pointing at the binary

Three variables expand inside `command`, `args`, every `env` value, `url` and every header value **[binary, confirmed measured]**:

| Variable | Resolves to |
|---|---|
| `${CLAUDE_PLUGIN_ROOT}` | The installed plugin directory |
| `${CLAUDE_PLUGIN_DATA}` | `~/.claude/plugins/data/<plugin>-<source>` |
| `${CLAUDE_PROJECT_DIR}` | The session's working directory |

An unset `${ENV_VAR}` is left literal rather than blanked, which turns a typo into a confusing runtime failure rather than an empty string.

So a manifest may say `"command": "dotnet", "args": ["${CLAUDE_PLUGIN_ROOT}/server/MyServer.dll"]`, and it validates **[measured]**.

### `mcpServers` takes four forms

**[binary]**, all four confirmed **[measured]**:

| Form | Example |
|---|---|
| A relative `.json` path | `"./servers/.mcp.json"` |
| An MCPB or DXT bundle, path or https URL | `"https://example.com/pack.mcpb"` |
| An inline object keyed by server name | `{"edict": {"command": "dotnet", ...}}` |
| An array mixing all three | `["./a.json", "./b.mcpb", {...}]` |

Rejected: a `.yaml` path, an unknown transport `type`, and a stdio entry with `args` but no `command`. Author-facing transports are `stdio` (the default), `sse`, `http`/`streamable-http`, and `ws`. Four further arms exist in the same union and are host-internal: `sse-ide`, `ws-ide`, `sdk`, `claudeai-proxy`.

The loader merges the plugin root's `.mcp.json` first, then whatever the manifest resolves to.

### Getting the binary onto the machine

Two routes. Commit it to the plugin repository, where nothing checks it and `${CLAUDE_PLUGIN_ROOT}` finds it. Or declare `binaries`, a map of basename to sha256 that Claude Code fetches into `bin/` at install time.

`binaries` has limits worth knowing before relying on it. Claude Code fetches at most 16 entries, and drops everything beyond 64 outright **[binary]**. More importantly, the target-triple table has no Windows entry **[binary]**:

```js
var MVn=["aarch64-apple-darwin","x86_64-apple-darwin",
         "aarch64-unknown-linux-musl","x86_64-unknown-linux-musl"];
```

The platform mapper returns `undefined` for `windows`. The auto-fetch path therefore resolves no triple on Windows. This was not tested end to end, so treat it as read from the source rather than measured.

Declaring `binaries` does switch on a useful cross-check. The validator matches every server command against `${CLAUDE_PLUGIN_ROOT}/bin/<name>` and verifies the basename **[measured]**:

```
⚠ mcpServers.s: bin/typo-name.exe is not a shipped file, a declared binaries entry,
  or a name derivable from the declared entries — the server will fail to start.
```

With no `binaries` map that check is skipped.

## One plugin can install another

`dependencies` is real dependency resolution, not a documentation hint.

Accepted reference formats **[measured]**: `other-plugin`, `other-plugin@marketplace`, `other-plugin@marketplace@^1.2.0`, and the object form `{"name": ..., "marketplace": ..., "version": "^1.0.0"}`. A tilde or `>=` range is rejected.

There is a trap. The version constraint is read from the raw JSON and the extractor skips strings **[binary]**:

```js
for(let o of t){ if(o===null||typeof o!=="object")continue;   // strings skipped
```

Meanwhile the string arm's transform is `.replace(/@\^[^@]*$/,"")`, which strips the caret range and throws it away. **Only the object form pins a version.** `plugin@mkt@^1.0.0` validates and does nothing. The object form also accepts `sha`, pinning to a git commit.

Auto-install is guarded rather than automatic in the loose sense. An installed plugin record carries an `auto` flag **[binary]**:

> True when this plugin was pulled in as a dependency rather than installed explicitly. Auto-installed plugins are eligible for removal by the orphan sweep when nothing depends on them.

That sweep is `claude plugin prune` **[measured]**, whose help reads "Remove auto-installed dependencies that are no longer needed".

Cross-marketplace dependencies need permission from the marketplace, via `allowCrossMarketplaceDependenciesOn`. Its description reads "Only the root marketplace's allowlist applies — no transitive trust" **[binary]**. Resolution walks the closure with cycle detection, and a plugin whose dependency is missing, disabled or version-unsatisfied is demoted at enable time rather than failing loudly.

## MCP cannot deliver a skill

[mcp-skill-delivery.md](mcp-skill-delivery.md) already covers the route where a server writes a `SKILL.md` to disk. This is the other question: does any MCP primitive contribute a skill?

No, and it was measured rather than inferred. A server advertising a prompt puts it in the session's `slash_commands` array as `mcp__probe__probe_prompt` and **not** in the `tools` array **[measured]**. There is no `SlashCommand` tool in Claude Code's tool list, so the model has no way to fire it.

An MCP prompt is therefore weaker than a skill carrying `disable-model-invocation: true`. Both need a human to type them, but a skill entry point can compose, as `Call the Skill tool with "name"`. A prompt cannot be invoked by the model at all.

Resources are content the model can read, exposed as `@server:protocol://resource/path` mentions plus three built-in tools. Nothing registers a resource in the skill namespace and nothing gives it a trigger description. The skills page enumerates seven places a skill can load from: enterprise, personal, project, nested, additional directory, plugin, claude.ai account. MCP is not among them **[docs, skills.md]**.

The nearest thing MCP has is the server `instructions` field, and the docs draw the comparison themselves **[docs, mcp.md]**:

> Server instructions help Claude understand when to search for your tools, similar to how skills work.

It is a description-level analogue only: at most 2 KB, always loaded, one per server, and it can only point at that server's own tools. It carries the always-on cost of a skill description with none of the progressive disclosure.

## Where they conflict

### Identity collision silently suppresses a plugin's server

This is the sharpest edge found, and nothing warns you at the point of use.

Plugin servers register as `plugin:<plugin>:<server>` **[binary, confirmed measured]**, so they cannot collide by name. They collide by **identity**. Claude Code fingerprints every server by command plus args, ignoring environment, or by normalised URL, then drops any plugin server matching one already configured **[binary]**:

```js
n(`Suppressing plugin MCP server "${g}": duplicates manually-configured "${M}"`)
```

Measured directly. A plugin declared `inline-srv` running `probe-nonexistent-binary alpha beta`. A user-scope server with a **different name** and the identical command was added. The plugin's server then vanished from `mcp list` entirely, with no warning printed **[measured]**.

For Edict this matters concretely. A developer who has already run `claude mcp add` for `dotnet edict-mcp`, and who then installs a plugin declaring the same command, gets the user's copy and loses the plugin's, including any `env` the plugin set, because the fingerprint ignores environment.

### Tool name normalisation can merge two servers

`mcp__` prefixes are built by replacing every character outside `[A-Za-z0-9_-]` with `_`, and the parser re-splits on `__` **[binary]**. So `plugin:my-plugin:my-server` becomes the prefix `mcp__plugin_my-plugin_my-server__`. Distinct servers can normalise to one prefix. The binary carries a guard for the case in the eval harness: "the plugins under test declare MCP servers "A" and "B", whose tool names collide".

A hook matcher written against the bare server key never fires for a plugin-bundled server **[docs, mcp.md]**.

### A skill and a tool competing for the same job

Not a mechanical conflict, but the real one in practice. Edict's answer is worth copying: rather than hoping description matching picks the right one, each MCP tool ships with a paired clause in a skill body naming it. Their own docs say why **[Edict, `docs/usage/agentic/integration.md:48`]**:

> A reference like 'use the Edict MCP' will not make the agent call the right tool reliably. Use the literal tool name.

## The context cost argument has flipped

The standard reason to prefer a skill over an MCP tool was that every tool's full JSON schema loads at session start. That is no longer true by default **[docs, mcp.md]**:

> Tool search keeps MCP context usage low by deferring tool definitions until Claude needs them. Only tool names and server instructions load at session start, so adding more MCP servers has minimal impact on your context window. Claude Code doesn't impose a fixed per-server tool cap.

`ENABLE_TOOL_SEARCH` values **[docs]**:

| Value | Behaviour |
|---|---|
| unset | All MCP tools deferred, with two hard fallback exceptions |
| `true` | All deferred |
| `auto` | Loaded upfront while definitions total under 10% of the context window, deferred above |
| `auto:N` | The same with a custom percentage |
| `false` | All loaded upfront |

The numbers the docs give **[docs]**:

| Number | Caps |
|---|---|
| 2 KB each | Tool descriptions and server instructions |
| 10,000 tokens | MCP tool output warning threshold |
| 25,000 tokens | Default max MCP output, raised with `MAX_MCP_OUTPUT_TOKENS` |
| 1,536 characters | The skill listing cap on description plus `when_to_use` |

So per unit of capability, an MCP tool now costs a name at session start and a skill costs up to 1,536 characters. **MCP is the cheaper one.**

Three asymmetries survive. A skill with `disable-model-invocation: true` costs literally nothing. Each MCP server adds a 2 KB instructions block regardless. And skill descriptions are not re-injected after `/compact`, while MCP tools are.

Our engine cap of about twelve is a limit on skill descriptions, and it stands. It was never a limit on MCP tools and should not be read as one.

## Rolling out to an organisation

### Settings precedence, lowest first

Extracted from the source array and its merge loop **[binary]**:

```js
ms=["userSettings","projectSettings","localSettings","flagSettings","policySettings"]
  .describe("Ordered low-to-high priority — later entries override earlier ones.")
```

| # | Layer | Windows path |
|---|---|---|
| 0 | Plugin base | in memory, `setPluginBase()` |
| 1 | User | `%USERPROFILE%\.claude\settings.json` |
| 2 | Project | `.claude\settings.json` |
| 3 | Local, gitignored | `.claude\settings.local.json` |
| 4 | CLI flag | `--settings` |
| 5 | Managed | `C:\Program Files\ClaudeCode\managed-settings.json` |

On macOS the managed directory is `/Library/Application Support/ClaudeCode`, on Linux and WSL `/etc/claude-code`. The Windows path `C:\ProgramData\ClaudeCode` is legacy and is not read.

A plugin's `settings` sits below everything, so it defaults rather than dictates. Managed settings sit above everything, with a carve-out list where a lower layer may still tighten **[binary]**.

### What an admin can push

| Component | Pushable? | Mechanism |
|---|---|---|
| Skills | Yes | `<managed dir>/.claude/skills/<name>/SKILL.md`, or a force-enabled plugin |
| Subagents | Yes | `<managed dir>/.claude/agents/*.md`, highest priority of all agent sources |
| Hooks | Yes | `hooks` in managed settings |
| Output styles | Yes | `<managed dir>/.claude/output-styles`, plus the `outputStyle` key |
| MCP servers | Yes, all or nothing | `managed-mcp.json` |
| Plugins | Enable and block yes. **Install, no** | `extraKnownMarketplaces` + `enabledPlugins` |

### The gap between listed and installed

This is the finding that shapes any rollout plan **[docs, discover-plugins]**:

> As of Claude Code v2.1.195, adding the marketplace doesn't install plugins that come from an external source, on any path that loads plugins.

Managed settings deliver the marketplace and the policy. Each developer still runs `claude plugin install`. Two paths close the gap: the claude.ai admin console's organisation plugin sync, and `CLAUDE_CODE_PLUGIN_SEED_DIR` for containers. Both are read from the docs and neither was tested here.

### MCP's managed file is exclusive

`managed-mcp.json` lives in the same managed directory. Its presence is a lockdown, not a contribution **[binary]**:

```
Cannot add MCP server: enterprise MCP configuration is active and
has exclusive control over MCP servers
```

Also emitted: `--mcp-config` entries ignored with a count, `--strict-mcp-config` refused, agent-frontmatter servers skipped. There is no partial merge. An organisation wanting "our servers plus whatever the developer adds" cannot express it with this file.

The softer tools are `allowedMcpServers` and `deniedMcpServers`, both managed-only, plus `allowManagedMcpServersOnly`. The denylist takes precedence and merges from all sources, so a developer can always deny more. One warning is worth repeating verbatim **[docs, managed-mcp]**: "A `serverName` entry, in either list, is not a security control."

A developer cannot self-approve a project `.mcp.json` from the repository. The resolver skips project and local settings for exactly that reason **[binary]**, so approval must come from the user's own settings or from managed settings.

### One documentation and binary disagreement

The docs describe a managed key `managedMcpServers` for pushing remote servers, marked v2.1.259+. On 2.1.248 the string appears five times in the binary and **all five are in the Claude Desktop organisation-config module**, not the Claude Code settings schema **[binary]**. On this version the only admin MCP mechanisms are `managed-mcp.json` and the allow and deny lists.

### Plugins can run code, three ways

The risk half. A `command` plugin source runs a shell command at install and update time, and re-runs "once per session in the background" **[binary]**. A `headersHelper` runs before each fetch. And `SessionStart` hooks fire at startup, so an enabled plugin executes on every session. The manifest describes `monitors` as "unsandboxed, same trust tier as hooks".

The controls are `disableCommandPluginSources`, `allowManagedHooksOnly`, `disableSideloadFlags`, `strictKnownMarketplaces` and `strictPluginOnlyCustomization`. Note that non-interactive sessions never show the trust dialog, so hooks committed to a repository run in a folder nobody ever trusted **[docs, hooks]**.

### And Anthropic recommends plugins for MCP anyway

From the managed MCP page **[docs]**:

> Claude Code doesn't have a built-in MCP server registry that users can browse and install from. For the approved-catalog pattern, share the approved list and its `claude mcp add` commands somewhere your users will find them, such as an internal wiki, or distribute the servers as plugins through a managed plugin marketplace so users can browse and install them from `/plugin`.

There is no MCP registry. The plugin marketplace is the registry.

## The split, and the Edict case

The line is not "tools versus prose". It is **what needs to run** versus **what needs to be read**.

Put it in an MCP server when answering the question requires computation the model cannot fake. Edict's `edict_list_handlers` is the clean example: it loads the solution through `MSBuildWorkspace` and walks `INamedTypeSymbol` base chains, because a syntactic walk "misses transitive inheritance (a consumer's `class OrderHandler : MyBase` where `MyBase : EdictCommandHandler<...>` would be invisible)" **[Edict, ADR-0044:13]**. Grep cannot see that. Neither can a model reading files.

Put it in a skill when the value is judgement, sequencing or a convention. And note the counter-case in Edict's own set: `edict_lookup_adr` and `edict_describe_glossary_term` do no computation at all. They are embedded-resource lookups, and they are tools only so the documents version-pin into the package rather than rotting in a copy inside the consumer's repository. A legitimate reason, and not a computational one.

### Stopping them drifting apart

Edict's `Edict.AgenticTooling.Architecture.Tests` is the best prior art we have found, and it goes further than the two directions recorded in [findings.md](findings.md).

Skills are embedded resources in the assembly, so a test loads a skill body by reflection and compares it against the real tool registry. No model call, no network, sub-second. Three layers:

**Referential integrity, both ways.** `EveryRegisteredMcpTool_HasAtLeastOneSkillCaller` fails the build on a tool no skill mentions. `EverySkillMcpToolReference_ResolvesToRegisteredTool` regexes `\bedict_[a-z]+(?:_[a-z]+)*\b` out of every skill body and requires each match to exist.

**Load-bearing sentences.** `SkillPrescriptionTests` asserts that instructions survive editing, using a 200-character proximity window:

```csharp
var pattern = $@"(?:\bbefore\b[\s\S]{{0,{CoLocationWindow}}}{Regex.Escape(toolName)})" + ...
```

The failure message states the intent: "must co-locate `edict_list_handlers` with the word `before` so the load-bearing prescription survives." One test even pins **both halves of a contrast**, so an edit cannot silently drop the warning half.

**Identifiers beyond MCP.** Every `EDICT\d{3}` cited in a skill must resolve to a real analyzer descriptor, and every glossary term must resolve in `CONTEXT.md`. The file's own comment gives the principle: "a skill can never ship a dangling pointer an agent would follow into nothing."

The runtime half is just as good. Every Roslyn response carries a top-level `driftStatus` string, so the model cannot act on stale data without the staleness sitting in the same payload.

### What the interlock does not catch

Prose counts. ADR-0044 says "six tools" throughout; the registry has seven. `edict_check_configuration` was added and the documentation never followed. The same error sits in `Edict.Mcp/README.md`, and twice inside one file. `docs/usage/agentic/mcp-tools.md` says "Seven tools ship" at line 3 and "the six MCP tools" at line 63. ADR-0044 line 27 also documents dogfooding that does not exist: there is no `.mcp.json` in the repository and no `edict-*` skill in its `.claude/skills/`.

Referential integrity catches a dangling pointer. It does not catch a stale number or a claim about a file that is absent. Worth knowing before we copy the pattern.

## Edict's rejection of plugins, re-examined

ADR-0044 was revisited on 2 June 2026 and kept the two-`dotnet tool` decision. The revisit already knew a plugin can carry skills and an MCP server together, and line 32 says so. That was never the objection.

| Reason | Kind | Holds at 2.1.248? |
|---|---|---|
| The server needs the .NET SDK and `MSBuildWorkspace`, so a plugin's `.mcp.json` still launches `dotnet edict-mcp` | Fact about .NET | **Yes.** A plugin `mcpServers` entry does not install a NuGet tool |
| A plugin versions by manifest or git SHA, not by NuGet, losing the pin to the consumer's `Edict.*` version | Fact plus a preference | **Substantially.** `dependencies` resolves plugins, not NuGet packages |
| A second distribution channel costs something | Preference | A judgement about Edict's position |

None of the three transfers to this repository. We have no library to pin to and no existing NuGet channel. The ADR leaves the door open at line 38: "A plugin wrapper remains a future option purely for marketplace discoverability, as an additional surface over the NuGet tools rather than a replacement."

One reason has weakened slightly. `binaries` and `${CLAUDE_PLUGIN_ROOT}` mean a plugin can now carry an executable, which narrows reason 1 for a self-contained server. It does not narrow it for Edict, whose server needs the consumer's SDK and solution at runtime regardless of how the binary arrived.

## What we could not verify

- **Whether a `binaries` auto-fetch works on Windows.** The target-triple table has no Windows entry and the mapper returns `undefined`. Read from the binary, not tested end to end.
- **Whether a version constraint is actually enforced when violated.** The extraction and the demotion path were both read from the binary. No runtime test.
- **Why `plugin details` reports `MCP servers (0)`** for manifest-declared servers, inline or by path, while counting those from a plugin-root `.mcp.json`. The behaviour is measured and the cause was not traced. They do load: `mcp list` shows `plugin:<plugin>:<server>` for an inline entry.
- **The claude.ai console organisation plugin sync, and `CLAUDE_CODE_PLUGIN_SEED_DIR`.** Both are named in the docs as the paths that install rather than merely list. Neither was tested, and this is the most consequential untested claim here.
- **Behaviour gated on versions newer than 2.1.248.** `managedMcpServers` at v2.1.259+, http-to-sse fallback at v2.1.265+. Read from the docs, unmeasurable here.
- **Whether Claude Code calls `completion/complete`** during interactive prompt-argument entry. The probe ran non-interactively and the docs are silent.
