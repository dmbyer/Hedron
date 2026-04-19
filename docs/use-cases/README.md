# Use Cases

Gameplay scenarios that describe *what the game should do* at the designer level. Each file captures one scenario with a consistent template that agents can trace into events → handlers → systems → components.

> **Currently empty.** The earlier 17 use cases were authored against a legacy code surface and were stripped alongside the Phase 1 code strip so they wouldn't mislead new work. Rebuild them one at a time as Phase 3 slices land (see [`../roadmap/plan.md`](../roadmap/plan.md)) — each vertical slice gets its use case re-authored against the current architecture before implementation begins.

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

## Index

_(no use cases currently authored — add via `implement-use-case` skill or `/new-use-case`)_

Suggested categories when rebuilding:

- **Gameplay — combat** (pulse processing, skill vs defense, death and respawn, mob death and loot, group combat, spell casting)
- **Gameplay — movement & world** (entity movement, mob wandering)
- **Gameplay — items & inventory** (equipment swap, potion consumption, container looting, access control)
- **Gameplay — economy & skills** (shop purchase, crafting)
- **Editor (admin)** (area deletion, mob deletion, tied to Ticket B scope)
- **System** (game-state persistence)

## Adding a new use case

Use the `implement-use-case` skill (`.claude/skills/implement-use-case/SKILL.md`) or the `/new-use-case` slash command. The skill will:

1. Scaffold the file using the template above.
2. Identify required events, handlers, and systems — cross-checking against the [reference catalogs](../reference/).
3. Surface unresolved design decisions to the author during scaffolding.

Every use case committed to `docs/use-cases/` must be authoritative — if a design question remains open, resolve it (or park it on an explicit roadmap ticket) before merging. Do not leave `TODO` or "to be decided" language in merged use cases.
