# Commands Reference

Living catalog of every registered command. Commands are the thinnest layer — they declare a schema and delegate to domain systems or events. See [`../architecture/subsystems/commands.md`](../architecture/subsystems/commands.md) for the framework design.

**Grouping:** by `CommandCategory`. Within each category, alphabetical by primary verb.

**`MatchingMode`** — every command declares `CommandMatchingMode.Partial` (prefix resolution enabled; player commands) or `CommandMatchingMode.Full` (exact match required; admin commands). See `subsystems/commands.md` for the two-phase lookup rules and `IVerbRegistry` for the read-only interface that exposes the command namespace to `HelpCommand` and future tab-completion.

---

## Player commands

### `commands`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Help/Commands/CommandsCommand.cs`  
**Description:** Prints a category-grouped one-line index of all commands visible to the caller. Same visibility filtering as `help` (admin commands hidden when their `RequiredPrivileges` are unsatisfied).  
**Usage:** `commands`  
**Schema:** no arguments  
**Events:** none

---

### `drop`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/DropCommand.cs`  
**Description:** Drops a named item from the player's inventory to the ground in the current room. Argument is resolved via `ItemInInventoryResolver` (prefix-matched against carried item names and keywords). On no match: "You aren't carrying that." `IItemSystem.DropToRoom` mutates ECS state (removes from `InventoryComponent`, attaches `LocationComponent`); the player entity is saved immediately; the item entity is **not** saved (dropped items vanish on restart by design — see the items-and-inventory use-case spec). Broadcasts drop messages to the room via `ItemInteractionHandler`.  
**Usage:** `drop <item>`  
**Schema:** `Token string "item"` (required, `ItemInInventoryResolver`)  
**Dependencies:** `IItemSystem`, `EntityService`, `IEventBus`, `IPersistenceSystem`, `ItemInInventoryResolver`  
**Events:** `ItemDroppedEvent`

---

### `down` / `d`

**Aliases:** `d`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move down if an exit exists.  
**Usage:** `down`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `east` / `e`

**Aliases:** `e`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move east if an exit exists.  
**Usage:** `east`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `get`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/GetCommand.cs`  
**Description:** Picks up a named item from the ground in the current room and adds it to the player's inventory. Argument is resolved via `ItemInRoomResolver` (prefix-matched against room item names and keywords). On no match after resolver: "You don't see that here." (handles race condition where item was taken between resolve and pickup). `IItemSystem.MoveToInventory` mutates ECS state (removes `LocationComponent` from item, appends to `InventoryComponent`); both item and player entities are saved immediately. Broadcasts pickup messages to the room via `ItemInteractionHandler`.  
**Usage:** `get <item>`  
**Schema:** `Token string "item"` (required, `ItemInRoomResolver`)  
**Dependencies:** `IItemSystem`, `EntityService`, `IEventBus`, `IPersistenceSystem`, `ItemInRoomResolver`  
**Events:** `ItemPickedUpEvent`

---

### `help` / `?`

**Aliases:** `?`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Help/Commands/HelpCommand.cs`  
**Description:** With no argument, lists all commands visible to the caller grouped by category. With a verb argument, shows `LongDescription` and `Usage` for that command.  
**Usage:** `help [<verb>]`  
**Schema:** optional `Token string "verb"`  
**Events:** none

---

### `inventory` / `inv` / `i`

**Aliases:** `inv`, `i`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/InventoryCommand.cs`  
**Description:** Lists the names of all items the player is currently carrying. If inventory is empty, writes "You are carrying nothing." Otherwise writes an `InventoryListMessage` (rendered as `"You are carrying:"` header + item list). No events fired.  
**Usage:** `inventory`  
**Schema:** no arguments  
**Dependencies:** `IItemSystem`, `EntityService`  
**Events:** none

---

### `look` / `l`

**Aliases:** `l`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/World/Commands/LookCommand.cs`  
**Description:** With no argument, displays the current room description, visible exits, other players present, and items on the ground (via `IBroadcastSystem.SendRoomDescriptionAsync`). With a target argument, prefix-matches first against items in the current room, then falls back to items in the player's inventory (both by name and keywords); shows the item's name and description. Writes "You don't see that here." on no match in either location.  
**Usage:** `look [target]`  
**Schema:** `RestOfLine string "target"` (optional)  
**Dependencies:** `EntityService`, `IBroadcastSystem`, `IItemSystem`  
**Events:** none

---

### `north` / `n`

**Aliases:** `n`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move north if an exit exists.  
**Usage:** `north`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `say`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Chat/Commands/SayCommand.cs`  
**Description:** Broadcasts a message to all players in the current room.  
**Usage:** `say <message>`  
**Schema:** `RestOfLine string "message"` (required)  
**Events:** `PlayerSaidEvent`

---

### `south` / `s`

**Aliases:** `s`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move south if an exit exists.  
**Usage:** `south`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `up` / `u`

**Aliases:** `u`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move up if an exit exists.  
**Usage:** `up`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `west` / `w`

**Aliases:** `w`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move west if an exit exists.  
**Usage:** `west`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

## Admin commands

All admin commands require `AdminRequirement`. The dispatcher enforces this via `IAuthorizationChecker`; no per-command `IsPrivileged` call is needed or permitted.

---

### `mkitem`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Items/Commands/MkitemCommand.cs`  
**Description:** Creates an ad-hoc item entity in the invoker's current room. Delegates to `IItemBuilderSystem.CreateItem`; the item gets `ItemDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent`. Prints the blueprint id (format `item.adhoc.<shortid>`) so the admin can configure it with `setitem`. Saves the item entity immediately.  
**Usage:** `mkitem [name]`  
**Schema:** `RestOfLine string "name"` (optional, default `"an item"`)  
**Events:** `ItemCreatedByAdminEvent`

---

### `setitem`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Items/Commands/SetitemCommand.cs`  
**Description:** Sets a property on an existing item entity identified by blueprint id. Validates the blueprint id exists in `ITemplateRegistry`; resolves the live entity by `BlueprintComponent.BlueprintId`. Delegates mutation to `IItemBuilderSystem`. Saves the item entity immediately.  
**Usage:** `setitem <blueprintId> <property> <value>`  
**Schema:** `Token string "blueprintId"` (required), `Token string "property"` (required: `name`, `description`, `keywords`, `type`), `RestOfLine string "value"` (required). For `keywords`, value is split on whitespace. For `type`, value must parse as `ItemType` enum.  
**Events:** `ItemPropertySetByAdminEvent`

---

### `dig`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/DigCommand.cs`  
**Description:** Creates a new room entity in the named direction from your current position, wires bidirectional exits, and auto-moves you into the new room. Delegates creation and exit wiring to `IRoomBuilderSystem`. The new room is registered in `ITemplateRegistry` and its blueprint id (format `room.adhoc.<shortid>`) is shown in the confirmation message. Replaces the slice-2 `dig <direction> <targetRoomBlueprintId>` syntax.  
**Usage:** `dig <direction> [name]`  
**Schema:** `Token Direction "direction"` (required), `RestOfLine string "name"` (optional, default `"New Room"`)  
**Events:** `RoomCreatedByAdminEvent`, `PlayerMovedEvent`

---

### `set`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/SetCommand.cs`  
**Description:** Sets a property on the room you are currently standing in. `name` updates `RoomComponent.Name`; `description` updates `RoomComponent.Description`. The room is marked dirty by `PersistenceHandler` on the next flush. Expanding `set` to other entity types is deferred to slices 6+.  
**Usage:** `set <name|description> <value>`  
**Schema:** `Token string "property"` (required, `name` or `description`), `RestOfLine string "value"` (required)  
**Events:** `RoomPropertySetByAdminEvent`

---

### `reload`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/ReloadCommand.cs`  
**Description:** Re-scans the content directory and refreshes the template registry. Newly authored templates with no live counterpart are seeded. **Existing live entities are not modified** — descriptions, exits, and components on rooms that already exist will not change. To pick up edits to a live room, restart, or use `dig` for exit changes.  
**Usage:** `reload`  
**Schema:** no arguments  
**Events:** `ContentReloadedEvent`

---

### `spawn`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/SpawnCommand.cs`  
**Description:** Spawns a templated entity into the world. In slice 3 this creates an orphan entity (rooms/areas); use `dig` to wire a new room in. Item and mob placement land with their slices.  
**Usage:** `spawn <blueprintId>`  
**Schema:** `Token string "blueprintId"` (required)  
**Events:** `EntitySpawnedByAdminEvent`

---

### `whois`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Account/Commands/WhoisCommand.cs`  
**Description:** Looks up account and character info for a given character name. Displays character entity id, account entity id, account username, and last login time.  
**Usage:** `whois <characterName>`  
**Schema:** `Token string "characterName"` (required)  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

### `teleport` / `tp`

**Aliases:** `tp`  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/TeleportCommand.cs`  
**Description:** Teleports the invoker to a target room (by blueprint id) or to a player's current room (by display name).  
**Usage:** `teleport <roomBlueprintId|playerName>`  
**Schema:** `Token string "target"` (required)  
**Events:** `PlayerTeleportedByAdminEvent`

---

## Adding a new command

Use the `add-command` skill (`.claude/skills/add-command/SKILL.md`) for step-by-step guidance on the new `ICommand` shape, argument schema, privilege declaration, and output.
