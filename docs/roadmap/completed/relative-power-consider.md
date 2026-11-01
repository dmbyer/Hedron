# Relative power + `consider` — Phase 5 slice 5 (completed)

> Implemented on branch `claude/refine-slice-5-relative-power-5fc409`, 2026-10-31. Living docs: [`progression`](../../features/progression/progression.md) ([`relative-power-system`](../../features/progression/relative-power-system.md), [`progression-system`](../../features/progression/progression-system.md), [`power-budget-system`](../../features/progression/power-budget-system.md)), [flow-17](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) (`consider` leg), [flow-31](../../architecture/flows/flow-31-progression-award.md) (ratio gather), [flow-33](../../architecture/flows/flow-33-simulation-run.md) (sandbox gather), [`balance.md`](../../design/balance.md) (cut-points, Known gaps). Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (R20–R22).

## Outcome

Players can now type `consider <mob>` (alias `con`) to get one descriptive line telling them how dangerous a mob is relative to them: *trivial*, *easy*, *even*, *dangerous* or *deadly*. The line never contains a number, tier or band. A new domain read, `IRelativePowerSystem`, owns that relationship. It compares effective power on both sides (gear, progression, effects, tier) through the core power oracle, and it owns the ratio → label rule, where *trivial* is exactly `AntiGrindFloorRatio`.

The same read now feeds anti-grind, a deliberate balance change:
- `ProgressionSystem` lost its raw-attribute power estimate and its oracle dependency. The award methods take the opponent ratio as an input, gathered by `AdvancementHandler` and the sandbox's `ProgressionScenarioExecutor`.
- The ability-use and damage-taken rules now apply anti-grind whenever an opponent is named. `AbilityActivatedEvent` gained `IsOffensive` so the handler can tell an attack from a self or ally use.

The admin `power` command moved onto the read and gained a `vs you: ratio … → label` line for mob targets. A thin `MobConsideredEvent` observation fact is published on each successful label, for the playtest recorder.

## Behavior digest

- **Requirements (as confirmed).**
  - **SR1–SR2.** `consider` resolves a mob in the room, prefix-only like `kill`, and prints one fixed descriptive line per label. It never shows numbers, tier or band.
  - **SR3–SR4.** The label uses effective power, not current HP, and is recomputed on every call.
  - **SR5.** *Trivial* starts exactly at the anti-grind floor: one constant, one meaning.
  - **SR6.** `consider` changes no state, spends no round, resource or cooldown, and works in combat. It publishes only `MobConsideredEvent`, on a successful label.
  - **SR7.** A fresh character reads a (T0, B1) midpoint mob as *even* or easier, and a (T0, B3) midpoint mob as *dangerous* or worse.
  - **SR8–SR12.** The kill, damage-taken (attacker) and offensive ability-use (target) awards apply the effective-power ratio. Self, ally and untargeted uses apply none. A below-floor ratio, or a named but unreadable opponent, is ineligible with zero draws. Live and sandbox kills compute identical ratios.
  - **SR13.** No new narration.
  - **SR14–SR15.** `power <mob>` adds a relation line. Self and item output is unchanged.
  - **SR16–SR17.** `help consider` works, and the engine `combat` topic points to `consider`.
- **Postconditions** (the coverage contract):
  - one label line with no digits;
  - trivial ⇔ ineligible award;
  - the SR7 midpoints;
  - kill-mirrored refusals (untargetable → "not something you can fight"; `self`/`me`/items → "You can only size up creatures."; others → "You don't see that here."; none → "Consider whom?");
  - one event on success, none otherwise;
  - ratio directions per rule;
  - unreadable → ineligible;
  - live/sandbox parity at T0 and T1;
  - `ProgressionSystem` free of `IStatSystem`/`IEffectSystem`/`IRelativePowerSystem`/`IPowerBudgetSystem`;
  - the `power` 650/712/400 goldens unchanged;
  - a deliberate re-pin with a sweep.
- **Main flow.**
  1. **`consider`.** The command resolves the token through Combat's room lookup, then checks `CanBeAttacked`. It calls `IRelativePowerSystem.Relate(player, mob)`, prints `Phrase(label, name)`, and publishes `MobConsideredEvent`.
  2. **Awards.** `AdvancementHandler` maps each trigger to an opponent: kill → victim; `IsOffensive` with a target → target; damage → attacker. It passes `Relate(...).Ratio` into `AwardCombatExperience(killer, victim, ratio)` or `UseAwardContext.OpponentPowerRatio`. `ProgressionSystem` applies `ratio < floor ? ineligible : min(ratio, cap)`. A null ratio means no opponent.
  3. **Sandbox.** The executor gathers the ratio once per kill, so the subject's grown power is seen.

## Shipped pieces

| Surface | Location |
|---|---|
| `RelativePowerLabel` enum (Trivial…Deadly) | `Core/Modules/Progression/RelativePowerLabel.cs` |
| `IRelativePowerSystem` / `RelativePowerSystem`, with `GetEffectivePower`, both `Relate` overloads and `Classify`; `RelativePowerReading` with `Unreadable` | `Core/Modules/Progression/Systems/` |
| Cut-points `RelativeEvenFloorRatio` 0.85, `RelativeDangerousFloorRatio` 1.12, `RelativeDeadlyFloorRatio` 1.30 | `Core/Modules/Progression/ProgressionConstants.cs` |
| `AbilityUse` / `DamageTaken` rows flagged `AppliesAntiGrindPowerRatio` | `ProgressionConstants.Rules` |
| `AwardCombatExperience(killer, victim, ratio)`; `UseAwardContext.OpponentPowerRatio` (`double?`, which replaces `OpponentEntityId`); raw proxy and oracle dependency removed | `Core/Modules/Progression/Systems/{IProgressionSystem,ProgressionSystem}.cs` |
| `AdvancementHandler` gathers the ratio (it now takes `IRelativePowerSystem`) | `Core/Modules/Progression/Handlers/AdvancementHandler.cs` |
| `AbilityActivatedEvent.IsOffensive`, set by the pipeline and by `useability` | `Core/Modules/Abilities/` |
| `SandboxWorld.RelativePower`, the factory wiring, and the executor's per-kill gather | `Core/Modules/Simulation/Systems/` |
| `ConsiderCommand` (`consider` / `con`) | `Core/Modules/Progression/Commands/ConsiderCommand.cs` |
| `MobConsideredEvent(PlayerEntityId, MobBlueprintId, DangerLabel)` | `Core/Modules/Progression/Events/MobConsideredEvent.cs` |
| `PowerCommand` on the read, plus the relation line; `PowerReadoutMessage.RelationRatio`/`RelationLabel` and the formatter | `Core/Modules/BalanceInspection/Commands/PowerCommand.cs`, `Core/Output/` |
| Engine help line pointing at `consider` | `content/help/combat.yaml` |
| New design doc | `docs/features/progression/relative-power-system.md` |

## Tests shipped

The suite went from 1796 to 1868 tests, and `dotnet test` is green (INV-25).
- **`RelativePowerSystemTests`** (system tier):
  - the effective-power goldens;
  - gear, progression and buffs each raising power;
  - a wounded mob reading the same as a fresh one;
  - inclusive cut-point boundaries;
  - trivial ⇔ below the floor;
  - the unreadable cases;
  - the SR7 midpoint fixture, computed from the oracle;
  - the cut-point ordering guard;
  - the constructor-shape guard;
  - the cross-system "a trivial reading is exactly a zero-award, zero-draw kill" test.
- **`ProgressionSystemTests`:**
  - the ratio-input kill and use rows;
  - zero draws below the floor, for an unreadable 0.0 ratio, and for NaN/±∞;
  - a null ratio reproducing today's draw sequence.
- **`AdvancementHandlerTests`:** a trivial victim, attacker or offensive target gives nothing; a destroyed target is ineligible; self and ally uses apply no ratio.
- **`ProgressionAwardFlowTests`** (flow tier):
  - gear lowering kill XP;
  - a killing-blow `kick` gathering the ratio before the strike;
  - self-cast `empower` publishing `IsOffensive = false`;
  - the live/sandbox parity theory at T0 and T1 (15 kills, seed 2026).
- **`ProgressionScenarioExecutorTests`:** the ratio is regathered per kill.
- **`ProgressionGuardTests`** (architecture tier): the four forbidden dependencies, and the registry tier step and tracked scores mirroring `AscensionConstants`.
- **`PowerCommandTests`:** the goldens are unchanged; added tests cover the relation line and an unreadable invoker.
- **`ConsiderCommandTests`** (20 cases): every label line plus its event; no digits; gear changing the label; every refusal path with an empty bus; usable in combat with no state change; the command metadata.
- **DevWorld smoke**, run on a scratch copy of the content because the committed dev-world has no mobs yet:
  - `consider shopkeeper` → "Shopkeeper is not something you can fight.";
  - `con dummy` → "Training dummy looks like an even fight.";
  - `help consider` and `help combat` resolve.

## Decisions

- **The relationship is a read, and the label belongs to the read.** Ratio → label is a game rule with two consumers (`consider`, `power`), so it lives in `IRelativePowerSystem.Classify`. Label → phrase is presentation and lives in `ConsiderCommand`. What the ratio *does* to an award (floor → ineligible, cap) stays in `ProgressionSystem`. The *trivial* boundary **is** `AntiGrindFloorRatio`, never a copy.
- **`ProgressionSystem` stays stat-free; callers gather.** Reading `IStatSystem` inside `ProgressionSystem` would close the `IStatSystem → IEffectSystem → ProgressionEffectContributor → IProgressionSystem` DI cycle. The orchestration boundary (the handler and the sandbox executor) gathers the ratio instead, and a parity test pins live against sandbox. A shared "kill award" façade was rejected as an unneeded layer for two callers.
- **Absent vs. unreadable vs. present.**
  - A `null` ratio means no opponent, so the award is as today.
  - `0.0` means the opponent was named but unreadable, which is ineligible with zero draws. It never falls back to ratio-free XP, which would let a finishing `kick` on an outgrown mob pay full rank XP.
  - Readability is an explicit `HasComponent<AttributesComponent>` check plus subject power > 0, because `AttributeSystem` returns reference defaults for missing components.
- **`IsOffensive` is a fact on the event.** A self-cast ability puts the actor in `TargetEntityId`, so the target alone couldn't distinguish an attack. The publisher computes the fact once, from `IAbilitySystem.IsOffensive`.
- **Tier source, without `IAscensionSystem`.** The oracle's tier argument is `MobDataComponent.Tier` (0 when absent). Player and sandbox tier ride `IStatSystem` through the ascension contributor.
  - **Parity caveat:** the registry tunables and `AscensionConstants` must agree. A guard test checks the defaults, and the standards store warns on drift.
- **The power overload is built now.** `Relate(subject, int targetPower)` is shaped for cell-anchor consumers: tiered-overworld entrance telegraphs and banners, and mob-AI threat. None is built.
- **`MobConsideredEvent` is an observation fact.** It honours the playtest run-mode plan's resolved decision 4, and SR6 was reworded to fit. It fires only on a successful label, carries no power values, and no gameplay handler subscribes.
- **User decisions (2026-10-31):**
  - five labels;
  - label-only descriptive output;
  - untargetable mobs read as "can't be fought";
  - the `power` relation line, for mobs only;
  - targets handled like `kill`;
  - the 0.25 floor kept;
  - cut-points 0.25 / 0.85 / 1.12 / 1.30, so B2 reads *dangerous* (1.16 rejected);
  - the combat gap split out as slice 5b.
- **Combat ignores contributor-folded `Body`/`HpMax`.** So until slice 5b (`combat-effective-scores`) lands, effective-power `consider` and anti-grind overstate a progressed character. The read is correct as built and needs no change when combat is fixed.

## Deviations / Follow-ups

- **Balance re-pin result.** No pinned golden value moved. Four fixtures were corrected instead, because HpMax now counts toward power: three flow-test kill fixtures became a wounded peer (MaxHp 100, CurrentHp 1), and the executor's "below the floor" victim's HP went 1000 → 1.
  - **Sweep** (T2B2 vs T2B2 equal-power, Body track, seed 2026, N 200). Mean kills to reach:

    | Target | Before | After |
    |---|---|---|
    | 1 improvement | 10.48 | 10.48 |
    | 3 improvements | 20.51 | 20.955 |
    | 5 improvements | 30.47 | 32.21 |
    | 10 improvements | 55.515 | **64.04** |

    Gaps between improvements now widen as the subject outgrows its victim. The kill-only sweep cannot see the use-row anti-grind change (balance.md Known gaps).
- **`PlainMessage`, not `ConsiderNarrationMessage`.** No `NarrationMessage` base exists yet (resolved decision 9's fallback).
- **The smoke test ran on a scratch content copy,** because the committed dev-world has no mobs until content drop 6.
- **`IPowerBudgetSystem`'s docstring and the `PowerBudgetSystem` catalog row** were repointed outside the work-package file lists. Pre-existing stale flow-24 wording ("`AbilityStrikeHandler` publishes `CombatRoundEvent`") was fixed at the code gate.
- **Optional hardening not taken.** Mapping attacker id 0 to a null ratio in `AwardDamageTakenAsync` was skipped: id 0 can't occur on either damage event (code-review verified), and the current fallback is the conservative one.
- **Follow-ups:**
  - [program slice 5b](../../implementation-plans/phase-5-content-baseline-program.md#slice-decomposition), `combat-effective-scores`;
  - the floor retune, because *trivial* is unreachable in Tier 0, and label compression at higher tiers. Both are for balance closure (R24–R25) and are recorded in balance.md Known gaps;
  - dev-world mob names that encode band, for content drop 6;
  - the PT-2 recorder subscribing to `MobConsideredEvent`;
  - a future use-based sweep must gather `OpponentPowerRatio` caller-side ([backlog](../backlog.md)).
