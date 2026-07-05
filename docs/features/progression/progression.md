# Progression

> Use-driven experience: every attribute/pool track accrues XP through play and grants a linear power step on crossing a growing threshold. **Status:** live (slice prog-1 — substrate; program continues with prog-2…5).

## What it is

A player earns experience on the four attribute tracks (`Mind`, `Body`, `Spirit`, `Attunement`) and the `HpMax` pool track just by playing — slice 1 wires combat kills as the only source. Crossing a track's next threshold grants a permanent, linear power step to that score; the threshold to the *next* step grows, so power is near-unlimited but the *rate* of gain slows over time — the curve lives in the threshold, never in the power. A trivial victim (far weaker than the killer) grants little or no XP; an over-strong victim never grants a windfall beyond a capped multiplier. Players inspect their progress with `progress`.

## How it works

`ExperienceAwardHandler` subscribes to `MobDiedEvent` (one of three independent subscribers alongside `CurrencyLootHandler` and `SpawnSystem` — see [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md)) and calls `IProgressionSystem.AwardCombatExperience`, which computes an anti-grind scale from the killer's and victim's raw attributes, rolls a randomized per-track base amount, and awards each combat track through `AwardExperience` → `TryImprove`. The handler publishes `ExperienceAwardedEvent` (every positive award) and `TrackImprovedEvent` (every threshold crossed) — both thin, past-tense, orchestrated by the handler, never by the system (INV-5/INV-8).

The power a track has earned is **never stored as a stat**. `ProgressionEffectContributor` — a third registrant on the same `IEffectContributor` port `EquipmentEffectContributor` and `AbilityEffectContributor` already use — is pulled on read by `IStatSystem.Get` and returns `PowerPerImprovement × improvementCount(track)` for that score, fresh every call (INV-24). This is the whole mechanism: nothing precomputes or caches a "current effective score with progression baked in."

## Systems

| System | Role |
|---|---|
| [`progression-system.md`](progression-system.md) | `ProgressionComponent` (per-track XP/improvement counts), `IProgressionSystem` (award/improve/read), `ProgressionEffectContributor` (the contribute-on-read fold), `ProgressionConstants` (the tuning knobs) |

## Surfaces

- **Commands** — `progress` (player) — lists every track's improvement count, cumulative XP, and XP-to-next-threshold. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `ExperienceAwardedEvent` (a track gained XP — frequent, thin), `TrackImprovedEvent` (a track crossed a threshold — the discrete milestone other slices/systems subscribe to). See [`../../architecture/03-events.md`](../../architecture/03-events.md).
- **Components** — `ProgressionComponent` (`[Persistent]`, player-only — never attached to world content). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — none. Progression is runtime-accrued state on persistent entities, not authored world content; there is no YAML shape or `TemplateRegistry` entry (a mob's value as an XP source is a function of its *existing* scores, read live). An admin `setprogress`-style hand-set command is deliberately deferred — no balance/testing task needs it yet — tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Flows

- [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) — the combat-award → threshold-improve → contribute-on-read path.
- [flow-20 — Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md) — `ExperienceAwardHandler`'s trigger (`MobDiedEvent`), alongside `CurrencyLootHandler` and `SpawnSystem`.

## Extending progression

Adding an XP source, tuning the curves, adding a track, or generalizing triggers to a rule table is covered by the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill — the three-layer extensibility model (mechanism = handler-on-event, tuning = constants → YAML, generalization = rule table at the ≥3-source threshold) lives there, not here.

## The program this slice belongs to

Progression substrate (this slice) is slice 1 of the five-slice **Progression & Balance program** — the character-wide Tier/Ascension scalar (slice 2), the shared power-budget oracle (slice 3), the offline simulation harness (slice 4), and the agentic/balance-doc layer (slice 5) build on this substrate's contribute-on-read seam. See [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) for the as-built history and design decisions, and [`../../roadmap/plan.md`](../../roadmap/plan.md) for the program's current status.

## Related

- [`progression-system.md`](progression-system.md) — the system design doc.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-24 (contribute-on-read), INV-5/INV-8 (systems return, handlers publish), INV-26 (determinism), INV-22/23 (persistence two-level opt-in; world-content vs persistent domains).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine E, the design model this instances.
- [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) — as-built history and design decisions.
- **Character stats** — [`../character-stats/stat-system.md`](../character-stats/stat-system.md) — the `IStatSystem`/`IEffectSystem` read seam the contributor folds into.
- **Effects** — [`../effects/effect-system.md`](../effects/effect-system.md) — the `IEffectContributor` port precedent (equipment, abilities).
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — `MobDiedEvent`, this feature's trigger.
