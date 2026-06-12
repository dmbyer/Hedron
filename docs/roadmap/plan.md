# Roadmap

> **Purpose.** Holds the end-goal, the strategic posture, and a pointer to whatever slice is currently in flight. Detail about *completed* work lives in [`done.md`](done.md) and [`completed/`](completed/); detail about *deferred* work lives in [`backlog.md`](backlog.md). Detail about each *upcoming* slice lives in its use-case doc under [`../implementation-plans/`](../implementation-plans/).

## End goal

A production-grade C# MUD engine on .NET 8 with:

- A single live-world ECS, per-component persistence, and an event-driven 4-layer architecture (handlers → domain systems → core systems → components).
- Telnet (and eventually web) clients with the same `ISession` contract.
- Authored content driven by data files plus in-game admin commands; designers iterate without redeploys.
- A vertical-slice delivery cadence where each gameplay scenario in [`../implementation-plans/`](../implementation-plans/) ships behind a use-case spec, an architecture review, and content tooling sufficient to author and exercise the feature.

## Posture

We are **salvaging, not migrating**. The [`../architecture/`](../architecture/) target is authoritative. Existing legacy trees outside the keep list are reference material for *intent* only; their implementations do not survive. Build red is acceptable between named assembly points up to and including the MVP gate; once Phase 3 starts, every merged branch leaves the build green.

The target is defined by:

- [`../architecture/00-overview.md`](../architecture/00-overview.md) through [`../architecture/05-configuration.md`](../architecture/05-configuration.md) — 4-layer model, ECS, events, pitfalls, configuration strategy
- [`../reference/`](../reference/) — catalogs of components, systems, handlers, archetypes
- [`../implementation-plans/`](../implementation-plans/) — designer scenarios, one per gameplay slice

## Phase summary

| Phase | Status | Pointer |
|---|---|---|
| **1 — Strip** | ✅ complete | [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **2 — Foundation / MVP** | ✅ complete | [`completed/phase-2-mvp.md`](completed/phase-2-mvp.md) |
| **3 — Vertical slices** | 🟡 in progress (slices 1–11-d + output-batching + area-model + content-tooling platform done; **next: slice 12 — Shopping**) | per-slice docs in [`../implementation-plans/`](../implementation-plans/); see [Slice queue](#slice-queue) |
| **4 — Hardening** | 🟡 testing complete (`Hedron.Tests` + 566 tests + CI live; Wave 2 backfill done); remaining: perf, thread-safety | CI green; see [`backlog.md`](backlog.md); testing strategy in [`../architecture/07-testing.md`](../architecture/07-testing.md) |

For the per-slice ledger of completed work, read [`done.md`](done.md).

## Current focus

**Phase 3 slice 12 — Shopping.** The area model slice is complete: `RoomComponent.AreaEntityId` is linked at startup by `WorldContentLoader.LinkRoomAreas`; `IAreaSystem` provides `GetRoomsInArea`/`GetAreaForRoom`/`AssignRoomToArea`; `AreaTemplate` now carries optional `AspectAffinities`; `RegistryValidationBootstrap` validates area aspect compositions at boot; `area`/`setarea` admin commands are live; `@dig` inherits the source room's area. The **content-tooling platform** also landed since (advisor-initiated, off the numbered queue): a shared content-definition layer (`IContentDefinitionCatalog`) + callable `IContentValidator` factored from the boot bootstrap, split hosted-service registration, a headless `generate` bulk-generation run-mode, and an in-process Blazor authoring editor (`Hedron.Web`) over all four content kinds — see [`../implementation-plans/content-tooling-platform.md`](../implementation-plans/content-tooling-platform.md). The next *numbered* slice is **Shopping** ([`../implementation-plans/shopping.md`](../implementation-plans/shopping.md) — to be authored) — economy mechanics enabling players to buy/sell items from vendor NPCs. This slice builds on the items + inventory substrate (slice 6) and the mob model (slice 8); no ability or combat dependency.

The per-slice spec is the single source of truth for "what is being built right now" — this file deliberately does not duplicate it.

## Phase 3 ground rules

Each slice runs this loop. There are **two** `architecture-reviewer` gates — one before code exists, one before merge. The spec gate exists because spec-level violations (a plan that directs a layer to break an invariant, or preserves a latent one) are invisible to a code-only reviewer until implementation is already built on the flaw — the failure that produced the slice-3 command-tier rework.

1. Pick the next use-case file from [`../implementation-plans/`](../implementation-plans/), or author a new one.
   - For a net-new feature or a non-trivial change, **frame it first with the `architecture-advisor` skill (`/advise`)** — an interactive principal-architect intake that locates the architectural seams, weighs the feature against existing and planned work ([gameplay-model spines](../design/gameplay-model.md), [feature-horizon](../design/feature-horizon.md), [backlog](backlog.md)), and seeds the use-case doc with a forward-looking architectural brief *before* the planner goes deep. This is the cheapest point to catch a seam-in-the-wrong-place or a missed generalization — the failure class the HP-threshold example surfaced. Skip only for a small, well-understood slice.
2. Plan via the `implementation-planner` agent — extends the advisor's seed (if present) into the component / system / handler / event list and file plan, folds the architectural brief's seam decisions into **Design notes**, and fills the use-case doc's **Cross-cutting surfaces stressed**, **Test plan / Verification** (INV-25), and **Flows introduced or modified** sections.
3. Resolve open questions with the user.
4. **Spec-review gate** — `architecture-reviewer` in **spec mode** against the use-case doc. Blocking findings are fixed *in the doc* before any code is written. The gate also checks the **Test plan** is honest given the Postconditions (a postcondition asserting player-invisible state with no test is a finding — INV-25). Re-run until the verdict is clean.
5. Implement (`implement-plan`) against the corrected spec — **including the tests named in the Test plan**; `dotnet test` must be green (INV-25). A previously-untested system this slice touches gains tests too (on-touch ratchet).
6. **Code-review gate** — `architecture-reviewer` in **code mode** against the diff, before merge. It also confirms the Test-plan tests are present and `dotnet test` is green (INV-25), and greps systems for ambient nondeterminism (INV-26).
7. **Sync roadmap** (`sync-roadmap` skill) — update [`done.md`](done.md), add `completed/<slice>.md`, and advance the slice queue in this file. Run before the PR merges.
8. Ship green — build **and** `dotnet test` green.

Both gates run against [`../architecture/checklist.md`](../architecture/checklist.md) — the single authoritative invariant list. A rule change lands there once; both gates and the planner pick it up.

The testing discipline (INV-25/26, the **Test plan** section, the `dotnet test` gate) is defined in [`../architecture/07-testing.md`](../architecture/07-testing.md). The `Hedron.Tests` harness is live; `dotnet test` is enforced on every PR via CI.

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
| 9-b | Time system (heartbeat) | `IHeartbeatService`, `HeartbeatTickEvent`; prereq for combat, mob AI, effect expiry | ✅ done |
| 9-c | Stat computation system | `IStatSystem` effective-stat pipeline; base + equipment bonus seam for future effects/buffs | ✅ done |
| 9 | Combat | Core gameplay loop | ✅ done |
| 9-d | **Stat & resource substrate** (gameplay-model S1) | Four attributes (Mind/Body/Spirit/Attunement), Mana/Stamina/Astra pools, `ScoreId`/`IStatRegistry` seam — substrate every later spine writes to | ✅ done |
| 9-e | **Effect substrate** (gameplay-model S2) | Effect kinds + lifetime/stacking/phase/Power + `EffectSystem`; bedrock for skills, potions, curses, auras | ✅ done |
| 10 | Death and respawn | Combat is terminal until this exists | ✅ done |
| 11-a | **Ability substrate** (gameplay-model S4) | Unified skill/spell primitive: `AbilityDefinition`/`IAbilitySystem`/`AbilitiesComponent`, multi-pool costs, cooldowns, passive effects via `IEffectContributor` (INV-24) | ✅ done |
| 11-b | **Ability invocation & combat targeting** | Dynamic skill verbs + `cast`, state-aware targeting, offensive-opens-combat, starting abilities at creation | ✅ done |
| 11-c | **Resource regeneration + `rest`** | Out-of-combat pool regen so ability costs recover; independent of 11-a/b | ✅ done |
| output-batching | **Player prompt + output batching** | Status prompt trailing every command + tick; session-scoped buffer; `IPromptSource` port; immediate flush for Chat | ✅ done |
| 11-d | **Aspect & Registry Foundation** (gameplay-model A + F) | `AspectComposition` + `IAspectSystem` (aspect-typed combat damage; affinity + independent per-aspect resistance); generic `IRegistry<TKey,TDef>` registry layer + Ability/Effect/Stat retrofit; fail-fast registry validation; `defs` inspector | ✅ done |
| area-model | **Area model + room–area membership** | Bidirectional area linking, `IAreaSystem`, aspect affinities on areas, `area`/`setarea` admin commands, `@dig` area inheritance | ✅ done |
| content-tooling | **Content-tooling platform** | Shared content-definition layer + callable `IContentValidator`; split hosted-service registration; headless `generate` bulk-generation CLI; in-process Blazor authoring editor (`Hedron.Web`) over all four content kinds. Advisor-initiated (off the numbered queue). | ✅ done |
| 12 | Shopping | Economy | 🟢 next (ready after 6) |
| 13 | Crafting, potions | Content depth | 🟢 ready after 6 |
| 14 | Web/SignalR client (deferred) | Dual-client transport | 🔵 deferred — see [`backlog.md`](backlog.md) |

Order is flexible past slice 5a; some slices can run in parallel branches, and each slice gets a use-case doc *before* implementation starts. (Historical numbering: the original combined command/output draft was split into slices 3 and 4, account creation moved to slice 5 with a +2 downstream shift, and 5a was inserted to give slices 6+ a content-authoring path.)

Slices 9-d, 9-e, and 11 onward implement the gameplay-model spines; see [`../design/gameplay-model.md`](../design/gameplay-model.md) §5 for the full S1–S9 decomposition, dependency order, and per-slice testability.

## Phase 4 — Hardening

Best addressed once a handful of Phase 3 slices have stressed the architecture:

- **Testing — complete.** `Hedron.Tests` harness is live (WP-1 shared helpers, WP-2 architecture-guard suite, WP-3 `IClock` seam). Wave 1 + Wave 2 backfill shipped (566 tests green). The per-slice gate (INV-25/26) and `dotnet test` are enforced on every PR. Strategy: [`../architecture/07-testing.md`](../architecture/07-testing.md). Wave 3 drains via the on-touch ratchet.
- **CI — complete.** `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` on every PR and push to `master`.
- Performance passes where profiling shows real cost
- Thread-safety review once concurrency shape is known (see [`backlog.md`](backlog.md))

Tracked in [`backlog.md`](backlog.md) until promoted into a dated slice.

## Ground rules

Architectural invariants (layering, ECS, events, persistence, …) are the `INV` list in [`../architecture/checklist.md`](../architecture/checklist.md); CLAUDE.md carries their day-to-day summary. This roadmap does not restate them — one rule, one home (see [`../architecture/09-documentation.md`](../architecture/09-documentation.md)).

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
- **How do I plan/implement/review a slice?** → `architecture-advisor`, `new-plan`, `implementation-planner`, `implement-plan`, `architecture-reviewer` under [`../../.claude/`](../../.claude/)
