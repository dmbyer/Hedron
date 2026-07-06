# Progression system

> Use-driven per-track experience accrual, threshold improvement, and the contribute-on-read power fold. **Authoring checkpoint:** slice prog-1. Living document.

## What it is / does

Domain-tier. `ProgressionSystem` owns two decisions: (1) accruing cumulative XP per `ScoreId` track and (2) resolving how many thresholds a track has crossed. `ProgressionEffectContributor` is the read-side adapter that turns "improvement count" into "power" for any `IStatSystem.Get` caller. Neither touches the event bus (INV-5) — `ExperienceAwardHandler` is the Initiator that publishes the result.

## How it works

**A track is a `ScoreId`** — no parallel key type. Slice 1 tracks the four primary attributes plus `HpMax` (the ungoverned pool); derived scores (`AttackPower`/`Defense`) are never tracked directly since they rise via their inputs' tracks.

**Accrual is cumulative, never decremented.** `AwardExperience(entity, track, amount, source)` adds `amount` to `ProgressionComponent.Xp[track]`, creating the component/entry lazily on first award. A non-positive amount is a no-op — no entry, no component created.

**Threshold math is linear-cumulative.** The cumulative XP required to have earned the *k*-th improvement is `ThresholdBase + k × ThresholdIncrement` (both in `ProgressionConstants`) — strictly increasing, so the "next" threshold always exceeds the last. `TryImprove` loops while cumulative XP ≥ the next threshold, incrementing the improvement count once per crossing; a single large award can cross several thresholds in one call. The power step itself (`PowerPerImprovement`) is constant — **the curve lives in the threshold, never in the power.**

**Combat award = anti-grind scale × randomized base, per track.** `AwardCombatExperience` computes `scale = ratio < AntiGrindFloorRatio ? 0 : min(ratio, AntiGrindCap)` where `ratio = victimPower / killerPower`; if `scale == 0` no `IRandom` draw happens (a trivial victim costs nothing, consumes no randomness); otherwise a base amount is drawn per track via `IRandom.Next(CombatAwardMin, CombatAwardMax+1)` and scaled (INV-26 — the only chance in this system).

### Anti-grind proxy reads raw attributes, not `IStatSystem`

The killer/victim "effective power" the anti-grind scale compares comes from raw `AttributesComponent.{Mind,Body,Spirit,Attunement}` fields, read directly via `EntityService`, packed into a `PowerSnapshot` and run through `IPowerBudgetSystem.Estimate` (the core-tier oracle shipped slice `prog-3` — see [power-budget-system.md](power-budget-system.md)) — **not** `IStatSystem.Get`. Going through `IStatSystem` here creates a genuine DI cycle: `IStatSystem` → `IEffectSystem` → the DI-collected `IEnumerable<IEffectContributor>` → `ProgressionEffectContributor` → `IProgressionSystem` → `ProgressionSystem` → `IStatSystem`. Any contributor whose backing domain system itself reads a computed (effect-folded) score closes this shape — it is not specific to progression's math. The general rule: a contributor's backing system reads **raw component data** for the inputs it needs; `IStatSystem`/`IEffectSystem` are the *output* seam a contributor feeds, never an input a contributor's own system may read. Injecting `IPowerBudgetSystem` (a **core** system) does not reopen this cycle — the guard is that the snapshot values stay raw, not that the oracle is un-injected. The ratio is scale-invariant under the shared weight table, so the rewire is behaviorally equivalent up to the weighted-sum rescale.

### Contribution rides the existing `IEffectContributor` port

`ProgressionEffectContributor` implements the core-owned `IEffectContributor` port — the same one `EquipmentEffectContributor` and `AbilityEffectContributor` register on — rather than a parallel contributor interface. `IStatSystem.Get` already folds exactly one aggregation path (`IEffectSystem.GetModifiers`'s DI-collected contributor list); a second port would need `IStatSystem` re-plumbed to also fold it. `GetModifiers` returns `PowerPerImprovement × improvementCount(score)`; `GetActive` yields a synthetic `WhileKnown` effect per improved track for display parity (mirrors `AbilityEffectContributor`) — nothing is ever written to `EffectsComponent` (INV-24).

## Interface

- [`IProgressionSystem.cs`](../../../Core/Modules/Progression/Systems/IProgressionSystem.cs) — `AwardExperience`, `TryImprove`, `AwardCombatExperience`, and read accessors (`GetXp`, `GetImprovementCount`, `GetXpToNextThreshold`, `GetTrackedScores`). Returns result records (`AwardOutcome`, `CombatAwardResult`); publishes nothing.
- [`ProgressionEffectContributor.cs`](../../../Core/Modules/Progression/ProgressionEffectContributor.cs) — the `IEffectContributor` registrant.
- [`ProgressionConstants.cs`](../../../Core/Modules/Progression/ProgressionConstants.cs) — every tuning knob (power step, threshold curve, combat award range, anti-grind floor/cap, tracked scores).

## Considerations

- **Persistence:** `ProgressionComponent` is `[Persistent]`, entity-keyed (`Dictionary<ScoreId,int> Xp`/`Improvements`, enum-name-serialized like `WalletComponent`). Only ever attached lazily to a persistent (player) entity on first award — never to world content (INV-23).
- **Determinism (INV-26):** the only chance is the per-track base-award roll via injected `IRandom`; the anti-grind scale and threshold math are pure functions of state.
- **Registration:** `ProgressionModule.AddProgressionModule` registers `IProgressionSystem`, the `IEffectContributor`, `ExperienceAwardHandler`, and the `progress` command. Called from `Server/CompositionRoot.Register` (not `Program.cs`) — the same reason `EconomyModule` is: the Blazor content-authoring host's `StatSystem` needs the contributor too, or it silently under-counts progression.

## Extensibility

Adding an XP source, a track, or generalizing to a rule table is the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill's job — it documents the three-layer model (mechanism/tuning/generalization) this system was built to support. The character-wide Tier baseline (slice prog-2, Ascension — shipped) rides this same contributor port as a second, independent contribution source, without reshaping this system — see [`ascension-system.md`](ascension-system.md).

## Related

- Flow: [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md); [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md) for the trigger.
- Reference rows: [`systems.md`](../../reference/systems.md), [`components.md`](../../reference/components.md), [`handlers.md`](../../reference/handlers.md).
- [`stat-system.md`](../character-stats/stat-system.md) · [`effect-system.md`](../effects/effect-system.md) — the read seam this contributor folds into, and the port precedent (equipment, abilities).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine E.
- [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) — as-built history and design decisions (including the DI-cycle fix).
