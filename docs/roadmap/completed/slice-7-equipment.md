# Phase 3 slice 7 — Equipment + `wear`/`remove` (completed)

> Implemented on branch `claude/naughty-turing-b0bbef`. Full feature spec: [`../../use-cases/equipment.md`](../../use-cases/equipment.md).

## Outcome

Items now have wearable/wieldable slot assignments. A character can `wear` an item from their inventory into one or more named equipment slots, with automatic silent displacement of any existing occupant; `remove` returns a worn item to inventory; `equipment` (alias `eq`) lists all occupied slots. The slot lifecycle is fully persistent — `EquipmentComponent` is `[Persistent]` and saved on every wear/remove transition. This slice is intentionally scoped to the infrastructure: no stat bonuses, no armor class calculations, no combat integration — those land in slices 9 and 11.

## Shipped pieces

| Surface | Location |
|---|---|
| `WornSlot` enum — `MainHand`, `OffHand`, `Head`, `Chest`, `Feet` | `Core/WornSlot.cs` |
| `EquipmentComponent` — `Dictionary<WornSlot, uint> Slots`, `[Persistent]`, cross-cutting | `Core/ECS/Components/EquipmentComponent.cs` |
| `ItemDataComponent` — `WornSlots: List<WornSlot>?` field added | `Core/ECS/Components/ItemDataComponent.cs` |
| `IEquipmentSystem` / `EquipmentSystem` — slot queries, `EquipItem` (with internal implicit-remove pass), `RemoveItem`, `RemoveFromSlot` | `Core/Modules/Items/Systems/IEquipmentSystem.cs`, `EquipmentSystem.cs` |
| `IItemBuilderSystem` — `SetItemSlots(uint, IReadOnlyList<WornSlot>)` added | `Core/Modules/Items/Systems/IItemBuilderSystem.cs` |
| `ItemBuilderSystem` — `SetItemSlots` implemented (mutates `ItemDataComponent` + `ItemTemplate`) | `Core/Modules/Items/Systems/ItemBuilderSystem.cs` |
| `ItemInEquipmentResolver` — `IArgumentResolver` over the invoker's equipped items | `Core/Modules/Items/Resolvers/ItemInEquipmentResolver.cs` |
| `WearCommand` — player `wear <item>` with `ItemInInventoryResolver` | `Core/Modules/Items/Commands/WearCommand.cs` |
| `RemoveCommand` — player `remove <item>` with `ItemInEquipmentResolver` | `Core/Modules/Items/Commands/RemoveCommand.cs` |
| `EquipmentCommand` — player `equipment`/`eq` | `Core/Modules/Items/Commands/EquipmentCommand.cs` |
| `SetitemCommand` — `slot` property case added; writes YAML via `IItemContentWriter` | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| `ItemEquippedEvent`, `ItemUnequippedEvent` | `Core/Modules/Items/Events/` |
| `EquipmentInteractionHandler` — wear/remove broadcast fan-out (priority 80) | `Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs` |
| `EquipmentDisplayMessage` — new output shape; slot label + item name table | `Core/Output/EquipmentDisplayMessage.cs` |
| `TelnetOutputFormatter` — `EquipmentDisplayMessage` case added | `Core/Output/TelnetOutputFormatter.cs` |
| `ItemTemplate` — `WornSlots: List<WornSlot>` field added; `Apply` populates `ItemDataComponent.WornSlots` | `Core/Modules/Items/Templates/ItemTemplate.cs` |
| `ItemContentWriter` — serializes `WornSlots` as `List<string>` (lowercase slot names) | `Core/Modules/Items/Systems/ItemContentWriter.cs` |
| `ItemTemplateDeserializer` — parses `wornSlots` YAML list; logs warnings for unknown slot names | `Core/Modules/Items/ItemTemplateDeserializer.cs` |
| `AccountSystem.CreateCharacterAsync` — attaches empty `EquipmentComponent` to new characters | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `CharacterHydrationHandler` — migration guard: attaches empty `EquipmentComponent` to pre-slice-7 characters | `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` |
| `ItemsModule` — registers `IEquipmentSystem`, `ItemInEquipmentResolver`, three new commands, `EquipmentInteractionHandler` | `Core/Modules/Items/ItemsModule.cs` |
| `Program.cs` — subscribes `EquipmentInteractionHandler` to `ItemEquippedEvent` + `ItemUnequippedEvent` | `Server/Program.cs` |
| `docs/reference/components.md` — `EquipmentComponent` row added; `ItemDataComponent` `WornSlots` field noted | — |
| `docs/reference/systems.md` — `EquipmentSystem` entry added; `ItemBuilderSystem` `SetItemSlots` and `ItemCreationResult` updated | — |
| `docs/reference/handlers.md` — `EquipmentInteractionHandler` added; `CharacterHydrationHandler` updated | — |
| `docs/reference/commands.md` — `wear`, `remove`, `equipment` added; `setitem slot` extension noted | — |
| `docs/architecture/flows/README.md` — flows 13 (wear) and 14 (remove) added; Flow 6 shape list updated with `EquipmentDisplayMessage` | — |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation. Blocking findings resolved in the doc before code:

1. **`ItemRemovedEvent` → `ItemUnequippedEvent`** — canonical event catalog in `03-events.md` already names `ItemUnequippedEvent`; spec was updated to align.
2. **BlueprintComponent decoupling (INV-21)** — confirmed that `ItemSystem.MoveToInventory` (slice 6) already clears `BlueprintComponent` at pickup, so `wear` needs no action. Design note added to spec.
3. **`setitem slot` must write YAML** — spec updated to explicitly require `IItemContentWriter.WriteAsync` after slot mutation, matching the existing `setitem` pattern.
4. **`docs/reference/commands.md` omitted** — added to Reference Catalog Updates in the spec.
5. **`EquipItem` owns the implicit-remove loop (INV-8)** — spec clarified that the command calls only `EquipItem`; the per-slot iteration lives in the system. Flow step rewritten to remove ambiguity.

**Code-mode gate:** APPROVE WITH NITS — no blocking findings. Non-blocking nits addressed:

- Flow 6 shape list updated to include `EquipmentDisplayMessage`.
- `ItemCreationResult` signature in `systems.md` corrected to include the `Template` field.
- Comment added in `RemoveCommand` explaining the pre-remove `GetWornSlots` call.
- `docs/architecture/03-events.md` verified — both events are already listed in the canonical catalog (pre-listed during spec design); no update needed.

## Notable design points

- **`EquipmentComponent` is cross-cutting.** Placed in `Core/ECS/Components/` so mob entities (slice 8) can carry it without any domain dependency on `Core/Modules/Items/`.
- **`EquipItem` owns the implicit-remove loop.** The command calls a single `EquipItem(playerEntityId, itemEntityId)` — slot iteration and displacement logic live entirely inside `EquipmentSystem`. This keeps `WearCommand` thin (INV-8) and ensures the game rule ("clear occupied slots before equipping") is unit-testable against mock ECS state.
- **Implicit swap is silent.** When wearing an item displaces another, no event is published for the displaced item — only `ItemEquippedEvent` fires. A future `autoswap no` player preference could add confirmation prompts; that requires the state-machine prompt infrastructure tracked in backlog.
- **Two-hand weapons.** An item with both `MainHand` and `OffHand` in `WornSlots` works without special-casing — `EquipItem` iterates the slot list and displaces occupants of each slot independently.
- **Stat effects are explicitly deferred.** `EquipmentComponent.Slots` is the hook; combat/skills slices read it to compute effective attack/defense. No stat computation in this slice.
- **Additional slots (`Legs`, `Hands`, `Neck`, `Ring`, etc.) are acknowledged debt.** Pure enum + YAML extension with no architecture change. Tracked in `backlog.md`.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 8 — Mobs + wandering.** `EquipmentComponent` is cross-cutting and ready for mobs to carry gear.
- **Slice 9 — Combat.** `EquipmentSystem.GetEquippedItems` / `EquipmentComponent.Slots` provide the hook for computing effective attack and defense from worn gear.
- **Slice 11 — Skills.** Equipment slot state available for skill-gating (e.g. "two-handed weapon required for cleave").
