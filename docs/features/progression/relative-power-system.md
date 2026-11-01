# Relative-power system

> The one shared measure of how strong a target is relative to a subject, on **effective** power for both sides, and the ratio → danger-label rule. Read by `consider`, the admin `power` relation line, and anti-grind. **Authoring checkpoint:** slice relative-power-consider (Phase 5 slice 5). Living document.

## What it is / does

Domain-tier, in the Progression module. `RelativePowerSystem` owns one decision: **how a target compares with a subject**. It measures each live entity's effective power, divides target ÷ subject into a ratio, and classifies the ratio into one of five labels — *trivial*, *easy*, *even*, *dangerous*, *deadly*. It composes the domain `IStatSystem` (effective scores) with the core `IPowerBudgetSystem` oracle ([power-budget-system.md](power-budget-system.md)); it is stateless and pure, and publishes nothing (INV-5, INV-26).

It is a **read**, not a rule about consequences. What the ratio *does* belongs to each consumer: `ProgressionSystem` turns it into an XP scale ([progression-system.md](progression-system.md#anti-grind-ratio-is-supplied-by-the-caller)); `consider` turns its label into a phrase.

## How it works

### Effective power, one way for every consumer

`GetEffectivePower(entity)` snapshots every `ScoreId` through `IStatSystem.Get` — so worn gear, progression improvements, active effects and a player's tier all count — and estimates it with the oracle. Current HP has weight 0, so a wounded mob reads the same as a fresh one. Nothing is cached: each call reads the entity as it is now, so a label changes as soon as gear, improvements, effects or tier change.

**Tier source.** The oracle's tier argument is `MobDataComponent.Tier` for a mob, and 0 for anything else. A player's tier (and a sandbox combatant's) already arrives through `AscensionEffectContributor` inside `IStatSystem.Get`, and a live mob never carries `AscensionComponent`, so tier is never counted twice. The two tier representations agree only while the balance-standards registry's `TierBaselineStep`/`TrackedScores` match `AscensionConstants`; an architecture-guard test pins the defaults, and the standards store warns on an operator-edited drift.

This is the same measurement the admin `power` command prints for a live self or mob target, so `power`, `consider` and anti-grind can never disagree about an entity (INV-19). Item targets stay on `IItemPowerProjectionSystem`.

### Readability is explicit

`IStatSystem` never says "missing": `AttributeSystem` returns reference defaults for an entity with no components, so a destroyed opponent would read as a fresh reference build and produce a plausible ratio. `Relate` therefore checks explicitly. A reading is **unreadable** when either side lacks `AttributesComponent`, or the subject's power is `<= 0`; it then has `IsReadable false`, `Ratio 0.0` and no label. Callers pass `Ratio` through verbatim: at `0.0` the anti-grind floor makes an award ineligible (never a divide-by-zero, never ratio-free XP), and `consider` prints "You can't tell how dangerous …". `GetEffectivePower` itself stays ungated, because it is `power`'s raw readout.

### Ratio → label

Inclusive lower bounds, from `ProgressionConstants`:

| Label | Ratio (target ÷ subject) | Constant |
|---|---|---|
| trivial | `< 0.25` | — |
| easy | `≥ 0.25` | `AntiGrindFloorRatio` |
| even | `≥ 0.85` | `RelativeEvenFloorRatio` |
| dangerous | `≥ 1.12` | `RelativeDangerousFloorRatio` |
| deadly | `≥ 1.30` | `RelativeDeadlyFloorRatio` |

**One constant, one meaning.** The trivial boundary **is** `AntiGrindFloorRatio`, never a copy: a mob that reads *trivial* is exactly one whose kill would award no XP. **Calibration:** against the oracle defaults a fresh character is 386 power, so the Tier-0 band midpoints read B1 (ratio 1.052) *even* and B3 (1.259) *dangerous*; B2's midpoint (1.155) reads *dangerous*. A band-midpoint fixture test validates this against the oracle rather than copied numbers. The knob rows and the known gap that *trivial* is unreachable in Tier 0 live in [balance.md](../../design/balance.md).

**Labels compress at higher tiers.** The oracle's tier baseline is additive on a growing base, so one tier above is ×1.31 at T0 but only ×1.16 at T3. That is a property of the oracle, flagged for balance closure.

### The label belongs to the read; phrases belong to the consumer

Mapping a ratio to a label is a game rule with more than one consumer, so it lives here (INV-1, INV-8), not in `consider` or `power`. Presentation stays with each consumer: `ConsiderCommand` owns the label → phrase table, and `power` prints the label's lower-case name.

### Power-keyed overload

The entity overload resolves the target's effective power and delegates to `Relate(subject, targetPower)`. The power overload exists for later consumers that compare a player against a *cell anchor* rather than an entity (entrance telegraphs, area banners, mob-AI threat). It is built and tested, with no consumer yet.

## Consumers

| Consumer | Uses | Doc |
|---|---|---|
| `consider <mob>` (player) | `Relate` → one fixed phrase per label; publishes `MobConsideredEvent` on a successful label only | [flow-17](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) · [commands.md](../../reference/commands.md#consider--con) |
| `power [target]` (admin) | `GetEffectivePower` for live targets; `Relate(invoker, mob)` for the `vs you` relation line | [power-budget-system.md](power-budget-system.md#consumers-one-function-each-inv-19) |
| `AdvancementHandler` (live anti-grind gather) | `Relate(earner, opponent).Ratio`: kill → victim; offensive ability use → target; damage taken → attacker | [flow-31](../../architecture/flows/flow-31-progression-award.md) |
| `ProgressionScenarioExecutor` (sandbox) | `SandboxWorld.RelativePower.Relate(subject, victim).Ratio`, once per kill | [flow-33](../../architecture/flows/flow-33-simulation-run.md) |

## Interface

- [`IRelativePowerSystem.cs`](../../../Core/Modules/Progression/Systems/IRelativePowerSystem.cs) — `GetEffectivePower` (ungated), the two `Relate` overloads (readability-gated, returning a `RelativePowerReading`), and `Classify`.
- [`RelativePowerLabel.cs`](../../../Core/Modules/Progression/RelativePowerLabel.cs) — the five labels, weakest to strongest.
- [`ProgressionConstants.cs`](../../../Core/Modules/Progression/ProgressionConstants.cs) — the cut-points beside the anti-grind floor.

## Considerations

- **No DI cycle.** The read depends on `IStatSystem`, and nothing on the `IStatSystem` → `IEffectSystem` → contributors → `IProgressionSystem` chain depends on the read. `ProgressionSystem` must never depend on it; an architecture-guard test pins both directions.
- **Combat agrees with the read.** Since combat-effective-scores, combat derives attack and defense from effective `Body` and caps pools at their effective max, so the progression, gear, effects and tier this read counts also win fights. A side effect: `AttackPower`/`Defense` rise with effective `Body`, so a `Body`-improved or tiered character's power reads higher than before (anti-grind tapers faster; labels shift) — the honest number. A fresh Tier-0 character carries no `Body` modifiers and reads as before.
- **Live vs sandbox above Tier 0.** A live mob's tier only feeds the oracle (which projects the baseline onto `Body`/`HpMax`), while a sandbox combatant's tier folds for real and now also raises its derived scores — so a sandbox tier-1 victim reads stronger than a live tier-1 mob. The parity test pins Tier 0 (all Phase 5 has) and records the tier-1 divergence; closing it is Phase 6 tier tuning ([backlog](../../roadmap/backlog.md#-ascension-tier-tuning-and-the-tier-projection-gap)).
- **Composition.** `ProgressionModule.AddProgressionModule` registers it on both hosts; the balance sandbox builds its own over the sandbox `StatSystem` and the shared oracle (`SandboxWorld.RelativePower`). A live-vs-sandbox parity test pins the two.
- **Concurrency (INV-31).** Stateless; runs on the session and heartbeat paths only.

## Extensibility

A new opponent-bearing XP source joins anti-grind by setting `AppliesAntiGrindPowerRatio` on its rule and gathering one `Relate(...).Ratio` at its orchestration boundary — data plus one call, no new mechanism. A new danger-read surface (a telegraph, a banner, an AI threat check) calls `Relate` — the power overload when comparing against a cell anchor — and owns only its own presentation.

## Related

- [`progression.md`](progression.md) — the feature; [`progression-system.md`](progression-system.md) — what the ratio does to an award; [`power-budget-system.md`](power-budget-system.md) — the oracle underneath.
- Flows: [flow-17](../../architecture/flows/flow-17-kill-mob-combat-initiation.md) (`consider`), [flow-31](../../architecture/flows/flow-31-progression-award.md) (anti-grind gather), [flow-33](../../architecture/flows/flow-33-simulation-run.md) (sandbox gather).
- Reference rows: [`systems.md`](../../reference/systems.md), [`commands.md`](../../reference/commands.md).
- [`../../design/balance.md`](../../design/balance.md) — the cut-point knob rows and known gaps.
