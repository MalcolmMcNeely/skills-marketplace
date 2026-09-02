# Skills marketplace

One catalogue of agent skills, shared across every team, installed by policy and versioned in git.

Today each team writes its own skills. The copies drift, nobody knows which version is current, and five teams solve the same problem five times. This repo holds the shared skills in one place and defines one route from a merge to a developer's laptop.

## Docs

- [What we should build, and how](docs/recommendation.md). The plan. Start here.
- [Findings](docs/findings.md). What the research turned up and what we verified.
- [Evals](docs/evals.md). How to verify a skill change made things better, and where "eval" is the wrong word.
- [Targeting](docs/skill-targeting.md). Measured: what fires when a task spans two technologies.
- [MCP skill delivery](docs/mcp-skill-delivery.md). Measured: can an MCP server install a skill, and should it.
- [Output styles](docs/output-styles.md). How to set one voice across the company, and what it costs.

## Two skill locations, and they are not the same thing

This trips people up.

| Path | What it is | Ships to anyone? |
|---|---|---|
| `.claude/skills/` | Our dev tools. What Claude uses while we work in this repo | No |
| `plugins/*/skills/` | The catalogue. What developers across the company install | Yes |

`.claude/skills/` holds 26 vendored skills so we can use `/grill-with-docs`, `/unslop` and the rest while building. Borrowed toolkit, not the product.

`plugins/core/skills/` is empty. We have not written a shipped skill yet. That is the work.

## Layout

```
skills-marketplace/
  .claude/skills/           dev tools, borrowed. Not shipped
  .claude-plugin/
    marketplace.json        catalogue of plugins
  docs/                     the plan and the findings
  plugins/
    core/
      .claude-plugin/plugin.json
      skills/               empty. This is the work
```

## Use it locally

Claude Code reads `.claude/skills/` when you open this repo. Nothing to install.

To test the catalogue as a developer would receive it:

```
/plugin marketplace add C:/Projects/skills-marketplace
/plugin install core@skills-marketplace
```

Validate before you commit:

```
claude plugin validate .
claude plugin validate ./plugins/core
```

## Where the dev skills came from

Vendored from [mattpocock/skills](https://github.com/mattpocock/skills) by way of `C:/Projects/podium`, which flattened the upstream bucket folders and dropped the `agents/openai.yaml` files. This is our copy, not a mirror. Edit it freely.

`unslop` came from [cursor/plugins](https://github.com/cursor/plugins), with its description rewritten so Claude Code can match it as a trigger.

`code-review` and `setup-matt-pocock-skills` read `docs/agents/*.md` from the repo root. Those are per-repo config that `/setup-matt-pocock-skills` writes. Run it here if you need them.

## Licence

MIT. See [LICENSE](LICENSE).

Both upstream sources are MIT too, so nothing constrains us. Their copyright
notices are kept in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
