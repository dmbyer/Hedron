# Components Reference

Living catalog of the ECS components **implemented** in Hedron. Update this file whenever a component is added, removed, or materially changed.

Source of truth: `Core/ECS/Components/` (cross-cutting) and `Core/Modules/<Feature>/Components/` (feature-owned).

> The full target component model (Identity, Transform, Attributes, Pools, item/room/area data, …) is design intent for future slices and lives in [`components-planned.md`](components-planned.md) — do not assume those types exist. Shape-level documentation only; for design rules (archetypes, persistence, effects) see [../architecture/02-ecs.md](../architecture/02-ecs.md). Why implemented and planned are separated: [`../architecture/09-documentation.md`](../architecture/09-documentation.md).

---

## Persistence — two-level model

Persistence uses two independent opt-ins. See [../architecture/06-persistence.md](../architecture/06-persistence.md) for the full model.

**Level 1 — entity opt-in:** an entity is saved only if it carries the `PersistentEntity` marker component. Entities without it are never written to disk, regardless of which other components they have.

**Level 2 — component inclusion:** `[Persistent]` on a component *type* tells `PersistenceSystem` to include that component in the snapshot for entities that are already opted in. The **Persisted?** column below marks which components carry the attribute.

---

## Implemented components

### Infrastructure (cross-cutting — `Core/ECS/Components/`)

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `PersistentEntity` | *(zero-data marker)* — opts the entity into persistence | any entity that must survive restart | yes (self-referential: the marker is `[Persistent]` so it round-trips) |
| `BlueprintComponent` | `BlueprintId : string` — records the authored template id this entity was spawned from | every templated entity (Phase 3 slice 2+) | yes |
| `AreaComponent` | `AreaId`, `Name`, `Description`, `RespawnRate`, `Pvp` — minimal area metadata seeded by `AreaTemplate` | Area entities (Phase 3 slice 2) | no — world content; `PersistentEntity` never applied ([INV-23](../architecture/checklist.md)); see [`../features/world/area-model.md`](../features/world/area-model.md) |
| `EntityStateComponent` | `ActiveStates: EntityStateFlags` — tracks active state flags for any entity; absent when `ActiveStates == None` | `IEntityStateService`; commands and handlers that guard on entity state | no — transient; cleared on restart by design |
| `EntityStateFlags` | `[Flags]` enum `{ None=0, InCombat=1, Resting=2, Incapacitated=4 }` — co-located with `EntityStateComponent` in `Core/ECS/Components/`; named with `Flags` suffix to avoid a C# namespace/type collision with the `EntityState` module | n/a (enum, not a component) | n/a |
| `CombatStateComponent` | `OpponentEntityId: uint` — holds the entity id of this entity's current combat opponent. Companion to `EntityState.InCombat`: the flag records that combat is active; this component records who the entity is fighting. Absent when not in combat. | `ICombatSystem`, `FleeCommand`, `CombatTickHandler` | no — transient; stale combat state on restart would reference entities that may not exist |
| `SpawnConfigComponent` | `Rules: List<SpawnRule>` — each `SpawnRule` holds `BlueprintId`, `MinCount`, `MaxCount`, `RespawnDelaySeconds`; one entry per independent respawn slot. Absent if the room has no spawn rules. | Room/area entities; `SpawnSystem` reads on `WorldContentReadyEvent` to populate its tracker | no — YAML is the authoritative source; always attached via `RoomTemplate.Apply` |

### Gameplay / session (cross-cutting — `Core/ECS/Components/`)

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `PlayerComponent` | `DisplayName`, `Session` (transient ref) | Player entity | no |
| `LocationComponent` | `RoomEntityId` (`uint`, `[JsonIgnore]` — runtime entity ID resolved at startup from blueprint) + `RoomBlueprintId` (`string?` — stable cross-restart room reference stored in SQLite). Updated by `IMovementSystem` on every successful move; see [`../features/world/movement-system.md`](../features/world/movement-system.md). | any mobile entity | yes (`[Persistent]` on class; `RoomEntityId` excluded via `[JsonIgnore]`, `RoomBlueprintId` included) |
| `RoomComponent` | `Name`, `Description`, `Exits` (`Dictionary<Direction, uint>`), `AreaEntityId` (`uint`, runtime-only, default 0 — set by `WorldContentLoader.LinkRoomAreas` on startup and `IAreaSystem.AssignRoomToArea` at runtime; NOT `[Persistent]` — durable form is `RoomTemplate.AreaId` in YAML) | Room entity | no — world content; `PersistentEntity` never applied ([INV-23](../architecture/checklist.md)); see [`../features/world/area-model.md`](../features/world/area-model.md) |
| `ItemDataComponent` | `Name`, `Description`, `Keywords` (`List<string>`), `ItemType`, `WornSlots` (`List<WornSlot>?` — null/empty = not wearable), `StatBonuses` (`List<EquipmentStatBonus>` — authored worn-gear stat contributions, derived on read as `WhileEquipped` StatModifiers by `EquipmentEffectContributor`; empty = none), `Value` (`long` — intrinsic base value in base-unit Coin; `0` = valueless/non-saleable; prices are derived from this at read time by consumers, never stored) | Item entity; read by `BroadcastSystem` (room description), `ItemSystem` / `ItemBuilderSystem` / `EquipmentSystem`, and `EquipmentEffectContributor` (stat fold) | yes |
| `EquipmentStatBonus` | `record(ScoreId TargetScore, int Magnitude)` — one signed worn-gear bonus row held in `ItemDataComponent.StatBonuses`; pure data (INV-3) | element of `ItemDataComponent.StatBonuses` | yes (rides `ItemDataComponent`) |
| `InventoryComponent` | `ItemEntityIds` (`List<uint>`) — item entity ids carried by this entity | Player/mob entities; items in inventory have **no** `LocationComponent` — tracked here exclusively | yes |
| `EquipmentComponent` | `Slots` (`Dictionary<WornSlot, uint>`) — maps each occupied slot to an item entity id | Player/mob entities; cross-cutting so future mob code carries it without a domain dependency | yes |
| `MobDataComponent` | `Name`, `Description`, `Keywords` (`List<string>`), `MobType`, `TierBand: int` (0–6, 0 = unbanded; Ascension tier-band content tag, prog-2) — cross-cutting so combat, AI, and dialogue modules can read mob names without a domain dependency | Mob entities; read by `BroadcastSystem` (room description) | yes (component-level), but `TierBand` never reaches a snapshot in practice — mobs are world content (no `PersistentEntity`); durable form is the `MobTemplate` YAML, re-applied on spawn |
| `MobType` | Enum `{ None, Vendor, Guard, Creature }` — classification only; no behavior routing in this slice | Co-located with `MobDataComponent` in `Core/ECS/Components/` | n/a (enum, not a component) |
| `ProtectionComponent` | `Flags: ProtectionFlags` — two-axis protection flags for any entity. Absent when flags are `None` (unprotected). World-content component for mobs (authored via mob YAML / `setmob protection`); also applicable to players (future). | `ICombatSystem.CanBeAttacked`, `EffectSystem.Apply` (Gate A/B reads); `MobTemplate.Apply` (seed); `IMobBuilderSystem.SetMobProtection` (author) | **no** — world content (INV-23); durable form is `MobTemplate.Protection` YAML; mobs never carry `PersistentEntity` |
| `ProtectionFlags` | `[Flags]` enum `{ None=0, Untargetable=1, EffectImmune=2 }` — `Untargetable`: entity cannot be the target of a melee or ability attack; `EffectImmune`: entity rejects ALL effects (beneficial and harmful alike). Co-located with `ProtectionComponent` in `Core/ECS/Components/ProtectionComponent.cs`. | n/a (enum, not a component) | n/a |
| `AttributesComponent` | `Level: int`, `Mind: int`, `Body: int`, `Spirit: int`, `Attunement: int` — base attributes for any living entity; defaults Level 1, all attributes 10. `Level` is vestigial (superseded by Ascension tier in slice S8). Read/written via `IAttributeSystem`; see [`../features/character-stats/attribute-system.md`](../features/character-stats/attribute-system.md). | Player and mob entities | yes |
| `PoolsComponent` | `MaxHp: int`, `CurrentHp: int`, `MaxMana: int`, `CurrentMana: int`, `MaxStamina: int`, `CurrentStamina: int`, `MaxAstra: int`, `CurrentAstra: int` — four resource pools (HP/Mana/Stamina/Astra). `SetMaxX` clamps `CurrentX` to new `MaxX` (INV-8). Governance (Mana↔Mind, Stamina↔Body, Astra↔Attunement) recorded in `IStatRegistry`; derivation is a later progression concern. Read/written via `IAttributeSystem`; see [`../features/character-stats/attribute-system.md`](../features/character-stats/attribute-system.md). | Player and mob entities | yes |
| `ResourceType` | Enum `{ Hp, Mana, Stamina, Astra }` — identifies a resource pool by type; the expandable seam for future pools. Co-located with `PoolsComponent` in `Core/ECS/Components/`. | `IStatRegistry` governance metadata; future effect/ability consumers | n/a (enum, not a component) |

### Module-owned (`Core/Modules/<Feature>/Components/`)

| Module | Component | Purpose | Persisted? |
|---|---|---|---|
| Account | `AccountComponent` | `Username` (lowercase-normalized), `PasswordHash` (PBKDF2-SHA256), `CharacterEntityIds`, `CreatedAtUtc` | yes |
| Account | `CharacterComponent` | `AccountEntityId`, `CharacterName`, `CreatedAtUtc`, `LastLoginUtc` | yes |
| Effects | `EffectsComponent` | `List<Effect> Effects` — active timed/permanent effects on an entity. `[Persistent]`, lifetime-filtered `JsonConverter` (`EffectsComponentJsonConverter`) serializes only `UntilRemoved` effects — timed effects are transient by design. Located in `Core/ECS/Components/` (cross-cutting). | yes |
| Death | `RespawnComponent` | `RoomBlueprintId: string?` — stable blueprint id of the room where this entity respawns after death. `null` means "use the world starting room" (fallback in `IDeathSystem.Respawn`). Stores the blueprint id rather than the runtime entity id because room entity ids are not stable across restarts. Attached to every new character by `AccountSystem.CreateCharacterAsync`. Set by `SetRespawnCommand` (admin boundary save, INV-22). Located in `Core/ECS/Components/` (cross-cutting). | yes |
| Abilities | `AbilitiesComponent` | `Known: List<string>` — ability ids the entity has learned. `CooldownRemaining: Dictionary<string, float>` — per-ability cooldown in seconds remaining. `[Persistent]` with `AbilitiesComponentJsonConverter`: `Known` is durable and persists across restarts; `CooldownRemaining` is transient (resets to ready on load). Located in `Core/ECS/Components/` (cross-cutting). | yes (`Known` only — `CooldownRemaining` excluded by converter) |
| Aspects | `AspectAffinitiesComponent` | `AffinityWeights: Dictionary<AspectId, int>` — normalized aspect composition for outgoing damage typing (empty, or positive weights summing to 100). `BaseResistances: Dictionary<AspectId, int>` — independent per-aspect base resistance [0, 100] (100 = full immunity). Both are compute-on-read via `IAspectSystem` — not cached. Serialized by `AspectId` name (never ordinal, INV-23) via the global `JsonStringEnumConverter`. Attached empty to every new character by `AccountSystem.CreateCharacterAsync`. Located in `Core/ECS/Components/` (cross-cutting). Co-located `AspectId` (enum) and `AspectCategory` (enum) are in `Core/Modules/Aspects/AspectId.cs`. See [`../features/aspects/aspect-system.md`](../features/aspects/aspect-system.md) for the affinity/resistance model. | yes |
| Economy | `WalletComponent` | `Balances: Dictionary<CurrencyId, long>` — currency balances in base units, keyed by `CurrencyId`. Absent keys = zero balance; values always ≥ 0. Created on first deposit by `IWalletSystem.Deposit`. Entity-keyed (any holder: player, vendor till, bank vault). Keys serialized by enum name via the global `JsonStringEnumConverter` (ordinal-safe under future `CurrencyId` reordering). Located in `Core/Modules/Economy/Components/`. | yes |
| Economy | `CurrencyLootComponent` | `Ranges: Dictionary<CurrencyId, (int Min, int Max)>` — per-currency loot drop range in base units (copper). Applied by `MobTemplate.Apply` only when at least one range has `Max > 0` (opt-in default: absent component or zero max = no drop). World content authored via YAML / Blazor editor; YAML is the durable form. Located in `Core/Modules/Economy/Components/`. | **no** — world content (INV-23); mobs never carry `PersistentEntity` |
| Shopping | `ShopComponent` | `AcceptedCurrency: CurrencyId`, `TillSeed: long`, `RatioOverride: decimal?` (deferred — unused), `BaseStock: List<ShopStockRow>` (`(BlueprintId, Quantity)`). Presence = "this mob trades" (`HasComponent`, INV-4). Added by `MobTemplate.Apply` when `IsShop`; authored via `IMobBuilderSystem.SetMobShop` / `setmob shop` / Blazor `MobEditor` / `shop:` YAML. Located in `Core/Modules/Shopping/Components/`. | **no** — world content (INV-23); durable form is `MobTemplate` shop YAML |
| Shopping | `ShopStockComponent` | `Provenance: StockProvenance { Base, Acquired }`, `DateTime? ExpiresAt`. Per-item provenance marker on each shop-held item — `Base` stamped at spawn/restock, `Acquired` + `ExpiresAt` stamped by the sell flow. Runtime-transient; not authored directly. Co-located `StockProvenance` enum. Located in `Core/Modules/Shopping/Components/`. | **no** — runtime-transient world state; base items re-spawn fresh, acquired items intentionally dropped on restart |
| Progression | `ProgressionComponent` | `Xp: Dictionary<ScoreId, int>` (cumulative, never decremented), `Improvements: Dictionary<ScoreId, int>` (thresholds crossed per track) — a track is keyed directly by `ScoreId`, no parallel key type. The derived power step is **not** stored here — pulled on read by `ProgressionEffectContributor` (INV-24). Attached lazily on an entity's first `AwardExperience` call — always a persistent (player) entity. Keys serialized by enum name via the global `JsonStringEnumConverter`, mirroring `WalletComponent`. Located in `Core/Modules/Progression/Components/`. | yes |
| Ascension | `AscensionComponent` | `Tier: int` (0–6, character-wide scalar), `GrantedUnlocks: List<string>` (unlock ids recorded on ascend, idempotent). The derived additive power baseline is **not** stored here — pulled on read by `AscensionEffectContributor` (INV-24). Attached lazily on an entity's first successful `TryAscend` call — always a persistent (player) entity; mob entities never carry it. Located in `Core/Modules/Ascension/Components/`. | yes |

---

## How to add a new component

Use the `add-component` skill — see `.claude/skills/add-component/SKILL.md`. Short version:

1. Decide if it's cross-cutting or module-owned.
2. Add the `.cs` file under the correct folder (name `*Component.cs`).
3. Implement `IComponent` with **pure data only** — no logic.
4. Decide persistence — two questions (see [../architecture/06-persistence.md](../architecture/06-persistence.md)):
   - **Should entities of this type survive restart?** → controlled by `PersistentEntity` on the *entity*, not by this component. Decide when defining the construction path for this archetype.
   - **If the entity IS saved, should this component's data be included?** → tag the class `[Persistent]` if yes; leave untagged if it's transient (session ref, cached lookup, derived state). Transient is the right default for session-only state.
5. Update this catalog (add a row to the appropriate table, including the **Persisted?** column).
6. If required by an archetype, update `Core/ECS/ArchetypeRegistry.cs` and [archetypes.md](archetypes.md).

Invariants to preserve:
- Components are pure data. No methods that perform work.
- Do not call back into `EntityService` / `EcsManager.World` from a component.
- Components may be struct or class. Prefer class (current codebase convention) unless profiling calls for struct.
- If a stat can be modified by gear or effects, the component stores the **base** value only; systems compute effective values on read by summing base + effects.
