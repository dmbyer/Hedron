---
name: sync-roadmap
description: Use after a slice merges (or before a PR is created) to keep plan.md, done.md, and completed/ in sync with the implemented state, and to disintegrate the slice's implementation plan into the living docs. Run on any PR that completes a slice.
---

# Sync Roadmap Docs

Run this skill whenever a slice is complete — either as the final step of `implement-plan` or standalone on any PR that closes out an implementation plan. It does two jobs: advance the roadmap ledger, and **disintegrate the plan on ship** (`INV-28`). For the doc-placement rules and templates it relies on, see the [`manage-docs`](../manage-docs/SKILL.md) skill.

## What to update

### 1. `docs/roadmap/plan.md`

`plan.md` no longer carries a per-slice queue — completed-slice detail lives in `done.md`/`completed/` only (the Phase 3 historical table retired there at the Phase-5 pivot).

**Phase summary table** (near the top):
- Update the active phase's `Status` cell if the slice materially advances it (e.g. a program completing).

**Current focus section**:
- If the completed slice changes what is "next," update the current-focus prose to point at the next body of work (name its implementation plan once framed, what it unlocks, prerequisites). Keep it at strategy altitude — the per-slice spec is the single source of "what is being built right now."

### 2. `docs/roadmap/done.md`

Add one row per completed slice (at the bottom of the table). The plan is deleted on ship, so the row points at the living docs, not a spec file:

```
| **Phase 3 slice N — <Name>** | One-sentence outcome covering key surfaces shipped. | [`completed/<slug>.md`](completed/<slug>.md) · feature: [`features/<feature>/<feature>.md`](../features/<feature>/<feature>.md) |
```

Keep it to one sentence — detail belongs in the `completed/` file.

### 3. `docs/roadmap/completed/<slug>.md` — the single historical artifact

Create it from `templates/completed-record.md` (in the [`manage-docs`](../manage-docs/templates/completed-record.md) skill). This file is where the plan's history and decisions live after the plan is deleted, so it must be **complete before deletion**. Required sections:

| Section | Content |
|---|---|
| Header + preamble | Branch/PR, date, link to the live [`features/`](../../../docs/features/) doc (NOT to the deleted plan) |
| **Outcome** | 2–3 sentence plain-English summary of what shipped |
| **Behavior digest** | The plan's durable Pre/Postconditions + Main-flow summary (the as-specified snapshot) |
| **Shipped pieces** | Table of every new/changed surface with its location |
| **Tests shipped** | Tests added (tier + target) per the plan's Test plan; note on-touch backfill; confirm `dotnet test` green (INV-25) |
| **Decisions** | Non-obvious design rationale — absorbs the plan's Design Notes; deferred items named. The durable home for shipped-slice "why" |
| **Deviations / Follow-ups** | Delta between plan and as-built (`None` if clean); what later slices can build on; debt parked in `backlog.md` |

Pull the content from the implementation plan (before deleting it) and the PR diff (`git show <hash> --stat`).

### 4. Disintegrate the plan, then delete it (`INV-28`)

The implementation plan is transient. Distribute its durable content into the living docs, then remove the file — there is **no trimmed spec left behind**:

1. **Behavior / orchestration →** the [`features/<feature>/`](../../../docs/features/) feature doc and its `<system>.md` design docs (create or update via the `manage-docs` templates).
2. **Runtime path →** the feature's [`flows/`](../../../docs/architecture/flows/) journey (create/extend; keep it at systems+events granularity).
3. **Catalog diffs →** the [`reference/`](../../../docs/reference/) catalogs (`INV-16`; trim interface dumps to links).
4. **Decisions / as-built →** the `completed/<slug>.md` record above — **verify it captures the slice's decisions before deleting** (enrich if anything is missing).
5. **Delete** `docs/implementation-plans/<slug>.md` (`git rm`). Repoint any inbound links to the new `features/` / `flows/` homes, and run the link check (see `manage-docs`).

A small quick-fix that warranted no plan simply updates the living docs directly — no completed-record needed.

## Checklist

- [ ] `plan.md` phase summary / current focus updated if the slice advances them
- [ ] `done.md` row added (points at `completed/` + the live feature doc)
- [ ] `completed/<slug>.md` created from the template, **including Behavior digest, Tests shipped, and Decisions**
- [ ] `dotnet test` green and the plan's Test-plan tests are present (INV-25) — "ship green" = build green **and** tests green
- [ ] Plan content distributed to `features/` / `flows/` / `reference/`; decisions verified in `completed/`; **plan deleted** (`INV-28`); inbound links repointed; link check clean

## When in doubt

- Don't guess at shipped pieces — read the PR diff or the plan's postconditions before deleting it.
- If a slice was split or renamed mid-implementation, record the *as-shipped* state, not the original plan. Note the delta in "Deviations".
- Reference-catalog and flow updates are part of the disintegration (step 4 of section 4) — they ship in the same PR, driven by the `manage-docs` rules.
