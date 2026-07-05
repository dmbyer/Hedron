# Progression

> Use-driven experience: every attribute/pool track accrues XP through play and grants a linear power step on crossing a growing threshold. A character-wide Tier scalar layers an additive power baseline on top. **Status:** live (slice prog-1 — substrate; slice prog-2 — Ascension/tier; program continues with prog-3…5).

## What it is

A player earns experience on the four attribute tracks (`Mind`, `Body`, `Spirit`, `Attunement`) and the `HpMax` pool track just by playing — slice 1 wires combat kills as the only source. Crossing a track's next threshold grants a permanent, linear power step to that score; the threshold to the *next* step grows, so power is near-unlimited but the *rate* of gain slows over time — the curve lives in the threshold, never in the power. A trivial victim (far weaker than the killer) grants little or no XP; an over-strong victim never grants a windfall beyond a capped multiplier. Players inspect their progress with `progress`.

On top of per-track progression sits **Ascension** (slice prog-2): a character-wide Tier scalar (`0`–`6`) that confers a flat additive power baseline across the same tracked scores — layered on top of progression power, never replacing or resetting it. An admin `ascend` command is the interim tier-up trigger (the real player-facing Ascension-Objective gate is deferred); mobs carry a lightweight, authored tier-band tag so content threat is emergent from the baseline. See [`ascension-system.md`](ascension-system.md).

## How it works

`ExperienceAwardHandler` subscribes to `MobDiedEvent` (one of three independent subscribers alongside `CurrencyLootHandler` and `SpawnSystem` — see [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md)) and calls `IProgressionSystem.AwardCombatExperience`, which computes an anti-grind scale from the killer's and victim's raw attributes, rolls a randomized per-track base amount, and awards each combat track through `AwardExperience` → `TryImprove`. The handler publishes `ExperienceAwardedEvent` (every positive award) and `TrackImprovedEvent` (every threshold crossed) — both thin, past-tense, orchestrated by the handler, never by the system (INV-5/INV-8).

The power a track has earned is **never stored as a stat**. `ProgressionEffectContributor` — a third registrant on the same `IEffectContributor` port `EquipmentEffectContributor` and `AbilityEffectContributor` already use — is pulled on read by `IStatSystem.Get` and returns `PowerPerImprovement × improvementCount(track)` for that score, fresh every call (INV-24). This is the whole mechanism: nothing precomputes or caches a "current effective score with progression baked in."

## Systems

| System | Role |
|---|---|
| [`progression-system.md`](progression-system.md) | `ProgressionComponent` (per-track XP/improvement counts), `IProgressionSystem` (award/improve/read), `ProgressionEffectContributor` (the contribute-on-read fold), `ProgressionConstants` (the tuning knobs) |
| [`ascension-system.md`](ascension-system.md) | `AscensionComponent` (tier scalar + unlock records), `IAscensionSystem` (tier read/ascend gate), `AscensionEffectContributor` (the additive-baseline contribute-on-read fold), `AscensionConstants` (tier baseline step, unlock table); mob `TierBand` content tag |

## Surfaces

- **Commands** — `progress` (player) — lists every track's improvement count, cumulative XP, and XP-to-next-threshold. `ascend [characterName]` (admin) — ascends the target one tier. `setmob band <blueprintId> <tier>` (admin) — authors a mob's tier-band tag. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `ExperienceAwardedEvent` (a track gained XP — frequent, thin), `TrackImprovedEvent` (a track crossed a threshold — the discrete milestone other slices/systems subscribe to), `AscendedEvent` (tier changed — milestone), `PlayerAscendedByAdminEvent` (admin audit). See [`../../architecture/03-events.md`](../../architecture/03-events.md).
- **Components** — `ProgressionComponent`, `AscensionComponent` (both `[Persistent]`, player-only — never attached to world content); `MobDataComponent.TierBand` (world content, never snapshotted). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — Progression itself has none (runtime-accrued state on persistent entities, not authored world content — a mob's value as an XP source is a function of its *existing* scores, read live). Ascension ships mob tier-band authoring: `setmob band`, YAML `band:` field, and the Blazor `MobEditor` band field. An admin `setprogress`-style hand-set command is deliberately deferred — no balance/testing task needs it yet — tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Flows

- [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) — the combat-award → threshold-improve → contribute-on-read path.
- [flow-32 — Ascension journey](../../architecture/flows/flow-32-ascension.md) — the tier-up → unlock-record → baseline-fold path.
- [flow-20 — Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md) — `ExperienceAwardHandler`'s trigger (`MobDiedEvent`), alongside `CurrencyLootHandler` and `SpawnSystem`.

## Extending progression

Adding an XP source, tuning the curves, adding a track, or generalizing triggers to a rule table is covered by the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill — the three-layer extensibility model (mechanism = handler-on-event, tuning = constants → YAML, generalization = rule table at the ≥3-source threshold) lives there, not here.

## The program this slice belongs to

Progression substrate (slice prog-1) and Ascension (slice prog-2) are the first two of the five-slice **Progression & Balance program** — the shared power-budget oracle (slice prog-3), the offline simulation harness (slice prog-4), and the agentic/balance-doc layer (slice prog-5) build on the contribute-on-read seam both this substrate and Ascension establish. See [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) and [`../../roadmap/completed/ascension.md`](../../roadmap/completed/ascension.md) for the as-built history and design decisions, and [`../../roadmap/plan.md`](../../roadmap/plan.md) for the program's current status.

## Related

- [`progression-system.md`](progression-system.md) · [`ascension-system.md`](ascension-system.md) — the system design docs.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-24 (contribute-on-read), INV-5/INV-8 (systems return, handlers publish), INV-26 (determinism), INV-22/23 (persistence two-level opt-in; world-content vs persistent domains).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine E, the design model this instances; §6 R1 for Ascension.
- [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) · [`../../roadmap/completed/ascension.md`](../../roadmap/completed/ascension.md) — as-built history and design decisions.
- **Character stats** — [`../character-stats/stat-system.md`](../character-stats/stat-system.md) — the `IStatSystem`/`IEffectSystem` read seam the contributor folds into.
- **Effects** — [`../effects/effect-system.md`](../effects/effect-system.md) — the `IEffectContributor` port precedent (equipment, abilities).
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — `MobDiedEvent`, this feature's trigger.
- **Mobs** — [`../mobs/mob-system.md`](../mobs/mob-system.md) — the builder/template/writer authoring pattern the tier-band tag mirrors.
