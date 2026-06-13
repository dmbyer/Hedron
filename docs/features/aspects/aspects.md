# Aspects

> Elemental typing of damage and effects — affinity and resistance make each aspect of an attack matter. **Status:** live (slice 11-d; `Fire` / `Ice` / `Lightning` / `Void` / `Nature` / `Light` vocabulary wired).

## What it is

An **aspect** is an elemental tag attached to an outgoing strike or effect. When a player lands a `kick` or a spell hit, that damage may carry a composition of aspects (e.g. pure Fire, or a blend of Fire and Void). The aspect system then applies the attacker's **affinity** — their natural talent with that element — to boost the magnitude, and the defender's **resistance** to reduce it.

From a player's seat:

- Damage is elemental when the ability or the attacker has an aspect composition.
- Affinities and resistances are per-entity values attached to characters and mobs; currently blank at character creation (all are neutral by default).
- The `defs aspect` admin command lists the full aspect vocabulary; `defs aspect fire` dumps a single definition.
- No separate "elemental strike" command exists — aspect typing is built into the combat round and ability activation transparently.

The result: the same raw damage number can deal more or less final damage depending on the elemental match between attacker and defender.

## How it works

The feature composes two pieces:

- **`AspectRegistry`** — the fixed vocabulary of `AspectDefinition` records (`AspectId` + `Name` + `Description` + `AspectCategory`). Populated at DI construction; validated at startup by `RegistryValidationBootstrap`. No events, no persistence.
- **`IAspectSystem`** — core math with no per-aspect branching ("no FireSystem"). On every combat round or ability strike, `CombatSystem` constructs the outgoing `AspectComposition` (from the ability's `Aspect` field or the attacker's entity affinity), then calls `IAspectSystem.Resolve` to apply affinity boost and independent per-aspect resistance and return the final magnitude. Affinities and resistances are computed on read from `AspectAffinitiesComponent`; nothing is cached (INV-24).

The full model — composition math, affinity/resistance dimensions, registry keys, and the `RegistryValidationBootstrap` sweep — is the [aspect-system design doc](aspect-system.md).

## Systems

| System | Role |
|---|---|
| [`aspect-system.md`](aspect-system.md) | Aspect composition math, affinity/resistance model, registry design, startup validation |

## Surfaces

- **Component** — `AspectAffinitiesComponent` (`[Persistent]`; `AffinityWeights` + `BaseResistances`). See [`../../reference/components.md`](../../reference/components.md).
- **Admin command** — `defs aspect [id]` — lists all aspect ids or dumps one definition (generic `defs` command covers all registries). See [`../../reference/commands.md`](../../reference/commands.md).

## Flows

Aspect resolution has no dedicated journey — it is a step inside the combat round. See the [combat journey](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) for the full round context.

## Related

- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine A (Aspect) and Spine F (Registry layer), the upstream design this realizes.
- [`../../roadmap/completed/aspect-foundation.md`](../../roadmap/completed/aspect-foundation.md) — as-built history and decisions (slice 11-d).
- **Combat** — `CombatSystem` calls `IAspectSystem.Resolve` on every round and ability strike. See [`../combat/combat.md`](../combat/combat.md).
- **Abilities** — `AbilityDefinition.Aspect` carries the `AspectComposition?` for offensive abilities. See [`../abilities/abilities.md`](../abilities/abilities.md).
- **Effects** — `EffectParams.Aspect` migration is deferred (tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md)).
