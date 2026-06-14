# Combat

> Melee combat between players and mobs: initiating with `kill`, heartbeat-driven round resolution, `flee`, and the incapacitation/death lifecycle. **Status:** live (slices 9, 9-a, 10).

## What it is

A player types `kill <mob>` to engage a mob in the current room. Once combat begins, the game clock drives rounds automatically — damage flows both ways each heartbeat tick, and the player's HP readout updates in the prompt after each round. Typing `flee` exits combat immediately and unconditionally.

If the mob's HP reaches zero, the mob is slain, its blueprint slot is freed, and it will respawn from its template later. If the player's HP reaches zero, they are incapacitated: unable to act, bleeding out one HP per tick, until they either receive healing or bleed to the death floor (−10 HP) and respawn in a weakened state at their stored respawn room.

The fight loop is intentionally simple — no aggro, no group combat, no skills-in-combat beyond what the abilities feature provides. Weapon damage contributes via `IStatSystem.GetEffectiveAttackPower`; armor defense contributes via `IStatSystem.GetEffectiveDefense`.

## How it works

The feature composes four cooperating pieces:

- **`KillCommand`** — validates state, prefix-matches the target mob in the room via `ICombatSystem.TryFindTargetInRoom`, transitions both entities to `InCombat` via `IEntityStateService`, attaches `CombatStateComponent` via `ICombatSystem.StartCombat`, and publishes `CombatStartedEvent`. It is the Initiator; it does not compute damage.
- **`ICombatSystem`** — domain system that owns target lookup, combat state attachment/removal, and round resolution. Reads effective stats through `IStatSystem`; applies aspect math through `IAspectSystem`; mutates HP through `IAttributeSystem`. Returns `CombatRoundResult`; never touches the event bus. The full model is the [combat-system design doc](combat-system.md).
- **`CombatTickHandler`** (priority 20, `HeartbeatTickEvent`) — the bridge between the time system and the combat domain. Snapshots all entities with `CombatStateComponent`, deduplicates into unique pairs, calls `ICombatSystem.ExecuteRound` per pair, and publishes `CombatRoundEvent`. Handles terminal outcomes inline: mob death → `CombatEndedEvent(MobDied)` before `CombatMobDeathHandler` destroys the entity; player incapacitation → delegates to `IDeathSystem.OnHpChanged`, then publishes `PlayerIncapacitatedEvent` to open the bleed-out lifecycle.
- **`IDeathSystem`** / **`DeathTickHandler`** — own the incapacitation → bleed-out → respawn pipeline once the HP floor is crossed. The full model is the [death-system design doc](death-system.md).

`CombatHandler` (priority 20) handles all three combat events for output fan-out only — it never calls systems or mutates state. `CombatMobDeathHandler` (priority 80) handles mob death finalization after output has landed.

The two-layer state model used throughout: `EntityStateComponent.InCombat` (the observable flag, gating state-gated commands) + `CombatStateComponent.OpponentEntityId` (the metadata, driving round execution). Both coexist; commands gate on the flag, the pulse queries the metadata. See [entity-state.md](entity-state.md).

## Systems

| System | Role |
|---|---|
| [`combat-system.md`](combat-system.md) | Target resolution, round formula, `CombatStateComponent` lifecycle, aspect-resolved damage |
| [`death-system.md`](death-system.md) | Incapacitation threshold, bleed-out, respawn mutation, respawn location |
| [`entity-state.md`](entity-state.md) | Cross-cutting `EntityStateFlags` + `IEntityStateService` transition table |

## Surfaces

- **Commands** — `kill <target>` / `k`, `flee`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `CombatStartedEvent`, `CombatRoundEvent`, `CombatEndedEvent` (`MobDied`/`PlayerIncapacitated`/`PlayerFled`); `PlayerIncapacitatedEvent`, `PlayerBleedingEvent`, `PlayerDiedEvent`, `PlayerRespawnedEvent`; `MobDiedEvent` (extended with `KillerEntityId`). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — `CombatStateComponent` (transient, not `[Persistent]`), `EntityStateComponent` (transient), `RespawnComponent` (`[Persistent]`). See [`../../reference/components.md`](../../reference/components.md).
- **Admin command** — `setrespawn <player> <roomBlueprintId>`. See [`../../reference/commands.md`](../../reference/commands.md).

## Flows

- [Combat journey (initiation · round pulse · flee)](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) — `kill` through `flee`, including the heartbeat-driven round loop and mob death.
- [Death & respawn journey (mob death · incapacitation · bleed-out · player death/respawn)](../../architecture/flows/flow-20-mob-death-respawn.md) — what happens after HP hits zero on either side.

## Related

- [`../../architecture/03-events.md`](../../architecture/03-events.md) — event bus and handler ordering (priority 20 / 80 distinction).
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems pure), INV-21 (blueprint/instance separation on mob death), INV-22 (admin boundary save for `setrespawn`).
- [`../../roadmap/completed/slice-9-combat.md`](../../roadmap/completed/slice-9-combat.md) · [`../../roadmap/completed/slice-10-death-and-respawn.md`](../../roadmap/completed/slice-10-death-and-respawn.md) · [`../../roadmap/completed/slice-9a-entity-state-management.md`](../../roadmap/completed/slice-9a-entity-state-management.md) — as-built history and design decisions.
- **Character stats** (not yet migrated) — `IStatSystem` is the stat aggregation seam combat reads; see [`../../reference/systems.md`](../../reference/systems.md) for the `StatSystem` row.
- **Effects** — [`../effects/effects.md`](../effects/effects.md) — DoT ticks also enter the death pipeline via `IDeathSystem.OnHpChanged`; `EffectTickHandler` calls `OnHpChanged` after each periodic HP mutation.
