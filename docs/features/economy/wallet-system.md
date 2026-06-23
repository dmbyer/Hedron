# Wallet system & currency registry

> Design doc for the currency substrate: the `CurrencyRegistry` (Spine F), the `IWalletSystem` mutation seam, persistence, and ladder formatting. Holistic view: [`economy.md`](economy.md).

## Currency registry & denomination ladder

A **currency family** is a `CurrencyDefinition` ([`CurrencyDefinition.cs`](../../../Core/Modules/Economy/CurrencyDefinition.cs)) keyed by the `CurrencyId` enum ([`CurrencyId.cs`](../../../Core/Modules/Economy/CurrencyId.cs)). The launch family is `Coin`. A definition carries a name and an **ordered denomination ladder** — a list of `Denomination(name, baseUnits)` ascending from the base unit: copper = 1, silver = 10, gold = 100. The base-unit multiplier is always 1; the ladder is strictly ascending.

`CurrencyRegistry` ([`CurrencyRegistry.cs`](../../../Core/Modules/Economy/CurrencyRegistry.cs)) is a `DefinitionRegistry<CurrencyId, CurrencyDefinition>` subclass — the same Spine F pattern as `StatRegistry`/`AspectRegistry` ([`../../reference/systems.md`](../../reference/systems.md)). It is a **core system** (generic lookup, no game semantics) and is registered as a singleton via `AddEconomyModule`. Rows are **validated at construction**: a non-ascending ladder or a base unit ≠ 1 throws (fail-fast, the registry-validation idiom).

**Why a registry, not constants.** "New currencies as the game expands" has two axes, both pure data: a new *denomination* (e.g. platinum above gold) is a ladder row; a new *family* (e.g. an Astral currency or faction marks, which do **not** convert into Coin) is a new `CurrencyId` + registry row + wallet-dictionary entry. No code path grows per currency.

## The wallet

`WalletComponent` ([`WalletComponent.cs`](../../../Core/Modules/Economy/Components/WalletComponent.cs)) is a pure-data (INV-3) `[Persistent]` component holding `Dictionary<CurrencyId, long> Balances` in **base units**. It is **entity-keyed** — any entity may carry one (player, vendor, future bank/safe/vault). Balances are non-negative `long`s.

**Persistence (INV-14/23).** `WalletComponent` is `[Persistent]`; it is added to already-persistent player entities (no new `PersistentEntity`, no domain transition), so player balances survive restart. The `Dictionary<CurrencyId, long>` key is serialized **by enum name, not ordinal** — `ComponentSerializer` registers `JsonStringEnumConverter` globally, which covers enum dictionary keys; a round-trip test asserts the key persists as `"Coin"` not `"0"` so a future `CurrencyId` reordering cannot silently corrupt saved wallets.

## The mutation seam — `IWalletSystem`

`IWalletSystem` ([`IWalletSystem.cs`](../../../Core/Modules/Economy/Systems/IWalletSystem.cs) · [`WalletSystem.cs`](../../../Core/Modules/Economy/Systems/WalletSystem.cs)) is a **domain system** and the single verb surface for moving value. It returns results and **never** touches the event bus or persistence (INV-5) — Initiators and handlers own those.

| Method | Contract |
|---|---|
| `GetBalance(entity, currency)` | Current base-unit balance (0 if no wallet / no entry). |
| `GetBalances(entity)` | `IReadOnlyDictionary<CurrencyId,long>` (empty if no wallet) — the `score` read. |
| `Deposit(entity, currency, amount)` | Adds `amount`; **creates `WalletComponent` on first deposit**; rejects negative `amount` (no-op). |
| `TryWithdraw(entity, currency, amount)` | `false` + unchanged when `balance < amount`; else `true` + decrement. |
| `CanAfford(entity, currency, amount)` | `balance >= amount`; mutates nothing. |
| `Transfer(from, to, currency, amount)` | **Atomic**: debits `from` and credits `to` iff `from` can afford it; on insufficient funds returns `false` with **neither** wallet mutated (no partial transfer). Self-transfer (`from == to`) is a balance-preserving no-op returning `true`. |
| `SetBalance(entity, currency, amount)` | Absolute-set (the `setwallet` admin path). |

**`Transfer` is the economy primitive.** Shopping, banking, player trade, and mail-COD are all transfers between two wallet-bearing entities; the atomic withdraw-then-deposit discipline lands **once** here rather than being re-rolled per consumer (INV-19). It is trivially atomic on the single-threaded game loop. Currency-foundation itself uses only `Deposit` (loot); `Transfer` exists for the immediate successors.

## Ladder formatting

`CurrencyFormatter` ([`CurrencyFormatter.cs`](../../../Core/Modules/Economy/CurrencyFormatter.cs)) converts a base-unit amount into full-word denomination text (`105 → "1 gold, 0 silver, 5 copper"`) by reading the registry's ladder. It is **presentation**, shared by `TelnetOutputFormatter` (the `score` screen) and `CurrencyAwardNarrationHandler` (the "You receive …" line) so the conversion lives in one place. Typed messages carry **raw** `CurrencyId → baseAmount` pairs; the formatter is the only place the ladder is rendered.

## Layer & invariants

- `CurrencyRegistry` is **core** (INV-2: references no domain type); `WalletSystem` is **domain**, depending only on `EntityService`.
- `WalletSystem` is pure (INV-5) — the architecture-guard suite's no-bus check covers `Economy/Systems`.
- The only caller-initiated `SaveEntityAsync` against a wallet is the `setwallet` admin boundary save (INV-22); all other deposits ride the periodic flush.

## Related

- [`economy.md`](economy.md) — holistic feature doc and the extensibility narrative.
- [`currency-loot-system.md`](currency-loot-system.md) — the first `Deposit` consumer.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — catalog rows.
- [`../character-stats/stat-system.md`](../character-stats/stat-system.md) — the `DefinitionRegistry` precedent.
