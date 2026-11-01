# Combat effective scores — Phase 5 slice 5b (completed)

> Implemented on branch `claude/refine-slice-5b-combat-scores-7699ed`, 2026-11-01. Living docs: [`character-stats`](../../features/character-stats/character-stats.md) ([`stat-system`](../../features/character-stats/stat-system.md), [`attribute-system`](../../features/character-stats/attribute-system.md)), [`combat-system`](../../features/combat/combat-system.md), [`death-system`](../../features/combat/death-system.md), [`simulation-engine`](../../features/simulation/simulation-engine.md), [flow-16](../../architecture/flows/flow-16-heartbeat-tick.md) (pool reconcile), [`balance.md`](../../design/balance.md) (Known gaps). Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (gap slice, R30; serves R21, R26 A3/A7).

## Outcome

Getting stronger now wins fights. Before this slice, progression improvements, passive abilities, buffs and the Ascension tier baseline raised a character's displayed and measured power but not their combat outcomes — only worn gear did. Combat now reads **effective** scores: melee attack power, defense and the hit roll derive from effective `Body`, and every pool is capped at its **effective** max everywhere current is written, filled, restored or read. A lowered max (a buff expiring, gear coming off) is visible at once and written back to storage within one heartbeat. It is a mechanism fix and a deliberate balance change: Tier-0 play is unchanged for a fresh character, a progressed character now beats mobs it used to lose to, and the tier step now dominates fights (its tuning is Phase 6).

## Behavior digest

- **Requirements (as confirmed, R1–R14).** Effective `Body` drives melee attack power, defense and the hit roll (R1–R3). One rule for all four pools: the effective max (base + `<Pool>Max` modifiers, floored at 1) caps every write, regen/heal fills into it, respawn restores a % of it, spawn starts at it (R4–R7). When the max drops below current, current clamps down at once, never lethally (R8). What the player sees matches what combat uses — prompt and `score` (which gained `Attack`/`Defense` lines) show effective values (R9). Current above the raw max survives logout when the effective cap still covers it (R10). Deliberate balance change: sweep before/after, re-pin, tier side effect accepted without tuning (R11–R12). Progressed beats fresh at a pinned seed; `consider`'s SR7 midpoints hold (R13). Doc accuracy (R14).
- **Postconditions** (the coverage contract): derived-score formula with a floor at 0; hit roll on `Get(Body)`; typed getters equal `Get`; `GetEffectiveMax` floored at 1 (0 when base is 0) and equal to `Get(<Pool>Max)`; clamped `GetCurrent` (negative HP unchanged); `SetCurrent` clamps to `[floor, cap]`; `SetMax` writes the base and clamps to the effective cap (incl. `setmob` live instances); `RestoreToFraction`; `ReconcilePools`/`ReconcileAllPools` never raise; a p=30 heartbeat reconcile that publishes nothing; sandbox reconcile with no `IRandom` draws; respawn after `RemoveImpermanent`; sandbox/creation fill at the cap; live mob spawn equal to the cap by construction; round-trip of current above raw max; guards (no `IStatSystem` edge into `AttributeSystem`, no `IStatSystem` setters, reconcile subscribed); R13a; SR7/`power` goldens unchanged; re-pins recorded; `score` Attack/Defense.
- **Main flow.** A combat round reads `Get(attacker, Body)` for the hit roll, `Get(defender, Defense)` for the threshold and `Get(attacker, AttackPower)` for damage; damage writes through `SetCurrentHp`, clamped to the defender's effective cap. In the same tick a timed `+HpMax` buff can expire in `EffectTickHandler` (p=20) — readers immediately see `min(stored, cap)` — and `PoolReconcileTickHandler` (p=30) writes the clamp back. On death, `DeathSystem.Respawn` strips impermanent effects, then `RestoreToFraction(pool, 0.25)` against the effective cap. New characters fill to the cap after learning starting abilities; sandbox combatants fill after tier/residuals/kit; the sandbox tick ends with a reconcile.

## Shipped pieces

| Surface | Location |
|---|---|
| `StatSystem`: typed getters wrap `Get`; `AttackPower`/`Defense` = `max(0, Get(Body)/2|/4 + modifiers)`; pool reads delegate to the cap seam | `Core/Modules/Stats/Systems/{IStatSystem,StatSystem}.cs` |
| `IAttributeSystem` pool-cap seam keyed by `ResourceType`: `GetEffectiveMax`, `GetCurrent`, `SetCurrent`, `SetMax`, `RestoreToFraction`, `ReconcilePools`, `ReconcileAllPools`; typed setters are wrappers; `GetMaxX` → `GetBaseMaxX`; typed `GetCurrentX` are clamped reads | `Core/Modules/Attributes/Systems/{IAttributeSystem,AttributeSystem}.cs` |
| `PoolReconcileTickHandler` (`HeartbeatTickEvent`, `HandlerPriority.Domain + 10`) + DI registration + `Server/Program.cs` subscription | `Core/Modules/Attributes/Handlers/PoolReconcileTickHandler.cs`, `AttributesModule.cs`, `Server/Program.cs` |
| `CombatSystem.ExecuteRound` hit roll on `Get(Body)` | `Core/Modules/Combat/Systems/CombatSystem.cs` |
| `DeathSystem.Respawn` restores via `RestoreToFraction`; `DeathOptions` doc | `Core/Modules/Death/` |
| `MobBuilderSystem` (+`IAttributeSystem`): live pool edits via `SetMax` | `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` |
| `AccountSystem` (+`IAttributeSystem`): fill to the cap after starting abilities | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `SimCombatantFactory.Materialize` fills last; `CombatScenarioExecutor` reconciles after regen | `Core/Modules/Simulation/Systems/` |
| `score` `Attack`/`Defense`: `ScoreDisplayMessage.AttackPower`/`Defense` + formatter lines | `Core/Modules/Attributes/Commands/ScoreCommand.cs`, `Core/Output/` |
| Test harness helpers `TestAttributes`, `TestEffects` | `Hedron.Tests/Harness/` |
| `add-handler` skill priority guidance corrected (named tiers; when `X + n` is legitimate) | `.claude/skills/add-handler/SKILL.md` |

## Tests shipped

The suite went from 1868 to 1940 tests; `dotnet build` and `dotnet test` green (INV-25).
- **System-unit:** `StatSystemTests` (derivation from effective Body, floor at 0, typed getters = `Get`, pool delegation incl. floor at 1); `CombatSystemTests` (hit roll on effective Body ±; no throw at AP 0); `AttributePoolCapTests` (all four pools: effective max, clamped read, effective-cap writes, `SetMax`, `RestoreToFraction`, reconcile); `RegenerationSystemTests.Regenerates_into_effective_max`; `MobBuilderSystemTests.SetMobStat_pool_clamps_live_instance_to_effective_cap`; `DeathSystemTests` respawn on the real attribute system (permanent bonus counted, stripped timed buff excluded); `AccountSystemTests.CreateCharacter_fills_pools_to_effective_cap`; `SimCombatantFactoryTests.Materialize_fills_pools_to_effective_cap`.
- **Handler:** `PoolReconcileTickHandlerTests` (all four pools written back; priority pinned; only collaborator is `IAttributeSystem`).
- **Flow:** `PoolClampFlowTests` (expired buff clamps at once and is written back in the same tick with no incapacitation; remove-and-rewear gives no refill; respawn after death uses the effective cap with progression); `CombatEffectiveScoresFlowTests` (R13a: same mob, same seed, N 200 — fresh 8 wins, progressed 200; the spawned mob reads current = cap); `CombatScenarioExecutorTests.Reconcile_runs_each_tick_and_preserves_determinism`.
- **Persistence:** `RoundTripTests.Pool_current_above_raw_max_survives_round_trip` (kept when the progression source survives; clamped on read and reconciled when it doesn't).
- **Architecture guards:** `PoolCapGuardTests` (`AttributeSystem` takes no `IStatSystem`/`IRelativePowerSystem`; `IStatSystem` declares no setters; the reconcile handler is resolved and subscribed in `Server/`). The existing composition guard resolves the new handler and re-wired systems.
- **Deliberate rewrites:** `EquipmentEffectContributorTests` bare-getter assertions (now gear-inclusive), `SimCombatantFactoryTests` reference-build materialization (derived scores include the tier `Body`), the sim invariants and the live/sandbox parity test (see Balance record).
- **DevWorld smoke:** the server boots on the dev-world profile with the heartbeat running and no errors. The scripted telnet `affect`-expiry smoke was not run; T28 (subscription guard) and the T17 flow cover that path.

## Balance record

Sweeps: seed 2026, N 2000, `cooldown-first` policy (ability kits exercised), `simulate` run on the base commit and then on the slice.

| # | Scenario | Before | After |
|---|---|---|---|
| S1 | Reference T2B2 vs T2B2 | 49.6% / 50.4%, ticks-to-kill mean 44.0 | 50.3% / 49.6%, mean 14.8 — PASS |
| S2 | Reference T3B1 vs T2B3 (one band higher, across a tier) | 49.6% (FAIL 65%), mean 44.0 | 98.9% (PASS), mean 11.9 |
| S3a | Reference T1B1 vs T0B3 | 50.2% (FAIL), mean 44.0 | 100.0% (PASS), mean 19.2 |
| S3b | Reference T0B2 vs T0B1 (band only) | 49.8% (FAIL), mean 43.9 | unchanged |
| S4 | Reference T0B1 vs T0B1 (Tier-0 stand-in; dev-world has no mobs yet) | 49.8%, mean 43.9 | unchanged |
| S5 | `progressionRate` Body ×3, T2B2 vs T2B2 | kills-to-target mean 21.0 | 21.3 |

| CI golden | Before | After |
|---|---|---|
| `EqualCell_ReferenceBuild_*` (×2) | 106/94/0 | 110/90/0, verdict passes |
| `OneBandHigher_ReferenceBuild_WinRate_PinnedPendingBalanceTuning` → `…_Pinned` | 106/94/0, fails the floor | 199/1/0, passes the floor |
| `ProgressionRate_…_KillsToFirstImprovement`, SR7 midpoints, `power` 650/712/400 | — | held, unedited |

## Decisions

- **"Effective" means effective, at one seam.** The gap was never in combat — combat already asked `IStatSystem`, and its "Effective" typed getters read raw `AttributesComponent` values, with the derived base computed from raw `Body`. Fixing `StatSystem` fixed every consumer (combat, `score`, prompt, relative power, sandbox) at once; patching combat alone would have left the misleading getters for the next caller. Typed getters became `Get` wrappers (the documented target, INV-15); removing them (stub churn) and keeping them base-only (the misleading name) were rejected. Acyclic: no contributor's backing system reads `IStatSystem`.
- **The pool cap belongs to the pool write seam, keyed by `ResourceType`.** `IAttributeSystem` already clamped writes to base + modifiers, so it became the single owner of the effective cap, generalized over the four pools (the per-pool clamp was copied 4×+, past INV-19). `IStatSystem` delegates its pool reads to it so display and writes can't drift, and stays setter-free. `GetMaxX` was renamed `GetBaseMaxX` (spec review) because the base-vs-effective name ambiguity is exactly what caused the respawn bug.
- **Clamp-down = clamped read + heartbeat reconcile** (user, 2026-11-01). Most max sources are contributors with no "changed" signal (INV-24), so eager per-source reconciliation would be the scattered-call-site trap; a clamped read alone would let stored overflow refill current when a bonus is re-applied. Reads clamp at once; `PoolReconcileTickHandler` writes back within the tick. Its `Domain + 10` priority is the first between-tier value in `Core/`, justified as a no-observer mechanical follow-up of the Domain tier; the `add-handler` skill now states when that is legitimate (spec review).
- **Live mob spawn fill unchanged.** `MobTemplate.Apply` fills at the template max, which equals the effective cap because a live mob has no modifier source at spawn; routing it through `RestoreToFraction` would add an `IAttributeSystem` dependency to every spawn site for zero behavior. A forward note on the tier-tuning backlog entry names the trigger.
- **Typed `GetCurrentX` are clamped reads**, not only the keyed verb, so an ability cost check can't spend stored overflow in the ≤1-tick window before reconcile.
- **Derived scores floor at 0.** `Next(1, AP + 2)` throws for AP ≤ −2, which a negative effective `Body` can now reach; the floor keeps minimum damage at 1. `Get(Body)` itself is not floored.
- **Sim residual stays against raw `Body`** so the tier's `Body` adds on top; rebasing on effective `Body` would cancel the tier in the sandbox.
- **Ability/effect potency excluded** (user, 2026-11-01): effect power reads base stats to keep `Effect → Stat` acyclic; backlogged with a caller-gathers shape.
- **Tier side effect accepted, not tuned** (user, 2026-11-01): the one-band-higher invariant was re-pinned to its measured value; `AscensionConstants` and the floor are untouched.
- **Live/sandbox parity pinned at Tier 0** (user, 2026-11-01). Above Tier 0 the live mob's tier only feeds the oracle (projected onto `Body`/`HpMax`), while the sandbox combatant's tier folds for real and now also raises its derived scores — so a sandbox tier-1 victim reads stronger (1.4767 vs 1.3109 at the first kill). The divergence is pinned as its own test and recorded on the Phase 6 tier-tuning entry. Rejected: changing `PowerBudgetMath` now (out of scope, moves power goldens); stripping tier folds from sandbox mobs (breaks tiered reference-build fidelity).

## Deviations / Follow-ups

- **Respawn tests:** the plan moved "existing respawn tests" onto the real `AttributeSystem`; instead the new T13 tests use the real system and the existing stub-based tests stay (the stub implements `RestoreToFraction` faithfully, so they still exercise the same contract).
- **T15** was satisfied by both a dedicated `Materialize_fills_pools_to_effective_cap` test and the rewritten reference-build materialization test; **T28** is a source-scan guard (`PoolCapGuardTests.Heartbeat_handlers_are_subscribed`).
- **The parity fork** (above) was not anticipated by the plan; resolved with the user mid-implementation.
- **Tier step dominance:** one tier up now wins ~99–100% and tiered fights are ~3× shorter. Expected and accepted; it is Phase 6 tuning, recorded with the projection asymmetry and the mob-spawn forward note on the [Ascension tier tuning](../backlog.md#-ascension-tier-tuning-and-the-tier-projection-gap) entry.
- **Backlog:** "Ability/effect power on effective stats" (deferred); "Collapse pool switches onto the keyed `IAttributeSystem` verbs" (on-touch ratchet over `AbilitySystem`, `EffectTickHandler`, `AffectCommand`, `SetPlayerCommand`, `CooldownFirstPolicy`, `CombatScenarioExecutor`).
- **Unlocked:** the `progressionRate` executor could now drive real per-kill combat (open decision, not blocked); content drops 6–8 are authored against the fixed mechanic.
