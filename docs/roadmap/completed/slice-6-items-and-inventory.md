# Phase 3 slice 6 — Items + inventory + `get`/`drop`/`look <item>` (completed)

> Implemented on branch `claude/quizzical-robinson-3ac390` (Phase A PR #80; Phase B this PR). Living feature docs: [`../../features/items/items.md`](../../features/items/items.md).

## Outcome

Items are now first-class entities in the world. An item carries `ItemDataComponent` (`Name`, `Description`, `Keywords`, `ItemType`) and is placed in a room via `LocationComponent` or carried in a character's `InventoryComponent`. Phase A shipped the item entity model, the admin authoring path (`mkitem`/`setitem`), YAML `kind: item` content pipeline, room description with items, and `look <item>` (room-only scan). Phase B shipped `InventoryComponent`, the `get`/`drop`/`inventory` player commands with concrete `IArgumentResolver` implementations (`ItemInRoomResolver`, `ItemInInventoryResolver`), the `IArgumentResolver` return-type update to `IReadOnlyList<ResolvedCandidate>?` (enabling keyword deduplication), `ItemInteractionHandler` for broadcast fan-out, and `look <item>` inventory fallback. Together the two phases deliver the full item lifecycle: exists → picked up → carried → dropped.

## Shipped pieces

| Surface | Location |
|---|---|
| `ItemDataComponent` — `Name`, `Description`, `Keywords`, `ItemType`, `[Persistent]` | `Core/ECS/Components/ItemDataComponent.cs` |
| `InventoryComponent` — `List<uint> ItemEntityIds`, `[Persistent]` (cross-cutting) | `Core/ECS/Components/InventoryComponent.cs` |
| `ItemType` enum — `None`, `Weapon`, `Armor`, `Consumable`, `Container`, `Misc` | `Core/ECS/Components/ItemType.cs` |
| `ResolvedCandidate` record struct — `(string MatchString, string CanonicalValue)` | `Core/Commands/ResolvedCandidate.cs` |
| `IArgumentResolver` — return type updated to `IReadOnlyList<ResolvedCandidate>?` | `Core/Commands/IArgumentResolver.cs` |
| `CommandArgumentParser` — deduplication by `CanonicalValue` after prefix match | `Core/Commands/CommandArgumentParser.cs` |
| `IItemSystem` / `ItemSystem` — query + mutation (room/inventory get, find, MoveToInventory, DropToRoom) | `Core/Modules/Items/Systems/IItemSystem.cs`, `ItemSystem.cs` |
| `IItemBuilderSystem` / `ItemBuilderSystem` — ad-hoc creation and property mutation | `Core/Modules/Items/Systems/IItemBuilderSystem.cs`, `ItemBuilderSystem.cs` |
| `ItemInRoomResolver` — `IArgumentResolver` over items in the invoker's room | `Core/Modules/Items/Resolvers/ItemInRoomResolver.cs` |
| `ItemInInventoryResolver` — `IArgumentResolver` over the invoker's inventory | `Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs` |
| `MkitemCommand` — admin `mkitem [name]` | `Core/Modules/Items/Commands/MkitemCommand.cs` |
| `SetitemCommand` — admin `setitem <blueprintId> <property> <value>` | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| `GetCommand` — player `get <item>` with `ItemInRoomResolver` | `Core/Modules/Items/Commands/GetCommand.cs` |
| `DropCommand` — player `drop <item>` with `ItemInInventoryResolver` | `Core/Modules/Items/Commands/DropCommand.cs` |
| `InventoryCommand` — player `inventory`/`inv`/`i` | `Core/Modules/Items/Commands/InventoryCommand.cs` |
| `LookCommand` — extended with room-first + inventory-fallback item scan | `Core/Modules/World/Commands/LookCommand.cs` |
| `ItemInteractionHandler` — pickup/drop broadcast fan-out (priority 80) | `Core/Modules/Items/Handlers/ItemInteractionHandler.cs` |
| `ItemPickedUpEvent`, `ItemDroppedEvent` | `Core/Modules/Items/Events/` |
| `ItemCreatedByAdminEvent`, `ItemPropertySetByAdminEvent` | `Core/Modules/Items/Events/` |
| `ItemTemplate` + `ItemTemplateDeserializer` — YAML `kind: item` deserialization | `Core/Modules/Items/Templates/ItemTemplate.cs`, `ItemTemplateDeserializer.cs` |
| `InventoryListMessage` — new output message shape | `Core/Output/InventoryListMessage.cs` |
| `TelnetOutputFormatter` — `InventoryListMessage` case added; `RoomDescriptionMessage.Items` already present from Phase A | `Core/Output/TelnetOutputFormatter.cs` |
| `AccountSystem.CreateCharacterAsync` — attaches empty `InventoryComponent` to new characters | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `CharacterHydrationHandler` — migration guard: attaches empty `InventoryComponent` to pre-Phase-B persisted characters | `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` |
| `WorldContentLoader` — extended to handle `kind: item`, `SpawnMissingEntities` for items, `PlaceItemsInRooms` (newly-spawned only) | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `BroadcastSystem.SendRoomDescriptionAsync` — populates `RoomDescriptionMessage.Items` | `Core/Systems/BroadcastSystem.cs` |
| `ItemsModule` — registers all new types; `Program.cs` subscribes `ItemInteractionHandler` | `Core/Modules/Items/ItemsModule.cs`, `Server/Program.cs` |
| `docs/reference/components.md` — `InventoryComponent` row added | — |
| `docs/reference/systems.md` — `IItemSystem` entry updated with all 6 methods | — |
| `docs/reference/handlers.md` — `ItemInteractionHandler` added; `CharacterHydrationHandler` updated with migration guard note | — |
| `docs/reference/commands.md` — `get`, `drop`, `inventory` added; `look` entry updated | — |
| `docs/architecture/flows/README.md` — flows 9 (pickup), 10 (drop), 11 (inventory) added; flows 3 and 6 updated | — |

## Spec-review provenance

**Spec-mode gate (Phase A):** Passed before Phase A implementation. Blocking findings resolved in the doc before code:
- `LookCommand` optional-arg design clarified (Phase A uses `RestOfLine`, no resolver; Phase B to add inventory fallback via manual scan).
- `PlaceItemsInRooms` confirmed to operate on `newlySpawned` set only, preventing overwrite of carried items' missing `LocationComponent`.
- `setitem` targets by blueprint id in Phase A; name/keyword targeting deferred to Phase B.

**Code-mode gate (Phase A → PR #80):** Passed before merge.

**Code-mode gate (Phase B — this PR):** Required NEEDS CHANGES on doc gaps only (all INV-16 / INV-17 — no code logic violations). All blocking findings resolved:
- `InventoryComponent` added to `docs/reference/components.md`.
- `IItemSystem` entry in `docs/reference/systems.md` updated with Phase B methods.
- `ItemInteractionHandler` added and `CharacterHydrationHandler` updated in `docs/reference/handlers.md`.
- `get`, `drop`, `inventory` added and `look` updated in `docs/reference/commands.md`.
- Flows 9, 10, 11 added; flows 3 and 6 updated in `docs/architecture/flows/README.md`.

## Notable design points

- **Item location model.** Items on the ground carry `LocationComponent`; items in inventory have **no** `LocationComponent` — tracked exclusively by `InventoryComponent.ItemEntityIds`. This keeps room queries clean (`ItemDataComponent` + `LocationComponent.RoomEntityId == roomId`) and eliminates null-sentinel room tracking.
- **Dropped items vanish on restart by design.** `DropCommand` saves only the player entity, not the item. The item's last-persisted state has no `LocationComponent` (saved during pickup), so it appears nowhere on next restart. Template items are re-placed in their YAML `spawnRoomId` by `PlaceItemsInRooms`; `mkitem` items simply vanish. This is an explicit policy decision — if "items persist where dropped" is needed, a future slice saves the item after drop and removes the `PlaceItemsInRooms` re-placement for items that have a saved location.
- **`ResolvedCandidate` deduplication.** The `IArgumentResolver` return-type change from `IReadOnlyList<string>?` to `IReadOnlyList<ResolvedCandidate>?` enables keyword aliases to share a `CanonicalValue`. The parser deduplicates by `CanonicalValue` after prefix matching — typing "sword" when both the name "a short sword" and the keyword "sword" match yields a single canonical match, not ambiguity.
- **`CharacterHydrationHandler` migration guard.** Characters persisted before Phase B lack `InventoryComponent`. The guard attaches an empty one at hydration without calling `SaveEntityAsync` — the component is persisted on the character's next save-on-change event, harmlessly re-run on each restart until a save occurs.
- **`ItemInteractionHandler` uses `SendToRoomAsync` with opposing filters.** Rather than adding a `SendToPlayerAsync` to `IBroadcastSystem`, the handler calls `SendToRoomAsync(roomId, msg, id => id == playerEntityId)` for the confirmation and the inverse filter for the broadcast. This keeps the interface surface minimal; a `SendToPlayerAsync` method can be added when a third handler needs it.
- **Phase B's `look <item>` keeps the manual scan.** The spec notes that Phase B could replace the Phase A inline scan with an `ItemInContextResolver`. The as-built implementation extends the manual scan to check inventory as a fallback rather than introducing a third resolver class — observable behavior is identical and the infrastructure cost is lower.

## Deviations from the use-case doc

None. All Phase A and Phase B postconditions were satisfied as written. The `look <item>` inventory fallback was implemented as a direct extension of the existing Phase A inline scan (spec notes both produce identical observable behavior).

## Follow-ups unlocked

- **Slice 7 — Equipment + `wear`/`remove`.** `ItemDataComponent.ItemType`, `InventoryComponent`, and the resolver infrastructure are all live. Equipment slot validation can query `ItemType` without any item-model changes.
- **Slice 12 — Shopping.** Items can be owned by vendor entities using `InventoryComponent`; `get` from vendor inventory is the purchase path.
- **Slice 13 — Crafting, potions.** Item entity lifecycle (create → inventory → consume/transform) is established.
- **`PlaceItemsInRooms` Phase B addition (deferred).** A second pass to re-place restored template items with no `LocationComponent` and not in any player's inventory is noted in the use-case design notes as a future enhancement. Currently template items dropped before restart re-appear in their `spawnRoomId` on next startup via the existing `PlaceItemsInRooms` newlySpawned logic.
