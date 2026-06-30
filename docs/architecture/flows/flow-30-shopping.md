# Flow 30 — Shopping journey (`list` · `buy` · `sell` · buy-back)

> [Back to flows index](README.md). **Trigger:** player sends `list`, `buy <item>`, or `sell <item>` in a room with a shopkeeper; the heartbeat drives restock + buy-back-expiry sweeps. Source: [shop-system.md](../../features/economy/shop-system.md).

## Summary

The thin trade commands (Initiators) resolve the shopkeeper (the implicit `ShopComponent` mob in the room) and the item, call a pure `IShopSystem` decision, and on success perform an atomic `IWalletSystem.Transfer` (till ↔ player wallet) plus `IItemSystem.MoveBetweenInventories`, then publish `ItemBoughtEvent`/`ItemSoldEvent`. `ItemContextHandler` applies the persistence-pool transition (add/remove `PersistentEntity`, keep `BlueprintComponent`); `ShopInteractionHandler` narrates and clears `ShopStockComponent` on buy. Prices are computed on read from `ItemDataComponent.Value × ShopOptions` ratios. Two independent heartbeat sweeps keep the shop topped up and the buy-back shelf pruned.

## Trade path (`buy` shown; `sell` mirrors)

```mermaid
sequenceDiagram
    participant P as Player
    participant BC as BuyCommand
    participant SS as IShopSystem
    participant WS as IWalletSystem
    participant IS as IItemSystem
    participant Bus as IEventBus
    participant H as ItemContext + ShopInteraction handlers

    P->>BC: "buy <item>"
    BC->>SS: TryResolveBuy(player, shop, item)
    Note over SS: price = Value×BuyRatio (or recorded buy-back price);<br/>CanAfford? — pure, no mutation
    SS-->>BC: ShopBuyResult { ok, price, currency }
    BC->>WS: Transfer(player → till, price)
    BC->>IS: MoveBetweenInventories(item, shop → player)
    BC->>Bus: Publish(ItemBoughtEvent)
    Bus->>H: ItemContext: +PersistentEntity, keep Blueprint, -ShopStock · ShopInteraction: narrate
```

## Steps

1. **Resolve.** `BuyCommand`/`SellCommand` reads the invoker's `LocationComponent`, finds the room's shopkeeper (first `ShopComponent` mob), and resolves the item token via `IItemSystem.TryFindItemInInventory` against the shop inventory (buy) or the player's inventory (sell). `ListCommand` may instead name a shopkeeper via the shared `MobInRoomResolver`.
2. **Decide (pure).** `IShopSystem.TryResolveBuy` / `TryResolveSell` / `GetListing` computes the price on read (`Value × ShopOptions.BuyRatio`/`SellRatio`, or the recorded paid price for a buy-back item), checks affordability (`IWalletSystem.CanAfford` — the player on buy, the **till** on sell), rejects `Value == 0` on sell, and returns the clock-derived `ExpiresAt` for the sell stamp (INV-8: the `now + retention` arithmetic lives in the system). No mutation, no event.
3. **Commit (command).** On success the command performs `IWalletSystem.Transfer` (`player → till` on buy, `till → player` on sell) then `IItemSystem.MoveBetweenInventories` (touches no `LocationComponent`/`BlueprintComponent`); on sell it stamps `ShopStockComponent { Acquired, ExpiresAt }`. On a refusal (insufficient funds, dry till, valueless item) nothing moves and no event fires — only a refusal line.
4. **Publish.** The command publishes `ItemBoughtEvent` / `ItemSoldEvent` (thin, past-tense).
5. **Persistence-pool transition.** `ItemContextHandler` (Domain, p=20) — buy: add `PersistentEntity`, **keep** `BlueprintComponent` (INV-21), remove `ShopStockComponent`; sell: remove `PersistentEntity` (the buy-back-shelf item becomes world-transient, mirroring drop). No `SaveEntityAsync` — durability rides the periodic flush.
6. **Narrate.** `ShopInteractionHandler` (Notification, p=80) writes the "You buy/sell … for …" line via `CurrencyFormatter` and broadcasts to the room.

**Buy-back** is identical to step 1–6 for `buy`; the resolved item simply carries `ShopStockComponent.Provenance == Acquired`, and `TryResolveBuy` prices it at the recorded buy-back price.

## Maintenance sweeps (heartbeat)

On each `HeartbeatTickEvent` (see [Flow 16](flow-16-heartbeat-tick.md)), two interval-gated handlers iterate every `ShopComponent` shop. Both are **closed sweeps** — they call an `IShopSystem` decision then mutate/spawn/destroy entities, and **publish nothing** (INV-10). They are mutually independent (no ordering) and deterministic (gate on accumulated `Elapsed`; time via injected `IClock`, INV-26):

- **`ShopRestockTickHandler`** (gated on `ShopOptions.RestockInterval`) → `IShopSystem.PlanRestock(shop)` returns `(blueprintId, shortfall)` per base-stock row (`authored − liveBaseCount`); for each shortfall it spawns via `ITemplateRegistry.Spawn` into the shop inventory and stamps `ShopStockComponent { Base }`. **Top-up** — never destroys a surviving base item, never duplicates one.
- **`ShopExpiryTickHandler`** (gated on `ShopOptions.BuyBackRetention`) → `IShopSystem.FindExpired(shop, clock.UtcNow)` returns `Acquired` item ids past `ExpiresAt`; it calls `EntityService.DestroyEntity` for each. Base stock untouched.

## Where to look

- [`Core/Modules/Shopping/`](../../../Core/Modules/Shopping/) — `IShopSystem`/`ShopSystem`, `Buy`/`Sell`/`ListCommand`, `ShopInteractionHandler`, `ShopRestockTickHandler`/`ShopExpiryTickHandler`, `ShopkeeperSpawnHandler`, `ShopComponent`/`ShopStockComponent`, `ItemBought`/`ItemSoldEvent`.
- [`Core/Modules/Items/Systems/ItemSystem.cs`](../../../Core/Modules/Items/Systems/ItemSystem.cs) — `MoveBetweenInventories`; [`Core/Modules/Spawn/Handlers/ItemContextHandler.cs`](../../../Core/Modules/Spawn/Handlers/ItemContextHandler.cs) — the shared pool transition (also [Flow 9](flow-09-item-pickup.md)).
- [shop-system.md](../../features/economy/shop-system.md) · [economy feature](../../features/economy/economy.md) — design + feature view.
