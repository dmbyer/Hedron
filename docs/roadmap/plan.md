# Roadmap

> **Purpose.** Holds the end-goal, the strategic posture, and a pointer to whatever slice is currently in flight. Detail about *completed* work lives in [`done.md`](done.md) and [`completed/`](completed/); detail about *deferred* work lives in [`backlog.md`](backlog.md). Detail about each *upcoming* slice lives in its use-case doc under [`../implementation-plans/`](../implementation-plans/).

## End goal

A production-grade C# MUD engine on .NET 8 with:

- A single live-world ECS, per-component persistence, and an event-driven 4-layer architecture (handlers → domain systems → core systems → components).
- Telnet (and eventually web) clients with the same `ISession` contract.
- Authored content driven by data files plus in-game admin commands; designers iterate without redeploys.
- A vertical-slice delivery cadence where each gameplay scenario in [`../implementation-plans/`](../implementation-plans/) ships behind a use-case spec, an architecture review, and content tooling sufficient to author and exercise the feature.

## Posture

We are **salvaging, not migrating**. The [`../architecture/`](../architecture/) target is authoritative. Existing legacy trees outside the keep list are reference material for *intent* only; their implementations do not survive. Build red is acceptable between named assembly points up to and including the MVP gate; once Phase 3 starts, every merged branch leaves the build green.

The target is defined by:

- [`../architecture/00-overview.md`](../architecture/00-overview.md) through [`../architecture/05-configuration.md`](../architecture/05-configuration.md) — 4-layer model, ECS, events, pitfalls, configuration strategy
- [`../reference/`](../reference/) — catalogs of components, systems, handlers, archetypes
- [`../implementation-plans/`](../implementation-plans/) — designer scenarios, one per gameplay slice

## Phase summary

| Phase | Status | Pointer |
|---|---|---|
| **1 — Strip** | ✅ complete | [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **2 — Foundation / MVP** | ✅ complete | [`completed/phase-2-mvp.md`](completed/phase-2-mvp.md) |
| **3 — Vertical slices** | 🟡 in progress (slices 1–11-d + output-batching + area-model + content-tooling + wearable-equipment-expansion + content-editor-integrity + currency-foundation + item-value (12a) + mob-protection (12b) + shopping (12c) + `prog-1` progression substrate + `prog-2` Ascension + `prog-3` power model & balance inspector + `prog-3b` power-model revision (Tier × Band) done; **now: Progression & Balance program — `prog-4` simulation harness, then `prog-5` agentic/balance-doc layer need `/new-plan`**) | per-slice docs in [`../implementation-plans/`](../implementation-plans/); see [Slice queue](#slice-queue) |
| **4 — Hardening** | 🟡 testing complete (`Hedron.Tests` + 566 tests + CI live; Wave 2 backfill done); remaining: perf, thread-safety | CI green; see [`backlog.md`](backlog.md); testing strategy in [`../architecture/07-testing.md`](../architecture/07-testing.md) |

For the per-slice ledger of completed work, read [`done.md`](done.md).

## Current focus

**Active body of work: the Progression & Balance program** — a foundational 5-slice program (experience-driven progression, gameplay-model Spine E, plus balance-observability tooling) landing *ahead of* the skill/item/mob content expansion. The whole-program architecture — seams, the 5-slice map, resolved decisions, open questions — is the program brief [`../implementation-plans/progression-and-balance.md`](../implementation-plans/progression-and-balance.md) (the seed the planner extends per slice).

- **Shipped — `prog-1` Progression substrate.** `ProgressionComponent` + `IProgressionSystem` (use-driven award/improve, anti-grind scale off raw attributes — a DI-cycle fix discovered during implementation, see the completed record) + `ProgressionEffectContributor` (a third `IEffectContributor` registrant, zero interface change to `IStatSystem`) + `ExperienceAwardHandler` (third independent `MobDiedEvent` subscriber) + `progress` inspector. See [`../features/progression/progression.md`](../features/progression/progression.md) and [`completed/progression-substrate.md`](completed/progression-substrate.md).
- **Shipped — `prog-2` Ascension (character-wide tier).** `AscensionComponent` (`Tier` scalar 0–6 + unlock-record state) + `IAscensionSystem` (`GetTier`/`CanAscend`/`TryAscend`/`GetGrantedUnlocks`, reads only raw component state — never `IStatSystem`, a second confirmed instance of the DI-cycle guard) + `AscensionEffectContributor` (a fourth `IEffectContributor` registrant — additive baseline, no XP reset, layers on top of progression power) + admin `ascend` command (`CanAscend`→`TryAscend`→one boundary save→`AscendedEvent`+`PlayerAscendedByAdminEvent`) + `AscensionNarrationHandler`/`AdminAuditHandler` fan-out + mobs-only `TierBand` content tag (`setmob band`, YAML, Blazor `MobEditor`) — mechanical threat is emergent from the baseline, no separate multiplier. Unlock content and the grant-execution seam are deferred (empty unlock table ships now); the real player-facing Ascension-Objective gate is deferred (`IObjectiveSystem` unbuilt). See [`../features/progression/progression.md`](../features/progression/progression.md), [`../features/progression/ascension-system.md`](../features/progression/ascension-system.md), and [`completed/ascension.md`](completed/ascension.md).
- **Shipped — `prog-3` Power model + balance inspector.** Core-tier `IPowerBudgetSystem`/`PowerBudgetSystem` (`Core/Systems/`, zero constructor dependencies — snapshot input, weighted `ScoreId` table, tier bands derived from a reference base build) serving three live consumers (INV-19): admin-gated `power`/`powerband` inspectors, the Blazor `ItemEditor`/`MobEditor` computed power/band readout (the primary designer observability surface), and the `ProgressionSystem` anti-grind-proxy rewire (raw-attribute snapshot, DI-cycle guard preserved, ratio unchanged). Also ships an authored item tier-band tag (`ItemDataComponent.TierBand`, `setitem band`, YAML round-trip) mirroring the mob band tag. Code review caught the plan's own design importing the domain `Ascension` module's constants into the core-tier oracle (INV-2); fixed by mirroring them onto `PowerBudgetConstants` and broadening the architecture guard to catch any `Core/Modules/<Feature>/` import generically — see [`completed/power-budget-inspector.md`](completed/power-budget-inspector.md). See [`../features/progression/progression.md`](../features/progression/progression.md).
- **Shipped — `prog-3b` Power-model revision.** A post-merge design conversation found `prog-3`'s one-axis Band model undershot the actual requirement: a two-axis **Tier (0–6, mechanical, coarse gate) × Band (1–3, purely descriptive, within each tier)** system, D&D-Challenge-Rating-style. `Classify(power)` now returns `PowerBand(Tier, Band)` (~21 anchors, partition within a tier, overlap retained only at tier boundaries); a new inverse `TargetRange(tier, band)` query; the authored `TierBand` tag split into a `Tier`+`Band` pair on both item and mob content with full authoring parity (`setitem`/`setmob` `tier`/`band`), a clean break with no migration (legacy `band:` in `[1,3]` reinterpreted, `[4,6]` untagged); a new shared item/mob power-projection seam (`IItemPowerProjectionSystem`/`IMobPowerProjectionSystem`) replacing three hand-rolled inline snapshot builds; a new `IBalanceAuditSystem` bulk band-drift sweep feeding a Blazor Integrity report (soft, advisory, never a build/CI gate); recalibrated `PowerBudgetConstants` (real headroom, `BandSpan < tierSpan/BandsPerTier`); and a new `docs/design/power-model.md` naming the oracle's snapshot-only extensibility principle, folded into the `add-domain-system`/`add-core-system`/`architecture-advisor` tooling (the OQ12 pull-forward from `prog-5`). `Estimate`'s algorithm, the three-consumer framework, and the anti-grind rewire hold unchanged. See [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md), [`../design/power-model.md`](../design/power-model.md), and [`completed/power-model-revision.md`](completed/power-model-revision.md).
- **Next — `prog-4` Balance simulator & workbench (expanded sub-program).** An advisor intake (2026-07-13) expanded the original single "Simulation harness" slice into the five-sub-slice [`balance-simulator`](../implementation-plans/balance-simulator.md) program: `sim-1` balance-standards registry (per-(Tier, Band) reference builds, target ranges, and outcome tolerances promoted to editable data; oracle tunables become injected plain data), `sim-2` simulation engine core (`Core/Modules/Simulation/`, sandbox worlds, `simulate` run-mode, promoted CI invariants — supersedes the dedicated-`Hedron.Sim`-project decision), `sim-3` Blazor editor integration, `sim-4` progression-rate scenarios, `sim-5` template conformance tooling. Frame each sub-slice with `/new-plan` against that seed, starting with `sim-1`. `prog-5` (agentic + balance-doc layer) follows the whole program.

The next *numbered* slice, **13 — Crafting / potions** (content depth on items + inventory), follows the progression foundation; it still has no use-case doc (frame with `/advise`).

Most recently shipped: **slice 12c — Shopping** — the trade half of the economy feature and the first `IWalletSystem.Transfer` consumer. Players `buy`/`sell`/`list` against shopkeeper mobs that carry a `ShopComponent` (accepted currency, till seed, authored base-stock rows) and a real `WalletComponent` till; buying/selling are atomic `Transfer`s between the till and the player's wallet, priced compute-on-read from each item's `ItemDataComponent.Value` (12a) × a global `ShopOptions` ratio. Stock is real item entities in the shopkeeper's `InventoryComponent`, tagged per-item with `ShopStockComponent { Provenance Base|Acquired, ExpiresAt? }`; a heartbeat restock sweep tops base stock back to authored levels and an expiry sweep clears the buy-back shelf (both deterministic via `IClock`/tick gating, INV-26). `IItemSystem.MoveBetweenInventories` is the new shared inventory→inventory seam; `MobInRoomResolver` moved to `Core/Modules/Mobs/Resolvers/`; authored via `IMobBuilderSystem.SetMobShop` + `setmob shop` + Blazor `MobEditor` + YAML. Shipped two cross-cutting fixes alongside: the world-content loader now excludes persisted (player-owned) instances from its blueprint-dedup map so a picked-up authored item no longer shadows its world re-spawn (INV-21), and `reload` was reconciled to a full world rebuild (force-save → tear down world content → re-spawn from YAML → re-publish `WorldContentReadyEvent`) so runtime instance state resets like a restart without dropping players. See [`../roadmap/completed/shopping.md`](completed/shopping.md) and the [economy feature](../features/economy/economy.md) (`shop-system.md`). Before it: **mob-protection (12b)** — any entity can be made invulnerable along two independent axes via the cross-cutting, non-`[Persistent]` `ProtectionComponent` (`[Flags] ProtectionFlags { None, Untargetable, EffectImmune }`). The attack gate is the shared `ICombatSystem.CanBeAttacked` query (consumed by `KillCommand` + `AbilityInvocationPipeline`, refusing before any state change or `CombatStartedEvent`); the effect gate is a new structured `EffectApplyResult` on `IEffectSystem.Apply` that rejects beneficial **and** harmful effects when `EffectImmune` (no `EffectAppliedEvent`). Authored via `IMobBuilderSystem.SetMobProtection` dual-write, `setmob protection`, the Blazor `MobEditor`, and a `MobTemplate.Protection` YAML round-trip (opt-in, mirroring `CurrencyLoot`). The cross-cutting invulnerability primitive Shopping (12c) consumes to protect safe-area shopkeepers; category-granular immunity deferred to [`backlog.md`](backlog.md). See [`../roadmap/completed/mob-protection.md`](completed/mob-protection.md) and the [mobs feature](../features/mobs/mobs.md#protection-invulnerability--immunity). Before it: **item-value (12a)** — every item carries an intrinsic `ItemDataComponent.Value` (non-negative base-unit Coin `long`, default 0 = valueless), the derive-only price substrate (`setitem value`, dual-write, no consumer wired) — see [`../roadmap/completed/item-value.md`](completed/item-value.md) and the [items feature](../features/items/items.md). Before it: **currency-foundation** — the economy substrate slice 12 builds on (entity-keyed `WalletComponent` ledger + `IWalletSystem` incl. atomic `Transfer`; `CurrencyRegistry` Spine F denomination ladders; opt-in mob `CurrencyLootComponent`; `score`/`setwallet`) — see [`../roadmap/completed/currency-foundation.md`](completed/currency-foundation.md) and the [economy feature](../features/economy/economy.md). Before it, **content-editor-integrity** (offline Blazor editor area filters, referential-integrity delete, Integrity sweep — see [`completed/content-editor-integrity.md`](completed/content-editor-integrity.md)) and **wearable-equipment-expansion** (worn-gear stat seam via `EquipmentStatBonus` + the INV-24 `EquipmentEffectContributor` — see [`completed/wearable-equipment-expansion.md`](completed/wearable-equipment-expansion.md)). The next *numbered* slice is **13 — Crafting / potions** (content depth on items + inventory); it has no use-case doc yet — frame it with `/advise` before planning.

The per-slice spec is the single source of truth for "what is being built right now" — this file deliberately does not duplicate it.

## Phase 3 ground rules

Each slice runs this loop. There are **two** `architecture-reviewer` gates — one before code exists, one before merge. The spec gate exists because spec-level violations (a plan that directs a layer to break an invariant, or preserves a latent one) are invisible to a code-only reviewer until implementation is already built on the flaw — the failure that produced the slice-3 command-tier rework.

1. Pick the next use-case file from [`../implementation-plans/`](../implementation-plans/), or author a new one.
   - For a net-new feature or a non-trivial change, **frame it first with the `architecture-advisor` skill (`/advise`)** — an interactive principal-architect intake that locates the architectural seams, weighs the feature against existing and planned work ([gameplay-model spines](../design/gameplay-model.md), [feature-horizon](../design/feature-horizon.md), [backlog](backlog.md)), and seeds the use-case doc with a forward-looking architectural brief *before* the planner goes deep. This is the cheapest point to catch a seam-in-the-wrong-place or a missed generalization — the failure class the HP-threshold example surfaced. Skip only for a small, well-understood slice.
2. Plan via the `implementation-planner` agent — extends the advisor's seed (if present) into the component / system / handler / event list and file plan, folds the architectural brief's seam decisions into **Design notes**, and fills the use-case doc's **Cross-cutting surfaces stressed**, **Test plan / Verification** (INV-25), and **Flows introduced or modified** sections.
3. Resolve open questions with the user.
4. **Spec-review gate** — `architecture-reviewer` in **spec mode** against the use-case doc. Blocking findings are fixed *in the doc* before any code is written. The gate also checks the **Test plan** is honest given the Postconditions (a postcondition asserting player-invisible state with no test is a finding — INV-25). Re-run until the verdict is clean.
5. Implement (`implement-plan`) against the corrected spec — **including the tests named in the Test plan**; `dotnet test` must be green (INV-25). A previously-untested system this slice touches gains tests too (on-touch ratchet).
6. **Code-review gate** — `architecture-reviewer` in **code mode** against the diff, before merge. It also confirms the Test-plan tests are present and `dotnet test` is green (INV-25), and greps systems for ambient nondeterminism (INV-26).
7. **Sync roadmap** (`sync-roadmap` skill) — update [`done.md`](done.md), add `completed/<slice>.md`, and advance the slice queue in this file. Run before the PR merges.
8. Ship green — build **and** `dotnet test` green.

Both gates run against [`../architecture/checklist.md`](../architecture/checklist.md) — the single authoritative invariant list. A rule change lands there once; both gates and the planner pick it up.

The testing discipline (INV-25/26, the **Test plan** section, the `dotnet test` gate) is defined in [`../architecture/07-testing.md`](../architecture/07-testing.md). The `Hedron.Tests` harness is live; `dotnet test` is enforced on every PR via CI.

## Slice queue

Order is **revised** from the original Phase 3 list to pull content tooling forward. Rationale: shipping gameplay slices without the tools to author and exercise their content makes every following slice harder to demonstrate and regression-test. Content tooling becomes a first-class concern *now*, not at the end.

| # | Slice | Unlocks | Status |
|---|---|---|---|
| 1 | Persistence substrate | Any slice that wants state to survive restart | ✅ done |
| 2 | World content loading + admin substrate | Authored rooms/areas from data files; in-game admin command framework (`@spawn`, `@teleport`, `@dig`, `@reload`); resolves Ticket B | ✅ done |
| 3 | **Command framework** | Typed `CommandContext`, declarative arg parsing, structural privilege gate, `help`/`commands`, `CommandExecutedEvent`; ships the minimal output seam | ✅ done |
| 3a | **Command prefix matching** | Dynamic prefix resolution (`lo`→`look`), `MatchingMode` per command, `IVerbRegistry`, alias surfacing in `help`/`commands`, `IArgumentResolver` interface + parser wiring | ✅ done |
| 4 | **Output framework** | Full `IOutputMessage` catalog, `IOutputFormatter`/telnet ANSI, `SupportsColor`, formatter-backed writer, broadcast audience-filter + system-wide; discharges slice-3 output debt | ✅ done |
| 5 | Account / character creation | Real identity instead of throwaway names; first `[Persistent]` user-facing component | ✅ done |
| 5a | **Bare-bones content spawning** | Ad-hoc `dig`/`set` admin commands backed by `IRoomBuilderSystem`; `RoomComponent` `[Persistent]`; unblocks runtime content authoring for slices 6+ | ✅ done |
| 5b | **Persistence two-level model** | `PersistentEntity` marker component; area-scoped periodic flush; save-on-change for admin/lifecycle transitions; `PersistenceHandler` deleted; dirty-set model removed | ✅ done |
| 6 | Items + inventory + `get`/`drop`/`look <item>` | Object interaction and inspection; `ItemDataComponent`, `InventoryComponent`; admin `mkitem`/`setitem`; concrete `IArgumentResolver` impls | ✅ done |
| 7 | Equipment + `wear`/`remove` | Gear; `EquipmentComponent`, `WornSlot` enum, `wear`/`remove`/`equipment` commands | ✅ done |
| 8 | Mobs (basic entity model and spawn) | Populated world; no wandering | ✅ done |
| 8a | Attributes and vitals (`AttributesComponent`, `PoolsComponent`, `score`) | HP + base stats required for combat | ✅ done |
| 9-a | Entity state management | Centralized entity state flags; command gating; prereq for combat and future states (resting, incapacitation, …) | ✅ done |
| 9-b | Time system (heartbeat) | `IHeartbeatService`, `HeartbeatTickEvent`; prereq for combat, mob AI, effect expiry | ✅ done |
| 9-c | Stat computation system | `IStatSystem` effective-stat pipeline; base + equipment bonus seam for future effects/buffs | ✅ done |
| 9 | Combat | Core gameplay loop | ✅ done |
| 9-d | **Stat & resource substrate** (gameplay-model S1) | Four attributes (Mind/Body/Spirit/Attunement), Mana/Stamina/Astra pools, `ScoreId`/`IStatRegistry` seam — substrate every later spine writes to | ✅ done |
| 9-e | **Effect substrate** (gameplay-model S2) | Effect kinds + lifetime/stacking/phase/Power + `EffectSystem`; bedrock for skills, potions, curses, auras | ✅ done |
| 10 | Death and respawn | Combat is terminal until this exists | ✅ done |
| 11-a | **Ability substrate** (gameplay-model S4) | Unified skill/spell primitive: `AbilityDefinition`/`IAbilitySystem`/`AbilitiesComponent`, multi-pool costs, cooldowns, passive effects via `IEffectContributor` (INV-24) | ✅ done |
| 11-b | **Ability invocation & combat targeting** | Dynamic skill verbs + `cast`, state-aware targeting, offensive-opens-combat, starting abilities at creation | ✅ done |
| 11-c | **Resource regeneration + `rest`** | Out-of-combat pool regen so ability costs recover; independent of 11-a/b | ✅ done |
| output-batching | **Player prompt + output batching** | Status prompt trailing every command + tick; session-scoped buffer; `IPromptSource` port; immediate flush for Chat | ✅ done |
| 11-d | **Aspect & Registry Foundation** (gameplay-model A + F) | `AspectComposition` + `IAspectSystem` (aspect-typed combat damage; affinity + independent per-aspect resistance); generic `IRegistry<TKey,TDef>` registry layer + Ability/Effect/Stat retrofit; fail-fast registry validation; `defs` inspector | ✅ done |
| area-model | **Area model + room–area membership** | Bidirectional area linking, `IAreaSystem`, aspect affinities on areas, `area`/`setarea` admin commands, `@dig` area inheritance | ✅ done |
| content-tooling | **Content-tooling platform** | Shared content-definition layer + callable `IContentValidator`; split hosted-service registration; headless `generate` bulk-generation CLI; in-process Blazor authoring editor (`Hedron.Web`) over all four content kinds. Advisor-initiated (off the numbered queue). | ✅ done |
| wearable-equip | **Wearable equipment expansion** | Worn-gear stat seam: `EquipmentStatBonus` rows + `EquipmentEffectContributor` (INV-24) folded by `IStatSystem.Get`; flat `DamageBonus` migrated onto it; `WornSlot` +9 (full suit); `setitem bonus`/`clearbonus` + YAML + Blazor row editor. Advisor-initiated (off the numbered queue). | ✅ done |
| content-editor-integrity | **Content-editor area filters, referential integrity & delete** | Catalog area-association read-model (`ContentSummary.AreaBlueprintId` + `RoomsInArea`); declared-edge `IContentReferenceIndex` (5 edges); `DeleteAsync` cascade-clear (file-only, INV-22/23); warn-but-allow `SaveAsync`; bidirectional exit linking (`Direction.Opposite`); Blazor area filters, lookup selection fields, delete, Integrity sweep page, dark theme. Advisor-initiated (off the numbered queue). | ✅ done |
| currency-foundation | **Currency foundation** | Entity-keyed `WalletComponent` ledger + `IWalletSystem` (incl. atomic `Transfer`); `CurrencyRegistry` (Spine F) with data-driven denomination ladders (Coin: 100c=10s=1g); opt-in mob `CurrencyLootComponent` (uniform `IRandom` roll) auto-awarded to the killer on `MobDiedEvent`; `score` balances + `setwallet` admin grant. Precursor to slice 12. Advisor-initiated (off the numbered queue). | ✅ done |
| 12a | **Item value** (advisor seed) | `ItemDataComponent.Value` substrate (base-unit Coin) every economic price derives from | ✅ done |
| 12b | **Mob protection** (advisor seed) | Cross-cutting `ProtectionComponent` (`Untargetable`/`EffectImmune`); combat + effect gates | ✅ done |
| 12c | **Shopping** (advisor seed) | Economy: shopkeeper till + base stock + buy-back; `buy`/`sell`/`list`; first `Transfer` consumer | ✅ done |
| prog-1 | **Progression substrate** — *Progression & Balance program* | Per-track XP · contribute-on-read into `IStatSystem` (INV-24) · use-driven accrual · `progress` inspector | ✅ done ([feature](../features/progression/progression.md) · [completed record](completed/progression-substrate.md)) |
| prog-2 | **Ascension / character tier** — *program* | Character-wide Tier scalar · additive power baseline · tier-up gate · overlapping content bands | ✅ done ([feature](../features/progression/progression.md) · [ascension-system](../features/progression/ascension-system.md) · [completed record](completed/ascension.md)) |
| prog-3 | **Power model + balance inspector** — *program* | `IPowerBudgetSystem` oracle · tier power bands · admin `power`/`powerband` + Blazor editor readout · item tier-band + anti-grind-proxy rewire | ✅ done ([feature](../features/progression/progression.md) · [completed record](completed/power-budget-inspector.md)) — **superseded by `3b`** |
| prog-3b | **Power-model revision** — *program* | Two-axis `Classify` (Tier × Band 1–3) · inverse target-range query · `TierBand` → `Tier`+`Band` pair on content · band-count-tolerance audit tooling · calibration · power-model extensibility principle | ✅ done ([feature](../features/progression/power-budget-system.md) · [completed record](completed/power-model-revision.md)) |
| prog-4 | **Balance simulator & workbench** — *program, expanded* | Sub-program [`balance-simulator.md`](../implementation-plans/balance-simulator.md) (`sim-1`–`sim-5`): balance-standards registry (data) · Core-module sim engine + `simulate` run-mode · Blazor editor integration · progression-rate scenarios · template conformance tooling · promoted CI invariants | 🟢 ready — `/new-plan` on `sim-1`, depends on `3b` (done) |
| prog-5 | **Agentic + balance-doc layer** — *program* | Balance catalog · `balance-tuning`/`run-simulation` skills · INV-20 refresh | 🔵 seed only → `/new-plan` |
| 13 | Crafting, potions | Content depth | 🟢 ready — follows the progression program |
| 14 | Web/SignalR client (deferred) | Dual-client transport | 🔵 deferred — see [`backlog.md`](backlog.md) |

Order is flexible past slice 5a; some slices can run in parallel branches, and each slice gets a use-case doc *before* implementation starts. (Historical numbering: the original combined command/output draft was split into slices 3 and 4, account creation moved to slice 5 with a +2 downstream shift, and 5a was inserted to give slices 6+ a content-authoring path.)

Slices 9-d, 9-e, and 11 onward implement the gameplay-model spines; see [`../design/gameplay-model.md`](../design/gameplay-model.md) §5 for the full S1–S9 decomposition, dependency order, and per-slice testability.

The **`prog-*` rows are the [Progression & Balance program](../implementation-plans/progression-and-balance.md)** — a foundational program (Spine E progression + balance-observability tooling), advisor-framed at the program level. **`prog-1`**, **`prog-2`**, **`prog-3`**, and **`prog-3b`** shipped (see [`features/progression/progression.md`](../features/progression/progression.md), [`features/progression/ascension-system.md`](../features/progression/ascension-system.md), [`features/progression/power-budget-system.md`](../features/progression/power-budget-system.md), [`completed/progression-substrate.md`](completed/progression-substrate.md), [`completed/ascension.md`](completed/ascension.md), [`completed/power-budget-inspector.md`](completed/power-budget-inspector.md), and [`completed/power-model-revision.md`](completed/power-model-revision.md)); a post-merge design conversation added **`prog-3b`** (a revision to `prog-3`'s Band model, not previously in the program map) which has now landed. **`prog-4`** and **`prog-5`** remain as the program map only — to build one, point `/new-plan` at the **program brief** (the seed), scoped to that slice's row + its Open Questions. Each slice then runs the normal loop (spec gate → implement → code gate → `sync-roadmap`).

## Phase 4 — Hardening

Best addressed once a handful of Phase 3 slices have stressed the architecture:

- **Testing — complete.** `Hedron.Tests` harness is live (WP-1 shared helpers, WP-2 architecture-guard suite, WP-3 `IClock` seam). Wave 1 + Wave 2 backfill shipped (566 tests green). The per-slice gate (INV-25/26) and `dotnet test` are enforced on every PR. Strategy: [`../architecture/07-testing.md`](../architecture/07-testing.md). Wave 3 drains via the on-touch ratchet.
- **CI — complete.** `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` on every PR and push to `master`.
- Performance passes where profiling shows real cost
- Thread-safety review once concurrency shape is known (see [`backlog.md`](backlog.md))

Tracked in [`backlog.md`](backlog.md) until promoted into a dated slice.

## Ground rules

Architectural invariants (layering, ECS, events, persistence, …) are the `INV` list in [`../architecture/checklist.md`](../architecture/checklist.md); CLAUDE.md carries their day-to-day summary. This roadmap does not restate them — one rule, one home (see [`../architecture/09-documentation.md`](../architecture/09-documentation.md)).

What this roadmap *owns* are the **slice-delivery obligations** — process rules the checklist enforces but explains here:

- **Content-tooling discipline (INV-18).** Every slice that adds gameplay state ships the tooling to author and exercise it in the same PR:
   - the use-case doc's **Content tooling impact** section lists the data-file shape, admin commands, and/or `TemplateRegistry` entries introduced or extended;
   - no gameplay slice merges without a way to populate and inspect the state it adds;
   - if a slice needs content with no authoring tool yet, the prerequisite tooling is split out as its own earlier slice.
- **Infrastructure-discipline parity (INV-19).** A new player-facing surface, or a hand-rolled pattern repeated ≥3×, lands its framework in the same or an adjacent slice; the use-case **Cross-cutting surfaces stressed** section is the structural check.

The per-slice delivery loop is the [Phase 3 ground rules](#phase-3-ground-rules) above.

## Resolved tickets

- **Ticket A — ECS redesign.** Resolved in Phase 1.5 (see [`completed/phase-1-strip.md`](completed/phase-1-strip.md)).
- **Ticket B — admin tooling / use-cases / skills scope.** Resolved 2026-05. **Disposition: in-game admin commands first; web/desktop UI deferred.** Admin authoring lands as part of Phase 3 slice 2 (`@spawn`, `@teleport`, `@dig`, `@reload`, etc.) rather than as a Phase 3 slice 13 web UI. Rationale: the smallest tool that lets a designer iterate on content is a privileged set of telnet commands; a web/desktop editor is a transport choice that can be layered on once SignalR / dual-client work lands. The `editor-*` use cases (deletion of areas/mobs, etc.) become admin commands authored against the same handler pipeline as player commands.

## Where to look

- **What's done?** → [`done.md`](done.md), then [`completed/`](completed/) for detail
- **What's next?** → "Current focus" above, then the linked use-case doc
- **What's deferred?** → [`backlog.md`](backlog.md)
- **What's the target architecture?** → [`../architecture/`](../architecture/)
- **How do I plan/implement/review a slice?** → `architecture-advisor`, `new-plan`, `implementation-planner`, `implement-plan`, `architecture-reviewer` under [`../../.claude/`](../../.claude/)
