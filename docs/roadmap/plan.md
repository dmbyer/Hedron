# Roadmap

> **Purpose.** Holds the end-goal, the strategic posture, and a pointer to whatever slice is currently in flight. Detail about *completed* work lives in [`done.md`](done.md) and [`completed/`](completed/); detail about *deferred* work lives in [`backlog.md`](backlog.md). Detail about each *upcoming* slice lives in its use-case doc under [`../use-cases/`](../use-cases/).

## End goal

A production-grade C# MUD engine on .NET 8 with:

- A single live-world ECS, per-component persistence, and an event-driven 4-layer architecture (handlers → domain systems → core systems → components).
- Telnet (and eventually web) clients with the same `ISession` contract.
- Authored content driven by data files plus in-game admin commands; designers iterate without redeploys.
- A vertical-slice delivery cadence where each gameplay scenario in [`../use-cases/`](../use-cases/) ships behind a use-case spec, an architecture review, and content tooling sufficient to author and exercise the feature.

## Posture

We are **salvaging, not migrating**. The [`../architecture/`](../architecture/) target is authoritative. Existing legacy trees outside the keep list are reference material for *intent* only; their implementations do not survive. Build red is acceptable between named assembly points up to and including the MVP gate; once Phase 3 starts, every merged branch leaves the build green.

The target is defined by:

- [`../architecture/00-overview.md`](../architecture/00-overview.md) through [`../architecture/05-configuration.md`](../architecture/05-configuration.md) — 4-layer model, ECS, events, pitfalls, configuration strategy
- [`../reference/`](../reference/) — catalogs of components, systems, handlers, archetypes
- [`../use-cases/`](../use-cases/) — designer scenarios, one per gameplay slice

## Phase summary

| Phase | Status | Pointer |
|---|---|---|
| **1 — Strip** | ✅ complete | [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **2 — Foundation / MVP** | ✅ complete | [`completed/phase-2-mvp.md`](completed/phase-2-mvp.md) |
| **3 — Vertical slices** | 🟡 in progress (slice 1 done; **next: slice 2 — world content loading + admin substrate**) | per-slice docs in [`../use-cases/`](../use-cases/); see [Slice queue](#slice-queue) |
| **4 — Hardening** | 🔵 not started | testing, CI, perf, thread-safety review — see [`backlog.md`](backlog.md) |

For the per-slice ledger of completed work, read [`done.md`](done.md).

## Current focus

**Phase 3 slice 2 — world content loading + admin substrate.** Use case to be authored next via the `new-use-case` skill; planning via `use-case-planner`; implementation via `implement-use-case`; review via `architecture-reviewer` before merge.

The per-slice spec is the single source of truth for "what is being built right now" — this file deliberately does not duplicate it.

## Phase 3 ground rules

Each slice:

1. Pick the next use-case file from [`../use-cases/`](../use-cases/), or author a new one (`new-use-case` skill).
2. Plan via the `use-case-planner` agent — produces the component / system / handler / event list and file plan.
3. Implement.
4. Review via the `architecture-reviewer` agent before merge.
5. Update [`done.md`](done.md) and add a `completed/<slice>.md` note when the slice merges.
6. Ship green.

## Slice queue

Order is **revised** from the original Phase 3 list to pull content tooling forward. Rationale: shipping gameplay slices without the tools to author and exercise their content makes every following slice harder to demonstrate and regression-test. Content tooling becomes a first-class concern *now*, not at the end.

| # | Slice | Unlocks | Status |
|---|---|---|---|
| 1 | Persistence substrate | Any slice that wants state to survive restart | ✅ done |
| 2 | **World content loading + admin substrate** | Authored rooms/areas from data files; in-game admin command framework (`@spawn`, `@teleport`, `@dig`, `@reload`); resolves Ticket B | 🟢 next |
| 3 | Account / character creation | Real identity instead of throwaway names; first `[Persistent]` user-facing component | 🟢 ready |
| 4 | Items + inventory + `get`/`drop`/`look <item>` | Object interaction and inspection (originally two slices; merged so the content tooling for items lands once) | 🟢 ready |
| 5 | Equipment + `wear`/`remove` | Gear | 🟢 ready |
| 6 | Mobs + wandering | Populated world; first `TimeSystem` use | 🟢 ready |
| 7 | Combat | Core gameplay loop | 🟡 blocked on 6 |
| 8 | Death and respawn | Combat is terminal until this exists | 🟡 blocked on 7 |
| 9 | Skills | Character progression | 🟢 ready after 7 |
| 10 | Shopping | Economy | 🟢 ready after 4 |
| 11 | Crafting, potions | Content depth | 🟢 ready after 4 |
| 12 | Web/SignalR client (deferred) | Dual-client transport | 🔵 deferred — see [`backlog.md`](backlog.md) |

Order is flexible past slice 3; some slices can run in parallel branches. Each slice gets a use-case doc *before* implementation starts.

## Phase 4 — Hardening

Best addressed once a handful of Phase 3 slices have stressed the architecture:

- Test framework (xUnit) and initial system-level test coverage
- CI wiring (build + tests on PR)
- Performance passes where profiling shows real cost
- Thread-safety review once `TimeSystem` and concurrency shape are real

Tracked in [`backlog.md`](backlog.md) until promoted into a dated slice.

## Ground rules across all phases

1. **Idealized API first.** If code can't match the target on first write, don't write it — fix the doc or defer the feature.
2. **4-layer discipline.** Handlers orchestrate → domain systems decide → core systems compute → components hold data. See [`../architecture/01-layers.md`](../architecture/01-layers.md).
3. **Component queries, not `is`/`as`.** Never.
4. **Past-tense thin events.** Events describe *what happened*. Logic lives in handlers/systems.
5. **One world; authored content spawns from `TemplateRegistry`.** Feature systems own bespoke construction. See [`../architecture/02-ecs.md`](../architecture/02-ecs.md).
6. **Persistence is per-component.** `[Persistent]` on a component type marks it as save-worthy.
7. **Docs drift is a bug.** Docs describe the target; if reality disagrees, one of them is wrong and gets fixed in the same PR.
8. **Content-tooling discipline.** Every slice that adds gameplay state must also land the content tooling needed to author and exercise that state. Concretely:
   - The slice's use-case doc includes a **Content tooling impact** section listing the data-file shape, admin commands, and/or `TemplateRegistry` entries the slice introduces or extends.
   - The slice's PR ships those tooling changes alongside the gameplay code; no gameplay slice merges without a way to populate and inspect the state it adds.
   - If a slice would need content that doesn't yet have a tool to author it, the prerequisite tooling work is split out as its own earlier slice.

## Resolved tickets

- **Ticket A — ECS redesign.** Resolved in Phase 1.5 (see [`completed/phase-1-strip.md`](completed/phase-1-strip.md)).
- **Ticket B — admin tooling / use-cases / skills scope.** Resolved 2026-05. **Disposition: in-game admin commands first; web/desktop UI deferred.** Admin authoring lands as part of Phase 3 slice 2 (`@spawn`, `@teleport`, `@dig`, `@reload`, etc.) rather than as a Phase 3 slice 13 web UI. Rationale: the smallest tool that lets a designer iterate on content is a privileged set of telnet commands; a web/desktop editor is a transport choice that can be layered on once SignalR / dual-client work lands. The `editor-*` use cases (deletion of areas/mobs, etc.) become admin commands authored against the same handler pipeline as player commands.

## Where to look

- **What's done?** → [`done.md`](done.md), then [`completed/`](completed/) for detail
- **What's next?** → "Current focus" above, then the linked use-case doc
- **What's deferred?** → [`backlog.md`](backlog.md)
- **What's the target architecture?** → [`../architecture/`](../architecture/)
- **How do I plan/implement/review a slice?** → `new-use-case`, `use-case-planner`, `implement-use-case`, `architecture-reviewer` under [`../../.claude/`](../../.claude/)
