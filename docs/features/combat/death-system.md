# Death System

> Domain system owning the HP-threshold evaluation, bleed-out lifecycle, and respawn mutation for player death. **Authoring checkpoint:** slice 10. Living document.

## What it is / does

`DeathSystem` is the **single HP-threshold decision seam** for player death. It evaluates whether an HP mutation crossed the incapacitation or death boundary, orchestrates the respawn mutation, and manages the stored respawn location. It never publishes events or calls persistence (INV-5, INV-8) — callers read the `DeathTransition` result and publish the appropriate events.

Mobs do not enter this pipeline. `CombatRoundOutcome.MobDied` is handled entirely by `CombatMobDeathHandler` in the combat feature.

## How it works

### The `OnHpChanged` seam

`IDeathSystem.OnHpChanged(entityId, previousHp, newHp)` is called by whichever Initiator/Handler mutated HP — `CombatTickHandler` on a killing-blow round, `EffectTickHandler` on a DoT tick, `DeathTickHandler` during bleed-out, or any admin command. `IAttributeSystem` deliberately does **not** call this itself — a core compute seam chaining into a domain decision would violate the layer model and create hidden side-effects on every HP write (INV-5). The caller owns the threshold call.

Return values: `None` (no threshold crossed), `BecameIncapacitated` (HP just crossed 0 for the first time), `Died` (HP ≤ `Death:HpFloor`).

### Incapacitation (HP → 0)

On a 0-or-below crossing for a player not already incapacitated: `OnHpChanged` calls `IEntityStateService.TryEnterState(entityId, Incapacitated)` and returns `BecameIncapacitated`. The caller then publishes `PlayerIncapacitatedEvent` to trigger narration and open the bleed-out loop.

### Command blocking while incapacitated

`CommandDispatcher` gains an incapacitation gate (after verb resolution, before privilege check): if `IEntityStateService.IsInState(Incapacitated)` is true and the command is not flagged `UsableWhileIncapacitated`, the dispatcher refuses it. `ICommand.UsableWhileIncapacitated` defaults `false` — new commands are blocked while incapacitated unless they opt out. `help`, `commands`, and `score` are allowlisted. The gate lives in the dispatcher, not in authorization, because incapacitation is a transient entity state, not a privilege.

### Bleed-out (heartbeat-driven)

`DeathTickHandler` (priority 20, `HeartbeatTickEvent`) snapshots all entities with `EntityStateFlags.Incapacitated`. For each: reads current HP, subtracts `Death:BleedPerTick` (default 1), calls `IAttributeSystem.SetCurrentHp`, then calls `IDeathSystem.OnHpChanged`. On `None`: publishes `PlayerBleedingEvent`; `DeathNarrationHandler` (priority 80) sends bleed status to the player and a third-person message to the room. On `Died`: publishes `PlayerDiedEvent` to trigger respawn.

`IAttributeSystem.SetCurrentHp`'s clamp floor is `Death:HpFloor` (default −10), not 0, so overkill and bleed-out can drive HP negative without clamping.

### Death and respawn

`PlayerDeathHandler` (priority 20, `PlayerDiedEvent`) calls `IDeathSystem.Respawn(entityId)`, which:

1. Calls `IEntityStateService.ExitState(entityId, Incapacitated)`.
2. Resolves `RespawnComponent.RoomBlueprintId` to a live room entity id (same blueprint→entity map as `CharacterHydrationHandler`). Falls back to `WorldConfiguration.StartingRoomBlueprintId` with a warning if unresolvable.
3. Updates `LocationComponent` to the resolved room.
4. Calls `IEffectSystem.RemoveImpermanent(entityId)` — strips all non-`UntilRemoved` effects. Permanent (`UntilRemoved`) effects persist through death.
5. Restores all four pools to `floor(Max × Death:RespawnPoolPercent)` (default 25%) via `IAttributeSystem`.

`PlayerDeathHandler` then publishes `PlayerRespawnedEvent`. `DeathNarrationHandler` (priority 80) handles narration: death broadcast to the death room, respawn confirmation to the player, arrival broadcast to the respawn room.

No `SaveEntityAsync` is called in the respawn flow — pool/location/effect mutations are covered by the next periodic flush (INV-22 runtime transition). The admin `setrespawn` command is the only death-module path that calls `SaveEntityAsync` (INV-22 admin boundary save).

### Respawn location

`RespawnComponent { RoomBlueprintId: string? }` is `[Persistent]`, player-only. It stores the blueprint id, not the runtime entity id — blueprint ids are stable across restarts; room entity ids are not. Set at character creation by `AccountSystem.CreateCharacterAsync`. Updated by admin `setrespawn` command via `IDeathSystem.SetRespawn(entityId, roomBlueprintId)`.

### Mob death reward seam

`MobDiedEvent` carries `KillerEntityId` (0 = no attributable killer). No subscriber consumes this field yet; it is the documented hook for a future `RewardSystem` (XP/loot). `SpawnSystem` observes `MobDiedEvent` for slot vacancy, unaffected by the added field.

## Interface

- [`IDeathSystem.cs`](../../../Core/Modules/Death/Systems/IDeathSystem.cs) — `OnHpChanged`, `Respawn`, `SetRespawn`. Pure: returns `DeathTransition`; never touches the bus or persistence.

## Considerations

- **Impermanent vs. permanent reuses `EffectLifetime`** — death-expiry and persistence-inclusion share one definition of "permanent" (`UntilRemoved`), so they cannot drift (see [`../effects/effect-system.md`](../effects/effect-system.md) § Lifetimes).
- **Soft death** — relocate + 25% restore; no corpse, item loss, or XP loss. `PlayerDiedEvent` carries the death room id so a future corpse-spawn handler has the location without re-deriving it.
- **`UsableWhileIncapacitated` is default-deny** — correct default for incapacitation (forgetting the flag fails safe: a command is blocked, not accidentally allowed). The inverse of the privilege model (default-public).
- **`RespawnComponent` stores blueprint id, not entity id** — mirrors `LocationComponent`'s cross-restart model; resolved to a live entity at respawn time.
- **Determinism** — no randomness in the death pipeline. If a future formula rolls (e.g. a death penalty roll), route it through `IRandom` (INV-26).

## Extensibility

- **Harsher death / corpse retrieval** — the soft-death substrate (`PlayerDiedEvent` with death room, `RespawnComponent`) is the extension point. No death pipeline changes needed.
- **Per-pool respawn fractions** — currently one flat `RespawnPoolPercent`. Generalize to a per-pool table when a consumer needs it.
- **Healing while incapacitated** — `OnHpChanged` returning `None` (HP went up but stays ≤ 0) is currently silent. A future healing system can check this case and exit incapacitation early.

## Related

- [`combat.md`](combat.md) — the holistic feature view; combat round pulse feeds into this pipeline.
- [Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md) — mob death, incapacitation, bleed-out, and player respawn in sequence.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `DeathSystem`/`IDeathSystem`, `RespawnComponent` rows.
- [`../../roadmap/completed/slice-10-death-and-respawn.md`](../../roadmap/completed/slice-10-death-and-respawn.md) — as-built record and design decisions.
- [`../effects/effect-system.md`](../effects/effect-system.md) — `EffectLifetime` and `RemoveImpermanent`; DoT ticks are a death-pipeline entry point.
