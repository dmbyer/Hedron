<!--
TEMPLATE: Reference catalog entry — a row in docs/reference/<catalog>.md
The catalog lists WHAT EXISTS (INV-29). Terse. NO inline interface dump — the .cs self-documents,
so link it. Idealized/not-yet-built entries go in the matching *-planned.md companion, clearly labeled.
Delete these comments on copy.
-->
### <Name>

**Purpose:** one line — what it is for.
**Location:** [`<path>.cs`](../../Core/.../<path>.cs).
**Interface:** [`I<Name>.cs`](../../Core/.../I<Name>.cs) — link only; do not paste signatures.
**Key behavior / dependencies:** terse bullets — the contract a reader needs before opening the file (invariants held, what it depends on, where it's registered).
**Status:** implemented. *(Not yet built? Move to `<catalog>-planned.md` and label it design-intent.)*
