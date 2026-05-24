---
name: sync-roadmap
description: Use after a slice merges (or before a PR is created) to keep plan.md, done.md, and completed/ in sync with the implemented state. Run on any PR that completes a use-case slice.
---

# Sync Roadmap Docs

Run this skill whenever a slice is complete — either as the final step of `implement-use-case` or standalone on any PR that closes out a use-case doc.

## What to update

### 1. `docs/roadmap/plan.md`

**Phase summary table** (near the top):
- Change the Phase 3 row's parenthetical to name the *next* unstarted slice, not the one just completed.

**Slice queue table**:
- Change the completed slice's `Status` cell from `🟢 next` → `✅ done`.
- If the slice was an unplanned insertion (e.g. an enhancement that arrived between two numbered slices), add a new row with a label like `Xa` between the adjacent rows.
- Advance the *next* slice's status to `🟢 next`.

**Current focus section**:
- Replace the description with the next slice. Name the use-case doc, state what the slice unlocks, and note any immediate prerequisites.

### 2. `docs/roadmap/done.md`

Add one row per completed slice (at the bottom of the table):

```
| **Phase 3 slice N — <Name>** | One-sentence outcome covering key surfaces shipped. | [`completed/<slug>.md`](completed/<slug>.md) · spec: [`use-cases/<slug>.md`](../use-cases/<slug>.md) |
```

Keep it to one sentence — detail belongs in the `completed/` file.

### 3. `docs/roadmap/completed/<slug>.md` (new file)

Create a new file following the established format. Use an existing file (e.g. [`slice-2-world-content-and-admin-substrate.md`](../completed/slice-2-world-content-and-admin-substrate.md)) as the template. Required sections:

| Section | Content |
|---|---|
| Header + preamble | PR number, link to the use-case doc |
| **Outcome** | 2–3 sentence plain-English summary of what changed |
| **Shipped pieces** | Table of every new/changed surface with its location |
| **Spec-review provenance** | Architecture-reviewer findings and how they were resolved |
| **Notable design points** | Non-obvious decisions; deferred items explicitly named |
| **Deviations from the use-case doc** | Any delta between spec and as-built; `None` if clean |
| **Follow-ups unlocked** | What the next slices can now build on |

Pull the shipped pieces from:
- The use-case doc's "Systems / Handlers Involved" section
- The PR diff (`git show <hash> --stat` for the file list)
- The use-case doc's "Postconditions" for things that were changed but not new files

### 4. Trim the use-case doc to its durable spec (trim-on-ship)

Confirm `**Status:** implemented` at the top, then **trim** the doc to its durable behavior spec — keep **Status, Actors, Module, Description, Preconditions, Postconditions, Main flow, Events fired, Design notes, Related**; delete the in-flight-only sections (Systems/handlers involved, Content tooling impact, Cross-cutting surfaces stressed, Flows introduced or modified, Reference catalog updates, Open questions). Design notes stay — they hold non-obvious rationale not captured in code. That detail is now authoritative in code, `docs/architecture/flows/README.md`, and the `docs/reference/` catalogs — keeping a second copy in the use-case doc is exactly the drift this prevents. See [`../../../docs/documentation-architecture.md`](../../../docs/documentation-architecture.md) (`INV-D2`).

## Checklist

- [ ] `plan.md` phase summary updated
- [ ] `plan.md` slice queue status changed to ✅ done; next slice advanced to 🟢 next
- [ ] `plan.md` current focus section updated
- [ ] `done.md` row added
- [ ] `completed/<slug>.md` created
- [ ] Use-case doc status is `implemented` **and** trimmed to its durable spec (trim-on-ship, `INV-D2`)

## When in doubt

- Don't guess at shipped pieces — read the PR diff or the use-case doc's postconditions.
- If a slice was split or renamed mid-implementation, record the *as-shipped* state, not the original plan. Note the delta in "Deviations".
- If the slice introduced a new architectural rule or changed a reference catalog (`systems.md`, `handlers.md`, `components.md`), those updates belong in the PR itself, not here — this skill only covers the roadmap ledger.
