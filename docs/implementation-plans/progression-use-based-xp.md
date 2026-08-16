# Use-based progression: skill tracks, award chance, tunable scales

**Status:** `planned`
**Actors:** Player, System
**Module:** `Core/Modules/Progression/` (owner) · `Core/Modules/Abilities/` · `Core/Modules/Combat/` · `Core/Modules/Session/` (new preferences surface) — feature: [`docs/features/progression/`](../features/progression/)

## Description

The progression substrate (`prog-1`) awards XP on one trigger only — a combat kill — to two
attribute tracks, and surfaces it through one admin-ish `progress` command with no narration.
A character therefore has no felt progression loop: gear is the only visible growth. This slice
closes that by making progression **use-driven, visible, chance-gated, and tunable**:

1. **Skills accrue their own XP** and `skills` / `spells` / `abilities` show each ability's rank,
   cumulative XP, and XP-to-next. Ability rank is **display-only this slice** — it grants no power
   (see [Decision D3](#d3--ability-rank-is-display-only-this-slice)).
2. **XP is awarded on use, by chance** — using an ability and taking damage each roll for an
   award rather than granting one deterministically, so progression is not linear in action count.
3. **Every award narrates** ("You feel more practiced with *kick*." / "Your Body improves!"), and
   the player can turn each narration class off with a new `toggle` command.
4. **Two tiers of tuning** — one `GlobalXpScalar` that moves all progression at the macro level,
   and per-source / per-ability / per-mob scales for fine tuning.

Adding two sources brings the total to three (kill, ability use, damage taken), which is exactly
the **INV-19 framework-at-the-third-repetition** threshold the `edit-progression-system` skill names.
So this slice does *not* add two more bespoke handlers — it promotes the trigger wiring to a
**single advancement handler over an award-rule registry**, and re-expresses the existing combat-kill
award as the first row of that table.

## Requirements (owner-confirmed 2026-08-16)

| # | Requirement | Where it lands |
|---|---|---|
| R1 | A player can see XP for their skills when checking skills | WP3 — `skills`/`spells`/`abilities` rank block |
| R2 | A message is emitted when XP is awarded to a skill or an attribute | WP3 — `ProgressionNarrationHandler` |
| R3 | A message is emitted when a skill or attribute is gained (improves) | WP3 — same handler, `TrackImprovedEvent` branch |
| R4 | The player can switch that messaging on and off | WP3 — `PreferencesComponent` + `toggle` command |
| R5 | XP is awarded based on **use**, with a **chance** to be awarded (non-linear) | WP1 (chance mechanism) + WP2 (the two sources) |
| R6 | A **global** XP scalar tunes progression at the macro level | WP1 — `ProgressionConstants.GlobalXpScalar` |
| R7 | Individual XP-granting things carry their own **granular** scales | WP1 (per-source, per-rule) + WP2 (per-ability, per-mob) |

Confirmed scope calls: XP sources this slice are **ability use** and **damage taken** (plus the
existing kill); ability rank is **shown but grants no power**; the toggle lands as a **general
preferences framework**; delivered as **one plan, three work packages**.

## Architecture brief

### D1 — Track key widens from `ScoreId` to `ProgressionTrack`

`IProgressionSystem` keys a track by `ScoreId` today, and the `edit-progression-system` guardrail
says *"Track key = `ScoreId`; no parallel `TrackId` enum."* R1 needs per-ability tracks, and an
ability id is a `string`, not a `ScoreId`. The guardrail's intent is *one progression engine, one
track vocabulary* — not literally *one CLR type*. It is satisfied by **widening** the key rather
than forking it:

```csharp
public readonly record struct ProgressionTrack(ScoreId? Score, string? AbilityId)
{
    public static ProgressionTrack Of(ScoreId score);          // "Body"
    public static ProgressionTrack Ability(string abilityId);  // "ability:kick"
    public bool IsAbility { get; }
    public string ToKey();                                     // dictionary/JSON key
    public static bool TryParse(string key, out ProgressionTrack track);
}
```

One engine, one component, one threshold curve, one `XpSource` vocabulary. The rejected
alternative — a second XP store inside `Core/Modules/Abilities/` — is a literal second improvement
engine, which the skill's *"Abilities: … do **not** build a second improvement engine"* guardrail
forbids outright.

**Serialization is backward compatible by construction.** A score track's key renders as the bare
enum name (`"Body"`, `"HpMax"`) — byte-identical to what `ProgressionComponent` writes today — and
ability tracks take the reserved `ability:` prefix, which no `ScoreId` name can collide with.
Existing player snapshots load unchanged; **no migration**. `AbilitiesComponentJsonConverter` is
the precedent for the converter shape.

`IProgressionSystem` keeps `ScoreId` overloads that delegate to the `ProgressionTrack` form, so
`ProgressionEffectContributor`, `ProgressCommand`, `AscensionSystem`, and the simulation executors
compile untouched.

### D2 — Three sources ⇒ promote to the advancement-rule registry (INV-19)

Instead of `AbilityUseXpHandler` + `DamageTakenXpHandler` joining `ExperienceAwardHandler`, one
`AdvancementHandler` subscribes to the trigger events and consults `IAdvancementRuleRegistry`:

```csharp
public sealed record AdvancementRule(
    XpSource Source,
    IReadOnlyList<ProgressionTrack> Tracks, // static tracks (attributes)
    bool IncludesSubjectTrack,              // + the ability's own track, when the trigger names one
    int BaseAwardMin, int BaseAwardMax,     // IRandom.Next(min, max+1)
    double BaseChance,                      // probability the use awards at all (R5)
    double ChanceDecayPerImprovement,       // chance falls as the track matures (anti-grind for use)
    double SourceScale,                     // granular per-source knob (R7)
    bool AppliesAntiGrindPowerRatio);       // kill-only: the existing victim/killer ratio
```

Rows are **hardcoded** in `ProgressionConstants.Rules` (Spine F registry shape, Category 3);
promotion to YAML is deferred to a demonstrated recompile-free need (OD-2) — the same posture the
existing constants carry. The registry is the seam a later trainer/objective/exploration source
adds a *row* to, not a handler.

`ProgressionSystem` gains one entry point that all three sources flow through:

```csharp
UseAwardResult AwardUseExperience(uint entityId, XpSource source, UseAwardContext context);
// context: optional subject ability id, optional counterpart entity (kill victim / attacker),
//          optional content scale (per-ability / per-mob), optional magnitude hint (damage taken)
```

`AwardCombatExperience` stays on the interface as a thin wrapper over `AwardUseExperience` with
the `CombatKill` row, so `ExperienceAwardHandler`'s existing behaviour and the sim executor's
`AwardCombatExperience` seam (`ProgressionScenarioExecutor`) are preserved.

### D3 — Ability rank is display-only this slice

`ProgressionEffectContributor.GetModifiers(entityId, ScoreId)` is keyed by `ScoreId` and folds only
**score** tracks; ability tracks are skipped in both `GetModifiers` and `GetActive`. Ability rank
therefore contributes **zero** estimated power, the power oracle is untouched, and no balance
standard or sim golden moves because of it. Making rank scale potency/cost is a deliberate later
balance slice that must fold into [`power-model.md`](../design/power-model.md) and re-pin goldens —
explicitly **out of scope here**, and recorded as such in the completed record so the next slice
starts from a stated position rather than rediscovering it.

An architecture-guard test pins this: *no ability track ever produces a non-zero modifier*.

### D4 — Chance and scale composition (R5–R7)

For each candidate track of a fired rule:

```
eligible?  = rule preconditions pass (attributable actor, positive damage, non-trivial victim…)
chance     = clamp01(rule.BaseChance / (1 + improvements(track) × rule.ChanceDecayPerImprovement))
roll       = _random.NextDouble() < chance          // one draw per candidate track
amount     = round( _random.Next(min, max+1)
                    × GlobalXpScalar                // R6, macro
                    × rule.SourceScale              // R7, per-source
                    × contentScale                  // R7, per-ability / per-mob
                    × antiGrindScale )              // kills only, existing ratio math
```

Two properties the tests pin: **no draw happens when the candidate is ineligible** (preserves the
existing "a trivial victim consumes no randomness" determinism contract, INV-26), and **`chance`
decays with rank**, so use-based gain is sub-linear in action count without curving the power step
(the curve stays in the threshold — the skill's central rule).

`GlobalXpScalar` defaults to `1.0` and every shipped rule's `SourceScale` to `1.0`, with the
`CombatKill` row carrying today's `CombatAwardMin/Max`, `AntiGrindFloorRatio`, `AntiGrindCap` and
`CombatTracks` values verbatim. **Kill-award behaviour is therefore numerically unchanged and
`SimulationInvariantTests`' pinned goldens must not move** — if they do, WP1 has a bug, not a
tuning change. That is the WP1 exit criterion.

### D5 — Player preferences are a framework, not a bool (INV-19)

Player-configurable output is a **new player-facing surface**, so it lands its framework in this
slice rather than two bespoke bools on `ProgressionComponent`:

- `PreferencesComponent` (`[Persistent]`, `Dictionary<PreferenceId, bool>`, absent key = the
  registry default) in `Core/Modules/Session/Components/`.
- `PreferenceId` enum, seeded with `ProgressionXpMessages` and `ProgressionImprovementMessages`.
- `IPreferenceSystem` — `IsEnabled(entityId, id)`, `Set(...)`, `GetAll(entityId)`; returns values,
  publishes nothing (INV-5).
- `toggle` command: bare `toggle` lists every preference with its state; `toggle <name>` flips it;
  `toggle <name> on|off` sets it explicitly.

Narration handlers ask `IPreferenceSystem` before writing. Future opt-outs (combat spam, currency
lines, tells) become enum rows, not new plumbing.

## Preconditions

- The invoking entity is a live player entity with `PersistentEntity` (progression and preferences
  are only ever attached to persistent entities — INV-23).
- The progression substrate (`prog-1`), ability substrate, and combat round loop are in place.
- For ability-use awards: the ability is known and successfully activated (a failed/blocked
  invocation publishes no `AbilityActivatedEvent`, so it cannot award).

## Postconditions (the coverage contract)

1. Using a known ability publishes `AbilityActivatedEvent`; the advancement handler rolls the
   `AbilityUse` rule and, on success, awards XP to **the ability's own track** and to the ability's
   configured attribute track.
2. Taking damage in a melee round or from an ability strike rolls the `DamageTaken` rule for the
   **defender** and, on success, awards XP to that rule's attribute tracks.
3. A kill awards exactly what it awards today — same tracks, same amounts, same anti-grind, same
   RNG draw count at a fixed seed.
4. Every positive award publishes one `ExperienceAwardedEvent`; every threshold crossing publishes
   one `TrackImprovedEvent` (unchanged contract, widened track key).
5. With `ProgressionXpMessages` enabled (default), each award writes one line to the earning player
   only; with it disabled, none. Same for `ProgressionImprovementMessages` and improvement lines.
6. `skills` / `spells` / `abilities` show rank, cumulative XP, and XP-to-next for every known
   ability that has ever earned XP; abilities with no XP show rank 0.
7. `progress` shows score tracks and ability tracks in separate blocks.
8. `toggle` lists and flips every registered preference; the setting survives a persistence
   round-trip.
9. Ability tracks contribute **0** to `IStatSystem.Get` for every score.
10. Setting `GlobalXpScalar` to `2.0` exactly doubles every awarded amount (rounding aside), and a
    per-ability/per-mob scale multiplies only awards that ability/mob produced.

## Main flow — ability use awarding XP

1. Player invokes a known ability; `AbilityInvocationPipeline` resolves costs/target and publishes
   `AbilityActivatedEvent(actor, abilityId, target)`.
2. `AdvancementHandler` (priority `HandlerPriority.Domain`) receives it, resolves the actor is a
   progression-eligible entity, and calls
   `IProgressionSystem.AwardUseExperience(actor, XpSource.AbilityUse, context{ subject: abilityId, contentScale: definition.XpScale })`.
3. `ProgressionSystem` looks up the `AbilityUse` rule, builds the candidate track list (the
   ability's own track + the ability's `XpAttributeTrack`, falling back to the rule's static
   tracks), and for each candidate: computes rank-decayed chance, draws `IRandom.NextDouble()`,
   and on success draws + scales the base amount and calls `AwardExperience`, which accrues XP and
   resolves threshold crossings. It returns a `UseAwardResult`; **it publishes nothing** (INV-5).
4. `AdvancementHandler` publishes one `ExperienceAwardedEvent` per positive row and one
   `TrackImprovedEvent` per threshold crossed (INV-8).
5. `ProgressionNarrationHandler` (priority `HandlerPriority.Notification`) receives each, checks
   the relevant `PreferenceId` via `IPreferenceSystem`, and — when enabled — writes the line to the
   earning player.
6. The player types `skills`; `SkillsCommand` reads rank/XP/to-next per ability from
   `IProgressionSystem` and renders them alongside the existing cooldown/cost block.

Damage-taken follows the same shape from `CombatRoundEvent` / `AbilityStrikeResolvedEvent`, with
the **defender** as the earner and `Result.DamageDealt > 0` as the eligibility precondition.

## Events fired

| Event | Publisher | Change |
|---|---|---|
| `AbilityActivatedEvent` | `AbilityInvocationPipeline`, `UseAbilityCommand` | none — new subscriber only |
| `CombatRoundEvent` | `CombatTickHandler` | none — new subscriber only |
| `AbilityStrikeResolvedEvent` | invocation commands | none — new subscriber only |
| `MobDiedEvent` | combat/death | none — existing subscriber rewired internally |
| `ExperienceAwardedEvent` | `AdvancementHandler` | **widened**: `ScoreId Track` → `ProgressionTrack Track` |
| `TrackImprovedEvent` | `AdvancementHandler` | **widened**: same |
| `PreferenceChangedEvent` | `ToggleCommand` | **new** — thin past-tense fact; narration confirms from it |

No new trigger event is needed: `CombatRoundEvent.Result.DamageDealt` and
`AbilityStrikeResolvedEvent.Result.DamageDealt` already carry everything the damage-taken rule reads.

## Systems / handlers involved

| Piece | Layer | Status |
|---|---|---|
| `IProgressionSystem` / `ProgressionSystem` | Domain | extended (`AwardUseExperience`, `ProgressionTrack` keys, chance/scale composition) |
| `IAdvancementRuleRegistry` / `AdvancementRuleRegistry` | Domain | **new** — rule rows keyed by `XpSource` |
| `AdvancementHandler` | Handler | **new** — one handler over all trigger events; replaces `ExperienceAwardHandler` |
| `ProgressionNarrationHandler` | Handler | **new** — award/improvement lines, preference-gated |
| `IPreferenceSystem` / `PreferenceSystem` | Domain | **new** |
| `ToggleCommand` | Command (Initiator) | **new** |
| `ProgressionEffectContributor` | Domain (read seam) | touched — skips ability tracks (D3) |
| `SkillsCommand` / `SpellsCommand` / `AbilitiesCommand` / `ProgressCommand` | Commands | touched — rank/XP display |
| `IPowerBudgetSystem` | Core | unchanged |

`ExperienceAwardHandler` is **deleted**, its `MobDiedEvent` subscription absorbed by
`AdvancementHandler` — its tests migrate rather than duplicate.

## Implementation plan — work packages

### WP1 — Track widening, award-rule registry, chance + scale composition

**Scope.** The engine changes, with **zero behavioural delta** at default constants.

**Files.**
- `Core/Modules/Progression/ProgressionTrack.cs` (new) + `ProgressionTrackJsonConverter.cs` (new)
- `Core/Modules/Progression/Components/ProgressionComponent.cs` — dictionaries rekeyed
- `Core/Modules/Progression/AdvancementRule.cs`, `Systems/IAdvancementRuleRegistry.cs`,
  `Systems/AdvancementRuleRegistry.cs` (new)
- `Core/Modules/Progression/ProgressionConstants.cs` — add `GlobalXpScalar`; move the combat
  numbers into the `CombatKill` rule row; keep the existing names as the row's values
- `Core/Modules/Progression/Systems/IProgressionSystem.cs`, `ProgressionSystem.cs` —
  `AwardUseExperience`, `UseAwardContext`, `UseAwardResult`, `ScoreId` overloads retained
- `Core/Modules/Progression/Events/*.cs` — widened track field
- `Core/Modules/Progression/Handlers/AdvancementHandler.cs` (new, `MobDiedEvent` only in this WP);
  `ExperienceAwardHandler.cs` deleted
- `Core/Modules/Progression/ProgressionModule.cs` — registrations
- `Core/Modules/Progression/ProgressionEffectContributor.cs` — ability tracks skipped

**Out of scope.** No new XP source is wired; no display change; no narration; no preferences.

**Exit criterion.** `dotnet test` green with **`SimulationInvariantTests` goldens unmoved**;
a persistence round-trip test proves a pre-existing score-only `ProgressionComponent` JSON payload
loads and re-serializes byte-identically.

### WP2 — The two use-based sources + granular content scales

**Scope.** Wire `AbilityUse` and `DamageTaken` rows and the per-content scales they read.

**Files.**
- `Core/Modules/Progression/XpSource.cs` — add `AbilityUse`, `DamageTaken`
- `Core/Modules/Progression/ProgressionConstants.cs` — the two new rule rows
- `Core/Modules/Progression/Handlers/AdvancementHandler.cs` — subscribe
  `AbilityActivatedEvent`, `CombatRoundEvent`, `AbilityStrikeResolvedEvent`
- `Core/Modules/Abilities/AbilityDefinition.cs` — `double XpScale = 1.0`,
  `ScoreId? XpAttributeTrack = null` (both optional, defaults preserve current YAML)
- ability YAML deserializer + `Core/Modules/Abilities/AbilityRegistry.cs` — read the new fields
- `Core/Modules/Mobs/Templates/MobTemplate.cs` — `double XpScale = 1.0`; applied to the
  `CombatKill` award via `MobDataComponent`
- `Core/Modules/Mobs/Commands/SetMobCommand.cs` — `setmob xpscale <value>`
- Blazor `MobEditor` / ability editor — the new fields

**Out of scope.** Display and narration (WP3). No change to what a kill awards.

**Exit criterion.** A player using an ability repeatedly accrues XP on that ability's track at a
rate matching the configured chance (seeded `IRandom`); taking damage accrues the defender's
tracks; `setmob xpscale 0` on a mob makes its kills award nothing.

### WP3 — Visibility, narration, and the preferences framework

**Scope.** Everything the player sees and controls.

**Files.**
- `Core/Modules/Session/Components/PreferencesComponent.cs`, `PreferenceId.cs`,
  `Systems/IPreferenceSystem.cs`, `PreferenceSystem.cs`, `Commands/ToggleCommand.cs`,
  `Events/PreferenceChangedEvent.cs` (all new)
- `Core/Modules/Progression/Handlers/ProgressionNarrationHandler.cs` (new)
- `Core/Output/AbilityProgressLine.cs` or an extension of `AbilityDisplayMessage` — rank/XP block
- `Core/Modules/Abilities/Commands/AbilitiesCommand.cs` — all three verbs render the rank block
- `Core/Output/ProgressDisplayMessage.cs` + `Commands/ProgressCommand.cs` — ability-track block
- `Core/Modules/Session/SessionModule.cs` (or the owning module entry point) — registrations
- `docs/` updates (below)

**Out of scope.** Prompt-bar XP display; per-channel output filtering beyond the two progression
preferences; any preference that is not a boolean.

**Exit criterion.** Manual and Tier-3 verification of Postconditions 5–8: messages appear, `toggle`
silences them, `skills` shows rank/XP, and the setting survives a restart.

The primary agent runs `architecture-reviewer` (code mode) across the combined WP1–WP3 diff before
merge, and the spec gate runs against **this document** before WP1 starts.

## Content tooling impact (INV-18)

This slice adds gameplay state (ability tracks, per-content XP scales, player preferences), so it
ships the tooling to author and inspect all of it in the same PR:

| State | Author | Inspect |
|---|---|---|
| Per-ability `XpScale` / `XpAttributeTrack` | ability YAML fields + the Blazor ability editor | `defs ability <id>` readout |
| Per-mob `XpScale` | `setmob xpscale <value>` + Blazor `MobEditor` field + YAML | `defs mob <id>` / mob editor readout |
| Award-rule rows | `ProgressionConstants.Rules` (Category 3, compiled — same posture as today's constants) | new admin `progress rules` sub-view listing each row's source, tracks, chance, and scales |
| `GlobalXpScalar` | `ProgressionConstants` (Category 3) | shown in the `progress rules` header |
| Ability track XP | earned in play | `skills` / `spells` / `abilities` / `progress` |
| Preferences | `toggle` | bare `toggle` lists every preference and its state |

**Why the rule table stays compiled:** the existing progression constants are Category 3 and
CI-pinned; moving them to YAML in the same slice that changes their shape would break the pinning
contract twice. The registry interface is the promotion seam — YAML rows land when a designer
needs recompile-free tuning (OD-2), tracked in [`backlog.md`](../roadmap/backlog.md).

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Note |
|---|---|---|
| XP-trigger wiring (3rd source) | **Gap exposed → closed here** | The bespoke-handler-per-source pattern hits its third repetition; promoted to `IAdvancementRuleRegistry` + one handler, exactly as `edit-progression-system` prescribes |
| Player-configurable output | **Gap exposed → closed here** | New player-facing surface; lands as `PreferencesComponent` + `IPreferenceSystem` + `toggle`, not a bespoke bool |
| Direct-to-entity output | **Acknowledged debt** | `IBroadcastSystem` has no `SendToEntityAsync`; `CurrencyAwardNarrationHandler` fakes it with `SendToRoomAsync` + a recipient-only predicate, and this slice's narration handler is the **second** use. At the third, promote per INV-19 — logged to [`backlog.md`](../roadmap/backlog.md) rather than expanded here |
| Persistence (component shape change) | **Adequate** | `ProgressionTrack` keys are a superset of today's enum-name keys; a round-trip test pins byte-identical output for score-only payloads. No migration framework needed |
| Power model / balance oracle | **Adequate** | Ability rank grants no power (D3); `GlobalXpScalar` defaults to 1.0; kill awards numerically unchanged, so no golden re-pin |
| Determinism seam (`IRandom`) | **Adequate** | All chance flows through the injected seam; draw *count* is pinned by test so ineligible candidates stay draw-free (INV-26) |
| Concurrency posture (INV-31) | **Adequate** | No new background initiator or shared mutable singleton. `AdvancementHandler` and `ProgressionNarrationHandler` run on the existing event-bus path off session/heartbeat threads; the registry is immutable after construction |

## Balance-catalog parity

Per [`balance.md`](../design/balance.md) rule 5, the knob catalog gains rows in the same PR:
`GlobalXpScalar`, the `Rules` table (per-source base range, chance, chance decay, source scale),
per-ability `XpScale`, per-mob `XpScale`. Rule 3 (re-validate and re-pin) is satisfied *negatively* —
defaults are chosen so no pinned number moves; **that no-move is the WP1 exit criterion**, not an
assumption. Any later deliberate change to these defaults runs a `progressionRate` sim sweep and
re-pins in its own commit.

## Flows introduced or modified (INV-17)

- [`flow-31-progression-award.md`](../architecture/flows/flow-31-progression-award.md) — **rewritten**
  from the kill-only path to the generalized rule-table path: trigger event → `AdvancementHandler`
  → rule lookup → chance/scale composition → award → events → preference-gated narration.
- [`flow-24-ability-activation.md`](../architecture/flows/flow-24-ability-activation.md) — **extended**
  with the advancement branch off `AbilityActivatedEvent`.
- [`flow-20-mob-death-respawn.md`](../architecture/flows/flow-20-mob-death-respawn.md) — **touched**:
  the XP step now names `AdvancementHandler`.
- New flow for `toggle` is **not** warranted — it is an ordinary command on
  [`flow-03-player-command-lifecycle.md`](../architecture/flows/flow-03-player-command-lifecycle.md).

## Test plan / Verification (INV-25)

**Tier 1 — system decisions** (`Hedron.Tests/Progression/`)
- `ProgressionTrack` round-trips: score key ⇄ `"Body"`, ability key ⇄ `"ability:kick"`, unknown
  key rejected, `ability:` prefix cannot collide with a `ScoreId` name.
- Scale composition: global × source × content × anti-grind, including `GlobalXpScalar = 2.0`
  doubling and `contentScale = 0` awarding nothing.
- Chance: a fake `IRandom` just below/at/above the threshold awards/doesn't; chance decays with
  improvement count; chance clamps to [0,1].
- **Draw-count determinism:** an ineligible candidate consumes no `IRandom` draw (extends the
  existing trivial-victim assertion).
- Threshold crossing on ability tracks matches score tracks (one curve).
- `AdvancementRuleRegistry` returns the right row per `XpSource`; an unmapped source is a no-op.

**Tier 2 — handler orchestration**
- `AdvancementHandler`: publishes one `ExperienceAwardedEvent` per positive row and N
  `TrackImprovedEvent`s per N crossings, for each of the three trigger events; `KillerEntityId == 0`
  and `DamageDealt == 0` are discarded; a non-player actor awards nothing.
- `ProgressionNarrationHandler`: writes on award/improvement when the preference is enabled, writes
  nothing when disabled, and never writes to a non-earner.
- Migrated from `ExperienceAwardHandlerTests` rather than duplicated.

**Tier 3 — flows** (`Hedron.Tests/Progression/ProgressionAwardFlowTests.cs`, extended)
- Use ability → XP on the ability track → threshold → `skills` shows the new rank.
- Take damage → defender's tracks accrue; attacker's do not.
- `toggle progressionxp off` → subsequent award produces no output but the XP still accrues.
- Existing kill-award flow test unchanged and still green.

**Persistence**
- `ProgressionComponent` round-trip with mixed score + ability keys.
- **Back-compat:** a pre-slice score-only payload deserializes, and re-serializes byte-identically.
- `PreferencesComponent` round-trip; absent key falls back to the registry default.

**Architecture guard** (`Hedron.Tests/Architecture/`)
- No ability track ever yields a non-zero `IEffectContributor` modifier (pins D3).
- `ProgressionSystem` and `PreferenceSystem` hold no `IEventBus` dependency (INV-5).
- `ProgressionSystem` does not depend on `IStatSystem`/`IEffectSystem` (the standing DI-cycle guard).

**Simulation**
- `SimulationInvariantTests` run unchanged; **any movement in a pinned golden fails WP1**.

**Not tested, and why:** the exact wording of narration strings (presentation, asserted only as
"a line was/wasn't written"); the Blazor editor field bindings for `XpScale` (covered by the
existing editor-binding tier's pattern, extended only if that tier already covers sibling fields);
long-horizon progression *balance* (that is the `progressionRate` sim sweep's job, run when the
defaults are deliberately tuned — not a unit test).

## Docs updated on ship (INV-27–INV-30)

[`features/progression/progression.md`](../features/progression/progression.md) and
[`progression-system.md`](../features/progression/progression-system.md) (use-based sources, the
rule table, chance, scales, the display-only ability rank); a new
`features/session/preferences-system.md`; [`design/balance.md`](../design/balance.md) knob rows;
[`reference/systems.md`](../reference/systems.md), [`handlers.md`](../reference/handlers.md),
[`components.md`](../reference/components.md); the three flows above; and
`.claude/skills/edit-progression-system/SKILL.md` — whose "add an XP source" recipe becomes
*"add a rule row"* and whose track-key guardrail is restated as the D1 widening. This plan is then
deleted, with decisions D1–D5 carried into
`roadmap/completed/progression-use-based-xp.md`.
