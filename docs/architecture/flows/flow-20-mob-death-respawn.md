# Flow 20 — Mob death and respawn

> [Back to flows index](README.md)

**Summary.** A mob's HP reaches 0. `CombatTickHandler` determines the outcome and publishes `CombatEndedEvent(Outcome=MobDied)`. `CombatHandler` broadcasts the death narrative. `CombatMobDeathHandler` clears the attacker's combat state, publishes `MobDiedEvent` while the entity is still live, then destroys the mob entity. `SpawnSystem` observes `MobDiedEvent` to mark the slot vacant and set a respawn timer. On a later `HeartbeatTickEvent`, `SpawnSystem` spawns a fresh entity from the template and places it in the room.

**Trigger.** Mob HP reaches zero during a combat round.

```mermaid
sequenceDiagram
    participant HB as HeartbeatBackgroundService
    participant Bus as IEventBus
    participant CTH as CombatTickHandler (Domain 20)
    participant CH as CombatHandler (Domain 20)
    participant CMDH as CombatMobDeathHandler (Notification 80)
    participant SS as SpawnSystem (Domain 20)
    participant ES as EntityService

    HB->>Bus: Publish(HeartbeatTickEvent)
    Bus->>CTH: HandleAsync — resolve combat rounds
    CTH->>Bus: Publish(CombatEndedEvent{Outcome=MobDied})
    Bus->>CH: HandleAsync — broadcast "The wolf dies!"
    Bus->>CMDH: HandleAsync
    CMDH->>CMDH: ExitState(attacker, InCombat)
    CMDH->>Bus: Publish(MobDiedEvent{MobEntityId, BlueprintId})
    Bus->>SS: HandleAsync(MobDiedEvent) — mark slot vacant; RespawnAt = now + delay
    CMDH->>ES: DestroyEntity(mobEntityId)

    Note over HB,SS: ... RespawnDelaySeconds later ...

    HB->>Bus: Publish(HeartbeatTickEvent)
    Bus->>SS: HandleAsync(HeartbeatTickEvent) — slot due
    SS->>SS: TryRespawn — Spawn(blueprintId), AddComponent(LocationComponent)
    SS->>SS: Register new entityId in SpawnTracker
```

**Steps.**

1. `HeartbeatBackgroundService` fires on each tick; `CombatTickHandler` resolves pending combat rounds. A round where the mob's HP reaches 0 publishes `CombatEndedEvent { Outcome = MobDied }`.
2. **Death narrative.** `CombatHandler` (priority 20) broadcasts `"The <mob> dies!"` to the room.
3. **Death finalization.** `CombatMobDeathHandler` (priority 80):
   a. Calls `IEntityStateService.ExitState(attacker, InCombat)`.
   b. Looks up `BlueprintComponent` on the mob entity.
   c. Publishes `MobDiedEvent { MobEntityId, BlueprintId }`. All `MobDiedEvent` handlers complete before step (d).
   d. Calls `EntityService.DestroyEntity(mobEntityId)` — removes all components; fires `OnPersistentEntityDestroying` (no-op since mobs are not persistent).
4. **Slot vacancy.** `SpawnSystem.HandleAsync(MobDiedEvent)` (priority 20): looks up the reverse map for `MobEntityId`. If found, sets `SlotState.LiveEntityId = null` and `SlotState.RespawnAt = UtcNow + RespawnDelaySeconds`. Removes from the reverse map.
5. **Respawn tick.** On a subsequent `HeartbeatTickEvent`, `SpawnSystem` (priority Ai) scans all slots with `RespawnAt <= UtcNow`. For each due slot:
   a. Calls `TemplateRegistry.Spawn(blueprintId)` to create a fresh entity.
   b. Attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` using the slot's room entity.
   c. Registers the new entity ID in the slot tracker.
   d. Clears `RespawnAt`.

**Ordering invariant.** `MobDiedEvent` is published inside `CombatMobDeathHandler.HandleAsync` and awaited before `DestroyEntity` is called. Because `IEventBus.PublishAsync` dispatches all handlers sequentially, `SpawnSystem.HandleAsync(MobDiedEvent)` completes before `DestroyEntity` runs — the mob entity is still alive when the slot is recorded.

**Cross-references.**
- [`Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs`](../../../Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs)
- [`Core/Modules/Mobs/Events/MobDiedEvent.cs`](../../../Core/Modules/Mobs/Events/MobDiedEvent.cs)
- [`Core/Modules/Spawn/Systems/SpawnSystem.cs`](../../../Core/Modules/Spawn/Systems/SpawnSystem.cs)
- [`Core/ECS/Components/SpawnConfigComponent.cs`](../../../Core/ECS/Components/SpawnConfigComponent.cs)
- [`docs/implementation-plans/persistence-reform.md`](../../implementation-plans/persistence-reform.md) — Stage C, mob death flow
