# Phase 3 — Currency foundation (completed)

> Implemented on branch `claude/practical-jang-73289e`, 2026-06-23. Living docs: [`features/economy/economy.md`](../../features/economy/economy.md). Advisor-initiated (off the numbered queue); precursor to slice 12 — Shopping.

## Outcome

Established the currency substrate. Entities now carry an entity-keyed `WalletComponent` ledger; a `CurrencyRegistry` (Spine F) defines currency families and their denomination ladders as data (launch family **Coin**: copper = 1, silver = 10, gold = 100). `IWalletSystem` is the single wallet-mutation seam (`Deposit`/`TryWithdraw`/`CanAfford`/`Transfer`/`SetBalance`), pure per INV-5. Mobs carry an **opt-in** `CurrencyLootComponent` range (authored via the Blazor `MobEditor` + YAML); on death `CurrencyLootHandler` rolls it (uniform, via `IRandom`) and auto-awards to the killer, narrated up the denomination ladder. `score` shows player balances; `setwallet` is the admin grant (boundary save + audit). The `Transfer` primitive is exposed now for the downstream economy slices (shopping/banking/trade) even though this slice only deposits.

## Behavior digest

*As-specified snapshot; authoritative present truth is in [`features/economy/`](../../features/economy/economy.md) + [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md).*

- **Wallet ledger** — `Deposit` adds exactly `n` (creates `WalletComponent` on first deposit; rejects negative); balances are non-negative base-unit `long`. `TryWithdraw`/`CanAfford` atomic & non-mutating on failure.
- **Transfer atomicity** — debits `from` + credits `to` iff affordable; neither mutated on insufficient funds (no partial transfer); self-transfer is a no-op returning `true`.
- **Loot roll** — `RollLoot(mob)` returns a uniform `[min, max]`-inclusive value per configured currency via injected `IRandom`; zero/absent range ⇒ no entry.
- **Auto-award** — on `MobDiedEvent` with `KillerEntityId != 0`, each rolled currency is deposited to the killer and one `CurrencyAwardedEvent` published per currency; `KillerEntityId == 0` ⇒ discard (no deposit, no event).
- **Score** — renders each non-empty balance up the ladder (full words); `ScoreDisplayMessage` carries raw `CurrencyId → baseAmount` pairs.
- **Admin set** — `setwallet <player> <currency> <amount>` absolute-sets via `IWalletSystem`, performs exactly one `SaveEntityAsync(target)`, publishes one `WalletSetByAdminEvent`; non-privileged rejected with no mutation.
- **Persistence** — `WalletComponent` round-trips (key by enum **name**); `CurrencyLootComponent` never persisted (mobs carry no SQLite row).

Main flow: **A** mob death → `CurrencyLootHandler` (p20) rolls + deposits + publishes → `CurrencyAwardNarrationHandler` (p80) narrates. **B** `score` reads `GetBalances` → `ScoreDisplayMessage` → `TelnetOutputFormatter` ladder render. **C** `setwallet` → admin gate → `SetBalance` → one save → `WalletSetByAdminEvent`.

## Shipped pieces

| Surface | Location |
|---|---|
| `CurrencyId` enum (`Coin`) | `Core/Modules/Economy/CurrencyId.cs` |
| `CurrencyDefinition` + `Denomination` (ladder, ctor-validated) | `Core/Modules/Economy/CurrencyDefinition.cs` |
| `ICurrencyRegistry` / `CurrencyRegistry` (core, `DefinitionRegistry`) | `Core/Modules/Economy/CurrencyRegistry.cs` |
| `WalletComponent` (`[Persistent]`, enum-name keys) | `Core/Modules/Economy/Components/WalletComponent.cs` |
| `IWalletSystem` / `WalletSystem` (domain; Get/Deposit/TryWithdraw/CanAfford/Transfer/SetBalance) | `Core/Modules/Economy/Systems/{IWalletSystem,WalletSystem}.cs` |
| `CurrencyLootComponent` (**not** `[Persistent]`) | `Core/Modules/Economy/Components/CurrencyLootComponent.cs` |
| `ICurrencyLootSystem` / `CurrencyLootSystem` + `CurrencyLootResult` (`IRandom` roll) | `Core/Modules/Economy/Systems/{ICurrencyLootSystem,CurrencyLootSystem,CurrencyLootResult}.cs` |
| `CurrencyFormatter` (shared ladder formatting) | `Core/Modules/Economy/CurrencyFormatter.cs` |
| `CurrencyAwardedEvent`, `WalletSetByAdminEvent` | `Core/Modules/Economy/Events/{CurrencyAwardedEvent,WalletSetByAdminEvent}.cs` |
| `CurrencyLootHandler` (p20, `MobDiedEvent`), `CurrencyAwardNarrationHandler` (p80) | `Core/Modules/Economy/Handlers/{CurrencyLootHandler,CurrencyAwardNarrationHandler}.cs` |
| `SetwalletCommand` (admin, absolute-set + boundary save + audit) | `Core/Modules/Economy/Commands/SetwalletCommand.cs` |
| `EconomyModule.AddEconomyModule` (DI) | `Core/Modules/Economy/EconomyModule.cs` |
| `ScoreCommand` + `ScoreDisplayMessage` extended (wallet balances) | `Core/Modules/Attributes/Commands/ScoreCommand.cs`, `Core/Output/ScoreDisplayMessage.cs` |
| `TelnetOutputFormatter` ladder rendering (injects `ICurrencyRegistry`) | `Core/Output/TelnetOutputFormatter.cs` |
| `AdminAuditHandler` += `WalletSetByAdminEvent` | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| `MobTemplate` loot range + conditional `Apply`; YAML writer + deserializer | `Core/Modules/Mobs/Templates/MobTemplate.cs`, `Systems/MobContentWriter.cs`, `MobTemplateDeserializer.cs` |
| Blazor `MobEditor` currency-loot field-row | `Hedron.Web/Components/Pages/MobEditor.razor` |
| DI + subscriptions | `Server/CompositionRoot.cs` (`AddEconomyModule`), `Server/Program.cs` (2 handler subs + audit sub) |
| Flow 20 updated (loot step + participants) | `docs/architecture/flows/flow-20-mob-death-respawn.md` |
| Reference catalogs + events doc | `docs/reference/{components,systems,handlers,commands}.md`, `docs/architecture/03-events.md` |
| Skill drift fixed (subscription site) | `.claude/skills/{add-handler,add-event}/SKILL.md` |
| Deferred general loot table | `docs/roadmap/backlog.md` |

## Tests shipped

All in `Hedron.Tests/Modules/Economy/`; **776 total** (from 687 before this slice: WP-1 → 732, WP-2 → 751, WP-3 → 776). `dotnet build` 0/0; `dotnet test` green.

| Test file | Tier | Asserts |
|---|---|---|
| `WalletSystemTests` | system-unit | Deposit (+n, create-on-first, negative rejected), TryWithdraw/CanAfford atomicity, Transfer (atomic, no partial, self-transfer no-op), SetBalance, INV-5 no-bus guard |
| `CurrencyRegistryTests` | system-unit | ctor throws on non-ascending ladder / base unit ≠ 1; happy-path construction |
| `CurrencyFormatterTests` | system-unit | base units → full-word ladder string (multi-denomination + zero cases) |
| `CurrencyLootSystemTests` | system-unit | determinism under `FakeRandom`; inclusive `[min,max]`; zero/absent range ⇒ no entry; INV-5 guard |
| `CurrencyLootHandlerTests` | handler | per-currency deposit + one event each; `KillerEntityId==0` discard; wallet created on award |
| `SetwalletCommandTests` | handler | absolute-set via system; **exactly one** `SaveEntityAsync`; one `WalletSetByAdminEvent`; non-privileged rejection (no mutation) |
| `CurrencyLootFlowTests` | flow | kill → loot → killer balance increased by seeded roll + `CurrencyAwardedEvent` fired |
| `ScoreWalletFlowTests` | flow | `score` with balances → `ScoreDisplayMessage` carries raw balance pairs |
| `WalletComponentRoundTripTests` | persistence | save→load equal balances; key serialized as `"Coin"` not `"0"` |
| `MobCurrencyLootRoundTripTests` | persistence | `CurrencyLootComponent` never serialized; YAML loot-range round-trips |
| `CurrencyLootComponentPersistenceTests` | persistence | mob carrying loot has no SQLite row |
| `EconomyModuleDiTests` | architecture-guard | `AddEconomyModule` resolves the registry + systems |

On-touch ratchet: none — `Economy` is a net-new module; touched files (`ScoreCommand`, `MobTemplate`, `AdminAuditHandler`, `TelnetOutputFormatter`) already had coverage that continued to pass.

## Decisions

- **Currency is a stored ledger, not a `ScoreId`/stat.** Directly mutated and persisted, no base+modifier compute, no max — routing it through `IStatSystem` would be the wrong-substrate trap. It is co-located with stats on the `score` screen for display only.
- **Wallet, not coins-as-items.** A base-unit `Dictionary<CurrencyId,long>` makes affordability/change-making trivial and sidesteps the unbuilt item-stacking feature.
- **Denomination ladder vs. currency family — two axes, one registry.** Copper/silver/gold are denominations of one **Coin** family stored as a single copper integer; a new family (no cross-family conversion) is a new `CurrencyId` + registry row. Both axes are pure data. (Owner decision.)
- **The wallet is entity-keyed — the extensibility lever.** `IWalletSystem` operates on any wallet-bearing entity, so banks/safes/vaults/vendor-tills are new *holders*, not new mechanisms; every interaction is an atomic `Transfer` between two wallets, with authorization in the command/handler (INV-8), never the system. `Transfer` is exposed now (≥4 downstream consumers, INV-19) though this slice only deposits. *Where* a bank balance lives is a deferred, non-foreclosed content decision.
- **Loot is an opt-in spec, not a wallet on the mob.** `CurrencyLootComponent` holds a range rolled at death; absent/zero ⇒ no drop (mobs don't drop by default). World content ⇒ not `[Persistent]` (INV-23).
- **Auto-award to the killer; no corpse/pile.** Corpse looting is unbuilt, so the rolled amount deposits straight to the killer's wallet; no attributable killer ⇒ discarded. (Owner decision.)
- **No generic `CurrencyChangedEvent`.** Each call site publishes its own contextual past-tense event (loot → `CurrencyAwardedEvent`; shopping will publish its own) — INV-5 keeps systems off the bus.
- **Enum-key persistence by name.** Guards saved wallets against future `CurrencyId` reordering; asserted in the round-trip test.
- **`IRandom.Next` is max-exclusive** → inclusive roll uses `Next(min, max + 1)`.
- **Resolved open questions:** `setwallet` absolute-set; editor authors/stores base-unit copper; score format full words; loot distribution uniform inclusive.

## Spec-review provenance

- **Spec gate (spec-mode):** APPROVE WITH NITS — no blocking. Four tightenings folded into the plan before code (DI home = `CompositionRoot.Register`; named handler subscription sites; enum-name serialization; `Next(min,max+1)`), plus the INV-20 skill-drift fix.
- **Code gate (code-mode):** APPROVE — merge. No blocking findings. Two cosmetic doc-prose nits (stale "v1 copper" strings in `handlers.md` + flow-20) fixed post-review. One advisory (`Deposit` result discarded before publish) judged acceptable — `RollLoot` only returns positive amounts and the loop filters `≤0`.

## Deviations / Follow-ups

- **Deviations from the plan:** none — shipped per spec; all postconditions met. WP-3's interrupted run had already written all code + tests; verified green on resume.
- **Skill drift reconciled (INV-20):** `add-handler`/`add-event` skills corrected to document the `Server/Program.cs` central subscription site (DI-register the handler type in `AddXModule`, subscribe in `Program.cs`), matching the as-built wiring.
- **Follow-ups unlocked:**
  - **Slice 12 — Shopping:** the first `Transfer` consumer (vendor till ↔ player wallet); `MobType.Vendor` gets a wallet, buy/sell publish their own events.
  - **Banks / safes / vaults / player trade / mail-COD:** all `Transfer` consumers over the entity-keyed wallet; the holder-location decision (account-wide / per-character / per-location) is open content.
  - **General mob loot table (Spine D):** deferred to [`../backlog.md`](../backlog.md) — weighted item+currency drops, rarity scaling, corpse/pile pickup; the `CurrencyLootHandler` seam is shaped so a general `LootHandler` is additive.
