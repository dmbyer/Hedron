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
| **3 — Vertical slices** | 🟡 in progress (slices 1–9a done; **next: slices 9-b/9-c — time & stat systems**) | per-slice docs in [`../use-cases/`](../use-cases/); see [Slice queue](#slice-queue) |
| **4 — Hardening** | 🔵 not started | testing, CI, perf, thread-safety review — see [`backlog.md`](backlog.md) |

For the per-slice ledger of completed work, read [`done.md`](done.md).

## Current focus

**Phase 3 slices 9-a / 9-b / 9-c — Combat prerequisites (parallel).** Slice 8a is complete. Before the core combat loop can land, three independent infrastructure slices must ship: entity state management (9-a), the heartbeat time system (9-b), and the stat computation system (9-c). All three can be designed and implemented in parallel; slice 9 (combat proper) is blocked on all three.

| Sub-slice | Spec | Status |
|---|---|---|
| **9-a — Entity state management** | [`../use-cases/entity-state-management.md`](../use-cases/entity-state-management.md) | ✅ done |
| **9-b — Time system (heartbeat)** | [`../use-cases/time-system.md`](../use-cases/time-system.md) | 🟢 planning |
| **9-c — Stat computation system** | [`../use-cases/stat-system.md`](../use-cases/stat-system.md) | 🟢 planning |
| **9 — Combat** | [`../use-cases/combat.md`](../use-cases/combat.md) | 🟡 blocked on 9-a, 9-b, 9-c |

The per-slice spec is the single source of truth for "what is being built right now" — this file deliberately does not duplicate it.

## Phase 3 ground rules

Each slice runs this loop. There are **two** `architecture-reviewer` gates — one before code exists, one before merge. The spec gate exists because spec-level violations (a plan that directs a layer to break an invariant, or preserves a latent one) are invisible to a code-only reviewer until implementation is already built on the flaw — the failure that produced the slice-3 command-tier rework.

1. Pick the next use-case file from [`../use-cases/`](../use-cases/), or author a new one (`new-use-case` skill).
2. Plan via the `use-case-planner` agent — produces the component / system / handler / event list and file plan, and fills the use-case doc's **Cross-cutting surfaces stressed** and **Flows introduced or modified** sections.
3. Resolve open questions with the user.
4. **Spec-review gate** — `architecture-reviewer` in **spec mode** against the use-case doc. Blocking findings are fixed *in the doc* before any code is written. Re-run until the verdict is clean.
5. Implement (`implement-use-case`) against the corrected spec.
6. **Code-review gate** — `architecture-reviewer` in **code mode** against the diff, before merge.
7. **Sync roadmap** (`sync-roadmap` skill) — update [`done.md`](done.md), add `completed/<slice>.md`, and advance the slice queue in this file. Run before the PR merges.
8. Ship green.

Both gates run against [`../architecture/checklist.md`](../architecture/checklist.md) — the single authoritative invariant list. A rule change lands there once; both gates and the planner pick it up.

## Slice queue

Order is **revised** from the original Phase 3 list to pull content tooling forward. Rationale: shipping gameplay slices without the tools to author and exercise their content makes every following slice harder to demonstrate and regression-test. Content tooling becomes a first-class concern *now*, not at the end.

| # | Slice | Unlocks | Status |
|---|---|---|---|
| 1 | Persistence substrate | Any slice that wants state to survive restart | ✅ done |
| 2 | World content loading + admin substrate | Authored rooms/areas from data files; in-game admin command framework (`@spawn`, `@teleport`, `@dig`, `@reload`); resolves Ticket B | ✅ done |
| 3 | **Command framework** | Typed `CommandContext`, declarative arg parsing, structural privilege gate, `help`/`commands`, `CommandExecutedEvent`; ships the minimal output seam | ✅ done |
| 3a | **Command prefix matching** | Dynamic prefix resolution (`lo`→`look`), `MatchingMode` per command, `IVerbRegistry`, alias surfacing in `help`/`commands`, `IArgumentResolver` interface + parser wiring | ✅ done |
| 4 | **Output framework** | Full `IOutputMessage` catalog, `IOutputFormatter`/telnet ANSI, `SupportsColor`, formatter-backed writer, broadcast audience-filter + system-wide; discharges slice-3 output debt | ✅ done |
| 5 | Account / character creation | Real identity instead of throwaway names; first `[Persistent]` user-facing component | ✅ done |
| 5a | **Bare-bones content spawning** | Ad-hoc `dig`/`set` admin commands backed by `IRoomBuilderSystem`; `RoomComponent` `[Persistent]`; unblocks runtime content authoring for slices 6+ | ✅ done |
| 5b | **Persistence two-level model** | `PersistentEntity` marker component; area-scoped periodic flush; save-on-change for admin/lifecycle transitions; `PersistenceHandler` deleted; dirty-set model removed | ✅ done |
| 6 | Items + inventory + `get`/`drop`/`look <item>` | Object interaction and inspection; `ItemDataComponent`, `InventoryComponent`; admin `mkitem`/`setitem`; concrete `IArgumentResolver` impls | ✅ done |
| 7 | Equipment + `wear`/`remove` | Gear; `EquipmentComponent`, `WornSlot` enum, `wear`/`remove`/`equipment` commands | ✅ done |
| 8 | Mobs (basic entity model and spawn) | Populated world; no wandering | ✅ done |
| 8a | Attributes and vitals (`AttributesComponent`, `PoolsComponent`, `score`) | HP + base stats required for combat | ✅ done |
| 9-a | Entity state management | Centralized entity state flags; command gating; prereq for combat and future states (resting, incapacitation, …) | ✅ done |
| 9-b | Time system (heartbeat) | `IHeartbeatService`, `HeartbeatTickEvent`; prereq for combat, mob AI, effect expiry | 🟢 next |
| 9-c | Stat computation system | `IStatSystem` effective-stat pipeline; base + equipment bonus seam for future effects/buffs | 🟢 next |
| 9 | Combat | Core gameplay loop | 🟡 blocked on 9-a, 9-b, 9-c |
| 10 | Death and respawn | Combat is terminal until this exists | 🟡 blocked on 9 |
| 11 | Skills | Character progression | 🟢 ready after 9 |
| 12 | Shopping | Economy | 🟢 ready after 6 |
| 13 | Crafting, potions | Content depth | 🟢 ready after 6 |
| 14 | Web/SignalR client (deferred) | Dual-client transport | 🔵 deferred — see [`backlog.md`](backlog.md) |

Order is flexible past slice 5a; some slices can run in parallel branches, and each slice gets a use-case doc *before* implementation starts. (Historical numbering: the original combined command/output draft was split into slices 3 and 4, account creation moved to slice 5 with a +2 downstream shift, and 5a was inserted to give slices 6+ a content-authoring path.)

## Phase 4 — Hardening

Best addressed once a handful of Phase 3 slices have stressed the architecture:

- Test framework (xUnit) and initial system-level test coverage
- CI wiring (build + tests on PR)
- Performance passes where profiling shows real cost
- Thread-safety review once `TimeSystem` and concurrency shape are real

Tracked in [`backlog.md`](backlog.md) until promoted into a dated slice.

## Ground rules

Architectural invariants (layering, ECS, events, persistence, …) are the `INV` list in [`../architecture/checklist.md`](../architecture/checklist.md); CLAUDE.md carries their day-to-day summary. This roadmap does not restate them — one rule, one home (see [`../documentation-architecture.md`](../documentation-architecture.md)).

What this roadmap *owns* are the **slice-delivery obligations** — process rules the checklist enforces but explains here:

- **Content-tooling discipline (INV-18).** Every slice that adds gameplay state ships the tooling to author and exercise it in the same PR:
   - the use-case doc's **Content tooling impact** section lists the data-file shape, admin commands, and/or `TemplateRegistry` entries introduced or extended;
   - no gameplay slice merges without a way to populate and inspect the state it adds;
   - if a slice needs content with no authoring tool yet, the prerequisite tooling is split out as its own earlier slice.
- **Infrastructure-discipline parity (INV-19).** A new player-facing surface, or a hand-rolled pattern repeated ≥3×, lands its framework in the same or an adjacent slice; the use-case **Cross-cutting surfaces stressed** section is the structural check.

The per-slice delivery loop is the [Phase 3 ground rules](#phase-3-ground-rules) above.

## Resolved tickets

- **Ticket A — ECS redesign.** Resolved in Phase 1.5 (see [`completed/phase-1-strip.md`](completed/phase-1-strip.md)).
- **Ticket B — admin tooling / use-cases / skills scope.** Resolved 2026-05. **Disposition: in-game admin commands first; web/desktop UI deferred.** Admin authoring lands as part of Phase 3 slice 2 (`@spawn`, `@teleport`, `@dig`, `@reload`, etc.) rather than as a Phase 3 slice 13 web UI. Rationale: the smallest tool that lets a designer iterate on content is a privileged set of telnet commands; a web/desktop editor is a transport choice that can be layered on once SignalR / dual-client work lands. The `editor-*` use cases (deletion of areas/mobs, etc.) become admin commands authored against the same handler pipeline as player commands.

## Where to look

- **What's done?** → [`done.md`](done.md), then [`completed/`](completed/) for detail
- **What's next?** → "Current focus" above, then the linked use-case doc
- **What's deferred?** → [`backlog.md`](backlog.md)
- **What's the target architecture?** → [`../architecture/`](../architecture/)
- **How do I plan/implement/review a slice?** → `new-use-case`, `use-case-planner`, `implement-use-case`, `architecture-reviewer` under [`../../.claude/`](../../.claude/)
