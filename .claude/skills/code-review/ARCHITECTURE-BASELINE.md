# Arrangement baseline

The Architecture axis's fixed set, in the same shape as the smell baseline: *what it is* → *how to fix*. It applies when a repo documents nothing, and it is overridden wherever the repo does.

Roots: Parnas on information hiding (a module's secret is what its callers must not know), and Martin's package principles — Acyclic Dependencies, and depending in the direction of stability. Where a repo already has design vocabulary of its own, use its words rather than these.

Three rules bind every item, and they are what keep the axis from degenerating into taste:

- **The repo overrides.** A documented rule or a recorded decision wins.
- **Diff-introduced only.** Standing debt is not a finding. Report what this change introduced, or made materially worse.
- **Cite or drop it.** Name the item and quote the line — usually a single import or a single path.

## The nine

- **Wrong-way dependency** — a module now imports from one that ought to depend on *it*: core reaching into infrastructure, a shared utility reaching back into a feature, a lower tier importing a higher one. The most common single-line architecture defect. → invert it behind an interface at the seam, or move the shared thing down to where both sides can see it.
- **New cycle** — two modules now depend on each other, directly or through a chain. Type-only imports count: they vanish at build time but the design coupling is real, and a cycle in the types is a boundary drawn in the wrong place. → move the shared concept into one of the two, or out into a third that both depend on.
- **Boundary bypass** — the diff reaches past a module's public entry point into its internals: a deep import under a package's `index`, an `internal`/`_private` path, a class the module never meant to export. → export it deliberately if it is wanted, or stop reaching for it. Two callers reaching past the same seam means the interface is wrong, not that the callers are.
- **Misplaced file** — a new or moved file whose folder does not predict its contents, or whose imports look nothing like its siblings'. The folder is a claim about what is inside it; this one is false. → move it to the module its dependencies say it belongs to. If no module fits, that is the finding — name the one that should exist.
- **Grab-bag growth** — something landed in `utils`, `common`, `helpers`, `shared`, `core`, or `misc` that has a real home elsewhere. These names describe where code goes when nobody decided, and they accrete forever because nothing is ever obviously out of scope for them. → name the concept and give it a module of its own.
- **Leaked framework** — an ORM entity, HTTP request, DI attribute, React hook, or serialization annotation appearing in a module whose whole value was not knowing about them. Usually arrives as one convenient import and is expensive to reverse later. → keep it behind the adapter; pass the module a plain value.
- **Scattered change** — one logical change touching many modules. Fowler's Shotgun Surgery at module scale: the thing that changes together is not stored together. → say where you would redraw the boundary, not merely that the diff is wide. A change that is wide because it is a rename is not this.
- **Duplicated concept** — the same domain concept modelled independently in two modules: two shapes for the same entity, two encodings of the same identifier, two copies of one rule. Diverges silently, because nothing fails when they disagree. → one owner; the other references it.
- **Undeclared seam** — a new hard dependency on an external system — clock, filesystem, network, queue, random — with no interface between, in a repo that puts a seam in front of such things elsewhere. → follow the pattern the repo already has. Where it has none, this is a judgement call and worth stating as one: a seam with a single implementation is a hypothetical seam.

## Weighting

The first three are usually the real findings, because they are checkable from an import line and they compound: a wrong-way dependency admitted once becomes the precedent for the next. The last three are judgement calls more often than not — say so when reporting them, the way the smell baseline labels its own.

A finding you cannot express as "this import/path, against this rule" is not ready to report.
