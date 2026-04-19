# Player Death and Respawn

**Status:** partial
**Actors:** Player, System
**Module:** `Core/Modules/Combat/` + `Core/Modules/Progression/`

This is the canonical worked example for multi-handler event flow — see [../architecture/03-events.md#worked-example-player-death](../architecture/03-events.md#worked-example-player-death).

## Description

A player's health drops to zero in combat. They are removed from combat, suffer a death penalty, receive a notification, are respawned at their home location, and any witnesses see the death.

## Preconditions

- Target has `PlayerDataComponent`
- `PoolsComponent.CurrentHP <= 0` after damage application
- A valid respawn location exists (world default if nothing bound)

## Postconditions

- Player removed from combat state
- Death penalty applied to attributes (see `DeathSystem.ApplyDeathPenalties`)
- Player moved to respawn location via `MovementSystem.Teleport`
- HP restored to a playable value
- Witnesses in the death location see a death message
- Player receives "You have died." + respawn description

## Main flow

1. `CombatSystem.ApplyDamage` returns `DamageResult { Killed = true }`
2. `CombatHandler` publishes `DamageEvent` then `PlayerDeathEvent`
3. Multiple handlers subscribe:
   - `CombatHandler` (priority 10) — remove from combat
   - `PlayerConditionHandler` (priority 20) — apply penalties + respawn
   - `NotificationHandler` (priority 80) — notify witnesses
   - `PersistenceHandler` (priority 90) — save state
   - `AIHandler` (priority 95) — update threat tables
4. Respawn sequence fires `PlayerRespawnedEvent` for UI refresh

## Events fired

- `DamageEvent` — the fatal hit
- `PlayerDeathEvent` — player died (carries location captured at death)
- `PlayerRespawnedEvent` _(planned)_ — after respawn

## Systems / handlers

- `CombatSystem`, `DeathSystem`, `MovementSystem`, `VisibilitySystem`, `NotificationSystem`
- Handlers: `CombatHandler`, `PlayerConditionHandler`, `NotificationHandler`, `PersistenceHandler`, `AIHandler`

## Design notes

- **Location captured at death time** — `PlayerDeathEvent.Location` records the death room so `NotificationHandler` picks the right witnesses even if `RespawnHandler` has already moved the player.
- **Separation of concerns** — penalty logic lives in `DeathSystem`, not in handlers.

## Related

- [../architecture/03-events.md](../architecture/03-events.md)
- [../architecture/04-pitfalls.md#handler-ordering-issues](../architecture/04-pitfalls.md#handler-ordering-issues)
- [mob-death-and-loot.md](mob-death-and-loot.md)
