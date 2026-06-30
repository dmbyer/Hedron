# Combat System

> Domain system for melee round resolution: target lookup, combat state management, hit/damage formula, and aspect-resolved outcome. **Authoring checkpoint:** slice 9 (aspect integration: slice 11-d). Living document.

## What it is / does

`CombatSystem` is a **domain-tier pure system** that owns combat round resolution. It resolves who a player can fight in a room, attaches and removes the `CombatStateComponent` that pairs two combatants, computes each round's hit check and damage, applies aspect math, mutates HP, and returns a structured result. It never publishes events, never calls persistence (INV-5, INV-8), and never calls `IEntityStateService` — state-flag coordination is the command/handler's job, not the round engine's.

## How it works

### Target lookup

`TryFindTargetInRoom(roomEntityId, token)` prefix-matches `token` against `MobDataComponent.Name` and `Keywords` for every entity with `MobDataComponent` in the given room. First match wins.

### Combat state lifecycle

`StartCombat(attackerId, defenderId)` attaches `CombatStateComponent { OpponentEntityId }` to both entities — the metadata that `CombatTickHandler` uses to find active pairs each tick. `EndCombat` removes the component from both. `CombatStateComponent` is not `[Persistent]` — a crash or restart drops all active combat; mobs respawn at full HP, players reconnect with their last-flushed HP.

### Round deduplication

`CombatTickHandler` processes a pair only when `entityId < opponentEntityId`. This prevents A→B and B→A from each running as separate rounds per tick. The lower entity id is designated the attacker for ordering; the defender counterstacks in the same tick.

### Hit and damage formula

Hit check: `Random.Next(1,21) + Body/2 >= 10 + defense`. If hit: raw damage = `Random.Next(1, attackPower+2)`. Both `attackPower` and `defense` are read from `IStatSystem`. `IRandom` is the injected seam (INV-26) — no `Random.Shared` calls in the system.

### Aspect resolution

After raw damage is computed, `IAspectSystem.Affinity(attackerId)` returns the attacker's outgoing `AspectComposition` (empty if no `AspectAffinitiesComponent`). `IAspectSystem.Resolve(rawDamage, composition, attackerId, defenderId)` applies the attacker's per-aspect affinity boost and the defender's independent per-aspect resistance, returning the final damage that is passed to `IAttributeSystem.SetCurrentHp`. `CombatRoundResult.AspectComposition` is the point-in-time capture of the composition used (null = untyped).

### Ability strikes

`ResolveAbilityStrike(attackerId, defenderId, basePower, composition?)` is the seam for offensive abilities. It skips the hit/miss roll (abilities don't miss in the current model) and accepts a caller-supplied composition instead of reading the entity's affinity. `AbilityInvocationPipeline` uses this after `IAbilitySystem.Activate` returns an `OffensivePower`.

### Outcomes

`CombatRoundOutcome`: `Hit`, `Miss`, `MobDied` (HP ≤ 0 for a `MobDataComponent` entity), `PlayerIncapacitated` (HP ≤ 0 for a `CharacterComponent` entity). `CombatTickHandler` reads the outcome and routes accordingly — it publishes events; the system returns results.

## Interface

- [`ICombatSystem.cs`](../../../Core/Modules/Combat/Systems/ICombatSystem.cs) — `TryFindTargetInRoom`, `StartCombat`, `EndCombat`, `ExecuteRound`, `ResolveAbilityStrike`. Pure: returns `CombatRoundResult`; never touches the bus or persistence.

## Considerations

- **`CombatStateComponent` is not `[Persistent]`** (INV-14 by exclusion) — see [combat.md](combat.md) § How it works.
- **`ICombatSystem` does not call `IEntityStateService`** — a cohesion choice (Domain→Domain is permitted per INV-2), not an invariant obligation. State-flag coordination belongs in the commands and handlers that bridge peer domain services.
- **`DefenderName` point-in-time capture.** `CombatTickHandler` reads `MobDataComponent.Name` from the mob entity **before** publishing `CombatEndedEvent(MobDied)` so `CombatHandler` can render the kill narrative from the payload without accessing a destroyed entity.
- **`flee` always succeeds.** No fail-chance roll. A skills slice can add a chance-to-fail mechanic if needed.
- **No mob aggro.** `StartCombat` is a public seam; a future mob-AI tick calls it directly when aggro conditions are met.

## Extensibility

- **Armor / weapon stat contribution** — landed via `ItemDataComponent.StatBonuses` + `EquipmentEffectContributor` (INV-24); combat reads `IStatSystem.Get(AttackPower|Defense)`, which folds the worn-gear bonuses. New weapon/armor types are pure data — no combat code change.
- **Weapon types / dual-wielding** — distinct weapon stat profiles are authored bonus rows; `OffHand` already exists for a second wielded item when a dual-wield use-case lands.
- **Group combat** — `CombatStateComponent` is a one-to-one opponent reference. Group combat requires either a list reference or multiple components; combat tick deduplication logic changes accordingly.
- **`MobInRoomResolver`** was extracted to the shared `Core/Modules/Mobs/Resolvers/` home in slice 12-c and is now bound as the optional `shopkeeper` argument resolver on the shopping `list` command (its one active consumer). `KillCommand` and `AbilityInvocationPipeline` still resolve room mobs via the inline `ICombatSystem.TryFindTargetInRoom` path; migrating both onto the resolver — which genuinely crosses the INV-19 ≥3-consumer threshold — is [backlogged](../../roadmap/backlog.md). It remains registered in `CombatModule` to preserve DI composition order.

## Related

- [`combat.md`](combat.md) — the holistic feature view and player surfaces.
- [Combat journey](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) — the runtime path for initiation, round pulse, and flee.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `CombatSystem`/`ICombatSystem`, `CombatStateComponent` catalog rows.
- [`../../roadmap/completed/slice-9-combat.md`](../../roadmap/completed/slice-9-combat.md) — as-built record and notable design decisions.
- **Character stats** (not yet migrated) — `IStatSystem` is the stat aggregation seam; see the `StatSystem` row in [`../../reference/systems.md`](../../reference/systems.md).
- [`../effects/effect-system.md`](../effects/effect-system.md) — effect modifiers feed into `IStatSystem.Get`; combat reads effect-modified stats transparently.
