# Shopping

> **Status:** `planned` — architecture-tier seed from the `architecture-advisor` intake. This is **12c — the trade feature**, the third of three split slices. It **depends on 12a (Item value)** for the `ItemDataComponent.Value` seam and **consumes 12b (Mob protection)** to protect safe-area shopkeepers; neither is re-specified here. The `implementation-planner` extends this into the full template ([`README.md`](README.md)). Planner-tier sections (Preconditions/Postconditions, Main flow, Events fired, Systems/handlers, Work packages, Content tooling impact, Cross-cutting surfaces, Flows, Test plan) are intentionally **not** stubbed here.

**Actors:** Player (buy / sell / browse / buy-back), Administrator (author shopkeepers, base stock, till seed), System (heartbeat-driven restock + buy-back-shelf expiry).

**Module:** new `Core/Modules/Shopping/` (domain), composing the existing `Core/Modules/Economy/` (`IWalletSystem`) and `Core/Modules/Items/` (`IItemSystem`) seams. Reads `ItemDataComponent.Value` (12a) and `ProtectionComponent` (12b) without owning either. Feature home on ship: [`../features/economy/`](../features/economy/) (shopping is the trade half of the economy feature).

## Dependencies

- **12a — Item value** (✅ shipped — [items feature](../features/items/items.md), [`../roadmap/completed/item-value.md`](../roadmap/completed/item-value.md)): the `ItemDataComponent.Value` field every shop price derives from. **Hard prerequisite — satisfied.**
- **12b — Mob protection** (✅ shipped — [mobs feature](../features/mobs/mobs.md#protection-invulnerability--immunity), [`../roadmap/completed/mob-protection.md`](../roadmap/completed/mob-protection.md)): the `ProtectionComponent` a safe-area shopkeeper sets. **Soft** — shopping works without it, but protected shopkeepers need it. **Satisfied.**

## Description

Players buy items from, and sell items to, shopkeeper mobs. A shopkeeper holds a real `InventoryComponent` (the same one any mob carries — so a future corpse system drops it on death) seeded from an authored **base stock** of finite, restocking item entities, plus a **buy-back shelf** of items players have sold, each tracked with its own expiry. A shopkeeper carries a **till** (`WalletComponent`) seeded large from config; buying and selling are atomic `IWalletSystem.Transfer`s between the till and the player's wallet. Prices derive from each item's `Value` (12a). `list` browses the shop; `buy` / `sell` trade; buying back a sold item is `buy` against the buy-back shelf.

## Design notes

Durable seam rationale (kept on ship per INV-28).

- **Shopkeeper stock is one `InventoryComponent`, two provenances.** Base stock and the buy-back shelf are both live item entities in the shopkeeper's single `InventoryComponent`, distinguished by a per-item `ShopStockComponent` (`Base` vs `Acquired`, plus `ExpiresAt` for acquired). The user's three constraints jointly force this — "inventory like any mob," "drops on death," and "base stock resets to a level" all require *real entities in the mob's inventory*, which a catalog-of-blueprints model could not satisfy. Corpse-loot drop then falls out for free.
- **The till is a real wallet; buy/sell are `Transfer`s.** The shopkeeper carries a `WalletComponent` (till) and trades via the atomic `IWalletSystem.Transfer` primitive — buying is `Transfer(player → till)`, selling is `Transfer(till → player)`. This **preserves** the economy doc's framing of shopping as "the first `Transfer` consumer (vendor till ↔ player wallet)" — no INV-15 doc fix needed. "Shopkeepers don't worry about money" is honored by **seeding the till large from config**, not by exempting the shop from the affordability rule — a till that genuinely runs dry refuses the sell, an accepted edge given the large seed.
- **The till re-seeds on startup; it is not persisted.** A shopkeeper is world content (INV-23: no `PersistentEntity`), so its `WalletComponent` — though `[Persistent]`-tagged — is never written (two-level opt-in, INV-14). The till resets to its configured seed each startup/respawn, keeping shops out of the SQLite domain entirely.
- **Prices derive from `Value`, never stored.** Buy price, sell price, and buy-back price are `Value × ratio` computed on read in `IShopSystem` (compute-on-read; nothing to recompute when value changes).
- **Restock and expiry are closed heartbeat sweeps, not event chains.** Both ride `HeartbeatTickEvent` via handlers that call an `IShopSystem` decision method and then mutate/spawn/destroy entities; neither has game-rule fan-out, so neither publishes (INV-10). They are mutually independent (no inter-handler ordering, like `CurrencyLootHandler` vs `SpawnSystem`). All time comparisons resolve through the injected `IClock` / heartbeat timestamp (INV-26), never `DateTime.UtcNow`.

## Architecture brief

*In-flight; trimmed on ship.*

### Seams and their homes

| New verb / state / signal | Home (layer) | Notes |
|---|---|---|
| **Shop** identity + trade config | `ShopComponent` on the mob (component) | presence = "this mob trades" (`HasComponent`, INV-4); holds accepted `CurrencyId`, till seed, optional per-shop ratio override (default global, deferred — backlog) |
| Shop **till** | `WalletComponent` on the mob (reuse) | seeded from `ShopComponent`/config on spawn; non-persistent (world content) |
| Per-item shop provenance + expiry | `ShopStockComponent` on each shop-held item (component) | `Provenance {Base, Acquired}`, `ExpiresAt?`; the per-item separate-tracking the user asked for; world-transient (not `[Persistent]`) |
| price calc, buy/sell/buy-back validation, restock + expiry decisions | `IShopSystem` (domain) | pure: returns results; composes `IWalletSystem` + `IItemSystem` (domain→domain, same tier — permitted); never touches bus/persistence (INV-5) |
| `list` / `buy` / `sell` (buy-back via `buy`) | commands (Initiators) | thin (INV-8): resolve shop + item → call `IShopSystem` → `Transfer` + item move → publish |
| `ItemBoughtEvent` / `ItemSoldEvent` | events (past-tense, thin) | drive narration + future faucet/sink monitoring; **drive the same persistence-pool transitions as pickup/drop** (see resolved Q below) |
| restock + expiry sweeps | handlers on `HeartbeatTickEvent` | closed sweeps, publish nothing (INV-10); call `IShopSystem`, then spawn/destroy entities |

### The family test (forward generalization)

- **Buy/sell** are the first `Transfer` consumer the economy doc anticipated (banking, player trade, mail-COD are the siblings sharing the atomic primitive — nothing to generalize here).
- **Base-stock + buy-back shelf** is a narrow instance of the deferred **Mob loadouts & loot tables** (Spine D) and future **player shops / banks**. The `InventoryComponent` + `ShopStockComponent` shape is additive: a general loot/vendor table slots onto the same inventory without a rewrite.
- **Per-shop pricing override** → **Defer** to [`backlog.md`](../roadmap/backlog.md) (added); `ShopComponent` carries the optional field unused.

### Observers & contributors

- **Observers:** `ItemBoughtEvent`/`ItemSoldEvent` serve narration now and deferred economy-sink/faucet monitoring later. Restock/expiry have no observers → no events (INV-10).
- **Contributors:** none — price is derived arithmetic on a single stored `Value`, not a multi-source aggregation (no INV-24 port). Currency itself remains a directly-owned ledger.

### Ordering & timing (INV-7 / INV-26)

- Restock and expiry handlers are independent on `HeartbeatTickEvent`; no ordering constraint between them or relative to `EffectTickHandler`/`CombatTickHandler` (shop bookkeeping touches neither HP nor effects).
- The sell command stamps `ShopStockComponent` with the `ExpiresAt` returned by `IShopSystem.TryResolveSell` (INV-8: the `now + retention` arithmetic lives in the system); the expiry *decision* likewise compares against the injected `IClock`/heartbeat timestamp inside `IShopSystem` — never `DateTime.UtcNow` in a system.

### Invariants in tension

- **INV-23:** shopkeeper + base-stock items are world content (no `PersistentEntity`, re-spawn from template). **Acquired (player-sold) items become non-persistent on sale** — `RemoveComponent<PersistentEntity>` mirroring drop-to-ground — so the buy-back shelf is world-transient (cleared on restart). This avoids broken references inside a non-persistent, re-spawned mob.
- **INV-21:** the bought item **keeps** its `BlueprintComponent` as an origin record — INV-21 forbids clearing it on acquisition, and the real `IItemSystem.MoveToInventory`/pickup path does not clear it either; spawn-slot vacancy is tracked by `SpawnSystem` via events, not blueprint presence. Items move between inventories with no blueprint mutation; restock re-spawns fresh entities from the blueprint. Admin authoring of base stock updates both template and live entity.
- **INV-8:** price/markup/affordability/expiry rules live in `IShopSystem`, never in command bodies.
- **INV-18 / INV-25:** new gameplay state (shop config, base stock, the trade verbs) ships authoring tooling and tests in-slice.

### Resolved decisions (do not relitigate)

1. **Base stock = finite live items + restock** (not an infinite catalog). Buying depletes; an app-wide restock interval respawns to authored levels; drops as corpse loot when the death/corpse system lands.
2. **Vendor money = till + `Transfer`** (not faucet/sink). Till is a `WalletComponent` seeded large from config; economy doc framing preserved.
3. **Protected shopkeepers use 12b's `ProtectionComponent`** — shopping adds no protection mechanism of its own.
4. **App-wide config via `ShopOptions`** (typed `IOptions<T>`, the established pattern): restock interval, buy-back retention interval, sell/buy-back price **ratio (global)**, default till seed. Per-shop ratio override deferred (backlog).
5. **Buy-back price = what the shop paid the player** (fair mistake-protection); `list` shows base stock and the buy-back shelf together with acquired items flagged; buy-back is the `buy` verb against a shelf item — **no distinct `buyback` verb**.
6. **Buy/sell publish `ItemBoughtEvent`/`ItemSoldEvent`** which drive the persistence-pool transition via `ItemContextHandler` (buy → add `PersistentEntity` to the player's item, **keeping** `BlueprintComponent` as an origin record per INV-21; sell → remove `PersistentEntity`, stamp `ShopStockComponent`). Shop base stock has **no `SpawnSystem` slot reservation** — `SpawnSystem` tracks mob/room spawn slots, not shop inventory — so buying a base item is invisible to `SpawnSystem`; restock is `IShopSystem`-owned and independent (see the Note under Events fired). The plan **extends `ItemContextHandler`** to the new events rather than overloading `ItemPickedUpEvent`/`ItemDroppedEvent` (whose `RoomEntityId` payload is a source/destination room, not a shop).

## Open questions

Both resolved here with the recommended answers (neither is load-bearing for the spec gate; restated for the record).

1. **Restock semantics — RESOLVED: top-up.** Base stock is authored as `(itemBlueprintId, quantity)` rows on `ShopComponent`. The restock sweep computes, per row, `authored − liveBaseCount` (live entities carrying `ShopStockComponent.Provenance == Base` for that blueprint id, in the shop's inventory) and spawns exactly the shortfall via `ITemplateRegistry.Spawn`, stamping each fresh entity with `ShopStockComponent { Base }`. It **tops up to**, never wipes-and-rebuilds: a not-yet-sold base item is left untouched, no entity is destroyed on restock, and a base item a player has not bought never duplicates. (Buy-back-shelf items carry `Provenance == Acquired` and are invisible to the base-stock top-up count.)
2. **`buy`/`sell` argument resolution — RESOLVED: reuse `MobInRoomResolver` + `IItemSystem` find-by-keyword.** The shopkeeper is resolved by the existing unbound `MobInRoomResolver` (`Core/Modules/Combat/Resolvers/`); the item token is resolved by `IItemSystem.TryFindItemInInventory` against the shopkeeper's inventory (for `buy`) or the player's inventory (for `sell`). This makes shopping the **third `MobInRoomResolver` consumer** (kill, useability/cast targeting, shop) — crossing the INV-19 extraction threshold that `combat-system.md` already flags. See the cross-cutting audit below: the resolver moves to a shared, non-combat home as part of this slice (**Gap exposed → framework extraction in-slice**).

---

## Preconditions

- The player is logged in, bound to a character entity with `LocationComponent`, an `InventoryComponent`, and a `WalletComponent` (auto-created on first deposit).
- A shopkeeper mob is in the player's current room: a live entity carrying `MobDataComponent`, `InventoryComponent`, `ShopComponent`, and a `WalletComponent` (the till, seeded on spawn).
- Slice 12a is shipped: every tradeable item carries `ItemDataComponent.Value` (base-unit `long`, Coin). Items with `Value == 0` are valueless — not stocked and refused for sale (12a resolved Q).
- `ShopOptions` is bound from configuration (restock interval, buy-back retention interval, global price ratio, default till seed).
- The heartbeat is running (`HeartbeatTickEvent` published each tick by `HeartbeatBackgroundService`).

## Postconditions

**Buy (base stock):**
- The bought item entity has moved from the shopkeeper's `InventoryComponent` to the player's `InventoryComponent` (via the new `IItemSystem.MoveBetweenInventories` — see the seam note in Systems/handlers).
- `ItemBoughtEvent` is published; the item gains `PersistentEntity`, **keeps** its `BlueprintComponent` (INV-21: preserved as an origin record — pickup does not clear it either), and its `ShopStockComponent` is removed — driven through the persistence-pool transition `ItemContextHandler` performs.
- `IWalletSystem.Transfer(player → till, Coin, buyPrice)` succeeded; player Coin decreased and till Coin increased by exactly `buyPrice = Value × ShopOptions.BuyRatio`.
- On insufficient player funds: no item moves, no wallet mutation, no event; the player sees a refusal line.

**Sell:**
- The sold item entity has moved from the player's `InventoryComponent` to the shopkeeper's `InventoryComponent` (via `IItemSystem.MoveBetweenInventories`).
- `ItemSoldEvent` is published; the item loses `PersistentEntity` (mirroring drop) and gains `ShopStockComponent { Provenance = Acquired, ExpiresAt }`, where `ExpiresAt` is the clock-derived value returned by `IShopSystem.TryResolveSell` (INV-8: the `now + retention` arithmetic lives in the system, not the command).
- `IWalletSystem.Transfer(till → player, Coin, sellPrice)` succeeded; `sellPrice = Value × ShopOptions.SellRatio`.
- On a dry till (`!CanAfford`) or a `Value == 0` item: no item moves, no wallet mutation, no event; the player sees a refusal line.

**Buy (buy-back shelf):**
- A previously-sold item (`ShopStockComponent.Provenance == Acquired`) moves back to the player's inventory at `buyBackPrice == sellPrice the shop paid` (resolved decision 5), re-gaining `PersistentEntity` and losing its `ShopStockComponent`; `ItemBoughtEvent` published. (Same verb as base-stock buy — no `buyback` verb.)

**`list`:**
- The player sees base stock and buy-back shelf together; each line shows name and buy price (via `CurrencyFormatter`); buy-back-shelf lines are flagged as acquired. No state mutation.

**Restock sweep (heartbeat):**
- On the restock interval, every base-stock shortfall (authored quantity − live `Base` entities) is re-spawned fresh from the blueprint with `ShopStockComponent { Base }`; never destroys, never duplicates a surviving base item.

**Buy-back-expiry sweep (heartbeat):**
- On the retention interval, every `Acquired` shop item whose `ExpiresAt <= clock.UtcNow` is destroyed (`EntityService.DestroyEntity`); base stock untouched.

**Restart:**
- Shopkeeper, till, and base stock re-spawn fresh from templates (world content, no `PersistentEntity`); the buy-back shelf is empty (acquired items were made non-persistent on sale).

## Main flow

1. **`list`** — `ListCommand` resolves the shopkeeper via `MobInRoomResolver`, calls `IShopSystem.GetListing(shopEntityId)` which returns base + buy-back rows each with a compute-on-read price (`Value × ratio`); the command renders them with `CurrencyFormatter` (acquired rows flagged). No system mutation, no event.
2. **`buy <item>` (base stock)** — `BuyCommand` resolves the shopkeeper (`MobInRoomResolver`) and the item token against the shop inventory (`IItemSystem.TryFindItemInInventory`); calls `IShopSystem.TryResolveBuy(player, shop, item)` which validates affordability (`IWalletSystem.CanAfford`) and computes price; on success the command calls `IWalletSystem.Transfer(player → till)` then `IItemSystem.MoveBetweenInventories(item, shop → player)`, then publishes `ItemBoughtEvent`. `ItemContextHandler` (subscribed to `ItemBoughtEvent`) adds `PersistentEntity` (**keeping** `BlueprintComponent`, INV-21); `ShopInteractionHandler` removes `ShopStockComponent` and narrates.
3. **`sell <item>`** — `SellCommand` resolves the shopkeeper and the item token against the *player's* inventory; calls `IShopSystem.TryResolveSell(player, shop, item)` (rejects `Value == 0`; checks till `CanAfford`; returns the clock-derived `ExpiresAt`); on success the command calls `IWalletSystem.Transfer(till → player)`, `IItemSystem.MoveBetweenInventories(item, player → shop)`, stamps `ShopStockComponent { Acquired, ExpiresAt }` (the value from `TryResolveSell`, INV-8), then publishes `ItemSoldEvent`. `ItemContextHandler` removes `PersistentEntity`; `ShopInteractionHandler` narrates.
4. **`buy <item>` (buy-back shelf)** — identical command/flow to step 2; the resolved item simply happens to carry `ShopStockComponent.Provenance == Acquired`. `IShopSystem.TryResolveBuy` prices it at the recorded buy-back price (what the shop paid, resolved decision 5) and the same `ItemBoughtEvent` path clears `ShopStockComponent` + restores `PersistentEntity`.
5. **Restock sweep** — on `HeartbeatTickEvent`, `ShopRestockTickHandler` (interval-gated by `TickId`/elapsed against `ShopOptions.RestockInterval`) iterates every entity with `ShopComponent`, calls `IShopSystem.PlanRestock(shopEntityId)` → list of `(blueprintId, shortfallCount)`, then for each shortfall spawns via `ITemplateRegistry.Spawn` into the shop inventory and stamps `ShopStockComponent { Base }`. No event (closed sweep, INV-10).
6. **Buy-back-expiry sweep** — on `HeartbeatTickEvent`, `ShopExpiryTickHandler` (interval-gated) iterates `ShopComponent` shops, calls `IShopSystem.FindExpired(shopEntityId, clock.UtcNow)` → list of acquired item ids past `ExpiresAt`, then calls `EntityService.DestroyEntity` for each. No event (closed sweep, INV-10). The two tick handlers are mutually independent (no ordering, like `CurrencyLootHandler`).

## Events fired

| Event | Payload | Publisher | Subscribers |
|---|---|---|---|
| `ItemBoughtEvent` | `(PlayerEntityId, ShopEntityId, ItemEntityId, RoomEntityId, long PricePaid, CurrencyId)` | `BuyCommand` | `ItemContextHandler` (add `PersistentEntity`, **keep** `BlueprintComponent` per INV-21), `ShopInteractionHandler` (clear `ShopStockComponent` + narrate) |
| `ItemSoldEvent` | `(PlayerEntityId, ShopEntityId, ItemEntityId, RoomEntityId, long PriceReceived, CurrencyId)` | `SellCommand` | `ItemContextHandler` (remove `PersistentEntity`), `ShopInteractionHandler` (narrate) |

Restock and expiry sweeps publish **nothing** (closed heartbeat sweeps with no game-rule fan-out, INV-10).

> **Note (resolved decision 6 — planner call):** rather than subscribing the existing `ItemContextHandler` to `ItemPickedUpEvent`/`ItemDroppedEvent` (whose payloads carry `RoomEntityId` as the *source/destination room*, not a shop), this plan **extends `ItemContextHandler` to also subscribe to `ItemBoughtEvent`/`ItemSoldEvent`** and apply the same persistence-pool transition. The `Bought`/`Sold` events carry shop-specific payload (price, shop id) the pickup/drop events do not, and the buy path additionally needs the `BlueprintComponent` clear that pickup performs. Reusing the *handler* (not the events) keeps the pool-transition logic in one place (INV-19) without overloading pickup/drop semantics onto a trade. The base-stock spawn-slot consistency that resolved decision 6 calls out is preserved because base-stock items are world content with no spawn-slot reservation (`SpawnSystem` tracks mob/room spawn slots, not shop inventory) — buying a base item simply removes one live entity that the next restock sweep re-creates.

## Systems / handlers involved

**New domain system — `IShopSystem` (`Core/Modules/Shopping/Systems/`):** pure, returns results, composes `IWalletSystem` + `IItemSystem` (domain→domain, same tier — permitted); never touches the bus or persistence (INV-5). Reads `ItemDataComponent.Value`, `ShopComponent`, `ShopStockComponent`; takes `IClock` for expiry stamping/decisions (INV-26).

- `GetListing(uint shopEntityId) → ShopListing` (base + acquired rows, each with compute-on-read price)
- `TryResolveBuy(uint playerEntityId, uint shopEntityId, uint itemEntityId) → ShopBuyResult` (price + affordability decision; does not mutate)
- `TryResolveSell(uint playerEntityId, uint shopEntityId, uint itemEntityId) → ShopSellResult` (price + `Value > 0` + till-affordability decision + the clock-derived `ExpiresAt` for the sell stamp; does not mutate — INV-8 keeps the `now + retention` arithmetic in the system)
- `PlanRestock(uint shopEntityId) → IReadOnlyList<(string blueprintId, int shortfall)>`
- `FindExpired(uint shopEntityId, DateTime nowUtc) → IReadOnlyList<uint>`
- `SeedTill(uint shopEntityId)` — applies the configured till seed on spawn (or this lives in the spawn/template path; see Work package 1)

**Reused systems:** `IWalletSystem` (`Transfer`/`CanAfford`/`Deposit`), `IItemSystem` (`TryFindItemInInventory`/`GetItemsInInventory`; **+ new `MoveBetweenInventories`** — see seam note), `ITemplateRegistry` (`Spawn` for restock), `EntityService` (`DestroyEntity`, component add/remove), `IClock`, `CurrencyFormatter`/`ICurrencyRegistry` (display), `IMobBuilderSystem` (authoring), `EntityService.GetAllComponents<ShopComponent>` (sweep iteration).

**New seam on `IItemSystem` (Items module) — `MoveBetweenInventories(uint itemEntityId, uint fromHolderEntityId, uint toHolderEntityId)`:** the existing `MoveToInventory` is **ground→inventory only** — it requires a `LocationComponent` on the item and silently no-ops if absent (`Core/Modules/Items/Systems/ItemSystem.cs`), so it cannot move an item between two `InventoryComponent` holders (shop↔player, neither of which carries a `LocationComponent`). This slice adds an inventory→inventory move to `IItemSystem`: remove the id from the source holder's `InventoryComponent`, append to the destination's; it touches **no** `LocationComponent` and **no** `BlueprintComponent` (INV-21). It is a reusable seam the deferred player-trade, banking, and give-to-NPC features also need (≥3 future consumers — INV-19), so it lands on the shared item seam rather than inside Shopping. **INV-20:** the `add-domain-system` skill's void-mutation example list gains this method in the same PR; **INV-16:** the `ItemSystem` row in `docs/reference/systems.md` is updated.

**Handlers:**

| Handler | Event(s) | Priority | Role |
|---|---|---|---|
| `ItemContextHandler` (extended) | + `ItemBoughtEvent`, `ItemSoldEvent` | 20 (Domain) | persistence-pool transition: buy → add `PersistentEntity` (**keep** `BlueprintComponent`, INV-21); sell → remove `PersistentEntity` |
| `ShopInteractionHandler` (new) | `ItemBoughtEvent`, `ItemSoldEvent` | 80 (Notification) | pure output fan-out ("You buy/sell … for …" via `CurrencyFormatter`; room broadcast); also clears `ShopStockComponent` on buy (domain-state cleanup co-located with narration is acceptable here, or split into `ItemContextHandler` — see WP-2) |
| `ShopRestockTickHandler` (new) | `HeartbeatTickEvent` | 20 (Domain) | interval-gated restock sweep; calls `IShopSystem.PlanRestock` + `ITemplateRegistry.Spawn`; publishes nothing |
| `ShopExpiryTickHandler` (new) | `HeartbeatTickEvent` | 20 (Domain) | interval-gated expiry sweep; calls `IShopSystem.FindExpired` + `EntityService.DestroyEntity`; publishes nothing |

**Commands (Initiators):** `ListCommand` (`list`), `BuyCommand` (`buy`), `SellCommand` (`sell`). All thin (INV-8): resolve → call `IShopSystem` → `Transfer` + `MoveBetweenInventories` → publish.

## Implementation plan — work packages

Each package is independently executable and testable. The primary agent runs `architecture-reviewer` (code mode) across the combined diff once all three land.

### WP-1 — Shop state, config, and authoring substrate
**Scope:** `ShopComponent` (`Core/Modules/Shopping/Components/`: accepted `CurrencyId`, till seed, optional per-shop ratio override [unused — deferred], base-stock rows `(blueprintId, quantity)`); `ShopStockComponent` (`Provenance {Base, Acquired}`, `DateTime? ExpiresAt`); `ShopOptions` (`Core/Modules/Shopping/`, bound `Shop:` — `RestockInterval`, `BuyBackRetention`, `BuyRatio`, `SellRatio`, `DefaultTillSeed`); `ShoppingModule.AddShoppingModule(IServiceCollection)` wiring `IShopSystem` + `Configure<ShopOptions>`; `IMobBuilderSystem.SetMobShop` authoring method + `MobTemplate` shop fields + `MobContentWriter` YAML round-trip + Blazor `MobEditor` shop row + till-seed-on-spawn (template `Apply` adds `WalletComponent` seeded from `ShopComponent`/`ShopOptions.DefaultTillSeed`, and stamps base-stock items with `ShopStockComponent { Base }` at spawn). Wire `AddShoppingModule` from `Server/Program.cs`/`CompositionRoot.cs`.
**Files:** `Core/Modules/Shopping/Components/ShopComponent.cs`, `ShopStockComponent.cs`, `Core/Modules/Shopping/ShopOptions.cs`, `Core/Modules/Shopping/ShoppingModule.cs`, `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` (+interface), `MobContentWriter.cs`, `Hedron.Web` MobEditor, `Server/CompositionRoot.cs`.
**Out of scope:** the trade verbs, the sweeps, pricing math.
**Exit criterion:** a shopkeeper authored in YAML spawns with a seeded till, a populated base-stock inventory each item carrying `ShopStockComponent { Base }`, and no `PersistentEntity` on shopkeeper/stock (round-trip test asserts absence).

### WP-2 — `IItemSystem.MoveBetweenInventories` + `IShopSystem` + trade verbs + events + persistence transition
**Scope:** **add `IItemSystem.MoveBetweenInventories` (+impl)** in the Items module — the inventory→inventory move (no `LocationComponent`/`BlueprintComponent` mutation), with the `add-domain-system` skill void-mutation example (INV-20) and the `reference/systems.md` `ItemSystem` row (INV-16) updated in the same PR; `IShopSystem` (all six methods, `TryResolveSell` returns the clock-derived `ExpiresAt`) reading `Value` and composing `IWalletSystem`/`IItemSystem`/`IClock`; `ItemBoughtEvent`/`ItemSoldEvent`; `ListCommand`/`BuyCommand`/`SellCommand` (resolvers per resolved Q2); extend `ItemContextHandler` to subscribe to the two new events (buy → add `PersistentEntity`, keep `BlueprintComponent`; sell → remove `PersistentEntity`); `ShopInteractionHandler` (narration + `ShopStockComponent` clear on buy). Depends on WP-1.
**Files:** `Core/Modules/Items/Systems/IItemSystem.cs` + `ItemSystem.cs`, `.claude/skills/add-domain-system/SKILL.md`, `docs/reference/systems.md`, `Core/Modules/Shopping/Systems/IShopSystem.cs` + `ShopSystem.cs`, result records, `Core/Modules/Shopping/Events/ItemBoughtEvent.cs` + `ItemSoldEvent.cs`, `Core/Modules/Shopping/Commands/{List,Buy,Sell}Command.cs`, `Core/Modules/Shopping/Handlers/ShopInteractionHandler.cs`, `Core/Modules/Spawn/Handlers/ItemContextHandler.cs` (extend), `Server/Program.cs` (command + handler wiring).
**Out of scope:** the heartbeat sweeps; the resolver extraction (WP-3).
**Exit criterion:** buy/sell/buy-back/list pass system-unit + handler tests with a fake `IClock`/`IWalletSystem`; `MoveBetweenInventories` has its own Items-module unit test; persistence-pool transitions assert `PersistentEntity` add/remove and `BlueprintComponent` **preservation** (INV-21).

### WP-3 — Heartbeat sweeps + `MobInRoomResolver` extraction (INV-19)
**Scope:** `ShopRestockTickHandler` + `ShopExpiryTickHandler` (interval-gated on `HeartbeatTickEvent`, deterministic via `TickId`/`Elapsed`); **extract `MobInRoomResolver` to a shared non-combat home** (`Core/Resolvers/` or `Core/Modules/Mobs/Resolvers/`) now that it has a third consumer, updating combat + ability registrations and references. Depends on WP-1 (`ShopComponent`); the resolver extraction can run parallel to WP-2 but its consumers (the WP-2 commands) bind the moved type.
**Files:** `Core/Modules/Shopping/Handlers/ShopRestockTickHandler.cs`, `ShopExpiryTickHandler.cs`, moved `MobInRoomResolver.cs` (+ all reference updates in Combat/Abilities modules), `Server/Program.cs` (handler wiring).
**Out of scope:** trade verbs (WP-2).
**Exit criterion:** restock tops up to authored levels and expiry destroys past-`ExpiresAt` acquired items, both deterministic under a fake clock/forced tick; resolver extraction compiles with all three consumers green.

## Content tooling impact (INV-18)

- **`ShopComponent` authoring:** `IMobBuilderSystem.SetMobShop` (dual-write live entity + `MobTemplate`, the established `SetMobProtection`/`SetItemSlots` pattern) exposed via `setmob shop …` and round-tripped through `MobContentWriter` YAML (new `shop:` block: accepted currency, till seed, per-shop ratio override, base-stock `[(blueprintId, quantity)]`) and a Blazor `MobEditor` shop section.
- **`ShopStockComponent`** is runtime-only provenance/expiry state — not authored directly; stamped by the spawn path (`Base`) and the sell flow (`Acquired`). No authoring surface.
- **`ShopOptions`** is app-wide config (`appsettings.json` `Shop:` section), not per-entity content; documented defaults shipped.
- **Inspection:** a designer inspects a live shop via `list` (player-facing) and the shopkeeper's `MobEditor` row (authoring); base-stock vs. buy-back provenance is visible in `list`'s acquired flag.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Notes |
|---|---|---|
| **Commands** | Adequate | `list`/`buy`/`sell` use the existing command framework + `IArgumentResolver`; no new plumbing. |
| **Argument resolvers** | **Gap exposed → extract in-slice** | `MobInRoomResolver` gains its **third** consumer (kill, ability targeting, shop). Per INV-19 and the explicit flag in `combat-system.md`, it moves out of `Core/Modules/Combat/Resolvers/` to a shared home in WP-3. Not absorbed silently. |
| **Item-movement seam** | **Gap exposed → framework in-slice** | The existing `IItemSystem.MoveToInventory` is ground→inventory only; shop↔player trade needs an inventory→inventory move. New `IItemSystem.MoveBetweenInventories` lands on the shared item seam (not inside Shopping) — deferred player-trade/banking/give-to-NPC are ≥3 future consumers (INV-19). Carries INV-20 (`add-domain-system` skill) + INV-16 (`reference/systems.md`) updates in WP-2. |
| **Output / display** | Adequate | Prices format via the shared `CurrencyFormatter`/`ICurrencyRegistry` (currency-foundation precedent); narration via `IBroadcastSystem`, the `CurrencyAwardNarrationHandler` pattern. |
| **Event bus** | Adequate | Two new past-tense thin events; sweeps publish nothing (INV-10). Persistence-transition reuse via `ItemContextHandler` (one home, INV-19). |
| **Persistence (opt-in)** | Adequate | See audit below — no new save sites; all transitions ride the flush via component add/remove. |
| **ECS queries** | Adequate | `GetAllComponents<ShopComponent>` for sweeps, `HasComponent`/`TryGet` for provenance — no new query infra. |
| **Time / heartbeat** | Adequate | Two interval-gated `HeartbeatTickEvent` handlers, the `EffectTickHandler`/`AbilityCooldownTickHandler` precedent; all time via injected `IClock`/tick fields (INV-26). |
| **Content templates** | Adequate | `MobTemplate` extended with a `shop:` block; `MobContentWriter` + deserializer round-trip it — the established item/mob field-addition path. |
| **Configuration** | Adequate | `ShopOptions` via `IOptions<T>` (`DeathOptions`/`WorldOptions` precedent). |
| **Sessions / broadcast** | Adequate | Narration addresses the actor + room via existing `IBroadcastSystem` filters. |
| **Modules** | Adequate | New `Core/Modules/Shopping/` with `AddShoppingModule`; handler subscriptions in central `Server/Program.cs` (the `CurrencyLootHandler` precedent). |

### Persistence opt-in audit (INV-22/23)

**Level 1 — entity-domain classification:**
- *Shopkeeper mob* — **world content** (no `PersistentEntity`); re-spawns from `MobTemplate`. Its `WalletComponent` (till), though `[Persistent]`-tagged, is never written (two-level opt-in) and re-seeds on spawn each restart.
- *Base-stock items* — **world content** (no `PersistentEntity`); re-spawn from blueprint on the restock sweep / at shopkeeper spawn.
- *Acquired (player-sold) items* — transition **persistent → world-transient** on sale: `ItemContextHandler` removes `PersistentEntity` (mirroring drop). They live only until expiry or restart, so the buy-back shelf is empty after a restart — no dangling reference inside a non-persistent re-spawned mob. On buy-back, `ItemContextHandler` re-adds `PersistentEntity` (mirroring pickup) as the item re-enters the player's inventory.
- *Bought items (player side)* — transition **world-transient → persistent**: `ItemContextHandler` adds `PersistentEntity` and **preserves** `BlueprintComponent` (INV-21: kept as an origin record — the real `MoveToInventory`/pickup path does *not* clear it). The transition is driven by `ItemBoughtEvent`/`ItemSoldEvent` through `ItemContextHandler`, **not** by the command (mirrors the resolved pattern that the *context* handler, not the verb, owns the pool transition).

**Level 2 — component inclusion:**
- `ShopComponent` — **omit `[Persistent]`**: world-content config on a mob (durable form is `MobTemplate` YAML); the `ProtectionComponent`/`CurrencyLootComponent` precedent.
- `ShopStockComponent` — **omit `[Persistent]`**: provenance/expiry is runtime-transient world state; base items re-spawn fresh, acquired items are intentionally dropped on restart.
- `WalletComponent` (till, reused) — already `[Persistent]`, but **never written** for the shopkeeper because the mob carries no `PersistentEntity` (the till re-seeds on spawn). No change to the component.
- `InventoryComponent` (reused) — already `[Persistent]`; on the world-content shopkeeper it is likewise never written. No change.

**Level 3 — save-on-change scope:** the slice introduces **no `SaveEntityAsync` call sites**. Buy/sell durability rides the periodic flush via `ItemContextHandler`'s `PersistentEntity` add/remove (the exact pickup/drop model). No handler or non-admin command force-saves a runtime state change → INV-22 satisfied.

## Flows introduced or modified (INV-17)

- **New: Shopping journey (`list` · `buy` · `sell` · buy-back)** — `flow-NN-shopping.md` (next free number): player command → `MobInRoomResolver` + `IItemSystem` resolution → `IShopSystem` decision → `IWalletSystem.Transfer` + `IItemSystem.MoveBetweenInventories` → `ItemBoughtEvent`/`ItemSoldEvent` → `ItemContextHandler` (pool transition) + `ShopInteractionHandler` (narration). Add the index row in `flows/README.md`.
- **New: Shop maintenance sweeps (restock · buy-back expiry)** — either a section of the shopping flow or a sibling `flow-NN-shop-maintenance.md`: `HeartbeatTickEvent` → interval-gated `ShopRestockTickHandler`/`ShopExpiryTickHandler` → `IShopSystem.PlanRestock`/`FindExpired` → `ITemplateRegistry.Spawn` / `EntityService.DestroyEntity`. Cross-reference [Flow 16 (Heartbeat tick)](../architecture/flows/flow-16-heartbeat-tick.md).
- **Modified: [Items journey (flow-09)](../architecture/flows/flow-09-item-pickup.md)** — `ItemContextHandler` now also drives pool transitions for `ItemBoughtEvent`/`ItemSoldEvent`; update the handler's subscription list and the persistence-transition note.

## Test plan / Verification (INV-25)

Deterministic throughout: fake `IClock` for expiry stamping/decisions, forced `HeartbeatTickEvent`/`TickId` for sweep gating (INV-26); fake or real `IWalletSystem` for transfer outcomes.

**System-unit (`IShopSystem` decisions — `Core/Modules/Shopping/Systems/`):**
- `TryResolveBuy` prices base stock at `Value × BuyRatio`; affordable vs. unaffordable returns success/refusal without mutating.
- `TryResolveBuy` against an `Acquired` item prices it at the recorded buy-back price (what the shop paid), not `Value × BuyRatio`.
- `TryResolveSell` prices at `Value × SellRatio`; refuses `Value == 0`; refuses when the till `!CanAfford`.
- `PlanRestock` returns `authored − liveBaseCount` per row; returns zero shortfall when full; ignores `Acquired` items in the count (top-up semantics, resolved Q1).
- `FindExpired` returns exactly the `Acquired` items with `ExpiresAt <= nowUtc` and never a `Base` item (fake clock).

**Handler tier:**
- `ItemContextHandler` on `ItemBoughtEvent` → asserts item gains `PersistentEntity`, **retains** `BlueprintComponent` (INV-21), and `ShopStockComponent` removed (internal state — Postcondition coverage).
- `IItemSystem.MoveBetweenInventories` (Items-module unit) → asserts the id leaves the source holder's `InventoryComponent` and appears in the destination's, with no `LocationComponent` and no `BlueprintComponent` mutation.
- `ItemContextHandler` on `ItemSoldEvent` → asserts item loses `PersistentEntity` and gains `ShopStockComponent { Acquired, ExpiresAt }` with the clock-derived value.
- `ShopRestockTickHandler` → forced tick at interval spawns exactly the shortfall, each fresh entity carrying `ShopStockComponent { Base }`; sub-interval tick is a no-op (interval gating).
- `ShopExpiryTickHandler` → forced tick destroys past-`ExpiresAt` acquired items only; not-yet-expired and base items survive.

**Flow tier:**
- End-to-end `buy`: player wallet decreases and till increases by exactly `buyPrice`; item is in player inventory; `ItemBoughtEvent` published.
- End-to-end `sell` then `buy` (buy-back): item round-trips, player nets the buy/sell ratio spread; persistence pool flips persistent → transient → persistent.

**Persistence round-trip:**
- Shopkeeper + base stock save→load: assert **no** SQLite rows (world content, no `PersistentEntity`) — the `CurrencyLootComponent` precedent.
- A bought item on a player save→load survives **with its `BlueprintComponent` intact** (INV-21); an acquired shelf item does **not** survive a restart (no `PersistentEntity`).

**Skipped (with reason):**
- Exact narration prose in `ShopInteractionHandler` and `list` rendering — presentation, asserted only at the "an output line addressed the actor" granularity, not verbatim text (07-testing rubric).
- `ShopComponent`/`ShopStockComponent` field accessors — pure-data components, no logic.
- Thin command plumbing (`ListCommand`/`BuyCommand`/`SellCommand` argument wiring) beyond the flow-tier end-to-end — the decision logic is all in `IShopSystem`, covered by system-unit tests.
- `MobInRoomResolver` extraction — behavior-preserving move; existing combat/ability resolver tests cover it, plus the new buy/sell flow tests exercise the moved type.
