# Flow 18 — Combat round pulse (heartbeat-driven)

> [Back to flows index](README.md)

**Summary.** `CombatTickHandler` subscribes to `HeartbeatTickEvent`. On each tick it snapshots all entities with `CombatStateComponent`, deduplicates pairs (lower entity id = attacker), calls `ICombatSystem.ExecuteRound` for each pair, and publishes `CombatRoundEvent`. Terminal outcomes are handled inline: `MobDied` triggers cleanup + `CombatEndedEvent(MobDied)`, causing `CombatHandler` (p=20) to broadcast the kill narrative before `CombatMobDeathHandler` (p=80) destroys the entity. `PlayerIncapacitated` ends combat and then calls `IDeathSystem.OnHpChanged` — if `BecameIncapacitated` is returned, `PlayerIncapacitatedEvent` is published to kick off the bleed-out lifecycle. All messages written during this flow carry `OutputCategory.System` (flush policy: **Batched**) — they accumulate in each session's buffer and are not sent until `OutputFlushTickHandler` (p=85) flushes at tick-end, appending one prompt per session. (`OutputCategory.Combat` is reserved for a future dedicated message type; current handlers use `System`.)

**Trigger.** `HeartbeatTickEvent` dispatched by `HeartbeatBackgroundService` (see [Flow 16](flow-16-heartbeat-tick.md)).

```mermaid
sequenceDiagram
    participant CTH as CombatTickHandler (p=20)
    participant CS as ICombatSystem
    participant DS as IDeathSystem
    participant Bus as IEventBus
    participant CH as CombatHandler (p=20)
    participant MDH as CombatMobDeathHandler (p=80)
    participant DNH as DeathNarrationHandler (p=80)
    participant OFTH as OutputFlushTickHandler (p=85)
    participant Buf as ISessionOutputBuffer (per session)

    CTH->>CTH: snapshot all CombatStateComponent entities
    loop per deduplicated pair (entityId < opponentId)
        CTH->>CTH: hpBefore = IStatSystem.GetCurrentHp(defenderId)
        CTH->>CS: ExecuteRound(attackerId, defenderId) → CombatRoundResult
        CTH->>Bus: PublishAsync(CombatRoundEvent)
        Bus->>CH: HandleAsync → hit/miss CombatMessage [enqueue in each session's buffer]
        alt result.Outcome == MobDied
            CTH->>CS: EndCombat(attackerId, defenderId)
            CTH->>Bus: PublishAsync(CombatEndedEvent MobDied, DefenderName=captured)
            Bus->>CH: HandleAsync (p=20) → "You have slain X!" CombatMessage [enqueue]
            Bus->>MDH: HandleAsync (p=80) → ExitState + MobDiedEvent + DestroyEntity
        else result.Outcome == PlayerIncapacitated
            CTH->>CS: EndCombat + ExitState on both
            CTH->>Bus: PublishAsync(CombatEndedEvent PlayerIncapacitated)
            Bus->>CH: HandleAsync → incapacitation CombatMessage [enqueue]
            CTH->>DS: OnHpChanged(defenderId, hpBefore, hpAfter) → BecameIncapacitated
            CTH->>Bus: PublishAsync(PlayerIncapacitatedEvent)
            Bus->>DNH: HandleAsync (p=80) → "You collapse..." to player; room broadcast [enqueue]
        end
    end
    Note over OFTH,Buf: After all combat handlers complete (p=85 fires last among output producers)
    Bus->>OFTH: HandleAsync → FlushAllPendingAsync()
    loop per session with pending combat messages
        OFTH->>Buf: FlushAsync() → drain + format + send + append one PromptMessage
    end
```

**Steps.**

1. `HeartbeatBackgroundService` publishes `HeartbeatTickEvent`; `CombatTickHandler` (priority 20) handles it.
2. **Snapshot.** Calls `EntityService.GetAllComponents<CombatStateComponent>().ToList()` — snapshot before iteration to avoid mutation during enumeration.
3. **Deduplication.** For each `(entityId, state)` in the snapshot: skip if `entityId >= state.OpponentEntityId`. This ensures each pair is processed exactly once; the lower entity id is designated the "attacker" for this round.
4. **Round execution.** Reads `hpBefore = IStatSystem.GetCurrentHp(defenderEntityId)`. Calls `ICombatSystem.ExecuteRound(attackerEntityId, defenderEntityId)` → `CombatRoundResult`. The formula: hit check `roll = Random.Shared.Next(1,21) + body/2 >= 10 + defense`; if hit, damage `= Random.Shared.Next(1, attackPower+2)` applied via `IAttributeSystem.SetCurrentHp`. *(Stat source changed from `Dexterity` to `Body` in slice 9-d; `AttackPower` and `Defense` are both Body-governed — see [`docs/use-cases/stat-resource-substrate.md`](../../use-cases/stat-resource-substrate.md).)*
5. Publishes `CombatRoundEvent`. `CombatHandler` (priority 20) broadcasts hit/miss/damage narrative.
6. **`MobDied` path.** Captures `MobDataComponent.Name` from the mob entity (point-in-time capture, before destruction). Calls `ICombatSystem.EndCombat`. Publishes `CombatEndedEvent(MobDied, DefenderName=mobName)`. `CombatHandler` (priority 20) broadcasts `"You have slain <mob>!"` using `DefenderName` from the payload. `CombatMobDeathHandler` (priority 80) then calls `IEntityStateService.ExitState(attackerEntityId, InCombat)`, publishes `MobDiedEvent` (with `KillerEntityId`), and calls `EntityService.DestroyEntity(mobEntityId)`.
7. **`PlayerIncapacitated` path.** Calls `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` on both entities. Publishes `CombatEndedEvent(PlayerIncapacitated)` — `CombatHandler` broadcasts incapacitation narrative. Then reads `hpAfter` and calls `IDeathSystem.OnHpChanged(defenderEntityId, hpBefore, hpAfter)`. If the result is `BecameIncapacitated`, publishes `PlayerIncapacitatedEvent(defenderEntityId, roomEntityId)` to begin the bleed-out lifecycle (see [Flow 22 — death and respawn](flow-22-death-and-respawn.md)).

**Output batching note.** `CombatHandler` and `DeathNarrationHandler` write `PlainMessage`s that carry `OutputCategory.System` (flush policy: `Batched`). These messages accumulate in each affected session's `ISessionOutputBuffer` during the combat round loop — no message is sent to any client during step 4. Once all `HeartbeatTickEvent` handlers at priority ≤ 80 have completed, `OutputFlushTickHandler` (p=85) fires and calls `ISessionBufferRegistry.FlushAllPendingAsync()`. Each session's buffer is then drained atomically, all pending messages are formatted and sent, and one `PromptMessage` is appended. The player sees all of that tick's combat lines (hit, damage, death narrative, incapacitation narrative, etc.) as a batch followed by a single prompt reflecting post-round pools — not a prompt after every individual line.

**Cross-references.**
- [`Core/Modules/Combat/Handlers/CombatTickHandler.cs`](../../../Core/Modules/Combat/Handlers/CombatTickHandler.cs)
- [`Core/Modules/Combat/Handlers/CombatHandler.cs`](../../../Core/Modules/Combat/Handlers/CombatHandler.cs)
- [`Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs`](../../../Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs)
- [`Core/Modules/Combat/Systems/CombatSystem.cs`](../../../Core/Modules/Combat/Systems/CombatSystem.cs)
- [`Core/Modules/Combat/Events/CombatRoundEvent.cs`](../../../Core/Modules/Combat/Events/CombatRoundEvent.cs)
- [`Core/Modules/Combat/Events/CombatEndedEvent.cs`](../../../Core/Modules/Combat/Events/CombatEndedEvent.cs)
- [`Core/Handlers/OutputFlushTickHandler.cs`](../../../Core/Handlers/OutputFlushTickHandler.cs) — tick-end buffer flush (WP-C)
- [`Core/Output/ISessionBufferRegistry.cs`](../../../Core/Output/ISessionBufferRegistry.cs) — session buffer registry
- [`docs/use-cases/combat.md`](../../use-cases/combat.md) — slice 9 spec; [Flow 16](flow-16-heartbeat-tick.md) — heartbeat trigger; [`docs/use-cases/prompt-and-output-batching.md`](../../use-cases/prompt-and-output-batching.md) — output batching spec
