# Combat effective scores — Phase 5 slice 5b

- **Status:** planned (full plan; F1 resolved 2026-11-01). Spec-mode `architecture-reviewer` gate run 2026-11-01: one blocking finding (the stale `add-handler` priority guidance) and the should-fix/nit findings are all folded in, so the plan is ready for `implement-plan`.
- **Actors:** Player, Mob, System (combat round, regeneration, respawn, spawn, sandbox)
- **Module:** `Core/Modules/Stats` (effective `Body` derivation) and `Core/Modules/Attributes` (the pool-cap seam and the reconcile tick). Callers are repointed in `Combat`, `Death`, `Mobs`/spawn, `Account` (creation) and `Simulation` (sandbox materialization and tick loop).
- **Program:** [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#slice-decomposition), slice 5b (gap slice, R30). It serves program R21 and R26 (A3/A7).

**Description.** Combat should read the same effective scores the rest of the game already shows. Today, improvements folded onto `Body`/`HpMax` by progression, passive abilities such as `toughness`, active buffs, and the ascension tier baseline raise a character's displayed and measured power, but not their fight outcomes. Melee attack power, defense and the hit roll read raw `Body`, and HP writes clamp to the raw pool max. Worn gear is the one source that already reaches combat. This slice makes melee combat and every pool write respect **effective** scores, so getting stronger means winning more fights. It is a mechanism fix and a deliberate balance change, not new gameplay.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-11-01. The advisor extends this file; the planner absorbs the R-numbers into Preconditions/Postconditions and Main flow.)*

### User stories

- *As a player*, I want my progression improvements, passive abilities and buffs to make me hit harder, hit more often, defend better and survive longer, so that getting stronger is something I feel in a fight and not just a number on `score`.
- *As a player*, I want the HP shown in my prompt to be HP I can actually have. A `100/130` prompt should regenerate to `130/130`.
- *As a player*, I want `consider` to tell the truth: if it says a mob is now *easy* because I improved, the fight should be easier.
- *As a mob*, buffs or debuffs on me change my combat numbers the same way they would for a player. There's one rule for every combatant.
- *As the system* (sandbox simulator), I want a sandbox combatant's tier and modifiers to change its sandbox fight outcomes, so sweeps measure the real mechanic.
- *As a designer*, I want this change to land as a deliberate, swept, re-pinned balance change, so I know what moved and why before content drops B1–B3 are tuned against it.

**Main scenario, as a player lives it.** A fresh character fights Band-2 mobs and loses most fights. They hunt Band 1, earn a few `Body` and `HpMax` improvements, and `score` shows the higher numbers. Their prompt reads `HP 112/112` after resting, where before it was stuck at `100/112`. Back in Band 2, they hit more often, deal more damage, take a little less, and last longer. They now win fights they used to lose. `consider` on a Band-2 mob moves from *dangerous* toward *even* at the same moment the fights actually get easier.

### Scope

**In scope:**
- **Melee reads effective scores.** Melee attack power, defense and the hit roll's `Body` term read effective values (R1–R3).
- **Effective pool maxima cap every pool write,** for **all four pools** (HP, Mana, Stamina, Astra): combat damage, regeneration, periodic effects, respawn restore, spawn fill, ability costs (R4–R7).
- **Clamp-down** when effective max falls below current (R8).
- **Display parity** between what the player sees and what combat uses (R9).
- **Persistence of HP above the raw max** across logout and restart (R10).
- **Tier side effect accepted.** The tier baseline now reaches combat; the one-band-higher sim invariant is re-pinned, not recalibrated (R12).
- **Deliberate balance change** with sweep and re-pins (R11–R13).
- **Doc-accuracy fixes** for every doc that currently describes the gap (R14).

**Out of scope (confirmed exclusions):**
- **Ability/effect power on effective stats** (`kick` damage, `empower` potency). Effect power reads base stats on purpose to keep the Effect→Stat dependency acyclic ([stat-system.md](../features/character-stats/stat-system.md)). Parked; see Open questions.
- **Tier tuning:** `AscensionConstants` and the standards' 65% higher-band floor. These stay in Phase 6 ([backlog](../roadmap/backlog.md), calibration-gap entry).
- **Mob tier folding into live combat stats.** Live mobs carry `MobDataComponent.Tier` for the oracle only. Irrelevant at Tier 0; Phase 6.
- **Moving the `progressionRate` sim onto real simulated combat per kill.** This slice removes the reason it was analytical, but changing it is its own decision ([simulation-engine.md](../features/simulation/simulation-engine.md)).
- **Any change to `IRelativePowerSystem`.** It is correct as built ([relative-power-system.md](../features/progression/relative-power-system.md)).
- **New authored state or authoring surface.** None is needed.
- **New combat mechanics:** hit/damage formula shape, crits, armor types. The formula shape is unchanged; only its inputs move to effective values.

### Behavioral requirements

**A. Melee combat inputs**
- **R1.** Melee **attack power** is derived from the attacker's **effective `Body`**, plus worn-gear and other `AttackPower` modifiers as today. A `Body` improvement, a `+Body` buff or a tier baseline raises melee damage.
- **R2.** Melee **defense** is derived from the defender's **effective `Body`**, plus worn-gear and other `Defense` modifiers as today. A `Body` improvement makes the character harder to hit.
- **R3.** The hit roll's attacker `Body` term uses **effective `Body`**. The roll's shape (`d20 + Body/2 ≥ 10 + Defense`) and the damage roll's shape are unchanged.

**B. Pool maxima**
- **R4.** For each of the four pools, the **effective max** (`Get(<Pool>Max)`) is the ceiling on current value **everywhere current is written**: combat damage results, regeneration, periodic heal/drain effects, ability cost spending, admin pool commands, respawn and spawn.
- **R5.** **Regeneration and healing** fill each pool up to its effective max. A player with a `+30 HpMax` improvement can rest from `100/130` to `130/130`.
- **R6.** **Respawn** restores each pool to the configured percentage (default 25%) of its **effective** max.
- **R7.** **Spawn** (mob or sandbox combatant) starts each pool at its **effective** max, so a mob with an `HpMax` modifier spawns at full effective HP.
- **R8.** When a pool's effective max **falls below** its current value (a buff expires, gear comes off, a passive is lost), current is **clamped down** to the new max at once. This clamp never kills or incapacitates by itself. An effective max is never below 1 for a pool whose base max is above 0.

**C. Visibility and durability**
- **R9.** What the player sees matches what combat uses. The prompt's current/max pairs (already `Get`-based) and `score`'s pool and attack/defense lines show effective values. A player can never see a max they cannot reach.
- **R10.** A player whose current pool value is above the raw stored max (because the effective max is higher) keeps that value across logout/login and a server restart. The bonus portion is not lost on load, provided the effective max on load still covers it. If it doesn't, R8 applies.

**D. Balance**
- **R11.** This is a **deliberate balance change** under the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) skill. A simulation sweep is run before and after, the results are recorded in the slice's completed record, and every golden that moves is re-pinned deliberately with its before/after value.
- **R12.** The **one-band-higher reference-build sim invariant** (pinned at 53% today against a 65% design floor) is **re-pinned to its new measured value**. It is not recalibrated, and `AscensionConstants` / the standards floor are not changed. The calibration-gap backlog entry is updated with the new number.
- **R13.** Observable acceptance facts:
  - **(a)** a character with `Body` and `HpMax` improvements beats the same mob measurably more often than a fresh character with the same gear, at a pinned seed. This is the A3/A7 contract.
  - **(b)** `consider`'s SR7 midpoints still hold: a fresh character reads a (T0, B1) midpoint mob as *even* or easier, and a (T0, B3) midpoint mob as *dangerous* or worse.

**E. Doc accuracy**
- **R14.** Every doc that currently describes the gap is corrected in the slice:
  - the "honesty is bounded by combat" consideration in [relative-power-system.md](../features/progression/relative-power-system.md);
  - the formula notes in [stat-system.md](../features/character-stats/stat-system.md) and [combat-system.md](../features/combat/combat-system.md);
  - the calibration-gap notes in [simulation-engine.md](../features/simulation/simulation-engine.md) and [balance.md](../design/balance.md) Known gaps (narrowed to tier tuning only);
  - the backlog entry.

### Edge cases & failure behavior

- **Buff expires mid-fight while above the new max:** current is clamped to the new max (R8). If the player was at, say, 125/130 and the max drops to 100, they go to 100/100. They are not incapacitated by the clamp, even if a hit lands in the same tick; only damage crosses the death threshold.
- **Unequip-then-re-equip loop:** removing gear clamps down (R8). Re-equipping raises the max but not current; the player regenerates into it. No free HP from swapping gear.
- **Debuff pushes effective `Body` very low:** attack power and defense floor at their existing minimums (whatever minimum damage the formula already has). Never negative. *(Assumption; the advisor confirms the existing floor.)*
- **Debuff pushes an effective max to 0 or below:** floored at 1 (R8), so a debuff alone never empties a pool.
- **Incapacitated player:** death thresholds (HP ≤ 0 incapacitates, HP ≤ `Death:HpFloor` kills) are unchanged and don't depend on max. Bleed-out is unaffected.
- **Login with stored current above stored raw max:** kept if the effective max on load covers it, clamped otherwise (R10).
- **Sandbox combatant at tier > 0:** its tier baseline now counts in fights (R12). That's intended.
- **Mob with no modifiers:** behaves exactly as today. Effective equals raw.

### Content & authoring needs

- **No new authored state.** No content field, editor or admin verb is added.
- **Inspection must keep reading effective values.** `score` (R9) is the player-side check. The admin `power` command already reads effective scores through the relative-power read.
- **Dev-world:** no content change required. Content drops 6–8 are authored *after* this slice, against the fixed mechanic.

### Grounding notes

- **The gap:**
  - [backlog.md](../roadmap/backlog.md), "Ascension tier baseline has no real combat effect", including its slice-5 update, which pulled this mechanism fix into Phase 5;
  - [simulation-engine.md](../features/simulation/simulation-engine.md), the "Known calibration gap" note;
  - [balance.md](../design/balance.md), Known gaps.
- **Gear already reaches combat** through `EquipmentEffectContributor` and `Get(AttackPower|Defense)` ([combat-system.md](../features/combat/combat-system.md) Extensibility, [stat-system.md](../features/character-stats/stat-system.md)). This slice extends the same principle to `Body` and the pool maxima.
- **The visible symptom:** the prompt already shows `Get(<Pool>Max)` ([prompt.md](../features/output/prompt.md)), while writes clamp to the raw `[0, max]` ([regeneration-system.md](../features/character-stats/regeneration-system.md), [attribute-system.md](../features/character-stats/attribute-system.md)).
  - ***Correction (advisor, code-verified):*** the `SetCurrentX` setters **already** clamp to base plus effect and contributor modifiers, so R5 already holds and this slice pins it with tests. The real pool gaps are spawn fill, respawn, the `SetMaxX`/builder clamps and the missing clamp-down. See the [Architecture brief](#architecture-brief).
- **Contributors that fold onto these scores:**
  - progression ([progression-system.md](../features/progression/progression-system.md): `Body`/`HpMax` tracks);
  - passive `toughness` ([ability-system.md](../features/abilities/ability-system.md));
  - ascension ([ascension-system.md](../features/progression/ascension-system.md));
  - effects ([effect-system.md](../features/effects/effect-system.md)).
- **The DI-cycle rule** ([progression-system.md](../features/progression/progression-system.md#anti-grind-ratio-is-supplied-by-the-caller)): a contributor's backing system never reads `IStatSystem`. It is relevant to whoever enforces the pool cap; see Open questions.
- **Death thresholds and the respawn percentage** ([death-system.md](../features/combat/death-system.md)) are unchanged except that the respawn percentage now applies to the effective max (R6).
- **Slice 5's decision** that the relative-power read "needs no change when combat is fixed" ([completed/relative-power-consider.md](../roadmap/completed/relative-power-consider.md#decisions)).

### Resolved decisions

*(Confirmed by the user on 2026-11-01. Do not relitigate downstream.)*

1. **Pool scope: all four pools.** The effective-max rule covers HP, Mana, Stamina and Astra. Rejected: HP only.
2. **Max drops below current: clamp down at once.** Never lethal; floored at 1. Rejected: keeping the overflow and letting it drain naturally, which invites gear-swap exploits.
3. **Ability/effect power: out of scope, parked.** Melee and pools only. Rejected: moving effect power onto effective stats in this slice.
4. **Tier side effect: accepted.** The one-band-higher invariant is re-pinned to its new value; no tier tuning. Rejected: tuning `AscensionConstants` now.
5. **Suppositions S1–S8 approved as proposed.** They are recorded above as R1–R3 (effective `Body` in melee), R2/R7 (one rule for every combatant), R4–R7 (effective max caps all writes), R9 (display parity), R10 (persistence), R11/R13 (re-pin, SR7 holds, progressed beats fresh), and the Out-of-scope list.

## Preconditions

- The effect contributor seam (INV-24) folds equipment, abilities (passives), progression and ascension onto `ScoreId` reads through `IEffectSystem.GetModifiers`. Unchanged by this slice.
- `IStatSystem.Get(entityId, ScoreId)` is the single effective-score read used by combat (`AttackPower`, `Defense`), `score`, the prompt and `IRelativePowerSystem.GetEffectivePower`.
- `IAttributeSystem` owns `AttributesComponent`/`PoolsComponent` reads and writes. `AttributeSystem` already depends on `IEffectSystem` (a legal downward edge), and its `SetCurrentX` setters already clamp to base plus modifiers.
- No contributor's backing system reads `IStatSystem` (the progression and ascension DI-cycle rule).
- Server startup hydrates **every** persistent entity before the heartbeat arms ([flow-01](../architecture/flows/flow-01-server-startup.md) step 7). So the first reconcile tick sees a character together with its equipped items, effects and progression.
- `PoolsComponent` is `[Persistent]`, and neither the persistence layer nor `CharacterHydrationHandler` clamps pool values on load (code-verified; see [Open questions, resolved](#open-questions)).
- The heartbeat's p=20 (Domain) handlers (`EffectTickHandler`, `CombatTickHandler`, `DeathTickHandler`, `RegenerationTickHandler`, `AbilityCooldownTickHandler`) write pools only through `IAttributeSystem`.
- The sandbox (`SandboxWorld`) composes its own `IAttributeSystem`/`IStatSystem`/`IEffectSystem` over its own `EntityService` and drives ticks in `CombatScenarioExecutor` with no bus.

## Postconditions

*(The coverage contract. Every row maps to a named test in [Test plan](#test-plan--verification).)*

**Effective scores (Stats / Combat)**
1. `IStatSystem.Get(AttackPower)` = `max(0, Get(Body) / 2 + GetModifiers(AttackPower))`. `Get(Defense)` = `max(0, Get(Body) / 4 + GetModifiers(Defense))`. Integer division applies to effective `Body` (R1, R2; edge case "never negative").
2. `CombatSystem.ExecuteRound`'s hit roll is `d20 + Get(Body) / 2 ≥ 10 + Get(Defense)`. The roll and damage shapes are unchanged (R3).
3. Every typed getter on `IStatSystem` is a thin wrapper over `Get`: `GetEffectiveMind/Body/Spirit/Attunement`, `GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp` and `GetMaxHp` equal `Get` of their score. **Semantic change:** `GetEffectiveAttackPower`/`GetEffectiveDefense` are no longer base-only (INV-15: the code moves to the documented wrapper shape).

**Pool cap seam (Attributes)**

4. `IAttributeSystem.GetEffectiveMax(entityId, ResourceType)` = base max + `GetModifiers(<Pool>Max)`. It is floored at **1** when base > 0, and at **0** otherwise. `IStatSystem.Get(<Pool>Max)` returns exactly this value for all four pools (R4, R8, R9).
5. `IAttributeSystem.GetCurrent(entityId, ResourceType)` = `min(stored current, GetEffectiveMax)`. The typed `GetCurrentHp/Mana/Stamina/Astra` and `IStatSystem.Get(<Pool>Current)` return this clamped read. A negative stored HP (incapacitated or bleeding) reads unchanged (R8, R9).
6. `IAttributeSystem.SetCurrent(entityId, ResourceType, value)` stores `clamp(value, floor, GetEffectiveMax)`. The floor is `Death:HpFloor` for HP and 0 for the other pools. The typed `SetCurrentX` wrap it. An entity with `+30 HpMax` regenerates from `100/130` to `130/130` (R4, R5: pinned, not changed).
7. `IAttributeSystem.SetMax(entityId, ResourceType, value)` stores the new **base** max, then clamps stored current to the new **effective** cap (not the raw max). The typed `SetMaxX` wrap it. `MobBuilderSystem`'s live-instance pool edits (`setmob hp/maxmana/maxstamina/maxastra`) go through it (R4).
8. `IAttributeSystem.RestoreToFraction(entityId, ResourceType, fraction)` stores `floor(GetEffectiveMax × fraction)` through `SetCurrent` (R6, R7).
9. `IAttributeSystem.ReconcilePools(entityId)` writes `min(stored, cap)` back to storage for each of the four pools and returns whether anything changed. It never raises a value and never writes below the cap. `ReconcileAllPools()` does this for every `PoolsComponent` entity and returns the number of pool values corrected (R8).

**Clamp-down (heartbeat and sandbox)**

10. `PoolReconcileTickHandler` (`HeartbeatTickEvent`, priority `HandlerPriority.Domain + 10` = 30) calls `ReconcileAllPools()` once per tick. It publishes nothing, has no `IDeathSystem` dependency, and never incapacitates. After a tick in which a timed `+HpMax` buff expires, stored HP ≤ the new cap. Removing `+HpMax` gear and putting it back on never refills current (R8; resolved decision 2).
11. `CombatScenarioExecutor` calls `world.Attributes.ReconcileAllPools()` once per tick, after regeneration, and draws zero extra `IRandom` values, so run determinism is preserved (live/sandbox parity).

**Restore and fill call sites**

12. `DeathSystem.Respawn` restores each pool via `RestoreToFraction(pool, RespawnPoolPercent)` **after** `RemoveImpermanent`. The cap therefore counts permanent and contributor modifiers, and excludes stripped timed buffs (R6).
13. `SimCombatantFactory.Materialize` calls `RestoreToFraction(pool, 1.0)` for all four pools **after** the `AscensionComponent`, the computed-score residual effects and the ability kit are attached. A tier-1 combatant starts at `Get(HpMax)` (R7, R12).
14. `AccountSystem.CreateCharacterAsync` calls `RestoreToFraction(pool, 1.0)` for all four pools **after** the starting abilities are learned, and before it returns to the construction-save initiator (R7).
15. A live mob spawned through `TemplateRegistry.Spawn` (`MobTemplate.Apply`) reads `current == GetEffectiveMax` for every pool. This holds **by construction**: a live mob has no modifier source at `Apply` time. See [Design notes, planner resolutions](#planner-resolutions-code-verified) (R7).

**Durability, guards, balance, docs**

16. A character whose stored `CurrentHp` is above stored `MaxHp`, but within the effective cap (from a persistent `HpMax` source), saves and loads with the stored value intact and reads it unclamped. If the cap on load is lower, the read is clamped and the next tick writes the clamp back (R10).
17. `AttributeSystem` takes no `IStatSystem` (or `IRelativePowerSystem`) constructor dependency. `IStatSystem` declares no `void` (setter) members (INV-1/INV-2; brief "Invariants in tension").
18. At a pinned seed, a character with `Body` and `HpMax` progression improvements wins measurably more of N fights against the same mob than a fresh character with the same gear (R13a).
19. The SR7 midpoint test and the `power` 650/712/400 goldens are unchanged (R13b; brief expectation, verified in [Balance record](#balance-record)).
20. Every moved golden is re-pinned with its before/after value recorded. The one-band-higher invariant is re-pinned to its measured value, its verdict assertion is updated to whatever the floor check now yields, and `AscensionConstants`/`HigherBandWinRateFloor` are unchanged (R11, R12).
21. Every doc listed under R14 and the brief's INV-15/20/27 list describes the as-built behavior, including the `balance-tuning` skill guardrail (R14).
22. `score` shows `Attack` and `Defense` lines from `Get(AttackPower)`/`Get(Defense)` (R9; F1 resolved as (a)).

## Main flow

*(A representative trace across the four touched paths: a progressed player fights, a buff expires, then death and respawn.)*

1. **Combat round.** `CombatTickHandler` (p=20) calls `ICombatSystem.ExecuteRound(attacker, defender)`. `CombatSystem` reads `IStatSystem.Get(attacker, Body)` for the hit roll and `Get(defender, Defense)` for the threshold. On a hit, it reads `Get(attacker, AttackPower)`, whose base `StatSystem` now derives from effective `Body`. Publishes: `CombatRoundEvent` (unchanged, published by the handler).
2. **Damage write.** `CombatSystem.ApplyDamageAndBuildResult` reads `IStatSystem.GetCurrentHp(defender)` (now the clamped read), then calls `IAttributeSystem.SetCurrentHp(defender, hp − damage)` → `SetCurrent(Hp, …)`, which clamps to `[HpFloor, GetEffectiveMax(Hp)]`. The death check reads the clamped HP within the same handler.
3. **Buff expiry.** In the same tick, `EffectTickHandler` (p=20) calls `IEffectSystem.AdvanceTick`. A timed `+30 HpMax` buff expires, and the handler publishes `EffectExpiredEvent` (unchanged). From this moment `GetEffectiveMax(Hp)` drops, so every reader (prompt, `score`, combat, ability cost checks) sees `GetCurrent(Hp) = min(stored, cap)` at once.
4. **Reconcile.** `PoolReconcileTickHandler` (p=30) calls `IAttributeSystem.ReconcileAllPools()`. Stored HP above the new cap is written down to it, and the same holds for Mana, Stamina and Astra. Nothing is published, and the death system is never consulted, because the cap is ≥ 1.
5. **Prompt.** `OutputFlushTickHandler` (p=85) appends the prompt. `PromptComposerSystem` reads `Get(HpCurrent)`/`Get(HpMax)`, which are now both delegated to the cap seam (R9).
6. **Death and respawn.** Later, HP crosses `Death:HpFloor`. `PlayerDeathHandler` → `IDeathSystem.Respawn` relocates the player and calls `IEffectSystem.RemoveImpermanent`. It then calls `IAttributeSystem.RestoreToFraction(pool, RespawnPoolPercent)` for each pool, so the player returns at 25% of the **effective** cap (permanent progression and `toughness` included). Publishes: the existing death and respawn events (unchanged).
7. **Sandbox parity.** In a `simulate` run, `SimCombatantFactory.Materialize` fills each pool via `RestoreToFraction(…, 1.0)` once tier, residuals and abilities are attached. `CombatScenarioExecutor` runs effects → cooldowns → actions → regen → `ReconcileAllPools()` each tick, with the same seams and no bus.
8. **New character.** `AccountSystem.CreateCharacterAsync` learns the starting abilities, then fills each pool via `RestoreToFraction(…, 1.0)`. The initiator then performs the construction save (INV-22 category a, unchanged).

## Events fired

**No new events.** No existing event changes payload or publisher.

| Event | Publisher | Change in this slice |
|---|---|---|
| `CombatRoundEvent`, `AbilityStrikeResolvedEvent` | `CombatTickHandler`, `AbilityStrikeHandler` | None. The values carried (damage) now reflect effective inputs. |
| `EffectExpiredEvent` | `EffectTickHandler` | None. The clamp it implies is applied by the reconcile tick, not by a subscriber. |
| `PlayerIncapacitatedEvent`, `MobDiedEvent`, death/respawn events | existing publishers | None. The reconcile can never cause them. |
| `HeartbeatTickEvent` | `HeartbeatBackgroundService` | **One new subscriber:** `PoolReconcileTickHandler` (p=30). |

The clamp-down is a silent correction (brief: no `PoolChangedEvent`).

## Systems / handlers involved

| Piece | Kind | Status | Change |
|---|---|---|---|
| `StatSystem` / `IStatSystem` (`Core/Modules/Stats/Systems/`) | domain system | **changed** | `Get(AttackPower)`/`Get(Defense)` derive their base from `Get(Body)` and floor the total at 0. `Get(<Pool>Max)`/`Get(<Pool>Current)` delegate to `IAttributeSystem.GetEffectiveMax`/`GetCurrent`. Typed getters wrap `Get`. XML docs are refreshed (the stale "base only in this slice" note is removed). No new members. |
| `AttributeSystem` / `IAttributeSystem` (`Core/Modules/Attributes/Systems/`) | domain system | **changed** | New keyed verbs: `GetEffectiveMax`, `GetCurrent`, `SetCurrent`, `SetMax`, `RestoreToFraction`, `ReconcilePools`, `ReconcileAllPools` (`ResourceType`-keyed). Typed `GetCurrentX`/`SetCurrentX`/`SetMaxX` become wrappers. Typed `GetMaxX` are **renamed `GetBaseMaxHp/Mana/Stamina/Astra`** (stored base read), so no `IAttributeSystem` getter named like an effective value returns a base one (spec review; the respawn bug came from exactly this ambiguity). A private `ResourceType → ScoreId(<Pool>Max)` map. Dependencies unchanged (`EntityService`, `IEffectSystem`, `DeathOptions`). |
| `PoolReconcileTickHandler` (`Core/Modules/Attributes/Handlers/`) | **new** heartbeat handler | **new** | `IEventHandler<HeartbeatTickEvent>`, priority `HandlerPriority.Domain + 10`. Depends only on `IAttributeSystem`. Calls `ReconcileAllPools()`. Registered in `AddAttributesModule`; subscribed wherever the other `HeartbeatTickEvent` handlers are subscribed (today `Server/Program.cs`; `CompositionRoot.SubscribeGameplayHandlers` if PT-1 lands first). |
| `CombatSystem` (`Core/Modules/Combat/Systems/`) | domain system | **changed** | The hit roll reads `_statSystem.Get(attacker, ScoreId.Body)`. Nothing else changes. |
| `DeathSystem` (`Core/Modules/Death/Systems/`) | domain system | **changed** | `Respawn` step 4 uses `RestoreToFraction(pool, RespawnPoolPercent)` over the four pools. |
| `MobBuilderSystem` (`Core/Modules/Mobs/Systems/`) | domain system | **changed** | Gains an `IAttributeSystem` dependency. The `hp/maxmana/maxstamina/maxastra` live-instance lambdas call `SetMax(id, pool, value)` in place of the inline raw clamp. |
| `AccountSystem` (`Core/Modules/Account/Systems/`) | domain system | **changed** | Gains an `IAttributeSystem` dependency. Fills pools to the effective cap after the starting abilities are learned. |
| `SimCombatantFactory` (`Core/Modules/Simulation/Systems/`) | sandbox system | **changed** | Fills pools to the effective cap at the end of `Materialize`. The residual computation stays against **raw** `body/2`, deliberately (see Design notes). |
| `CombatScenarioExecutor` (`Core/Modules/Simulation/Systems/`) | sandbox driver | **changed** | Adds step 5, `world.Attributes.ReconcileAllPools()`, after regen. |
| `MobTemplate.Apply`, `SpawnSystem` | template / domain | **unchanged** | The fill at the template max equals the effective cap at `Apply` time (resolution below). |
| `RegenerationSystem`, `EffectTickHandler`, `DeathTickHandler`, `AbilitySystem`, `AffectCommand`, `CooldownFirstPolicy`, `CombatTickHandler` | callers | **unchanged code** | They now read the clamped `GetCurrentX` through the existing typed getters. Their `ResourceType`/`ScoreId` switches may collapse onto the keyed verbs **on touch** (acknowledged debt, backlog). |
| `IRelativePowerSystem`, `PowerBudgetSystem` | domain / core | **unchanged** (R-scope) | They read more for `Body`-modified entities (brief balance consequence). |
| `ScoreCommand` / `ScoreDisplayMessage` / `TelnetOutputFormatter` | command / output | **changed** (F1 = (a)) | Two fields (`AttackPower`, `Defense`) read from `Get` plus their formatter line. |

## Implementation plan — work packages

**Before any code:** WP3 step 1 (the "before" sweep) runs on the slice's base commit. It can run in parallel with WP1, since it only reads the base.

### WP1 — Pool-cap seam, reconcile tick, pool call sites

- **Scope:**
  1. `IAttributeSystem`/`AttributeSystem`: add the seven keyed verbs (Postconditions 4–9). Re-implement the typed `GetCurrentX`/`SetCurrentX`/`SetMaxX` as one-line wrappers over the keyed verbs. Rename the typed `GetMaxX` to `GetBaseMaxX` (stored-base read, XML doc "base; read `GetEffectiveMax` for the cap") and update its ~12 test references. Refresh the interface's pool-invariant XML doc (the cap is effective, floored at 1 when base > 0; reads are clamped).
  2. `StatSystem.Get`: the eight pool `ScoreId` arms delegate to `GetEffectiveMax`/`GetCurrent`, so the floor-at-1 rule is shared by display and writes. `GetCurrentHp`/`GetMaxHp` become `Get(HpCurrent)`/`Get(HpMax)`.
  3. New `Core/Modules/Attributes/Handlers/PoolReconcileTickHandler.cs` (Postcondition 10). Register it in `AddAttributesModule` and subscribe it beside the other heartbeat handlers.
  4. `DeathSystem.Respawn` step 4 → `RestoreToFraction` (Postcondition 12).
  5. `MobBuilderSystem`: inject `IAttributeSystem`; the pool lambdas call `SetMax` (Postcondition 7).
  6. `AccountSystem`: inject `IAttributeSystem`; fill after the starting-ability loop (Postcondition 14).
  7. `SimCombatantFactory.Materialize`: fill at the end (Postcondition 13). `CombatScenarioExecutor`: reconcile after regen (Postcondition 11). Update its class XML doc ("effects → cooldowns → actions → regen → reconcile").
  8. Tests T7–T19, T21–T24 and T28 ([Test plan](#test-plan--verification)).
- **Files:**
  - `Core/Modules/Attributes/{Systems/IAttributeSystem.cs, Systems/AttributeSystem.cs, Handlers/PoolReconcileTickHandler.cs (new), AttributesModule.cs}`;
  - `Core/Modules/Stats/Systems/StatSystem.cs` (pool arms only; WP2 owns the rest of the file);
  - `Core/Modules/Death/Systems/DeathSystem.cs`; `Core/Modules/Mobs/Systems/MobBuilderSystem.cs`; `Core/Modules/Account/Systems/AccountSystem.cs`;
  - `Core/Modules/Simulation/Systems/{SimCombatantFactory.cs, CombatScenarioExecutor.cs}`;
  - `Server/Program.cs` (or `Server/CompositionRoot.cs`) for the subscription;
  - tests under `Hedron.Tests/{Attributes, Stats, Death, Authoring, Account, Simulation, Regeneration, Persistence, Architecture, Attributes/Handlers}/` and a new flow file `Hedron.Tests/Attributes/PoolClampFlowTests.cs`.
- **Docs (WP1-owned, same commit):** `.claude/skills/add-handler/SKILL.md` ("Picking a priority": bucket list → the `HandlerPriority` constants 10/20/80/85/90/95; a between-tier `X + n` is legitimate only for a no-observer mechanical follow-up of tier X, the precedent being `PoolReconcileTickHandler`; otherwise prefer a phased event) (INV-20); [attribute-system.md](../features/character-stats/attribute-system.md) (the keyed verbs, clamped reads, effective-cap clamp, reconcile tick); [regeneration-system.md](../features/character-stats/regeneration-system.md) (`[0, max]` → effective cap); [death-system.md](../features/combat/death-system.md) (respawn % of the effective cap); `reference/systems.md` rows (`AttributeSystem`, `DeathSystem`, `AccountSystem`, `MobBuilderSystem`, `SimCombatantFactory`, `CombatScenarioExecutor`); `reference/handlers.md` (new `PoolReconcileTickHandler` row under `HeartbeatTickEvent`); flows 07, 13, 16, 20, 21, 33 (see [Flows](#flows-introduced-or-modified)).
- **Depends on:** nothing.
- **Out of scope:**
  - the `Body` derivation, typed `IStatSystem` attribute getters and the combat hit roll (WP2);
  - migrating the `ResourceType` switches in `AbilitySystem`/`EffectTickHandler`/`AffectCommand`/`SetPlayerCommand`/`CooldownFirstPolicy`/`CombatScenarioExecutor.ApplyPeriodicMagnitude` onto the keyed verbs (on-touch debt);
  - `MobTemplate.Apply` and `SpawnSystem` (no change; Postcondition 15);
  - re-pinning balance goldens (WP3).
- **Test stubs implementing `IAttributeSystem`** (they break on the new members): `Hedron.Tests/Abilities/AbilitySystemTests.cs:34` and `Hedron.Tests/Death/DeathSystemTests.cs:35`. Extend both. **T13 and the existing respawn tests move onto the real `AttributeSystem` + `EffectSystem`**, so the respawn assertions exercise the effective cap rather than stub behaviour. The Death stub stays for the non-respawn tests (spec review).
- **Exit criterion:** build green. T7–T19 and T21–T24 green. **Smoke:** on DevWorld, apply a timed `+HpMax` effect via admin `affect`, rest to full, let it expire, and confirm the prompt and stored HP drop to the base cap within a tick. This proves the `HeartbeatTickEvent` subscription is wired in `Server/Program.cs` (see T28). Every **other** test is green **except** goldens listed in the [Balance record](#balance-record) "expected to move" column, which WP1 records (test name + new value) but does not re-pin.

### WP2 — Effective `Body` in the derived scores and the hit roll

- **Scope:**
  1. `StatSystem`: `Get(AttackPower)` = `max(0, Get(Body)/2 + mods)` and `Get(Defense)` = `max(0, Get(Body)/4 + mods)`. The base derivation moves into a private helper. Every typed getter wraps `Get` (Postconditions 1, 3). Rewrite the `IStatSystem` XML docs: the typed getters are effective wrappers, and the "base-only" wording is removed.
  2. `CombatSystem.ExecuteRound` hit roll → `Get(attacker, ScoreId.Body) / 2` (Postcondition 2).
  3. Rewrite the tests that pinned the old base-only semantics as deliberate semantic changes, not re-pins: `StatSystemTests` `GetEffectiveAttackPower_*`/`GetEffectiveDefense_*` (→ assert through `Get` with and without `Body` modifiers), and `EquipmentEffectContributorTests` lines 126/142 (→ `GetEffectiveAttackPower`/`GetEffectiveDefense` now include gear). Update the `CombatFlowTests` remark at line ~431 that cites "the base-only `GetEffectiveAttackPower`".
  4. Add `AttackPower`/`Defense` to `ScoreDisplayMessage`, populated by `ScoreCommand` from `Get`, plus one formatter line in `TelnetOutputFormatter` (T27).
  5. Tests T1–T6 and T27.
- **Files:** `Core/Modules/Stats/Systems/{IStatSystem.cs, StatSystem.cs}`; `Core/Modules/Combat/Systems/CombatSystem.cs`; tests `Hedron.Tests/{Stats/StatSystemTests.cs, Combat/CombatSystemTests.cs, Items/EquipmentEffectContributorTests.cs, Combat/CombatFlowTests.cs}`. Also `Core/Modules/Attributes/Commands/ScoreCommand.cs`, `Core/Output/{ScoreDisplayMessage.cs, TelnetOutputFormatter.cs}`, and a score command test.
- **Docs (WP2-owned):** [stat-system.md](../features/character-stats/stat-system.md) (formula table, typed-getter paragraph, Interface and Extensibility bullets; "Acyclic by construction" kept and extended with the note that `Get(Body)` in the derived base adds no edge); [character-stats.md](../features/character-stats/character-stats.md) lines ~15 (`[0, max]`), ~56 and ~82; [combat-system.md](../features/combat/combat-system.md) (hit-roll term, derived-score inputs); flow-17 step text; `reference/systems.md` rows `StatSystem`/`CombatSystem`. Also [`score`](../reference/commands.md) and the output doc for the two lines.
- **Depends on:** WP1, for file-level sequencing only (both edit `StatSystem.cs`). Logically independent.
- **Out of scope:**
  - ability/effect power (`ResolveAbilityStrike`'s `basePower`, `EffectSystem` power formulas);
  - `SimCombatantFactory.ApplyComputedResidual` (keeps raw `body/2`; see Design notes);
  - any `IRelativePowerSystem`/`PowerBudgetMath` change.
- **Exit criterion:** build green; T1–T6 and T27 green. Every other test green except goldens in the [Balance record](#balance-record), which WP2 appends to.

### WP3 — Balance sweep, re-pins, doc accuracy (R11–R14)

- **Scope:**
  1. **Before sweep (base commit, before WP1 lands):** run the [sweep set](#balance-record) with `dotnet run --project Server -- simulate --scenario <path> --seed 2026` and record the results in the Balance record.
  2. **After sweep (on WP1 + WP2):** same scenarios, same seeds and N. Record the after values.
  3. **Re-pin** every golden WP1/WP2 listed, with before → after in the Balance record, in one commit:
     - `SimulationInvariantTests.EqualCell_*` (2 tests; the verdict must still pass);
     - `OneBandHigher_*`: rename it to `OneBandHigher_ReferenceBuild_WinRate_Pinned`, rewrite its remarks from "calibration gap: zero effect" to "tier now reaches combat; magnitude tuning is Phase 6", and set `Assert.True`/`Assert.False` to the measured verdict;
     - any `SimCombatantFactoryTests`/`CombatScenarioExecutorTests`/`SimulationRunnerTests` golden at tier > 0.
     
     Verify (no edit expected) the ProgressionRate first-improvement pin, the SR7 test and the `PowerCommandTests` goldens. If any of these moves, **stop and surface it**: it contradicts the brief's expectation.
  4. **R13a:** T20 lands here, since it is a balance-facing flow test and pins win counts.
  5. **Docs (R14 and the brief list):**
     - [relative-power-system.md](../features/progression/relative-power-system.md): drop the "honesty is bounded by combat" caveat;
     - [simulation-engine.md](../features/simulation/simulation-engine.md): the "Known calibration gap" note and line ~85 (`GetEffectiveAttackPower` wording), and correct the "combat HP/death check reads raw pools" claim;
     - [simulation.md](../features/simulation/simulation.md), [power-budget-system.md](../features/progression/power-budget-system.md), [ascension-system.md](../features/progression/ascension-system.md): any calibration-gap mention;
     - [balance.md](../design/balance.md) Known gaps, narrowed to tier tuning plus the tier-projection asymmetry (Design notes);
     - [flow-32](../architecture/flows/flow-32-ascension.md) (baseline fold now reaches combat);
     - [`balance-tuning` SKILL.md](../../.claude/skills/balance-tuning/SKILL.md) line 115 guardrail (INV-20);
     - [backlog.md](../roadmap/backlog.md):
       - narrow the "Ascension tier baseline has no real combat effect" entry and retitle it (e.g. "Ascension tier tuning"). Record the new one-band-higher number and the asymmetry. Add the forward note: *when a live mob gains a modifier source at spawn (mob tier folding, mob gear), route the spawn fill through `IAttributeSystem.RestoreToFraction`*;
       - add a one-line on-touch entry: "collapse `ResourceType`/pool-`ScoreId` switches onto the keyed `IAttributeSystem` verbs";
     - [plan.md](../roadmap/plan.md), wherever it mentions the gap;
     - the [program row](phase-5-content-baseline-program.md#slice-decomposition) for 5b (description correction: the pool gaps were spawn, respawn and clamp-down, not the death check), **and the program's Out-of-scope line ~50** ("the tier baseline has no real combat effect": now false; reword to "tier tuning");
     - [`reference/systems.md`](../reference/systems.md) line ~918 (the `SimCombatantFactory` row: "`StatSystem` derives from raw `Body`");
     - [flow-33](../architecture/flows/flow-33-simulation-run.md) line ~93 ("current tier-baseline calibration gap");
     - the **INV-8 mis-citation** for the clamp invariant in `attribute-system.md:7`, `regeneration-system.md:25` and `reference/systems.md:502`. INV-8 is about thin initiators; drop or correct the citation while rewriting those lines (spec review);
     - optional: [`edit-progression-system` SKILL.md](../../.claude/skills/edit-progression-system/SKILL.md) line ~67, naming `IAttributeSystem` pool reads as computed values now that they are clamped to the effective cap (clarification only, INV-20);
     - a grep sweep of the remaining living-doc hits for `GetEffectiveAttackPower`, "raw Body", "calibration gap", "[0, max]": [gameplay-model.md](../design/gameplay-model.md) line ~100 (historic design, so a one-line "as built" note only), and the [playtest-run-mode](playtest-run-mode.md) in-flight plan (notify only; do not edit another slice's plan beyond a factual pointer).
- **Files:** `Hedron.Tests/Simulation/*` (re-pins), the new R13a test file, the docs above, scenario YAMLs for the sweep (in the scratchpad or under `data/`, **never committed** to `content/`).
- **Depends on:** WP1 + WP2 for steps 2–5. Step 1 depends on nothing.
- **Out of scope:** changing `AscensionConstants`, `HigherBandWinRateFloor`, `PowerBudgetTunables` or any standards value (R12).
- **Exit criterion:** `dotnet build` + `dotnet test` fully green. The Balance record has before/after for every sweep row and every re-pinned golden. The doc grep shows no remaining living-doc claim of the gap.

**Primary agent:** once all three packages land, run `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact

*(INV-18.)*

- **No new authored state.** No content-file shape, no `TemplateRegistry` kind, no admin verb, no `[Persistent]` component, and no `appsettings` key. `Death:RespawnPoolPercent` is reused with its meaning sharpened (a fraction of the **effective** max).
- **Inspection is unchanged and now honest:**
  - the prompt and `score` show `Get(<Pool>Current/Max)` from the shared cap seam;
  - `power <target>` reads `GetEffectivePower`;
  - `consider` reads the relative-power label.
  
  `score` also shows effective `Attack`/`Defense`, the only player-side view of the derived scores.
- **Existing admin verbs** `setplayer <pool>`/`setplayer max<pool>` and `setmob <bp> hp|maxmana|maxstamina|maxastra` keep their syntax. Their clamps now target the effective cap (Postcondition 7).
- **Dev-world:** no content change.
- **Sweep scenarios** are operator/agent artifacts (scratchpad or `data/`); none are committed.

## Cross-cutting surfaces stressed

*(INV-19, INV-17, INV-31.)*

| Surface | Classification | Notes |
|---|---|---|
| **Pool write family (`SetCurrentX`/`SetMaxX`/`GetMaxX` × 4)** | **Gap exposed → fixed in-slice (WP1)** | The same clamp logic was copied four times (and four more times inline in `MobBuilderSystem`), past the INV-19 ≥3 bar. Fixed by the `ResourceType`-keyed verbs, with the typed members kept as wrappers. **Caller switches** on `ResourceType`/pool `ScoreId` (`AbilitySystem` ×2, `EffectTickHandler`, `AffectCommand`, `SetPlayerCommand`, `CooldownFirstPolicy`, `CombatScenarioExecutor`) stay as-is: **acknowledged debt**, an on-touch ratchet with a backlog line (WP3). They are correct today because the typed wrappers route through the keyed verbs. |
| **Event bus** | Adequate | No new event. One new `HeartbeatTickEvent` handler that calls a system and publishes nothing (INV-5 satisfied; no exception needed). |
| **Handler ordering (INV-7)** | Gap exposed → fixed in-slice (WP1) | p=30 is a value between named tiers, which `HandlerPriority`'s remarks sanction. But `.claude/skills/add-handler/SKILL.md` lists stale buckets (10/20/50/80/90/95: there is no 50 constant, and 85 is missing) and discourages in-between values. WP1 corrects the skill (INV-20, spec review). This is the first `HandlerPriority.X + n` in `Core/`; the skill states when that is legitimate: a mechanical follow-up that must see one named tier's writes in the same event, with no observer of its own, so a phased event would add a type for nothing. It runs after every p=20 tick writer and before Notification (80) / OutputFlush (85) / `SpawnSystem` (95). Clamped reads make the order non-load-bearing for correctness. It cannot interleave a damage write and its death check, which share one handler. |
| **ECS queries** | Adequate | `ReconcileAllPools` iterates `GetAllComponents<PoolsComponent>()`, the same sweep `RegenerationSystem.ApplyTickRegen` already does. |
| **Persistence** | Adequate | See the audit below. No shape change, no save site. |
| **Commands** | Adequate | No new verb. Two admin verbs change only their clamp target. |
| **Output** | Adequate | The prompt is unchanged (already `Get`). `ScoreDisplayMessage` gains two fields: an additive typed-message change through the existing formatter path. |
| **Time** | Adequate | The reconcile reads no clock. It runs per heartbeat, and the sandbox runs it per synthetic tick. |
| **Randomness (INV-26)** | Adequate | No chance is added. The sandbox reconcile draws nothing, so the per-run draw sequence is unchanged by the reconcile itself. Goldens move only because the combat **inputs** changed. |
| **Content templates** | Adequate | `MobTemplate.Apply` is unchanged; Postcondition 15 holds by construction. A forward note goes on the Phase 6 entry (WP3). |
| **Configuration** | Adequate | `Death:RespawnPoolPercent` and `Death:HpFloor` are reused. Nothing is added. |
| **Sessions** | Adequate | Untouched. |
| **Modules / DI** | Adequate (guarded) | `AccountSystem` and `MobBuilderSystem` gain `IAttributeSystem`. There is no cycle, because `AttributeSystem`'s graph is `EntityService`/`IEffectSystem`/`DeathOptions`, and `AccountSystem` already reaches `IAttributeSystem` transitively via `IAbilitySystem`. Pinned by T23 (no `IStatSystem` edge into `AttributeSystem`) and the existing DI-resolution guard (T24). |
| **Docs / skills (INV-20, INV-27)** | **Gap exposed → fixed in-slice (WP1–WP3)** | Docs and the `balance-tuning` skill encode the gap as current truth. They are corrected per R14 and the brief list. |
| **Flows (INV-17)** | Adequate | No new flow. Seven existing flows are updated (below). |

### INV-31 posture

- **The reconcile runs on the heartbeat thread**, the established world-mutation path, through `IAttributeSystem` like every other p=20 pool writer. It adds no thread, timer or background service, and no shared singleton state (`AttributeSystem` stays stateless).
- **The `MobBuilderSystem` and `AccountSystem` call sites** run where they already run (the admin `setmob` session path; the login/creation session path). Rerouting their writes through `IAttributeSystem` changes the code path, not the thread. `Hedron.Web` does not call `IMobBuilderSystem` (code-verified).
- **The sandbox reconcile** runs inside each run's confined `SandboxWorld`.
- **Logical read-modify-write races** between a session-thread pool write and the heartbeat reconcile are the same class already accepted for regen and combat. They are tracked by the existing 🟡 "World-state threading model" backlog entry. The reconcile only ever lowers toward a cap, so the worst-case interleave is a one-tick-late clamp, never a gain.

### Persistence opt-in audit (INV-14 / INV-22 / INV-23)

- **Level 1 (domains):**
  - players are persistent entities (unchanged);
  - live mobs and sandbox combatants are world content or sandbox-only, with no `PersistentEntity` (unchanged).
  
  No entity changes domain.
- **Level 2 (components):**
  - `PoolsComponent` stays `[Persistent]`, with its shape unchanged. Stored max remains the **base**, and stored current may legitimately exceed it. The effective cap is never stored (INV-24).
  - `AttributesComponent` is unchanged.
  - No component is added.
- **Level 3 (save sites):** no new `SaveEntityAsync`.
  - The creation fill happens inside `AccountSystem` **before** the existing category-(a) construction save by the initiator.
  - The reconcile's writes, like regen's, ride the periodic flush ([flow-04](../architecture/flows/flow-04-persistence-flush-cycle.md)).
  - The `setplayer` admin boundary save (category b) is unchanged.
- **Load:** no clamp on hydration (code-verified). Clamped reads plus the first post-boot reconcile give R10 exactly. The heartbeat arms only after full hydration (flow-01 step 7), so contributor-backing entities (equipped items) are present before the first reconcile.

## Flows introduced or modified

*(INV-17. Reference the flow; never reproduce its diagram here.)*

- **[Flow 16 — Heartbeat tick](../architecture/flows/flow-16-heartbeat-tick.md)** (WP1): add `PoolReconcileTickHandler (p=30)` as a step. The diagram already has 7 participants, so **merge the four p=20 tick handlers (ETH, CTH, ACTH, RTH) into one "p=20 tick handlers" participant** to stay within the ≤7-box rule ([flows/README.md](../architecture/flows/README.md)), and add the reconcile handler as its own participant. Add it as a step between regen and output flush. Update the summary sentence.
- **[Flow 21 — Effects journey](../architecture/flows/flow-21-effect-tick.md)** (WP1): expiry step. A dropped `<Pool>Max` modifier is visible at once through the clamped read; the p=30 reconcile writes the clamp back in the same tick.
- **[Flow 13 — Equipment journey](../architecture/flows/flow-13-wear-item.md)** (WP1): `remove` of pool-max gear → clamped read immediately, stored clamp on the next tick. `wear` raises the cap but not current (no refill).
- **[Flow 20 — Death & respawn journey](../architecture/flows/flow-20-mob-death-respawn.md)** (WP1): the player-respawn step restores `RespawnPoolPercent` of the **effective** cap after `RemoveImpermanent`. The mob-respawn step notes the template fill equals the effective cap (no modifier source at spawn).
- **[Flow 7 — Login journey](../architecture/flows/flow-07-login-character-flow.md)** (WP1): the creation step fills pools to the effective cap after the starting abilities. The returning-player leg notes there is no load clamp (R10).
- **[Flow 33 — Simulation run journey](../architecture/flows/flow-33-simulation-run.md)** (WP1): materialization fills at the effective cap after tier/residuals/abilities. The per-tick sequence gains step 5 (reconcile).
- **[Flow 17 — Combat journey](../architecture/flows/flow-17-kill-mob-combat-initiation.md)** (WP2): the round-pulse step's hit roll reads `Get(Body)`, and `AttackPower`/`Defense` derive from effective `Body`.
- **[Flow 32 — Ascension journey](../architecture/flows/flow-32-ascension.md)** (WP3): the baseline-fold step drops any "no combat effect" wording. The fold now reaches combat via `Get(Body)` and the `HpMax` cap.
- Index rows in [`flows/README.md`](../architecture/flows/README.md): no summary change is needed. Add "extended combat-effective-scores" to the *Slice introduced* cell of all eight modified flows (07, 13, 16, 17, 20, 21, 32, 33).

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md). The "Post." column maps each test to [Postconditions](#postconditions).)*

| # | Tier | Target | Asserts | Post. |
|---|---|---|---|---|
| T1 | system-unit | `StatSystemTests.Get_AttackPower_derives_from_effective_Body` | Body 10 with a `+6 Body` modifier → `Get(AttackPower)` = 8 (+ any AP modifier); odd effective Body uses integer division; with no Body modifier the value equals the old base | 1 |
| T2 | system-unit | `StatSystemTests.Get_Defense_derives_from_effective_Body` | same for `/4` plus the Defense modifier | 1 |
| T3 | system-unit | `StatSystemTests.Derived_scores_floor_at_zero` | a `−40 Body` modifier, or a large negative AP/Defense modifier → `Get(AttackPower)` = 0 and `Get(Defense)` = 0 (and `ExecuteRound` does not throw at AP 0) | 1 |
| T4 | system-unit | `StatSystemTests.Typed_getters_equal_Get` (theory) | each of the 8 typed getters equals `Get` of its score, with modifiers present | 3 |
| T5 | system-unit | `StatSystemTests.Pool_reads_delegate_to_attribute_cap` (theory × 4 pools) | `Get(<Pool>Max)` = `GetEffectiveMax`, including the floor-at-1 case; `Get(<Pool>Current)` = `GetCurrent` with stored above cap | 4, 5 |
| T6 | system-unit | `CombatSystemTests.Hit_roll_uses_effective_Body` | scripted `IRandom`: the roll misses with raw Body but hits with a `+Body` modifier (and the inverse with a debuff) | 2 |
| T7 | system-unit | `AttributeSystemTests.GetEffectiveMax_*` (theory × 4) | base + modifiers; floored at 1 when base > 0; floored at 0 when base is 0 | 4 |
| T8 | system-unit | `AttributeSystemTests.GetCurrent_is_clamped_read_*` (theory × 4) | stored above cap reads as cap; the typed `GetCurrentX` agrees; stored HP −5 reads −5 | 5 |
| T9 | system-unit | `AttributeSystemTests.SetCurrent_clamps_to_effective_cap` (theory × 4) + `RegenerationSystemTests.Regenerates_into_effective_max` | `+30 HpMax`: a write of 200 → 130; regen ticks take `100/130` to `130/130` (R5 pin); the HP floor is still `Death:HpFloor` | 6 |
| T10 | system-unit | `AttributeSystemTests.SetMax_clamps_current_to_effective_cap` (theory × 4) + `MobBuilderSystemTests.SetMobStat_pool_clamps_live_instance_to_effective_cap` | the base is written; current clamps to base+mods (not the raw base); a live mob instance with an `+HpMax` effect is clamped to the effective cap | 7 |
| T11 | system-unit | `AttributeSystemTests.RestoreToFraction_*` | `floor(cap × 0.25)`; 1.0 fills to the cap; the floor-at-1 cap with a 0.25 fraction → 0 | 8 |
| T12 | system-unit | `AttributeSystemTests.ReconcilePools_*` / `ReconcileAllPools_*` | it writes stored → cap only when stored > cap; never raises; negative HP untouched; returns changed / count | 9 |
| T13 | system-unit | `DeathSystemTests.Respawn_restores_percent_of_effective_max` / `Respawn_cap_excludes_stripped_timed_buff` | a permanent `+40 HpMax` modifier → 25% of 140 = 35; a timed `+40 HpMax` buff removed by `RemoveImpermanent` → 25% of 100 = 25 | 12 |
| T14 | system-unit | `AccountSystemTests.CreateCharacter_fills_pools_to_effective_cap` | the starting-ability config includes a passive `+HpMax` test ability (registry stub) → `CurrentHp` = `Get(HpMax)` | 14 |
| T15 | system-unit | `SimCombatantFactoryTests.Materialize_fills_pools_to_effective_cap` | a tier-1 combatant: `GetCurrent(Hp)` = 110 = `Get(HpMax)`; the residual effect for AP is still `target − raw body/2` | 13 |
| T16 | handler | `PoolReconcileTickHandlerTests` | on `HeartbeatTickEvent`, an entity with stored current above cap is written to cap (all four pools); `RecordingEventBus` records nothing; `Priority` = `HandlerPriority.Domain + 10`; the ctor has no `IDeathSystem`/`IEventBus` parameter | 10 |
| T17 | flow | `PoolClampFlowTests.Expired_buff_clamps_at_once_and_is_written_back_within_a_tick` | real bus with `EffectTickHandler` + `PoolReconcileTickHandler`; player at 125/130 with a timed `+30 HpMax` buff; one tick past expiry → `Get(HpCurrent)` = 100, stored `CurrentHp` = 100, not `Incapacitated`, no `PlayerIncapacitatedEvent` | 10 |
| T18 | flow | `PoolClampFlowTests.Remove_and_rewear_gives_no_free_refill` | wear `+HpMax` gear, set current to the effective max, remove → read clamped at once; tick → stored clamped; re-wear → current unchanged, max raised | 10 |
| T19 | flow | `PoolClampFlowTests.Respawn_after_death_uses_effective_cap` | a player with a `Body`/`HpMax` progression improvement dies via combat and respawns → HP = floor(25% × `Get(HpMax)`) | 12 |
| T20 | flow (WP3) | `CombatEffectiveScoresFlowTests.Progressed_character_beats_fresh_character_at_pinned_seed` | same mob template, same gear, seeded `IRandom` per arm, N = 200 fights each, pools restored to cap between fights; the progressed arm (a `Body` + `HpMax` improvement via `ProgressionComponent`) wins more fights; both win counts pinned. The fixture asserts the spawned mob reads `current == GetEffectiveMax` (Postcondition 15) | 15, 18 |
| T21 | flow (sandbox) | `CombatScenarioExecutorTests.Reconcile_runs_each_tick_and_preserves_determinism` | a combatant whose timed `+HpMax` effect expires mid-run ends with stored ≤ cap; two executions of the same seed produce identical `RunRecord`s | 11 |
| T22 | persistence round-trip | `RoundTripTests.Pool_current_above_raw_max_survives_round_trip` | a character with an `HpMax` progression improvement (+30) and stored `CurrentHp` 125 / `MaxHp` 100 → save → load → stored 125 and `Get(HpCurrent)` 125. Variant: the improvement removed before load → reads 100, and the first reconcile writes 100 | 16 |
| T23 | architecture-guard | `ArchitectureGuardTests.AttributeSystem_does_not_depend_on_IStatSystem` + `IStatSystem_declares_no_setters` | ctor-parameter reflection (precedent: `ProgressionSystem_does_not_depend_on_…`); `IStatSystem` has no `void`-returning members | 17 |
| T24 | architecture-guard (existing) | `CompositionRoot_resolves_all_commands_handlers_and_systems` | resolves `PoolReconcileTickHandler` and the re-wired `AccountSystem`/`MobBuilderSystem` (DI cycle check) | 17 |
| T28 | architecture-guard | `ArchitectureGuardTests.Heartbeat_handlers_are_subscribed` (or an extension of the composition guard) | the `HeartbeatTickEvent` subscriptions wired by the Program/CompositionRoot include `PoolReconcileTickHandler`, so a missing `Subscribe` line fails CI and not just runtime (spec review) | 10 |
| T25 | sim invariant (WP3, re-pin) | `SimulationInvariantTests` equal-cell ×2, one-band-higher | new golden pins with before/after in the Balance record; equal-cell verdict passes; one-band-higher verdict asserted as measured | 20 |
| T26 | regression (no edit) | `RelativePowerSystemTests.A_fresh_character_reads_…` (SR7), `PowerCommandTests` 650/712/400, ProgressionRate first-improvement pin | still green, unmodified | 19 |
| T27 | handler/command | `ScoreCommandTests.Score_reports_effective_attack_and_defense` | `ScoreDisplayMessage.AttackPower`/`Defense` equal `Get(AttackPower)`/`Get(Defense)` with a `+Body` modifier (field assertion, not prose) | 22 |

**Skipped (with reason):**
- **Exact sweep numbers beyond the CI pins.** They are recorded in the Balance record and the completed record, but heavy sweeps stay out of CI (the sim-2 seed brief).
- **Narration and formatter prose,** including the F1 `score` line text (presentation).
- **`MobTemplate.Apply`'s fill** as a separate unit test. It is unchanged, and Postcondition 15 is asserted in T20's fixture.
- **Heartbeat dispatch order beyond the priority constant.** `EventBus` priority ordering is already covered by its own tests; T16 pins the constant.
- **The typed wrapper one-liners on `IAttributeSystem`,** individually. Covered by the T7–T12 theories, which run through both the keyed and typed members.
- **Doc edits** (INV-27/28 is a review gate, not a test).

**Testability:** no gap. Combat chance resolves through the injected `IRandom` (`CombatSystem` already takes it), and the sandbox uses `SeededRandom`. The reconcile has no chance and no clock. No new seam is needed (INV-26).

## Balance record

*(R11–R12. Filled by WP1/WP2 (which goldens moved) and WP3 (sweeps). Copied into `roadmap/completed/combat-effective-scores.md` at close-out.)*

**Sweep set** (seed 2026; N = 2000 for sweeps, CI pins at their own N):

| # | Scenario | Purpose | Before | After |
|---|---|---|---|---|
| S1 | Reference build T2B2 vs T2B2 | equal-cell sanity (both sides tiered) | *WP3* | *WP3* |
| S2 | Reference build T3B1 vs T2B3 | the one-band-higher invariant (R12) | *WP3* | *WP3* |
| S3 | Reference build T1B1 vs T0B3, and T0B2 vs T0B1 | tier boundary at Tier 0 → 1; in-tier band (expected ≈ unchanged: band grants no power) | *WP3* | *WP3* |
| S4 | Fresh reference T0 vs each dev-world mob template (`MobTemplate` source) | Tier-0 content baseline for drops 6–8 (expected ≈ unchanged: no `Body` modifiers on either side) | *WP3* | *WP3* |
| S5 | `progressionRate`, Body track, 3 improvements, T2B2 vs T2B2 | anti-grind taper under the stronger effective-power read | *WP3* | *WP3* |

**CI goldens:**

| Test | Before | Expected | After |
|---|---|---|---|
| `EqualCell_ReferenceBuild_WinRateWithinTolerance` (+ extended-signature twin) | 106/94/0 | **moves** (both sides gain the tier `Body` → AP/Def and full-HP fill; symmetric, so it stays near 50% and passes) | *WP3* |
| `OneBandHigher_ReferenceBuild_WinRate_PinnedPendingBalanceTuning` | 106/94/0, verdict fails the 65% floor | **moves** substantially toward side A; the verdict may flip to pass | *WP3* |
| `ProgressionRate_EqualPower…_KillsToFirstImprovement` | 200 / 10 / 12 / 10.48 | **holds** (ratio is 1.0 until the first improvement; analytical executor) | *WP3 verify* |
| SR7 midpoint (`RelativePowerSystemTests`) | passes | **holds** (fresh, Tier 0, no `Body` modifiers) | *WP3 verify* |
| `PowerCommandTests` 650/712/400 | — | **holds by construction** (stub `IStatSystem`) | *WP3 verify* |
| Tier > 0 goldens in `SimCombatantFactoryTests`/`CombatScenarioExecutorTests`/`SimulationRunnerTests` | per test | may move (fill at cap) | *WP1/WP3* |

## Design notes

*(Durable seam rationale. It survives disintegration into the slice's `completed/` record and the living docs.)*

- **"Effective" means effective, at one seam.** `IStatSystem` is the only place a raw attribute becomes an effective one.
  - The typed getters (`GetEffectiveBody` and the rest) become thin wrappers over `Get`, which is what [stat-system.md](../features/character-stats/stat-system.md) already documents (INV-15: code moves to the doc).
  - The *base* of the derived scores is computed from **effective** `Body` inside `StatSystem`: `AttackPower` = `Get(Body)/2` and `Defense` = `Get(Body)/4`, each plus its own modifiers.
  - Combat's hit roll reads `Get(Body)`.
  - **Why here:** the gap was never in combat. Combat already asked `IStatSystem`, and the getters lied, reading raw `AttributesComponent` values under an "Effective" name. Fixing the seam fixes every consumer at once: combat, `score`, the prompt, the relative-power read and the sandbox. Patching combat alone would leave the misleading getters in place for the next caller.
  - **Acyclic:** no contributor's backing system reads `IStatSystem` ([progression-system.md](../features/progression/progression-system.md#anti-grind-ratio-is-supplied-by-the-caller), [ascension-system.md](../features/progression/ascension-system.md)). Folding `Get(Body)` into the derived scores adds no edge to the `IStatSystem → IEffectSystem → contributors` chain.
- **The pool cap belongs to the pool write seam, keyed by `ResourceType`.** `IAttributeSystem` already owns the pools and already clamps `SetCurrentX` to base plus `IEffectSystem.GetModifiers`, a legal downward dependency. It becomes the **single owner of "a pool's effective cap"**, generalized over all four pools:
  - **The cap.** Effective max = base + modifiers, floored at 1 when base > 0 (R8).
  - **The observable current.** Current = `min(stored, cap)`. It is clamped on read, so a dropped max is visible at once.
  - **Restore to a fraction of the cap.** Spawn fill uses 1.0 and respawn uses `RespawnPoolPercent`.
  - **Reconcile.** Write the clamp back to storage.
  
  `IStatSystem.Get(<Pool>Max/<Pool>Current)` **delegates** to this seam instead of re-adding modifiers itself, so display, combat and writes share one definition and cannot drift (R9).
  - **Why `IAttributeSystem`, not `IStatSystem`:** the cap is a rule about the data being written (mechanism with its data). `IStatSystem` stays setter-free by design.
  - **Why keyed, not per-pool:** the per-pool `SetCurrentX` / `GetMaxX` method family is already copied four times with the same clamp logic; that crosses the INV-19 ≥3 bar. Keying the new verbs by `ResourceType` (the documented "expandable seam") means a fifth pool joins as data. The existing typed setters can stay as wrappers.
- **Clamp-down is a clamped read plus a heartbeat reconcile, not eager per-source hooks.** A pool's max can drop through:
  - effect expiry or removal;
  - unequip;
  - losing a passive;
  - a tier change;
  - admin edits;
  - future auras and areas.
  
  Most of these are **contributors**, which are pulled on read and deliberately have no "changed" signal (INV-24), so eager reconciliation would mean every source remembering to call it. That is the scattered-call-site trap.
  - **The mechanism:** reads are clamped (above), so the drop is observable immediately. A heartbeat pass over pool-bearing entities writes the clamp back, so stored overflow never outlives one tick.
  - **What it rules out:** the "remove the HP buff, put it back, get free HP" overflow (resolved decision, user 2026-11-01). Swapping gear off and back on inside one tick gains nothing, because the entity ends where it began.
  - **R10 falls out for free:** a loaded current above the raw max survives exactly when the effective cap on load covers it.
- **Spawn fill and respawn read the cap, not the raw max.** Every "start full" or "restore N%" path goes through the restore-to-fraction verb:
  - live mob spawn;
  - sandbox combatant materialization, *after* its effects and tier are attached;
  - character creation, *after* starter abilities are learned;
  - respawn.
  
  This is the other half of why the sim saw no tier effect: a tier-1 combatant's `HpMax` baseline raised its max but it spawned at the raw max.

### Planner resolutions (code-verified)

*(These settle the brief's planner-tier open questions; recorded 2026-11-01.)*

- **Reconcile cost: no skip optimization.**
  - The pass costs one `GetModifiers` per pool per `PoolsComponent` entity per tick. That is the same order as the already-accepted `RegenerationSystem` sweep, which calls the four `SetCurrentX` (each one a `GetModifiers`) for every resting entity every tick and every idle entity every third tick.
  - A cheap "skip if stored ≤ base" test is **unsound**: a debuff can push the cap *below* base.
  - "Skip entities with no modifiers" is **not cheaply knowable**: contributors are pulled on read (INV-24).
  - At Phase 5 scale (dev-world: tens of pool-bearing entities) the cost is negligible. The LINQ inside `GetModifiers` is already tracked by the 🟡 "Performance: LINQ in hot paths" backlog entry, so no new entry is needed. If profiling ever shows the pass hot, the remedy is a per-entity early exit after computing the cap once per pool, not a source hook.
- **Live mob spawn fill: no code change; Postcondition 15 holds by construction.**
  - Live mob `PoolsComponent` is created in `MobTemplate.Apply` (inside `TemplateRegistry.Spawn`, reached from `SpawnSystem.TrySpawn` and the admin `spawn` command), filled at the template max.
  - At that moment a live mob has **no modifier source**: no `EffectsComponent`, no `AbilitiesComponent`, no `AscensionComponent` (live mob tier rides `MobDataComponent.Tier` for the oracle only), no `EquipmentComponent` (archetype: "planned for mobs but not yet added"), and no progression. So the template max **is** the effective cap.
  - Routing the fill through `RestoreToFraction` would mean adding an `IAttributeSystem` dependency to every spawn site (`SpawnSystem`, `SpawnCommand`), since `IEntityTemplate.Apply` only receives `EntityService`. That is the scattered-site shape, for zero behavior today.
  - **Trigger recorded:** the slice that first gives a live mob a modifier source at spawn (mob tier folding or mob gear, both Phase 6) routes the fill through `RestoreToFraction` (forward note on the backlog entry, WP3).
  - *Cheap to veto:* if the user prefers the repoint now, it is a two-site change (`SpawnSystem.TrySpawn` + `SpawnCommand`) in WP1.
- **Typed-getter callers.**
  - Outside `StatSystem`, production code calls only `GetEffectiveBody`, in `CombatSystem`'s hit roll (repointed to `Get(Body)`). No production code calls `GetEffectiveAttackPower`/`GetEffectiveDefense`; `SimCombatantFactory` computes `body/2` itself.
  - The test callers are `StatSystemTests` (base-only pins), `EquipmentEffectContributorTests` (asserts the getter *excludes* gear), and four test stubs implementing `IStatSystem` (PowerCommandTests, ScoreWalletFlowTests, PromptComposerSystemTests, RelativePowerSystemTests).
  - **Decision: the getters survive as thin wrappers over `Get`** (the approved "typed getters wrap `Get`" design and the stat-system.md contract). `GetEffectiveAttackPower`/`GetEffectiveDefense` therefore become gear- and modifier-inclusive. The tests pinning base-only semantics are rewritten as a deliberate semantic change (WP2 step 3).
  - **Rejected:** removing them. That is interface churn across five stubs for no caller benefit.
  - **Rejected:** keeping them base-only. A base-only "Effective" getter is the misleading name this slice exists to remove.
- **No load-time pool clamp exists.**
  - `PoolsComponent` is restored verbatim by `PersistenceSystem.LoadAllAsync`.
  - `CharacterHydrationHandler` only attaches a default `PoolsComponent` when missing (a migration guard), and never touches values.
  - No other code reads or writes pool values on hydration.
  
  R10 therefore needs only the clamped read, pinned by T22.
- **Clamped typed `GetCurrentX`.** The approved "observable current" is implemented on the existing typed getters, not only on the new keyed verb. Every current pool reader therefore sees the clamped value in the ≤ 1-tick window before reconcile: `AbilitySystem` affordability and spend, `RegenerationSystem`, `EffectTickHandler`, `DeathTickHandler`, `AffectCommand` and `CooldownFirstPolicy`. Otherwise an ability's cost check could spend stored overflow. Negative HP is unaffected (cap ≥ 1).
- **Derived-score floor.** The edge case says "attack power and defense floor at their existing minimums. Never negative." Code check: no explicit floor existed. `ExecuteRound` calls `_random.Next(1, attackPower + 2)`, which **throws** for AP ≤ −2, a case unreachable before only because raw `Body` and gear are non-negative. Effective `Body` under a debuff can go negative, so `Get(AttackPower)` and `Get(Defense)` floor at 0. This preserves the existing minimum damage of 1. `Get(Body)` itself is not floored (the hit roll tolerates a negative term).
- **Sim residual stays against raw `Body`.** `SimCombatantFactory.ApplyComputedResidual` computes `target − rawBody/2` (and `/4`). This is kept deliberately. The residual represents **gear** on top of the snapshot's base derivation, and the tier's `Body` contribution now enters through the derived base. Rebasing the residual on effective `Body` would cancel the tier's effect on AP/Def and re-open the gap in the sandbox.
- **Tier-projection asymmetry (recorded for Phase 6, no action).** `PowerBudgetMath.Estimate(snapshot, tier)` adds the tier baseline to the tracked scores (`Body`, `HpMax`) only. It does not re-derive `AttackPower`/`Defense` from the higher `Body`. A live tiered character's `GetEffectivePower` (which snapshots `Get(AttackPower)`/`Get(Defense)`) therefore now exceeds the standards' `TierReferencePower` for its tier. At Tier 0 this is moot (no baseline), and the brief accepted "the oracle reads more for a progressed entity". It is noted on the tier-tuning backlog entry (WP3) because Phase 6's band classification of tiered players inherits it.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Seams and homes

| Seam | Kind | Home / layer | Disposition |
|---|---|---|---|
| Effective `Body` → derived scores; typed getters wrap `Get` | Change inside an existing domain system | `Core/Modules/Stats/Systems/StatSystem.cs` | **Build now** |
| Combat hit roll reads `Get(Body)` | Caller repoint | `CombatSystem.ExecuteRound` | **Build now** |
| Pool cap / clamped current / restore-to-fraction / reconcile, keyed by `ResourceType` | New methods on an existing domain system | `IAttributeSystem` (`Core/Modules/Attributes`); `IStatSystem.Get(<Pool>…)` delegates to it | **Build now** (general over 4 pools) |
| `SetMaxX` and `MobBuilderSystem` clamp to the effective cap, not the raw max | Caller repoint | `AttributeSystem`, `MobBuilderSystem` | **Build now** |
| Pool reconcile tick | New heartbeat handler (Initiator path) | `Core/Modules/Attributes/Handlers/`, on `HeartbeatTickEvent` | **Build now** |
| Spawn fill at the effective cap | Caller repoint | Mob spawn path, `SimCombatantFactory.Materialize`, `AccountSystem` character creation | **Build now** |
| Respawn at % of the effective cap | Caller repoint | `DeathSystem.Respawn` | **Build now** |
| Sandbox reconcile parity | Sandbox tick loop calls the same reconcile | `Core/Modules/Simulation` | **Build now** (live/sandbox parity) |
| Ability/effect power on effective stats | — | — | **Defer**: [backlog](../roadmap/backlog.md) entry added |
| Tier tuning | — | — | **Defer**: existing backlog entry, narrowed |
| Real-combat `progressionRate` sim | — | — | **Defer**: remains a simulation-backlog decision |

### Family dispositions

- **Pools:** general now. All four pools go through one `ResourceType`-keyed cap. A future pool is an enum entry.
- **Effective scores:** the whole `ScoreId` family already goes through `Get`. This slice removes the last raw shortcuts (the typed getters and the derived-score base). No new family.
- **Clamp-down sources:** general by construction. The reconcile tick covers any modifier source, current or future, without per-source code.

### Observers, contributors, ordering

- **No new events.** The clamp-down is a silent correction: no `PoolChangedEvent`, and no narration of "your max fell". A future "pool changed" fact would want a central publisher. The reconcile handler is such a point for clamp-downs only, and nothing needs that fact today (noted, not built).
- **No new contributor.** This slice only *consumes* contributor-folded values (INV-24 unchanged).
- **Ordering (INV-7):**
  - Clamped reads make the reconcile tick's priority **non-load-bearing for correctness**: every consumer sees the clamped value regardless.
  - Run it **after** `EffectTickHandler`, so a buff that expires this tick is written back in the same tick, and before the persistence flush timer's next sweep. Use a late priority on `HeartbeatTickEvent`; the planner picks the exact `HandlerPriority`.
  - It must not run *between* a damage write and the death check that reads it. Those happen inside one handler (`CombatTickHandler`, `DeathTickHandler`), so a separate handler can't interleave.
- **The clamp never kills.** The reconcile only lowers current toward a cap ≥ 1, so it never crosses an HP threshold and never calls `IDeathSystem.OnHpChanged`. It can't trigger an incapacitation (edge cases).

### Balance consequences (deliberate; `balance-tuning` skill)

- **Combat now responds to `Body` improvements, `+Body` buffs and the tier baseline.** Combatants with `HpMax` modifiers now spawn full and respawn at a % of the effective max.
- **The oracle reads more for a progressed entity.** `GetEffectivePower` snapshots `Get(Body)`, `Get(AttackPower)` and `Get(Defense)`, and the last two now rise with `Body`. So a `Body`-improved or tiered character's effective power rises *more* than before. Consequences:
  - anti-grind tapers progression faster as a character grows;
  - `consider` labels shift for buffed, progressed or tiered characters;
  - the sandbox progression sweep's gaps widen further.
  
  This is the honest number now that combat agrees with it. A fresh character carries no `Body` modifiers, so its readings, the SR7 midpoints, and the `power` 650/712/400 goldens (if their fixtures carry no `Body` modifiers) are expected to stand. The planner verifies.
- **Expected to move and need deliberate re-pins:**
  - the one-band-higher invariant (53%);
  - the progression sweep goldens;
  - any combat or flow test whose fixtures carry `Body`/`HpMax` modifiers or tier.
- **Ability-strike mitigation shifts too.** `CombatSystem.ResolveAbilityStrike` subtracts `Get(defender, Defense)`, which now rises with effective `Body`. Ability *power* stays base (out of scope), but ability damage *taken* by a `Body`-modified defender drops. The S1–S3 sweeps must include ability-kit combatants (a `RoundRobinPolicy`/`CooldownFirstPolicy` side) so this shift is measured, not just melee (spec review).
- **Catalog maintenance:** [balance.md](../design/balance.md) Known gaps narrows to tier tuning. The `CombatSystem` knobs row is unchanged (same formula shape).

### Invariants in tension

- **INV-1 / INV-2:**
  - `AttributeSystem → IEffectSystem` is the existing legal downward edge. The new cap verbs add no dependency.
  - `DeathSystem`, the spawn path, `AccountSystem` and `SimCombatantFactory` call `IAttributeSystem`, as they already do.
  - **`IStatSystem` must not gain setters, and `IAttributeSystem` must not depend on `IStatSystem`.** That would invert the stat → attribute edge. An architecture-guard test is advisable.
- **INV-24:** contributions stay pulled on read. The reconcile writes the *clamped current*, a pool's own state, never a materialized contributor value. Stored max stays the base; the effective max is never cached.
- **INV-5:** the reconcile handler is a handler on `HeartbeatTickEvent` that calls a system and publishes nothing. It needs no exception. *(Corrected at spec review from an "INV-10 no-chain initiator" framing: it is a handler, not an initiator.)*
- **INV-31:** the reconcile runs on the heartbeat path, the established mutation path. It adds no new thread and doesn't widen the `ComponentRepository` exposure. Sandbox runs reconcile inside their own confined world.
- **INV-26:** no chance involved. The sandbox reconcile keeps the per-run determinism (zero `IRandom` draws).
- **INV-25:**
  - system tests for the `StatSystem` derivation and the `AttributeSystem` cap, clamped read, restore and reconcile (all four pools, floor at 1);
  - a handler test for the reconcile tick;
  - flow tests: a progressed character beats a fresh one at a pinned seed (R13a); respawn at % of the effective max; expired buff → clamped at once and written back within a tick; remove-and-rewear gives no free refill;
  - a persistence test: current above the raw max survives a round-trip (R10);
  - deliberate re-pins with a sweep (R11–R12).
- **INV-15 / INV-20 / INV-27, doc accuracy (R14 plus these):**
  - [stat-system.md](../features/character-stats/stat-system.md): the typed getters now really wrap `Get`; the derived base is from effective `Body`.
  - [attribute-system.md](../features/character-stats/attribute-system.md) and [regeneration-system.md](../features/character-stats/regeneration-system.md): the clamp is to the **effective** cap. The docs currently say `[0, max]`.
  - Correct the backlog and [simulation-engine.md](../features/simulation/simulation-engine.md) claim that "the combat HP/death check reads raw pools". The real gaps were the derived-score base, spawn fill and respawn.
  - [combat-system.md](../features/combat/combat-system.md): the hit-roll term.
  - [death-system.md](../features/combat/death-system.md): respawn uses the effective max.
  - [relative-power-system.md](../features/progression/relative-power-system.md): drop the "honesty is bounded by combat" caveat.
  - The `balance-tuning` skill's mention of the calibration gap, if any.
- **INV-16:** catalog rows for `StatSystem`, `AttributeSystem` and the new reconcile handler.
- **INV-17, flows touched:**
  - [flow-16](../architecture/flows/flow-16-heartbeat-tick.md): the new reconcile handler;
  - [flow-17](../architecture/flows/flow-17-kill-mob-combat-initiation.md): the hit roll;
  - [flow-20](../architecture/flows/flow-20-mob-death-respawn.md): spawn fill;
  - [flow-21](../architecture/flows/flow-21-effect-tick.md): expiry → reconcile;
  - [flow-13](../architecture/flows/flow-13-wear-item.md): unequip clamp, observed;
  - [flow-07](../architecture/flows/flow-07-login-character-flow.md): creation fill and load;
  - [flow-33](../architecture/flows/flow-33-simulation-run.md): sandbox fill and reconcile;
  - the player death/respawn trace wherever it lives.
- **INV-18:** no new authored state, so nothing to author. Inspection is unchanged (`score`, prompt and `power` read effective values).

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-11-01. Do not relitigate.)*

1. **Clamp-down mechanism:** a clamped read plus a heartbeat reconcile write-back. Rejected: eager per-source reconcile (scattered call sites, and contributors have no hook); a clamped read alone (stored overflow lets a re-applied buff refill current for free).
2. **Ability power backlog entry:** filed now in [backlog.md](../roadmap/backlog.md). Not left to ship.

**Advisor calls** *(cheap to veto at planning)*:
- the fix lives in `StatSystem`'s derivation and typed getters, not in combat;
- the pool cap seam lives on `IAttributeSystem`, keyed by `ResourceType`, and `IStatSystem` delegates its pool reads to it;
- no `PoolChangedEvent`;
- the sandbox loop runs the same reconcile, for parity.

## Open questions

- ~~**USER FORK F1: `score` has no attack/defense lines today.**~~ **Resolved by the user (2026-11-01): (a).** `score` gains `Attack`/`Defense` lines from `Get` (WP2 step 4, T27, Postcondition 22). Rejected: narrowing R9 to pools.
- **The real-combat progression-rate sim.** With this gap closed, the `progressionRate` executor could drive real fights per kill. Whether and when stays a separate decision ([simulation-engine.md](../features/simulation/simulation-engine.md)). *(User, later.)*
- **Resolved by the planner (code-verified; see [Planner resolutions](#planner-resolutions-code-verified)):**
  - ~~reconcile cost and scope~~: no skip; same order as the regen sweep;
  - ~~mob spawn path location~~: `MobTemplate.Apply`, with no modifier source at fill, so it holds by construction; a forward trigger is recorded; *cheap to veto*;
  - ~~typed-getter callers~~: only the hit roll used `GetEffectiveBody`; all typed getters survive as `Get` wrappers;
  - ~~load-time clamp~~: none exists;
  - plus the derived-score floor at 0 (the requirements edge case; the formula would throw for AP ≤ −2).
- **Resolved earlier:**
  - ~~where the cap is enforced~~: on `IAttributeSystem`; see Design notes;
  - ~~how clamp-down triggers~~: resolved decision 1;
  - ~~persistence on load~~: clamped reads make R10 hold; pinned by T22;
  - ~~ability power~~: backlogged.

## Related

- Program: [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md) (slice 5b, R21, R26, R30).
- Discovery: [`completed/relative-power-consider.md`](../roadmap/completed/relative-power-consider.md#decisions) · [`completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md) (the sim-2 calibration-gap record).
- Precedents:
  - [`completed/wearable-equipment-expansion.md`](../roadmap/completed/wearable-equipment-expansion.md) (gear onto `Get(AttackPower|Defense)` via the contributor seam);
  - [`completed/slice-9d-stat-resource-substrate.md`](../roadmap/completed/slice-9d-stat-resource-substrate.md) (`ResourceType` as the expandable seam).
- Features:
  - [stat-system.md](../features/character-stats/stat-system.md) · [attribute-system.md](../features/character-stats/attribute-system.md) · [regeneration-system.md](../features/character-stats/regeneration-system.md);
  - [combat-system.md](../features/combat/combat-system.md) · [death-system.md](../features/combat/death-system.md);
  - [relative-power-system.md](../features/progression/relative-power-system.md) · [ascension-system.md](../features/progression/ascension-system.md);
  - [simulation-engine.md](../features/simulation/simulation-engine.md).
- Balance: [`balance.md`](../design/balance.md) · [`balance-tuning` skill](../../.claude/skills/balance-tuning/SKILL.md).
- Backlog: "Ascension tier baseline has no real combat effect" (narrowed here) · "Ability/effect power on effective stats" (deferred here) · "World-state threading model" (INV-31 context).
- Parallel in-flight: [`playtest-world-substrate.md`](playtest-world-substrate.md). If PT-1 merges first, the reconcile handler subscribes in `CompositionRoot.SubscribeGameplayHandlers`, not `Program.cs`.
