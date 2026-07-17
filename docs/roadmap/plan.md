# Roadmap

> **Purpose.** Holds the end-goal, the strategic posture, and a pointer to whatever slice is currently in flight. Detail about *completed* work lives in [`done.md`](done.md) and [`completed/`](completed/); detail about *deferred* work lives in [`backlog.md`](backlog.md). Detail about each *upcoming* slice lives in its use-case doc under [`../implementation-plans/`](../implementation-plans/).

## End goal

A production-grade C# MUD engine on .NET 8 with:

- A single live-world ECS, per-component persistence, and an event-driven 4-layer architecture (handlers → domain systems → core systems → components).
- Telnet (and eventually web) clients with the same `ISession` contract.
- Authored content driven by data files plus in-game admin commands; designers iterate without redeploys.
- A vertical-slice delivery cadence where each gameplay scenario ships behind a use-case spec, an architecture review, and content tooling sufficient to author and exercise the feature.

## Posture

The salvage rebuild is **done** — the engine, tooling, and balance substrate now match the [`../architecture/`](../architecture/) target, and every merged branch leaves the build **and** `dotnet test` green (INV-25, CI-enforced). The strategic pivot: **from building systems to filling them.** The engine has more mechanical depth than it has content; the next phases scale content out to a playable game, then deepen the mechanics that content proves out.

The target remains defined by:

- [`../architecture/00-overview.md`](../architecture/00-overview.md) through [`../architecture/09-documentation.md`](../architecture/09-documentation.md) — 4-layer model, ECS, events, pitfalls, configuration, persistence, testing, web host, docs discipline
- [`../reference/`](../reference/) — catalogs of components, systems, handlers, archetypes
- [`../design/`](../design/) — gameplay model (spines), [feature horizon](../design/feature-horizon.md) (the long-range menu), [power model](../design/power-model.md), [balance catalog](../design/balance.md)

## Phase summary

| Phase | Status | Pointer |
|---|---|---|
| **1 — Strip** | ✅ complete | [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **2 — Foundation / walking-skeleton MVP** | ✅ complete | [`completed/phase-2-mvp.md`](completed/phase-2-mvp.md) |
| **3 — Vertical slices (engine + tooling + progression/balance)** | ✅ complete — ~45 slices from persistence through the Progression & Balance program (`prog-5` closed it 2026-07-17) | ledger: [`done.md`](done.md); per-slice records: [`completed/`](completed/) |
| **4 — Hardening** | ✅ resolved as standing discipline — testing + CI live and enforced per-slice; thread-safety converted to invariant INV-31; perf remains a profiling-triggered backlog item | [`../architecture/07-testing.md`](../architecture/07-testing.md) · [`../architecture/checklist.md`](../architecture/checklist.md) · [`backlog.md`](backlog.md) |
| **5 — Content baseline → functional MVP** | 🟡 **current focus** | [Current focus](#current-focus-phase-5--content-baseline--functional-mvp) below |
| **6 — Full MVP: mechanics + generated content expansion** | 🔵 next | [Phase 6](#phase-6--full-mvp-mechanics--generated-content-expansion) below |
| **7 — Scale out** | 🔵 after full MVP | [`../design/feature-horizon.md`](../design/feature-horizon.md) |

Phase 3's slice queue (the full historical table of every slice and sub-slice) has retired to [`done.md`](done.md) — one row per slice, each linking to its `completed/` record. This file no longer carries it.

## Current focus (Phase 5) — Content baseline → functional MVP

**Goal:** a stranger can log in and *play the game that exists* — create a character, explore a coherent starting region, fight banded mobs, gain progression, earn and spend currency, die and respawn, and feel a difficulty gradient — with every mechanic already built actually exercised by authored content.

This is a **content program, not a systems program.** The tooling to do it is the point of everything Phase 3 built: the Blazor editor + `generate` CLI author it, the power oracle + Tier×Band tags place it, the band-drift audit + conformance fitter keep it in-cell, and the simulator validates the difficulty curve before players feel it. The expected shape of the work:

- **A curated starting region** — areas/rooms with real descriptions, connected geography, and area aspect affinities; mobs and items authored *per (Tier, Band) cell* across the Tier 0–1 range (the [`TargetRange`](../features/progression/power-budget-system.md) query is the design tool); shops, currency loot, starting abilities, and respawn points wired.
- **Content-driven gap-finding.** Authoring at volume will surface small system gaps (a missing `setmob` field, an audit blind spot, a needed room flag). Those land as thin slices through the normal [per-slice loop](#the-per-slice-loop) — the content program is also the shakedown cruise for the tooling.
- **Balance closure on real content.** Run the baseline sim sweeps against the authored content set; fix the known [ascension-baseline calibration gap](backlog.md) when combat tuning happens against real fights; author the first real outcome expectations into the standards document.
- **The functional-MVP acceptance test** (the loop-closer, analogous to Phase 2's): a new player can complete a session — fight, progress visibly, buy/sell, die, recover — without an admin's help and without hitting authored-content edges in the starting region.

Frame the program with `/advise` at kickoff (region scope, tier range, content volume targets, what "done" measures); individual content-tooling gap slices run the normal loop. **Deliberately punted out of this MVP:** crafting/potions (moved to [`backlog.md`](backlog.md) — content depth that doesn't gate the core loop), the web/SignalR client, channels/socials beyond `say`.

## Phase 6 — Full MVP: mechanics + generated content expansion

Once the curated baseline proves the loop, deepen the mechanics the content leans on — each family as its own advisor-framed program, drawing from the seams already shaped for it:

- **Equipment & itemization** — gear variety across all worn slots and cells; item rarity/affixes (Spine D — affixes *are* effects); item flags; loot-worthy drops.
- **Abilities, skills & spells** — expand the unified kit across aspects and tiers (aspect-gated kits, activation modes already in the substrate); starting-kit diversity; trainers/learning as the first non-combat XP sources.
- **Combat depth** — the backlogged action-economy/command-queue and resolution/reactions work (hit/miss, defensive triggers); status/control effects; threat where grouping lands.
- **Progression expansion** — new XP sources (ability use, exploration, objectives) promoting the trigger table at ≥3 sources; the ascension Objective gate + unlock grants; the balance workbench keeping every step honest.
- **Generated content alongside curated** — weighted loot tables (the general `ILootSystem` over the currency-loot seam), randomly generated items via rarity/affix rolls, and instance-based content (generated dungeons/instanced areas — the INV-12 scoped-sub-world design decision) — near-infinite content from finite authoring, all landing in the same Tier×Band cells the oracle already prices.

That constitutes the **full MVP**: a complete, replayable game loop with both curated and generated content. After it, **Phase 7 — scale out**: the [feature horizon](../design/feature-horizon.md) menu (social systems, guilds, housing, PvP, web client, …) prioritized by what the live game shows players want.

## The per-slice loop

Every slice — content-program slices included when they touch systems — runs this loop. There are **two** `architecture-reviewer` gates: one before code exists, one before merge. The spec gate exists because spec-level violations are invisible to a code-only reviewer until implementation is already built on the flaw.

1. Pick or author the use-case file in [`../implementation-plans/`](../implementation-plans/).
   - For a net-new feature or a non-trivial change, **frame it first with the `architecture-advisor` skill (`/advise`)** — the interactive principal-architect intake that locates seams, weighs the feature against existing and planned work ([gameplay-model spines](../design/gameplay-model.md), [feature-horizon](../design/feature-horizon.md), [backlog](backlog.md)), and seeds the doc with an architectural brief. Skip only for a small, well-understood slice.
2. Plan via the `implementation-planner` agent — extends the seed into the component/system/handler/event list and file plan, and fills the doc's **Cross-cutting surfaces stressed**, **Test plan / Verification** (INV-25), and **Flows introduced or modified** sections.
3. Resolve open questions with the user.
4. **Spec-review gate** — `architecture-reviewer` in **spec mode**. Blocking findings are fixed *in the doc* before any code. Re-run until clean.
5. Implement (`implement-plan`) against the corrected spec — including the Test plan's tests; `dotnet test` green (INV-25); the on-touch ratchet covers previously-untested systems.
6. **Code-review gate** — `architecture-reviewer` in **code mode** against the diff, before merge.
7. **Sync roadmap** (`sync-roadmap` skill) — update [`done.md`](done.md), add `completed/<slice>.md`, disintegrate the plan (INV-28), advance this file. Run before the PR merges.
8. Ship green — build **and** `dotnet test` green.

Both gates run against [`../architecture/checklist.md`](../architecture/checklist.md) — the single authoritative invariant list.

## Slice-delivery obligations

Architectural invariants live in [`../architecture/checklist.md`](../architecture/checklist.md); CLAUDE.md carries their day-to-day summary. This roadmap does not restate them — one rule, one home ([`../architecture/09-documentation.md`](../architecture/09-documentation.md)). What this file *owns* are the process rules the checklist enforces but explains here:

- **Content-tooling discipline (INV-18).** Every slice that adds gameplay state ships the tooling to author and exercise it in the same PR; if prerequisite tooling is missing, it splits out as its own earlier slice.
- **Infrastructure-discipline parity (INV-19).** A new player-facing surface, or a hand-rolled pattern repeated ≥3×, lands its framework in the same or an adjacent slice.
- **Balance parity (the [balance catalog](../design/balance.md) maintenance contract).** A slice adding a power source, a tunable knob, or banded content follows the catalog's five rules — new power sources fold in per the [power model](../design/power-model.md); tuning changes re-validate and re-pin.

## Resolved tickets

- **Ticket A — ECS redesign.** Resolved in Phase 1.5 (see [`completed/phase-1-strip.md`](completed/phase-1-strip.md)).
- **Ticket B — admin tooling scope.** Resolved 2026-05: in-game admin commands first; the offline Blazor authoring editor landed later via the content-tooling platform; a live web/desktop editor remains folded into the deferred SignalR/dual-client work.
- **Thread-safety review (Phase 4).** Resolved 2026-07-17: converted from a deferred one-time review into standing invariant **INV-31** (declared concurrency posture at every cross-thread surface, reviewer-enforced) — see [`../architecture/checklist.md`](../architecture/checklist.md) and the threading-model section of [`../architecture/04-pitfalls.md`](../architecture/04-pitfalls.md). One bounded engineering decision remains open in [`backlog.md`](backlog.md): the world-state threading model (guard vs. marshal for ECS component storage).

## Where to look

- **What's done?** → [`done.md`](done.md), then [`completed/`](completed/) for detail
- **What's next?** → [Current focus](#current-focus-phase-5--content-baseline--functional-mvp) above, then the linked use-case doc once framed
- **What's deferred?** → [`backlog.md`](backlog.md)
- **What's the target architecture?** → [`../architecture/`](../architecture/)
- **How do I plan/implement/review a slice?** → `architecture-advisor`, `new-plan`, `implementation-planner`, `implement-plan`, `architecture-reviewer` under [`../../.claude/`](../../.claude/)
