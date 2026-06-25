# Item value — slice 12a (completed)

> Implemented on branch `claude/peaceful-goodall-e66511`, 2026-06-24. Living docs: [`items`](../../features/items/items.md).

## Outcome

Every item now carries an intrinsic **value** — a non-negative base-unit Coin `long` on `ItemDataComponent`, authored beside the item's other designer fields and persisted with it. Value is the single source from which every economic price will be *derived* (shop buy/sell/buy-back in slice 12c; salvage, repair, enchanting, generation value-scaling later) — it is never itself a price, and no price is stored. This slice landed the field, its authoring path (`setitem value`, the Blazor editor row, YAML round-trip), and its persistence; it wires **no consumer** (the first consumer is Shopping, 12c). It is the precursor substrate that 12c consumes, mirroring how `currency-foundation` preceded shopping.

## Behavior digest

*As-specified snapshot (the authoritative present-truth lives in the [items feature doc](../../features/items/items.md)).*

**Preconditions:** the Items module is registered (`ItemDataComponent`, `IItemBuilderSystem`/`ItemBuilderSystem`, `SetitemCommand`, `ItemTemplate`, `ItemContentWriter`, `ItemTemplateDeserializer`, Blazor `ItemEditor` all present); the currency-foundation base-unit Coin convention has shipped (#124, reused without a code dependency); the admin satisfies `AdminRequirement`; a live item entity for the target blueprint exists.

**Postconditions:**
- `ItemDataComponent` carries a `Value` field: non-negative base-unit `long` (Coin), `[Persistent]`-included (inherits the component attribute), default `0`.
- `ItemTemplate` carries a matching `Value` (default `0`); `ItemTemplate.Apply` copies it onto the spawned `ItemDataComponent`.
- `IItemBuilderSystem.SetItemValue(uint itemEntityId, long value)` sets `ItemDataComponent.Value` on the live entity **and** `ItemTemplate.Value` on the in-memory template (dual-write; mirrors `SetItemType`); pure setter, no throw.
- `setitem <blueprintId> value <n>` parses `n` as a non-negative `long`, calls `SetItemValue`, publishes `ItemPropertySetByAdminEvent` (property `"value"`), persists the template YAML via `IItemContentWriter`, and echoes a confirmation. A negative or non-integer `n` is rejected with an error echo and **no** mutation.
- `ItemContentWriter` serializes `value` into the item YAML; `ItemTemplateDeserializer` reads it back (absent field → `0`). Round-trip is lossless.
- A saved → reloaded persistent entity carrying `ItemDataComponent` preserves its `Value`.
- The Blazor `ItemEditor` shows a numeric Value field (min 0) that round-trips through the content catalog.
- `Value == 0` means "valueless / not saleable" (the consumer contract for 12c). No consumer reads `Value` in this slice.

**Main-flow summary:** admin runs `setitem <bp> value <n>` → command resolves template + live entity (existing logic), the new `value` case parses/validates `n` → calls `SetItemValue` (dual-write) → the existing post-switch path publishes `ItemPropertySetByAdminEvent` and persists the template YAML → confirmation echo. On reload, `ItemTemplateDeserializer` reads `value` (absent → 0) and `ItemTemplate.Apply` stamps it onto the freshly spawned `ItemDataComponent`.

## Shipped pieces

| Surface | Location |
|---|---|
| `ItemDataComponent.Value` — `long`, `[Persistent]`-inherited, default 0 | `Core/ECS/Components/ItemDataComponent.cs` |
| `ItemTemplate.Value` + `Apply` copy | `Core/Modules/Items/Templates/ItemTemplate.cs` |
| `IItemBuilderSystem.SetItemValue` — interface method | `Core/Modules/Items/Systems/IItemBuilderSystem.cs` |
| `ItemBuilderSystem.SetItemValue` — dual-write impl (mirrors `SetItemType`) | `Core/Modules/Items/Systems/ItemBuilderSystem.cs` |
| `value` serialization (write side) | `Core/Modules/Items/Systems/ItemContentWriter.cs` |
| `value` deserialization (read side, absent → 0) | `Core/Modules/Items/ItemTemplateDeserializer.cs` |
| `setitem <bp> value <n>` case + usage/long-description text | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| Numeric Value editor row | `Hedron.Web/Components/Pages/ItemEditor.razor` |
| Catalog rows updated | `docs/reference/components.md` (`ItemDataComponent`), `docs/reference/systems.md` (`ItemBuilderSystem`) |

No new component, event, command, handler, or flow shape — the slice reuses `ItemPropertySetByAdminEvent` and the existing `setitem` publish + YAML-persist path.

## Tests shipped

`dotnet test Hedron.sln` green — **801 tests, 0 failures** (up from 776).

- **System-unit** — `ItemBuilderSystemTests` (`Hedron.Tests/Authoring/`): `SetItemValue` dual-write (entity + template), zero accepted, no-op for unknown entity; `ItemTemplate.Apply` copies `Value` (default-0 and non-zero cases).
- **Content + persistence round-trip** — `ItemValueRoundTripTests` (`Hedron.Tests/Items/`): YAML write→read for non-zero/zero/large value; absent-field → `Value == 0` (backward-compat); SQLite persistence save→load preserves `Value` (confirms `[Persistent]` inclusion).
- **Handler-tier** — `SetitemValueCommandTests` (`Hedron.Tests/Modules/Items/`): valid `value 250` mutates entity + template, invokes the content writer, publishes `ItemPropertySetByAdminEvent` (`PropertyName = "value"`, `NewValue = "250"`); `value 0` accepted as the valueless sentinel; `value -1` and `value abc` produce an error echo with **no** mutation, **no** write, **no** event.
- **Skipped (per testing rubric):** the Blazor `ItemEditor` row (presentation — manual render/save check); exact confirmation prose; the pure getter/setter (covered transitively by round-trip tests). No consumer/price logic exists to test in this slice.

## Decisions

- **Value is a field on `ItemDataComponent`, not a new component.** It joins the existing authored `[Persistent]` item fields (`StatBonuses`, `WornSlots`, `ItemType`). A separate `ItemValueComponent` would fragment item authoring and querying for no gain — every item already has the data store that should hold it.
- **Denominated in the currency base unit** (a `long` in base-unit Coin, `1c`), matching `WalletComponent`'s storage exactly — no conversion seam, no ladder math at the value layer. Display formatting up the ladder is the consumer's job via the shared `CurrencyFormatter`, not this field's.
- **Prices are derived, never stored (compute-on-read).** Consumers compute buy/sell/buy-back from `Value × ratio` at read time. Storing a price would reintroduce the "did I recompute when value changed?" bug family — the same discipline as derived stats. This slice stores *only* the base value.
- **A reusable substrate, not a shopping detail.** Value is the general "an item is worth X" primitive; shopping is merely its first consumer. Salvage/disenchant yield, repair cost, enchanting cost, and item-generation value-scaling (Spine D) all read the same field. Landing it as its own thin slice keeps it independently reviewable and available to later features.
- **Authoring mirrors `SetItemType` exactly** (the simplest dual-write precedent — `SetItemStatBonus`/`SetItemSlots` add list semantics this field doesn't need). The system method is a pure setter (no throw); validation lives at the command/editor edge (INV-5 boundary).
- **`Value == 0` means "valueless / not saleable."** The natural default — items authored before this field deserialize to 0 and are correctly non-saleable until an admin sets a value. A separate "no-sell" flag is *not* reserved; if a designer later needs "valuable but unsellable" it can be added as its own field. This keeps the substrate a single scalar.
- **Negative value is rejected at the authoring boundary.** `SetitemCommand` rejects a negative or non-integer parse with an error echo and no mutation; the Blazor editor uses `<input type="number" min="0">`.
- **Deferred:** *multi-currency value* (an item priced in a non-Coin family) — the field can later carry a `CurrencyId` without moving off `ItemDataComponent`. Parked in [`backlog.md`](../backlog.md).

## Deviations / Follow-ups

- **Deviations from the plan:** none. Built as specified across the three work packages (WP1 field + system + persistence; WP2 `setitem value` command; WP3 Blazor row). The only review finding (an INV-16 `reference/systems.md` drift — the `ItemBuilderSystem` entry not updated for `SetItemValue`) was fixed in the same PR.
- **Follow-ups unlocked:** slice 12c (Shopping) is the first `Value` consumer — derives buy/sell/buy-back prices compute-on-read. Salvage, repair, enchanting cost, and item-generation value-scaling (Spine D) read the same field later.
- **Debt parked:** multi-currency value, in [`../backlog.md`](../backlog.md).
