# Use Cases

Gameplay scenarios that describe *what the game should do* at the designer level. Each file captures one scenario with a consistent template that agents can trace into events → handlers → systems → components.

## Template

Every use-case file contains:

- **Status** — draft | partial | implemented
- **Actors** — Player / Mob / System / Administrator
- **Module** — which `Core/Modules/<Feature>/` owns the scenario
- **Description** — one paragraph
- **Preconditions**
- **Postconditions**
- **Main flow** — numbered steps
- **Events fired** — so an agent can find publishers/subscribers
- **Systems / handlers involved** — traceable to the reference catalogs

## Index

### Gameplay — combat
- [combat-pulse-processing.md](combat-pulse-processing.md)
- [combat-skill-vs-defense.md](combat-skill-vs-defense.md)
- [player-death-and-respawn.md](player-death-and-respawn.md)
- [mob-death-and-loot.md](mob-death-and-loot.md)
- [group-combat-initiation.md](group-combat-initiation.md)
- [spell-casting-in-combat.md](spell-casting-in-combat.md)

### Gameplay — movement & world
- [entity-movement.md](entity-movement.md)
- [mob-wandering.md](mob-wandering.md)

### Gameplay — items & inventory
- [equipment-swap.md](equipment-swap.md)
- [potion-consumption.md](potion-consumption.md)
- [container-looting.md](container-looting.md)
- [access-control-violation.md](access-control-violation.md)

### Gameplay — economy & skills
- [shop-purchase.md](shop-purchase.md)
- [skill-based-crafting.md](skill-based-crafting.md)

### Editor (admin)
- [editor-area-deletion.md](editor-area-deletion.md)
- [editor-mob-deletion-with-inventory.md](editor-mob-deletion-with-inventory.md)

### System
- [game-state-persistence.md](game-state-persistence.md)

## Adding a new use case

Use the `implement-use-case` skill (`.claude/skills/implement-use-case/SKILL.md`) or the `/new-use-case` slash command. The skill will:

1. Scaffold the file using the template above.
2. Identify required events, handlers, and systems — cross-checking against the [reference catalogs](../reference/).
3. Leave TODO markers where the agent must make design decisions.
