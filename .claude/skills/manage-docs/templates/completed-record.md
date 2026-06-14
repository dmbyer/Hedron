<!--
TEMPLATE: Completed-record — docs/roadmap/completed/<slice>.md
The SINGLE historical artifact for a shipped slice: what shipped, the as-built record, and the
design decisions. This is where a deleted implementation plan's rationale lands. It must capture
the slice's decisions BEFORE the plan is deleted (INV-28). Rarely referenced — for archaeology
beyond what the living docs show. Delete these comments on copy.
-->
# <Slice> (completed)

> Implemented on branch `<branch>`, <date>. Living docs: [`<feature>`](../../features/<feature>/<feature>.md).

## Outcome
What shipped, as a narrative — the capability now in the codebase.

## Behavior digest
The durable Pre/Postconditions and a Main-flow summary the plan carried (the original required behavior, for the record). The authoritative present-truth version lives in the feature/system docs and flows; this is the as-specified snapshot.

## Shipped pieces
| Surface | Location |
|---|---|
| `<Type>` — one line | `Core/.../<File>.cs` |

## Decisions
The design rationale — the "why," the rejected alternatives, the non-obvious choices. This absorbs the implementation plan's Design Notes. The durable home for shipped-slice rationale.

## Deviations / Follow-ups
- Deviations from the plan (or "none").
- Follow-ups unlocked / debt parked in [`../backlog.md`](../backlog.md).
