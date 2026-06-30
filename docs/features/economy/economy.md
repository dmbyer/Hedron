# Economy

> The economy feature: entity-keyed **wallets** + a registry of currency families (the substrate), opt-in mob currency-loot, and **shopping** — players buy/sell against shopkeeper mobs. **Status:** live (currency-foundation + slice 12c shopping).

## What it is

Every entity can hold a **wallet** — a ledger of balances keyed by currency. The launch currency is **Coin**, whose copper / silver / gold are *denominations* of a single base-unit ledger (`100c = 10s = 1g`), stored as one base-unit (copper) integer and presented up the ladder only at display time. A player earns currency by killing mobs that carry an opt-in loot range, inspects their balance on the `score` screen, and (for admins) has it granted via `setwallet`. Shopping, banking, player trade, and quest rewards are all future consumers of the same wallet seam.

The model is deliberately a **stored ledger, not a stat** — currency is directly mutated and persisted, with no base+modifier computation, no max, and no effect targeting. It is co-located with stats on the `score` screen for display only; it is not part of the `IStatSystem` substrate.

## How it works

The feature composes three pieces (the registry is core; the two systems are domain):

- **`CurrencyRegistry`** (core) — the lookup spine: every `CurrencyDefinition` (name + ordered denomination ladder) keyed by `CurrencyId`. A `DefinitionRegistry<TKey,TDef>` subclass (the `StatRegistry`/`AspectRegistry` precedent), construction-validated (ladder strictly ascending, base unit = 1). A new currency family or denomination is a registry row, not code.
- **`WalletSystem`** (domain) — the single wallet-mutation seam: `GetBalance`/`GetBalances`/`Deposit`/`TryWithdraw`/`CanAfford`/`Transfer`/`SetBalance`. Operates on **any entity** carrying a `WalletComponent`; returns results, never publishes events or calls persistence (INV-5). `Transfer` is the atomic withdraw-then-deposit primitive every future economy verb (shop, bank, trade, mail) reuses.
- **`CurrencyLootSystem`** (domain) — rolls a mob's opt-in `CurrencyLootComponent` range uniformly via injected `IRandom` (INV-26) and returns a `CurrencyLootResult`. The auto-award orchestration is a handler, not the system.

The headline runtime path — mob death → loot roll → auto-award — is the [Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md); the wallet and registry internals are the [wallet-system design doc](wallet-system.md); the loot roll + award handlers are the [currency-loot-system design doc](currency-loot-system.md).

## Systems

| System | Role |
|---|---|
| [`wallet-system.md`](wallet-system.md) | Currency registry + denomination ladder, the `IWalletSystem` mutation seam (incl. `Transfer`), persistence, and ladder formatting |
| [`currency-loot-system.md`](currency-loot-system.md) | Opt-in `CurrencyLootComponent`, the `IRandom`-backed loot roll, and the auto-award + narration handlers on `MobDiedEvent` |
| [`shop-system.md`](shop-system.md) | Shopkeeper trade: `ShopComponent` + per-item `ShopStockComponent`, the pure `IShopSystem` (compute-on-read pricing, buy/sell/buy-back, restock + expiry), and the `buy`/`sell`/`list` verbs — the first `IWalletSystem.Transfer` consumer |

## Surfaces

- **Commands** — `score` (player, extended: shows wallet balances up the ladder), `setwallet <player> <currency> <amount>` (admin, absolute-set + boundary save + audit); **shopping:** `list` (browse a shop), `buy <item>` (base stock or buy-back), `sell <item>` (player, against the shopkeeper in the room), authored via `setmob shop` (admin). See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `CurrencyAwardedEvent` (loot/reward award), `WalletSetByAdminEvent` (admin audit); **shopping:** `ItemBoughtEvent` / `ItemSoldEvent` (drive the persistence-pool transition via `ItemContextHandler` + narration). All thin, past-tense. See [`../../architecture/03-events.md`](../../architecture/03-events.md).
- **Components** — `WalletComponent` (`[Persistent]`, entity-keyed balance ledger), `CurrencyLootComponent` (**not** `[Persistent]`), `ShopComponent` + `ShopStockComponent` (**not** `[Persistent]` — world-content trade config + per-item provenance). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — `MobTemplate` carries the opt-in per-`CurrencyId` `(min, max)` loot range and a `shop:` block (accepted currency, till seed, base-stock rows); the Blazor `MobEditor` exposes both; YAML round-trips them. Absent ⇒ no component ⇒ no loot / not a shop (opt-in defaults). `ShopOptions` (`Shop:` config) holds app-wide restock/retention intervals, price ratios, and the default till seed.

## The extensibility seam (banks, safes, shops, trade)

The wallet is **entity-keyed**, and `IWalletSystem` operates on any wallet-bearing entity — this is the lever the whole economy hangs off. A bank account, a safe, a guild vault, a vendor till, and a corpse are not new currency mechanisms; they are new *holders* of the same ledger. Every interaction (bank deposit/withdraw, safe, player trade, mail/COD, shop buy/sell) reduces to one operation: an atomic `Transfer` of N of currency C from wallet A to wallet B iff A can afford it — differing only in *which two entities* and *what authorizes the move*. Authorization (owner-only, guild-rank) lives in the resolving command/handler (INV-8), never in the wallet system. **Shopping (slice 12c) is the first realized `Transfer` consumer** — a vendor till ↔ player wallet — validating the seam; banking, player trade, and mail are the remaining anticipated consumers. *Where* a bank balance lives (account-wide, per-character, or per-location) is a future content decision the seam does not foreclose.

## Related

- [`wallet-system.md`](wallet-system.md) · [`currency-loot-system.md`](currency-loot-system.md) — the design docs.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems return; handlers publish), INV-14/22/23 (persistence two-level opt-in; admin boundary save; world-content vs persistent domains), INV-24 (currency is correctly a directly-owned ledger, **not** a computed score — no contributor port), INV-26 (loot roll determinism).
- [`../../roadmap/completed/currency-foundation.md`](../../roadmap/completed/currency-foundation.md) — as-built history and design decisions.
- **Mobs** — [`../mobs/mob-system.md`](../mobs/mob-system.md) — `MobTemplate` + authoring path the loot range extends.
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — `MobDiedEvent` (published pre-destroy) is the loot handler's trigger.
- **Character stats** — [`../character-stats/stat-system.md`](../character-stats/stat-system.md) — the `StatRegistry` `DefinitionRegistry` precedent and the `score`-screen co-location (display only).
- **Shopping** — [`shop-system.md`](shop-system.md) — the trade half of this feature; the first realized `Transfer` consumer (vendor till ↔ player wallet).
