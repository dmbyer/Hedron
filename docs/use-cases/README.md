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
- **Content tooling impact** — required: list every data-file shape, admin command, and `TemplateRegistry` entry the slice introduces or extends. If the slice adds gameplay state, this section must describe how a designer authors and inspects that state in the same PR. Pure-infrastructure slices (no new gameplay state) may state "none" with one sentence of justification. See [`../roadmap/plan.md`](../roadmap/plan.md) ground-rule 8 ("Content-tooling discipline").

## Index

| Status | Use case | Slice |
|---|---|---|
| `implemented` | [`persistence-substrate.md`](persistence-substrate.md) | Phase 3 slice 1 |
| `implemented` | [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) | Phase 3 slice 2 |
| `deferred` | [`admin-privilege-elevation.md`](admin-privilege-elevation.md) | Future (TBD) — placeholder |

Suggested categories as new slices are authored:

- **Gameplay — combat** (pulse processing, skill vs defense, death and respawn, mob death and loot, group combat, spell casting)
- **Gameplay — movement & world** (entity movement, mob wandering)
- **Gameplay — items & inventory** (equipment swap, potion consumption, container looting, access control)
- **Gameplay — economy & skills** (shop purchase, crafting)
- **Admin / authoring** (area edit, mob edit, content reload — telnet admin commands per the resolved Ticket B in [`../roadmap/plan.md`](../roadmap/plan.md))
- **System** (game-state persistence, content loading)

## Adding a new use case

Use the `implement-use-case` skill (`.claude/skills/implement-use-case/SKILL.md`) or the `/new-use-case` slash command. The skill will:

1. Scaffold the file using the template above.
2. Identify required events, handlers, and systems — cross-checking against the [reference catalogs](../reference/).
3. Surface unresolved design decisions to the author during scaffolding.

Every use case committed to `docs/use-cases/` must be authoritative — if a design question remains open, resolve it (or park it on an explicit roadmap ticket) before merging. Do not leave `TODO` or "to be decided" language in merged use cases.
