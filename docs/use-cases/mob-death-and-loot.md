# Mob Death and Loot Generation

**Status:** partial
**Actors:** Player, Mob, System
**Module:** `Core/Modules/Combat/` + `Core/Modules/Loot/`

## Description

A mob is killed in combat. It is removed from the world. The killing player receives the mob's currency and any carried items (transferred to inventory or dropped to the room per loot rules).

## Preconditions

- Mob is an instance with `CurrentHP <= 0`
- A player was engaged in combat with the mob
- Mob may have `CurrencyComponent` and/or items in `InventoryComponent`

## Postconditions

- Mob removed from instance cache; world has no reference to it
- Currency transferred to player's `CurrencyComponent`
- Items: transferred to player inventory (simple looting) OR dropped to `InventoryComponent` of the room (auto-drop when full) OR left in a `Storage` corpse entity (advanced looting)
- Player exits combat state if no targets remain

## Main flow

1. `CombatSystem.ApplyDamage` returns `DamageResult { Killed = true }` for a mob (entity has `MobDataComponent`, not `PlayerDataComponent`)
2. `CombatHandler` publishes `DamageEvent`, then `MobDeathEvent` _(new, planned)_
3. `LootHandler` picks up the event:
   - Calls `LootSystem.GenerateLoot` with the mob's `MobDataComponent` and level context
   - Calls currency transfer
   - Distributes items (alone, or per group rules — see group combat)
4. `CombatHandler` removes the mob from the combat state and destroys the entity via `EntityService.DestroyEntity`
5. If no more opponents remain, `CombatEndedEvent` fires

## Events fired

- `DamageEvent`
- `MobDeathEvent` _(planned — symmetric to PlayerDeathEvent but distinct)_
- `LootDroppedEvent`
- `CombatEndedEvent` (if no opponents remain)

## Systems / handlers

- `CombatSystem`, `LootSystem`, `ItemGeneratorSystem`, `InventorySystem`, `CurrencySystem`
- Handlers: `CombatHandler`, `LootHandler`, `NotificationHandler`

## Design notes

- **Prototype vs instance**: only instances die. Prototype mob deletion is an editor concern — see [editor-mob-deletion-with-inventory.md](editor-mob-deletion-with-inventory.md).
- Loot tables live as data; the `LootSystem` is generic (domain), and the rules (weighted rolls, luck) live in `RandomGeneratorSystem` (core).

## Related

- [player-death-and-respawn.md](player-death-and-respawn.md)
- [combat-pulse-processing.md](combat-pulse-processing.md)
