# Items

> Physical objects in the world: picking up and dropping, carrying in inventory, and wearing/wielding as equipment. **Status:** live (slices 6, 7).

## What it is

An item is a physical object that exists on the ground in a room or in a character's inventory. A player picks up an item from the ground, carries it, drops it back to the ground, and can wear wearable items (weapons, armor) in named equipment slots on their character. Items are first-class entities — they carry `ItemDataComponent` for name/description/keywords and `ItemType` for sub-class routing.

From a player's seat: `get <item>` picks up from the room, `drop <item>` sets it back down, `inventory` (or `inv`, `i`) lists what you are carrying, `wear <item>` equips a wearable item into its declared slot(s), `remove <item>` returns a worn item to inventory, and `equipment` (or `eq`) lists all occupied slots.

## How it works

The feature composes two cooperating subsystems:

- **`IItemSystem`** — query and mutation for item entities: find items in a room or inventory, prefix-match by name/keyword, move items between ground and inventory. Pure ECS mutations; no events.
- **`IEquipmentSystem`** — query and mutation for equipment slots: find worn items, equip (with implicit silent displacement of any existing slot occupant), remove back to inventory. Pure ECS mutations; no events.

Commands are the Initiators: they call the appropriate system, publish a past-tense event, then save the player entity. Handlers (priority 80) subscribe for broadcast fan-out only.

**Item location model.** Items on the ground carry `LocationComponent.RoomEntityId`. Items in inventory have *no* `LocationComponent` — tracked exclusively by `InventoryComponent.ItemEntityIds` on the holder. This keeps room queries clean: "items in this room" = entities with both `ItemDataComponent` and a `LocationComponent` pointing to that room.

**Drop-and-vanish persistence policy.** `DropCommand` saves only the player, not the item. The item's last persisted state has no `LocationComponent` (saved during pickup), so it appears nowhere on restart. YAML-spawned items are re-placed in their `spawnRoomId` by `PlaceItemsInRooms`; ad-hoc `mkitem` items simply vanish. If items persisting where dropped is needed, a future slice saves the item on drop and removes the `PlaceItemsInRooms` re-placement for items with a saved location. See [completed/slice-6-items-and-inventory.md](../../roadmap/completed/slice-6-items-and-inventory.md) for the full rationale.

**Argument resolution.** `ItemInRoomResolver` and `ItemInInventoryResolver` implement `IArgumentResolver`, returning `ResolvedCandidate(MatchString, CanonicalValue)` pairs for each item name and keyword. The parser deduplicates by `CanonicalValue` after prefix-matching so typing "sword" when both "a short sword" (name) and "sword" (keyword) match yields one canonical result, not ambiguity.

**Equipment — slot lifecycle.** `ItemDataComponent.WornSlots` declares the slots an item occupies (`MainHand`, `OffHand`, `Head`, `Chest`, `Feet`). `EquipItem` owns the implicit-remove loop: for each declared slot that is already occupied, it silently displaces the existing item back to inventory before placing the new one. `WearCommand` calls only `EquipItem(playerEntityId, itemEntityId)` — the per-slot iteration is the system's job (INV-8). Displacement is silent — no event fires for the displaced item, only `ItemEquippedEvent` for the new one.

**`BlueprintComponent` decoupling.** `ItemSystem.MoveToInventory` (slice 6) unconditionally clears `BlueprintComponent` at pickup. By the time a player can invoke `wear`, the item entity is already decoupled from its template; `WearCommand` and `EquipmentSystem` need not handle `BlueprintComponent` (INV-21).

## Systems

| System | Role |
|---|---|
| [`item-inventory-system.md`](item-inventory-system.md) | Item entity model, room/inventory query + mutation, argument resolvers, persistence lifecycle |
| [`equipment-system.md`](equipment-system.md) | Equipment slot lifecycle: worn slots, equip (implicit swap), remove, display |

## Surfaces

- **Commands** — `get <item>`, `drop <item>`, `inventory`/`inv`/`i`, `wear <item>`, `remove <item>`, `equipment`/`eq`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `ItemPickedUpEvent`, `ItemDroppedEvent`, `ItemEquippedEvent`, `ItemUnequippedEvent`, `ItemCreatedByAdminEvent`, `ItemPropertySetByAdminEvent`. See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — `ItemDataComponent` (`[Persistent]`, name/description/keywords/type/worn-slots/damage-bonus), `InventoryComponent` (`[Persistent]`, cross-cutting), `EquipmentComponent` (`[Persistent]`, cross-cutting). See [`../../reference/components.md`](../../reference/components.md).
- **Admin commands** — `mkitem [name]`, `setitem <blueprintId> <property> <value>`. See [`../../reference/commands.md`](../../reference/commands.md).

## Flows

- [Items journey (pickup · drop · inventory)](../../architecture/flows/flow-09-item-pickup.md) — how `get`, `drop`, and `inventory` execute end-to-end, including persistence lifecycle and spawn slot vacancy.
- [Equipment journey (wear · remove)](../../architecture/flows/flow-13-wear-item.md) — how `wear` and `remove` move items through `EquipmentComponent.Slots`.

## Related

- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-8 (command calls one system method), INV-14 (two-level persistence opt-in), INV-21 (blueprint/instance separation at pickup).
- [`../../roadmap/completed/slice-6-items-and-inventory.md`](../../roadmap/completed/slice-6-items-and-inventory.md) · [`../../roadmap/completed/slice-7-equipment.md`](../../roadmap/completed/slice-7-equipment.md) — as-built history and design decisions.
- **Combat stats** — worn gear contributes via `ItemDataComponent.StatBonuses` + `EquipmentEffectContributor` (the INV-24 effect seam), folded into `IStatSystem.Get(AttackPower|Defense)`; see [`equipment-system.md`](equipment-system.md#worn-gear-stat-contributions) and the `EquipmentEffectContributor` row in [`../../reference/systems.md`](../../reference/systems.md).
- **Mobs** (not yet migrated) — `EquipmentComponent` is cross-cutting; mob entities carry it without a domain dependency.
