# Use Cases

Gameplay scenarios that describe *what the game should do* at the designer level. Each file captures one scenario with a consistent template that agents can trace into events → handlers → systems → components.

> **Authored on demand.** The earlier 17 use cases were stripped alongside the Phase 1 code strip so they wouldn't mislead new work. Use cases are now re-authored one at a time as each Phase 3 slice begins, against the current architecture. The use-case doc *is* the per-slice plan: it is the single source of truth for what is being built right now and is the input to the `use-case-planner` and `implement-use-case` agents. See [`../roadmap/plan.md`](../roadmap/plan.md) for the slice queue and current focus.

## Template

Every use-case file contains:

- **Status** — `planned` (design complete, no code yet) | `partial` (some supporting code exists) | `implemented` (end-to-end in code)
- **Actors** — Player / Mob / System / Administrator
- **Module** — which `Core/Modules/<Feature>/` owns the scenario
- **Description** — one paragraph
- **Preconditions**
- **Postconditions**
- **Main flow** — numbered steps
- **Events fired** — so an agent can find publishers/subscribers
- **Systems / handlers involved** — traceable to the reference catalogs
- **Implementation plan — work packages** — decompose the build into **1–3 independently-executable work packages**, each sized for a limited-context sub-agent: scope, files, dependencies (which package lands first), out-of-scope bounds, and a *testable* exit criterion. Packages depending only on a shared earlier package (not on each other) may run in parallel. The **primary agent runs `architecture-reviewer` (code mode) across the combined diff** once all packages land — sub-agents do not self-review. This keeps each agent run inside a small context window. See [`stat-resource-substrate.md`](stat-resource-substrate.md) for a worked example.
- **Content tooling impact** — required: list every data-file shape, admin command, and `TemplateRegistry` entry the slice introduces or extends. If the slice adds gameplay state, this section must describe how a designer authors and inspects that state in the same PR. Pure-infrastructure slices (no new gameplay state) may state "none" with one sentence of justification. See [`../architecture/checklist.md`](../architecture/checklist.md) INV-18 ("Content-tooling discipline").
- **Cross-cutting surfaces stressed** — required (ground rule 9). Enumerate the cross-cutting infrastructure this slice exercises (commands, output, persistence, event bus, ECS queries, broadcast, time, content templates, configuration, …). For each surface, classify as one of:
  - **Adequate** — existing shape covers what this slice adds; no change needed. State why.
  - **Gap exposed** — this slice would force a hand-rolled pattern, repeat an existing pattern ≥3 times, or otherwise reveal that the existing surface is insufficient. The framework work is **required** before or alongside this slice; resolve before merge.
  - **Acknowledged debt** — gap exists but defer is justified; tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md) with rationale.

  This section is the structural check that prevents repeats of slice 2's command-framework miss. If you can't list every surface honestly, the slice isn't ready.
- **Flows introduced or modified** — required. List every canonical flow in [`../architecture/flows/README.md`](../architecture/flows/README.md) the slice creates, replaces, or extends. The implementation PR must update `flows/README.md` to match — partial drift is a blocker for the architecture-review pass. New flows that aren't yet canonical (one-off scenarios) may be described in the use-case doc itself; if a flow recurs across slices, promote it to `flows/README.md`.
- **Test plan / Verification** — required (INV-25). Name the tests the slice ships, keyed to the rubric in [`../architecture/07-testing.md`](../architecture/07-testing.md): for each new/changed system method, each Main-Flow postcondition that asserts internal **player-invisible** state, each `[Persistent]` shape, and each fail-fast validation — give the tier (system-unit / handler / flow / persistence round-trip / architecture-guard) and what each test asserts. State explicitly what is **not** tested and why — presentation (exact prose), thin command/plumbing, and pure-data components are legitimately skipped per the rubric. The **Postconditions are the coverage contract**: a postcondition asserting invisible state with no matching test is a gap the spec-review gate flags. Pure-doc or trivial slices may state "none" with one sentence of justification. "Ship green" includes `dotnet test` green; the **on-touch ratchet** means any previously-untested system this slice modifies gains tests here too. See [`../architecture/checklist.md`](../architecture/checklist.md) INV-25.

## Lifecycle (trim-on-ship)

A use-case doc is the **single per-slice work artifact while in flight** — it carries the behavior spec *plus* the implementation plan, cross-cutting audit, flows, and reference-catalog diffs that the `use-case-planner`, the spec-review gate, and `implement-use-case` all operate on.

At slice close-out, `sync-roadmap` **trims** it to its durable behavior spec — **Status, Actors, Module, Description, Preconditions, Postconditions, Main flow, Events fired, Design notes, Related**. The in-flight-only sections (Systems/handlers involved, Content tooling impact, Test plan / Verification, Cross-cutting surfaces stressed, Flows introduced or modified, Reference catalog updates, Open questions) are **removed**: that detail is now authoritative in code, the `Hedron.Tests` suite, [`../architecture/flows/README.md`](../architecture/flows/README.md), and the [`../reference/`](../reference/) catalogs. The Postconditions remain the durable coverage contract. A shipped use-case states present truth, not a frozen plan. See [`../documentation-architecture.md`](../documentation-architecture.md) (`INV-D2`).

## Index

| Status | Use case | Slice |
|---|---|---|
| `implemented` | [`persistence-substrate.md`](persistence-substrate.md) | Phase 3 slice 1 |
| `implemented` | [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) | Phase 3 slice 2 |
| `implemented` | [`command-framework.md`](command-framework.md) | Phase 3 slice 3 |
| `implemented` | [`output-framework.md`](output-framework.md) | Phase 3 slice 4 |
| `implemented` | [`command-prefix-matching.md`](command-prefix-matching.md) | Phase 3 (after slice 4) |
| `implemented` | [`account-character-creation.md`](account-character-creation.md) | Phase 3 slice 5 |
| `implemented` | [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) | Phase 3 slice 5a |
| `implemented` | [`persistence-two-level-model.md`](persistence-two-level-model.md) | Phase 3 slice 5b |
| `implemented` | [`items-and-inventory.md`](items-and-inventory.md) | Phase 3 slice 6 |
| `implemented` | [`equipment.md`](equipment.md) | Phase 3 slice 7 |
| `implemented` | [`mobs.md`](mobs.md) | Phase 3 slice 8 |
| `implemented` | [`attributes.md`](attributes.md) | Phase 3 slice 8a |
| `implemented` | [`entity-state-management.md`](entity-state-management.md) | Phase 3 slice 9-a |
| `implemented` | [`time-system.md`](time-system.md) | Phase 3 slice 9-b |
| `implemented` | [`stat-system.md`](stat-system.md) | Phase 3 slice 9-c |
| `implemented` | [`combat.md`](combat.md) | Phase 3 slice 9 |
| `implemented` | [`stat-resource-substrate.md`](stat-resource-substrate.md) | Phase 3 slice 9-d (gameplay-model S1) |
| `implemented` | [`effect-substrate.md`](effect-substrate.md) | Phase 3 slice 9-e (gameplay-model S2) |
| `implemented` | [`death-and-respawn.md`](death-and-respawn.md) | Phase 3 slice 10 |
| `planned` | [`ability-substrate.md`](ability-substrate.md) | Phase 3 slice 11-a (gameplay-model S4) |
| `planned` | [`ability-invocation.md`](ability-invocation.md) | Phase 3 slice 11-b (gameplay-model S4) |
| `planned` | [`resource-regeneration.md`](resource-regeneration.md) | Phase 3 slice 11-c (supporting pool mechanic) |
| `implemented` | [`aspect-foundation.md`](aspect-foundation.md) | Phase 3 slice 11-d (gameplay-model A + F) |
| `deferred` | [`admin-privilege-elevation.md`](admin-privilege-elevation.md) | Future (TBD) — placeholder |
| `planned` | [`area-model.md`](area-model.md) | Area model + room–area membership (bidirectional, aspect affinities, three area modes) |
| `planned` | [`persistence-reform.md`](persistence-reform.md) | Persistence reform (Stages A–C): SQLite backend, EntityService lifecycle, world content de-persistence, context-driven item persistence, spawn slot foundation |
| `planned` | [`prompt-and-output-batching.md`](prompt-and-output-batching.md) | Phase 3 — player prompt + session-scoped output batching framework |
| `planned` | [`testing-harness-and-backfill.md`](testing-harness-and-backfill.md) | Phase 4 — `Hedron.Tests` harness + architecture-guard suite + system backfill (sub-agent work packages) |

> See [`../roadmap/plan.md`](../roadmap/plan.md#slice-queue) for the full slice queue and current focus.

Suggested categories as new slices are authored:

- **Gameplay — combat** (pulse processing, skill vs defense, death and respawn, mob death and loot, group combat, spell casting)
- **Gameplay — movement & world** (entity movement, mob wandering)
- **Gameplay — items & inventory** (equipment swap, potion consumption, container looting, access control)
- **Gameplay — economy & skills** (shop purchase, crafting)
- **Admin / authoring** (area edit, mob edit, content reload — telnet admin commands per the resolved Ticket B in [`../roadmap/plan.md`](../roadmap/plan.md))
- **System** (game-state persistence, content loading)

## Adding a new use case

For a net-new feature or a non-trivial change, **frame it first with the [`architecture-advisor`](../../.claude/skills/architecture-advisor/SKILL.md) skill (`/advise`)** — the interactive principal-architect intake **seeds this doc** with the Description, Module, Design-note seam rationale, and an in-flight `## Architecture brief` (trimmed on ship). The `use-case-planner` then extends that seed into the full template below. For a small, well-understood slice, skip straight to planning.

Use the `implement-use-case` skill (`.claude/skills/implement-use-case/SKILL.md`) or the `/new-use-case` slash command. The skill will:

1. Scaffold the file using the template above.
2. Identify required events, handlers, and systems — cross-checking against the [reference catalogs](../reference/).
3. Surface unresolved design decisions to the author during scaffolding.

Every use case committed to `docs/use-cases/` must be authoritative — if a design question remains open, resolve it (or park it on an explicit roadmap ticket) before merging. Do not leave `TODO` or "to be decided" language in merged use cases.
