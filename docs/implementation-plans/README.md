# Implementation Plans

**Transient per-slice build artifacts.** An implementation plan captures *what is being built right now* — the behavior spec **plus** the implementation plan, cross-cutting audit, flows, test plan, and reference diffs that the planner, the spec-review gate, and `implement-plan` all operate on. It is the single source of truth for a slice **while in flight**, and it is **deleted on ship** — its durable content moves into the living docs.

> **The durable home is no longer here.** A shipped feature's behavior lives in [`../features/`](../features/) (holistic) and its `<system>.md` design docs; its runtime path in [`../architecture/flows/`](../architecture/flows/); its catalog rows in [`../reference/`](../reference/); its history and design rationale in [`../roadmap/completed/`](../roadmap/completed/). An implementation plan is the scaffolding, not the building. See [`../architecture/09-documentation.md`](../architecture/09-documentation.md) and `INV-28`.

Plans are authored on demand, one per slice, against the current architecture. See [`../roadmap/plan.md`](../roadmap/plan.md) for the slice queue and current focus.

## Template (in-flight)

Every in-flight plan contains:

- **Status** — `planned` (design complete, no code) | `partial` (some supporting code exists) | `implemented` (end-to-end in code — slated for disintegration)
- **Actors** — Player / Mob / System / Administrator
- **Module** — which `Core/Modules/<Feature>/` and [`feature`](../features/) own the scenario
- **Description** — one paragraph
- **Preconditions** / **Postconditions** — the Postconditions are the coverage contract
- **Main flow** — numbered steps
- **Events fired** — so an agent can find publishers/subscribers
- **Systems / handlers involved** — traceable to the [reference catalogs](../reference/)
- **Implementation plan — work packages** — 1–3 independently-executable packages, each sized for a limited-context sub-agent: scope, files, dependencies, out-of-scope bounds, testable exit criterion. The **primary agent runs `architecture-reviewer` (code mode)** across the combined diff once all packages land.
- **Content tooling impact** — required (INV-18): every data-file shape, admin command, and `TemplateRegistry` entry the slice introduces. If the slice adds gameplay state, describe how a designer authors and inspects it in the same PR.
- **Cross-cutting surfaces stressed** — required (INV-19): enumerate cross-cutting infrastructure exercised; classify each **Adequate** / **Gap exposed** / **Acknowledged debt**.
- **Flows introduced or modified** — required (INV-17): list every [`../architecture/flows/`](../architecture/flows/) journey the slice creates/extends. Reference the flow; never reproduce its diagram.
- **Test plan / Verification** — required (INV-25): name the tests per the rubric in [`../architecture/07-testing.md`](../architecture/07-testing.md); state what is not tested and why.

## Lifecycle (disintegrate-on-ship)

A plan moves through three states:

1. **`planned`** — behavior spec + plan drafted; no code.
2. **In-flight** (`planned`/`partial`) — the single per-slice work artifact.
3. **`implemented` → disintegrated** — at slice close-out, `sync-roadmap` **distributes** the plan's durable content and then **deletes the file**:
   - behavior / orchestration → the [`../features/`](../features/) feature doc and its `<system>.md` design docs;
   - runtime path → the feature's [`../architecture/flows/`](../architecture/flows/) journey;
   - catalog diffs → the [`../reference/`](../reference/) catalogs;
   - **decisions, rationale, as-built record → [`../roadmap/completed/<slice>.md`](../roadmap/completed/)** — the single historical artifact. The completed-record is *verified to capture the design decisions before the plan is deleted* (enriched if anything is missing).

   There is no retained trimmed spec. History is `roadmap/completed/`; present truth is the living docs. See [`../architecture/09-documentation.md`](../architecture/09-documentation.md) (`INV-28`) and the `sync-roadmap` skill.

A small quick-fix or minor enhancement that warrants no slice-sized record updates the living docs directly and needs no plan at all.

## Index (in-flight only)

> This index tracks only genuinely **in-flight** work — plans whose `Status` is `planned` / `partial` / `deferred`. Shipped history lives in [`../roadmap/done.md`](../roadmap/done.md) and [`../roadmap/completed/`](../roadmap/completed/).

| Status | Plan | Slice |
|---|---|---|
| `planned` | [`admin-area-authoring.md`](admin-area-authoring.md) | `mkarea` + `listents <type>` |
| `planned` | [`persistence-reform.md`](persistence-reform.md) | Persistence reform (Stages A–C) |
| `planned` | [`progression-and-balance.md`](progression-and-balance.md) | Progression & Balance — program brief (slices 4–5 remain) |
| `deferred` | [`admin-privilege-elevation.md`](admin-privilege-elevation.md) | Future (TBD) — placeholder |

> The legacy `implemented` plans that predated disintegrate-on-ship have all been disintegrated into the living docs ([`../features/`](../features/), [`../architecture/flows/`](../architecture/flows/), [`../reference/`](../reference/)) and removed — see the completed docs-refinement program in [`../roadmap/backlog.md`](../roadmap/backlog.md). This folder now holds only the in-flight plans listed above.

> See [`../roadmap/plan.md`](../roadmap/plan.md#slice-queue) for the full slice queue and current focus.

## Adding a new plan

For a net-new feature or non-trivial change, **frame it first with the [`architecture-advisor`](../../.claude/skills/architecture-advisor/SKILL.md) skill (`/advise`)** — the interactive principal-architect intake seeds the plan with Description, Module, seam rationale, and an in-flight `## Architecture brief`. The planner then extends that seed into the full template above. For a small, well-understood slice, skip straight to planning.

Use the `implement-plan` skill or the `/new-plan` command (see [`.claude/README.md`](../../.claude/README.md) for current tooling names). Every plan committed here must be authoritative — resolve open design questions (or park them on an explicit roadmap ticket) before merging. No `TODO` / "to be decided" language in merged plans.
