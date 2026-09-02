---
name: code-review
description: Review the changes since a fixed point (commit, branch, tag, or merge-base) along three axes — Standards (does the code follow this repo's documented coding standards?), Spec (does the code match what the originating issue/spec asked for?) and Architecture (is it in the right module, pointing the right way?). Runs the reviews in parallel sub-agents and reports them side by side. Use when the user wants to review a branch, a PR, work-in-progress changes, or asks to "review since X".
---

Three-axis review of the diff between `HEAD` and a fixed point the user supplies:

- **Standards** — does the code conform to this repo's documented coding standards?
- **Spec** — does the code faithfully implement the originating issue / spec?
- **Architecture** — is the code in the right module, and do its dependencies point the way this repo says they should?

Each axis runs as a **parallel sub-agent** so they don't pollute each other's context, then this skill aggregates their findings.

The issue tracker should have been provided to you. If `docs/agents/issue-tracker.md` is missing, tell the user to run `/setup-matt-pocock-skills`.

## Process

### 1. Pin the fixed point

Whatever the user said is the fixed point — a commit SHA, branch name, tag, `main`, `HEAD~5`, etc. If they didn't specify one, ask for it.

Capture the diff command once: `git diff <fixed-point>...HEAD` (three-dot, so the comparison is against the merge-base). Also note the list of commits via `git log <fixed-point>..HEAD --oneline`.

Before going further, confirm the fixed point resolves (`git rev-parse <fixed-point>`) and the diff is non-empty. A bad ref or empty diff should fail here — not inside three parallel sub-agents.

Also capture `git diff <fixed-point>...HEAD --stat -M`, which the Architecture axis needs: `-M` is what turns a rename into a rename rather than a delete plus an add.

### 2. Identify the spec source

Look for the originating spec, in this order:

1. Issue references in the commit messages (`#123`, `Closes #45`, GitLab `!67`, etc.) — fetch via the workflow in `docs/agents/issue-tracker.md`.
2. A path the user passed as an argument.
3. A spec file under `docs/`, `specs/`, or `.scratch/` matching the branch name or feature.
4. If nothing is found, ask the user where the spec is. If they say there isn't one, the **Spec** sub-agent will skip and report "no spec available".

### 3. Identify the standards sources

Anything in the repo that documents how code should be written, such as `CODING_STANDARDS.md` or `CONTRIBUTING.md`.

On top of whatever the repo documents, the Standards axis always carries the **smell baseline** below — a fixed set of Fowler code smells (_Refactoring_, ch.3) that applies even when a repo documents nothing. Two rules bind it:

- **The repo overrides.** A documented repo standard always wins; where it endorses something the baseline would flag, suppress the smell.
- **Always a judgement call.** Each smell is a labelled heuristic ("possible Feature Envy"), never a hard violation — and, like any standard here, skip anything tooling already enforces.

Each smell reads *what it is* → *how to fix*; match it against the diff:

- **Mysterious Name** — a function, variable, or type whose name doesn't reveal what it does or holds. → rename it; if no honest name comes, the design's murky.
- **Duplicated Code** — the same logic shape appears in more than one hunk or file in the change. → extract the shared shape, call it from both.
- **Feature Envy** — a method that reaches into another object's data more than its own. → move the method onto the data it envies.
- **Data Clumps** — the same few fields or params keep travelling together (a type wanting to be born). → bundle them into one type, pass that.
- **Primitive Obsession** — a primitive or string standing in for a domain concept that deserves its own type. → give the concept its own small type.
- **Repeated Switches** — the same `switch`/`if`-cascade on the same type recurs across the change. → replace with polymorphism, or one map both sites share.
- **Shotgun Surgery** — one logical change forces scattered edits across many files in the diff. → gather what changes together into one module.
- **Divergent Change** — one file or module is edited for several unrelated reasons. → split so each module changes for one reason.
- **Speculative Generality** — abstraction, parameters, or hooks added for needs the spec doesn't have. → delete it; inline back until a real need shows.
- **Message Chains** — long `a.b().c().d()` navigation the caller shouldn't depend on. → hide the walk behind one method on the first object.
- **Middle Man** — a class or function that mostly just delegates onward. → cut it, call the real target direct.
- **Refused Bequest** — a subclass or implementer that ignores or overrides most of what it inherits. → drop the inheritance, use composition.

### 4. Identify the architecture sources

Two kinds, and the second outranks the first.

**Documented** — anything in the repo that says how the code is *arranged*, as opposed to how it is written: `ARCHITECTURE.md`, `CONTEXT.md`, a `docs/adr/` or `decisions/` folder, a README section on layout. Decisions are often recorded somewhere a template wouldn't predict — if the repo has a doc telling agents where its decisions live, read that first and follow it.

**Executable** — a boundary rule the repo can actually run. This is the higher-trust source, because it is enforced rather than aspired to:

| Look for | Ecosystem |
|---|---|
| `.dependency-cruiser.*`, `eslint-plugin-boundaries`, `import/no-restricted-paths`, Nx `tags` | JS / TS |
| `ProjectReference` graph across `*.csproj`, `.editorconfig` layering rules | .NET |
| `importlinter` / `.importlinter`, `tach.toml` | Python |
| `internal/` directories, `go.mod` boundaries | Go |
| `module-info.java`, ArchUnit tests, Maven module graph | Java |

**If an executable rule exists, run it rather than reason about it** — `npm run lint`, `depcruise`, `lint-imports`, `dotnet build`, whatever the repo wires it to. A violation it reports is a fact, not a judgement call, and belongs at the top of the axis. Say in the report which command you ran; if none exists, say that too, because "this repo cannot check its own boundaries" is itself the finding a reader wants.

On top of whatever the repo has, the Architecture axis always carries the **arrangement baseline** in [`ARCHITECTURE-BASELINE.md`](ARCHITECTURE-BASELINE.md) — nine failures of placement and direction that apply even when a repo documents nothing, in the same *what it is* → *how to fix* shape as the smell baseline. It sits in its own file because only the sub-agent needs it; pass the path, don't paste the contents.

Three rules bind the axis, and the third is the one that decides whether anyone keeps reading its reports:

- **The repo overrides.** A documented rule or a recorded decision wins. Don't re-litigate an ADR — if the diff contradicts one, that is the finding; if the *ADR* looks wrong, say so once and move on.
- **Diff-introduced only.** Standing debt is not a finding. Report what this change introduced, or made materially worse. An axis that re-reports the same architecture every run gets skimmed and then skipped.
- **Cite or drop it.** Every finding names either the doc/rule it breaches or the baseline item, and quotes the line — usually a single import. Architecture judgement without evidence is just taste, and it is the failure this axis is most prone to.

**Skip the axis** when the diff sits inside one module and touches no config, no dependency manifest, and no file moves — there is no arrangement question to answer. Note the skip in the report.

### 5. Spawn the sub-agents in parallel

**Standards sub-agent prompt** — include:

- The full diff command and commit list.
- The list of standards-source files you found in step 3, **plus the smell baseline from step 3** pasted in full — the sub-agent has no other access to it.
- The brief: "Report — per file/hunk where relevant — (a) every place the diff violates a documented standard: cite the standard (file + the rule); and (b) any baseline smell you spot: name it and quote the hunk. Distinguish hard violations from judgement calls — documented-standard breaches can be hard, but baseline smells are always judgement calls, and a documented repo standard overrides the baseline. Skip anything tooling enforces. Under 400 words."

**Spec sub-agent prompt** — include:

- The diff command and commit list.
- The path or fetched contents of the spec.
- The brief: "Report: (a) requirements the spec asked for that are missing or partial; (b) behaviour in the diff that wasn't asked for (scope creep); (c) requirements that look implemented but where the implementation looks wrong. Quote the spec line for each finding. Under 400 words."

If the spec is missing, skip the Spec sub-agent and note this in the final report.

**Architecture sub-agent prompt** — include:

- The diff command, the `--stat -M` command, and the commit list.
- The architecture sources from step 4: the documented files by path, the executable rule and the exact command that runs it, and the path to `ARCHITECTURE-BASELINE.md` — the sub-agent reads that itself.
- The three binding rules from step 4, verbatim.
- The brief: "First run the repo's own boundary check if there is one, and report what it says. Then, for **every module the diff touches**: (a) does anything the diff added point the wrong way, cross a seam it shouldn't, or reach past a module's public entry point; (b) is every added or moved file in the module its dependencies say it belongs to; (c) does the change introduce a cycle. Cite the rule or name the baseline item for each finding, and quote the import or path it turns on. Report only what this diff introduced or worsened — standing debt is out of scope. Under 400 words."

Unlike Standards, this axis needs to read outside the diff: an import line is only wrong relative to the module graph around it. Say so in the prompt, and let it read the tree.

### 6. Aggregate

Present the reports under `## Standards`, `## Spec` and `## Architecture` headings, verbatim or lightly cleaned. Do **not** merge or rerank findings — the axes are deliberately separate (see _Why three axes_).

End with a one-line summary: total findings per axis, and the worst issue _within each axis_ (if any). Don't pick a single winner across axes — that's the reranking the separation exists to prevent.

## Why three axes

A change can pass on one axis and fail on another, so each masks the others when merged:

- Code that follows every standard but implements the wrong thing → **Standards pass, Spec fail.**
- Code that does exactly what the issue asked but breaks the project's conventions → **Spec pass, Standards fail.**
- Code that is well named, well tested, and exactly what the issue asked for, in a module that should never have imported it → **Standards and Spec pass, Architecture fail.**

The third axis is separate from Standards for two further reasons. Its evidence is different — Standards reads the diff, Architecture needs the module graph the diff sits in. And its remedies cost differently: "rename this" and "invert this dependency" do not belong in one ranked list, because the cheap findings crowd out the expensive ones.
