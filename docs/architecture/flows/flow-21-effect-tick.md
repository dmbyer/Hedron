# Flow 21 — Effect tick (periodic application + expiry)

> [Back to flows index](README.md)

**Summary.** `EffectTickHandler` subscribes to `HeartbeatTickEvent` at priority 20. On each tick it calls `IEffectSystem.AdvanceTick`, which advances elapsed time on all `Timed` effects, collects `Periodic` applications due this tick, and removes just-expired effects. The handler then applies each periodic magnitude to the relevant pool via `IAttributeSystem` (in `Phase` order — HoT before DoT) and publishes `EffectExpiredEvent` for every effect that expired. For `HpCurrent` applications, the handler additionally calls `IDeathSystem.OnHpChanged` so a DoT that drives HP to 0 enters the incapacitation lifecycle.

**Trigger.** `HeartbeatTickEvent` dispatched by `HeartbeatBackgroundService` (see [Flow 16](flow-16-heartbeat-tick.md)). `EffectTickHandler` fires before `CombatTickHandler` (both priority 20; effects are processed before the combat round so updated pool values are available immediately).

```mermaid
sequenceDiagram
    participant ETH as EffectTickHandler (p=20)
    participant ES as IEffectSystem
    participant AS as IAttributeSystem
    participant DS as IDeathSystem
    participant Bus as IEventBus

    ETH->>ES: AdvanceTick(elapsed) → EffectTickResult
    note over ES: advance Elapsed on Timed effects;<br/>collect Periodic apps + expired;<br/>remove expired from EffectsComponent
    loop per PeriodicApplication (Phase order)
        alt TargetScore == HpCurrent
            ETH->>AS: hpBefore = GetCurrentHp(entityId)
            ETH->>AS: SetCurrentHp(entityId, hpBefore + magnitude)
            ETH->>AS: hpAfter = GetCurrentHp(entityId)
            ETH->>DS: OnHpChanged(entityId, hpBefore, hpAfter)
            opt result == BecameIncapacitated
                ETH->>Bus: PublishAsync(PlayerIncapacitatedEvent)
            end
        else other pool
            ETH->>AS: SetCurrentX(entityId, current + magnitude)
        end
    end
    loop per expired effect
        ETH->>Bus: PublishAsync(EffectExpiredEvent{TargetId, EffectId})
    end
```

**Steps.**

1. `HeartbeatBackgroundService` publishes `HeartbeatTickEvent`; `EffectTickHandler` (priority 20) handles it before `CombatTickHandler`.
2. Calls `IEffectSystem.AdvanceTick(@event.Elapsed)`. Inside the core system:
   - Iterates all entities with `EffectsComponent` (back-to-front to allow safe `RemoveAt`).
   - For each `Timed` effect: increments `Elapsed` by the tick's elapsed seconds using `with { Elapsed = newElapsed }` (record mutation).
   - Collects any `Timed` effects where `Elapsed >= Duration` as expired; removes them from the component list.
   - Collects any `Periodic` effects as due applications (fire once per tick).
   - Sorts both lists by `Phase` (ascending: `Early` < `Normal` < `Late`).
   - Returns `EffectTickResult { DueApplications, Expired }`.
3. **Periodic application.** For each `PeriodicApplication` in `DueApplications` (Phase order):
   - If `TargetScore == HpCurrent`: reads `hpBefore`, calls `IAttributeSystem.SetCurrentHp(entityId, hpBefore + magnitude)`, reads `hpAfter`, then calls `IDeathSystem.OnHpChanged(entityId, hpBefore, hpAfter)`. If the result is `BecameIncapacitated`, publishes `PlayerIncapacitatedEvent(entityId, roomEntityId)`.
   - All other pool scores: calls `IAttributeSystem.SetCurrentX(entityId, current + magnitude)` directly. `IAttributeSystem` clamps to `[0, MaxX]` automatically. `StatModifier` periodic effects targeting non-pool scores are a no-op here — their contribution is read via `IEffectSystem.GetModifiers` at query time.
4. **Expiry notification.** For each `(entityId, effect)` in `Expired`, publishes `EffectExpiredEvent { TargetId = entityId, EffectId = effect.EffectId }`. A player-facing "effect fades" broadcast is deferred to a future notification slice.

**Phase ordering.** `EffectPhase.Early` (e.g. `regen` HoT) fires before `EffectPhase.Late` (e.g. `poison` DoT) within a single tick. This ensures a heal-before-damage ordering when both apply on the same tick, preventing an entity from dying to DoT before a HoT that could save it.

**Persistence.** `EffectsComponent` is `[Persistent]`; `EffectsComponentJsonConverter` writes only `UntilRemoved` entries. `Timed` effects that expire mid-session were never written; `UntilRemoved` effects persist across restarts and resume ticking (they have no expiry timer — they are removed only by explicit `RemoveByCategory` or `Remove` calls).

**Cross-references.**
- [`Core/Modules/Effects/Handlers/EffectTickHandler.cs`](../../../Core/Modules/Effects/Handlers/EffectTickHandler.cs)
- [`Core/Modules/Effects/Systems/EffectSystem.cs`](../../../Core/Modules/Effects/Systems/EffectSystem.cs)
- [`Core/Modules/Effects/Events/EffectExpiredEvent.cs`](../../../Core/Modules/Effects/Events/EffectExpiredEvent.cs)
- [`Core/ECS/Components/EffectsComponent.cs`](../../../Core/ECS/Components/EffectsComponent.cs)
- [`docs/architecture/effects.md`](../effects.md) — effect model design (kinds, lifetimes, stacking, phases)
- [`docs/use-cases/effect-substrate.md`](../../use-cases/effect-substrate.md) — slice 9-e spec
- [Flow 16](flow-16-heartbeat-tick.md) — heartbeat trigger
