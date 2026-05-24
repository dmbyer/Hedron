# Use Case: Items, Inventory, and Basic Item Commands

**Status:** partial
**Actors:** Player, Administrator, System
**Module:** `Core/Modules/Items/` (new); `Core/ECS/Components/` (cross-cutting inventory); `Core/Commands/` (resolver contract update)

---

## Description

Introduces items as first-class entities in the world. An item is a physical object that can exist on the ground in a room or be carried in a character's inventory. This slice delivers the item entity model, an admin authoring path (`mkitem` / `setitem`), a minimal inventory per character, the `get` / `drop` / `inventory` player commands, an extended `look <item>` command, and the concrete `IArgumentResolver` implementations (filling the seam deferred since slice 3a). Items can optionally be typed (`ItemType` enum) to support future sub-type matching and equipment slot validation. Content that requires items spawned from pre-authored YAML (e.g. a world-file item in a starter room) is also supported via an `ItemTemplate` + deserializer. Both authoring paths — ad-hoc admin creation and YAML templates — land in this slice.

This slice is **deliberately narrow**: no equipment slots, no containers, no combat interaction, no item stat bonuses. Those land in slices 7+. The goal here is the core object lifecycle (exists → picked up → carried → dropped) together with the infrastructure subsequent slices build on.

---

## Implementation Phases

This slice is large enough to warrant two sequential sub-phases, each independently testable. Phase A may be built and validated before Phase B begins; both must land in the same PR or adjacent PRs before slice 7 starts.

| Phase | Deliverable | Testable checkpoint |
|---|---|---|
| **A** | Item entity model · admin authoring · `look <item>` · room description with items | Admin creates item; `look` shows it in room; `look <item>` shows description |
| **B** | `InventoryComponent` · `get` / `drop` / `inventory` commands · `IArgumentResolver` concrete implementations · broadcast on pickup/drop | Player picks up item; `inventory` shows it; `drop` returns it; room sees broadcast messages |

---

## Preconditions

- Slices 1–5b complete. Reused: `EntityService`, `IEventBus`, `ITemplateRegistry`, `IPersistenceSystem`, `IBroadcastSystem`, `IOutputWriter`, `ICommandDispatcher`, command framework (slice 3, 3a), output framework (slice 4), `LocationComponent`, `RoomComponent`, `PersistentEntity`, `BlueprintComponent`, `IAdminAuthorizer`, `AdminRequirement`, `IRoomBuilderSystem` (reference pattern).
- `IArgumentResolver` interface and parser wiring exist from slice 3a; no concrete implementations exist yet.
- Every connected player has a `CharacterComponent` and a `LocationComponent` with a valid room.

---

## Postconditions

### After Phase A

- An `ItemDataComponent` (`Name`, `Description`, `Keywords`, `ItemType`) exists and is `[Persistent]`.
- An `ItemType` enum exists (`None`, `Weapon`, `Armor`, `Consumable`, `Container`, `Misc`).
- Items dropped or created in a room carry `LocationComponent.RoomEntityId` pointing to that room. Items with `PersistentEntity` survive restart in their position.
- An `ItemTemplate` YAML shape exists (`kind: item`). `WorldContentLoader` registers item templates on startup and reload via a new `ItemTemplateDeserializer`.
- Admin `mkitem [name]` creates an ad-hoc item entity (`item.adhoc.<shortid>`) in the invoker's room with `ItemDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent`. A confirmation confirms the blueprint id.
- Admin `setitem <blueprintId> <property> <value>` mutates name, description, keywords (space-separated), or type on the target item, saves immediately, confirms to the admin.
- `RoomDescriptionMessage` carries an `Items` field; `SendRoomDescriptionAsync` populates it with the names of items whose `LocationComponent.RoomEntityId` is the displayed room.
- `look` (no arg) shows the room as before, now including items on the ground.
- `look <item>` resolves the target against items in the invoker's room (by name and keyword, prefix-matched), and writes the item's name + description. "You don't see that here." on no match or ambiguity.

### After Phase B

- A cross-cutting `InventoryComponent` (`List<uint> ItemEntityIds`) exists and is `[Persistent]`. `CreateCharacterAsync` in `AccountSystem` attaches an empty `InventoryComponent` to every new character.
- `IArgumentResolver` contract is updated to return `IReadOnlyList<ResolvedCandidate>?` (where `ResolvedCandidate(string MatchString, string CanonicalValue)` allows keyword aliases to map back to canonical item name). The parser deduplicates by `CanonicalValue` after prefix matching — multiple candidates with the same `CanonicalValue` count as one match, not ambiguity. This is the only breaking change to an interface that has no concrete implementations until this slice.
- `get <item>` picks up a named item from the current room into the player's inventory: removes `LocationComponent` from item, appends entity id to `InventoryComponent.ItemEntityIds`; saves item and player entities; publishes `ItemPickedUpEvent`; broadcasts to room.
- `drop <item>` drops a named item from inventory to the current room: removes item id from `InventoryComponent.ItemEntityIds`, attaches `LocationComponent` pointing to current room; saves both; publishes `ItemDroppedEvent`; broadcasts to room.
- `inventory` (aliases `inv`, `i`) lists the names of all items in the player's inventory, or "You are carrying nothing." when empty.
- `look <item>` is extended to resolve against inventory as a fallback when not found in room (room first, then inventory).

---

## Main Flow

### Flow A-1 — Admin `mkitem [name]`

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions rejected.
2. **Creation.** `MkitemCommand` calls `IItemBuilderSystem.CreateItem(name, roomEntityId)`. The system generates `item.adhoc.<8-char-base36>`, creates the entity, attaches `ItemDataComponent { Name, Description="", Keywords=[], ItemType=None }` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId = invoker's room }`, registers a minimal `ItemTemplate`.
3. **Event + save.** Command publishes `ItemCreatedByAdminEvent(AdminEntityId, ItemEntityId, BlueprintId, RoomEntityId)`. Calls `SaveEntityAsync(itemEntityId)` (save-on-change). Writes confirmation `PlainMessage` including blueprint id.
4. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-2 — Admin `setitem <blueprintId> <property> <value>`

1. **Privilege gate.** As A-1.
2. **Resolve item.** Command looks up `blueprintId` in `ITemplateRegistry` to confirm it exists, then queries `EntityService` for the entity with matching `BlueprintComponent.BlueprintId`. Not found → error `PlainMessage`.
3. **Mutation.** Based on `property` token (`name` | `description` | `keywords` | `type`): calls the matching `IItemBuilderSystem.Set*` method. `keywords` splits `value` on whitespace. `type` parses as `ItemType` enum. Unrecognized property → usage hint.
4. **Event + save.** Publishes `ItemPropertySetByAdminEvent(AdminEntityId, ItemEntityId, PropertyName, NewValue)`. Calls `SaveEntityAsync(itemEntityId)`. Writes confirmation.
5. `AdminAuditHandler` logs.

### Flow A-3 — `look <item>`

1. `LookCommand` is extended: if raw tail is non-empty, attempt item resolution.
2. Command queries `IItemSystem.GetItemsInRoom(currentRoomId)` to build the candidate set.
3. Prefix-matches the token against item names and keywords (manual O(n) scan; no resolver needed here since look resolves across multiple pools). The first unique match → write `PlainMessage(item.Name + "\n" + item.Description)`. Zero matches → "You don't see that here." No-match does not fall through to a room description — the command body distinguishes empty vs non-empty tail before dispatching.
4. Future: the full resolver seam (Phase B) will replace the manual scan; `look <item>` will use `ItemInContextResolver`.

> **Note on look-argument design.** `LookCommand.ArgumentSchema` is updated to declare an optional `Token` arg `"target"` (`Required: false`, no `Resolver` in Phase A). If `target` is absent (empty tail), the existing room description path runs. If present, item resolution runs. No direction-based peek (`look north`) in this slice.

### Flow A-4 — World-content item spawn (startup / reload)

1. `WorldContentLoader` encounters a YAML file with `kind: item`. It calls `ItemTemplateDeserializer.Deserialize(body)` → `ItemTemplate`.
2. `TemplateRegistry.Register(blueprintId, itemTemplate)`.
3. `SpawnMissingEntities`: for any `ItemTemplate` with no live entity, `TemplateRegistry.Spawn(blueprintId)` creates the entity. `WorldContentLoader` attaches `PersistentEntity` and `LocationComponent { RoomEntityId }` from the template's `spawnRoomId` field. No event published (boot-time spawn).

### Flow B-1 — `get <item>` (pickup)

1. **Argument resolve.** `GetCommand` declares `item` as a `Token` arg with `ItemInRoomResolver`. The resolver calls `IItemSystem.GetItemsInRoom(currentRoomId)`, builds `ResolvedCandidate` list (name + each keyword → canonical name). Parser prefix-matches → unique canonical name or ambiguity/not-found error.
2. **Entity resolve.** Command calls `IItemSystem.TryFindItemInRoom(roomId, canonicalName, out itemEntityId)`. Not found (race condition: item taken by someone else) → "You don't see that here."
3. **Pickup.** `IItemSystem.MoveToInventory(itemEntityId, playerEntityId)` removes `LocationComponent` from item, appends item id to `InventoryComponent.ItemEntityIds`. Returns without error if either entity is missing (no crash; race condition is acceptable).
4. **Event + save.** Command publishes `ItemPickedUpEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`. Calls `SaveEntityAsync(itemEntityId)`, `SaveEntityAsync(playerEntityId)`.
5. **Handler.** `ItemInteractionHandler` (priority `HandlerPriority.Notification` = 80) handles `ItemPickedUpEvent`: broadcasts `PlainMessage("<PlayerName> picks up <ItemName>.")` to the room (excluding the picker). Writes `PlainMessage("You pick up <ItemName>.")` to the player.

### Flow B-2 — `drop <item>` (drop)

1. **Argument resolve.** `DropCommand` declares `item` as `Token` with `ItemInInventoryResolver`. Resolver calls `IItemSystem.GetItemsInInventory(playerEntityId)`, builds candidate list from carried items' names + keywords.
2. **Entity resolve.** `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName, out itemEntityId)`. Not found → "You aren't carrying that."
3. **Drop.** `IItemSystem.DropToRoom(itemEntityId, playerEntityId, currentRoomId)` removes item id from `InventoryComponent.ItemEntityIds`, attaches `LocationComponent { RoomEntityId = currentRoom }` to item.
4. **Event + save.** Publishes `ItemDroppedEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`. Calls `SaveEntityAsync(itemEntityId)`, `SaveEntityAsync(playerEntityId)`.
5. **Handler.** `ItemInteractionHandler` broadcasts `"<PlayerName> drops <ItemName>."` to room (excluding dropper). Writes `"You drop <ItemName>."` to player.

### Flow B-3 — `inventory`

1. `InventoryCommand.ExecuteAsync` reads `InventoryComponent` from invoker entity. If absent or empty → writes "You are carrying nothing." and returns.
2. For each item entity id in `InventoryComponent.ItemEntityIds`, reads `ItemDataComponent.Name`. Writes an `InventoryListMessage` (new output message shape; see Output section below) listing items with a header line.
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

## Systems / Handlers Involved

| Type | Name | Location | Purpose |
|---|---|---|---|
| Domain system | `IItemSystem` / `ItemSystem` | `Core/Modules/Items/Systems/ItemSystem.cs` | Query items in room/inventory; pickup/drop ECS mutations (no events, no persistence) |
| Domain system | `IItemBuilderSystem` / `ItemBuilderSystem` | `Core/Modules/Items/Systems/ItemBuilderSystem.cs` | Ad-hoc item creation and property mutation for admin authoring; mirrors `IRoomBuilderSystem` pattern |
| Handler | `ItemInteractionHandler` | `Core/Modules/Items/Handlers/ItemInteractionHandler.cs` | Subscribes to `ItemPickedUpEvent` + `ItemDroppedEvent`; broadcast fan-out to room and confirmation to actor |
| Resolver | `ItemInRoomResolver` | `Core/Modules/Items/Resolvers/ItemInRoomResolver.cs` | `IArgumentResolver` impl; builds candidate list from items with `LocationComponent.RoomEntityId == invokerRoom` |
| Resolver | `ItemInInventoryResolver` | `Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs` | `IArgumentResolver` impl; builds candidate list from invoker's `InventoryComponent.ItemEntityIds` |
| Deserializer | `ItemTemplateDeserializer` | `Core/Modules/Items/ItemTemplateDeserializer.cs` | `ITemplateDeserializer` for `kind: item` YAML files |

**Interface additions:**

```csharp
public interface IItemSystem
{
    IReadOnlyList<uint> GetItemsInRoom(uint roomEntityId);
    IReadOnlyList<uint> GetItemsInInventory(uint holderEntityId);
    bool TryFindItemInRoom(uint roomEntityId, string canonicalName, out uint itemEntityId);
    bool TryFindItemInInventory(uint holderEntityId, string canonicalName, out uint itemEntityId);
    void MoveToInventory(uint itemEntityId, uint holderEntityId);  // removes LocationComponent; appends to InventoryComponent
    void DropToRoom(uint itemEntityId, uint holderEntityId, uint roomEntityId);  // removes from InventoryComponent; attaches LocationComponent
}

public interface IItemBuilderSystem
{
    ItemCreationResult CreateItem(string name, uint roomEntityId);
    void SetItemName(uint itemEntityId, string name);
    void SetItemDescription(uint itemEntityId, string description);
    void SetItemKeywords(uint itemEntityId, IReadOnlyList<string> keywords);
    void SetItemType(uint itemEntityId, ItemType itemType);
}

public readonly record struct ItemCreationResult(uint ItemEntityId, string BlueprintId);
```

**`IArgumentResolver` contract update (Phase B):**

```csharp
// Before (slice 3a, no concrete implementations exist):
IReadOnlyList<string>? GetCandidates(CommandArgumentResolverContext context);

// After (this slice — minimal breaking change to an unimplemented interface):
IReadOnlyList<ResolvedCandidate>? GetCandidates(CommandArgumentResolverContext context);
public readonly record struct ResolvedCandidate(string MatchString, string CanonicalValue);
```

The parser deduplicates by `CanonicalValue` after prefix matching: multiple candidates with the same `CanonicalValue` that all match collapse to a single match, not ambiguity. This enables keywords ("sword", "short") to coexist as match strings that all resolve to the canonical item name "a short sword". If two different items both have keyword "sword", typing "sword" remains ambiguous (two different `CanonicalValue`s).

---

## Content Tooling Impact

| Tooling | Form | Notes |
|---|---|---|
| `mkitem [name]` | Admin command | Creates ad-hoc item entity in current room; prints blueprint id |
| `setitem <blueprintId> <property> <value>` | Admin command | Mutates name/description/keywords/type on any existing item by blueprint id |
| `items` YAML block | `kind: item` YAML file under `World:ContentDirectory` | Template-based items auto-spawned at startup/reload in their configured room |
| `inventory` / `inv` / `i` | Player command | Inspect carried items |
| `get <item>` / `drop <item>` | Player commands | Item lifecycle; argument resolvers surface item names for prefix/keyword matching |

**YAML item template shape:**
```yaml
kind: item
blueprintId: item.sword.shortsword
name: a short sword
description: A simple iron short sword. It is functional if not elegant.
keywords:
  - sword
  - short
  - iron
itemType: weapon          # none | weapon | armor | consumable | container | misc
spawnRoomId: room.start   # blueprint id of the room to spawn in at startup
```

`spawnRoomId` is resolved by `WorldContentLoader` after room exits are wired; if the target room doesn't exist, a warning is logged and the item is created without a `LocationComponent` (effectively "in the void"). A future admin command or `setitem` can place it.

---

## Cross-Cutting Surfaces Stressed

| Surface | Classification | Rationale |
|---|---|---|
| **Commands** (`ICommand`, dispatcher, schema) | Adequate | No new command-framework mechanics; `mkitem`, `setitem`, `get`, `drop`, `inventory` fit the existing declarative schema pattern. `LookCommand` gains an optional arg — parser already supports `Required: false`. |
| **`IArgumentResolver` / argument parsing** | **Gap exposed** — resolved in Phase B | The seam was designed in slice 3a explicitly for this slice. The return type changes from `IReadOnlyList<string>?` to `IReadOnlyList<ResolvedCandidate>?` to support keyword deduplication. No concrete implementations exist, so the interface evolution is safe. Must resolve before merge. |
| **Output** (`IOutputMessage`, formatter) | **Gap exposed** — resolved in Phase A | `RoomDescriptionMessage` has no items section; `BroadcastSystem.SendRoomDescriptionAsync` does not query for items. Both must be extended. Additionally, a new `InventoryListMessage` output shape is introduced. The `TelnetOutputFormatter` must handle both. Must resolve before merge. |
| **Persistence** (two-level opt-in) | Adequate | `ItemDataComponent` and `InventoryComponent` are `[Persistent]`; items get `PersistentEntity` on creation. The existing flush + save-on-change model handles items and inventory without extension. |
| **`TemplateRegistry` / `WorldContentLoader`** | Adequate | `ITemplateDeserializer` registration pattern is used by world and area modules; `kind: item` just adds another deserializer. `SpawnMissingEntities` loops by template kind; items join the loop. `LinkRoomExits` is room-only and unaffected. |
| **`BroadcastSystem`** | **Gap exposed** — resolved in Phase A | `SendRoomDescriptionAsync` must query for items in the room. This is a targeted extension to an existing method; no new broadcast shape or audience model is needed. Must resolve before merge. |
| **Persistence save-on-change** | Adequate | `mkitem` and `setitem` call `SaveEntityAsync` directly (same pattern as `dig`/`set`). Get/drop also call it on both the item and the player. |
| **Event bus** | Adequate | Four new events following existing past-tense, thin-payload conventions. |
| **Admin authorization** | Adequate | `AdminRequirement` gate reused unchanged. |

---

## Flows Introduced or Modified

| # | Flow | Change | Introduced by |
|---|---|---|---|
| 9 | Item pickup (`get <item>`) | New | Phase B |
| 10 | Item drop (`drop <item>`) | New | Phase B |
| 11 | Inventory display (`inventory`) | New | Phase B |
| 12 | Admin item creation (`mkitem`) | New | Phase A |
| 3 | Player command lifecycle | Modified — `IArgumentResolver` contract updated; `look` gains optional `target` arg | Phase B / Phase A |
| 6 | Output rendering | Modified — `RoomDescriptionMessage` gains `Items` field; new `InventoryListMessage` shape | Phase A / Phase B |
| 1 | Server startup | Modified — `WorldContentLoader.SpawnMissingEntities` now handles `ItemTemplate`; item YAML loaded on startup | Phase A |

New flows (9–12) must be added to [`../architecture/flows/README.md`](../architecture/flows/README.md) in the implementation PR. Flows 1, 3, and 6 must be updated to reflect the changes. The required prose changes for each modified flow are sketched below (to be replaced by accurate as-built text in the implementation PR).

**Flow 1 (Server startup) — update:** In the mermaid, add a new loop or parallel lane showing `WorldContentLoader` calling `ItemTemplateDeserializer` for `kind: item` files, and a `SpawnMissingEntities` step that now includes item entities (attaching `LocationComponent` from `spawnRoomId`). In the prose step 7, extend the `SpawnMissingEntities` description to note that item templates spawn item entities with `PersistentEntity` + `LocationComponent` into their configured room.

**Flow 3 (Player command lifecycle) — update:** In the mermaid argument-parse step and in prose step 4, update the `IArgumentResolver` description from "returns `IReadOnlyList<string>?`" to "returns `IReadOnlyList<ResolvedCandidate>?` where each `ResolvedCandidate(string MatchString, string CanonicalValue)` allows keyword aliases to resolve back to the canonical item name; the parser deduplicates by `CanonicalValue` after prefix matching." Also update the resolver seam note from "no concrete resolver ships until slice 6" to "concrete resolvers (`ItemInRoomResolver`, `ItemInInventoryResolver`) ship in slice 6."

**Flow 6 (Output rendering) — update:** In the mermaid formatter pattern-match step and in prose step 3, add two new cases: `RoomDescriptionMessage` now carries an `Items: IReadOnlyList<string>` field and the formatter renders it as a "Items on the ground: X, Y, Z" line (or omits it if empty). `InventoryListMessage` renders as a header "You are carrying:" followed by a bulleted item list.

---

## Reference Catalog Updates

**`docs/reference/components.md`** — add:
- `ItemDataComponent` (Items module, `[Persistent]`)
- `InventoryComponent` (cross-cutting, `[Persistent]`)

**`docs/reference/systems.md`** — add:
- `ItemSystem` (domain, Items module)
- `ItemBuilderSystem` (domain, Items module)

**`docs/reference/handlers.md`** — add:
- `ItemInteractionHandler` (Items module, priority 80)
- Update `AdminAuditHandler` entry to list `ItemCreatedByAdminEvent` and `ItemPropertySetByAdminEvent` among its subscribed events.

**`docs/reference/commands.md`** — add:
- `MkitemCommand`, `SetitemCommand` (admin)
- `GetCommand`, `DropCommand`, `InventoryCommand` (player)
- `LookCommand` updated (optional target arg); also remove the stale note "output is not yet routed through the `IOutputWriter` formatter (deferred to slice 4)" from the existing `look` entry — that deferral shipped in slice 4.

**`docs/reference/archetypes.md`** — update:
- `Player` archetype row: note that the "Inventory" required component is `InventoryComponent` (implemented in this slice, `Core/ECS/Components/InventoryComponent.cs`).
- `Mob` archetype row: same note.

---

## Design Notes

- **Item location model.** Items on the ground have `LocationComponent.RoomEntityId` pointing to their room. Items in inventory have **no** `LocationComponent` — they are tracked exclusively by `InventoryComponent.ItemEntityIds` on the holder. This keeps queries clean: room items = entities with `LocationComponent.RoomEntityId == roomId` AND `ItemDataComponent`; inventory items = iterate `InventoryComponent.ItemEntityIds`. No null-room sentinel or container-chain walk needed in this slice.
- **Inventory on character.** `AccountSystem.CreateCharacterAsync` is extended to attach an empty `InventoryComponent` to every new character. Existing persisted characters that lack `InventoryComponent` will have it added at hydration time (either via a migration guard in `CharacterHydrationHandler` or simply by the `[Persistent]` round-trip creating an empty component on first save after upgrade).
- **`IItemBuilderSystem` mirrors `IRoomBuilderSystem`.** Pure domain logic only; all event publication and persistence calls remain in the command (Initiator). Same reasoning as rooms: reusable by a future in-game editor.
- **`ItemType` is data only in this slice.** No matching behavior uses `ItemType` yet — it is a field on `ItemDataComponent` that equipment (slice 7) and combat (slice 9) will query. Setting it now via `setitem type <value>` means authors can classify items before the consuming slices land.
- **Keywords and partial matching.** `ItemInRoomResolver` and `ItemInInventoryResolver` both emit `ResolvedCandidate` pairs for item name + each keyword. Deduplication in the parser (by `CanonicalValue`) means a player typing any prefix of any keyword resolves to the item. Multiple items sharing the same keyword remain correctly ambiguous. Subtype-based matching ("get sword" matching all weapons regardless of name) is **not** supported in this slice — add a typed `ItemType`-aware resolver in a later slice if needed.
- **Look-item in Phase A.** Because resolvers are a Phase B concern, `LookCommand` in Phase A does a manual inline scan of items in the invoker's room. In Phase B, this is replaced by `ItemInContextResolver` (room items + inventory items) wired via the standard `IArgumentResolver` path. Both implementations produce identical observable behavior; the Phase B refactor is pure infrastructure.
- **Race conditions.** `get` resolves the entity id before calling `MoveToInventory`. If the item was taken between resolution and mutation, `MoveToInventory` is a no-op (item has no `LocationComponent` to remove; it's not in the room anymore). The command writes "You don't see that here." — acceptable in the absence of a locking model.
- **YAML `spawnRoomId`.** Template items appear in a specific room on boot; this is the authored-world counterpart of ad-hoc items created by `mkitem`. The `spawnRoomId` field uses a room blueprint id (same as `RoomTemplate` exit blueprint ids). If the room is missing at spawn time, the item is created with no `LocationComponent` — it still exists in `EntityService` and persists, but has no ground presence until placed via `setitem` or a future `place` command.
- **`InventoryListMessage`.** A new output message shape. The formatter renders it with a header (`"You are carrying:"`) followed by a bullet-style item list. This is the minimal new output shape for this slice; a styled inventory with columns/weight/value is a future enhancement.
- **Module registration.** `Core/Modules/Items/ItemsModule.cs` exposes `AddItemsModule(IServiceCollection)`, called from `Server/Program.cs`. Registers `IItemSystem`, `IItemBuilderSystem`, all commands, handler, and the `ItemTemplateDeserializer`.

---

## Open Questions

*All must be resolved before implementation begins.*

- **Should existing characters that lack `InventoryComponent` (persisted before this slice) receive an empty component automatically?** Proposed: yes — `CharacterHydrationHandler` checks for the presence of `InventoryComponent` after hydration and attaches an empty one if absent. This prevents a null-deref in `InventoryCommand` for old saves. Alternative: a one-time migration script. **Confirmed scope: hydration guard.** The guard calls `EntityService.AddComponent` only; no `SaveEntityAsync` is called in the guard itself. The component is persisted on the character's next save-on-change event (first `get` or `drop`) or the next periodic flush, whichever comes first. This is acceptable — the guard re-runs harmlessly on each restart until a save occurs.
- **`setitem` targets by blueprint id — is this ergonomic enough?** Admin sees the blueprint id in `mkitem` output. For templated items the blueprint id is known from YAML. For Phase A this is acceptable. Targeting by name/keyword (using the resolver) can be added in Phase B once resolvers exist. **Confirmed scope: blueprint-id target in Phase A, name/keyword in Phase B.**
- **Should `look <item>` check inventory in Phase A or only in Phase B?** Proposed: Phase A checks room only (simpler); Phase B adds inventory fallback. **Confirmed scope: room-only in Phase A.**

---

## Related

- [`command-prefix-matching.md`](command-prefix-matching.md) — slice 3a; introduced `IArgumentResolver` seam and `CommandArgumentResolverContext`; concrete implementations deferred to this slice.
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; `IRoomBuilderSystem` is the direct pattern for `IItemBuilderSystem`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` + `PersistentEntity` model items and inventory follow.
- [`output-framework.md`](output-framework.md) — slice 4; `RoomDescriptionMessage` and `IOutputWriter` are both extended here.
- [`equipment.md`](equipment.md) — slice 7; builds on `ItemDataComponent`, `InventoryComponent`, and item resolver infrastructure from this slice.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; `ITemplateDeserializer` pattern and `WorldContentLoader` extension points.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
