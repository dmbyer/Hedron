# Phase 3 slice 9 — Combat (completed)

> Implemented on branch `claude/heuristic-blackwell-2b63c2`. Full feature spec: [`../../implementation-plans/combat.md`](../../implementation-plans/combat.md).

## Outcome

The codebase now has a working core melee loop. A player types `kill <mob>` to initiate combat; `KillCommand` validates state, resolves the target via prefix-match, transitions both entities to `InCombat` via `IEntityStateService`, attaches `CombatStateComponent` on both via `ICombatSystem.StartCombat`, and publishes `CombatStartedEvent`. Each `HeartbeatTickEvent` tick drives `CombatTickHandler`, which processes all active pairs (deduplicated by lower entity id), calls `ICombatSystem.ExecuteRound` for each, and publishes `CombatRoundEvent`. Terminal outcomes are handled inline: mob death publishes `CombatEndedEvent(MobDied)` so `CombatHandler` (priority 20) broadcasts the kill narrative before `CombatMobDeathHandler` (priority 80) clears the blueprint slot and destroys the mob entity. The `flee` command always succeeds and exits combat with `CombatEndedEvent(PlayerFled)`. Player incapacitation stubs HP to 1 and ends combat, publishing `CombatEndedEvent(PlayerIncapacitated)` for slice 10 to consume.

## Shipped pieces

| Surface | Location |
|---|---|
| `CombatStateComponent` — `OpponentEntityId: uint`; not `[Persistent]`; cross-cutting transient; companion to `EntityState.InCombat` | `Core/ECS/Components/CombatStateComponent.cs` |
| `CombatStartedEvent` — `AttackerEntityId, DefenderEntityId, RoomEntityId` | `Core/Modules/Combat/Events/CombatStartedEvent.cs` |
| `CombatRoundEvent` — `AttackerEntityId, DefenderEntityId, RoomEntityId, CombatRoundResult` | `Core/Modules/Combat/Events/CombatRoundEvent.cs` |
| `CombatEndedEvent` — `AttackerEntityId, DefenderEntityId, Outcome, RoomEntityId, DefenderName?`; `CombatEndOutcome` enum (`MobDied`, `PlayerIncapacitated`, `PlayerFled`) | `Core/Modules/Combat/Events/CombatEndedEvent.cs` |
| `ICombatSystem` — `TryFindTargetInRoom`, `StartCombat`, `EndCombat`, `ExecuteRound`; `CombatRoundResult` record; `CombatRoundOutcome` enum | `Core/Modules/Combat/Systems/ICombatSystem.cs` |
| `CombatSystem` — implementation; prefix-match target lookup; `CombatStateComponent` add/remove; hit-check and damage formula via `IStatSystem`; `IAttributeSystem.SetCurrentHp` for damage application; outcome detection via `HasComponent<MobDataComponent>` / `HasComponent<CharacterComponent>` | `Core/Modules/Combat/Systems/CombatSystem.cs` |
| `CombatTickHandler` — priority 20; subscribes `HeartbeatTickEvent`; snapshot + deduplicate; `ExecuteRound` per pair; publishes `CombatRoundEvent`; handles `MobDied` and `PlayerIncapacitated` terminal outcomes inline | `Core/Modules/Combat/Handlers/CombatTickHandler.cs` |
| `CombatHandler` — priority 20; subscribes `CombatStartedEvent`, `CombatRoundEvent`, `CombatEndedEvent`; pure output fan-out via `IBroadcastSystem` | `Core/Modules/Combat/Handlers/CombatHandler.cs` |
| `CombatMobDeathHandler` — priority 80; subscribes `CombatEndedEvent`; acts only on `MobDied`; calls `IEntityStateService.ExitState(InCombat)` on attacker; removes `BlueprintComponent` (INV-21); calls `EntityService.DestroyEntity` | `Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs` |
| `KillCommand` — verb `kill`, alias `k`, `Partial`; `RestOfLine "target"`; in-combat guard; target resolution; `TryEnterState(InCombat)` on both; `StartCombat`; publishes `CombatStartedEvent` | `Core/Modules/Combat/Commands/KillCommand.cs` |
| `FleeCommand` — verb `flee`, `Partial`; not-in-combat guard; reads `CombatStateComponent.OpponentEntityId`; `EndCombat` + `ExitState(InCombat)` on both; publishes `CombatEndedEvent(PlayerFled)` | `Core/Modules/Combat/Commands/FleeCommand.cs` |
| `CombatModule` — `AddCombatModule(IServiceCollection)` DI extension; registers `ICombatSystem`, three handlers, two commands | `Core/Modules/Combat/CombatModule.cs` |
| `AdminAuditHandler` — extended with `IEventHandler<CombatEndedEvent>`; logs `CombatEnded` with attacker/defender ids and outcome | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| `Program.cs` — `services.AddCombatModule()`; subscribes `CombatTickHandler` to `HeartbeatTickEvent`; `CombatHandler` to all three combat events; `CombatMobDeathHandler` to `CombatEndedEvent`; `AdminAuditHandler` to `CombatEndedEvent` | `Server/Program.cs` |
| `docs/reference/components.md` — `CombatStateComponent` row added | `docs/reference/components.md` |
| `docs/reference/systems.md` — `ICombatSystem`/`CombatSystem` entry added | `docs/reference/systems.md` |
| `docs/reference/handlers.md` — `CombatTickHandler`, `CombatHandler`, `CombatMobDeathHandler` entries added; `AdminAuditHandler` entry updated | `docs/reference/handlers.md` |
| `docs/reference/commands.md` — `kill` and `flee` entries added | `docs/reference/commands.md` |
| `docs/implementation-plans/combat.md` — status set to `implemented`; trimmed to durable spec | `docs/implementation-plans/combat.md` |

**Validation fix additions (commit 3 — found during PR validation):**

| Surface | Location |
|---|---|
| `EntityArchetype` — enum of 16 values (`Unknown`, `Player`, `Mob`, `Weapon`, `Armor`, `Potion`, `StaticItem`, `Consumable`, `Room`, `Area`, `World`, `Storage`, `Inventory`, `Portal`, `Trigger`, `Custom`) | `Core/ECS/EntityArchetype.cs` |
| `ArchetypeDefinition` — declarative required/optional component lists for one archetype; `sealed class` with `Required` and `Optional` | `Core/ECS/ArchetypeDefinition.cs` |
| `IArchetypeRegistry` — `RequiredComponents`, `OptionalComponents`, `Validate`, `Detect`, `MissingRequired` | `Core/ECS/IArchetypeRegistry.cs` |
| `ArchetypeRegistry` — implements `IArchetypeRegistry`; `BuildDefinitions` (Mob, Player, Room, Area, StaticItem); `BuildDetectionOrder` (Mob→Player→Room→Area→StaticItem) | `Core/ECS/ArchetypeRegistry.cs` |
| `MobBuilderSystem.CreateMob` — extended to attach `AttributesComponent` + `PoolsComponent`; previously absent, causing `SetCurrentHp` to silently discard damage on admin-built mobs | `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` |
| `CombatTickHandler` — extended with counterattack `else` branch: defender calls `ExecuteRound` back against attacker each tick; `PlayerIncapacitated` from the counterattack handled symmetrically | `Core/Modules/Combat/Handlers/CombatTickHandler.cs` |
| `CombatHandler` — extended: injects `IStatSystem`; emits per-round HP status `[You: X/Y HP \| <mob>: X/Y HP]` to the player after every round (hit and miss) | `Core/Modules/Combat/Handlers/CombatHandler.cs` |
| `CombatRoundResult` / `CombatRoundOutcome` — moved from `ICombatSystem.cs` (Systems namespace) to `Core/Modules/Combat/CombatRoundResult.cs` (Combat namespace) to remove the Events→Systems coupling | `Core/Modules/Combat/CombatRoundResult.cs` |
| `WorldContentLoader.MigrateEntityComponentsAsync` — archetype-aware startup/reload migration; fills missing required components across all five registered archetypes; persists each modified entity; never removes extra components | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `Program.cs` — `services.AddSingleton<IArchetypeRegistry, ArchetypeRegistry>()` registration added | `Server/Program.cs` |
| `docs/reference/archetypes.md` — "not yet built" banner replaced with implementation-status note; table updated to show as-built required components for ✓ archetypes; planned-only rows marked | `docs/reference/archetypes.md` |
| `docs/reference/systems.md` — `IArchetypeRegistry`/`ArchetypeRegistry` entry added; `WorldContentLoader` entry updated with new dependencies and `MigrateEntityComponentsAsync` | `docs/reference/systems.md` |
| `docs/architecture/flows/flow-01-server-startup.md` — `MigrateEntityComponentsAsync` step added to mermaid and step 7 | `docs/architecture/flows/flow-01-server-startup.md` |
| `docs/architecture/flows/flow-05-content-reload.md` — `MigrateEntityComponentsAsync` step added to mermaid and steps (new step 10; existing 10–12 renumbered to 11–13) | `docs/architecture/flows/flow-05-content-reload.md` |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation (use-case doc authored as part of the slice 9 planning batch and reviewed by `architecture-reviewer` in spec mode).

**Code-mode gate:** Run twice — once before merge (no blocking findings) and once after validation fixes (commit 3) were applied (verdict: APPROVE WITH NITS; all nits resolved in the same commit batch).

## Notable design points

- **Two-layer state model.** `EntityStateComponent` (slice 9-a) holds the `InCombat` flag; `CombatStateComponent` holds the opponent entity id. Commands gate on the flag via `IEntityStateService`; combat logic queries the metadata via `CombatStateComponent`. `ICombatSystem` does not call `IEntityStateService` — a cohesion choice, not an INV-2 obligation (Domain → Domain is permitted; the separation keeps `IEntityStateService` lateral peer coordination in commands and handlers where it belongs).

- **Round deduplication.** Lower entity id is designated the "attacker" in the pair ordering. `CombatTickHandler` processes the pair only when `entityId < opponentEntityId`, preventing A→B and B→A from both running in a single tick.

- **`CombatEndedEvent.DefenderName` point-in-time capture.** `CombatTickHandler` reads `MobDataComponent.Name` from the mob entity **before** publishing `CombatEndedEvent(MobDied)`. This guarantees `CombatHandler` can render the death narrative from the payload without re-reading a potentially destroyed entity.

- **`CombatStateComponent` is not `[Persistent]`.** A crash or restart drops all active combat. Players reconnect with HP at the last periodic flush; mobs re-spawn from templates on next startup or `reload`. This avoids orphaned opponent references.

- **Blueprint slot freed before `DestroyEntity` (INV-21).** `CombatMobDeathHandler` calls `EntityService.RemoveComponent<BlueprintComponent>(mobEntityId)` before `EntityService.DestroyEntity(mobEntityId)`. This makes the invariant visible in code; `DestroyEntity` removes all components anyway, so the slot is freed either way.

- **Player death stubbed.** When the player's HP hits 0, it is clamped to 1 and combat ends with `CombatEndedEvent(PlayerIncapacitated)`. No death penalty, no corpse, no respawn. Slice 10 subscribes to this event without needing payload changes.

- **`flee` always succeeds.** No fail-chance roll in Phase 3. The skills slice can add a chance-to-fail mechanic if needed.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 10 — Death and respawn.** `CombatEndedEvent(PlayerIncapacitated)` is already shaped and published. Slice 10 subscribes to it and adds corpse, death penalty, and respawn mechanics.
- **Mob aggro.** `StartCombat` is a public system call. A future mob-AI tick can call it directly when aggro conditions are met, without changing the combat loop.
- **Loot drop.** `CombatMobDeathHandler` has the right priority and the mob's entity id — the loot drop path plugs in here (or in a new higher-priority handler on `CombatEndedEvent(MobDied)`).
- **Armor defense contribution.** `IStatSystem.GetEffectiveDefense` is the single extension point; no combat code changes when armor-slot bonuses are added.
