# Currency loot system

> Design doc for opt-in mob currency loot: the `CurrencyLootComponent` spec, the `IRandom`-backed roll, and the auto-award + narration handlers on `MobDiedEvent`. Holistic view: [`economy.md`](economy.md).

## The loot spec — `CurrencyLootComponent`

`CurrencyLootComponent` ([`CurrencyLootComponent.cs`](../../../Core/Modules/Economy/Components/CurrencyLootComponent.cs)) is a pure-data (INV-3) component holding a per-`CurrencyId` `(min, max)` range in **base-unit copper**. It is a *loot specification*, not a wallet — the mob carries no balance; an amount is rolled at death.

**Opt-in by default.** A mob gets a `CurrencyLootComponent` only when `MobTemplate.Apply` finds a non-zero range (zero/absent ⇒ no component ⇒ no drop). This is the "mobs do not drop loot by default" rule — loot must be assigned through the editor/YAML (or future random generation).

**Not persistent (INV-23).** The component is world content — its durable form is the `MobTemplate` YAML, fresh-spawned on startup/reload. It is **never** `[Persistent]`; mobs carry no SQLite row. A round-trip test asserts the component is absent after a save/load cycle.

## The roll — `ICurrencyLootSystem`

`ICurrencyLootSystem.RollLoot(mobEntityId)` ([`ICurrencyLootSystem.cs`](../../../Core/Modules/Economy/Systems/ICurrencyLootSystem.cs) · [`CurrencyLootSystem.cs`](../../../Core/Modules/Economy/Systems/CurrencyLootSystem.cs)) reads the mob's `CurrencyLootComponent` and, for each configured currency, rolls a **uniform inclusive** `[min, max]` value, returning a `CurrencyLootResult` ([`CurrencyLootResult.cs`](../../../Core/Modules/Economy/Systems/CurrencyLootResult.cs)) of non-zero `CurrencyId → baseAmount` awards. Absent component ⇒ empty result.

**Determinism (INV-26).** The roll draws from the injected `IRandom`, never `Random.Shared`. Because `IRandom.Next(min, max)` is **max-exclusive**, the inclusive bound is `Next(min, max + 1)`. Under a fixed `FakeRandom` the result is exactly reproducible — the no-ambient-nondeterminism guard covers `Economy/Systems`.

It is a **domain system**: it returns results and holds the roll *mechanic*; the *consequences* (deposit, publish, narrate) belong to the handler.

## The auto-award path

The headline runtime path is the [Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md):

1. `CombatMobDeathHandler` publishes `MobDiedEvent { MobEntityId, BlueprintId, KillerEntityId }` while the mob is **still live**, then destroys it. Because it `await`s `PublishAsync` before `DestroyEntity`, every `MobDiedEvent` subscriber reads the live mob.
2. **`CurrencyLootHandler`** ([`CurrencyLootHandler.cs`](../../../Core/Modules/Economy/Handlers/CurrencyLootHandler.cs)) — priority 20 (`HandlerPriority.Domain`). If `KillerEntityId == 0` (no attributable killer) it discards — no deposit, no event. Otherwise it calls `RollLoot`, and for each `(currency, amount)` calls `IWalletSystem.Deposit(KillerEntityId, …)` then publishes `CurrencyAwardedEvent(KillerEntityId, currency, amount)` **per currency**. It holds **no game rule** — the roll is in the system, the mutation in the wallet system (INV-8).
3. **`CurrencyAwardNarrationHandler`** ([`CurrencyAwardNarrationHandler.cs`](../../../Core/Modules/Economy/Handlers/CurrencyAwardNarrationHandler.cs)) — priority 80 (`HandlerPriority.Notification`), on `CurrencyAwardedEvent`. Writes a "You receive …" line (formatted up the ladder via the shared `CurrencyFormatter`/`ICurrencyRegistry`) to the recipient; no-ops if the recipient has no `LocationComponent`.

The deposit rides the periodic persistence flush (the killer carries `PersistentEntity`) — no boundary save in the loot path (INV-22).

**Subscription site.** Both handlers are DI-registered via `AddEconomyModule` and **subscribed in the central `Server/Program.cs` event-handler wiring** — `bus.Subscribe<MobDiedEvent>(currencyLootHandler)` (20) and `bus.Subscribe<CurrencyAwardedEvent>(currencyAwardNarrationHandler)` (80). `CurrencyLootHandler` is a **second** `MobDiedEvent` subscriber alongside `SpawnSystem`; the two are **independent reads** of the live mob (loot spec vs. slot vacancy) with no inter-handler ordering constraint.

## Forward generalization

`CurrencyLootComponent` is a deliberately **narrow instance** of the general **Mob loadouts & loot tables** feature (gameplay-model Spine D — weighted item + currency drops, rarity scaling, corpse/pile pickup). The seam is shaped to keep that **additive, not a rewrite**: a future general `LootHandler` slots onto the same `MobDiedEvent` and deposits via the same `IWalletSystem`, with currency loot becoming one contributor. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Related

- [`economy.md`](economy.md) · [`wallet-system.md`](wallet-system.md) — the wallet seam this deposits through.
- [`../../architecture/flows/flow-20-mob-death-respawn.md`](../../architecture/flows/flow-20-mob-death-respawn.md) — the runtime journey.
- [`../mobs/mob-system.md`](../mobs/mob-system.md) — `MobTemplate` + the YAML/editor authoring path the loot range extends.
- [`../../reference/handlers.md`](../../reference/handlers.md) · [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — catalog rows.
