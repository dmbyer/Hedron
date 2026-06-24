# Item Value

> **Status:** `planned` — architecture-tier seed from the `architecture-advisor` intake (the shopping feature, split into slices 12a/12b/12c). This is **12a — the precursor substrate** that 12c (Shopping) consumes, mirroring how `currency-foundation` preceded shopping. The `implementation-planner` extends this into the full template ([`README.md`](README.md)).

**Actors:** Administrator (authors an item's value), System (price derivation consumers read it), Player (sees value indirectly through shop prices once 12c lands).

**Module:** extends the **Items** feature — a new field on `ItemDataComponent` (`Core/ECS/Components/`). Conceptually correlated with the **Economy** currency substrate (value is denominated in the `CurrencyRegistry` base unit), but introduces no Economy code. Feature home on ship: [`../features/items/`](../features/items/).

## Description

Every item carries an intrinsic **value** — a non-negative base-unit integer in the launch currency (**Coin**), authored beside the item's other designer fields. Value is the single source from which every economic price is *derived* (shop buy/sell/buy-back in slice 12c; salvage, repair, enchanting, and generation value-scaling later) — it is never itself a price, and prices are never stored. This slice lands the value field, its authoring tooling, and persistence; it wires no consumer (the first consumer is Shopping, 12c).

## Design notes

- **Value is a field on `ItemDataComponent`, not a new component.** It joins the existing authored, `[Persistent]` item fields (`StatBonuses`, `WornSlots`, `ItemType`). A separate `ItemValueComponent` would fragment item authoring and querying for no gain — every item already has the data store that should hold it.
- **Denominated in the currency base unit.** Value is a `long` in base-unit Coin (the `CurrencyRegistry` ladder's unit, `1c`), matching `WalletComponent`'s base-unit storage exactly — no conversion seam, no ladder math at the value layer. Display formatting up the ladder is the consumer's job via the shared `CurrencyFormatter` (the currency-foundation precedent), not this field's.
- **Prices are derived, never stored (compute-on-read).** Consumers compute buy/sell/buy-back from `Value × ratio` at read time. Storing a price would reintroduce the "did I recompute when value changed?" bug family that compute-on-read exists to kill — the same discipline as derived stats. This slice therefore stores *only* the base value.
- **A reusable substrate, not a shopping detail.** Value is the general "an item is worth X" primitive; shopping is merely its first consumer. Salvage/disenchant yield, repair cost, enchanting cost, and item-generation value-scaling (Spine D) all read the same field. Landing it as its own thin slice — exactly as `currency-foundation` preceded shopping — keeps it independently reviewable and available to those later features.
- **Authoring mirrors `SetItemType` exactly** (the simplest dual-write precedent — `SetItemStatBonus`/`SetItemSlots` add list semantics this field doesn't need). `IItemBuilderSystem.SetItemValue` writes `ItemDataComponent.Value` on the live entity **and** `ItemTemplate.Value` on the in-memory template; `SetitemCommand`'s `value` case parses the `long`, calls the system, then (as it already does for every property) publishes `ItemPropertySetByAdminEvent` and persists via `IItemContentWriter.WriteAsync` (INV-5: the command is the Initiator that touches the bus and disk). No new event, no new command.
- **`Value == 0` means "valueless / not saleable" (open question resolved).** A consumer that prices items (12c) treats 0 as "shop refuses to buy/stock it." This is the natural default — items authored before this field deserialize to 0 and are correctly non-saleable until an admin sets a value. A separate "no-sell" flag is *not* reserved here; if a designer later needs "valuable but unsellable" it can be added as its own field. This keeps the substrate a single scalar.
- **Negative value is rejected at the authoring boundary.** Value is a non-negative base-unit `long`; `SetitemCommand` rejects a negative parse (mirroring its other parse-failure echoes). The system method itself stays a pure setter (no throw) for the same reason the other `SetItem*` methods do — validation lives at the command/editor edge, the domain method trusts its caller. The Blazor editor uses `<input type="number" min="0">`.

## Architecture brief

*In-flight; trimmed on ship.*

### Seams and their homes

| New state | Home (layer) | Notes |
|---|---|---|
| item intrinsic **value** | `ItemDataComponent.Value` (`long`, `[Persistent]`) | base-unit Coin; default 0 (valueless / non-saleable) |
| authoring verb | `IItemBuilderSystem.SetItemValue` (domain) + `SetitemCommand value` | dual-write live entity + in-memory `ItemTemplate`, then command writes YAML (INV-5) — the established `SetItemSlots`/`SetItemStatBonus` pattern |
| YAML + editor | `ItemTemplate.value` field + Blazor `ItemEditor` row | round-trips the value |

### The family test (forward generalization)

Value is the general primitive; the siblings that will read it are salvage, repair, enchanting cost, loot/economy monitoring, and item generation value-scaling (Spine D). **Build now** as a single base-unit `long`. **Shape for later:** *multi-currency value* (an item priced in a non-Coin family) — the field can later carry a `CurrencyId` without moving off `ItemDataComponent` → **Defer** to [`backlog.md`](../roadmap/backlog.md) (added).

### Observers & contributors

None. Value is stored authored data with no aggregation (no INV-24 port) and no event (a value change is an admin mutation, surfaced by the `setitem` confirmation echo like other item-field edits — no past-tense fact needed yet).

### Invariants in tension

- **INV-21 (blueprint/instance):** `setitem value` updates both the `ItemTemplate` (YAML) and the live entity (the admin-mutation dual-write); player-owned instances authored before the field default to 0.
- **INV-18 / INV-25:** the field ships its authoring path (command + YAML + editor) and a persistence round-trip test in this slice.
- **INV-16:** `reference/components.md` `ItemDataComponent` row gains the `Value` field.

### Resolved decisions (do not relitigate)

1. **Field on `ItemDataComponent`**, not a new component.
2. **Single base-unit `long` in Coin** (multi-currency deferred to backlog).
3. **No consumer wired here** — prices are 12c's concern; this slice is the substrate only.

## Open questions

- **Default & semantics of 0 — RESOLVED.** `Value == 0` means "valueless / not saleable" (the documented consumer contract for 12c). No separate "no-sell" flag is reserved in this slice (see Design notes). No residual blocking question; flagged here only for the spec gate's confirmation.

## Preconditions

- The Items module is registered; `ItemDataComponent`, `IItemBuilderSystem`/`ItemBuilderSystem`, `SetitemCommand`, `ItemTemplate`, `ItemContentWriter`, `ItemTemplateDeserializer`, and the Blazor `ItemEditor` exist (all confirmed present).
- The currency-foundation substrate (`CurrencyRegistry` base unit Coin, `WalletComponent`'s base-unit `long`) has shipped (#124) — value reuses its base-unit denomination convention without taking a code dependency.
- The admin invoking `setitem` satisfies `AdminRequirement` (existing command gate).
- A live item entity for the target blueprint exists in the world (existing `setitem` precondition — it resolves the entity by blueprint id and errors if absent).

## Postconditions

- `ItemDataComponent` carries a `Value` field: a non-negative base-unit `long` (Coin), `[Persistent]`-included (inherits the component's `[Persistent]` attribute), default `0`.
- `ItemTemplate` carries a matching `Value` field (default `0`); `ItemTemplate.Apply` copies it onto the spawned `ItemDataComponent`.
- `IItemBuilderSystem.SetItemValue(uint itemEntityId, long value)` sets `ItemDataComponent.Value` on the live entity **and** `ItemTemplate.Value` on the in-memory template (dual-write; mirrors `SetItemType`).
- `setitem <blueprintId> value <n>` parses `n` as a non-negative `long`, calls `SetItemValue`, publishes `ItemPropertySetByAdminEvent` (property `"value"`), persists the template YAML via `IItemContentWriter`, and echoes a confirmation. A negative or non-integer `n` is rejected with an error echo and no mutation. *(invisible-state assertions: the entity field, the template field, the event publication, the YAML round-trip.)*
- `ItemContentWriter` serializes `value` into the item YAML; `ItemTemplateDeserializer` reads it back (absent field → `0`). Round-trip is lossless.
- A saved → reloaded persistent entity carrying `ItemDataComponent` preserves its `Value` (persistence round-trip).
- The Blazor `ItemEditor` shows a numeric Value field (min 0) that round-trips through the content catalog save/load.
- No consumer reads `Value` in this slice — no price is computed or stored anywhere.

## Main flow

1. An admin runs `setitem <blueprintId> value <n>`. `SetitemCommand` resolves the template and the live item entity (existing logic) and validates that a value arg is present (existing logic).
2. The new `value` case parses `n` via `long.TryParse`; on failure or `n < 0` it writes an error `PlainMessage` and returns without mutating.
3. The case calls `IItemBuilderSystem.SetItemValue(itemEntityId, n)`.
4. `ItemBuilderSystem.SetItemValue` sets `ItemDataComponent.Value` on the live entity and `ItemTemplate.Value` on the in-memory template (dual-write).
5. `SetitemCommand` publishes `ItemPropertySetByAdminEvent(invoker, itemEntityId, "value", n.ToString())` (existing post-switch publish path — no new code).
6. `SetitemCommand` persists the template via `IItemContentWriter.WriteAsync` (existing post-switch persist path); `ItemContentWriter` serializes the new `value` field into the YAML DTO.
7. `SetitemCommand` echoes the existing per-property confirmation (`Item value set to '<n>'.`).
8. On the next content reload / server startup, `ItemTemplateDeserializer` reads `value` from YAML (absent → 0) and `ItemTemplate.Apply` stamps it onto the freshly spawned `ItemDataComponent`. A persistent entity carrying the item round-trips its `Value` through the snapshot.

## Events fired

- `ItemPropertySetByAdminEvent` (existing; `Core/Modules/Items/Events/`) — reused unchanged; the `value` case rides the command's existing post-switch publish with `PropertyName = "value"`. **No new event** (per Architecture brief: a value change is an admin mutation surfaced by the confirmation echo and the existing admin-audit event; no new past-tense fact needed).

## Systems / handlers involved

| Piece | Catalog | Disposition |
|---|---|---|
| `ItemBuilderSystem` / `IItemBuilderSystem` | [`reference/systems.md`](../reference/systems.md) | **extend** — add `SetItemValue` |
| `ItemContentWriter` / `IItemContentWriter` | [`reference/systems.md`](../reference/systems.md) | **extend** — serialize `value` |
| `ItemTemplateDeserializer` | [`reference/systems.md`](../reference/systems.md) | **extend** — read `value` |
| `SetitemCommand` | command catalog (Items module) | **extend** — add `value` case + usage/long-description text |
| `ItemDataComponent` | [`reference/components.md`](../reference/components.md) | **extend** — add `Value` (INV-16: update the row) |
| `TemplateRegistry` / `ITemplateRegistry` | [`reference/systems.md`](../reference/systems.md) | reused unchanged (holds the mutated `ItemTemplate`) |

No handlers added or changed (no new event subscription).

## Implementation plan — work packages

### WP1 — Value field + system setter + persistence shape

- **Scope:** add `long Value` (default 0) to `ItemDataComponent` and to `ItemTemplate`; copy it in `ItemTemplate.Apply`; add `SetItemValue` to `IItemBuilderSystem` and implement in `ItemBuilderSystem` (mirror `SetItemType`'s dual-write); add `value` to `ItemContentWriter`'s DTO + serialization and to `ItemTemplateDeserializer`'s DTO + read (absent → 0).
- **Files:** `Core/ECS/Components/ItemDataComponent.cs`, `Core/Modules/Items/Templates/ItemTemplate.cs`, `Core/Modules/Items/Systems/IItemBuilderSystem.cs`, `Core/Modules/Items/Systems/ItemBuilderSystem.cs`, `Core/Modules/Items/Systems/ItemContentWriter.cs`, `Core/Modules/Items/ItemTemplateDeserializer.cs`.
- **Dependencies:** none.
- **Out of scope:** the command verb, the Blazor row, any consumer.
- **Exit criterion:** `SetItemValue` dual-write unit test passes; YAML write→read round-trip test (value preserved; absent → 0) passes; persistence save→load round-trip preserves `Value`.

### WP2 — `setitem value` command surface

- **Scope:** add the `value` case to `SetitemCommand`'s switch (parse non-negative `long`, error-echo on negative/non-integer, call `SetItemValue`); extend `LongDescription`/`Usage` text to list `value`.
- **Files:** `Core/Modules/Items/Commands/SetitemCommand.cs`.
- **Dependencies:** WP1 (the system method must exist).
- **Out of scope:** new event/persist plumbing (rides the existing post-switch publish + write).
- **Exit criterion:** handler-tier test — `setitem <bp> value 250` mutates entity + template and triggers a YAML write; `setitem <bp> value -1` and `value abc` produce an error echo and no mutation.

### WP3 — Blazor editor row

- **Scope:** add a numeric Value field (`<input type="number" min="0" @bind="_template.Value">`) to `ItemEditor.razor` bound to `ItemTemplate.Value`.
- **Files:** `Hedron.Web/Components/Pages/ItemEditor.razor`.
- **Dependencies:** WP1.
- **Out of scope:** any display formatting up the currency ladder (consumer's job).
- **Exit criterion:** editing the value and saving round-trips it through the content catalog (manual/Blazor-render check; presentation, no automated tier per the testing rubric).

> The **primary agent runs `architecture-reviewer` (code mode)** across the combined WP1–WP3 diff once all land.

## Content tooling impact

- **Data-file shape:** `items/<id>.yaml` gains an optional `value:` field (non-negative integer base-unit Coin; absent → 0). Round-tripped by `ItemContentWriter` (write) and `ItemTemplateDeserializer` (read).
- **Admin command:** `setitem <blueprintId> value <n>` — a new property case on the existing verb (no new command). Confirmation + audit event reuse the existing path.
- **Authoring surface (Blazor):** `ItemEditor` gains a numeric Value field. A designer can author and inspect value in the same PR (INV-18 satisfied).
- **`TemplateRegistry` entries:** none added; the existing item kind carries the new field.

## Cross-cutting surfaces stressed

| Surface | Classification | Rationale |
|---|---|---|
| Commands | **Adequate** | New property case on the existing `setitem` verb; uses the established argument schema and parse-error echo pattern. No new command framework needed. |
| Output | **Adequate** | Reuses `PlainMessage` confirmation/error severities exactly as the other `setitem` cases. |
| Persistence (component opt-in) | **Adequate** | `Value` lives on `ItemDataComponent`, already `[Persistent]`; the field inherits inclusion (INV-14). **Domain audit:** value is intrinsic item data, correct on world-content items (no `PersistentEntity`, re-spawned from YAML) **and** on player-owned instances (entity already opted in by `ItemContextHandler` on pickup — INV-22/23 unchanged). **Save-on-change:** the command does **not** call `SaveEntityAsync`; it persists the *template YAML* via `IItemContentWriter` (the established admin authoring write, not an entity snapshot) — no INV-22 violation. |
| Content templates / YAML | **Adequate** | `value` is one scalar added to the existing item DTO write/read pair; `IgnoreUnmatchedProperties` + absent→0 keeps old files loadable. |
| Event bus | **Adequate** | Reuses `ItemPropertySetByAdminEvent`; no new event (Architecture brief). |
| ECS queries | **Adequate** | No new query; the field is read by future consumers only. |
| Content authoring (Blazor) | **Adequate** | One numeric field added to `ItemEditor`, mirroring its existing bound fields. |
| Configuration / sessions / modules / broadcast / time | **Adequate (untouched)** | This slice touches none of these. |

No **Gap exposed** and no **Acknowledged debt**: the slice is a single scalar added along surfaces that already carry the sibling fields (`ItemType`, `StatBonuses`). Multi-currency value is explicitly **Deferred** to backlog by the Architecture brief (not debt incurred here).

## Flows introduced or modified

- **Admin item-authoring flow** ([`../architecture/flows/README.md`](../architecture/flows/README.md), the `setitem` / content-write journey): a new property branch (`value`) follows the identical trace as `type` (resolve entity → system dual-write → publish admin event → `IItemContentWriter.WriteAsync` → confirm). **No new flow shape** — the existing flow's per-property step already covers it; the PR confirms the flow doc still describes the generic path and adds `value` to any enumerated property list. No persistence-flush or startup flow changes (template-driven spawn already stamps `ItemDataComponent` fields; `Value` joins them).

## Test plan / Verification

Per [`../architecture/07-testing.md`](../architecture/07-testing.md). Each invisible-state postcondition maps to a named test.

- **System-unit** — `ItemBuilderSystemTests` (`Hedron.Tests/Authoring/`): `SetItemValue` sets `ItemDataComponent.Value` on the live entity **and** `ItemTemplate.Value` on the template (dual-write decision — mirrors the existing `SetItemType` test).
- **System-unit** — `ItemTemplate.Apply` copies `Value` onto the spawned `ItemDataComponent` (default-0 case and non-zero case).
- **Persistence round-trip** — an entity with `ItemDataComponent { Value = N }` saved then loaded preserves `Value` (confirms `[Persistent]` inclusion).
- **Content round-trip (system-unit)** — `ItemContentWriter.WriteAsync` → `ItemTemplateDeserializer.Deserialize`: `Value` survives the YAML round-trip; a YAML file with no `value` field deserializes to `Value == 0` (backward-compat).
- **Handler-tier** — `SetitemCommand` `value` case: valid `n` mutates entity + template and invokes the content writer + publishes the admin event; negative `n` and non-integer `n` produce an error echo with **no** mutation and **no** write (the validation-boundary throws-equivalent — here an error echo, asserted on the captured output + unchanged state).
- **Skipped:** the Blazor `ItemEditor` row (presentation; no automated tier per rubric — manual render/save check). Exact confirmation prose (presentation). The `Value` getter/setter itself (pure-data component field — covered transitively by the round-trip tests). No consumer/price logic exists to test in this slice.

> **Testability:** no un-injected seam — the system is a pure setter, the writer uses injected `WorldOptions`, the command takes its collaborators by constructor. No INV-26 seam gap.

## Related

- [`currency-foundation`](../roadmap/completed/) — the denomination precedent (base-unit `long` Coin); value reuses its convention.
- [`shopping.md`](shopping.md) — slice 12c, the **first consumer** of `Value` (price derivation). Not built here.
- [`mob-protection.md`](mob-protection.md) — slice 12b, the other shopping precursor.
- `items-and-inventory` feature ([`../features/items/`](../features/items/)) — the destination feature doc on ship.
- The `SetItemType` / `SetItemStatBonus` / `SetItemSlots` authoring trio — the dual-write pattern this slice mirrors.
