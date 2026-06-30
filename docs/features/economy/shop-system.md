# Shop system

> The trade half of the [economy feature](economy.md): players `buy`/`sell`/`list` against shopkeeper mobs. The first consumer of the atomic `IWalletSystem.Transfer` primitive. **Status:** live (slice 12c).

## What it is

A **shopkeeper** is an ordinary mob the author marks as a shop. It carries:

- a **`ShopComponent`** — the trade config: accepted `CurrencyId`, a till seed, authored **base-stock rows** `(blueprintId, quantity)`, and an optional per-shop ratio override (shaped but unused — [deferred](../../roadmap/backlog.md));
- a real **`WalletComponent` till**, seeded large from `ShopComponent.TillSeed` (or `ShopOptions.DefaultTillSeed`) on spawn; and
- a real **`InventoryComponent`** holding the live stock — the *same* inventory any mob carries, so a future corpse system drops it on death.

Stock is **real item entities**, not a catalog of blueprints. Each shop-held item carries a **`ShopStockComponent { Provenance, ExpiresAt? }`** distinguishing **base stock** (`Base`) from the **buy-back shelf** (`Acquired`, with an expiry). `list` browses both; `buy` purchases either (buying back a sold item is just `buy` against an `Acquired` item — there is no separate `buyback` verb); `sell` adds an item to the buy-back shelf.

## How it works

`IShopSystem` (domain) owns every rule and is **pure** — it returns results, never touches the bus or persistence (INV-5), and reads time only through the injected `IClock` (INV-26). It composes `IWalletSystem` + `IItemSystem` (domain→domain, same tier):

| Method | Decision |
|---|---|
| `GetListing(shop)` | base + acquired rows, each with a **compute-on-read** price |
| `TryResolveBuy(player, shop, item)` | price + affordability (`IWalletSystem.CanAfford`); does not mutate |
| `TryResolveSell(player, shop, item)` | price + `Value > 0` + till-affordability + the clock-derived `ExpiresAt` for the sell stamp; does not mutate |
| `PlanRestock(shop)` | per base-stock row, the `authored − liveBaseCount` shortfall (top-up) |
| `FindExpired(shop, nowUtc)` | `Acquired` items with `ExpiresAt <= nowUtc` |
| `SeedTill(shop)` | applies the configured till seed on spawn |

**Prices are never stored.** Buy price = `Value × ShopOptions.BuyRatio`; sell price = `Value × ShopOptions.SellRatio`; **buy-back price = what the shop paid the player** (fair mistake-protection), not `Value × BuyRatio`. All derive from each item's `ItemDataComponent.Value` ([slice 12a](../items/items.md)) on read, so nothing recomputes when value changes.

**Buy/sell are atomic `Transfer`s.** The thin command (Initiator, INV-8) resolves the shopkeeper (the implicit `ShopComponent` mob in the room; `list` may name one via `MobInRoomResolver`) and the item (`IItemSystem.TryFindItemInInventory`), calls the `IShopSystem` decision, then on success performs `IWalletSystem.Transfer` (`player → till` on buy, `till → player` on sell) + `IItemSystem.MoveBetweenInventories`, and publishes `ItemBoughtEvent`/`ItemSoldEvent`. The runtime path is the [Shopping journey (flow-30)](../../architecture/flows/flow-30-shopping.md).

**`MoveBetweenInventories`** is a new shared `IItemSystem` seam (the existing `MoveToInventory` is ground→inventory only). It removes the id from the source holder's `InventoryComponent` and appends to the destination's, touching no `LocationComponent` and no `BlueprintComponent` (INV-21). It is the reusable inventory↔inventory primitive deferred player-trade / banking / give-to-NPC also need (≥3 consumers, INV-19), so it lives on the item seam, not inside Shopping.

**Two events, the persistence-pool transition through `ItemContextHandler`.** `ItemBoughtEvent`/`ItemSoldEvent` are thin and past-tense. `ItemContextHandler` (the same handler that owns pickup/drop pool transitions) is extended to subscribe to both: buy → add `PersistentEntity`, **keep** `BlueprintComponent` (INV-21 — an origin record), clear `ShopStockComponent`; sell → remove `PersistentEntity`. `ShopInteractionHandler` (Notification) narrates and clears `ShopStockComponent` on buy. Reusing the *handler* (not overloading pickup/drop, whose `RoomEntityId` payload is a room, not a shop) keeps the pool-transition logic in one home (INV-19).

**Restock and expiry are closed heartbeat sweeps.** `ShopRestockTickHandler` and `ShopExpiryTickHandler` ride `HeartbeatTickEvent`, interval-gated on accumulated `Elapsed` against `ShopOptions.RestockInterval` / `BuyBackRetention`. Each calls the matching `IShopSystem` decision then spawns (`ITemplateRegistry.Spawn`, stamping `{ Base }`) or destroys (`EntityService.DestroyEntity`) entities. They have no game-rule fan-out, so they **publish nothing** (INV-10), and are mutually independent (no ordering). Restock is **top-up**: it spawns exactly the shortfall, never wiping a surviving base item or duplicating one.

## Persistence shape (INV-22/23)

- **Shopkeeper + base stock** are world content — no `PersistentEntity`, re-spawned from `MobTemplate` / blueprint. `ShopComponent` and `ShopStockComponent` are **not** `[Persistent]`. The till `WalletComponent` is `[Persistent]`-tagged but never written (two-level opt-in) and re-seeds each spawn.
- **Acquired (player-sold) items** transition persistent→world-transient on sale (`ItemContextHandler` removes `PersistentEntity`, mirroring drop), so the buy-back shelf is empty after a restart — no dangling reference inside a non-persistent re-spawned shopkeeper.
- **Bought items** transition world-transient→persistent and **preserve** `BlueprintComponent`. No slice introduces a new `SaveEntityAsync` site — durability rides the periodic flush via the `PersistentEntity` add/remove.

## Content tooling (INV-18)

Authored via `IMobBuilderSystem.SetMobShop` (dual-write live entity + `MobTemplate`, the `SetMobProtection` pattern), exposed as the `setmob shop <off | on [tillSeed] [currency]>` admin verb, a Blazor `MobEditor` shop section (incl. base-stock rows), and a `shop:` YAML block round-tripped by `MobContentWriter` / `MobTemplateDeserializer`. `ShopStockComponent` is runtime-only provenance (stamped by the spawn path and the sell flow) — no authoring surface. `ShopOptions` is app-wide `Shop:` config, not per-entity content. A designer inspects a live shop with `list`.

## Related

- [`economy.md`](economy.md) — the feature view; [`wallet-system.md`](wallet-system.md) — the `IWalletSystem`/`Transfer` seam shopping consumes.
- [Shopping journey (flow-30)](../../architecture/flows/flow-30-shopping.md) — buy/sell/list + the restock/expiry sweeps. Cross-refs [Heartbeat tick (flow-16)](../../architecture/flows/flow-16-heartbeat-tick.md) and [Items journey (flow-09)](../../architecture/flows/flow-09-item-pickup.md) (the shared `ItemContextHandler`).
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) · [`../../reference/handlers.md`](../../reference/handlers.md) · [`../../reference/commands.md`](../../reference/commands.md) — `ShopSystem`/`ItemSystem`, `ShopComponent`/`ShopStockComponent`, the shop handlers, and `buy`/`sell`/`list`.
- [`../../roadmap/completed/shopping.md`](../../roadmap/completed/shopping.md) — as-built history and design decisions.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5/8/10 (pure system, thin commands, closed sweeps), INV-14/21/22/23 (persistence two-level opt-in; `BlueprintComponent` preserved on buy), INV-19 (`MoveBetweenInventories` seam), INV-26 (clock-driven expiry/sweeps).
