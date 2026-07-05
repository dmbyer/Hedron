# Progression journey (combat XP award · threshold improve · contribute-on-read)

> [Back to flows index](README.md). **Trigger:** `MobDiedEvent` fires (a player kills a mob).

## Summary

`ExperienceAwardHandler` is one of three independent `MobDiedEvent` subscribers (see [flow-20](flow-20-mob-death-respawn.md)) — it reads the still-live mob to resolve the killer's combat XP award. `IProgressionSystem.AwardCombatExperience` computes a killer-vs-victim anti-grind scale from raw `AttributesComponent` fields (not `IStatSystem` — see the [progression-system design doc](../../features/progression/progression-system.md#anti-grind-proxy-reads-raw-attributes) for why), rolls a randomized per-track base amount via the injected `IRandom`, scales it, and awards each of the slice-1 combat tracks (`Body`, `HpMax`) through `AwardExperience` → threshold-check → `TryImprove`. The handler then publishes `ExperienceAwardedEvent` per positive-amount track and `TrackImprovedEvent` per threshold crossed. The power step from any improved track is never stored — it is pulled on read by `ProgressionEffectContributor` (an `IEffectContributor` registrant) the next time `IStatSystem.Get` is called for that score, e.g. from `score`, combat, or the `progress` inspector.

```mermaid
sequenceDiagram
    participant Bus as IEventBus
    participant EAH as ExperienceAwardHandler (p=20)
    participant PS as IProgressionSystem
    participant Rng as IRandom
    participant Stat as IStatSystem.Get (later read)
    participant PEC as ProgressionEffectContributor

    Bus->>EAH: HandleAsync(MobDiedEvent{KillerEntityId})
    Note over EAH: KillerEntityId==0 → discard, nothing published
    EAH->>PS: AwardCombatExperience(killerId, victimId)
    PS->>PS: read raw AttributesComponent (killer, victim) → anti-grind scale
    PS->>Rng: Next(CombatAwardMin, CombatAwardMax+1) per track (skipped if scale==0)
    PS->>PS: AwardExperience → TryImprove (per track)
    PS-->>EAH: CombatAwardResult (per-track AwardOutcome)
    EAH->>Bus: PublishAsync(ExperienceAwardedEvent) per positive-amount track
    EAH->>Bus: PublishAsync(TrackImprovedEvent) per threshold crossed

    Note over Stat,PEC: Later, any IStatSystem.Get(entity, track) call
    Stat->>PEC: GetModifiers(entity, track)
    PEC->>PS: GetImprovementCount(entity, track)
    PEC-->>Stat: PowerPerImprovement × improvementCount
```

## Steps

1. `ExperienceAwardHandler` (priority 20) receives `MobDiedEvent`. `KillerEntityId == 0` → return (no award, no event).
2. `IProgressionSystem.AwardCombatExperience(killerEntityId, victimEntityId)`: sums each combatant's raw `Mind + Body + Spirit + Attunement` (not effect-folded — see the design doc), computes `scale = ratio < AntiGrindFloorRatio ? 0 : min(ratio, AntiGrindCap)`, then for each of `ProgressionConstants.CombatTracks` (`Body`, `HpMax`): if `scale > 0`, draws a base amount via `IRandom.Next(CombatAwardMin, CombatAwardMax+1)` and scales it; if `scale == 0`, the amount is `0` with no draw.
3. Each track's scaled amount flows through `AwardExperience` → `TryImprove`: adds to cumulative XP (no-op if ≤ 0), then loops while cumulative XP ≥ the next cumulative threshold (`ThresholdBase + improvementCount × ThresholdIncrement`), incrementing the improvement count once per crossing — a single large award can cross several thresholds in one call.
4. The handler publishes one `ExperienceAwardedEvent(entityId, track, amount, XpSource.CombatKill)` per track with a positive amount, and one `TrackImprovedEvent(entityId, track, newImprovementCount)` per threshold crossed.
5. Nothing is stored beyond `ProgressionComponent`'s XP/improvement counters. The next time anything calls `IStatSystem.Get(entity, track)`, `EffectSystem.GetModifiers` sums the DI-collected `IEffectContributor`s, including `ProgressionEffectContributor`, which returns `PowerPerImprovement × improvementCount(track)` pulled fresh from `IProgressionSystem` — the INV-24 contribute-on-read fold, same pattern as equipment and ability contributors.

## Where to look

- [`Core/Modules/Progression/Handlers/ExperienceAwardHandler.cs`](../../../Core/Modules/Progression/Handlers/ExperienceAwardHandler.cs) — the entry point.
- [`Core/Modules/Progression/Systems/ProgressionSystem.cs`](../../../Core/Modules/Progression/Systems/ProgressionSystem.cs) · [`Core/Modules/Progression/ProgressionEffectContributor.cs`](../../../Core/Modules/Progression/ProgressionEffectContributor.cs) · [`Core/Modules/Progression/ProgressionConstants.cs`](../../../Core/Modules/Progression/ProgressionConstants.cs)
- [`../../features/progression/progression.md`](../../features/progression/progression.md) — the feature; [`progression-system.md`](../../features/progression/progression-system.md) for the system internals.
- [flow-20](flow-20-mob-death-respawn.md) — the mob-death fan-out this handler is one of three subscribers of.
