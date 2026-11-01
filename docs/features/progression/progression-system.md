# Progression system

> Use-driven per-track experience accrual, chance-gated awards over an advancement-rule table, threshold improvement, and the contribute-on-read power fold. **Authoring checkpoint:** slice prog-6; challenge gating on effective power relative-power-consider. Living document.

## What it is / does

Domain-tier. `ProgressionSystem` owns three decisions: (1) whether an action qualifies for an award and how much it grants, (2) accruing cumulative XP per track, and (3) resolving how many thresholds a track has crossed. `ProgressionEffectContributor` is the read-side adapter that turns "improvement count" into "power" for any `IStatSystem.Get` caller. Neither touches the event bus (INV-5) — `AdvancementHandler` is the Initiator that publishes the result.

## How it works

### A track is a `ProgressionTrack`

One vocabulary over one improvement engine, not a parallel key type and not a second engine. A `ProgressionTrack` is **either** a score track (an attribute or the `HpMax` pool, keyed by `ScoreId`) **or** an ability track (keyed by an ability id). Derived scores (`AttackPower`/`Defense`) are never tracked directly since they rise via their inputs' tracks.

Invalid states are unrepresentable: the constructor is private, `Of`/`Ability` are the only entry points, `Ability` rejects null/empty/whitespace ids and ids containing the reserved `:` separator, and `default(ProgressionTrack)` throws from `ToKey()` rather than rendering an empty key.

**The serialized key is what makes the widening migration-free.** A score track renders as the bare enum name (`"Body"`, `"HpMax"`) — byte-identical to what `ComponentSerializer` already emitted for `Dictionary<ScoreId,int>` keys, because it sets `PropertyNamingPolicy` but not `DictionaryKeyPolicy`. Ability tracks take the reserved `ability:` prefix, which no `ScoreId` name can produce. Pre-slice player snapshots therefore load unchanged, and a persistence test asserts a pre-slice payload re-serializes byte-identically.

`ProgressionTrackJsonConverter` is attached by `[JsonConverter]` **on the struct** — `ComponentSerializer.Options` is a `private static` field, so a converter cannot be injected into it — and overrides `WriteAsPropertyName`/`ReadAsPropertyName` in addition to `Write`/`Read`, because the type is used as a **dictionary key** and `System.Text.Json` routes key serialization through those methods.

### One entry point over an advancement-rule table

Every XP source flows through `AwardUseExperience(entityId, source, context)`. It looks up the source's `AdvancementRule` via `IAdvancementRuleRegistry` and reads everything from that row: candidate tracks, eligibility, base range, chance, decay, and the per-source scale. This is the INV-19 promotion — the bespoke-handler-per-source pattern hit its third repetition, so the trigger wiring became a table rather than a third handler.

**Eligibility is data on the rule, not a branch in the handler.** `AdvancementEligibility` declares what the rule requires — `RequiresAttributableActor`, `RequiresPlayerEarner`, `RequiresPositiveMagnitude`, `AppliesAntiGrindPowerRatio` — and is evaluated here, at the system tier, against the incoming `UseAwardContext`. The handler's job is the mechanical mapping *event fields → context* plus one relative-power read per opponent-bearing trigger, to fill `UseAwardContext.OpponentPowerRatio`; opponent selection maps event facts, never a game-rule branch (INV-8).

`RequiresPlayerEarner` gates on `CharacterComponent`. It is set on the two use-based rows — without it every mob taking damage in every combat round would accrue XP — and deliberately **left off** the kill row, whose earner in the balance sandbox is mob-shaped (`SimCombatantFactory` builds combatants from `MobDataComponent`).

**Candidate tracks** are the ability's own track (when the rule takes one and the trigger named an ability) plus the attribute track — the subject's configured `XpAttributeTrack`, falling back to the rule's `StaticTracks` when the subject declares none. An ability with no `XpAttributeTrack` therefore grants rank only and adds no attribute power.

`AwardCombatExperience(killer, victim, opponentPowerRatio)` stays on the interface as a thin wrapper over the `CombatKill` row, preserving the balance sandbox's seam; the caller supplies the victim ÷ killer ratio. **It resolves the victim's `MobDataComponent.XpScale` internally**, so a live kill and a simulated kill cannot drift — the sandbox calls the wrapper directly and would otherwise never see a scale the handler applied.

### Chance, decay, and the four tuning tiers

Per candidate track:

```
chance = clamp01(BaseChance / (1 + improvements(track) × ChanceDecayPerImprovement))
amount = round( Next(BaseAwardMin, BaseAwardMax+1)
                × GlobalXpScalar     // R6 — the macro knob
                × rule.SourceScale   // R7 — per-source
                × contentScale       // R7 — per-ability / per-mob
                × antiGrindScale )   // flagged rows, only when a ratio is supplied
```

Rank decay makes use-based gain **sub-linear in action count** without curving the power step. The **cumulative** threshold grows, but each step costs the same flat `ThresholdIncrement`, so the per-step XP cost curve is flat; what slows a chance-gated track per action is the **chance decay**, plus **anti-grind** as the earner outgrows its opponents on opponent-bearing triggers. That composition is deliberate rather than emergent, and a Tier-1 test pins it.

### The RNG draw contract (INV-26)

This is the load-bearing constraint of the slice, and it is asserted **directly** by a draw-sequence test over a counting fake `IRandom` — not indirectly via "the goldens did not move", which asserts invisible state.

1. **A chance of `>= 1.0` short-circuits with no `IRandom` call** (and `<= 0.0` likewise auto-fails with no call).
2. **An ineligible candidate consumes zero draws** — an anti-grind scale below the floor is an *eligibility failure*, not a zero multiplier. This holds on all three rows: a below-floor, unreadable (`0.0`) or non-finite ratio on a kill, an ability use or a damage-taken award draws nothing at all.
3. **No opponent means no ratio, and the draw sequence is unchanged.** A `null` `OpponentPowerRatio` (a self, ally or untargeted ability use) draws exactly what the pre-anti-grind use row drew. A peer ratio of `1.0` is a ×1 scale, so it draws and awards exactly what `null` does.

The pinned sequences (`ProgressionSystemTests`, over `CountingRandom`):

| Case | Draws |
|---|---|
| Kill, any eligible ratio | one `Next(8, 13)` per combat track (`Body`, `HpMax`); no `NextDouble()` |
| Kill, trivial / unreadable / non-finite ratio | none |
| Ability use, `OpponentPowerRatio = null` | one `NextDouble()` (chance roll), then on a pass one `Next(3, 7)` |
| Ability use, ratio below the floor or `0.0` | none |
| Damage taken, ratio `1.0` (equal to `null`) | per combat track: one `NextDouble()`, then on a pass one `Next(2, 5)` |
| Damage taken, ratio below the floor | none |

These matter because the balance sandbox shares **one** seeded `IRandom` across every system in a run: a single extra `NextDouble()` would shift the whole stream and move every pinned golden. The `CombatKill` row is exactly case 1 (`BaseChance 1.0`, zero decay), and the trivial-victim path is exactly case 2 — so the kill award draws precisely what it drew before this slice.

### Threshold math is linear-cumulative

The cumulative XP required to have earned the *k*-th improvement is `ThresholdBase + k × ThresholdIncrement` (both in `ProgressionConstants`) — strictly increasing, so the "next" threshold always exceeds the last. `TryImprove` loops while cumulative XP ≥ the next threshold, incrementing the improvement count once per crossing; a single large award can cross several thresholds in one call. The power step itself (`PowerPerImprovement`) is constant — **the curve lives in the threshold, never in the power.** One curve serves score and ability tracks alike.

### Ability rank is display-only

`ProgressionEffectContributor` folds only **score** tracks. `GetModifiers` takes a `ScoreId` by signature, and `GetActive` enumerates `GetTrackedScores`, which excludes ability tracks by construction. Ability rank therefore contributes **zero** estimated power, pinned by an architecture-guard test that ranks an ability far past any threshold and asserts every score is unmoved (with a score-track control proving the contributor is not simply broken).

Making rank itself scale potency or cost is a deliberate later balance slice that must fold into [`power-model.md`](../../design/power-model.md) and re-pin goldens — explicitly out of scope here.

### Anti-grind ratio is supplied by the caller

Anti-grind is **challenge gating**: an award against an opponent the earner has clearly outgrown pays less, and below the floor nothing. The ratio it compares is opponent ÷ earner on **effective** power — gear, progression, active effects and tier on both sides — measured by the shared relative-power read ([relative-power-system.md](relative-power-system.md)), the same read `consider` and `power` use.

`ProgressionSystem` itself **never measures power.** The award methods take the ratio as an input — `AwardCombatExperience`'s third parameter, and `UseAwardContext.OpponentPowerRatio` for the use rows — and the orchestration boundary gathers it: `AdvancementHandler` live (kill → victim; offensive ability use → target; damage taken → attacker) and `ProgressionScenarioExecutor` in the balance sandbox, once per kill. A live-vs-sandbox parity test pins the two callers equal.

- **Absent, unreadable, present.** `null` means the trigger named no opponent, so no ratio applies. `0.0` means an opponent was named but could not be read — ineligible, never a fallback to ratio-free XP. Any other value gives `scale = !IsFinite(ratio) || ratio < AntiGrindFloorRatio ? 0 : min(ratio, AntiGrindCap)`; `AntiGrindCap` (1.5) is "full", so an over-strong opponent is never a windfall.
- **All three rows carry `AppliesAntiGrindPowerRatio`.** The `CombatKill` row always has an opponent; the `AbilityUse` and `DamageTaken` rows apply it only when the context supplies one.
- **One constant, one meaning.** `AntiGrindFloorRatio` is also the relative-power read's *trivial* boundary: a mob that reads *trivial* to `consider` is exactly one whose kill awards nothing. In Tier 0 no mob reaches it at the 0.25 floor, so outgrown kills scale down without zeroing (see [balance.md](../../design/balance.md) Known gaps).

**Why the caller gathers.** Reading `IStatSystem` here would close a genuine DI cycle: `IStatSystem` → `IEffectSystem` → the DI-collected `IEnumerable<IEffectContributor>` → `ProgressionEffectContributor` → `IProgressionSystem` → `ProgressionSystem` → `IStatSystem`. Any contributor whose backing domain system itself reads a computed (effect-folded) score closes this shape — it is not specific to progression's math. The general rule: a contributor's backing system reads **raw component data** for the inputs it needs, or takes computed values **as inputs from its caller**; `IStatSystem`/`IEffectSystem` are the *output* seam a contributor feeds, never an input a contributor's own system may read. The relative-power read can depend on `IStatSystem` because nothing on that chain depends on it. An architecture-guard test pins that `ProgressionSystem` depends on none of `IStatSystem`, `IEffectSystem`, `IRelativePowerSystem` or `IPowerBudgetSystem` (the former raw-attribute estimate and its oracle dependency are gone).

### Contribution rides the existing `IEffectContributor` port

`ProgressionEffectContributor` implements the core-owned `IEffectContributor` port — the same one `EquipmentEffectContributor` and `AbilityEffectContributor` register on — rather than a parallel contributor interface. `IStatSystem.Get` already folds exactly one aggregation path (`IEffectSystem.GetModifiers`'s DI-collected contributor list); a second port would need `IStatSystem` re-plumbed to also fold it. `GetModifiers` returns `PowerPerImprovement × improvementCount(score)`; `GetActive` yields a synthetic `WhileKnown` effect per improved score track for display parity (mirrors `AbilityEffectContributor`) — nothing is ever written to `EffectsComponent` (INV-24).

## Interface

- [`IProgressionSystem.cs`](../../../Core/Modules/Progression/Systems/IProgressionSystem.cs) — `AwardUseExperience` (the one entry point; its `UseAwardContext` carries the optional `OpponentPowerRatio`), `AwardExperience`, `TryImprove`, `AwardCombatExperience` (takes the caller-gathered ratio), and read accessors (`GetXp`, `GetImprovementCount`, `GetXpToNextThreshold`, `GetTrackedScores`, `GetTrackedTracks`). Each read has a `ScoreId` overload delegating to the `ProgressionTrack` form. Returns result records (`AwardOutcome`, `UseAwardResult`, `CombatAwardResult`); publishes nothing.
- [`ProgressionTrack.cs`](../../../Core/Modules/Progression/ProgressionTrack.cs) · [`ProgressionTrackJsonConverter.cs`](../../../Core/Modules/Progression/ProgressionTrackJsonConverter.cs) — the track vocabulary and its key serialization.
- [`AdvancementRule.cs`](../../../Core/Modules/Progression/AdvancementRule.cs) · [`AdvancementEligibility.cs`](../../../Core/Modules/Progression/AdvancementEligibility.cs) · [`IAdvancementRuleRegistry.cs`](../../../Core/Modules/Progression/Systems/IAdvancementRuleRegistry.cs) — the rule shape and its lookup.
- [`ProgressionEffectContributor.cs`](../../../Core/Modules/Progression/ProgressionEffectContributor.cs) — the `IEffectContributor` registrant.
- [`ProgressionConstants.cs`](../../../Core/Modules/Progression/ProgressionConstants.cs) — every tuning knob (power step, threshold curve, `GlobalXpScalar`, anti-grind floor/cap, combat tracks) **and the `Rules` table**.

## Considerations

- **Persistence:** `ProgressionComponent` is `[Persistent]`, entity-keyed (`Dictionary<ProgressionTrack,int> Xp`/`Improvements`). Only ever attached lazily to a persistent (player) entity on first award — never to world content (INV-23). The key widening needs **no migration**: score keys serialize identically to the pre-slice enum-name form, and a persistence test pins a pre-slice payload re-serializing byte-identically.
- **Determinism (INV-26):** all chance flows through the injected `IRandom`, and the draw *sequence* is directly asserted. The anti-grind scale, the chance arithmetic, and the threshold math are pure functions of state and the supplied ratio.
- **Concurrency (INV-31):** the rule registry is immutable after construction; no background initiator and no shared mutable singleton state are added.
- **Registration:** `ProgressionModule.AddProgressionModule` registers `IAdvancementRuleRegistry`, `IProgressionSystem`, `IRelativePowerSystem`, the `IEffectContributor`, `AdvancementHandler`, `ProgressionNarrationHandler`, and the `progress` and `consider` commands. Called from `Server/CompositionRoot.Register` (not `Program.cs`) — the same reason `EconomyModule` is: the Blazor content-authoring host's `StatSystem` needs the contributor too, or it silently under-counts progression.
- **Known balance gap:** the two use-based rows feed *attribute* tracks, which do grant power, and the `progressionRate` simulation scenario **cannot see them** — it exercises `AwardCombatExperience` exclusively, so it validates neither their rate nor their anti-grind. Defaults are deliberately conservative (low `BaseChance`, meaningful decay) so the unvalidated rate is a slow drift rather than a step change. Extending the scenario is filed in [`../../roadmap/backlog.md`](../../roadmap/backlog.md); see [`../../design/balance.md`](../../design/balance.md).

## Extensibility

Adding an XP source (a rule row), a track, or tuning the curves is the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill's job — it documents the three-layer model (mechanism/tuning/generalization) this system was built to support. The character-wide Tier baseline (slice prog-2, Ascension — shipped) rides this same contributor port as a second, independent contribution source, without reshaping this system — see [`ascension-system.md`](ascension-system.md).

## Related

- Flow: [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md); [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md) and [flow-24](../../architecture/flows/flow-24-ability-activation.md) for the triggers.
- Reference rows: [`systems.md`](../../reference/systems.md), [`components.md`](../../reference/components.md), [`handlers.md`](../../reference/handlers.md), [`commands.md`](../../reference/commands.md).
- [`../preferences/preference-system.md`](../preferences/preference-system.md) — the preference framework the narration handler is gated on.
- [`stat-system.md`](../character-stats/stat-system.md) · [`effect-system.md`](../effects/effect-system.md) — the read seam this contributor folds into, and the port precedent (equipment, abilities).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine E.
- [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) · [`../../roadmap/completed/progression-use-based-xp.md`](../../roadmap/completed/progression-use-based-xp.md) — as-built history and design decisions.
