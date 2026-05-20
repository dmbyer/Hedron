# Commands Reference

Living catalog of every registered command. Commands are the thinnest layer — they declare a schema and delegate to domain systems or events. See [`../architecture/06-commands.md`](../architecture/06-commands.md) for the framework design.

**Grouping:** by `CommandCategory`. Within each category, alphabetical by primary verb.

---

## Player commands

### `commands`

**Aliases:** none  
**Location:** `Core/Modules/Help/Commands/CommandsCommand.cs`  
**Description:** Prints a category-grouped one-line index of all commands visible to the caller. Same visibility filtering as `help` (admin commands hidden when their `RequiredPrivileges` are unsatisfied).  
**Usage:** `commands`  
**Schema:** no arguments  
**Events:** none

---

### `down` / `d`

**Aliases:** `d`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move down if an exit exists.  
**Usage:** `down`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `east` / `e`

**Aliases:** `e`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move east if an exit exists.  
**Usage:** `east`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `help` / `?`

**Aliases:** `?`  
**Location:** `Core/Modules/Help/Commands/HelpCommand.cs`  
**Description:** With no argument, lists all commands visible to the caller grouped by category. With a verb argument, shows `LongDescription` and `Usage` for that command.  
**Usage:** `help [<verb>]`  
**Schema:** optional `Token string "verb"`  
**Events:** none

---

### `look` / `l`

**Aliases:** `l`  
**Location:** `Core/Modules/World/Commands/LookCommand.cs`  
**Description:** Displays the current room description, visible exits, and other players present. Delegates to `IBroadcastSystem.SendRoomDescriptionAsync`; output is not yet routed through the `IOutputWriter` formatter (deferred to slice 4).  
**Usage:** `look`  
**Schema:** no arguments  
**Events:** none

---

### `north` / `n`

**Aliases:** `n`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move north if an exit exists.  
**Usage:** `north`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `say`

**Aliases:** none  
**Location:** `Core/Modules/Chat/Commands/SayCommand.cs`  
**Description:** Broadcasts a message to all players in the current room.  
**Usage:** `say <message>`  
**Schema:** `RestOfLine string "message"` (required)  
**Events:** `PlayerSaidEvent`

---

### `south` / `s`

**Aliases:** `s`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move south if an exit exists.  
**Usage:** `south`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `up` / `u`

**Aliases:** `u`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move up if an exit exists.  
**Usage:** `up`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

### `west` / `w`

**Aliases:** `w`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move west if an exit exists.  
**Usage:** `west`  
**Schema:** no arguments  
**Events:** `PlayerMovedEvent`

---

## Admin commands

All admin commands require `AdminRequirement`. The dispatcher enforces this via `IAuthorizationChecker`; no per-command `IsPrivileged` call is needed or permitted.

---

### `dig`

**Aliases:** none  
**Location:** `Core/Modules/Admin/Commands/DigCommand.cs`  
**Description:** Adds an exit from the current room to the target room and wires the reverse link by default. Updates the in-memory `RoomTemplate` so a same-session `reload` won't undo the change. The source YAML file is not rewritten; durability comes from `PersistenceSystem`.  
**Usage:** `dig <direction> <targetRoomBlueprintId>`  
**Schema:** `Token Direction "direction"` (required), `Token string "targetRoomBlueprintId"` (required)  
**Events:** `RoomExitAuthoredByAdminEvent`

---

### `reload`

**Aliases:** none  
**Location:** `Core/Modules/Admin/Commands/ReloadCommand.cs`  
**Description:** Re-scans the content directory and refreshes the template registry. Newly authored templates with no live counterpart are seeded. **Existing live entities are not modified** — descriptions, exits, and components on rooms that already exist will not change. To pick up edits to a live room, restart, or use `dig` for exit changes.  
**Usage:** `reload`  
**Schema:** no arguments  
**Events:** `ContentReloadedEvent`

---

### `spawn`

**Aliases:** none  
**Location:** `Core/Modules/Admin/Commands/SpawnCommand.cs`  
**Description:** Spawns a templated entity into the world. In slice 3 this creates an orphan entity (rooms/areas); use `dig` to wire a new room in. Item and mob placement land with their slices.  
**Usage:** `spawn <blueprintId>`  
**Schema:** `Token string "blueprintId"` (required)  
**Events:** `EntitySpawnedByAdminEvent`

---

### `teleport` / `tp`

**Aliases:** `tp`  
**Location:** `Core/Modules/Admin/Commands/TeleportCommand.cs`  
**Description:** Teleports the invoker to a target room (by blueprint id) or to a player's current room (by display name).  
**Usage:** `teleport <roomBlueprintId|playerName>`  
**Schema:** `Token string "target"` (required)  
**Events:** `PlayerTeleportedByAdminEvent`

---

## Adding a new command

Use the `add-command` skill (`.claude/skills/add-command/SKILL.md`) for step-by-step guidance on the new `ICommand` shape, argument schema, privilege declaration, and output.
