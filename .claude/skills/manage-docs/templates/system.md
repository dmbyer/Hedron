<!--
TEMPLATE: System design doc — docs/features/<feature>/<system>.md
The heart of the logic for one system. Living document. Low code, low duplication.
Interface signatures self-document in the .cs — LINK the interface file, don't paste it.
Cite invariants by id (INV-n); never restate them. Delete these comments on copy.
-->
# <System>

> One line: the responsibility this system owns. **Authoring checkpoint:** slice <id>. Living document.

## What it is / does

The single responsibility and the decision this system owns ("computes effective scores," "resolves combat rounds"). Where it sits in the tier model (core-tier vs domain-tier) and why.

## How it works

The model, the seams, the key invariants. The non-obvious mechanics. Cite rules by id (e.g. INV-5, INV-24) — link the checklist, don't restate. Tables and short prose over walls of text.

## Interface

The contract lives in code and self-documents — link it, describe behavior in words:

- [`I<System>.cs`](../../../Core/Modules/<Feature>/Systems/I<System>.cs) — <one line on what the seam exposes>.

Describe *what the methods do and the invariants they hold* (clamps, ordering, purity), not their signatures.

## Considerations

Maintainability notes, gotchas, persistence shape (`[Persistent]`?), determinism seam (`IRandom`/clock per INV-26), performance caveats.

## Extensibility

How this scales to new features — the forward hooks. What a future slice adds *without* changing this system (open/closed), and where the extension point is.

## Related

- Flow: [`<feature>-journey`](../../architecture/flows/<feature>-journey.md)
- Reference rows: [`systems.md`](../../reference/systems.md), [`components.md`](../../reference/components.md)
- Design model / peer systems / `roadmap/completed/` history as relevant.
