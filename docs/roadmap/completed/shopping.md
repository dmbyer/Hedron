# Shopping — slice 12c (completed)

> Implemented on branch `claude/awesome-gates-ba566f`, 2026-06-30. Living docs: [economy feature](../../features/economy/economy.md) · [`shop-system.md`](../../features/economy/shop-system.md) · [Shopping journey (flow-30)](../../architecture/flows/flow-30-shopping.md).

## Outcome

Players can now trade with shopkeeper mobs: `list` browses a shop's wares, `buy <item>` purchases from base stock or the buy-back shelf, and `sell <item>` sells an item from inventory. Shopping is the **trade half of the economy feature** and the first consumer of the `IWalletSystem.Transfer` primitive the currency-foundation slice anticipated — buying is `Transfer(player → till)`, selling is `Transfer(till → player)`, both against a real `WalletComponent` till the shopkeeper carries. Prices derive compute-on-read from each item's `ItemDataComponent.Value` (12a) × a global `ShopOptions` ratio. A shopkeeper's stock is **real item entities** in its ordinary `InventoryComponent`, tagged per-item with `ShopStockComponent { Provenance, ExpiresAt? }`; heartbeat sweeps top base stock back to authored levels and expire the buy-back shelf.

## Behavior digest

**Preconditions:** player bound to a character with `LocationComponent` + `InventoryComponent` + `WalletComponent`; a shopkeeper mob (`MobDataComponent` + `InventoryComponent` + `ShopComponent` + `WalletComponent` till) in the room; 12a shipped (`ItemDataComponent.Value`); `ShopOptions` bound; heartbeat running.

**Postconditions (as specified):**
- **Buy (base / buy-back):** the item entity moves shop→player via `IItemSystem.MoveBetweenInventories`; `ItemBoughtEvent` published; item gains `PersistentEntity`, **keeps** `BlueprintComponent` (INV-21), loses `ShopStockComponent`; `Transfer(player → till, buyPrice)` succeeded (`buyPrice = Value × BuyRatio` for base; the recorded paid price for buy-back). Insufficient funds → no move, no wallet change, no event, refusal line.
- **Sell:** item moves player→shop; `ItemSoldEvent` published; item loses `PersistentEntity`, gains `ShopStockComponent { Acquired, ExpiresAt }` (clock-derived in `IShopSystem.TryResolveSell`); `Transfer(till → player, Value × SellRatio)`. Dry till or `Value == 0` → refusal, no mutation.
- **`list`:** base stock + buy-back shelf shown with compute-on-read prices (`CurrencyFormatter`), acquired rows flagged. No mutation.
- **Restock sweep:** per base-stock row, spawns `authored − liveBaseCount` fresh entities tagged `{ Base }` (top-up, never wipe/duplicate).
- **Buy-back-expiry sweep:** destroys every `Acquired` item with `ExpiresAt <= clock.UtcNow`; base stock untouched.
- **Restart:** shopkeeper + till + base stock re-spawn from templates (world content, no `PersistentEntity`); buy-back shelf empty (acquired items were made non-persistent on sale).

**Main flow:** command resolves shopkeeper (implicit `ShopComponent` mob in room; `list` may name one via `MobInRoomResolver`) + item token (`IItemSystem.TryFindItemInInventory`) → `IShopSystem.TryResolveBuy`/`TryResolveSell`/`GetListing` decision → on success the command does `Transfer` + `MoveBetweenInventories` → publishes `ItemBought/SoldEvent` → `ItemContextHandler` applies the persistence-pool transition, `ShopInteractionHandler` narrates + clears `ShopStockComponent` on buy. Restock/expiry ride `HeartbeatTickEvent` via interval-gated handlers that call `IShopSystem.PlanRestock`/`FindExpired` then spawn/destroy entities (closed sweeps, publish nothing).

## Shipped pieces

| Surface | Location |
|---|---|
| `ShopComponent` — accepted currency, till seed, optional ratio override (deferred/unused), base-stock rows | `Core/Modules/Shopping/Components/ShopComponent.cs` |
| `ShopStockComponent` — `StockProvenance { Base, Acquired }`, `DateTime? ExpiresAt` (not `[Persistent]`) | `Core/Modules/Shopping/Components/ShopStockComponent.cs` |
| `ShopOptions` — `RestockInterval`, `BuyBackRetention`, `BuyRatio`, `SellRatio`, `DefaultTillSeed` (`Shop:` config) | `Core/Modules/Shopping/ShopOptions.cs` |
| `IShopSystem` / `ShopSystem` — `GetListing`/`TryResolveBuy`/`TryResolveSell`/`PlanRestock`/`FindExpired`/`SeedTill` (pure; `IWalletSystem`+`IItemSystem`+`IClock`) | `Core/Modules/Shopping/Systems/IShopSystem.cs`, `ShopSystem.cs`, `ShopResults.cs` |
| `ItemBoughtEvent` / `ItemSoldEvent` — thin past-tense payloads | `Core/Modules/Shopping/Events/` |
| `BuyCommand` / `SellCommand` / `ListCommand` — thin Initiators | `Core/Modules/Shopping/Commands/` |
| `ShopInteractionHandler` — narration + `ShopStockComponent` clear on buy | `Core/Modules/Shopping/Handlers/ShopInteractionHandler.cs` |
| `ShopkeeperSpawnHandler` — `WorldContentReadyEvent`: seed till (via `IShopSystem.SeedTill`) + spawn base stock | `Core/Modules/Shopping/Handlers/ShopkeeperSpawnHandler.cs` |
| `ShopRestockTickHandler` / `ShopExpiryTickHandler` — interval-gated `HeartbeatTickEvent` sweeps | `Core/Modules/Shopping/Handlers/` |
| `ShoppingModule.AddShoppingModule` — DI wiring + `Configure<ShopOptions>` | `Core/Modules/Shopping/ShoppingModule.cs` |
| `IItemSystem.MoveBetweenInventories` — shared inventory→inventory move (no Location/Blueprint mutation) | `Core/Modules/Items/Systems/IItemSystem.cs` + `ItemSystem.cs` |
| `ItemContextHandler` (extended) — subscribes `ItemBought/SoldEvent` for the pool transition | `Core/Modules/Spawn/Handlers/ItemContextHandler.cs` |
| `IMobBuilderSystem.SetMobShop` + `MobTemplate` shop fields + `MobContentWriter`/deserializer + Blazor `MobEditor` shop row + `setmob shop` verb | `Core/Modules/Mobs/…`, `Core/Modules/Admin/Commands/SetMobCommand.cs`, `Hedron.Web/Components/Pages/MobEditor.razor` |
| `MobInRoomResolver` relocated to a shared non-combat home | `Core/Modules/Mobs/Resolvers/MobInRoomResolver.cs` |
| Admin `list` → `listents` (`ListEntitiesCommand`) — resolves the duplicate-verb collision with the player `list` | `Core/Modules/Admin/Commands/ListEntitiesCommand.cs` |
| World-loader persisted-instance exclusion + `reload` full-rebuild reconciliation | `Core/Modules/World/Systems/WorldContentLoader.cs`, `Core/Modules/Admin/Commands/ReloadCommand.cs`, `Core/Modules/Spawn/Systems/SpawnSystem.cs` |

## Tests shipped

All tiers from the plan's Test plan, green at **913 tests** (`dotnet test`):
- **System-unit** (`ShopSystemTests`) — buy/sell/buy-back pricing, `Value == 0` and dry-till refusals, `PlanRestock` top-up math (ignores `Acquired`), `FindExpired` (fake `IClock`).
- **Items-module unit** (`MoveBetweenInventoriesTests`) — id moves between holders; no Location/Blueprint mutation.
- **Handler** (`ItemContextHandlerShoppingTests`) — buy adds `PersistentEntity` + retains `BlueprintComponent` + clears `ShopStockComponent`; sell removes `PersistentEntity` + stamps `{ Acquired, ExpiresAt }`. (`ShopRestockTickHandlerTests` for sweep gating.)
- **Flow** (`ShoppingFlowTests`) — end-to-end buy/sell/buy-back wallet + inventory deltas; persistence pool flips.
- **Persistence round-trip** (`ShopkeeperRoundTripTests`) — YAML-authored shopkeeper spawns with seeded till + base stock tagged `{ Base }` + **no** `PersistentEntity`.
- **Authoring** (`MobBuilderSystemTests`) — `SetMobShop` add/update/remove + null-base-stock branches.
- **Reload / persistence-shadow** (`WorldContentReloadTests`, `WorldContentLoaderTests`) — picked-up world item respawns on reload; persisted player copy preserved; world content torn down + rebuilt.

## Decisions

- **Stock is one `InventoryComponent`, two provenances.** Base stock and the buy-back shelf are both live entities in the shopkeeper's single inventory, distinguished by `ShopStockComponent.Provenance`. The user's three constraints ("inventory like any mob," "drops on death," "base stock resets to a level") jointly force real entities over a catalog-of-blueprints; corpse-loot drop then falls out for free.
- **The till is a real wallet; buy/sell are `Transfer`s** — preserving the economy doc's "first `Transfer` consumer" framing (no INV-15 doc fix). "Shopkeepers don't worry about money" is honored by seeding the till large from config, not by exempting the shop from affordability — a dry till genuinely refuses a sell (accepted edge given the large seed). The till is `[Persistent]`-tagged but never written (world-content mob has no `PersistentEntity`); it re-seeds each spawn.
- **Prices derive from `Value`, never stored** (compute-on-read in `IShopSystem`). Buy-back price = what the shop paid the player (fair mistake-protection), not `Value × BuyRatio`.
- **Restock = top-up, not wipe-and-rebuild** (resolved Q1): per row, spawn `authored − liveBaseCount`; never destroys a surviving base item, never duplicates.
- **Sweeps are closed heartbeat sweeps** (INV-10) — no game-rule fan-out, so they publish nothing; mutually independent; all time via injected `IClock`/tick gating (INV-26).
- **`ItemContextHandler` (not the command) owns the persistence-pool transition**, extended to `ItemBought/SoldEvent` rather than overloading `ItemPickedUp/DroppedEvent` (whose `RoomEntityId` payload is a room, not a shop). Buy **keeps** `BlueprintComponent` (INV-21 — pickup doesn't clear it either).
- **`MoveBetweenInventories` lands on the shared `IItemSystem` seam**, not inside Shopping — deferred player-trade/banking/give-to-NPC are ≥3 future consumers (INV-19). Carried the INV-20 `add-domain-system` skill + INV-16 `reference/systems.md` updates.

## Deviations / Follow-ups

- **`MobInRoomResolver` "third consumer" not realized.** The slice relocated the resolver to `Mobs/Resolvers/`, but `buy`/`sell` resolve the implicit shopkeeper directly and combat/ability targeting still use inline `ICombatSystem.TryFindTargetInRoom` — so `list` is its one active consumer. Docs record the honest state; the combat/ability migration that genuinely crosses the INV-19 threshold is parked in [`../backlog.md`](../backlog.md).
- **`setmob shop` is toggle + till + currency only**; base-stock row authoring is YAML / Blazor `MobEditor` (richer surfaces). Per-shop pricing override is shaped on `ShopComponent` but unused — deferred to [`../backlog.md`](../backlog.md).
- **Two cross-cutting fixes shipped alongside** (surfaced while exercising the slice, both with their own tests + docs):
  - *World-loader persisted-instance exclusion* — `BuildLiveBlueprintMap` now excludes `PersistentEntity` entities, so a picked-up authored item (which keeps its `BlueprintComponent`, INV-21) no longer shadows its authored world re-spawn. Documented in [`../../architecture/06-persistence.md`](../../architecture/06-persistence.md).
  - *`reload` reconciled to a full world rebuild* — force-save → tear down world content → re-spawn from YAML → re-publish `WorldContentReadyEvent` (shop re-seed, spawn slots, player re-placement). Resets runtime instance state like a restart without dropping players. See [Flow 5](../../architecture/flows/flow-05-content-reload.md). Reload now destroys/re-creates world entities — transient combat references to a destroyed mob are dropped (acceptable for an admin op); heartbeat-thread concurrency remains the deferred [thread-safety](../backlog.md) concern.
- **Admin `list` renamed `listents`** to resolve a runtime duplicate-verb collision with the new player `list`.
