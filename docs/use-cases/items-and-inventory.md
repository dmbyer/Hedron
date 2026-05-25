# Use Case: Items, Inventory, and Basic Item Commands

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Modules/Items/` (new); `Core/ECS/Components/` (cross-cutting inventory); `Core/Commands/` (resolver contract update)

---

## Description

Introduces items as first-class entities in the world. An item is a physical object that can exist on the ground in a room or be carried in a character's inventory. This slice delivers the item entity model, an admin authoring path (`mkitem` / `setitem`), a minimal inventory per character, the `get` / `drop` / `inventory` player commands, an extended `look <item>` command, and the concrete `IArgumentResolver` implementations. Items can optionally be typed (`ItemType` enum) to support future sub-type matching and equipment slot validation. Content that requires items spawned from pre-authored YAML (e.g. a world-file item in a starter room) is also supported via an `ItemTemplate` + deserializer.

This slice is **deliberately narrow**: no equipment slots, no containers, no combat interaction, no item stat bonuses. Those land in slices 7+. The goal here is the core object lifecycle (exists → picked up → carried → dropped) together with the infrastructure subsequent slices build on.

---

## Preconditions

- Slices 1–5b complete. Reused: `EntityService`, `IEventBus`, `ITemplateRegistry`, `IPersistenceSystem`, `IBroadcastSystem`, `IOutputWriter`, `ICommandDispatcher`, command framework (slice 3, 3a), output framework (slice 4), `LocationComponent`, `RoomComponent`, `PersistentEntity`, `BlueprintComponent`, `IAdminAuthorizer`, `AdminRequirement`, `IRoomBuilderSystem` (reference pattern).
- `IArgumentResolver` interface and parser wiring exist from slice 3a.
- Every connected player has a `CharacterComponent` and a `LocationComponent` with a valid room.

---

## Postconditions

- An `ItemDataComponent` (`Name`, `Description`, `Keywords`, `ItemType`) exists and is `[Persistent]`.
- An `ItemType` enum exists (`None`, `Weapon`, `Armor`, `Consumable`, `Container`, `Misc`).
- Items dropped or created in a room carry `LocationComponent.RoomEntityId` pointing to that room. Items with `PersistentEntity` survive restart in their position.
- An `ItemTemplate` YAML shape exists (`kind: item`). `WorldContentLoader` registers item templates on startup and reload via `ItemTemplateDeserializer`.
- Admin `mkitem [name]` creates an ad-hoc item entity in the invoker's room. A confirmation shows the blueprint id.
- Admin `setitem <blueprintId> <property> <value>` mutates name, description, keywords, or type on the target item.
- `RoomDescriptionMessage` carries an `Items` field; `SendRoomDescriptionAsync` populates it with item names whose `LocationComponent.RoomEntityId` is the displayed room.
- `look` (no arg) shows the room as before, now including items on the ground.
- `look <item>` resolves the target against items in the invoker's room first, then their inventory as a fallback; shows name + description. "You don't see that here." on no match.
- A cross-cutting `InventoryComponent` (`List<uint> ItemEntityIds`) exists and is `[Persistent]`. `CreateCharacterAsync` attaches an empty `InventoryComponent` to every new character. `CharacterHydrationHandler` attaches one to pre-existing characters that lack it.
- `IArgumentResolver` returns `IReadOnlyList<ResolvedCandidate>?` where `ResolvedCandidate(string MatchString, string CanonicalValue)` allows keyword aliases to resolve to the canonical item name; the parser deduplicates by `CanonicalValue`.
- `get <item>` picks up a named item from the current room into inventory; saves item and player; publishes `ItemPickedUpEvent`; broadcasts to room.
- `drop <item>` drops a named item from inventory to the current room; saves only the player (item intentionally not saved — see Design Notes); publishes `ItemDroppedEvent`; broadcasts to room.
- `inventory` (aliases `inv`, `i`) lists carried items, or "You are carrying nothing." when empty.

---

## Main Flow

### Flow A-1 — Admin `mkitem [name]`

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions rejected.
2. **Creation.** `MkitemCommand` calls `IItemBuilderSystem.CreateItem(name, roomEntityId)`. The system generates `item.adhoc.<8-char-base36>`, creates the entity, attaches `ItemDataComponent { Name, Description="", Keywords=[], ItemType=None }` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId = invoker's room }`, registers a minimal `ItemTemplate`.
3. **Event + save.** Command publishes `ItemCreatedByAdminEvent`. Calls `SaveEntityAsync(itemEntityId)`. Writes confirmation including blueprint id.
4. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-2 — Admin `setitem <blueprintId> <property> <value>`

1. **Privilege gate.** As A-1.
2. **Resolve item.** Command looks up `blueprintId` in `ITemplateRegistry`, then queries `EntityService` for the entity with matching `BlueprintComponent.BlueprintId`. Not found → error.
3. **Mutation.** Calls `IItemBuilderSystem.Set*` for the named property. `keywords` splits `value` on whitespace. `type` parses as `ItemType` enum.
4. **Event + save.** Publishes `ItemPropertySetByAdminEvent`. Calls `SaveEntityAsync(itemEntityId)`. Writes confirmation.

### Flow A-3 — `look <item>`

1. `LookCommand` detects non-empty `target` tail.
2. Calls `IItemSystem.TryFindItemInRoom(currentRoomId, target, ...)` first.
3. If not found, falls back to `IItemSystem.TryFindItemInInventory(playerEntityId, target, ...)`.
4. On match: writes `PlainMessage(item.Name + "\n" + item.Description)`. On no match: "You don't see that here."

### Flow A-4 — World-content item spawn (startup / reload)

1. `WorldContentLoader` encounters `kind: item` YAML. Calls `ItemTemplateDeserializer.Deserialize` → `ItemTemplate`. Registers in `TemplateRegistry`.
2. `SpawnMissingEntities`: for templates with no live entity, creates the entity with `PersistentEntity`. Returns `newlySpawned` set.
3. **Immediate save.** `WorldContentLoader` calls `SaveEntityAsync` for every newly-spawned entity to make IDs durable.
4. `PlaceItemsInRooms`: for each newly-spawned item entity **only**, attaches `LocationComponent { RoomEntityId }` from `spawnRoomId`. Restored-from-persistence entities are skipped.

### Flow B-1 — `get <item>` (pickup)

1. **Argument resolve.** `GetCommand` uses `ItemInRoomResolver`; parser prefix-matches → canonical name or error.
2. **Entity resolve.** `IItemSystem.TryFindItemInRoom(roomId, canonicalName, ...)`. Not found → "You don't see that here."
3. **Pickup.** `IItemSystem.MoveToInventory(itemEntityId, playerEntityId)` removes `LocationComponent`; appends to `InventoryComponent.ItemEntityIds`. No-op if item already has no `LocationComponent`.
4. **Event + save.** Publishes `ItemPickedUpEvent`. Calls `SaveEntityAsync(itemEntityId)`, `SaveEntityAsync(playerEntityId)`.
5. **Handler.** `ItemInteractionHandler` (priority 80) broadcasts to room (excluding picker); writes confirmation to picker.

### Flow B-2 — `drop <item>` (drop)

1. **Argument resolve.** `DropCommand` uses `ItemInInventoryResolver`; parser prefix-matches → canonical name or error.
2. **Entity resolve.** `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName, ...)`. Not found → "You aren't carrying that."
3. **Drop.** `IItemSystem.DropToRoom(itemEntityId, playerEntityId, currentRoomId)` removes from `InventoryComponent`; attaches `LocationComponent { RoomEntityId }`.
4. **Event + save.** Publishes `ItemDroppedEvent`. Saves player entity only — item intentionally not saved (see Design Notes).
5. **Handler.** `ItemInteractionHandler` broadcasts drop messages.

### Flow B-3 — `inventory`

1. `InventoryCommand.ExecuteAsync` reads `InventoryComponent` from invoker. If empty → "You are carrying nothing."
2. For each item entity id in `InventoryComponent.ItemEntityIds`, reads `ItemDataComponent.Name`. Writes `InventoryListMessage`.
3. No events fired.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `ItemCreatedByAdminEvent` | `MkitemCommand` | `uint AdminEntityId, uint ItemEntityId, string BlueprintId, uint RoomEntityId` | Audit log; future editor hooks |
| `ItemPropertySetByAdminEvent` | `SetitemCommand` | `uint AdminEntityId, uint ItemEntityId, string PropertyName, string NewValue` | Audit log |
| `ItemPickedUpEvent` | `GetCommand` | `uint PlayerEntityId, uint ItemEntityId, uint RoomEntityId` | Room broadcast; audit; future stat/weight tracking |
| `ItemDroppedEvent` | `DropCommand` | `uint PlayerEntityId, uint ItemEntityId, uint RoomEntityId` | Room broadcast; audit |

---

## Design Notes

- **Item location model.** Items on the ground have `LocationComponent.RoomEntityId` pointing to their room. Items in inventory have **no** `LocationComponent` — tracked exclusively by `InventoryComponent.ItemEntityIds` on the holder. Room items = entities with `LocationComponent.RoomEntityId == roomId` AND `ItemDataComponent`; inventory items = iterate `InventoryComponent.ItemEntityIds`.
- **Item persistence strategy.**
  - **`mkitem` (ad-hoc) items**: Saved immediately on creation. When picked up, saved again without `LocationComponent`. When dropped, **not** re-saved — reverts to no-location state on restart; effectively vanishes. Admin can re-place via `setitem`.
  - **Template (YAML) items**: Saved immediately on first spawn (entity ID durability). When picked up, saved without `LocationComponent`. When dropped, **not** saved — `PlaceItemsInRooms` re-places it in `spawnRoomId` on next restart.
  - **Player entity**: Always saved on pickup and drop to durably record `InventoryComponent` changes.
  - **`PlaceItemsInRooms` restricted to `newlySpawned` set**: Prevents overriding a carried item's missing `LocationComponent` with its spawn room.
  - **Dropped items vanish by design**: If "items persist where dropped" is needed in a future slice, that slice adds: (a) save item entity after drop, and (b) remove template re-placement in `PlaceItemsInRooms` for items that have a saved location.
- **`InventoryComponent` migration guard.** `CharacterHydrationHandler` attaches an empty `InventoryComponent` to any character entity that lacks one, without saving immediately. The component is persisted on the character's next save-on-change event.
- **`IArgumentResolver` deduplication.** `ResolvedCandidate(MatchString, CanonicalValue)` enables keyword aliases (e.g. `"sword"`, `"short"`, `"iron"`) to resolve to the same `CanonicalValue` (`"a short sword"`). The parser deduplicates by `CanonicalValue` — multiple matching `MatchString` values for the same item are not ambiguous.
- **Keywords and partial matching.** Both resolvers emit candidates for item name + each keyword. If two different items share a keyword (e.g. both are `"sword"`), typing that keyword remains correctly ambiguous.
- **`ItemType` is data only in this slice.** No matching behavior uses `ItemType` until slice 7 (equipment slot validation).
- **YAML `spawnRoomId`.** If the target room doesn't exist at spawn time, the item is created with no `LocationComponent` (in the void). `setitem` or a future `place` command can relocate it.

---

## Related

- [`command-prefix-matching.md`](command-prefix-matching.md) — slice 3a; introduced `IArgumentResolver` seam and `CommandArgumentResolverContext`.
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; `IRoomBuilderSystem` is the direct pattern for `IItemBuilderSystem`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` + `PersistentEntity` model items and inventory follow.
- [`output-framework.md`](output-framework.md) — slice 4; `RoomDescriptionMessage` and `IOutputWriter` are both extended here.
- [`equipment.md`](equipment.md) — slice 7; builds on `ItemDataComponent`, `InventoryComponent`, and item resolver infrastructure from this slice.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; `ITemplateDeserializer` pattern and `WorldContentLoader` extension points.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
