# Commands Reference

Living catalog of every registered command. Commands are the thinnest layer — they declare a schema and delegate to domain systems or events. See [`../features/commands/command-framework.md`](../features/commands/command-framework.md) for the framework design.

**Grouping:** by `CommandCategory`. Within each category, alphabetical by primary verb.

**`MatchingMode`** — every command declares `CommandMatchingMode.Partial` (prefix resolution enabled; player commands) or `CommandMatchingMode.Full` (exact match required; admin commands). See [`../features/commands/command-framework.md`](../features/commands/command-framework.md) for the three-phase lookup rules and `IVerbRegistry` for the read-only interface that exposes the command namespace to `HelpCommand` and future tab-completion.

---

## Player commands

### `affects`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Effects/Commands/AffectsCommand.cs`  
**Description:** Lists all effects currently active on the invoking player, including effect id, category, power (signed), and remaining duration (or `permanent` for `UntilRemoved` effects). Writes "You have no active effects." when the effects list is empty. No events fired.  
**Usage:** `affects`  
**Schema:** no arguments  
**Dependencies:** `IEffectSystem`  
**Events:** none

---

### `abilities`

**Aliases:** none
**MatchingMode:** `Partial`
**Location:** `Core/Modules/Abilities/Commands/AbilitiesCommand.cs`
**Description:** Lists known abilities that are not classified as Skill-kind or Spell-kind (future kinds such as stances, racials, feats). When the known set contains only skills/spells, writes "You have no other abilities. Use 'skills' to see your skills and 'spells' to see your spells." When nothing is known at all, writes "You have no abilities. Use 'skills' or 'spells' to see what can be learned." No events fired.
**Usage:** `abilities`
**Schema:** no arguments
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`
**Events:** none

---

### `cast` / `c`

**Aliases:** `c`
**MatchingMode:** `Partial`
**Location:** `Core/Modules/Abilities/Commands/CastCommand.cs`
**Description:** Invokes a known Active Spell. Spell argument is resolved via `KnownSpellResolver` (prefix-matched against known Active Spells by id and display name). Delegates the full invocation pipeline to `AbilityInvocationPipeline`: target resolution (Self → actor, explicit token → `TryFindTargetInRoom`, in-combat → current opponent, offensive + no token → "{spell} whom?" prompt), combat entry for offensive spells not already fighting, `IAbilitySystem.Activate` with `resolveOffensiveExternally: true` for offensive spells, event publication, and `ICombatSystem.ResolveAbilityStrike` for offensive spells. On no spell match: "You don't know that spell."
**Usage:** `cast <spell> [target]`
**Schema:** `Token string "spell"` (required, `KnownSpellResolver`), `RestOfLine string "target"` (optional)
**Dependencies:** `IAbilityRegistry`, `KnownSpellResolver`, `AbilityInvocationPipeline`
**Events:** `CombatStartedEvent` (offensive, opens combat), `AbilityActivatedEvent`, `EffectAppliedEvent` (per applied effect), `AbilityStrikeResolvedEvent` (offensive only)

---

### `skills`

**Aliases:** none
**MatchingMode:** `Partial`
**Location:** `Core/Modules/Abilities/Commands/AbilitiesCommand.cs`
**Description:** Lists all known Skill-kind abilities. For each Active Skill shows the invocation verb (`[invoke: <id>]`) alongside the standard ability display line (id, Kind, Activation, Targeting, costs, cooldown). Writes "You know no skills." when empty. Footer cross-reference to `spells` and `help <skill-name>`. No events fired.
**Usage:** `skills`
**Schema:** no arguments
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`
**Events:** none

---

### `spells`

**Aliases:** none
**MatchingMode:** `Partial`
**Location:** `Core/Modules/Abilities/Commands/AbilitiesCommand.cs`
**Description:** Lists all known Spell-kind abilities. For each Active Spell shows the invocation form (`[invoke: cast <id>]`) alongside the standard ability display line. Writes "You know no spells." when empty. Footer cross-reference to `skills` and `help <spell-name>`. No events fired.
**Usage:** `spells`
**Schema:** no arguments
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`
**Events:** none

---

### `commands`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** [`Core/Modules/Help/Commands/CommandsCommand.cs`](../../Core/Modules/Help/Commands/CommandsCommand.cs)  
**Description:** Prints a category-grouped one-line index of all commands visible to the caller. Same visibility filtering as `help` (admin commands hidden when their `RequiredPrivileges` are unsatisfied). See [`../features/communication/help-system.md`](../features/communication/help-system.md).  
**Usage:** `commands`  
**Schema:** no arguments  
**Events:** none  
**UsableWhileIncapacitated:** `true`

---

### `equipment` / `eq`

**Aliases:** `eq`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/EquipmentCommand.cs`  
**Description:** Lists all items currently worn or wielded by the player, grouped by slot. Renders an `EquipmentDisplayMessage` (slot label + item name table, ordered by `WornSlot` enum ordinal). Writes "You are not wearing anything." when all slots are empty. No events fired.  
**Usage:** `equipment`  
**Schema:** no arguments  
**Dependencies:** `EntityService`  
**Events:** none

---

### `drop`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/DropCommand.cs`  
**Description:** Drops a named item from the player's inventory to the ground in the current room. Argument is resolved via `ItemInInventoryResolver` (prefix-matched against carried item names and keywords). On no match: "You aren't carrying that." `IItemSystem.DropToRoom` mutates ECS state; the player entity is saved immediately; the item entity is **not** saved (drop-and-vanish policy — see [`../features/items/item-inventory-system.md`](../features/items/item-inventory-system.md)). Broadcasts drop messages to the room via `ItemInteractionHandler`.  
**Usage:** `drop <item>`  
**Schema:** `Token string "item"` (required, `ItemInInventoryResolver`)  
**Dependencies:** `IItemSystem`, `EntityService`, `IEventBus`, `IPersistenceSystem`, `ItemInInventoryResolver`  
**Events:** `ItemDroppedEvent`

---

### `down` / `d`

**Aliases:** `d`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move down if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `down`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

### `east` / `e`

**Aliases:** `e`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move east if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `east`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

### `flee`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Combat/Commands/FleeCommand.cs`  
**Description:** Exits combat immediately. Always succeeds — no fail roll in Phase 3. Checks `IEntityStateService.IsInState(InCombat)`; if not in combat writes "You are not in combat." and returns. Reads `CombatStateComponent.OpponentEntityId`, calls `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` on both participants, then publishes `CombatEndedEvent(PlayerFled)`. Output via `CombatHandler`.  
**Usage:** `flee`  
**Schema:** no arguments  
**Dependencies:** `ICombatSystem`, `IEntityStateService`, `EntityService`, `IEventBus`  
**Events:** `CombatEndedEvent`

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
**Location:** [`Core/Modules/Help/Commands/HelpCommand.cs`](../../Core/Modules/Help/Commands/HelpCommand.cs)  
**Description:** With no argument, lists all commands visible to the caller grouped by category. With a verb argument, shows `LongDescription` and `Usage` for that command; falls through to `IAbilityRegistry` when no command matches. Special topics `skills`/`spells`/`abilities` append a global ability catalog. See [`../features/communication/help-system.md`](../features/communication/help-system.md) for the full lookup design.  
**Usage:** `help [<verb>]`  
**Schema:** optional `Token string "verb"`  
**Events:** none  
**UsableWhileIncapacitated:** `true`

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

### `kill` / `k`

**Aliases:** `k`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Combat/Commands/KillCommand.cs`  
**Description:** Initiates melee combat with a mob in the current room. Guards: if already `InCombat` writes "You are already fighting!" and returns. Resolves target via `ICombatSystem.TryFindTargetInRoom` (prefix-match against `MobDataComponent.Name` and `Keywords`); on no match writes "You don't see that here." Calls `IEntityStateService.TryEnterState(InCombat)` on both player and mob; on blocked player transition writes the fail reason. Calls `ICombatSystem.StartCombat` to attach `CombatStateComponent` on both. Publishes `CombatStartedEvent`; output via `CombatHandler`.  
**Usage:** `kill <target>`  
**Schema:** `RestOfLine string "target"` (required)  
**Dependencies:** `ICombatSystem`, `IEntityStateService`, `EntityService`, `IEventBus`, `ILogger<KillCommand>`  
**Events:** `CombatStartedEvent`

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
**Description:** Move north if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `north`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

### `remove`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/RemoveCommand.cs`  
**Description:** Takes off a worn or wielded item and returns it to the player's inventory. Argument is resolved via `ItemInEquipmentResolver` (prefix-matched against worn item names and keywords). On no match: "You aren't wearing that." `IEquipmentSystem.RemoveItem` clears the slot(s) in `EquipmentComponent` and appends the item id to `InventoryComponent`; player entity is saved immediately. Broadcasts remove messages to the room via `EquipmentInteractionHandler`.  
**Usage:** `remove <item>`  
**Schema:** `Token string "item"` (required, `ItemInEquipmentResolver`)  
**Dependencies:** `IEquipmentSystem`, `IEventBus`, `IPersistenceSystem`, `ItemInEquipmentResolver`  
**Events:** `ItemUnequippedEvent`

---

### `rest`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Regeneration/Commands/RestCommand.cs`  
**Description:** Enters the `Resting` state, accelerating regeneration of all resource pools (HP/Mana/Stamina/Astra) to every-tick rate. Blocked while `InCombat` or `Incapacitated` (writes the `failReason` from `IEntityStateService`). Writes "You are already resting." if already in `Resting` state. On success writes "You sit down and begin to rest."  
**Usage:** `rest`  
**Schema:** no arguments  
**Dependencies:** `IEntityStateService`, `IEventBus`  
**Events:** `EntityStateChangedEvent`

---

### `list`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Shopping/Commands/ListCommand.cs`  
**Description:** Browses a shopkeeper's wares — base stock and the buy-back shelf together — showing each item's name and compute-on-read buy price (via `CurrencyFormatter`); acquired (buy-back) rows are flagged. The shopkeeper may be named (resolved by the shared `MobInRoomResolver`) or defaults to the one shop in the room. No state mutation. (Distinct from the admin `listents` entity inspector.)  
**Usage:** `list [shopkeeper]`  
**Schema:** `Token string "shopkeeper"` (optional, `MobInRoomResolver`)  
**Dependencies:** `IShopSystem`, `EntityService`, `ICurrencyRegistry`, `MobInRoomResolver`  
**Events:** none

---

### `buy`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Shopping/Commands/BuyCommand.cs`  
**Description:** Buys a named item from the shopkeeper in the current room — works for both base stock and buy-back-shelf items (same verb). Resolves the implicit shopkeeper and the item (`IItemSystem.TryFindItemInInventory` against the shop), calls `IShopSystem.TryResolveBuy` (price + affordability), and on success does `IWalletSystem.Transfer(player → till)` + `IItemSystem.MoveBetweenInventories(shop → player)` and publishes `ItemBoughtEvent`. Insufficient funds → refusal, no mutation. All pricing/affordability rules live in `IShopSystem` (INV-8).  
**Usage:** `buy <item>`  
**Schema:** `Token string "item"` (required)  
**Dependencies:** `IShopSystem`, `IItemSystem`, `IWalletSystem`, `EntityService`, `IEventBus`  
**Events:** `ItemBoughtEvent`

---

### `sell`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Shopping/Commands/SellCommand.cs`  
**Description:** Sells a named item from the player's inventory to the shopkeeper in the room. Calls `IShopSystem.TryResolveSell` (price = `Value × SellRatio`; rejects `Value == 0`; checks the till can afford; returns the clock-derived `ExpiresAt`), then `IWalletSystem.Transfer(till → player)` + `IItemSystem.MoveBetweenInventories(player → shop)`, stamps `ShopStockComponent { Acquired, ExpiresAt }`, and publishes `ItemSoldEvent`. Dry till or valueless item → refusal, no mutation.  
**Usage:** `sell <item>`  
**Schema:** `Token string "item"` (required)  
**Dependencies:** `IShopSystem`, `IItemSystem`, `IWalletSystem`, `EntityService`, `IEventBus`  
**Events:** `ItemSoldEvent`

---

### `score`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Attributes/Commands/ScoreCommand.cs`  
**Description:** Displays the invoking player's Level, HP (`CurrentHp/MaxHp`), Mana, Stamina, Astra, the four attributes (Mind, Body, Spirit, Attunement), and the current respawn room blueprint id (from `RespawnComponent.RoomBlueprintId`; shows `(starting room)` when null). Also shows a highlighted `** INCAPACITATED — bleeding out **` status line when the player is currently incapacitated (from `IEntityStateService`). If `AttributesComponent` or `PoolsComponent` are absent (pre-hydration edge case), defaults are shown. No events fired.  
**Usage:** `score`  
**Schema:** no arguments  
**Dependencies:** `EntityService`, `IStatSystem`, `IEntityStateService`  
**Events:** none  
**UsableWhileIncapacitated:** `true`

---

### `say`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** [`Core/Modules/Chat/Commands/SayCommand.cs`](../../Core/Modules/Chat/Commands/SayCommand.cs)  
**Description:** Broadcasts a message to all players in the current room. See [`../features/communication/chat-system.md`](../features/communication/chat-system.md) for the pipeline design.  
**Usage:** `say <message>`  
**Schema:** `RestOfLine string "message"` (required)  
**Events:** `PlayerSaidEvent`

---

### `south` / `s`

**Aliases:** `s`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move south if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `south`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

### `stand` / `wake`

**Aliases:** `wake`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Regeneration/Commands/StandCommand.cs`  
**Description:** Exits the `Resting` state. Writes "You are already standing." if not currently resting (no `ExitState` call in that path). On success writes "You stand up." and publishes `EntityStateChangedEvent`.  
**Usage:** `stand`  
**Schema:** no arguments  
**Dependencies:** `IEntityStateService`, `IEventBus`  
**Events:** `EntityStateChangedEvent`

---

### `up` / `u`

**Aliases:** `u`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move up if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `up`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

### `wear`

**Aliases:** none  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Items/Commands/WearCommand.cs`  
**Description:** Puts on a wearable or wieldable item from the player's inventory. Argument is resolved via `ItemInInventoryResolver`. Validates the item has `ItemDataComponent.WornSlots` populated; if not, writes "You can't wear that." `IEquipmentSystem.EquipItem` handles the full slot lifecycle: implicitly removes any item occupying the target slot(s) (silently, no event), then moves the new item from `InventoryComponent` into `EquipmentComponent.Slots`. Player entity is saved immediately. Broadcasts wear messages via `EquipmentInteractionHandler`.  
**Usage:** `wear <item>`  
**Schema:** `Token string "item"` (required, `ItemInInventoryResolver`)  
**Dependencies:** `IItemSystem`, `IEquipmentSystem`, `IEventBus`, `IPersistenceSystem`, `ItemInInventoryResolver`  
**Events:** `ItemEquippedEvent`

---

### `west` / `w`

**Aliases:** `w`  
**MatchingMode:** `Partial`  
**Location:** `Core/Modules/Movement/Commands/MoveCommand.cs`  
**Description:** Move west if an exit exists. If the invoker is in the `Resting` state, exits it first with "You stop resting and stand up." before attempting the move.  
**Usage:** `west`  
**Schema:** no arguments  
**Dependencies:** `IMovementSystem`, `IEntityStateService`, `IEventBus`  
**Events:** `PlayerMovedEvent`; `EntityStateChangedEvent` (conditional — only when breaking rest)

---

## Admin commands

All admin commands require `AdminRequirement`. The dispatcher enforces this via `IAuthorizationChecker`; no per-command `IsPrivileged` call is needed or permitted.

---

### `area`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/AreaCommand.cs`  
**Description:** Inspects an area entity. Without an argument, shows the area for the room the invoker is currently in. With a blueprint id, inspects the named area entity. Displays the area name, description, aspect affinities (if any), and the list of rooms assigned to the area (name + blueprint id per room). Writes an error if the current room has no area assignment, or if the given blueprint id is not found. No events fired.  
**Usage:** `area [blueprintId]`  
**Schema:** `Token string "blueprintId"` (optional)  
**Dependencies:** `IAreaSystem`, `EntityService`  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

### `defs`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/DefsCommand.cs`  
**Description:** Generic inspector over every definition registry. Without an id, lists all defined ids in the family. With an id, dumps the full definition. Families: `aspect`, `ability`, `effect`, `score`. Aspect ids are parsed as `AspectId` enum values (case-insensitive); ability and effect ids are string-keyed; score ids are parsed as `ScoreId` enum values. Returns an error message for unknown families or ids. Admin-gated (`AdminRequirement`).  
**Usage:** `defs <family> [id]`  
**Schema:** `Token string "family"` (required), `RestOfLine string "id"` (optional)  
**Dependencies:** `IAspectRegistry`, `IAbilityRegistry`, `IEffectRegistry`, `IStatRegistry`  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

### `affect`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Effects/Commands/AffectCommand.cs`  
**Description:** Applies a named effect from the `IEffectRegistry` to the specified target. Target is resolved by character name (connected players only) or by raw `uint` entity id. If an optional `power` integer is provided, the definition's `BaseMagnitude` and `PowerScalingFormula` are overridden — useful for testing effect boundaries. Returns an error if the effect id is not in the registry, the target cannot be resolved, or `HighestWins` blocks application (existing effect has equal or greater power).  
**Usage:** `affect <target> <effectId> [power]`  
**Schema:** `Token string "target"` (required), `Token string "effectId"` (required), `Token string "power"` (optional)  
**Dependencies:** `IEffectSystem`, `IEffectRegistry`, `EntityService`, `IEventBus`, `ISessionManager`  
**Events:** `EffectAppliedEvent`, `EffectAppliedByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `listents`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/ListEntitiesCommand.cs`  
**Description:** Prints a tabular view of all entities of a given type. Accepts `area` or `room` (case-insensitive). Unknown type token → error message. Columns: Name | ShortDesc (first 15 chars) | BlueprintId (entity id if no `BlueprintComponent`). No events fired. (Renamed from `list` in slice 12-c so the player shop-browse verb `list` owns the unqualified verb.)  
**Usage:** `listents <area|room>`  
**Schema:** `Token string "type"` (required: `area` or `room`)  
**Dependencies:** `EntityService`  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

### `mkarea`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/MkareaCommand.cs`  
**Description:** Creates an ad-hoc area entity. Delegates to `IAreaBuilderSystem.CreateArea`; the area gets `AreaComponent` + `BlueprintComponent` (no `PersistentEntity`). Writes the `AreaTemplate` to YAML via `IAreaContentWriter` before publishing the audit event. Prints the blueprint id (format `area.adhoc.<shortid>`) so the admin can configure it with `setarea`.  
**Usage:** `mkarea [name]`  
**Schema:** `RestOfLine string "name"` (optional, default `"New Area"`)  
**Dependencies:** `IAreaBuilderSystem`, `IAreaContentWriter`, `IEventBus`  
**Events:** `AreaCreatedByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

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

### `mkmob`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Mobs/Commands/MkMobCommand.cs`  
**Description:** Creates an ad-hoc mob entity in the invoker's current room. Delegates to `IMobBuilderSystem.CreateMob`; the mob gets `MobDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent`. Prints the blueprint id (format `mob.adhoc.<shortid>`) so the admin can configure it with `setmob`. Saves the mob entity immediately.  
**Usage:** `mkmob [name]`  
**Schema:** `RestOfLine string "name"` (optional, default `"a mob"`)  
**Events:** `MobCreatedByAdminEvent`

---

### `setitem`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Items/Commands/SetitemCommand.cs`  
**Description:** Sets a property on an existing item entity identified by blueprint id. Validates the blueprint id exists in `ITemplateRegistry`; resolves the live entity by `BlueprintComponent.BlueprintId`. Delegates mutation to `IItemBuilderSystem`. Saves the item entity immediately. Properties: `name`, `description`, `keywords` (space-separated), `type`, `slot`, `value`, `bonus`, `clearbonus`, `tier` (mechanical Ascension tag, prog-3b), `band` (purely descriptive tag, prog-3b, replacing the one-axis `band` from prog-3).  
**Usage:** `setitem <blueprintId> <property> [value]`  
**Schema:** `Token string "blueprintId"` (required), `Token string "property"` (required: `name`, `description`, `keywords`, `type`, `slot`, `value`, `bonus`, `clearbonus`, `tier`, `band`), `RestOfLine string "value"` (optional — required for every property except `clearbonus`). For `keywords`, value is split on whitespace. For `type`, value must parse as `ItemType` enum. For `slot`, value is a space-separated list of `WornSlot` names (e.g. `mainhand`, `chest`, `legs`, `finger`, `wrist2`); an empty list clears `WornSlots`. For `value`, value must be a non-negative integer (base-unit Coin; 0 = valueless/non-saleable). For `bonus`, value is `<score> <amount>` where `<score>` is a `ScoreId` (e.g. `attackpower`, `defense`) and `<amount>` is an integer (0 removes that score's row; negative allowed for cursed gear) — add-or-replaces one worn-stat bonus row. `clearbonus` removes all bonus rows and takes no value. For `tier`, value must be an integer `0`–`6` (0 = unbanded/base); dual-writes the live `ItemDataComponent.Tier` and `ItemTemplate.Tier`. For `band`, value must be an integer `0`–`3` (0 = unbanded); dual-writes `ItemDataComponent.Band` and `ItemTemplate.Band`.  
**Events:** `ItemPropertySetByAdminEvent`

---

### `setmob`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Mobs/Commands/SetMobCommand.cs`  
**Description:** Sets a property on an existing mob entity identified by blueprint id. Validates the blueprint id exists in `ITemplateRegistry`; resolves the live entity by `BlueprintComponent.BlueprintId`. Delegates mutation to `IMobBuilderSystem`. Writes the updated template to YAML via `IMobContentWriter`. Saves the mob entity immediately. Properties: `name`, `description`, `keywords` (space-separated), `type`, `level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `maxmana`, `maxstamina`, `maxastra` (positive integer values), `protection` (flags), `tier` (mechanical Ascension tag, prog-2/3b), `band` (purely descriptive tag, prog-3b, replacing the one-axis `band` from prog-2), `shop`.  
**Usage:** `setmob <blueprintId> <property> <value>`  
**Schema:** `Token string "blueprintId"` (required), `Token string "property"` (required: `name`, `description`, `keywords`, `type`, `level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `maxmana`, `maxstamina`, `maxastra`, `protection`, `tier`, `band`, `shop`), `RestOfLine string "value"` (required). For `keywords`, value is split on whitespace. For `type`, value must parse as `MobType` enum. For numeric properties, value must be a positive integer. For `tier`, value must be an integer `0`–`6` (0 = unbanded/base); dual-writes the live `MobDataComponent.Tier` and `MobTemplate.Tier`. For `band`, value must be an integer `0`–`3` (0 = unbanded); dual-writes `MobDataComponent.Band` and `MobTemplate.Band`.  
**Events:** `MobPropertySetByAdminEvent`

---

### `setplayer`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Attributes/Commands/SetPlayerCommand.cs`  
**Description:** Sets a stat on a currently-connected player by character name (case-insensitive). Resolves player via `ISessionManager.GetAll()`. `hp` sets `MaxHp` and clamps `CurrentHp` if needed; pool current-value properties (`mana`, `stamina`, `astra`) clamp to their respective max. Attribute setters delegate to `IAttributeSystem`. Saves the player entity immediately. Protected by `AdminRequirement`.  
**Usage:** `setplayer <characterName> <property> <value>`  
**Schema:** `Token string "characterName"` (required), `Token string "property"` (required: `level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `mana`, `maxmana`, `stamina`, `maxstamina`, `astra`, `maxastra`), `Token string "value"` (required, positive integer)  
**Events:** `PlayerAttributeSetByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `setrespawn`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Death/Commands/SetRespawnCommand.cs`  
**Description:** Sets the respawn room blueprint id for a currently-connected player. Resolves the target player by character name via `ISessionManager.GetAll()`. Validates the blueprint exists via `IDeathSystem.SetRespawn` (fails with a descriptive error if the blueprint is not in `ITemplateRegistry`). On success: persists the player entity immediately (admin boundary save, INV-22), then publishes `PlayerRespawnSetByAdminEvent` for the audit log.  
**Usage:** `setrespawn <characterName> <roomBlueprintId>`  
**Schema:** `Token string "characterName"` (required), `Token string "roomBlueprintId"` (required)  
**Dependencies:** `IDeathSystem`, `ISessionManager`, `EntityService`, `IEventBus`, `IPersistenceSystem`  
**Events:** `PlayerRespawnSetByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `ascend`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Ascension/Commands/AscendCommand.cs`  
**Description:** Ascends a currently-connected player one character-wide tier (defaults to the invoker when `characterName` is omitted). Resolves the target by character name via `ISessionManager.GetAll()`. Calls `IAscensionSystem.CanAscend` → `TryAscend`; a non-eligible target (e.g. already at `AscensionConstants.MaxTier`) is rejected with no mutation. On success: persists the player entity immediately (admin boundary save, INV-22), then publishes `AscendedEvent` (milestone) and `PlayerAscendedByAdminEvent` (audit log). The real player-facing Ascension-Objective gate is deferred — this command is the interim trigger.  
**Usage:** `ascend [characterName]`  
**Schema:** `Token string "characterName"` (optional)  
**Dependencies:** `IAscensionSystem`, `ISessionManager`, `EntityService`, `IEventBus`, `IPersistenceSystem`  
**Events:** `AscendedEvent`, `PlayerAscendedByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `setwallet`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Economy/Commands/SetwalletCommand.cs`  
**Description:** Absolute-sets the balance of a given currency in a currently-connected player's wallet. Resolves player by character name via `ISessionManager.GetAll()`. Parses `<currency>` as a `CurrencyId` enum name (case-insensitive, e.g. `Coin`) and `<amount>` as a non-negative `long` in base units (copper for Coin). Calls `IWalletSystem.SetBalance`, performs exactly one admin boundary save (`IPersistenceSystem.SaveEntityAsync(targetEntityId)`, INV-22), then publishes `WalletSetByAdminEvent` for the audit log. Protected by `AdminRequirement`.  
**Usage:** `setwallet <characterName> <currency> <amount>`  
**Schema:** `Token string "characterName"` (required), `Token string "currency"` (required: `Coin`), `Token string "amount"` (required, non-negative integer in base units)  
**Dependencies:** `IWalletSystem`, `ISessionManager`, `EntityService`, `IEventBus`, `IPersistenceSystem`  
**Events:** `WalletSetByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `teach`

**Aliases:** none
**MatchingMode:** `Full`
**Location:** `Core/Modules/Abilities/Commands/TeachCommand.cs`
**Description:** Grants a named ability to a connected player (or entity by raw entity id). Target is resolved by character name (connected players only) then by raw `uint` entity id. Validates the ability id exists in `IAbilityRegistry` and that the target does not already know it. On success: performs an admin boundary save (INV-22) then publishes `AbilityLearnedEvent` and `AbilityTaughtByAdminEvent`.
**Usage:** `teach <target> <abilityId>`
**Schema:** `Token string "target"` (required), `Token string "abilityId"` (required)
**Dependencies:** `IAbilitySystem`, `EntityService`, `IEventBus`, `ISessionManager`, `IPersistenceSystem`
**Events:** `AbilityLearnedEvent`, `AbilityTaughtByAdminEvent`
**RequiredPrivileges:** `AdminRequirement`

---

### `useability`

**Aliases:** none
**MatchingMode:** `Full`
**Location:** `Core/Modules/Abilities/Commands/UseAbilityCommand.cs`
**Description:** Admin testing affordance. Invokes the full ability activation pipeline for the invoker (or an optional target entity). Delegates to `IAbilitySystem.Activate`; returns structured failure reasons (unknown ability, not known, not activatable, state blocked, on cooldown, insufficient resources). On `Activated`: publishes `AbilityActivatedEvent` and one `EffectAppliedEvent` per applied non-null effect.
**Usage:** `useability <abilityId> [target]`
**Schema:** `Token string "abilityId"` (required), `Token string "target"` (optional — character name or entity id)
**Dependencies:** `IAbilitySystem`, `EntityService`, `IEventBus`, `ISessionManager`
**Events:** `AbilityActivatedEvent`, `EffectAppliedEvent` (per applied effect)
**RequiredPrivileges:** `AdminRequirement`

> **Internal: `SkillInvocationCommand`** (NOT a discoverable `ICommand`). Called by `CommandDispatcher` Phase 3 after `IAbilityVerbResolver` confirms a unique Active Skill match. Delegates to `AbilityInvocationPipeline` for the full target-resolution → combat-entry → Activate → event-publish → strike chain. Location: `Core/Modules/Abilities/Commands/SkillInvocationCommand.cs`.

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

### `setarea`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/SetAreaCommand.cs`  
**Description:** Assigns a room entity to an area entity, both identified by blueprint id. Resolves both entities by scanning `BlueprintComponent`; rejects if the room or area blueprint is not found. Calls `IAreaSystem.AssignRoomToArea` to set `RoomComponent.AreaEntityId` and mirrors the `AreaId` to the `RoomTemplate`; writes the updated room template to YAML via `IRoomContentWriter`. Publishes `RoomAreaAssignedByAdminEvent` for the audit log.  
**Usage:** `setarea <roomBlueprintId> <areaBlueprintId>`  
**Schema:** `Token string "roomBlueprintId"` (required), `Token string "areaBlueprintId"` (required)  
**Dependencies:** `IAreaSystem`, `EntityService`, `ITemplateRegistry`, `IRoomContentWriter`, `IEventBus`  
**Events:** `RoomAreaAssignedByAdminEvent`  
**RequiredPrivileges:** `AdminRequirement`

---

### `reload`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/Admin/Commands/ReloadCommand.cs`  
**Description:** Rebuilds the live world from the content directory the same way a restart does, without dropping connected players. Force-saves all persistent state (`IPersistenceSystem.FlushAllAsync`), tears down every world-content entity (rooms, mobs, world/dropped/shop items — anything with a `BlueprintComponent` but no `PersistentEntity`), re-reads the YAML, and re-spawns the world fresh, then re-publishes `WorldContentReadyEvent` so the startup fan-out re-runs (shops re-seed, spawn slots rebuild, players' rooms are re-resolved). **Runtime instance state is reset**: edits to existing rooms/mobs/items take effect, picked-up world items respawn, depleted shops refill, and the buy-back shelf clears. Players whose room was removed from YAML are moved to the starting room. Persistent entities (players and player-owned items/containers) are preserved.  
**Usage:** `reload`  
**Schema:** no arguments  
**Events:** publishes `WorldContentReadyEvent` (drives shop re-seed, spawn-slot rebuild, player re-hydration), then `ContentReloadedEvent` (audit)

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

### `power`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/BalanceInspection/Commands/PowerCommand.cs`  
**Description:** Resolves a runtime-in-world target — self (default, or `self`/`me`), an item in the invoker's inventory/room, or a mob in the invoker's room — to a score snapshot, then prints the target's computed power scalar and classified `(Tier, Band)` via `IPowerBudgetSystem`. Self reads `IStatSystem.Get` per `ScoreId` (folds worn gear/abilities/progression/tier); an item projects its `ItemDataComponent` via the shared `IItemPowerProjectionSystem` seam (tier = the item's authored `Tier`); a mob reads its effective scores (tier = the mob's authored `Tier`). For a tagged target, echoes the authored `(Tier, Band)` alongside the computed one. Blueprint-id/template resolution is deferred to the Blazor editor readout.  
**Usage:** `power [target]`  
**Schema:** `RestOfLine string "target"` (optional — omit for self)  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

### `powerband`

**Aliases:** none  
**MatchingMode:** `Full`  
**Location:** `Core/Modules/BalanceInspection/Commands/PowerbandCommand.cs`  
**Description:** With no argument, lists every `(Tier, Band)` cell (tiers 0–6 × bands 1–3, ~21 rows) with its target power range. With a tier argument, lists just that tier's three band rows. Ranges are derived from `IPowerBudgetSystem.TargetRange` — the anchor table's inverse, not hand-authored ranges.  
**Usage:** `powerband [tier]`  
**Schema:** `Token string "tier"` (optional — 0–6; omit to list every tier)  
**Events:** none  
**RequiredPrivileges:** `AdminRequirement`

---

## Adding a new command

Use the `add-command` skill (`.claude/skills/add-command/SKILL.md`) for step-by-step guidance on the new `ICommand` shape, argument schema, privilege declaration, and output.
