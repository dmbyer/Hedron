# Use-based progression — skill tracks, award chance, tunable scales (completed)

> Implemented on branch `claude/progression-use-based-xp-7b09fb`, 2026-08-15. Living docs: [`features/progression/progression.md`](../../features/progression/progression.md) · [`features/progression/progression-system.md`](../../features/progression/progression-system.md) · [`features/preferences/preference-system.md`](../../features/preferences/preference-system.md) · [flow-31](../../architecture/flows/flow-31-progression-award.md).

## Outcome

The progression substrate awarded XP on **one** trigger (a combat kill) to two attribute tracks, with no narration and no per-content tuning. This slice made progression use-driven, visible, chance-gated, and tunable:

- **Skills accrue their own XP.** `ProgressionTrack` widened the track key from `ScoreId` to *either* a score *or* an ability id, over the same engine, component, and threshold curve. `skills`/`spells`/`abilities` show each ability's rank and XP-to-next; `progress` gained a separate ability block. **Ability rank is display-only — it grants no power.**
- **Two new XP sources: ability use and damage taken.** That made three, which is exactly the INV-19 framework-at-the-third-repetition threshold — so instead of two more bespoke handlers, the trigger wiring was promoted to **one `AdvancementHandler` over an `IAdvancementRuleRegistry`**, and the existing kill award was re-expressed as the table's first row.
- **Awards are chance-gated,** with the chance decaying as a track's rank rises — progression is sub-linear in action count rather than a grind counter.
- **Every award and improvement narrates** to the earner, each class independently silenceable via a new `config` verb backed by the already-planned `PlayerConfigurationComponent`.
- **Two tiers of tuning:** one `GlobalXpScalar` for macro pacing, plus per-source, per-ability, and per-mob scales.

`ExperienceAwardHandler` was deleted; its tests migrated rather than duplicated.

## Behavior digest

- **One entry point.** `AwardUseExperience(entityId, source, context)` looks up the source's `AdvancementRule`, evaluates its `AdvancementEligibility`, builds the candidate track list, rolls a rank-decayed chance per candidate, and on a pass draws and scales the base amount.
- **Chance:** `clamp01(BaseChance / (1 + improvements × ChanceDecayPerImprovement))`.
- **Amount:** `round(Next(min, max+1) × GlobalXpScalar × SourceScale × contentScale × antiGrindScale)`, away from zero.
- **Candidates:** the ability's own track (when the rule takes one and the trigger named an ability) plus the subject's `XpAttributeTrack`, falling back to the rule's `StaticTracks`.
- **Eligibility is rule data, evaluated in the system:** `RequiresAttributableActor`, `RequiresPlayerEarner`, `RequiresPositiveMagnitude`, `AppliesAntiGrindPowerRatio`. The handler holds no discard branch (INV-8).
- **Kills are unchanged** — same tracks, same amounts, and the same `IRandom` draw sequence at a fixed seed. Every pinned simulation golden stayed put.
- **Narration:** one line per award and per improvement to the earner only, each gated on its own `PreferenceId`, delivered by the new `IBroadcastSystem.SendToEntityAsync`.
- **Persistence:** no migration. Score-track keys serialize to the same bare enum names as before.

## Shipped pieces

| Surface | Location |
|---|---|
| `ProgressionTrack` + `ProgressionTrackJsonConverter` | `Core/Modules/Progression/ProgressionTrack.cs` · `ProgressionTrackJsonConverter.cs` |
| `AdvancementRule` / `AdvancementEligibility` | `Core/Modules/Progression/AdvancementRule.cs` · `AdvancementEligibility.cs` |
| `IAdvancementRuleRegistry` / `AdvancementRuleRegistry` | `Core/Modules/Progression/Systems/` |
| `ProgressionConstants.Rules` + `GlobalXpScalar` | `Core/Modules/Progression/ProgressionConstants.cs` |
| `AwardUseExperience` / `UseAwardContext` / `UseAwardResult`; widened `AwardOutcome.Track` and both events | `Core/Modules/Progression/Systems/` · `Events/` |
| `AdvancementHandler` (replaces `ExperienceAwardHandler`, **deleted**) | `Core/Modules/Progression/Handlers/AdvancementHandler.cs` |
| `ProgressionNarrationHandler` | `Core/Modules/Progression/Handlers/ProgressionNarrationHandler.cs` |
| `Core/Modules/Preferences/` (new module) — `PlayerConfigurationComponent`, `PreferenceId`, `PreferenceRegistry`, `IPreferenceSystem`/`PreferenceSystem`, `ConfigCommand`, `PreferenceChangedEvent`, `PreferenceListMessage`, `PreferencesModule` | `Core/Modules/Preferences/` |
| `IBroadcastSystem.SendToEntityAsync` | `Core/Systems/IBroadcastSystem.cs` · `BroadcastSystem.cs` |
| Rank block on `skills`/`spells`/`abilities` (`AbilityLineBuilder`) | `Core/Modules/Abilities/AbilityDisplayMessage.cs` · `Commands/AbilitiesCommand.cs` |
| Ability block on `progress` | `Core/Output/ProgressDisplayMessage.cs` · `TelnetOutputFormatter.cs` · `Progression/Commands/ProgressCommand.cs` |
| `AbilityDefinition.XpScale` / `.XpAttributeTrack` + registry values | `Core/Modules/Abilities/AbilityDefinition.cs` · `AbilityRegistry.cs` |
| `MobDataComponent.XpScale` + template/YAML/builder/`setmob xpscale`/`MobEditor` | `Core/ECS/Components/MobDataComponent.cs` · `Core/Modules/Mobs/` · `Hedron.Web/Components/Pages/MobEditor.razor` |
| Sim compile-boundary adapt (`AwardOutcome.Track` widening) | `Core/Modules/Simulation/Systems/ProgressionScenarioExecutor.cs` · `SandboxWorldFactory.cs` |
| Composition | `Server/CompositionRoot.cs` (`AddPreferencesModule`) · `Server/Program.cs` (four advancement subscriptions + two narration subscriptions) |

## Tests shipped

- **Tier 1** — `ProgressionTrackTests` (key round-trip, fail-fast validation, prefix-cannot-collide, dictionary-key JSON); `ProgressionSystemTests` extended with the **draw-sequence** assertions, eligibility, the chance gate at/above/below threshold, rank decay, two-composing-curves, and scale composition; `PreferenceSystemTests`.
- **Tier 2** — `AdvancementHandlerTests` (migrated from `ExperienceAwardHandlerTests`, extended to all four triggers, non-player actor, zero-damage round); `ProgressionNarrationHandlerTests` (preference gating both ways, earner-only).
- **Tier 3** — `ProgressionAwardFlowTests` extended: ability use → ability-track rank → `skills` shows it; damage taken accrues the defender and not the attacker; `config progressionxp off` silences narration while XP still accrues; bare `config` lists every preference.
- **Persistence** — `ProgressionPersistenceTests`: the **byte-identical pre-slice payload** round-trip (the no-migration proof), mixed score+ability keys, `PlayerConfigurationComponent` round-trip, registry-default fallback.
- **Architecture guard** — `ProgressionGuardTests`: ability tracks yield zero modifiers and never move an effective score (with a score-track control), `ProgressionSystem` holds no `IStatSystem`/`IEffectSystem`, the `CombatKill` row stays aligned with `CombatTracks` and remains a no-draw certainty.
- **Content** — `MobXpScaleRoundTripTests` (YAML round-trip, zero vs default, negative rejected, spawn apply, builder dual-write).
- `dotnet test` green — **1415 tests** (up from 1338 pre-slice). `SimulationInvariantTests` goldens **unmoved**.

## Decisions

- **D1 — The track key widened rather than forked.** The `edit-progression-system` skill was internally inconsistent: its guardrail said "track key = `ScoreId`, no parallel `TrackId` enum", while its add-a-track recipe said a future ability track reuses `IProgressionSystem` with the ability id as the key. Both cannot hold literally. The guardrail's *intent* is one engine and one track vocabulary — not one CLR type — so widening satisfies it, while the rejected alternative (a second XP store inside `Core/Modules/Abilities/`) would be the literal second improvement engine it forbids. The skill's guardrail and recipe were reconciled on ship.
- **D2 — Three sources ⇒ the rule table, and eligibility is rule data.** An earlier draft left `KillerEntityId == 0` / `DamageDealt == 0` discards in the handler — award rules living in the orchestration tier, contrary to the very contract carried by the handler being replaced. `AdvancementEligibility` moved them onto the rule, evaluated inside the system.
- **D3 — Ability rank is display-only, and the slice is *not* power-neutral.** Ability tracks contribute zero to `IStatSystem.Get`, pinned by a guard test. But ability-use awards feed the ability's `XpAttributeTrack` and the damage-taken row feeds attribute tracks — both `ScoreId` tracks, both folded as power. **This slice adds two continuous new sources of attribute power growth.** See the balance note below; it was not filed as "Adequate".
- **D4 — The RNG draw contract is the load-bearing constraint.** An earlier draft claimed kill awards would be numerically identical while adding an unconditional `NextDouble()` per candidate. That was false: `SandboxWorld` shares one seeded `IRandom`, so one extra draw shifts the whole stream and moves every pinned golden. Two rules bind — a chance `>= 1.0` short-circuits with no draw, and an anti-grind failure is an *eligibility* failure consuming zero draws. Verified **directly** by a draw-sequence test over a counting fake `IRandom`; goldens staying put is the consequence, not the proof.
- **D5 — Preferences implement the already-planned component.** `PlayerConfigurationComponent` was already the documented target in three places (`components-planned.md`, `02-ecs.md`, `backlog.md`), paired with a `config`/`set` verb. An earlier draft invented a fresh `PreferencesComponent` + `toggle` with no reconciliation; corrected per INV-15. Module home is a new `Core/Modules/Preferences/`, not `Modules/Session/` — preferences are per-**character** persistent state, not per-connection, and `Session` has no module entry point.
- **Converter mechanics (no repo precedent).** `ComponentSerializer.Options` is `private static`, so the converter is attached by `[JsonConverter]` **on the struct** and must override `WriteAsPropertyName`/`ReadAsPropertyName` because the type is a *dictionary key*. `AbilitiesComponentJsonConverter` is not the precedent here — it is a whole-component converter and says nothing about key serialization.
- **`RequiresPlayerEarner` was added as-built, beyond the plan's three eligibility flags.** Without it the `DamageTaken` row would award XP to every mob in every combat round, attaching `ProgressionComponent` to world content. It gates on `CharacterComponent` and is deliberately **off** on the kill row, whose earner in the balance sandbox is mob-shaped (`SimCombatantFactory` builds combatants from `MobDataComponent`) — turning it on there would have silently disabled the `progressionRate` scenario.
- **`AwardCombatExperience` resolves the mob's `XpScale` internally,** not in the handler, so a live kill and a sandbox kill (which calls the wrapper directly) cannot drift.
- **Abilities are authored in code, not YAML.** An earlier draft listed an "ability YAML deserializer" and "the Blazor ability editor" as one-line file bullets. Neither exists: `AbilityRegistry` is five hardcoded rows, there is no ability YAML anywhere in the repo, and the Blazor editors are Area/AreaGrid/Room/Item/Mob only. An ability content pipeline is a slice of its own; the two new fields landed as compiled rows inspected via `defs ability <id>`, and the pipeline is filed to the backlog.
- **Direct-to-entity output debt was counted, not estimated.** The plan claimed 14 files; the verified figure is **19 single-recipient call sites across 10 files** (plus two handlers that pass *exclusion* predicates and are genuine room broadcasts). `SendToEntityAsync` landed here and is used by the new narration; migrating the existing sites is filed with the real numbers.

## Balance disposition (acknowledged, bounded, assigned)

Kill awards are numerically unchanged, so no existing golden moved. But the two new rows feed **attribute** tracks, which do grant power, and the `progressionRate` scenario **cannot see them** — `ProgressionScenarioExecutor` exercises `AwardCombatExperience` exclusively. So "no golden re-pin needed" is true only because the sim is blind to the new sources, not because they are power-neutral.

Defaults are set conservatively (`AbilityUse` 0.25 base chance / 0.15 decay; `DamageTaken` 0.15 / 0.20) so the unvalidated rate is a slow drift rather than a step change. The gap is recorded in [`design/balance.md`](../../design/balance.md) Known gaps, warned about in the `balance-tuning` skill's validation recipe, and filed to [`backlog.md`](../backlog.md) — where it also fires the **alignment trigger** the sim-4 forward notes already recorded ("when the ≥3-source advancement-rule table lands, repoint the sweep's event model at the same `XpSource`/rule vocabulary"). Extending the scenario needs its own schema work and was deliberately not folded in.

## Deferred (filed to [`backlog.md`](../backlog.md))

- Migrate the 19 single-recipient `SendToRoomAsync` call sites onto `SendToEntityAsync`.
- Ability authoring pipeline (YAML + deserializer + writer + Blazor editor).
- Extend the `progressionRate` sim scenario to use-based accrual.
- Ability rank granting power (potency/cost scaling) — a deliberate balance slice that must fold into `power-model.md` and re-pin goldens.
- Prompt-bar XP display; non-boolean preferences; the prompt-template field on `PlayerConfigurationComponent`.
