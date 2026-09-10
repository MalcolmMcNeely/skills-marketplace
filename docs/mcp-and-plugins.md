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

## Sharing this internally, without publishing anything

The blunt question an organisation asks first: to give our own developers a catalogue, must we put anything on a public surface? No. Nothing requires it, no mechanism could force it, and one official route actively forbids it.

Three routes were proved end to end on this machine with no network. Three headless boots were pointed at `ANTHROPIC_BASE_URL=http://127.0.0.1:1`, so the cost was zero.

### Nothing can be published to Anthropic even deliberately

`claude plugin --help` has no `publish`, `submit` or `login` subcommand **[measured]**. The whole list is `details, disable, enable, eval, help, init, install, list, marketplace, prune, tag, uninstall, update, validate`.

`claude plugin tag` sounds like a release step and is not **[measured]**. It creates a local annotated git tag and optionally pushes it to a named remote. No registry, no external service.

The official marketplace is closed **[docs, plugins.md]**:

> There is no application process, and the submission form does not add plugins to the official marketplace.

A separate community marketplace exists and is opt-in. `plugin validate` runs entirely locally and checks nothing against a remote allowlist.

`claude-plugins-official` registers itself on first interactive launch, and both removal and a policy block work **[measured]**:

```
$ claude plugin marketplace remove claude-plugins-official
✔ Successfully removed marketplace: claude-plugins-official
```

Managed `blockedMarketplaces` accepts `{"source":"github","repo":"anthropics/*"}`, and `strictKnownMarketplaces` takes a `hostPattern`. The example shipped in the binary is literally `"^github\.mycompany\.com$"` **[binary]**.

### The org console route requires a private repo

The one route that can force-install rejects a public repository outright **[docs, support.claude.com, Manage plugins for your organization]**:

> Your repository must be private or internal—public repos aren't allowed for organization marketplaces.

An admin configures it at Organization settings > Plugins on a Team or Enterprise plan, by ZIP upload or by syncing a private repository on github.com or GitHub Enterprise. Four distribution states, and two of them push without asking: "Installed by default", and "Required", which members "cannot be disabled or uninstalled".

Three caveats matter. Auto-sync fires on a pull request merge carrying a version bump and **not** on a direct push. A failed sync "may temporarily remove plugins for your team members". And a plugin with a top-level `bin/` directory is rejected, which collides with the `binaries` manifest key that installs into exactly that folder.

The important limit today **[docs, plugins-reference.md]**:

> Claude Code doesn't load them in sessions you start in your own terminal.

It reaches chat, the Desktop Chat tab, Cowork and cloud sessions. Not `claude` in a terminal.

That looks like it is changing. The binary carries a `syncClaudeAiPlugins` setting absent from the published settings reference, whose own description says synced plugins "load in every session like plugins you installed yourself" **[binary]**. It is gated on a server-side flag and an org policy key, both invisible from the client. Docs and binary disagree, so check the state rather than designing for either.

### Routes proved with no network

**A local directory** **[measured]**:

```
$ claude plugin marketplace add "C:\...\mp"
✔ Successfully added marketplace: privatelab (declared in user settings)
$ claude plugin install hello@privatelab
✔ Successfully installed plugin: hello@privatelab (scope: user)
```

**A local git repository**, cloned from a bare repo with no network **[measured]**. The CLI argument parser rejects `file://`, but declaring the same repo as a `git` source in `extraKnownMarketplaces` works. The gate is the parser, not the clone machinery.

**An internal URL over plain HTTP**, with a bearer token reaching the server **[measured]**:

```
$ claude plugin marketplace add "http://127.0.0.1:8799/marketplace.json"
✔ Successfully added marketplace: urlmp (declared in user settings)
```

Note the trap: a marketplace registers under the `name` in its own manifest, not the key it was declared under. Declaring `authmp` produced `urlmp`. That will bite anyone writing managed settings.

**A seed directory**, and this was the doc's most consequential unverified claim. It is true, and it does more than register.

`CLAUDE_CODE_PLUGIN_SEED_DIR` expects exactly this layout **[binary]**:

```
<SEED_DIR>/known_marketplaces.json
<SEED_DIR>/marketplaces/<name>/     (or <name>.json)
```

One headless boot registered the marketplace pointing at the seed image in place, nothing copied, with `autoUpdate` forced to `false` **[measured]**. Pairing it with `enabledPlugins` then installed the plugin on startup **[measured]**:

```
Installed plugins:
  ❯ hello@seedmp   Version: 0.1.0   Scope: user   Status: ✔ enabled
```

Ship a read-only seed directory in the machine image plus `enabledPlugins` in managed settings, and every developer gets the catalogue installed on first launch with no git, no npm, no HTTP and no network. The seed function runs from `performStartupChecks` and `installPluginsForHeadless` only, never from a `claude plugin` subcommand, which is why the claim looked unverifiable from the CLI.

### What the CLI accepts as a marketplace source

**[measured]**, against the parser:

| Form | Result |
|---|---|
| `git@git.corp.internal:team/catalogue.git` | Accepted, reaches clone |
| `https://git.corp.internal/team/catalogue.git` | Accepted, reaches clone |
| `ssh://git@host:2222/team/repo.git` | Rejected. Needs `extraKnownMarketplaces` |
| `file:///…` | Rejected on the CLI, accepted via settings |

The scp-like regex is `[a-zA-Z0-9._-]+@`, so any username works, not only `git@`. And the parser carries an explicit Azure DevOps case, `o.includes("/_git/")` **[binary]**, so an internal Azure DevOps host is a first-class path.

Two unions exist and the same type name means different things at each level. `archive`, `command` and `git-subdir` are plugin-level only. Marketplace level has `git`, `file`, `settings`, `hostPattern` and `pathPattern`. Plugin-level `url` is a git repository URL; marketplace-level `url` is a `marketplace.json` URL. Only plugin-level `npm` takes a `registry` override.

`archive` refuses anything but https and bans loopback **[binary]**:

```js
Azt="Archive URLs must use https:// and must not point at a loopback, link-local, or cloud-metadata host";
```

So it serves an internal artefact host and never a local file.

### Private HTTPS marketplaces stop auto-updating

This one deserves care because it fails silently.

Every plugin git call runs with prompting disabled **[binary]**:

```js
var Tue={GIT_TERMINAL_PROMPT:"0",GIT_ASKPASS:"",GCM_INTERACTIVE:"never"};
var S$=["-c","core.sshCommand=ssh -o BatchMode=yes -o StrictHostKeyChecking=yes"];
```

The background refresh then goes further and empties the credential helper list, behind a gate that defaults to off **[binary]**:

```js
let y=R("tengu_plugin_autoupdate_allow_credential_helper",!1),
    ... await NH(r,e,void 0,{disableCredentialHelper:!y})
```

With `-c credential.helper=` set alongside an empty `GIT_ASKPASS`, a private HTTPS remote has nothing left to authenticate with. A human running `marketplace add` or `plugin update` is fine. The background pass is not.

**SSH remotes are unaffected**, because they never touch a credential helper. Prefer `git@internal-host:team/catalogue.git` over `https://` for anything meant to auto-update. Proved in the binary, not exercised against a real private host.

Setting `GITHUB_TOKEN` does not help; tokens only take effect through a configured helper.

### What leaks

On a private git, seed or managed-settings route, nothing about the catalogue reaches Anthropic. Telemetry redacts a private plugin's name to a literal string **[binary]**:

```js
plugin_name_redacted: u ? e : pv     // pv = "third-party"
```

Skills report `"custom_skill"`. A stable hash groups events without naming anything, and debug-only `_PROTO_plugin_name` fields are stripped before the sink.

The exception is the org console route, where the plugin contents are uploaded by definition. Anthropic's backend clones, packages and hosts them, and an Enterprise org with scanning enabled has Claude review the contents. A deliberate trade rather than a leak, but it is the one route where the catalogue leaves the company's own infrastructure.

### Which route to take

| Route | Git? | Versioned? | Auto-updates? | Admin can force it? | Reaches the terminal? |
|---|---|---|---|---|---|
| Seed directory + managed `enabledPlugins` | No | Pinned at build | No, by design | Yes, if you own the image | Yes |
| Internal git over SSH | Yes | Yes | Yes | Register only, not install | Yes |
| Internal git over HTTPS | Yes | Yes | **Silently stops** | Register only | Yes |
| Internal `url` endpoint with `headers` | No | Yes | Yes | Register only | Yes |
| Managed settings `.claude/skills/` | No | **No** | No | Yes, strongest | Yes |
| claude.ai org console | Private repo or ZIP | Yes | Yes, on PR merge | **Yes, "Required"** | **Not yet** |

For this repository the honest recommendation is internal git over SSH, with managed `extraKnownMarketplaces` to register it. It keeps the normal git workflow, keeps versioning, and is the only remote form whose background auto-update survives the credential-helper wipe. The gap it leaves is force-install, and the seed directory closes that for any fleet where we control the image.

## The developer experience of staying current

Registration is not installation, and the gap between them is where a catalogue dies. This section measures what actually removes manual work.

### Auto-update works, and it is slower than it sounds

A local git marketplace registered through `extraKnownMarketplaces` with `"autoUpdate": true`, then bumped three times. Three for three, with nobody typing anything **[measured]**:

| Run | Version | Update landed |
|---|---|---|
| 1 | 0.1.0 to 0.2.0 | Yes |
| 2 | 0.2.0 to 0.3.0 | 8 minutes 30 seconds into the session |
| 3 | 0.3.0 to 0.4.0 | About 7 minutes in |

Short sessions never update at all. A three-second and a twenty-two-second run left the clone on the old commit. The reason is a random jitter on an unreferenced timer **[binary]**:

```js
var O=600000;                        // 0 to 10 minutes, mean 5
let k=Math.floor(Math.random()*O);
await ne(k,void 0,{unref:!0}),d=Date.now();
```

So this is a background refresh mid-session, not a startup step. The current session pays the wait and the next one gets the new version.

Our marketplace will not get this by default. The third-party default is a hard-coded name allowlist **[binary]**, and `skills-marketplace` is not on it, so `autoUpdate: true` has to be declared.

### Auto-update never brings a plugin nobody has

The update pass enumerates the installed registry and nothing else **[binary]**. A brand-new plugin added to an already-registered marketplace reaches nobody.

That is not the worst of it. Shipping a new plugin by having an existing one depend on it **breaks the existing one** **[measured]**:

```
❯ orgkit@org-mkt
  Version: 0.4.0
  Status: ✘ failed to load
  Error: Dependency "orgdep@org-mkt" is not installed
```

`claude plugin update` did not fix it, reporting "already at the latest version". Only a fresh install did. So the measured failure mode for a growing catalogue is a **broken** plugin, not a missing one.

`enabledPlugins` alone does not install either. Naming an uninstalled plugin wrote an installation record pointing at a cache directory that was never created **[measured]**.

### The `command` source removes the update step entirely

This is the mechanism worth knowing about. A marketplace entry runs a command that prints a plugin directory, and Claude Code re-resolves it once per session in the background.

Measured: install, then edit the source content, then run a **three-second** session **[measured]**:

| | Version |
|---|---|
| After install | `1.0.0-c1ece0ea1a1b` |
| After edit and a 3s session | `1.0.0-89395c0c0de3` |

No `plugin update`, no prompt. The version is the manifest version plus a content hash.

It also ignores both kill switches, because the re-resolve is awaited **before** the disabled check **[binary]**. `DISABLE_AUTOUPDATER=1` and `DISABLE_UPDATES=1` together still refreshed it **[measured]**. Command-sourced plugins reload in-session too, where a git update needs `/reload-plugins`.

Three costs, and the first is the sting. Consent is a one-time human act that an agent cannot perform **[measured]**:

```
-y/--yes is ignored inside a Claude Code session: run this in your own terminal
to accept the command shown above.
```

Second, changing the command string revokes that consent and every developer silently stops updating until they run an explicit update. Third, `mode: link` is refused on Windows **[measured]**, so the idea of symlinking to a directory the organisation controls is unavailable here.

### The version pin that does not pin

`enabledPlugins` is documented as supporting "extended format with version constraints". It does not hold a version **[measured]**:

| Value | Marketplace at | Result |
|---|---|---|
| `{"orgkit@org-mkt": ["0.4.0"]}` | 0.5.0 | Updated to **0.5.0** |
| `{"orgkit@org-mkt": {"version":"0.4.0"}}` | 0.5.0 | Already at 0.5.0 |

The enabled predicate is `e===!0||Array.isArray(e)` **[binary]**. It reads enabled-or-not and discards the rest.

The real pin lives in the org console. The moment an admin sets `auto_install` or `required`, a 40-character commit SHA becomes mandatory **[binary]**:

> Ref must be a full 40-character commit SHA when Installation is auto_install or required

Rolling the whole organisation forward or back is then one SHA edit. That is the rollback story worth having.

### The only mechanism that pushes a new plugin

The org plugins endpoint, managed key `organizationPluginsUrl`, with a per-marketplace `installationPreference` **[binary]**:

> `available` (the default) lets users install from the Directory's Organization tab. `auto_install` installs automatically once per pinned commit; a plugin the user removes stays removed until the pin changes. `required` reinstalls on every sync, so it cannot stay removed.

Nothing else measured or found installs a brand-new plugin onto an existing developer's machine.

### Day-one cost, per route

**[measured]** unless noted:

| Route | Day-one actions | Keeps current? | Catches new plugins? |
|---|---|---|---|
| Nothing | 2 | No | No |
| `extraKnownMarketplaces` in user or managed settings | 1 | Only with `autoUpdate: true` | No |
| Same, in project settings | 1, plus a folder-trust prompt | Same | No |
| Seed directory + `enabledPlugins` | **0** | No, `autoUpdate` forced false | No |
| `command` source | 1, and it must be a real terminal | **Yes, every session** | No |
| Org console with `required` | **0** | Yes, at the pinned SHA | **Yes** |

A project `.claude/settings.json` declaring `extraKnownMarketplaces` registered nothing until the folder was trusted, and failed silently until then **[measured]**.

## How other organisations solve this

Every company that achieved zero-action delivery did it the same way: they attached to a channel that already ran without being asked. Nobody built a new pipe for skills and got adoption.

**LinkedIn** is the clearest statement of it. Their tool CAPT is a Python package that is also a local MCP server **[LinkedIn engineering blog, 27 January 2026]**:

> Using LinkedIn's internal developer tool distribution, CAPT ships to every laptop and updates silently in the background.
>
> No JSON editing, no config wrangling, no dependency issues. Adoption grew quickly because onboarding required essentially zero effort.

They did not solve distribution. They put the skills inside the distribution they already had. Over 1,000 engineers, 500-plus playbooks.

**Cloudflare** removed the laptop from the problem **[Cloudflare blog, 20 April 2026]**. Markdown compiles to one JSON config served from a Worker, and "Every new session picks up the latest version automatically". Shipping to 3,000-plus people is "just a `wrangler deploy` away". This needs an agent that supports remote config discovery. Claude Code does not, so the pattern is instructive rather than available.

**Zalando** is the only company found publishing the managed-settings route **[Zalando engineering blog, 14 August 2026]**:

> The skill collection is distributed via managed configuration settings or cli command installing the needed symlinks

**DoorDash, Duolingo and Monzo** dissolve the problem by moving execution off the laptop entirely. There is nothing local to update. Out of proportion for a catalogue, but it explains why their write-ups never mention an install step.

**GitLab is the cautionary case.** Five documented distribution channels, and not one explains how an update reaches a developer who already installed. Four are clone-or-symlink. Their product docs say it plainly: "Existing conversations and flows do not have access to new or updated skills automatically."

Two corrections to [findings.md](findings.md) fall out of this. Uber's registry and context-triggered auto-install story is **secondary only**, from conference coverage rather than anything Uber wrote; the headline numbers are primary and the distribution story is not. And OpenAI did not simply keep everything repo-local: they ran an open skills catalogue of around 26,600 stars, deprecated it in June 2026, and folded it into a curated in-product directory. The direction of travel is away from an open browsable repo.

### The finding that outranks distribution

Vercel ran evals against a hardened suite targeting APIs absent from training data **[Vercel blog, 27 January 2026]**:

| Arm | Pass rate |
|---|---|
| Baseline | 53% |
| Skill, default | 53% |
| Skill plus an explicit instruction | 79% |
| Docs index in `AGENTS.md` | 100% |

> In 56% of eval cases, the skill was never invoked.

Winning the distribution argument only gets the file onto the disk. This is evidence for the negative-boundary rule rather than against skills, since their fix was making the *when* unmissable, but it deserves its own ticket and its own measurement here.

### Two things worth checking before building on them

A `SessionStart` hook can return `reloadSkills: true` to re-scan skill directories, per the v2.1.152 changelog. It appears nowhere in the hooks reference, and the issue asking for it to be documented was closed as not planned. That would give a zero-action update path for a self-hosted design, and it should be verified on this machine before anyone relies on it.

Server-managed settings are fetched at startup and refreshed hourly, applied to running sessions without a restart, and they carry `extraKnownMarketplaces` and `enabledPlugins`. Read from the docs, not measured here, and it needs a Team or Enterprise plan with an Owner role.

### What this means for us

Two mechanisms remove real work and they solve different halves.

The `command` source removes the update step completely, at the cost of a consent step no agent can perform and no `mode: link` on Windows. The org console with `required` removes everything including the first install, at the cost of a plan tier and uploading the catalogue to Anthropic.

`autoUpdate: true` on a git marketplace is genuine and cheap, and its limit is the one that bites a catalogue designed to grow: it will never deliver a skill nobody has yet.

The honest recommendation, which corrects the SSH suggestion above: SSH fixes updates and never fixes the first install. Treat the bootstrap as a separate problem and solve it the way LinkedIn did, through whatever already reaches developer machines. For a fleet we control, the seed directory does it for nothing.

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
- **The claude.ai console organisation plugin sync.** Named in the docs as a path that installs rather than merely lists, and it needs a Team or Enterprise plan to exercise. Not tested. `CLAUDE_CODE_PLUGIN_SEED_DIR`, the other such path, is now measured and written up above.
- **A real clone from a private HTTPS or SSH host** with credential helpers. No such host was reachable, so the auto-update credential-helper failure is proved in the binary and not exercised.
- **An `archive` install end to end.** The loopback ban makes it untestable on one machine.
- **A private or scoped npm package against an internal registry,** and whether `.npmrc` is honoured.
- **Behaviour gated on versions newer than 2.1.248.** `managedMcpServers` at v2.1.259+, http-to-sse fallback at v2.1.265+. Read from the docs, unmeasurable here.
- **Whether Claude Code calls `completion/complete`** during interactive prompt-argument entry. The probe ran non-interactively and the docs are silent.
