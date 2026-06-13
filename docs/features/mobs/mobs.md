# Mobs

> Non-player characters that occupy rooms, are visible to players via `look`, and serve as combat targets. **Status:** live (slice 8; attributes extended slice 8-a).

## What it is

A **mob** is a non-player entity that inhabits the world. A designer authors one in YAML (`kind: mob`) and on startup (or `reload`) it spawns into its configured room. Players see it listed in `look` output; the `kill` command targets it by name or keyword. Mobs have no autonomous behavior today — wandering, aggro, and loot belong to later slices.

The mob model is intentionally narrow: identity data (`Name`, `Description`, `Keywords`, `MobType`), a spawn location, base attributes for combat, and a blueprint tracking component. Everything a consuming feature (combat, AI, dialogue) needs is reachable through `MobDataComponent` without a domain dependency on the Mobs module itself.

## How it works

The feature composes two lightweight pieces:

- **`MobBuilderSystem`** — the domain system that creates ad-hoc mob entities and mutates their properties. Returns results; never publishes events or calls persistence (INV-5). Admin commands are the Initiators.
- **`WorldContentLoader`** (extended) — loads `kind: mob` YAML files from `data/content/mobs/` at startup and reload, registers `MobTemplate`s, and calls `PlaceMobsInRooms` for newly-spawned entities only (restored-from-persistence mobs already carry a saved `LocationComponent`).

`BroadcastSystem.SendRoomDescriptionAsync` queries entities with `MobDataComponent` in the room and populates `RoomDescriptionMessage.Mobs`; `TelnetOutputFormatter` renders each mob as `"<Name> is here."` below the items section.

The full model — YAML shape, blueprint id format, builder method contracts, and the YAML atomic-write path — is the [mob-system design doc](mob-system.md).

## Systems

| System | Role |
|---|---|
| [`mob-system.md`](mob-system.md) | Mob data model, builder lifecycle, YAML content writer, and template deserializer |

## Surfaces

- **Commands (admin)** — `mkmob [name]` (creates a mob in the invoker's room; prints blueprint id), `setmob <blueprintId> <property> <value>` (mutates `name`, `description`, `keywords`, `type`). See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `MobCreatedByAdminEvent`, `MobPropertySetByAdminEvent` (thin, past-tense; audit only). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Component** — `MobDataComponent` (`[Persistent]`, cross-cutting). See [`../../reference/components.md`](../../reference/components.md).

## The combat-target surface

`MobDataComponent` is the hook combat and ability features use to find and engage a mob:

- `ICombatSystem.TryFindTargetInRoom(roomEntityId, token)` prefix-matches `token` against `MobDataComponent.Name` and `Keywords` for every entity in the room carrying `MobDataComponent`. First match wins.
- `LocationComponent.RoomEntityId` on a mob entity is the room it occupies — the same field that tracks player location.
- On mob death, `CombatMobDeathHandler` captures `MobDataComponent.Name` from the payload **before** destruction so the kill narrative renders correctly. See [`../combat/combat.md`](../combat/combat.md).

`look <mob>` by keyword (inspect a specific mob) is out of scope until the combat slice introduces `kill <mob>` argument resolution.

## Related

- [`mob-system.md`](mob-system.md) — the design doc for `MobBuilderSystem`, `MobContentWriter`, `MobTemplate`, and the content-writer pattern.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-21 (blueprint/instance separation; mobs retain `BlueprintComponent` unlike items — the respawn system will handle it); INV-14 (persistence two-level opt-in).
- [`../../roadmap/completed/slice-8-mobs.md`](../../roadmap/completed/slice-8-mobs.md) — as-built history and design decisions.
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — `MobDataComponent` provides the identity and targeting hook; `LocationComponent` provides the location hook.
- **Items** (not yet migrated) — `IItemBuilderSystem` and `PlaceItemsInRooms` are the direct precedents for the builder and spawn patterns.
