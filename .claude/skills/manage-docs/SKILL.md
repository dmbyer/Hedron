---
name: manage-docs
description: Use when creating, updating, moving, or trimming any Hedron documentation — feature docs, per-system design docs, runtime flows/journeys, reference catalogs, implementation plans, or roadmap/completed records. Covers the doc taxonomy (where each fact lives), which template to use, the disintegrate-on-ship plan lifecycle, and the link-don't-duplicate discipline. Invoke whenever a change touches docs/, including as the doc step of a slice.
---

# Manage Docs

The single entry point for documentation work in Hedron. The docs are treated like code: **one fact, one home; one doc, one job; link, don't duplicate; no silent staleness.** This skill carries the *method*; it carries **no copy** of the invariant list — it reads [`docs/architecture/checklist.md`](../../../docs/architecture/checklist.md) (the `INV-D*` series) live and cites by id, and defers the full rationale to [`docs/documentation-architecture.md`](../../../docs/documentation-architecture.md).

## Where each kind of fact lives (pick the bucket first)

| You are documenting… | Home | Template |
|---|---|---|
| A player-facing **capability** (holistic, orchestration-level) | `docs/features/<feature>/<feature>.md` | `templates/feature.md` |
| **How a system works** (the living design) | `docs/features/<feature>/<system>.md` | `templates/system.md` |
| A **runtime hot path / feature journey** | `docs/architecture/flows/<name>.md` | `templates/flow.md` |
| **What exists** (a component/system/handler/command/archetype) | `docs/reference/<catalog>.md` | `templates/reference-entry.md` |
| The **build plan** for a slice (transient) | `docs/implementation-plans/<slug>.md` | `templates/implementation-plan.md` |
| A shipped slice's **history + decisions** | `docs/roadmap/completed/<slice>.md` | `templates/completed-record.md` |
| A **foundational/cross-cutting rule or concept** | `docs/architecture/00`–`08` + the rule in `checklist.md` | — (edit in place) |
| A **forward design model** spanning many slices | `docs/design/<model>.md` | — |

If a fact doesn't fit its bucket, it moves to the bucket that owns it (`INV-D4`). The canonical taxonomy and the feature list are [`docs/features/README.md`](../../../docs/features/README.md); the navigation doc-map is the Related Documents table in [`docs/architecture/00-overview.md`](../../../docs/architecture/00-overview.md). Don't fork either.

## The four discipline rules (every doc)

1. **Link, don't duplicate.** A one-line summary + link is allowed; an authoritative-sounding restatement is not (`INV-D1`). Never restate an invariant outside `checklist.md` — cite `INV-n`. Never reproduce a flow's mermaid outside `flows/`.
2. **Link interfaces; don't dump them.** An implementer will open the `.cs` anyway, and it self-documents. Reference `I<X>.cs` and describe *behavior and invariants* in words — do not paste method signatures into a system doc or a reference row. (This is the main lever for trimming the reference catalogs.)
3. **Catalogs list what exists** (`INV-D3`). Idealized/not-yet-built designs go in the matching `*-planned.md` companion, clearly labeled — never in the implemented catalog.
4. **No silent staleness.** A doc is current, or explicitly marked (trimmed/superseded/archived). When code moves, its docs move in the same change.

## Disintegrate-on-ship (when a slice lands)

An implementation plan is a **transient build artifact**, deleted on ship (`INV-D2`). At close-out (driven by the `sync-roadmap` skill), distribute its content and delete it:

1. **Behavior / orchestration →** the [`features/`](../../../docs/features/) feature doc and its `<system>.md` design docs (create or update).
2. **Runtime path →** the feature's `flows/` journey (create/extend; de-detail to systems+events).
3. **Catalog diffs →** the `reference/` catalogs (`INV-16`).
4. **Decisions, rationale, as-built →** `roadmap/completed/<slice>.md` — the **single historical artifact**. *Verify it captures the slice's design decisions (enrich if missing) BEFORE deleting the plan.*
5. **Delete** the plan from `implementation-plans/`.

There is no retained trimmed spec. Present truth lives in the living docs; history lives in `roadmap/completed/`.

## Moving or renaming docs — link integrity

A restructure's top failure mode is dangling links. Whenever you move/rename/delete a doc:
- repoint every inbound link (search the path segment across `docs/` and `.claude/`);
- after the change, run a link check and resolve every dangling local `.md` target before you're done. A quick checker:

```bash
python - <<'PY'
import os,re,glob
files=[f for r in ['docs','.claude'] for f in glob.glob(r+'/**/*.md',recursive=True)]+['CLAUDE.md']
L=re.compile(r'\]\(([^)#]+)')
for f in files:
    for t in L.findall(open(f,encoding='utf-8').read()):
        t=t.strip()
        if t.startswith(('http','#','mailto')) or '<' in t: continue
        p=os.path.normpath(os.path.join(os.path.dirname(f),t.split('#')[0]))
        if not os.path.exists(p): print(f"{f} -> {t}")
PY
```

## Templates

Copy from [`templates/`](templates/) and delete the guidance comments. Keep docs terse and well-anchored (stable headings to deep-link). The templates encode the discipline rules above — follow their structure so docs stay uniform.

## Keep tooling current (`INV-20`)

If a change alters a pattern a skill or agent teaches, update that `.claude/` file in the same PR. If it alters the doc taxonomy or lifecycle, update [`documentation-architecture.md`](../../../docs/documentation-architecture.md) and this skill — they move together.
