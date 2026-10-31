# Relative power + `consider` (Phase 5 slice 5)

- **Status:** planned (full plan, 2026-10-31. Planning forks resolved as decisions 8–12. Awaiting the spec-mode `architecture-reviewer` gate.)
- **Actors:** Player (`consider`), Administrator (`power` relation line), System (anti-grind on kill, ability-use and damage-taken awards; the balance sandbox's progression executor)
- **Module:** `Core/Modules/Progression` (relative-power read, `consider` command, label cut-points in `ProgressionConstants`, award handlers); `Core/Modules/BalanceInspection` (`power` migration). Feature home: [`progression/`](../features/progression/progression.md). Engine help: `content/help/`.
- **Program:** slice 5 of [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#slice-decomposition). Covers program **R20–R22**, and carries the program's anti-grind decisions (advisor resolved decisions 2, 3, 6, 7, 8). No slice dependencies.

**Description.** A player types `consider <mob>` and reads one descriptive line saying how dangerous the mob is *relative to them*: *trivial*, *easy*, *even*, *dangerous* or *deadly*. The line never shows a number, a tier or a band. The label comes from one shared relative-power relationship, computed on **effective** power for both sides (gear, progression, effects, tier). Anti-grind uses that same relationship, so *trivial* means exactly "this fight would teach you nothing". As a deliberate balance change, the slice moves anti-grind off raw attributes onto effective power. Anti-grind is extended to the ability-use and damage-taken award rows whenever they name an opponent. The admin `power` command moves onto the same read and gains a relation line for mob targets.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-10-31. The program file's [Design notes](phase-5-content-baseline-program.md#design-notes) and [Architecture brief](phase-5-content-baseline-program.md#architecture-brief) already frame this slice's seams; `/advise` confirms or refines them for this slice. Cite items as SR-numbers.)*

### User stories

- *As a player*, I want to `consider` a mob before I attack it, so I can tell an easy fight from one that will kill me, without needing numbers.
- *As a player*, I want the read to change as I grow stronger (better gear, improvements, tier), so I can see that a mob I once feared is now easy, and that it has stopped teaching me.
- *As a player*, I want my progression to keep pace when I fight things near my own strength, and to stop paying out when I fight things I've clearly outgrown. That should hold for kills, my skills and the blows I take.
- *As a designer/administrator*, I want `power <mob>` to show the target's relationship to me (the ratio and the label `consider` would give), so I can check the label cut-points against real mobs in-game.
- *As the system*, I want live kills and simulated kills to compute the anti-grind ratio identically, so sandbox predictions stay faithful to play.

**Main scenario (player).** A fresh character stands in a B1 hunting room with a wolf.
1. They type `consider wolf` and read *"The wolf looks like an even fight."*
2. They walk to B3 and `consider` a mob there: *"The bear would almost certainly kill you."* (deadly).
3. Much later, geared and improved, they return to B1. `consider wolf` now reads *"The wolf would be no challenge at all."* (trivial). *(Illustrative only. With the kept 0.25 floor, a B1 mob reads* trivial *only at about 4× its power, which is out of reach in Tier 0. In Phase 5 an outgrown B1 mob reads* easy*, and its XP is scaled down but not zeroed. See resolved decision 11.)*
4. Killing it awards no XP, and `kick`ing it advances nothing. *(Illustrative only, like step 3. In Tier 0 an outgrown B1 mob's kill and `kick` XP is scaled down but not zeroed; see resolved decision 11.)*
5. Against a peer-level B3 mob, kills and skill uses advance them at full pace.

### Scope

**In scope:**
- **`consider <mob>`:** a player command showing one of 5 descriptive labels.
- **The shared relative-power relationship** on effective power, taking a target *power* as well as a target entity (program design note).
- **Anti-grind on effective power** for the kill rule. It is extended to the ability-use and damage-taken rules when an opponent is named. This is a deliberate balance change, with re-pins.
- **The admin `power` migration** onto the shared read, plus a relation line for mob targets.
- **Help:** `consider` verb help, and a line in the engine `combat` topic.
- **The progression doc-accuracy fixes** the program assigns to this slice.

**Out of scope (confirmed):**
- **Showing current health or condition** in `consider`. The label is power-only (SR3).
- **Revealing numbers, tier or band to players.** No raw numbers appear anywhere player-facing; the `consider` line names neither tier nor band.
- **New progression narration** for anti-grind outcomes (SR13).
- **The XP-cost curve** (lever (a)). It stays flat. Its future is decided during balance closure (program open question).
- **Repeat-count or time-window diminishing returns,** now or later (program resolved decision 6).
- **Other consumers of the relative-power read:** entrance telegraphs, area banners, mob-AI flee/threat. They are shaped for later only.
- **Designer-authored cut-points in the standards document.** The cut-points are `ProgressionConstants` tunables (advisor call).
- **Dev-world mob content and naming.** These belong to content drops 6–8 (see Open questions).

### Behavioral requirements

**A. `consider`**
- **SR1.** A player in a room types `consider <target>`. The target is resolved among the mobs in that room with exactly the matching `kill` uses: a case-insensitive prefix match on the mob's name, then its keywords, taking the first hit. There are no ordinals (`ICombatSystem.TryFindTargetInRoom`). The command prints **one line**: the mob's authored name plus a fixed descriptive phrase for its label. Five labels, from weakest to strongest relative to the player: **trivial, easy, even, dangerous, deadly**.
- **SR2.** The `consider` output **never** contains a number, a power value, a ratio, a tier or a band. It is descriptive only. The phrases are fixed engine output strings, one per label. Illustrative only, with final wording at planning:
  - trivial: *"The wolf would be no challenge at all."*
  - easy: *"The wolf should be easy prey."*
  - even: *"The wolf looks like an even fight."*
  - dangerous: *"The wolf looks dangerous."*
  - deadly: *"The wolf would almost certainly kill you."*
- **SR3.** The label is derived from the **effective-power** relationship between the player and the mob (gear, progression, active effects, tier on both sides). It does **not** use current HP. A wounded mob reads the same as a fresh one.
- **SR4.** The label is recomputed on every `consider`. Wearing or removing gear, gaining an improvement, gaining or losing an effect, or changing tier changes the label the next time the player considers the mob. Nothing is cached.
- **SR5.** **Trivial** begins exactly at the anti-grind floor. Any mob that reads *trivial* is one whose kill would award no XP to that player, and vice versa. There is one constant with one meaning. The four label boundaries are tunables in `ProgressionConstants`, beside the anti-grind floor, and are catalogued in [balance.md](../design/balance.md).
- **SR6.** `consider` is read-only and free:
  - it is usable in or out of combat;
  - it spends no combat round, resource or cooldown;
  - it changes no state, and publishes only the observation event `MobConsideredEvent`, on a successful label (resolved decision 8);
  - any player may use it.
- **SR7.** **Direction of the gradient (program R21).** With the slice's default cut-points, a fresh starting character considering a mob at the (Tier 0, Band 1) anchor midpoint reads *even* or easier. Considering a mob at the (Tier 0, Band 3) anchor midpoint reads *dangerous* or worse. This slice verifies it against synthetic band-midpoint mobs. The content drops (6–8) and acceptance criterion A7 verify it again against authored dev-world mobs.

**B. Anti-grind (challenge-gated progression)**
- **SR8.** The **kill** award's anti-grind ratio (victim power ÷ earner power) is computed on **effective** power for both sides, not raw attributes. Below the floor, the earner is ineligible and receives no XP. Between the floor and the cap, the award scales proportionally. At the cap or above, it pays full.
- **SR9.** The **damage-taken** rule applies the same ratio when an attacker is named. The attacker is the opponent. Getting hit by an outgrown mob advances nothing.
- **SR10.** The **ability-use** rule applies the same ratio when the use names an opponent. The opponent is the ability's target *only when the ability is used offensively against it*, on the same offensive-target path `kill` and attack abilities share. Self-targeted uses (e.g. `empower`), ally-targeted uses and untargeted uses have **no opponent**. They keep today's behaviour exactly: no ratio, only the per-rank chance decay.
- **SR11.** A live kill and a balance-sandbox kill of the same pair compute the same ratio. The sandbox's progression path supplies the ratio the same way the live path does.
- **SR12.** **Determinism holds:** an ineligible candidate (below the floor) consumes **zero** random draws, now on the ability-use and damage-taken rows too. A no-opponent ability use draws exactly what it draws today.
- **SR13.** No new player-facing narration is added. The player sees the effect through existing XP narration and `progress`: kill XP against a given mob shrinks as the player's effective power grows, and outgrown opponents award nothing. The *trivial* label and the existing `progression` help topic are the explanation.

**C. Admin `power`**
- **SR14.** `power <mob>` keeps every line it prints today (computed power, classified `(Tier, Band)`, the authored `(Tier, Band)` echo). It **adds a relation line** giving the target's power ratio relative to the invoking admin, plus the label `consider` would show that admin. Example: `vs you: ratio 1.42 → dangerous`.
- **SR15.** For self and item targets, `power` output is unchanged; no relation line is shown. The live-entity snapshot `power` prints comes from the same shared read `consider` and anti-grind use, not a separate hand-rolled path.

**D. Help**
- **SR16.** `help consider` returns verb help: usage, what it tells you, and that it never shows numbers.
- **SR17.** The engine `combat` help topic (`content/help/combat.yaml`) gains a short engine-generic line pointing to `consider <target>` as the way to judge a fight before starting it. The `progression` topic already states that outgrown creatures teach little or nothing. It stays as is unless the planner finds it inaccurate after SR8–SR10.

### Edge cases & failure behavior

- **No argument:** standard usage / missing-argument messaging.
- **No matching mob in the room:** the standard target-resolution "not here" messaging (program edge case).
- **Target is an item:** refused with *"You can only size up creatures."*
- **Target is another player's name, or an exit keyword:** gets *"You don't see that here."*, exactly as `kill` does, because the lookup finds mobs only (resolved decision 10).
- **Target is an untargetable mob** (a hub shopkeeper): no danger label. It prints that the target can't be fought, e.g. *"The shopkeeper is not something you can fight."* This matches the meaning of `kill`'s refusal.
- **Target is `self` / `me`:** refused with *"You can only size up creatures."*
- **Player is in combat:** allowed. The read uses effective power and is unaffected by the ongoing fight's HP.
- **Mob in combat with someone else:** allowed; same read.
- **Player is incapacitated:** the command framework's existing incapacitated gate applies, unchanged.
- **Ability used offensively on an untargetable mob:** already refused upstream before any award. No anti-grind question arises.
- **Earner has no effective power** (a degenerate zero): the planner defines safe handling. The result must not divide by zero and must not award XP spuriously. *(Planner.)*

### Content & authoring needs

- **Nothing new to author for `consider`.** Labels derive from the oracle and from mobs' existing stats and Tier tags.
- **Label cut-points:** compiled tunables in `ProgressionConstants`. They are inspectable through [balance.md](../design/balance.md)'s tunables catalog, and in-game through the `power` relation line (SR14), which is the designer's way of checking them.
- **Help:** engine help-topic text edited in `content/help/combat.yaml` (authored YAML, the slice 4 pipeline); verb help for `consider`.
- **Balance:** the anti-grind change re-pins goldens and draw-sequence pins deliberately, under the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) skill, with a simulation sweep.

### Grounding notes

- **Backlog entry pulled in:** ["Player-facing `consider` danger-gauge"](../roadmap/backlog.md). It is a thin consumer of `IPowerBudgetSystem`, mapping the relationship to a coarse label with no raw numbers. The entry's "add a comparison helper when `consider` is the consumer" is satisfied by the program's relative-power read.
- **Program framing (already decided):** [Design notes](phase-5-content-baseline-program.md#design-notes):
  - "One relative-power scale, on effective power, owned by Progression"
  - "Progression is gated by challenge"

  [Resolved decisions (advisor intake)](phase-5-content-baseline-program.md#resolved-decisions-advisor-intake) 2, 3, 6, 7, 8. The architecture brief's rows for this slice: relative-power read, anti-grind input and coverage, `consider`, `power` live snapshot.
- **Exists today:**
  - anti-grind reads raw attributes to avoid a DI cycle ([progression-system.md](../features/progression/progression-system.md#anti-grind-proxy-reads-raw-attributes)), which makes it blind to gear and progression;
  - the `power` inspector's per-target snapshot ([commands.md `power`](../reference/commands.md#power), [power-budget-system.md](../features/progression/power-budget-system.md#consumers-one-function-each-inv-19));
  - the `Untargetable` protection axis and the shared `CanBeAttacked` gate ([mobs.md](../features/mobs/mobs.md#protection-invulnerability--immunity), [combat.md](../features/combat/combat.md));
  - the engine help topics `combat` / `progression` (`content/help/`, slice 4).
- **Doc-accuracy debts this slice clears** (program INV-20 tension): the "reads raw attributes" guidance, the "growing threshold / slows twice over" wording, and the challenge-gating model record. These live in progression.md, progression-system.md, power-budget-system.md, flow-31, and the `edit-progression-system` / `balance-tuning` skills.
- **Collision:** the program's R1 naming example (`Band-2 Wolf`) would leak band through a mob's *name*, which runs against SR2's spirit. This is parked for the content drops (below). It does not change this slice.

### Resolved decisions

*(Confirmed by the user on 2026-10-31. Do not relitigate downstream.)*

1. **Five labels:** trivial / easy / even / dangerous / deadly. Rejected: 4 labels (the backlog's original set); 7 finer labels.
2. **Output is the label only, descriptive:** no tier, band, number or ratio in the message. Rejected: a "teaches you nothing" learn-hint on *trivial*; showing the target's condition.
3. **Untargetable mobs** read as "can't be fought", with no label. Rejected: labelling them like any mob.
4. **Admin `power`** gains a relation line (ratio and label vs. the invoker) for mob targets. Rejected: a byte-identical pure migration.
5. **Approved as proposed** (round 2, S1–S9):
   - fixed compiled phrases per label (SR2);
   - offensive-target-only opponent for ability use, and the attacker for damage taken (SR9–SR10);
   - power-not-HP basis (SR3);
   - free/read-only (SR6);
   - synthetic band-midpoint verification here, re-verified in drops and A7 (SR7);
   - relation line for mobs only (SR15);
   - verb help plus a `combat` topic line (SR16–SR17);
   - no new narration (SR13);
   - re-pins under `balance-tuning` (Content & authoring).
6. **Mob naming that leaks band** is parked for the content drops, not fixed in this slice.

*Planning forks, confirmed by the user on 2026-10-31. Do not relitigate. Numbering continues after the advisor's decision 7.*

8. **`MobConsideredEvent` is added.** It is a thin observation event, `MobConsideredEvent(uint PlayerEntityId, string MobBlueprintId, RelativePowerLabel DangerLabel)`. Its shape and field names match [`playtest-run-mode.md`](playtest-run-mode.md) resolved decision 4 exactly, so there is no rename; `DangerLabel` is typed as the `RelativePowerLabel` enum.
   - `ConsiderCommand`, as the Initiator, publishes it **only on a successful label**. It never fires on a refusal, a not-found, an untargetable target, or an unreadable reading.
   - No gameplay handler subscribes. The PT-2 playtest recorder subscribes when that slice lands, which keeps that plan's decision 4 and its A7 row valid as written.
   - SR6 is reworded to match. Rejected: no event, which would have meant amending PT-2.
9. **Typed output.** `consider` emits `ConsiderNarrationMessage(Text, TargetName, Label)`, a `NarrationMessage` subclass, if PT-1's `NarrationMessage` base has merged by the time WP-3 starts. Otherwise it emits `PlainMessage`, and the PT-1 on-touch ratchet converts it later.
10. **Targets mirror `kill`.**
    - `self`/`me` and item tokens are refused with "You can only size up creatures."
    - Any other unmatched token, including another player's name, gets "You don't see that here."
    - SR1 is reworded to prefix-only matching. Rejected: a ninth hand-rolled player-by-name match.
11. **The anti-grind floor stays 0.25.** *Trivial* is unreachable in Tier 0, because a B1 mob reads trivial only at about 1624 power, above even the T6 reference of 1106. This is recorded under balance.md Known gaps (a WP-3 doc item) and in the plan's Design notes. Main-scenario step 3 is annotated as illustrative. The floor is retuned at balance closure (R24–R25), alongside slice 5b. Rejected: raising the floor in this slice.
12. **Cut-points are easy 0.25 (the floor), even 0.85, dangerous 1.12, deadly 1.30.** B2's midpoint (1.155) reads *dangerous*. Rejected: an even/dangerous cut at 1.16.

## Preconditions

- The power-budget oracle (`IPowerBudgetSystem`, `PowerBudgetTunables`) is composed by `BalanceInspectionModule` on both hosts. The tunables come from the balance-standards registry, falling back to `PowerBudgetTunables.Default`.
- `IStatSystem.Get` folds every contributor: equipment, abilities, progression and ascension.
- Mobs carry `MobDataComponent` (authored `Tier`) and `AttributesComponent`. Players carry `CharacterComponent` and `AttributesComponent`.
- `ICombatSystem.TryFindTargetInRoom` and `ICombatSystem.CanBeAttacked` are the shared room target lookup and protection gate.
- Slice 4's help pipeline is live. Verb help is generated from `ICommand` metadata, and `content/help/combat.yaml` is the engine layer.

### Verified in code (2026-10-31)

These answer the planner open questions the advisor raised. Paths are relative to the repo root.

1. **Event payloads.**
   - `CombatRoundEvent(AttackerEntityId, DefenderEntityId, …)` (`Core/Modules/Combat/Events/CombatRoundEvent.cs:12-17`) and `AbilityStrikeResolvedEvent(AttackerEntityId, DefenderEntityId, …)` (`Core/Modules/Combat/Events/AbilityStrikeResolvedEvent.cs:17-24`) already name the attacker. **No change.**
   - `AbilityActivatedEvent(ActorEntityId, AbilityId, uint? TargetEntityId)` (`Core/Modules/Abilities/Events/AbilityActivatedEvent.cs:6`) carries the target but **no offensive flag**.
   - The target id alone can't tell an opponent from a non-opponent. `AbilityInvocationPipeline.ResolveTargetAsync` returns the **actor's own id** for a `Targeting.Self` ability (`Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs:165-166`), so a self-cast `empower` publishes a non-null `TargetEntityId`.
   - **Add one thin flag:** `bool IsOffensive = false`, appended last so existing call sites keep compiling.
   - **Publishers.** The pipeline passes its already-computed `isOffensive` (`AbilityInvocationPipeline.cs:71`, published at `:123`). The admin `useability` (`Core/Modules/Abilities/Commands/UseAbilityCommand.cs:137`) passes `_abilitySystem.IsOffensive(abilityId) && targetEntityId.HasValue`.
2. **Ability-use ordering.** The pipeline publishes `AbilityActivatedEvent` (`:123`) **before** it calls `ResolveAbilityStrike` and publishes `AbilityStrikeResolvedEvent` (`:140-145`).
   - `EventBus.PublishAsync` awaits each subscriber in turn (`Core/Events/EventBus.cs:64-65`). The target can be destroyed only further down the strike chain: `AbilityStrikeResolvedEvent` → `AbilityStrikeHandler` → `CombatEndedEvent` → `CombatMobDeathHandler.DestroyEntity` (`Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs:53`).
   - **So the target is always live and readable when `AdvancementHandler` handles `AbilityActivatedEvent`, even for a killing `kick`.** The ratio is gathered there. The "opponent named but unreadable ⇒ ineligible" rule stays as a defensive backstop, for example against a stale `CombatStateComponent.OpponentEntityId`. It is never a fallback to ratio-free XP.
3. **Kill path.** `CombatMobDeathHandler` (priority 80) publishes `MobDiedEvent` and awaits it (`CombatMobDeathHandler.cs:47-51`) before `DestroyEntity` (`:53`). `AdvancementHandler` runs at `HandlerPriority.Domain` (20) (`Core/Modules/Progression/Handlers/AdvancementHandler.cs:48`).
   - The victim's `AttributesComponent`, `PoolsComponent`, `MobDataComponent` and effects are all still present, so its effective scores are readable.
   - `KillerEntityId == 0` is rejected by context eligibility (`RequiresAttributableActor`) **before** the anti-grind step (`Core/Modules/Progression/Systems/ProgressionSystem.cs:44-45`). The unreadable-killer reading is therefore never consulted for it.
4. **`power`'s live snapshot today** (`Core/Modules/BalanceInspection/Commands/PowerCommand.cs`):
   - **Self:** `LiveEntitySnapshot(invoker)`, which is every `ScoreId` through `IStatSystem.Get` (`:115-121`), estimated with `tier: 0` (`:78`). A player's tier is already folded in by `AscensionEffectContributor`.
   - **Mob:** the same snapshot, estimated with `tier: mob.Tier` from `MobDataComponent` (`:97`). Live mobs never carry `AscensionComponent`, so tier is never counted twice.
   - **Item:** `IItemPowerProjectionSystem.Project(item)` with `item.Tier` (`:89`). This path is unchanged.
   - **Output:** `Estimate` → `Classify` → `PowerReadoutMessage` (`:109-111`).
   - **The read reproduces this exactly:** `tier = MobDataComponent?.Tier ?? 0`.
   - **The sandbox is consistent with it.** Sandbox combatants put tier on an `AscensionComponent` and leave `MobDataComponent.Tier` at 0 (`Core/Modules/Simulation/Systems/SimCombatantFactory.cs:132, 162-163`), so the same rule is correct there too.
   - Existing golden numbers that must not move: self 650, mob 712, item 400 (`Hedron.Tests/Modules/BalanceInspection/PowerCommandTests.cs:151, 201, 230`).
5. **Default cut-points.** See [Design notes → Cut-point proposal](#cut-point-proposal-sr7-arithmetic). Fresh-character power is **386**, equal to the T0 reference build. The T0 midpoints are B1 = **406** (ratio 1.052) and B3 = **486** (ratio 1.259). Proposed cut-points: easy ≥ 0.25 (`AntiGrindFloorRatio`), even ≥ 0.85, dangerous ≥ 1.12, deadly ≥ 1.30.
6. **Degenerate earner power.** `AttributeSystem` returns **defaults** (10 per attribute, 100 MaxHp) when a component is missing (`Core/Modules/Attributes/Systems/AttributeSystem.cs:37-50`).
   - So a destroyed or attribute-less entity read through `IStatSystem.Get` looks like a fresh reference build (386), not like 0. Readability must be checked **explicitly**.
   - Today's raw path returned 0 for a missing `AttributesComponent` (`ProgressionSystem.cs:272-273`) and a 0 scale for killer power ≤ 0 (`:255-256`).
   - The read keeps both outcomes. A reading is **unreadable** when either side lacks `AttributesComponent`, or when subject power ≤ 0. An unreadable reading has `Ratio = 0.0` and `Label = null`.
   - At 0.0 the existing floor makes the award ineligible, with zero draws. This avoids divide-by-zero and never awards XP spuriously.
7. **Anti-grind values and signatures today.**
   - `AntiGrindFloorRatio = 0.25` (`Core/Modules/Progression/ProgressionConstants.cs:40`) and `AntiGrindCap = 1.5` (`:43`).
   - The scale is `ratio < floor ? 0 : min(ratio, cap)` (`ProgressionSystem.cs:253-263`). "Full" in SR8 therefore means the capped 1.5× multiplier.
   - `CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId)` (`Core/Modules/Progression/Systems/IProgressionSystem.cs:112`).
   - `UseAwardContext(string? SubjectAbilityId, ScoreId? SubjectAttributeTrack, double ContentScale = 1.0, int Magnitude = 0, uint OpponentEntityId = 0)` (`IProgressionSystem.cs:36-41`). `OpponentEntityId` exists only to feed the raw anti-grind proxy.
   - The `AbilityUse` and `DamageTaken` rows do not set `AppliesAntiGrindPowerRatio` (`ProgressionConstants.cs:99-125`).
8. **Sandbox composition point.** `SandboxWorldFactory.Create` (`Core/Modules/Simulation/Systems/SandboxWorldFactory.cs:50-84`) hand-builds `ProgressionSystem` with `_powerBudget` (`:58`) and `StatSystem` (`:71`).
   - The new read is built right after `:71` as `new RelativePowerSystem(ecs, stats, _powerBudget)`.
   - It is exposed as `SandboxWorld.RelativePower` (`Core/Modules/Simulation/Systems/SandboxWorld.cs:40-68`).
   - `ProgressionScenarioExecutor` calls `AwardCombatExperience` at `ProgressionScenarioExecutor.cs:41`.
   - Live composition is `ProgressionModule.AddProgressionModule` (`Core/Modules/Progression/ProgressionModule.cs:21-30`), which both hosts call.
9. **Target lookup facts.** `TryFindTargetInRoom` (`Core/Modules/Combat/Systems/CombatSystem.cs:43-67`) matches **mobs only** (`MobDataComponent`). It is a case-insensitive prefix match on the name, then on keywords, and takes the first hit. **There are no ordinals.**
   - SR1's "prefix/ordinal" is therefore read as "exactly `kill`'s matching", which is prefix only.
   - `kill` prints "Kill what?" when given no argument (`Core/Modules/Combat/Commands/KillCommand.cs:59-63`).
   - Commands are blocked while the player is incapacitated unless they opt in (`Core/Commands/ICommand.cs:55`, `CommandDispatcher.cs:174`).
10. **Fresh character.** Attributes default to 10 (`Core/ECS/Components/AttributesComponent.cs:7-10`) and MaxHp to 100 (`PoolsComponent.cs:6`). `AttackPower = Body/2 = 5` and `Defense = Body/4 = 2` (`Core/Modules/Stats/Systems/StatSystem.cs:25-27`).
    - The starting abilities (`kick`, `empower`; `Core/Modules/Account/CharacterDefaultsOptions.cs:10`) are both `Activation.Active`, so they add no passive modifiers.
    - The `HpCurrent` weight is 0 (`Core/Systems/PowerBudgetTunables.cs:38`), so SR3 (power, not HP) holds by construction.

## Postconditions

*(These are the coverage contract. ◆ marks player-invisible internal state, and each ◆ maps to a named test in [Test plan](#test-plan--verification-inv-25).)*

1. `consider <mob>` prints exactly one line: the mob's name plus the fixed phrase for its label. No other output is written.
2. ◆ The `consider` output contains no digit, no ratio, no tier and no band (SR2).
3. ◆ The label is computed from effective power on both sides (gear, progression, effects, tier) and is recomputed on every call. Current HP has no effect (SR3, SR4).
4. ◆ The relative-power read returns `Label = Trivial` **iff** `Ratio < ProgressionConstants.AntiGrindFloorRatio`. The same ratio passed to `AwardCombatExperience` is ineligible **iff** it is trivial. Both use one constant (SR5).
5. ◆ With the default cut-points and `PowerBudgetTunables.Default`, a fresh character reads **even or easier** against a mob at the (0, 1) midpoint and **dangerous or worse** against a mob at the (0, 3) midpoint (SR7).
6. `consider` refuses an untargetable mob with the "can't be fought" line and prints no label. It refuses `self`/`me` and item tokens with the creature-only line. Any other unmatched token, including another player's name, gets "You don't see that here." No argument gets "Consider whom?" (resolved decision 10).
7. ◆ `consider` spends no round, resource or cooldown and changes no state (SR6).
   - A successful label publishes exactly one `MobConsideredEvent(PlayerEntityId, MobBlueprintId, DangerLabel)`. Its `DangerLabel` equals the label printed.
   - A refusal, a not-found, an untargetable target or an unreadable reading publishes **nothing** (resolved decision 8).
8. ◆ The kill award's anti-grind ratio is `victimEffectivePower / killerEffectivePower`. The killer's gear and improvements lower it (SR8).
9. ◆ The damage-taken award applies the ratio `attackerPower / defenderPower`. Below the floor it is ineligible, with **zero draws** (SR9, SR12).
10. ◆ An offensive ability use (`IsOffensive == true` with a target) applies the ratio `targetPower / actorPower`. Below the floor it is ineligible with zero draws. A self-targeted, ally-targeted or untargeted use applies **no** ratio and draws exactly what it draws today (SR10, SR12).
11. ◆ An opponent that is named but unreadable (no `AttributesComponent`, or subject power ≤ 0) gives an **ineligible** award with zero draws. It never falls back to ratio-free XP.
12. ◆ A live kill and a sandbox kill of the same pair compute the same ratio and produce the same award rows at the same seed (SR11). This holds at Tier 0 and at Tier 1, which exercises the two tier representations.
13. ◆ `ProgressionSystem` depends on none of `IStatSystem`, `IEffectSystem`, `IRelativePowerSystem` or `IPowerBudgetSystem`. Its raw-attribute estimate is gone (architecture guard).
14. `power <mob>` prints every line it prints today with unchanged values, plus `vs you: ratio <r:0.00> → <label>`. `power` for self and items is unchanged (SR14, SR15).
15. ◆ `power`'s live-entity power comes from `IRelativePowerSystem.GetEffectivePower`. The self (650) and mob (712) goldens are unchanged.
16. `help consider` returns verb help covering usage, what it tells you, and that it never shows numbers. `help combat` points to `consider <target>` (SR16, SR17).
17. ◆ The sandbox progression goldens and the use-based draw pins are re-pinned deliberately under `balance-tuning`, and a sweep result is recorded in the completed record.

## Main flow

**`consider` (player):**
1. The player sends `consider wolf` (or `con wolf`). `CommandDispatcher` resolves `ConsiderCommand` and applies the incapacitation gate, which is default-deny ([flow-03](../architecture/flows/flow-03-player-command-lifecycle.md)).
2. `ConsiderCommand` handles the token:
   - empty → "Consider whom?";
   - `self`/`me` → the creature-only refusal;
   - otherwise `ICombatSystem.TryFindTargetInRoom(room, token)`.
3. On a miss, it checks for an item via `IItemSystem.TryFindItemInInventory` / `TryFindItemInRoom`. A hit gets the creature-only refusal. Anything else, including another player's name, gets "You don't see that here.", mirroring `kill`.
4. On a mob hit, `ICombatSystem.CanBeAttacked(mob)` is checked. If it is false, the command prints "\<Name\> is not something you can fight." and stops.
5. `IRelativePowerSystem.Relate(player, mob)` builds both effective-power values (snapshot step → `IPowerBudgetSystem.Estimate`), divides them, and maps the ratio to a `RelativePowerLabel` using `ProgressionConstants`.
6. `ConsiderCommand` maps the label to its fixed phrase and writes one message: a `ConsiderNarrationMessage` if PT-1's `NarrationMessage` base has merged, otherwise a `PlainMessage` (resolved decision 9).
   - It then publishes `MobConsideredEvent(player, blueprintId, label)`. `blueprintId` comes from the mob's `BlueprintComponent`, or `string.Empty` if it has none, matching `CombatMobDeathHandler`.
   - An unreadable reading writes the fixed "can't tell" line instead and publishes nothing.

**Anti-grind gather (system), the alternate flows:**

7. **Kill.** `MobDiedEvent` → `AdvancementHandler.HandleAsync`:
   - `ratio = Relate(killer, victim).Ratio`;
   - `AwardCombatExperience(killer, victim, ratio)`.

   This runs while the victim is still live, before `DestroyEntity`.
8. **Ability use.** `AbilityActivatedEvent` → `AdvancementHandler`:
   - `OpponentPowerRatio = IsOffensive && TargetEntityId is {} t ? Relate(actor, t).Ratio : null`;
   - `AwardUseExperience(actor, AbilityUse, context)`.
9. **Damage taken.** `CombatRoundEvent` / `AbilityStrikeResolvedEvent` → `AdvancementHandler`:
   - `OpponentPowerRatio = Relate(defender, attacker).Ratio`;
   - `AwardUseExperience(defender, DamageTaken, context)`.
10. **Sandbox.** In each loop iteration, `ProgressionScenarioExecutor` computes `ratio = world.RelativePower.Relate(subject, victim).Ratio`, then calls `world.Progression.AwardCombatExperience(subject, victim, ratio)`. The ratio is recomputed per kill because the subject's effective power grows as it improves.

## Events fired

| Event | Publisher | Change |
|---|---|---|
| `AbilityActivatedEvent` | `AbilityInvocationPipeline`, `UseAbilityCommand` | **Payload +1 thin field:** `bool IsOffensive = false` (INV-6). No new subscriber. |
| `MobDiedEvent`, `CombatRoundEvent`, `AbilityStrikeResolvedEvent` | unchanged | `AdvancementHandler` now also reads the opponent's power through the read, and still publishes nothing extra |
| `ExperienceAwardedEvent`, `TrackImprovedEvent` | `AdvancementHandler` | Unchanged shape. Their *amounts* move (deliberate balance change) |
| `MobConsideredEvent(uint PlayerEntityId, string MobBlueprintId, RelativePowerLabel DangerLabel)` | `ConsiderCommand` (Initiator) | **New** (`Core/Modules/Progression/Events/MobConsideredEvent.cs`, past tense, thin; INV-6). Published only on a successful label. **No subscriber in this slice.** The PT-2 playtest recorder subscribes when it lands. Its shape and names match [`playtest-run-mode.md`](playtest-run-mode.md) resolved decision 4, with no rename (resolved decision 8). |
| *(none)* | `PowerCommand` | Read-only. The dispatcher's `CommandExecutedEvent` (framework audit) is the only event on the path. |

## Systems / handlers involved

| Part | Status | Shape |
|---|---|---|
| `IRelativePowerSystem` / `RelativePowerSystem` (`Core/Modules/Progression/Systems/`) | **new** (domain) | Deps: `EntityService`, `IStatSystem`, `IPowerBudgetSystem`. No `IAscensionSystem` is needed: tier comes from `MobDataComponent.Tier` for mobs and from the contributor fold for players, as in `power` today. This is a simplification of the brief's placement row. Stateless and pure (INV-26 moot). |
| `RelativePowerLabel` enum (`Core/Modules/Progression/RelativePowerLabel.cs`) | **new** | `Trivial, Easy, Even, Dangerous, Deadly` |
| `RelativePowerReading` record (in `IRelativePowerSystem.cs`) | **new** | `readonly record struct RelativePowerReading(bool IsReadable, int SubjectPower, int TargetPower, double Ratio, RelativePowerLabel? Label)`. When unreadable, `Ratio = 0.0` and `Label = null`. |
| `ProgressionConstants` | **changed** | Adds `RelativeEvenFloorRatio = 0.85`, `RelativeDangerousFloorRatio = 1.12` and `RelativeDeadlyFloorRatio = 1.30`. The easy floor **is** `AntiGrindFloorRatio`. The `AbilityUse` and `DamageTaken` rows gain `AppliesAntiGrindPowerRatio: true`. The `ThresholdIncrement` docstring is fixed (doc accuracy). |
| `IProgressionSystem` / `ProgressionSystem` | **changed** | `AwardCombatExperience(uint killer, uint victim, double opponentPowerRatio)`. `UseAwardContext.OpponentEntityId` is **replaced** by `double? OpponentPowerRatio = null`. `GetEffectivePower` and the `IPowerBudgetSystem` ctor dependency are **deleted**. `ComputeAntiGrindScale(double ratio)`: `!double.IsFinite(ratio) \|\| ratio < floor ? 0 : min(ratio, cap)`. A flagged rule applies the scale only when the ratio is non-null. |
| `AdvancementEligibility.AppliesAntiGrindPowerRatio` | doc only | "Applied when the context supplies `OpponentPowerRatio`, and absent means no opponent." |
| `AdvancementHandler` (p=20) | **changed** | Ctor adds `IRelativePowerSystem`. Its job is field mapping plus one relative-power read per opponent-bearing trigger. Opponent selection maps event facts, never a game-rule branch (INV-8). |
| `AbilityActivatedEvent` + 2 publishers | **changed** | `IsOffensive` flag (see Verified 1) |
| `ConsiderCommand` (`Core/Modules/Progression/Commands/`) | **new** (Initiator) | `Name "consider"`, `Aliases ["con"]`, `Category Player`, `MatchingMode Partial`, `RestOfLine` target (optional, so it can answer "Consider whom?"). Deps: `IRelativePowerSystem`, `ICombatSystem`, `IItemSystem`, `EntityService`, `IEventBus`. `UsableWhileIncapacitated` stays at the default (false). It publishes `MobConsideredEvent` on a successful label only. |
| `MobConsideredEvent` (`Core/Modules/Progression/Events/`) | **new** event | See [Events fired](#events-fired). Built with the [`add-event`](../../.claude/skills/add-event/SKILL.md) skill and catalogued in [03-events.md](../architecture/03-events.md). |
| `ConsiderNarrationMessage` (`Core/Modules/Progression/`) | **new, conditional** | `record ConsiderNarrationMessage(string Text, string TargetName, RelativePowerLabel Label) : NarrationMessage`, only if PT-1's base has merged by WP-3. Otherwise `PlainMessage` (resolved decision 9). |
| `PowerCommand` | **changed** | Ctor swaps `IStatSystem` for `IRelativePowerSystem`, and `LiveEntitySnapshot` is deleted. Live targets use `power = GetEffectivePower(id)`, then `IPowerBudgetSystem.Classify`. Mob targets add `Relate(invoker, mob)`. |
| `PowerReadoutMessage` + `TelnetOutputFormatter.FormatPowerReadout` | **changed** | Adds `double? RelationRatio = null, string? RelationLabel = null` (primitives, so `Core/Output` imports no module type). The line renders `  vs you : ratio 1.42 → dangerous`, or `  vs you : unreadable` when the ratio is non-null and the label is null. |
| `SandboxWorld`, `SandboxWorldFactory`, `ProgressionScenarioExecutor` | **changed** | `RelativePower` member. `ProgressionSystem` is built without `_powerBudget`. The executor gathers the ratio once per kill. |
| `ProgressionModule` | **changed** | `services.AddSingleton<IRelativePowerSystem, RelativePowerSystem>()` and `services.AddSingleton<ICommand, ConsiderCommand>()` |
| Reused unchanged | — | `IPowerBudgetSystem`, `IStatSystem`, `ICombatSystem.TryFindTargetInRoom`/`CanBeAttacked`, `IItemSystem` lookups, `IItemPowerProjectionSystem`, `AbilityInvocationPipeline` ordering, `CombatMobDeathHandler` ordering |

**Interface (target):**

```csharp
public interface IRelativePowerSystem
{
    /// Effective power of a live entity: every ScoreId via IStatSystem.Get, estimated with
    /// tier = MobDataComponent.Tier (mobs) or 0 (players; tier is contributor-folded).
    /// Ungated — `power`'s readout. Never used for a ratio without Relate's readability check.
    int GetEffectivePower(uint entityId);

    /// target ÷ subject on effective power. Unreadable (either side lacks AttributesComponent,
    /// or subject power <= 0) ⇒ IsReadable false, Ratio 0.0, Label null.
    RelativePowerReading Relate(uint subjectEntityId, uint targetEntityId);

    /// Power-keyed overload (shaped for later: cell anchors). Negative targetPower clamps to 0.
    RelativePowerReading Relate(uint subjectEntityId, int targetPower);

    /// Ratio → label. Inclusive lower bounds: < AntiGrindFloorRatio Trivial; < RelativeEvenFloorRatio Easy;
    /// < RelativeDangerousFloorRatio Even; < RelativeDeadlyFloorRatio Dangerous; else Deadly.
    RelativePowerLabel Classify(double ratio);
}
```

## Implementation plan — work packages

The primary agent runs `architecture-reviewer` (code mode) across the combined diff once all three packages land.

### WP-1 — The relative-power read and the `power` migration

- **Scope:**
  - `RelativePowerLabel`, `RelativePowerReading`, `IRelativePowerSystem`/`RelativePowerSystem`, and the three cut-point constants;
  - the `ProgressionModule` registration;
  - `PowerCommand` moved onto the read, plus the relation line (message fields and formatter).
- **Files:**
  - `Core/Modules/Progression/{RelativePowerLabel.cs, ProgressionConstants.cs (cut-points only), ProgressionModule.cs, Systems/IRelativePowerSystem.cs, Systems/RelativePowerSystem.cs}`;
  - `Core/Modules/BalanceInspection/Commands/PowerCommand.cs`;
  - `Core/Output/{PowerReadoutMessage.cs, TelnetOutputFormatter.cs}`;
  - tests under `Hedron.Tests/Progression/` and `Hedron.Tests/Modules/BalanceInspection/`;
  - `docs/reference/systems.md`:
    - a **new** `IRelativePowerSystem` / `RelativePowerSystem` entry (Location, Dependencies `EntityService`/`IStatSystem`/`IPowerBudgetSystem`, the interface block, and the readability contract), per [`add-domain-system`](../../.claude/skills/add-domain-system/SKILL.md) step 4;
    - the `MobPowerProjectionSystem` entry (~line 794): its note that "`PowerCommand`'s live self/mob path keeps its own `IStatSystem` snapshot" no longer holds. Repoint it to `IRelativePowerSystem.GetEffectivePower`.
- **Depends on:** nothing.
- **Out of scope:**
  - any `ProgressionSystem`, handler, event or sandbox change;
  - `consider`;
  - the item projection path;
  - combat stat reads (slice 5b).
- **Exit:** build green. `RelativePowerSystemTests` (including the SR7 fixture and the cut-point guard) pass. `PowerCommandTests` pass with the 650/712/400 goldens unchanged, plus the new relation-line tests.

### WP-2 — Anti-grind on effective power, across opponent-bearing rules (deliberate balance change)

- **Scope:**
  - the `ProgressionSystem` signature change and the deletion of the raw path;
  - `UseAwardContext.OpponentPowerRatio`;
  - the rule flags on `AbilityUse` and `DamageTaken`;
  - `AbilityActivatedEvent.IsOffensive` and its two publishers;
  - `AdvancementHandler` gathering;
  - the sandbox world, factory and executor;
  - the extended architecture guard;
  - code docstring fixes (`IProgressionSystem`, `AdvancementEligibility`, `ProgressionConstants`);
  - re-pins under the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) skill, with a sweep.
- **Files:**
  - `Core/Modules/Progression/{Systems/IProgressionSystem.cs, Systems/ProgressionSystem.cs, AdvancementEligibility.cs, ProgressionConstants.cs (rows + docstrings), Handlers/AdvancementHandler.cs}`;
  - `Core/Modules/Abilities/{Events/AbilityActivatedEvent.cs, Commands/AbilityInvocationPipeline.cs, Commands/UseAbilityCommand.cs}`;
  - `Core/Modules/Simulation/Systems/{SandboxWorld.cs, SandboxWorldFactory.cs, ProgressionScenarioExecutor.cs}`;
  - every test that constructs `ProgressionSystem` or `AdvancementHandler`: `ProgressionSystemTests`, `AdvancementHandlerTests`, `ProgressionAwardFlowTests`, `ProgressionEffectContributorTests`, `AscensionEffectContributorTests`, `ProgressionGuardTests`;
  - `Hedron.Tests/Simulation/ProgressionScenarioExecutorTests.cs` (the new grown-power case) and `SimulationInvariantTests`;
  - `AdvancementHandler`'s XML doc: reword "this handler's entire job is the field mapping" to "field mapping plus one relative-power read per opponent-bearing trigger; opponent selection maps event facts, never a game-rule branch" (INV-8);
  - `docs/reference/systems.md`:
    - the `ProgressionSystem` entry (~730–749): drop `IPowerBudgetSystem` from Dependencies; the new `AwardCombatExperience(killer, victim, double opponentPowerRatio)` signature; `UseAwardContext.OpponentPowerRatio` (replacing `OpponentEntityId`); the raw-attribute proxy text removed;
    - the `SandboxWorldFactory` / `ProgressionScenarioExecutor` entries (~890–917): the `SandboxWorld.RelativePower` member, `ProgressionSystem` constructed without the oracle, and the per-kill ratio gather in the executor;
  - `docs/reference/handlers.md`, the `AdvancementHandler` entry (~194–199): Uses gains `IRelativePowerSystem`, and its responsibilities gain the per-trigger ratio gather (kill → victim; offensive ability → target; damage → attacker).
- **Depends on:** WP-1 (`IRelativePowerSystem`).
- **Out of scope:**
  - floor and cap values (unchanged);
  - the XP-cost curve;
  - generalizing the progressionRate sweep to use-based sources (backlog);
  - an `AwardAbilityUseExperience` wrapper (backlog);
  - combat stat reads (slice 5b).
- **Exit:** `dotnet test` green with deliberate re-pins only. The new zero-draw, no-opponent, unreadable and parity tests pass. The guard test forbids the four types. A sweep result (scenario, seed, before → after) is captured for the completed record.

### WP-3 — `consider`, help, and docs/flows

- **Scope:**
  - `ConsiderCommand` (phrases, refusals, `con` alias) and its registration;
  - `MobConsideredEvent`, built per the [`add-event`](../../.claude/skills/add-event/SKILL.md) skill and published on a successful label only;
  - the conditional `ConsiderNarrationMessage` (resolved decision 9);
  - verb-help metadata, plus the `combat.yaml` line;
  - flows 31/33/24/20/17 (body **and** mermaid);
  - doc-accuracy fixes in the progression docs and skills;
  - balance.md knob rows, plus a Known-gaps entry: "*trivial* is unreachable in Tier 0 at the 0.25 floor (a B1 mob needs ~1624 player power, above T6's 1106 reference); retune at balance closure (R24–R25) with slice 5b" (resolved decision 11);
  - the events catalog diff in [03-events.md](../architecture/03-events.md): a new `MobConsideredEvent` row under Progression. That catalog doesn't list `AbilityActivatedEvent`'s payload, so the `IsOffensive` field is documented in flow-24 instead;
  - the `commands.md` rows for `consider` and `power`;
  - closing the backlog `consider` entry, and adding one line to the backlog's use-based sweep entry (~`backlog.md:339`): "a future use-based executor must also gather `OpponentPowerRatio` caller-side, through `IRelativePowerSystem`, exactly as the kill executor does";
  - a **new design doc**, `docs/features/progression/relative-power-system.md`, the durable home of the read's design: the readability contract, the ratio → label rule, the tier source, cut-points, and consumers. It is linked from progression.md's doc table;
  - the flows index (`docs/architecture/flows/README.md`): update **flow-31's** row (~line 30) and **flow-17's** row (~line 24, adding the `consider` pre-initiation leg).
- **Files:**
  - `Core/Modules/Progression/Commands/ConsiderCommand.cs`, `Core/Modules/Progression/Events/MobConsideredEvent.cs`, `ProgressionModule.cs`, and `ConsiderNarrationMessage.cs` if PT-1 has merged;
  - `content/help/combat.yaml`;
  - `docs/architecture/flows/{flow-31,flow-33,flow-24,flow-20,flow-17}-*.md`, `docs/architecture/flows/README.md`, `docs/architecture/03-events.md`;
  - `docs/features/progression/{progression.md, progression-system.md, power-budget-system.md, relative-power-system.md (new)}`. In `power-budget-system.md`, the exact line is **:29** (the consumer that "passes raw attributes"), plus the consumer-list row (~130) and the deferred-`consider` paragraph (~140);
  - `docs/design/balance.md`, `docs/reference/commands.md`, `docs/roadmap/backlog.md`;
  - `docs/reference/systems.md` / `handlers.md`: a final pass that every entry WP-1/WP-2 touched reads true, plus the `ConsiderCommand` dependency note if the catalog lists command dependencies;
  - `.claude/skills/edit-progression-system/SKILL.md`:
    - line 39: fix the "slows twice over" wording (the cumulative total grows, each step costs a flat increment, and the chance decays);
    - line 62: "field mapping plus one relative-power read per opponent-bearing trigger; opponent selection maps event facts, never a game-rule branch";
    - line 67: keep the "ProgressionSystem never reads `IStatSystem`" guardrail, with the rationale changed to "the ratio is supplied by the caller";
    - line 68: replace the `GetEffectivePower`/oracle backend with the `IRelativePowerSystem` gather at the handler and the executor;
  - `.claude/skills/balance-tuning/SKILL.md`, lines 46–52: say that the `AbilityUse`/`DamageTaken` rows now carry anti-grind (applied when an opponent is named), and that the kill-only sweep does **not** validate this;
  - `.claude/skills/add-domain-system/SKILL.md:134`: reword the allowed dependencies to "core systems, `EntityService`, and same-or-lower domain reads (INV-1)";
  - `Hedron.Tests/Progression/ConsiderCommandTests.cs`.
- **Depends on:** WP-1 for the command, and WP-2 for the anti-grind doc and flow text.
- **Out of scope:**
  - dev-world mob content and naming;
  - the playtest recorder's subscription to `MobConsideredEvent` (PT-2);
  - telegraphs, banners and AI consumers.
- **Exit:** `ConsiderCommandTests` pass. A `DevWorld` smoke shows `consider` against a protected hub NPC printing the "can't be fought" line, and `help consider` resolving. The flows, the flows index, the docs, the skills, and the **reference catalogs** (`systems.md`, `handlers.md`, `commands.md`, `03-events.md`) all read true to the code (INV-16).

## Content tooling impact (INV-18)

- **No new authored state, data-file shape or `TemplateRegistry` entry.** Labels derive from the oracle and from existing mob stats and `Tier` tags.
- **Cut-points** are compiled Category-3 tunables in `ProgressionConstants`, catalogued as new rows in [balance.md](../design/balance.md#knob-catalog). The rows record the trivial boundary's identity with `AntiGrindFloorRatio` and the SR7 calibration.
- **Inspection:** `power <mob>`'s relation line is the designer's in-game check of the cut-points against real mobs (SR14).
- **Help:** `consider` verb help comes from `ICommand` metadata. `content/help/combat.yaml` gains one engine-generic line, authored through the slice-4 pipeline. The world layer can override it.
- **Balance:** the re-pins plus one sweep, under `balance-tuning`. The sweep **cannot see** the `AbilityUse`/`DamageTaken` anti-grind change, because the progressionRate executor models kills only. The plan states this as an unvalidated, conservative change, per the skill's step-2 warning.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Note |
|---|---|---|
| Commands | **Adequate** | `ICommand` with `Partial` matching. The exact alias `con` wins over prefix resolution, while `conf`/`config` still reach `config`. The incapacitation gate is default-deny. |
| Target resolution | **Adequate** | `consider` mirrors `kill` (resolved decision 10). It reuses `TryFindTargetInRoom` + `CanBeAttacked` (the `kill` and pipeline precedent) and the `IItemSystem` lookups (the `power` precedent). Player names fall through to "not here", so no player-by-name match is added; the existing 8 hand-rolled admin copies are untouched. |
| Output | **Adequate** | `PowerReadoutMessage` gains two nullable primitives. `consider` emits `ConsiderNarrationMessage` if PT-1's `NarrationMessage` base has merged by WP-3, otherwise `PlainMessage`, and the ratchet converts it later (resolved decision 9). |
| Event bus | **Adequate** | Two thin additions with no power values in any payload (INV-6): the `IsOffensive` flag on `AbilityActivatedEvent`, and the new observation event `MobConsideredEvent`, published by an Initiator (INV-5/8) with no gameplay subscriber. Ordering is verified, not assumed (Verified 2, 3). |
| ECS queries | **Adequate** | `HasComponent<AttributesComponent>` is the readability gate. No `is`/`as` (INV-4). |
| Domain-system layering | **Adequate** | Label rule in the read (INV-1/8). `ProgressionSystem` stays stat-free. No DI cycle: the read depends on `IStatSystem`, and nothing on the `IStatSystem → IEffectSystem → contributors → IProgressionSystem` chain depends on the read (INV-24). |
| Persistence | **Adequate** | No new or changed component. Level 1: no new entity paths. Level 2: `ProgressionComponent` is unchanged (already `[Persistent]`). Mob `MobDataComponent` is read-only here. Level 3: no `SaveEntityAsync` anywhere in the slice (INV-22). |
| Time / randomness | **Adequate** | The read is pure. Awards still draw through `IRandom`, and the zero-draw contract is extended and asserted (INV-26). |
| Content templates / reload | **Adequate** | Nothing is authored. Help is on the slice-4 store. |
| Configuration | **Adequate** | Compiled constants, consistent with OD-2. `PowerBudgetTunables` comes from the standards registry, unchanged. |
| Sessions / concurrency | **Adequate** | `consider`/`power` run on the session path and awards on the session/heartbeat paths. There is no shared mutable state and no new thread (INV-31). |
| Modules / composition | **Adequate** | `ProgressionModule` (both hosts) plus the hand-built sandbox graph, mirrored. |
| Simulation sandbox | **Adequate** | Same read type, same call shape, pinned by a parity test (SR11). The use-based sweep blind spot is existing **acknowledged debt** (backlog "Extend the progressionRate sim scenario to use-based accrual"). |

## Flows introduced or modified (INV-17)

- **[flow-31 — Progression journey](../architecture/flows/flow-31-progression-award.md)** (modified, WP-3):
  - Step 1 gains the gather. The read is called per trigger, and the opponent rules are: kill → victim, ability → target only when `IsOffensive`, damage → attacker.
  - Step 2's raw-attribute bullet is replaced by "ratio supplied by the caller; absent ⇒ no ratio; unreadable ⇒ 0 ⇒ ineligible".
  - The `AbilityUse`/`DamageTaken` anti-grind coverage is added.
  - The "slows twice over / threshold grows" wording is fixed: the cumulative total grows, each step costs a flat increment, and the chance decays.
  - The mermaid diagram gains an `RP as IRelativePowerSystem` participant.
  - The index row summary is updated ("combat XP award" → "XP award · challenge gating").
- **[flow-33 — Simulation run](../architecture/flows/flow-33-simulation-run.md)** (modified): the progression-rate leg gathers the ratio from `SandboxWorld.RelativePower` once per kill, before `AwardCombatExperience`.
- **[flow-24 — Abilities journey](../architecture/flows/flow-24-ability-activation.md)** (modified): flow-24 is where `AbilityActivatedEvent`'s payload is documented, since 03-events.md doesn't list it. The event now carries `(ActorEntityId, AbilityId, TargetEntityId?, IsOffensive)`, and and the subscriber note says `AdvancementHandler` gathers the target's ratio for an offensive use **before** the strike resolves.
- **[flow-20 — Death & respawn](../architecture/flows/flow-20-mob-death-respawn.md)** (modified, one line): `AdvancementHandler` reads the victim's **effective** power pre-destroy.
- **[flow-17 — Combat journey](../architecture/flows/flow-17-kill-mob-combat-initiation.md)** (modified, WP-3) gains a pre-initiation "judge the fight" leg, in body and mermaid:
  - `consider <mob>` → shared target lookup → `CanBeAttacked` → `IRelativePowerSystem.Relate` → one label line → `MobConsideredEvent`, published on a successful label only;
  - no state change, no subscriber yet;
  - its index row in `flows/README.md` (~line 24) is updated to name the `consider` leg.
- **No new flow file for `consider`.** It is a read-only command on the standard [flow-03](../architecture/flows/flow-03-player-command-lifecycle.md) journey, documented as flow-17's pre-initiation leg and in the progression feature doc.

## Test plan / Verification (INV-25)

Tier names follow [07-testing.md](../architecture/07-testing.md). The harness is `EntityBuilder`, `FakeRandom`/`CountingRandom`, `RecordingEventBus`, `RecordingOutput`.

**System-unit — `RelativePowerSystemTests` (new, WP-1):**
- `GetEffectivePower` for a player equals `Estimate(Get-snapshot, 0)`. For a mob it equals `Estimate(Get-snapshot, MobDataComponent.Tier)`. Golden values use `PowerBudgetTunables.Default` (Post 15).
- Worn gear, a progression improvement and an active buff each raise `GetEffectivePower`, using a real `StatSystem` with the real contributors. This is the inverse of the deleted `AwardCombatExperience_ignores_worn_gear_and_uses_only_raw_attributes` (Post 3, 8).
- A wounded mob (`CurrentHp` lowered) gives the same reading as at full HP (Post 3).
- `Relate(entity, entity).Ratio == target/subject`. `Relate(subject, power)` equals the entity overload at the same power (shaped for later).
- **Boundaries.** `Classify` at each cut-point uses inclusive lower bounds. `Classify(AntiGrindFloorRatio - 1e-9) == Trivial` and `Classify(AntiGrindFloorRatio) == Easy` (Post 4).
- **One constant, one meaning (cross-system, Post 4).** For each `r` in {`floor - 1e-9`, `floor`, `floor + 1e-9`, `0.0`, `1.0`}, assert `Classify(r) == Trivial` ⇔ `AwardCombatExperience(k, v, r)` awards 0 with zero draws. The test pairs the read with a real `ProgressionSystem` over `CountingRandom`.
- **Unreadable.** A target with no `AttributesComponent`, a subject with no `AttributesComponent`, and a subject with power ≤ 0 each give `IsReadable false, Ratio 0.0, Label null`, without throwing (Post 11).
- **SR7 fixture.** A fresh character (default attributes and pools, no gear) faces synthetic mobs whose snapshot `Estimate` lands on `TargetRange(0,1)` and `TargetRange(0,3)` midpoints. The midpoints are computed from the oracle, never hard-coded. Asserts `≤ Even` and `≥ Dangerous` (Post 5).

**Architecture-guard:**
- Cut-point ordering: `AntiGrindFloorRatio < RelativeEvenFloorRatio < RelativeDangerousFloorRatio < RelativeDeadlyFloorRatio`.
- `ProgressionGuardTests.ProgressionSystem_does_not_depend_on_…` is extended to forbid `IRelativePowerSystem` and `IPowerBudgetSystem` alongside `IStatSystem`/`IEffectSystem` (Post 13).
- `RelativePowerSystem` does not depend on `IProgressionSystem` (no cycle).

**System-unit — `ProgressionSystemTests` (re-pinned, WP-2):**
- `AwardCombatExperience(k, v, ratio)`:
  - `0.2` → amounts 0 and **zero draws** (`CountingRandom`);
  - `1.0` → full base;
  - `3.0` → capped at 1.5×;
  - `0.0` (unreadable) → zero draws (Post 8, 11).
- **New:** an `AbilityUse` with `OpponentPowerRatio = 0.2` gives zero draws (INV-26).
- **New:** an `AbilityUse` with `OpponentPowerRatio = null` gives a draw sequence identical to today's pin (Post 10).
- **New:** a `DamageTaken` with ratio `0.2` gives zero draws. With `1.0`, amounts and draws equal today's (Post 9).
- **New:** `double.NaN`/`∞` → ineligible (defensive `IsFinite`).
- Existing draw-contract, scale-composition and determinism tests are migrated to the ratio input.

**Handler — `AdvancementHandlerTests` (re-pinned, WP-2):** each case uses a real `RelativePowerSystem` over a real `StatSystem`.
- A trivial victim publishes nothing, and a peer publishes per track.
- An offensive `AbilityActivatedEvent` against a trivial target publishes nothing.
- A self-target use (`IsOffensive false`, `TargetEntityId == actor`) is awarded as today.
- An ally-targeted, non-offensive use on another entity (`IsOffensive false`, `TargetEntityId ≠ actor`, with a trivially weak target) applies **no** ratio, and its draw sequence is identical to today's (Post 10).
- An offensive use against an already-destroyed target is ineligible.
- A `CombatRoundEvent` from a trivial attacker gives the defender nothing.
- `KillerEntityId 0` still publishes nothing.

**Flow — `ProgressionAwardFlowTests` (extended, WP-2):**
- End-to-end through the bus: the **same** kill awards less XP once the killer wears gear (SR8 headline).
- A killing-blow `kick` through `AbilityInvocationPipeline` applies the target's ratio, because it was gathered before the strike destroyed the target (Verified 2).
- A self-cast `empower` through `AbilityInvocationPipeline` publishes `AbilityActivatedEvent` with `IsOffensive == false` and a **non-null** `TargetEntityId == actor` (Verified 1). That use gets no ratio.
- **Parity (SR11):** the same pair is composed in the live test world and in `SandboxWorldFactory.Create`, with the same seed. The ratio and the award rows are equal, at Tier 0 and Tier 1. Tier 1 sits on `MobDataComponent.Tier` in the live world and on `AscensionComponent` in the sandbox (Post 12).

**Command — `ConsiderCommandTests` (new, WP-3):**
- One line per label: five fixtures, each tuned to land its ratio in the band. The assertion is on the label-keyed phrase, not on exact prose.
- No digit in any output (regex `\d`) (Post 2).
- Untargetable mob → the can't-fight line, no label.
- `self`/`me` and an item token → creature-only.
- Unknown token, and another player's name in the room → not-here. No argument → "Consider whom?" (Post 6).
- **Event:** a successful label publishes exactly one `MobConsideredEvent`, with `PlayerEntityId`, the mob's `BlueprintComponent` id, and `DangerLabel` equal to the label printed. The `RecordingEventBus` is **empty** after each refusal, not-found, untargetable target and unreadable reading (Post 7).
- Usable while `InCombat`. The actor's pools and cooldowns are unchanged (Post 7).
- If `ConsiderNarrationMessage` ships (resolved decision 9), the message's `Label` equals the event's `DangerLabel`.
- Gear worn between two `consider` calls changes the label (Post 3, SR4).

**Command — `PowerCommandTests` (extended, WP-1):**
- The self, item and mob goldens are unchanged (650/400/712).
- A mob target carries `RelationRatio == mobPower/selfPower` and `RelationLabel == Relate(...).Label`.
- Self and item targets carry null relation fields (Post 14, 15).
- An attribute-less invoker gets the `unreadable` relation.

**System-unit — `ProgressionScenarioExecutorTests` (extended, WP-2; `Hedron.Tests/Simulation/`):**
- A later kill's award reflects the subject's grown effective power. After the subject's first `Body` improvement, kill-N's per-track XP is **lower** than kill-1's at the same base draw, because the ratio is regathered per kill (Main flow 10).

**Architecture-guard — tier mirror (WP-2):**
- Assert `PowerBudgetTunables.Default.TierBaselineStep == AscensionConstants.TierBaselineStep`, and that `PowerBudgetTunables.Default.TrackedScores` equals `AscensionConstants.TrackedScores`.
- This guards the parity caveat in [Planning additions](#planning-additions-2026-10-31): live-mob tier and player/sandbox tier agree only while these values match.

**Sim goldens (WP-2, under `balance-tuning`):** re-pin `SimulationInvariantTests` progression pins if they move.
- `ProgressionRate_EqualPowerReferenceBuild_KillsToFirstImprovement` (10/12/10.48) may hold, since the ratio is exactly 1.0 until the first improvement on either track.
- Later milestones slow as the subject outgrows the victim. That is the intended change.
- Record one `simulate` sweep (equal-power reference builds, seed 2026, N 200, before → after) in the completed record (Post 17).

**Skipped, with reasons:**
- exact phrase prose and the formatter's column layout (presentation);
- `help consider` text and the `combat.yaml` wording (help prose; the slice-4 registry tests cover resolution);
- the `IsOffensive` field on the admin `useability` beyond compile (thin admin plumbing);
- the `RelativePowerLabel` enum itself (pure data).

## Design notes

*(Durable seam rationale. These survive disintegration into `completed/relative-power-consider.md` and the new design doc [`features/progression/relative-power-system.md`](../features/progression/relative-power-system.md), linked from progression.md's doc table (WP-3), plus the other progression design docs. They refine the program's ["One relative-power scale"](phase-5-content-baseline-program.md#design-notes) and ["Progression is gated by challenge"](phase-5-content-baseline-program.md#design-notes) notes for this slice; they don't replace them.)*

- **The relationship is a read, and the label belongs to the read.** A new domain-tier system in `Core/Modules/Progression` (working name `IRelativePowerSystem`) composes `IStatSystem` (effective scores) with the core `IPowerBudgetSystem` oracle. It returns a relation result: subject power, target power, ratio, and label.
  - **Why the label lives here and not in `consider`:** mapping ratio → label is a game rule (INV-1, INV-8), and it has two consumers (`consider` and the `power` relation line). Putting it in either command would mean copying it into the other.
  - **The anti-grind scale stays in `ProgressionSystem`.** Anti-grind consumes the *ratio*. `ProgressionSystem` still owns what the ratio *does* to an award (floor → ineligible, cap). Mechanism and consequence: the read measures the relationship, then the danger label and the XP scale each consume it.
  - **One constant, one meaning (SR5):** the label's *trivial* boundary **is** `ProgressionConstants.AntiGrindFloorRatio`, read by both. It is never a second constant set to the same value.
- **The read is power-keyed underneath.** The entity overload resolves the target's effective power and delegates to a *target-power* overload.
  - **Why build the power overload now:** it costs almost nothing, and later consumers (tiered-overworld R10 entrance telegraphs, area banners, mob-AI threat) compare against a *cell anchor*, not an entity. This is **shaped for later**: the overload exists and is tested, but no consumer is built.
- **The live-entity snapshot moves onto the read.** The way `power` builds a live entity's snapshot (self through `IStatSystem.Get` with tier folded by the contributor; a mob through its effective scores with its authored tier) moves into the read as its snapshot step. `power`, `consider` and anti-grind then all measure an entity the same way. This is the program's INV-19 third consumer. The **item** projection stays on `IItemPowerProjectionSystem`, unchanged.
- **`ProgressionSystem` stays stat-free; the orchestration boundary gathers.** The award methods take the opponent ratio as an *input*. A use-award context carries an optional opponent ratio; `AwardCombatExperience` takes it as a parameter.
  - **Rules:** a rule flagged `AppliesAntiGrindPowerRatio` applies the ratio only when one is supplied. Absent means "no opponent", which keeps today's behaviour (SR10).
  - **Who gathers:** `AdvancementHandler` (live) and `ProgressionScenarioExecutor` (sandbox) are the callers that gather the ratio.
  - **Raw path retired:** `ProgressionSystem`'s own raw-attribute power estimate is deleted with its `IPowerBudgetSystem` dependency. The architecture-guard test is extended: `ProgressionSystem` depends on neither `IStatSystem` nor the relative-power read.
  - **Why not a shared "kill award" façade both callers call:** the program put gathering at the orchestration boundary, and with two callers the drift risk is small. A **parity test** (same pair, live vs. sandbox, same ratio and same award) pins it instead of a new layer (SR11).
- **An opponent named but no longer readable is ineligible, not "no opponent".** If an award names an opponent whose power can't be read when the ratio is gathered (e.g. a killing-blow ability resolves after the target is destroyed), the award is **ineligible**, so it consumes zero draws. It does not fall back to full, ratio-free XP.
  - **Why:** falling back to "no opponent" would let a finishing `kick` on a trivial mob pay full rank XP, which is exactly the outgrown-content payout SR8–SR10 close.
  - The cleaner fix is ordering: gather while the opponent still exists (see the ordering constraints).
- **What effective power means here is only as honest as combat is.** Combat computes attack power, defense and HP from raw `Body` and raw pools. Only gear-folded `AttackPower`/`Defense` reach a fight ([stat-system.md](../features/character-stats/stat-system.md); the backlog's calibration-gap entry). So contributor-folded progression and tier raise *effective power* without making a character win more fights.
  - **Consequence:** until combat reads effective scores, `consider` and anti-grind overstate a progressed character.
  - **Resolution:** that is a combat/stats fix, not a relative-power one. It lands as a **separate Phase 5 gap slice** (resolved decision 7 below). This slice's read is correct as built and needs no change when combat is fixed: labels and ratios simply start matching fight outcomes.

### Planning additions (2026-10-31)

- **Readability is explicit, because `IStatSystem` never says "missing".** `AttributeSystem` returns reference defaults for an entity with no components (Verified 6). A destroyed opponent would therefore read as a fresh 386-power build and silently produce a plausible ratio.
  - So `Relate` gates on `HasComponent<AttributesComponent>` on both sides and on `subjectPower > 0`.
  - An unreadable reading's `Ratio` is defined as `0.0`. The handler then passes `reading.Ratio` verbatim (pure field mapping, INV-8), and the existing floor makes the award ineligible with zero draws.
  - `GetEffectivePower` stays **ungated**, so `power` prints exactly what it prints today, including for stub/attribute-less test entities.
- **Absent vs. unreadable vs. present.** `UseAwardContext.OpponentPowerRatio` is a `double?`:
  - `null` means the trigger named no opponent, so no ratio applies and draws are as today;
  - `0.0` means the opponent was named but unreadable, which is ineligible;
  - any other value is applied.

  `OpponentEntityId` is removed rather than kept beside it. Its only consumer was the deleted raw proxy, and keeping both would invite a second gather path.
- **`IsOffensive` is a fact on the event, not a rule in the handler.** The handler maps "offensive use with a target" → opponent. Deciding *what counts as offensive* stays in `IAbilitySystem.IsOffensive`, computed once by the publisher.
  - The admin `useability` publishes the same fact honestly (`IsOffensive(abilityId) && target.HasValue`), even though it bypasses the combat-entry path. The flag describes the use; it does not describe the code path.
- **Tier source, no `IAscensionSystem`.** The read uses `MobDataComponent.Tier` (0 when absent) as the oracle's tier argument, which is exactly `power`'s rule.
  - Players' tier and sandbox combatants' tier arrive through `AscensionEffectContributor` inside `IStatSystem.Get`.
  - This removes the brief's planned `IAscensionSystem` dependency. A live mob never carries `AscensionComponent`, and a sandbox combatant never sets `MobDataComponent.Tier`. The parity test at Tier 1 pins this.
  - **Parity caveat.** Live-mob tier is computed by the oracle from `PowerBudgetTunables.TierBaselineStep` / `TrackedScores`, sourced from the balance-standards registry. Player and sandbox tier come from the compiled `AscensionConstants` via the contributor. The two agree only while these values match.
  - A WP-2 architecture-guard test asserts that the registry's default step and tracked scores equal `AscensionConstants`. The standards store's existing load-time mirror-drift warning covers an operator-edited file.
- **Phrase mapping is presentation; label mapping is rule.** `ConsiderCommand` owns the label → phrase table. `IRelativePowerSystem.Classify` owns ratio → label. The same split holds for `power`, which prints the label's lower-case name.
- **`con` alias.** This is the MUD-standard abbreviation. As an exact alias it resolves before prefix matching, so `con` stops prefix-resolving to `config`. `conf`/`config` are unaffected.
- **`MobConsideredEvent` is an observation fact, not a gameplay trigger (resolved decision 8).** `consider` stays read-only: no state changes, and no gameplay handler subscribes.
  - The event exists so the playtest recorder can report per-band labels for A7 without parsing prose. Firing only on a successful label keeps its meaning exact: "this player saw this label for this mob".
  - It carries ids and the label only, never power values (INV-6).
- ***Trivial* is unreachable in Tier 0 (resolved decision 11).** At the kept 0.25 floor, anti-grind in Phase 5 scales outgrown kills down without ever zeroing them. The floor is retuned at balance closure alongside slice 5b, and the gap is recorded under balance.md Known gaps.
- **Ratio-based labels compress at higher tiers.** The oracle's tier baseline is additive (+120 power per tier) on a growing base. One tier above is ×1.31 at T0 but only ×1.16 at T3, so a T3 character facing a T4 mob reads *dangerous*, not *deadly*. This is a property of the oracle, not of the read. It is flagged for balance closure, not changed here.

#### Cut-point proposal (SR7 arithmetic)

These are the oracle defaults (`PowerBudgetTunables.Default`, `Core/Systems/PowerBudgetTunables.cs:27-62`; formulas in `PowerBudgetMath.cs`).

- **Fresh character = T0 reference build:** Mind 10×1 + Body 10×10 + Spirit 10×1 + Attunement 10×1 + HpMax 100×2 + AttackPower 5×8 + Defense 2×8. The pool maxima are weighted 0. **Total = 386.**
- **Tier span** = (w(Body) + w(HpMax)) × `TierBaselineStep` = (10 + 2) × 10 = **120**. Each band is 40 wide.
- **T0 target ranges:**

| Cell | Range | Midpoint | Ratio vs. 386 |
|---|---|---|---|
| (0,1) | [386, 426] | 406 | **1.052** |
| (0,2) | [426, 466] | 446 | 1.155 |
| (0,3) | [466, 506] | 486 | **1.259** |
| (1,1) | [506, 546] | 526 | 1.363 |

| Label | Ratio (inclusive lower bound) | Constant |
|---|---|---|
| trivial | `< 0.25` | — |
| easy | `≥ 0.25` | `AntiGrindFloorRatio` (unchanged) |
| even | `≥ 0.85` | `RelativeEvenFloorRatio` (new) |
| dangerous | `≥ 1.12` | `RelativeDangerousFloorRatio` (new) |
| deadly | `≥ 1.30` | `RelativeDeadlyFloorRatio` (new) |

- **SR7 holds.** The whole of B1, [1.000, 1.104], reads *even*. The B3 midpoint (1.259) reads *dangerous*, and all of B3 reads dangerous except its top 4 points, which abut T1 and read *deadly*. All of T1 reads *deadly* to a fresh character.
- **B2's midpoint reads *dangerous*,** as confirmed by resolved decision 12. The 1.16 alternative is rejected.
- **How fast labels move.**
  - The B3 midpoint drops to *even* once the player's power reaches 486/1.12 ≈ 434. That is +48, about 2–3 Body improvements at +20 each.
  - The B1 midpoint reaches *easy* at 406/0.85 ≈ 478, about +92.
  - **B1 reaches *trivial* only at 406/0.25 = 1624.** That is above T6's reference power (1106), so in Tier 0 no B1 mob ever becomes trivial (resolved decision 11: the floor is kept, the gap is recorded in balance.md Known gaps, and it is retuned at balance closure).
- **Validation.** The SR7 fixture test validates the values against the oracle, not against copied numbers. The WP-2 sweep checks them at scale, and content drops 6–8 and A7 recheck them against authored mobs.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Placement

| Part | Layer | Home | Notes |
|---|---|---|---|
| Relative-power read + label | Domain system | `Core/Modules/Progression/Systems/` | Depends on `IStatSystem`, `IPowerBudgetSystem`, and Ascension's tier read (for a mob's authored tier vs. a player's folded tier, as `power` does today) *(Amended at planning: no `IAscensionSystem`. Tier comes from `MobDataComponent.Tier` or the contributor fold.)*. Must not be depended on by `ProgressionSystem` (guard test). Registered in `ProgressionModule`, and in the balance sandbox's world composition. |
| Label enum + cut-points | Data / constants | `Core/Modules/Progression/` + `ProgressionConstants` | The trivial boundary is `AntiGrindFloorRatio`. Three more cut-points (easy/even, even/dangerous, dangerous/deadly) sit beside it. Catalogued in [balance.md](../design/balance.md). |
| Anti-grind input | Domain system signature | `IProgressionSystem` award methods + the use-award context | Optional opponent ratio. The raw-attribute estimate is removed. |
| Anti-grind coverage | Data | `ProgressionConstants.Rules` | `AppliesAntiGrindPowerRatio` on the `AbilityUse` and `DamageTaken` rows. |
| Ratio gathering | Handler + sandbox executor | `AdvancementHandler`; `ProgressionScenarioExecutor` | Field mapping plus one read call each (INV-8: no rule logic in the handler). |
| `consider` | Command (Initiator) | `Core/Modules/Progression/Commands/` | Resolves the target with Combat's room target lookup. The untargetable check uses `ICombatSystem.CanBeAttacked` (SR edge cases), reused rather than re-derived. Prints the line; publishes nothing. *(Amended by resolved decision 8: publishes `MobConsideredEvent` on a successful label only.)* |
| `power` relation line | Command | `Core/Modules/BalanceInspection/Commands/PowerCommand` | Calls the read for mob targets; the live snapshot comes from the read. |
| Help | Content | verb help for `consider`; `content/help/combat.yaml` | — |

### Family dispositions

- **Relative danger:** **build now** in its *input* (entity and power overloads). Consumers beyond `consider`, anti-grind and `power` are **shaped for later**: entrance telegraphs and banners (tiered-overworld R10/Stage A), mob-AI threat/flee (backlogged mob-AI adapter). No new backlog entry is needed; both are already tracked.
- **Anti-grind across sources:** **build now** for every opponent-bearing rule. A future opponent-bearing XP source (e.g. a crafting duel, a PvP rule) joins by setting the flag and supplying the ratio, which is data plus one gather call, not new mechanism.
- **Combat reading effective scores:** **separate gap slice** (resolved decision 7). It is out of this slice; nothing here forecloses it.

### Observers, contributors, event granularity

- **No new events.** `consider` and `power` are read-only. The award's `ExperienceAwardedEvent` / `TrackImprovedEvent` are unchanged; only their *input* moves. *(Amended by resolved decision 8: `consider` publishes one observation event, `MobConsideredEvent`, on a successful label.)*
- **Event payload sufficiency is the planner's to verify:**
  - `CombatRoundEvent` / `AbilityStrikeResolvedEvent` must name the attacker (the defender's opponent).
  - `AbilityActivatedEvent` must expose the target and whether the use was offensive.
  - If a payload lacks a field, the planner adds it as a thin id or flag (INV-6 thin payload), not an enriched power value. The handler gathers power; the event never carries it.
- **Contributors:** no new `IEffectContributor`. The read *consumes* contributor-folded scores and never feeds them (INV-24). That is why it may depend on `IStatSystem` without reopening the progression DI cycle: nothing on the `IStatSystem → IEffectSystem → contributors → IProgressionSystem` chain depends on the read.

### Ordering constraints (INV-7)

- **Kill:** `AdvancementHandler` (priority 20) gathers on `MobDiedEvent` **before** the victim is destroyed. The planner confirms the victim's effective scores are still readable at that priority (the program flagged the same constraint).
- **Ability use:** the ratio is gathered while the target still exists. If `AbilityActivatedEvent` is handled after a killing strike has already destroyed the target, the planner either gathers earlier (from the strike or invocation point) or relies on the ineligible-when-unreadable rule. It must not fall back to ratio-free XP.
- **Damage taken:** the attacker is alive by construction when the round resolves; no constraint beyond today's.

### Invariants in tension

- **INV-1 / INV-2 / INV-8:** the label rule lives in a domain system, and the commands and handlers only call it. `ProgressionSystem` gains no upward or stat dependency; the guard test is extended.
- **INV-19:** `power`'s hand-rolled live snapshot is the third consumer. It migrates in this slice, not as debt.
- **INV-25 / INV-26:** a deliberate balance change, applied under the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) skill with a sweep.
  - **Re-pins:** sandbox progression goldens, anti-grind-dependent tests, and the use-based draw-sequence pins in [progression-system.md](../features/progression/progression-system.md).
  - **New tests:**
    - a below-floor ability use with an opponent (zero draws);
    - a no-opponent ability use (draws unchanged);
    - an opponent that can't be read (ineligible, zero draws);
    - the live/sandbox parity test;
    - the SR7 band-midpoint label fixture.
- **INV-20 (doc accuracy):** the progression doc fixes the program assigns to this slice, plus:
  - power-budget-system.md's consumer list: the `ProgressionSystem.GetEffectivePower` row is replaced by the relative-power read; the "deferred consider" paragraph becomes live;
  - the backlog's `consider` entry is closed.
- **INV-31:** no new shared mutable state or thread. `consider`/`power` run on the session path; the read is stateless.
- **INV-18:** nothing new to author beyond help text. The cut-points are compiled tunables, inspectable through balance.md and the `power` relation line.
- **INV-17:** flow-31 (the ratio gathered in the handler, the raw-attribute step removed, the use-based rows) and flow-33 (the sandbox executor supplies the ratio). The planner decides whether `consider` warrants a flow entry; it is a read-only command.

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-10-31. Do not relitigate.)*

7. **Combat ignores contributor-folded `Body`/`HpMax`** (progression and tier), so effective-power `consider`/anti-grind overstate a progressed character. This is handled as a **separate Phase 5 gap slice**, "combat reads effective scores" (R30), scheduled before content drop 6 with its own balance re-pin. It narrows the Phase 6 calibration item to tier *tuning*. Rejected: folding the combat fix into this slice; accepting the gap for all of Phase 5.

**Advisor calls** *(cheap to veto at planning)*:
- the label mapping lives on the relative-power read, not in `consider`;
- the trivial boundary *is* `AntiGrindFloorRatio`, never a copy;
- the target-power overload is built and tested now with no new consumer;
- a parity test instead of a shared kill-award façade;
- an opponent named but unreadable is **ineligible**, never ratio-free;
- `consider` reuses Combat's target lookup and `CanBeAttacked`;
- event payload gaps are filled with thin ids/flags, never power values.

## Open questions

### Resolved at planning (2026-10-31, code-verified)

- **Event payloads:** combat events already name the attacker. `AbilityActivatedEvent` gains `bool IsOffensive` ([Verified 1](#verified-in-code-2026-10-31)).
- **Ability-use ordering:** the target is live at `AbilityActivatedEvent` because the event is published before the strike, so the ratio is gathered there. Unreadable ⇒ ineligible stays as a backstop ([Verified 2](#verified-in-code-2026-10-31)).
- **Where each use-based award resolves its opponent:**
  - all in `AdvancementHandler`;
  - kill → victim;
  - offensive ability → target;
  - damage → attacker ([Main flow](#main-flow) 7–9).
- **Default cut-points:** 0.25 / 0.85 / 1.12 / 1.30 ([arithmetic](#cut-point-proposal-sr7-arithmetic)), confirmed as resolved decision 12. They are validated by the SR7 fixture and the sweep, and floor and cap are unchanged.
- **Degenerate or unreadable power:** `IsReadable false`, `Ratio 0.0`, `Label null`. The award is ineligible with zero draws. `consider` prints "You can't tell how dangerous \<Name\> is.", and `power` prints `vs you : unreadable` ([Planning additions](#planning-additions-2026-10-31)).

### Proposed phrasing (planner; wording only, cheap to change)

| Case | Line |
|---|---|
| trivial | "\<Name\> would be no challenge at all." |
| easy | "\<Name\> should be easy prey." |
| even | "\<Name\> looks like an even fight." |
| dangerous | "\<Name\> looks dangerous." |
| deadly | "\<Name\> would almost certainly kill you." |
| untargetable | "\<Name\> is not something you can fight." |
| self / item | "You can only size up creatures." |
| unreadable | "You can't tell how dangerous \<Name\> is." |
| no argument | "Consider whom?" |
| no match, or another player's name | "You don't see that here." (`kill`'s) |

`<Name>` is the authored `MobDataComponent.Name` with its first character upper-cased.

### Planning forks: resolved

All four forks, plus the cut-points, were decided by the user on 2026-10-31. They are recorded as [resolved decisions 8–12](#resolved-decisions):

- **8:** `MobConsideredEvent` is added, published on a successful label only.
- **9:** `ConsiderNarrationMessage` if PT-1 has merged, otherwise `PlainMessage`.
- **10:** targets mirror `kill`.
- **11:** the floor stays 0.25, and the Tier-0 *trivial* gap is recorded.
- **12:** cut-points 0.25 / 0.85 / 1.12 / 1.30, with B2 reading *dangerous*.

### Still open (user/designer)

- **Mob names that encode band** (e.g. R1's `Band-2 Wolf` example). Should dev-world mob names avoid encoding tier/band, so that `consider` stays the danger read? For example, `Gray Wolf` vs `Dire Wolf`, while staying generic and lore-free. *(User + designer, at content drop 6; may amend the program's R1 example.)*
- **Final label wording** beyond the proposed set above. This is wording only. *(Designer.)*

## Related

- Program: [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md) (slice 5; slice 5b [`combat-effective-scores`] is separate and repoints no stat reads here).
- Playtest consumers: [`playtest-run-mode.md`](playtest-run-mode.md) (resolved decision 4, A7) · [`playtest-world-substrate.md`](playtest-world-substrate.md) (`NarrationMessage` ratchet).
- Features: [`progression.md`](../features/progression/progression.md) · [`progression-system.md`](../features/progression/progression-system.md) · [`power-budget-system.md`](../features/progression/power-budget-system.md) · [`combat.md`](../features/combat/combat.md) · [`mobs.md`](../features/mobs/mobs.md).
- Flows: [flow-31](../architecture/flows/flow-31-progression-award.md) · [flow-33](../architecture/flows/flow-33-simulation-run.md) · [flow-24](../architecture/flows/flow-24-ability-activation.md) · [flow-20](../architecture/flows/flow-20-mob-death-respawn.md) · [flow-17](../architecture/flows/flow-17-kill-mob-combat-initiation.md) · [flow-03](../architecture/flows/flow-03-player-command-lifecycle.md).
- Design: [balance.md](../design/balance.md) · [power-model.md](../design/power-model.md).
- Completed precedents: [`progression-use-based-xp.md`](../roadmap/completed/progression-use-based-xp.md) · [`power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md) · [`progression-rate-scenarios.md`](../roadmap/completed/progression-rate-scenarios.md).
- Skills: [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) · [`edit-progression-system`](../../.claude/skills/edit-progression-system/SKILL.md) · [`add-tests`](../../.claude/skills/add-tests/SKILL.md).
