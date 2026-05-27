# Phase 3 slice 9-c — Stat computation system (completed)

> Implemented on branch `claude/confident-gates-534273`. Full feature spec: [`../../use-cases/stat-system.md`](../../use-cases/stat-system.md).

## Outcome

The codebase now has a stable effective-stat read seam (`IStatSystem`) that aggregates base attributes and equipment bonuses into a single interface for the combat slice and future consumers. `GetEffectiveAttackPower` returns `Strength / 2 + MainHand item DamageBonus`, reading `EquipmentComponent.Slots[WornSlot.MainHand]` via a direct dictionary lookup (no list allocation, no `is`/`as` casts). `IAttributeSystem` gains `SetCurrentHp`, the write seam the combat slice will use to apply damage — clamping `[0, MaxHp]` is enforced inside the system, not at each call site. `ItemDataComponent.DamageBonus` carries authored weapon power, round-tripped through YAML load, `setitem dmg <n>` save, and template/entity sync.

## Shipped pieces

| Surface | Location |
|---|---|
| `IStatSystem` — interface: `GetEffectiveStrength`, `GetEffectiveDexterity`, `GetEffectiveConstitution`, `GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp`, `GetMaxHp` | `Core/Modules/Stats/Systems/IStatSystem.cs` |
| `StatSystem` — implementation; delegates base stats to `IAttributeSystem`; reads `EquipmentComponent.Slots[WornSlot.MainHand]` + `ItemDataComponent.DamageBonus` for attack power; returns `dexterity / 4` for defense | `Core/Modules/Stats/Systems/StatSystem.cs` |
| `StatsModule` — `AddStatsModule(IServiceCollection)` DI extension; registers `IStatSystem`/`StatSystem` as singleton | `Core/Modules/Stats/StatsModule.cs` |
| `IAttributeSystem.SetCurrentHp` — new method; clamped write to `PoolsComponent.CurrentHp`; clamp rule `[0, MaxHp]` owned by the system (INV-8) | `Core/Modules/Attributes/Systems/IAttributeSystem.cs` |
| `AttributeSystem.SetCurrentHp` — implementation; reads max via `GetMaxHp`, applies `Math.Clamp`, writes `PoolsComponent.CurrentHp` | `Core/Modules/Attributes/Systems/AttributeSystem.cs` |
| `ItemDataComponent.DamageBonus: int` — new field (default 0); included in persistence snapshot via existing class-level `[Persistent]` | `Core/ECS/Components/ItemDataComponent.cs` |
| `ItemTemplate.DamageBonus: int` — new property (default 0); passed through `Apply` to `ItemDataComponent` | `Core/Modules/Items/Templates/ItemTemplate.cs` |
| `ItemTemplateDeserializer` — reads optional `damageBonus: int` YAML key (absent = 0) | `Core/Modules/Items/ItemTemplateDeserializer.cs` |
| `ItemContentWriter` — writes `damageBonus` field to YAML DTO on every `WriteAsync` call | `Core/Modules/Items/Systems/ItemContentWriter.cs` |
| `IItemBuilderSystem.SetItemDamageBonus` — new method; mutates both `ItemDataComponent.DamageBonus` and `ItemTemplate.DamageBonus` atomically | `Core/Modules/Items/Systems/IItemBuilderSystem.cs` |
| `ItemBuilderSystem.SetItemDamageBonus` — implementation; follows the same component + template pattern as the other setters | `Core/Modules/Items/Systems/ItemBuilderSystem.cs` |
| `SetitemCommand` — extended with `dmg` property case; validates non-negative int; calls `IItemBuilderSystem.SetItemDamageBonus`; publishes `ItemPropertySetByAdminEvent(PropertyName="damageBonus")`; writes YAML; saves entity | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| `Program.cs` — `services.AddStatsModule()` added after `AddAttributesModule()` | `Server/Program.cs` |
| `docs/reference/systems.md` — `IAttributeSystem` entry updated with `SetCurrentHp`; `IStatSystem`/`StatSystem` entry added; `IItemBuilderSystem` entry updated with `SetItemDamageBonus` | `docs/reference/systems.md` |
| `docs/reference/components.md` — `ItemDataComponent` row updated to include `DamageBonus: int` | `docs/reference/components.md` |
| `docs/use-cases/stat-system.md` — status set to `implemented`; open questions marked resolved; trimmed to durable spec | `docs/use-cases/stat-system.md` |
| `docs/use-cases/README.md` — status updated to `implemented` | `docs/use-cases/README.md` |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation (use-case doc authored as part of the slice 9 planning batch).

**Code-mode gate:** Run before merge (see architecture-reviewer output). No blocking findings.

## Notable design points

- **Direct `EquipmentComponent` access, not `IEquipmentSystem.GetEquippedItems`.** `StatSystem` reads `EquipmentComponent.Slots[WornSlot.MainHand]` directly via `EntityService.TryGet<EquipmentComponent>`. This avoids the `IReadOnlyList<uint>` allocation that `GetEquippedItems` would return and is a single dictionary lookup — the cheapest possible path for a per-combat-round hot call.

- **`IStatSystem` has no setter methods.** Pure aggregation only. All writes go through `IAttributeSystem` (HP mutation) or `IEquipmentSystem` (slot mutation). The interface can be extended with new getter overloads when `ActiveEffectsComponent` lands; no callers need to change.

- **`SetCurrentHp` clamp ownership (INV-8).** The `[0, MaxHp]` rule is enforced inside `AttributeSystem.SetCurrentHp`. The combat slice's `ICombatSystem.ExecuteRound` will pass the raw computed value; the attribute system is the single enforcement point. No caller needs to implement the clamp.

- **`DamageBonus` on `ItemDataComponent`, not a separate `CombatItemDataComponent`.** The earlier `combat.md` draft proposed `CombatItemDataComponent.WeaponDamageBonus`. This slice adopts `ItemDataComponent.DamageBonus` instead: the field is authored alongside the item's other stats, the YAML file is already the item authoring surface, and separating it would require two write paths for one `setitem dmg` command. `combat.md` was already updated before implementation began.

- **`StatsModule` is a thin DI registration.** No hosted service, no handlers, no config binding — just `services.AddSingleton<IStatSystem, StatSystem>()`.

- **`GetEffectiveDefense` is dexterity-only.** Returns `dexterity / 4`. Armor-slot contribution is acknowledged future debt — when armor lands, `StatSystem.GetEffectiveDefense` sums armor bonuses here without changing the interface.

## Deviations from the use-case doc

None. All postconditions satisfied as written. The open questions were resolved as recommended: direct `EntityService` access for `GetEffectiveAttackPower`; `IItemBuilderSystem.SetItemDamageBonus` added following the setter pattern rather than inline mutation.

## Follow-ups unlocked

- **Slice 9 — Combat.** All three prerequisites (9-a, 9-b, 9-c) are now complete. `ICombatSystem.ExecuteRound` can call `IStatSystem.GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp`, and `IAttributeSystem.SetCurrentHp` without any further infrastructure work.
- **Future modifier sources.** When `ActiveEffectsComponent` or auras land, `StatSystem` sums their contributions into the existing getter implementations without touching the interface or any consumer.
- **Armor defense contribution.** `GetEffectiveDefense` returns `dexterity / 4` today; an armor-slot pass can extend this inline when equipment slots carry defense ratings.
