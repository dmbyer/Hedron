# Use-based progression: skill tracks, award chance, tunable scales

**Status:** `planned`
**Actors:** Player, System
**Module:** `Core/Modules/Progression/` (owner) · `Core/Modules/Abilities/` · `Core/Modules/Combat/` · `Core/Modules/Preferences/` (new) — feature: [`docs/features/progression/`](../features/progression/)

> **Spec-gate history.** Reviewed in spec mode 2026-08-16; six blocking findings fixed in place
> (RNG draw contract, ability-authoring scope, direct-to-entity debt count, the planned
> `PlayerConfigurationComponent` reconciliation, the attribute-track power leak, and missing files
> in the work packages). Those corrections are folded into the sections below and summarized in
> [Spec-review corrections](#spec-review-corrections).

## Description

The progression substrate (`prog-1`) awards XP on one trigger only — a combat kill — to two
attribute tracks, and surfaces it through one `progress` command with no narration. A character
therefore has no felt progression loop: gear is the only visible growth. This slice closes that by
making progression **use-driven, visible, chance-gated, and tunable**:

1. **Skills accrue their own XP** and `skills` / `spells` / `abilities` show each ability's rank,
   cumulative XP, and XP-to-next. Ability rank is **display-only this slice** — it grants no power
   (see [D3](#d3--ability-rank-is-display-only-attribute-tracks-still-grant-power)).
2. **XP is awarded on use, by chance** — using an ability and taking damage each roll for an award
   rather than granting one deterministically, so progression is not linear in action count.
3. **Every award narrates** ("You feel more practiced with *kick*." / "Your Body improves!"), and
   the player can turn each narration class off with a `config` command.
4. **Two tiers of tuning** — one `GlobalXpScalar` that moves all progression at the macro level,
   and per-source / per-ability / per-mob scales for fine tuning.

Adding two sources brings the total to three (kill, ability use, damage taken), which is exactly
the **INV-19 framework-at-the-third-repetition** threshold the `edit-progression-system` skill
names. So this slice does *not* add two more bespoke handlers — it promotes the trigger wiring to a
**single advancement handler over an award-rule registry**, and re-expresses the existing
combat-kill award as the first row of that table.

## Requirements (owner-confirmed 2026-08-16)

| # | Requirement | Where it lands |
|---|---|---|
| R1 | A player can see XP for their skills when checking skills | WP3 — `skills`/`spells`/`abilities` rank block |
| R2 | A message is emitted when XP is awarded to a skill or an attribute | WP3 — `ProgressionNarrationHandler` |
| R3 | A message is emitted when a skill or attribute is gained (improves) | WP3 — same handler, `TrackImprovedEvent` branch |
| R4 | The player can switch that messaging on and off | WP3 — `PlayerConfigurationComponent` + `config` command |
| R5 | XP is awarded based on **use**, with a **chance** to be awarded (non-linear) | WP1 (chance mechanism) + WP2 (the two sources) |
| R6 | A **global** XP scalar tunes progression at the macro level | WP1 — `ProgressionConstants.GlobalXpScalar` |
| R7 | Individual XP-granting things carry their own **granular** scales | WP1 (per-source, per-rule) + WP2 (per-ability, per-mob) |

Confirmed scope calls: XP sources this slice are **ability use** and **damage taken** (plus the
existing kill); ability rank is **shown but grants no power**; the toggle lands as a **general
preferences framework**; delivered as **one plan, three work packages**.

## Architecture brief

### D1 — Track key widens from `ScoreId` to `ProgressionTrack`

`IProgressionSystem` keys a track by `ScoreId` today. R1 needs per-ability tracks, and an ability
id is a `string`. The `edit-progression-system` skill is **internally inconsistent** on this point:
its track-key guardrail says *"Track key = `ScoreId`; no parallel `TrackId` enum"*, while its
add-a-track recipe says *"Abilities: a future ability track reuses `IProgressionSystem` with the
ability id as the key — do **not** build a second improvement engine."* Both cannot hold literally.

The guardrail's intent is *one progression engine, one track vocabulary* — not *one CLR type*. It
is satisfied by **widening** the key rather than forking it:

```csharp
public readonly record struct ProgressionTrack
{
    public ScoreId? Score { get; }
    public string? AbilityId { get; }
    public static ProgressionTrack Of(ScoreId score);          // "Body"
    public static ProgressionTrack Ability(string abilityId);  // "ability:kick"
    public bool IsAbility { get; }
    public string ToKey();
    public static bool TryParse(string key, out ProgressionTrack track);
}
```

One engine, one component, one threshold curve, one `XpSource` vocabulary. The rejected
alternative — a second XP store inside `Core/Modules/Abilities/` — is a literal second improvement
engine, which the skill forbids outright.

**Invalid states are made unrepresentable.** The public constructor is private; `Of`/`Ability` are
the only entry points; `Ability` rejects null/empty/whitespace ids and ids containing `:`.
`default(ProgressionTrack)` (both fields null) is invalid — `ToKey()` throws on it and a Tier-1
test pins the fail-fast (INV-25).

**Serialization is backward compatible by construction.** A score track's key renders as the bare
enum name (`"Body"`, `"HpMax"`); ability tracks take the reserved `ability:` prefix, which no
`ScoreId` name can produce. `ComponentSerializer.Options` sets `PropertyNamingPolicy = CamelCase`
but **not** `DictionaryKeyPolicy`, so dictionary keys are already emitted as bare enum names today
— pinned by `WalletComponentRoundTripTests`. Existing player snapshots load unchanged;
**no migration**.

> **Converter mechanics (no repo precedent — get this right).** `ComponentSerializer.Options` is a
> `private static` field, so a converter cannot be DI-injected. `ProgressionTrackJsonConverter`
> must be attached via a `[JsonConverter]` attribute **on the struct**, and — because the type is
> used as a *dictionary key* — it must override **`WriteAsPropertyName` / `ReadAsPropertyName`**
> in addition to `Write`/`Read`. `AbilitiesComponentJsonConverter` is **not** the precedent here:
> it is a whole-component converter and says nothing about key serialization.

`IProgressionSystem` keeps `ScoreId` overloads that delegate to the `ProgressionTrack` form.
**`AwardOutcome.Track` widens to `ProgressionTrack`** — which is a compile break at
`ProgressionScenarioExecutor`, handled explicitly in WP1 (see [B6 fix](#wp1--track-widening-award-rule-registry-chance--scale-composition)).
`ProgressionEffectContributor` and `ProgressCommand` keep compiling against the `ScoreId` overloads.
(An earlier draft claimed `AscensionSystem` was affected — it never referenced `IProgressionSystem`
at all.)

### D2 — Three sources ⇒ promote to the advancement-rule registry (INV-19)

Instead of `AbilityUseXpHandler` + `DamageTakenXpHandler` joining `ExperienceAwardHandler`, one
`AdvancementHandler` subscribes to the trigger events and consults `IAdvancementRuleRegistry`:

```csharp
public sealed record AdvancementRule(
    XpSource Source,
    IReadOnlyList<ProgressionTrack> StaticTracks,
    bool IncludesSubjectTrack,        // + the ability's own track, when the trigger names one
    AdvancementEligibility Eligibility, // ← the predicate, as data (see below)
    int BaseAwardMin, int BaseAwardMax,
    double BaseChance,
    double ChanceDecayPerImprovement,
    double SourceScale);
```

**Eligibility is data on the rule, not a branch in the handler.** The spec gate flagged that an
earlier draft left `KillerEntityId == 0` / `DamageDealt == 0` discards in the handler — award
rules living in the orchestration tier, contrary to the very contract carried by the handler being
replaced (*"No game rule is held here"*). `AdvancementEligibility` is a small flags/record shape
declaring what the rule requires — `RequiresAttributableActor`, `RequiresPositiveMagnitude`,
`AppliesAntiGrindPowerRatio` — evaluated inside `ProgressionSystem` against the
`UseAwardContext`. The handler's only job is the mechanical mapping *event fields →
`UseAwardContext`*.

Rows are **hardcoded** in `ProgressionConstants.Rules` (Spine F registry shape, Category 3);
promotion to YAML is deferred to a demonstrated recompile-free need (OD-2) — the same posture the
existing constants carry, and the reason is the CI-pinning contract, not inertia.

`ProgressionSystem` gains one entry point all three sources flow through:

```csharp
UseAwardResult AwardUseExperience(uint entityId, XpSource source, UseAwardContext context);
```

`AwardCombatExperience` stays on the interface as a thin wrapper over `AwardUseExperience` with the
`CombatKill` row, preserving the sim executor's seam. **The wrapper resolves the victim's
`MobDataComponent.XpScale` internally**, so a live kill and a simulated kill cannot drift — the sim
calls `AwardCombatExperience(killer, victim)` directly and would otherwise never see a scale the
handler applied.

### D3 — Ability rank is display-only; attribute tracks still grant power

`ProgressionEffectContributor.GetModifiers(entityId, ScoreId)` folds only **score** tracks; ability
tracks are skipped in both `GetModifiers` and `GetActive`. Ability rank contributes **zero**
estimated power. An architecture-guard test pins this.

**But the slice is not power-neutral, and the earlier draft was wrong to imply it was.** Ability-use
awards feed the ability's `XpAttributeTrack` and the damage-taken rule feeds attribute tracks —
both `ScoreId` tracks, both folded as `PowerPerImprovement × improvements` into every
`IStatSystem.Get`. **This slice adds two continuous new sources of attribute power growth.** See
the [Balance-catalog parity](#balance-catalog-parity) section for the honest classification and the
named follow-up; it is not filed as "Adequate".

Making rank *itself* scale potency or cost is a deliberate later balance slice that must fold into
[`power-model.md`](../design/power-model.md) and re-pin goldens — explicitly **out of scope here**,
and recorded in the completed record so the next slice starts from a stated position.

### D4 — Chance and scale composition (R5–R7), and the RNG draw contract

For each candidate track of a fired rule:

```
eligible?  = rule.Eligibility passes (attributable actor, positive magnitude,
             and — for kills — anti-grind scale > 0)        ← ineligible ⇒ ZERO draws
chance     = clamp01(rule.BaseChance / (1 + improvements(track) × rule.ChanceDecayPerImprovement))
roll       = (chance >= 1.0) ? auto-pass with NO draw       ← see draw contract below
                             : _random.NextDouble() < chance
amount     = round( _random.Next(min, max+1)
                    × GlobalXpScalar        // R6, macro
                    × rule.SourceScale      // R7, per-source
                    × contentScale          // R7, per-ability / per-mob
                    × antiGrindScale )      // kills only, existing ratio math
```

**The RNG draw contract (INV-26) — this is the load-bearing correction from the spec gate.** An
earlier draft claimed kill awards would be numerically identical while adding an unconditional
`NextDouble()` per candidate. That was false: `ProgressionSystem` today consumes **zero**
`NextDouble()` calls and exactly one `Next(min,max+1)` per track, and **no draw at all** when the
anti-grind scale is 0 (a contract documented in flow-31). `SandboxWorld` shares one seeded
`IRandom`, so one extra draw shifts the whole stream and moves every pinned golden. Two rules
therefore bind:

1. **A rule with `BaseChance >= 1.0` and zero decay short-circuits the chance roll with no
   `IRandom` call.** The `CombatKill` row is exactly that, so kills draw what they draw today.
2. **Anti-grind `scale == 0` is an *eligibility* failure, not a zero multiplier** — an ineligible
   candidate consumes zero draws, preserving the trivial-victim contract.

This is verified **directly**, by a Tier-1 test asserting the exact draw *sequence* through a
counting fake `IRandom` — not indirectly via "goldens unmoved", which asserts invisible state.
Goldens staying put is then a consequence, and still a WP1 exit criterion.

Two properties follow: use-based gain is **sub-linear** in action count (chance decays with rank)
without curving the power step — the curve stays in the threshold, the system's central rule.
Note this means **two independent rate-slowing curves** now compose on a track fed by three sources
at different chances (the growing threshold *and* the decaying chance); a Tier-1 test pins the
composition so the interaction is deliberate rather than emergent.

`GlobalXpScalar` defaults to `1.0`, every shipped rule's `SourceScale` to `1.0`, and the
`CombatKill` row carries today's `CombatAwardMin/Max`, `AntiGrindFloorRatio`, `AntiGrindCap` and
`CombatTracks` values verbatim.

### D5 — Preferences implement the already-planned `PlayerConfigurationComponent`

Player-configurable output is a new player-facing surface, so it lands its framework here rather
than as two bespoke bools (INV-19). **The target is already documented**, and INV-15 says write
against the documented target rather than inventing a parallel one:

- [`reference/components-planned.md:24`](../reference/components-planned.md) — `PlayerConfigurationComponent`
  ("prompt template, preferences", player-only, persistent)
- [`architecture/02-ecs.md:160`](../architecture/02-ecs.md) — placed in the player archetype
- [`roadmap/backlog.md:154`](../roadmap/backlog.md) — names it plus a `config`/`set` player command

An earlier draft introduced a fresh `PreferencesComponent` + `toggle` with no reconciliation. That
is corrected: this slice **implements the planned component and the planned verb**.

- `PlayerConfigurationComponent` (`[Persistent]`, `Dictionary<PreferenceId, bool>` for now; the
  prompt-template field folds in when the prompt slice needs it) in
  `Core/Modules/Preferences/Components/`.
- `PreferenceId` enum, seeded with `ProgressionXpMessages` and `ProgressionImprovementMessages`.
- `IPreferenceSystem` — `IsEnabled`, `Set`, `GetAll`; returns values, publishes nothing (INV-5).
- `config` command (alias `toggle`): bare `config` lists every preference with its state;
  `config <name>` flips it; `config <name> on|off` sets it explicitly.

**Module home:** a new `Core/Modules/Preferences/` with a `PreferencesModule.cs`, matching the
25-module `<Feature>Module.cs` convention. Not `Core/Modules/Session/` — that directory has only
`Events/` + `Handlers/`, has no module entry point (its handler is wired directly in
`CompositionRoot.cs:128`), and preferences are per-**character** persistent state, not
per-connection.

## Preconditions

- The invoking entity is a live player entity with `PersistentEntity` (INV-23).
- The progression substrate (`prog-1`), ability substrate, and combat round loop are in place.
- For ability-use awards: the ability is known and successfully activated (a failed/blocked
  invocation publishes no `AbilityActivatedEvent`, so it cannot award).

## Postconditions (the coverage contract)

1. Using a known ability publishes `AbilityActivatedEvent`; the advancement handler rolls the
   `AbilityUse` rule and, on success, awards XP to **the ability's own track** and to the ability's
   configured attribute track.
2. Taking damage in a melee round or from an ability strike rolls the `DamageTaken` rule for the
   **defender** and, on success, awards XP to that rule's attribute tracks.
3. A kill awards exactly what it awards today — same tracks, same amounts, and **the same
   `IRandom` draw sequence at a fixed seed** (asserted directly, per D4).
4. Every positive award publishes one `ExperienceAwardedEvent`; every threshold crossing publishes
   one `TrackImprovedEvent` (unchanged contract, widened track key).
5. With `ProgressionXpMessages` enabled (default), each award writes one line to the earning player
   only; with it disabled, none. Same for `ProgressionImprovementMessages` and improvement lines.
6. `skills` / `spells` / `abilities` show rank, cumulative XP, and XP-to-next for every known
   ability that has earned XP; abilities with no XP show rank 0.
7. `progress` shows score tracks and ability tracks in separate blocks.
8. `config` lists and flips every registered preference; the setting survives a persistence
   round-trip.
9. Ability tracks contribute **0** to `IStatSystem.Get` for every score.
10. Setting `GlobalXpScalar` to `2.0` exactly doubles every awarded amount (rounding aside), and a
    per-ability/per-mob scale multiplies only awards that ability/mob produced.

## Main flow — ability use awarding XP

1. Player invokes a known ability; `AbilityInvocationPipeline` resolves costs/target and publishes
   `AbilityActivatedEvent(actor, abilityId, target)`.
2. `AdvancementHandler` (priority `HandlerPriority.Domain`) receives it and maps the event fields
   into a `UseAwardContext` (subject ability id, content scale from the definition), then calls
   `IProgressionSystem.AwardUseExperience(actor, XpSource.AbilityUse, context)`. **No eligibility
   rule is evaluated here** (D2).
3. `ProgressionSystem` looks up the `AbilityUse` rule, checks `rule.Eligibility`, builds the
   candidate track list (the ability's own track + its `XpAttributeTrack`, falling back to the
   rule's static tracks), and for each eligible candidate: computes rank-decayed chance, rolls
   (or short-circuits per the draw contract), and on success draws + scales the base amount and
   calls `AwardExperience`, which accrues XP and resolves threshold crossings. Returns a
   `UseAwardResult`; **publishes nothing** (INV-5).
4. `AdvancementHandler` publishes one `ExperienceAwardedEvent` per positive row and one
   `TrackImprovedEvent` per threshold crossed (INV-8).
5. `ProgressionNarrationHandler` (priority `HandlerPriority.Notification`) receives each, checks
   the relevant `PreferenceId` via `IPreferenceSystem`, and — when enabled — writes the line to the
   earning player via `IBroadcastSystem.SendToEntityAsync` (new; see cross-cutting table).
6. The player types `skills`; `SkillsCommand` reads rank/XP/to-next per ability from
   `IProgressionSystem` and renders them alongside the existing cooldown/cost block.

Damage-taken follows the same shape from `CombatRoundEvent` / `AbilityStrikeResolvedEvent`, with
the **defender** as the earner and `Result.DamageDealt > 0` as the rule's magnitude precondition.

## Events fired

| Event | Publisher | Change |
|---|---|---|
| `AbilityActivatedEvent` | `AbilityInvocationPipeline`, `UseAbilityCommand` | none — new subscriber only |
| `CombatRoundEvent` | `CombatTickHandler` | none — new subscriber only |
| `AbilityStrikeResolvedEvent` | invocation commands | none — new subscriber only |
| `MobDiedEvent` | combat/death | none — existing subscriber rewired internally |
| `ExperienceAwardedEvent` | `AdvancementHandler` | **widened**: `ScoreId Track` → `ProgressionTrack Track` |
| `TrackImprovedEvent` | `AdvancementHandler` | **widened**: same |
| `PreferenceChangedEvent` | `ConfigCommand` | **new** — thin past-tense fact |

No new trigger event is needed: `CombatRoundResult.DamageDealt` rides both combat events, and
`AbilityActivatedEvent` is published at `AbilityInvocationPipeline.cs:123` and
`UseAbilityCommand.cs:137` (verified).

## Systems / handlers involved

| Piece | Layer | Status |
|---|---|---|
| `IProgressionSystem` / `ProgressionSystem` | Domain | extended (`AwardUseExperience`, `ProgressionTrack` keys, eligibility, chance/scale) |
| `IAdvancementRuleRegistry` / `AdvancementRuleRegistry` | Domain | **new** — rule rows keyed by `XpSource` |
| `AdvancementHandler` | Handler | **new** — one handler over all trigger events; replaces `ExperienceAwardHandler` |
| `ProgressionNarrationHandler` | Handler | **new** — award/improvement lines, preference-gated |
| `IPreferenceSystem` / `PreferenceSystem` | Domain | **new** |
| `ConfigCommand` | Command (Initiator) | **new** |
| `IBroadcastSystem.SendToEntityAsync` | Core | **new method** (see cross-cutting) |
| `ProgressionEffectContributor` | Domain (read seam) | touched — skips ability tracks (D3) |
| `SkillsCommand` / `SpellsCommand` / `AbilitiesCommand` / `ProgressCommand` | Commands | touched — rank/XP display |

`ExperienceAwardHandler` is **deleted**, its `MobDiedEvent` subscription absorbed by
`AdvancementHandler`; its tests migrate rather than duplicate.

## Implementation plan — work packages

### WP1 — Track widening, award-rule registry, chance + scale composition

**Scope.** The engine changes, with **zero behavioural delta** at default constants.

**Files.**
- `Core/Modules/Progression/ProgressionTrack.cs` + `ProgressionTrackJsonConverter.cs` (new —
  `[JsonConverter]` on the struct, overriding `WriteAsPropertyName`/`ReadAsPropertyName`)
- `Core/Modules/Progression/Components/ProgressionComponent.cs` — dictionaries rekeyed
- `Core/Modules/Progression/AdvancementRule.cs`, `AdvancementEligibility.cs`,
  `Systems/IAdvancementRuleRegistry.cs`, `Systems/AdvancementRuleRegistry.cs` (new)
- `Core/Modules/Progression/ProgressionConstants.cs` — add `GlobalXpScalar`; move the combat
  numbers into the `CombatKill` rule row
- `Core/Modules/Progression/Systems/IProgressionSystem.cs`, `ProgressionSystem.cs` —
  `AwardUseExperience`, `UseAwardContext`, `UseAwardResult`, **`AwardOutcome.Track` widened**,
  `ScoreId` overloads retained
- `Core/Modules/Progression/Events/*.cs` — widened track field
- `Core/Modules/Progression/Handlers/AdvancementHandler.cs` (new, `MobDiedEvent` only in this WP);
  `ExperienceAwardHandler.cs` **deleted**
- `Core/Modules/Progression/ProgressionModule.cs` — registrations
- `Core/Modules/Progression/ProgressionEffectContributor.cs` — ability tracks skipped
- **`Core/Modules/Simulation/Systems/ProgressionScenarioExecutor.cs`** — compile break from the
  widened `AwardOutcome.Track` (`.First(row => row.Track == settings.TargetTrack)` and its
  `IReadOnlyDictionary<ScoreId,int>` result shape); adapt at the `ScoreId` boundary so the
  scenario contract is unchanged
- **`Server/Program.cs:185`** — `ExperienceAwardHandler` resolution/subscription replaced by
  `AdvancementHandler`

**Out of scope.** No new XP source wired; no display change; no narration; no preferences.

**Exit criterion.** `dotnet test` green; the **draw-sequence test** passes (the direct assertion);
`SimulationInvariantTests` goldens unmoved (the consequence); and a persistence test proves a
pre-existing score-only `ProgressionComponent` payload loads and re-serializes byte-identically.

### WP2 — The two use-based sources + granular content scales

**Scope.** Wire `AbilityUse` and `DamageTaken` rows and the per-content scales they read.

**Files.**
- `Core/Modules/Progression/XpSource.cs` — add `AbilityUse`, `DamageTaken`
- `Core/Modules/Progression/ProgressionConstants.cs` — the two new rule rows
- `Core/Modules/Progression/Handlers/AdvancementHandler.cs` — subscribe `AbilityActivatedEvent`,
  `CombatRoundEvent`, `AbilityStrikeResolvedEvent`
- `Core/Modules/Abilities/AbilityDefinition.cs` — `double XpScale = 1.0`,
  `ScoreId? XpAttributeTrack = null` (optional; defaults preserve every existing row)
- `Core/Modules/Abilities/AbilityRegistry.cs` — values on the 5 hardcoded rows
- `Core/ECS/Components/MobDataComponent.cs` — `XpScale`, applied from the template on spawn
- `Core/Modules/Mobs/Templates/MobTemplate.cs` — `double XpScale = 1.0`
- `Core/Modules/Mobs/Commands/SetMobCommand.cs` — `setmob xpscale <value>`
- `Hedron.Web/Components/Pages/MobEditor.razor` — the new field
- `Server/Program.cs` — the two added subscriptions

**Out of scope.** Display and narration (WP3). **No ability YAML file, no ability deserializer, and
no Blazor ability editor** — see the content-tooling section for why.

**Exit criterion.** With a seeded `IRandom`, repeated ability use accrues XP on that ability's track
at the configured chance; taking damage accrues the defender's tracks and not the attacker's;
`setmob xpscale 0` makes that mob's kills award nothing.

### WP3 — Visibility, narration, and the preferences framework

**Scope.** Everything the player sees and controls.

**Files.**
- `Core/Modules/Preferences/` (new module): `Components/PlayerConfigurationComponent.cs`,
  `PreferenceId.cs`, `PreferenceRegistry.cs` (defaults), `Systems/IPreferenceSystem.cs`,
  `PreferenceSystem.cs`, `Commands/ConfigCommand.cs`, `Events/PreferenceChangedEvent.cs`,
  `PreferencesModule.cs`
- `Core/Systems/IBroadcastSystem.cs` + implementation — **add `SendToEntityAsync`**
- `Core/Modules/Progression/Handlers/ProgressionNarrationHandler.cs` (new)
- `Core/Modules/Abilities/AbilityDisplayMessage.cs` + `Commands/AbilitiesCommand.cs` — rank block
  on all three verbs
- `Core/Output/ProgressDisplayMessage.cs` + `Progression/Commands/ProgressCommand.cs` —
  ability-track block
- `Server/CompositionRoot.cs` — register `PreferencesModule`; `Server/Program.cs` — narration
  handler subscription
- `docs/` updates (below)

**Out of scope.** Prompt-bar XP display; migrating the 14 existing `SendToRoomAsync`-with-predicate
call sites (backlog); non-boolean preferences; the prompt-template field on
`PlayerConfigurationComponent`.

**Exit criterion.** Postconditions 5–8 verified: messages appear, `config` silences them, `skills`
shows rank/XP, and the setting survives a restart.

The primary agent runs `architecture-reviewer` (code mode) across the combined WP1–WP3 diff before
merge.

## Content tooling impact (INV-18)

| State | Author | Inspect |
|---|---|---|
| Per-ability `XpScale` / `XpAttributeTrack` | compiled rows in `AbilityRegistry` (Category 3) | `defs ability <id>` |
| Per-mob `XpScale` | `setmob xpscale <value>` + Blazor `MobEditor` field + mob YAML | `defs mob <id>` / editor readout |
| Award-rule rows | `ProgressionConstants.Rules` (Category 3, compiled) | new admin `progress rules` sub-view: source, tracks, chance, decay, scales |
| `GlobalXpScalar` | `ProgressionConstants` (Category 3) | `progress rules` header |
| Ability track XP | earned in play | `skills` / `spells` / `abilities` / `progress` |
| Preferences | `config` | bare `config` lists every preference and its state |

**Why abilities are authored in code, not YAML.** An earlier draft listed "ability YAML
deserializer" and "the Blazor ability editor" as one-line file bullets in WP2. Neither exists:
`AbilityRegistry` is a hardcoded `DefinitionRegistry` of 5 rows, there is no ability YAML file or
deserializer anywhere in the repo, and `Hedron.Web/Components/Pages/` has Area/AreaGrid/Room/Item/
Mob editors only. **An ability content pipeline is a slice of its own**, not a bullet inside this
one. The two new fields therefore land as compiled rows — the same Category-3 posture this plan
already argues for the rule table — with the existing `defs ability <id>` as the inspect surface.
Building the ability authoring pipeline is a prerequisite for *content-volume* ability work, and is
filed to [`backlog.md`](../roadmap/backlog.md) accordingly.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Note |
|---|---|---|
| XP-trigger wiring (3rd source) | **Gap exposed → closed here** | Bespoke-handler-per-source hits its third repetition; promoted to `IAdvancementRuleRegistry` + one handler |
| Player-configurable output | **Gap exposed → closed here** | Implements the already-planned `PlayerConfigurationComponent` + `config` verb (D5), not a parallel invention |
| Direct-to-entity output | **Gap exposed → closed here** | The `SendToRoomAsync(room, msg, id => id == recipient)` workaround is live in **14 files** (`CurrencyAwardNarrationHandler`, `CombatHandler` ×5, `AbilityStrikeHandler`, `PlayerSessionHandler`, `AscensionNarrationHandler`, `AbilityInvocationHandler`, `DeathNarrationHandler` ×2, `ItemInteractionHandler` ×2, `EquipmentInteractionHandler` ×2, `ShopInteractionHandler` ×2) — far past INV-19's third repetition, with **no** backlog entry. An earlier draft miscounted this as "the second use" and deferred; that rationale was false. WP3 adds `IBroadcastSystem.SendToEntityAsync` and uses it for the new narration; **migrating the 14 existing call sites is filed as a real backlog entry stating the true count** |
| Persistence (component shape change) | **Adequate** | `ProgressionTrack` keys are a superset of today's enum-name keys; back-compat pinned by a byte-identical round-trip test. No migration framework needed |
| Power model / balance oracle | **Gap exposed** | See below — the slice adds two unsimulated attribute-power sources |
| Determinism seam (`IRandom`) | **Adequate** | All chance flows through the injected seam; the draw *sequence* is directly asserted, and ineligible candidates stay draw-free (INV-26) |
| Concurrency posture (INV-31) | **Adequate** | No new background initiator or shared mutable singleton; registry immutable after construction; handlers on the existing bus path |

## Balance-catalog parity

Per [`balance.md`](../design/balance.md) rule 5, the knob catalog gains rows in the same PR:
`GlobalXpScalar`, the `Rules` table (per-source base range, chance, chance decay, source scale),
per-ability `XpScale`, per-mob `XpScale`.

**Rule 3 (re-validate and re-pin) is only partly satisfied, and that is stated rather than
glossed.** Kill awards are numerically unchanged, so no existing golden moves. But ability-use and
damage-taken awards feed **attribute** tracks, which do grant power through
`ProgressionEffectContributor` — two new continuous sources of attribute growth. The existing
`progressionRate` scenario **cannot see them**: `ProgressionScenarioExecutor` exercises
`AwardCombatExperience` exclusively. So "no golden re-pin needed" is true only because the sim is
blind to the new sources, not because they are power-neutral.

Disposition: **acknowledged, bounded, and assigned.** Defaults are set conservatively (low
`BaseChance`, meaningful `ChanceDecayPerImprovement`) so the unvalidated rate is a slow drift
rather than a step change; the gap is filed to [`backlog.md`](../roadmap/backlog.md) as *"extend
`progressionRate` to model use-based accrual (ability use, damage taken)"* with a named follow-up
slice, and `balance-tuning`'s knob table is updated in the same PR. Extending the sim scenario is
**not** folded into this slice — it needs its own scenario schema work, and hiding it here would
repeat the sizing mistake the spec gate caught in WP2.

## Flows introduced or modified (INV-17)

- [`flow-31-progression-award.md`](../architecture/flows/flow-31-progression-award.md) —
  **rewritten** from the kill-only path to the generalized rule-table path, including the revised
  RNG draw contract in step 2.
- [`flow-24-ability-activation.md`](../architecture/flows/flow-24-ability-activation.md) —
  **extended** with the advancement branch off `AbilityActivatedEvent`.
- [`flow-20-mob-death-respawn.md`](../architecture/flows/flow-20-mob-death-respawn.md) —
  **touched**: the XP step now names `AdvancementHandler`.
- No new flow for `config` — an ordinary command on
  [`flow-03-player-command-lifecycle.md`](../architecture/flows/flow-03-player-command-lifecycle.md).

## Test plan / Verification (INV-25)

**Tier 1 — system decisions** (`Hedron.Tests/Progression/`)
- `ProgressionTrack` round-trips: `"Body"` ⇄ score, `"ability:kick"` ⇄ ability, unknown key
  rejected, `ability:` prefix cannot collide with a `ScoreId` name.
- **Fail-fast:** `default(ProgressionTrack).ToKey()` throws; `Ability(null/""/"a:b")` rejected.
- **Draw sequence (the B1 fix):** a counting fake `IRandom` asserts a kill consumes the *exact*
  today's sequence — zero `NextDouble()`, one `Next(8,13)` per track, and **zero draws** for a
  trivial victim.
- Scale composition: global × source × content × anti-grind; `GlobalXpScalar = 2.0` doubles;
  `contentScale = 0` awards nothing.
- Chance: fake `IRandom` just below/at/above threshold; decay with improvement count; clamp to
  [0,1]; `BaseChance >= 1.0` short-circuits without a draw.
- **Curve composition:** growing threshold × decaying chance on one track, pinned deliberately.
- Threshold crossing on ability tracks matches score tracks (one curve).
- `AdvancementRuleRegistry` returns the right row per `XpSource`; unmapped source is a no-op.
- **Eligibility as data:** each `AdvancementEligibility` flag rejects at the system tier.

**Tier 2 — handler orchestration**
- `AdvancementHandler`: one `ExperienceAwardedEvent` per positive row, N `TrackImprovedEvent`s per
  N crossings, for each of the three trigger events; a non-player actor awards nothing; the handler
  performs **only** field mapping (no eligibility branch).
- `ProgressionNarrationHandler`: writes on award/improvement when the preference is enabled,
  nothing when disabled, never to a non-earner.
- Migrated from `ExperienceAwardHandlerTests`, not duplicated.

**Tier 3 — flows** (`ProgressionAwardFlowTests.cs`, extended)
- Use ability → XP on the ability track → threshold → `skills` shows the new rank.
- Take damage → defender's tracks accrue; attacker's do not.
- `config progressionxp off` → subsequent award produces no output but XP still accrues.
- Existing kill-award flow test unchanged and still green.

**Persistence**
- `ProgressionComponent` round-trip with mixed score + ability keys.
- **Back-compat:** a pre-slice score-only payload deserializes and re-serializes byte-identically.
- `PlayerConfigurationComponent` round-trip; absent key falls back to the registry default.

**Architecture guard** (`Hedron.Tests/Architecture/`)
- No ability track ever yields a non-zero `IEffectContributor` modifier (pins D3).
- `ProgressionSystem` / `PreferenceSystem` hold no `IEventBus` dependency (INV-5).
- `ProgressionSystem` does not depend on `IStatSystem`/`IEffectSystem` (standing DI-cycle guard).

**Simulation**
- `SimulationInvariantTests` run unchanged; movement in a pinned golden fails WP1.

**Not tested, and why:** narration string wording (presentation — asserted only as "a line
was/wasn't written"); the `MobEditor` `XpScale` binding (the existing editor-binding tier already
covers sibling scalar fields, extended only if that tier covers them individually); long-horizon
progression *balance*, which is the `progressionRate` sim's job and is explicitly outside this
slice's sim coverage (see Balance-catalog parity).

## Docs updated on ship (INV-27–INV-30)

- [`features/progression/progression.md`](../features/progression/progression.md) +
  [`progression-system.md`](../features/progression/progression-system.md) — use-based sources, the
  rule table, chance/decay, scales, display-only ability rank, the RNG draw contract
- New `features/preferences/preference-system.md`
- [`design/balance.md`](../design/balance.md) — knob rows + the sim-blind-spot known gap
- [`reference/systems.md`](../reference/systems.md), [`handlers.md`](../reference/handlers.md),
  [`components.md`](../reference/components.md), **[`commands.md`](../reference/commands.md)**
  (`config` is new; `skills`/`spells`/`abilities`/`progress` change output — INV-16),
  **[`archetypes.md`](../reference/archetypes.md)** (player archetype gains the component)
- **[`reference/components-planned.md`](../reference/components-planned.md)** — retire the
  `PlayerConfigurationComponent` row (now implemented); **[`architecture/02-ecs.md`](../architecture/02-ecs.md):160**
  updated; **[`roadmap/backlog.md`](../roadmap/backlog.md):154** player-config bullet closed
- Backlog **additions**: `SendToEntityAsync` migration of the 14 existing call sites; ability
  authoring pipeline (YAML + editor); extend `progressionRate` to use-based accrual
- The three flows above

**Agent/skill updates (INV-20):**
- `.claude/skills/edit-progression-system/SKILL.md` — **three** places: the "add an XP source"
  recipe → "add a rule row"; the track-key guardrail (line 54) reconciled with the ability-track
  recipe (line 44) per D1; the tuning recipe's constants list (`CombatAwardMin/Max` etc. now live
  in the `CombatKill` row).
- `.claude/skills/balance-tuning/SKILL.md` — knob-home table gains `GlobalXpScalar` + the rule
  table; its "progression-affecting → run a `progressionRate` scenario" rule is the exact rule the
  sim blind spot suspends, so the deferral is recorded there too.

This plan is then deleted, with decisions D1–D5 carried into
`roadmap/completed/progression-use-based-xp.md`.

## Spec-review corrections

Fixed in place after the 2026-08-16 spec gate:

| # | Finding | Resolution |
|---|---|---|
| B1 | "Identical RNG draw order" was false — an unconditional `NextDouble()` per candidate would shift the shared seeded stream and move every golden | D4 draw contract: `BaseChance >= 1.0` short-circuits without a draw; anti-grind 0 is an *eligibility* failure (zero draws); asserted directly by a draw-sequence test, not via goldens |
| B2 | WP2 committed to an ability YAML deserializer and Blazor ability editor — **neither exists**; that is a content pipeline, not a bullet | Dropped. `XpScale`/`XpAttributeTrack` are compiled `AbilityRegistry` rows inspected via `defs ability`; the pipeline is filed to backlog |
| B3 | Direct-to-entity debt counted as "the second use" — it is live in **14 files**, far past INV-19, with no backlog entry | Count corrected; `SendToEntityAsync` lands in WP3; migrating the 14 call sites filed as a real backlog entry |
| B4 | `PlayerConfigurationComponent` is already the documented target in three places; the plan invented `PreferencesComponent` (INV-15) | D5 implements the planned component and the planned `config` verb; the three docs are reconciled on ship |
| B5 | "Power model — Adequate" was wrong: ability-use and damage-taken awards feed *attribute* tracks, which grant power, and the sim cannot see them | Reclassified **Gap exposed**; blind spot stated; conservative defaults; extension filed with a named follow-up slice |
| B6 | `ProgressionScenarioExecutor`, `Server/Program.cs:185`, and `MobDataComponent` were in no work package; the `AscensionSystem` "compiles untouched" claim was fabricated | All three added to WP1/WP2; `AwardOutcome.Track` widening stated explicitly; the `AscensionSystem` claim removed |

Non-blocking review items also folded in: the converter-precedent correction
(`WriteAsPropertyName`/`ReadAsPropertyName`, not `AbilitiesComponentJsonConverter`), eligibility as
rule data rather than a handler branch, the mob-scale read site pinned inside the
`AwardCombatExperience` wrapper, the `Core/Modules/Preferences/` module home, `ProgressionTrack`
fail-fast validation, the two-composing-curves note, and the `commands.md` / `archetypes.md` /
`components-planned.md` doc drift.
