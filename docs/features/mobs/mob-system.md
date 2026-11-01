# Mob System

> Domain system for mob entity creation, YAML authoring, and template deserialization. **Authoring checkpoint:** slice 8 (attributes extended: slice 8-a). Living document.

## What it is / does

`MobBuilderSystem` is a **domain-tier pure system** that creates ad-hoc mob entities and mutates their properties. It mutates ECS state only — event publication and persistence calls remain in the commands that call it (INV-5). It never touches the event bus and never calls persistence directly.

`MobContentWriter` is the atomic YAML writer for `MobTemplate`: it serializes the template to `{contentDirectory}/mobs/{blueprintId}.yaml` using a tmp-then-rename pattern. `MobTemplateDeserializer` is the symmetric reader, registered as `ITemplateDeserializer` for `kind: mob` with `YamlContentSerializer`.

## How it works

### Mob template lifecycle

`CreateMob(name)` generates a blueprint id (`mob.adhoc.<8-char-base36>`) and registers a minimal `MobTemplate` in `ITemplateRegistry` — **no entity is created**. Returns `MobCreationResult(BlueprintId, Template)`. The world instance comes from a room spawn rule: `mkmob` first checks the invoker's room (`IRoomBuilderSystem.TryGetAuthorableRoom`), then calls `CreateMob`, writes the mob YAML, and adds a count-1 rule via `IRoomBuilderSystem.AddSpawnRule(room, bp, 1, 300)`, which spawns the instance through `SpawnSystem.SyncRoom`. World mobs are world content: no `PersistentEntity`, never saved (INV-23).

### Mutation methods

Every setter is **blueprint-keyed** (INV-21): it updates the in-memory `MobTemplate` **and every live world instance** of that blueprint (a `MobDataComponent` entity with a matching `BlueprintComponent`, never a `PersistentEntity`), and returns the number of instances updated (`0` is valid; `-1` means no such mob template). Callers (commands) write YAML after each mutation — the system does not call `IMobContentWriter` (INV-5).

- `SetMobName` / `SetMobDescription` / `SetMobKeywords` / `SetMobType` — identity fields on `MobDataComponent`.
- `SetAttribute(blueprintId, property, value)` (slice 8-a) — `AttributesComponent` / `PoolsComponent` (`level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `maxmana`, `maxstamina`, `maxastra`); pool maxima go through `IAttributeSystem.SetMax`, which writes the base and clamps each instance's current to its effective cap.
- `SetMobProtection(blueprintId, ProtectionFlags)` (slice 12b) — `ProtectionComponent` added/updated when flags ≠ `None`, removed when `None`, plus `MobTemplate.Protection`. The gates that read it live in the combat and effects domains. See [mobs.md → Protection](mobs.md#protection-invulnerability--immunity).
- `SetMobTier` / `SetMobBand` / `SetMobXpScale` — the Tier×Band tags and kill-XP scale on `MobDataComponent`.
- `SetMobShop(blueprintId, isShop, …)` — adds/updates or removes `ShopComponent` (ensuring an `InventoryComponent`); till seeding stays with `ShopkeeperSpawnHandler`.

### YAML template shape

`MobTemplate` fields: `blueprintId`, `name`, `description`, `keywords`, `type` (string enum — `none`, `vendor`, `guard`, `creature`). A mob carries **no spawn location** — room spawn rules place it ([spawn-system](../world/spawn-system.md)); a non-blank legacy `spawnRoomBlueprintId` is a fail-fast load error, a blank one is ignored. Slice 8-a extended the DTO with `level`, `maxHp`, attribute fields (`mind`, `body`, `spirit`, `attunement`), and pool fields (`maxMana`, `maxStamina`, `maxAstra`). Slice 12b added `protection` — a flag-name string list (`untargetable`, `effectimmune`); absent/empty ⇒ `None` ⇒ no `ProtectionComponent` on `Apply` (opt-in, mirrors `currencyLoot`).

`MobTemplateDeserializer` warns on unknown `type` values and never throws — resilient deserialization is the design contract (unknown fields are ignored; bad fields log and default). Unknown `protection` flag names are logged and skipped (case-insensitive parse).

### Spawn from content

`WorldContentLoader` reads files from `<ContentDirectory>/mobs/` on startup and reload and registers the templates; it spawns no mob. Every world mob is placed by a room `SpawnRule`: `SpawnSystem` fills each rule's slots on `WorldContentReadyEvent` (with `LocationComponent { RoomEntityId, RoomBlueprintId }`) and re-spawns a slot after its mob dies. Admin `mkmob` registers a template and adds a count-1 rule to the invoker's room; `setmob` updates the template and every live world instance.

### `MobType` classification

`MobType` (`None`, `Vendor`, `Guard`, `Creature`) is **data only** — no routing or behavior logic consumes it in the current implementation. It is a classification field for future use (vendor dialogue, guard aggression, AI routing).

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IMobBuilderSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — `CreateMob` / `SetMobName` / `SetMobDescription` / `SetMobKeywords` / `SetMobType` / `SetAttribute` / `SetMobProtection`. Pure: returns results; never touches the bus or persistence (INV-5).
- [`IMobContentWriter.cs`](../../../Core/Modules/Mobs/Systems/IMobContentWriter.cs) — `WriteAsync(MobTemplate, CancellationToken)`. Atomic YAML write (tmp → rename).

## Considerations

- **`MobDataComponent` is cross-cutting.** Placed under `Core/ECS/Components/` so future modules (combat, AI, dialogue) can query mob names without a domain dependency on `Core/Modules/Mobs/`. Mirrors `ItemDataComponent`.
- **`BlueprintComponent` is retained on live mob entities.** Unlike items (which clear `BlueprintComponent` on pickup — INV-21), mobs have no pickup transition. The INV-21 obligation (clear `BlueprintComponent` from the dead entity before re-spawning) is tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).
- **Event publication ordering.** `MkMobCommand` and `SetMobCommand` write YAML first, save entity second, publish event last — matching the crash-safety ordering precedent in `LoginFlow` (entity save before account save). This ensures the template file exists on disk before any crash window opens.
- **Mob display.** `BroadcastSystem.SendRoomDescriptionAsync` queries `MobDataComponent` entities in the room and populates `RoomDescriptionMessage.Mobs`. `TelnetOutputFormatter` renders one line per mob: `"<Name> is here."`. Identical-name mobs are not stacked — stacking is acknowledged debt.
- **`look <mob>` is out of scope.** Inspecting a specific mob by keyword belongs with `kill <mob>` argument resolution in the combat slice.
- **No `InventoryComponent`/`EquipmentComponent` on mobs.** Added when the combat/death slice requires them.

## Extensibility

- **Wandering (AI movement)** — requires `TimeSystem` + AI behavior infrastructure; deferred to a dedicated slice.
- **Faction / aggro** — `MobType` is the seam; guard aggression and creature behavior routing layer on without model change.
- **Loot** — `InventoryComponent` is cross-cutting and ready to attach; the death slice drives when it lands.

## Related

- [`mobs.md`](mobs.md) — holistic feature view, player-facing surfaces, and the combat-target surface.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `MobBuilderSystem` / `MobContentWriter` and `MobDataComponent` catalog rows.
- [`../../roadmap/completed/slice-8-mobs.md`](../../roadmap/completed/slice-8-mobs.md) — as-built record and notable design decisions.
- **Combat** — [`../combat/combat.md`](../combat/combat.md) and [`../combat/combat-system.md`](../combat/combat-system.md) — `TryFindTargetInRoom` uses `MobDataComponent.Name`/`Keywords` for target resolution.
