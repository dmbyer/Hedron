# Progression

> Use-driven experience: every attribute/pool track accrues XP through play and grants a linear power step on crossing a growing threshold. A character-wide Tier scalar layers an additive power baseline on top, and a shared power-budget oracle turns any of it into an inspectable power scalar + tier band. **Status:** live (slice prog-1 — substrate; slice prog-2 — Ascension/tier; slice prog-3 — power model + balance inspector; program continues with prog-4…5).

## What it is

A player earns experience on the four attribute tracks (`Mind`, `Body`, `Spirit`, `Attunement`) and the `HpMax` pool track just by playing — slice 1 wires combat kills as the only source. Crossing a track's next threshold grants a permanent, linear power step to that score; the threshold to the *next* step grows, so power is near-unlimited but the *rate* of gain slows over time — the curve lives in the threshold, never in the power. A trivial victim (far weaker than the killer) grants little or no XP; an over-strong victim never grants a windfall beyond a capped multiplier. Players inspect their progress with `progress`.

On top of per-track progression sits **Ascension** (slice prog-2): a character-wide Tier scalar (`0`–`6`) that confers a flat additive power baseline across the same tracked scores — layered on top of progression power, never replacing or resetting it. An admin `ascend` command is the interim tier-up trigger (the real player-facing Ascension-Objective gate is deferred); mobs carry a lightweight, authored tier-band tag so content threat is emergent from the baseline. See [`ascension-system.md`](ascension-system.md).

**Power model + balance inspector** (slice prog-3) turns all of the above into an inspectable, comparable number. `IPowerBudgetSystem` is a core-tier, generic oracle: given a score snapshot (never an entity id), it computes a weighted-sum power scalar and classifies it into a derived tier band. Three consumers read it — admin-gated `power`/`powerband` in-game inspectors, a computed power/band readout on the Blazor `ItemEditor`/`MobEditor` (the primary designer observability surface), and the `ProgressionSystem` anti-grind proxy, rewired off its inline attribute sum onto the shared oracle. Items now carry the same lightweight, authored tier-band tag mobs already had. See [`power-budget-system.md`](power-budget-system.md).

## How it works

`ExperienceAwardHandler` subscribes to `MobDiedEvent` (one of three independent subscribers alongside `CurrencyLootHandler` and `SpawnSystem` — see [flow-20](../../architecture/flows/flow-20-mob-death-respawn.md)) and calls `IProgressionSystem.AwardCombatExperience`, which computes an anti-grind scale from the killer's and victim's raw attributes, rolls a randomized per-track base amount, and awards each combat track through `AwardExperience` → `TryImprove`. The handler publishes `ExperienceAwardedEvent` (every positive award) and `TrackImprovedEvent` (every threshold crossed) — both thin, past-tense, orchestrated by the handler, never by the system (INV-5/INV-8).

The power a track has earned is **never stored as a stat**. `ProgressionEffectContributor` — a third registrant on the same `IEffectContributor` port `EquipmentEffectContributor` and `AbilityEffectContributor` already use — is pulled on read by `IStatSystem.Get` and returns `PowerPerImprovement × improvementCount(track)` for that score, fresh every call (INV-24). This is the whole mechanism: nothing precomputes or caches a "current effective score with progression baked in."

## Systems

| System | Role |
|---|---|
| [`progression-system.md`](progression-system.md) | `ProgressionComponent` (per-track XP/improvement counts), `IProgressionSystem` (award/improve/read), `ProgressionEffectContributor` (the contribute-on-read fold), `ProgressionConstants` (the tuning knobs) |
| [`ascension-system.md`](ascension-system.md) | `AscensionComponent` (tier scalar + unlock records), `IAscensionSystem` (tier read/ascend gate), `AscensionEffectContributor` (the additive-baseline contribute-on-read fold), `AscensionConstants` (tier baseline step, unlock table); mob `TierBand` content tag |
| [`power-budget-system.md`](power-budget-system.md) | `IPowerBudgetSystem`/`PowerBudgetSystem` (core-tier oracle — `Estimate`/`Classify`/`BandAnchor`), `PowerBudgetConstants` (weight table, band span, mirrored reference-build + tier-band constants); `power`/`powerband` inspectors; item `TierBand` content tag |

## Surfaces

- **Commands** — `progress` (player) — lists every track's improvement count, cumulative XP, and XP-to-next-threshold. `ascend [characterName]` (admin) — ascends the target one tier. `setmob band <blueprintId> <tier>` / `setitem band <blueprintId> <tier>` (admin) — author a mob's/item's tier-band tag. `power [target]` / `powerband [tier]` (admin) — inspect computed power + tier band. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `ExperienceAwardedEvent` (a track gained XP — frequent, thin), `TrackImprovedEvent` (a track crossed a threshold — the discrete milestone other slices/systems subscribe to), `AscendedEvent` (tier changed — milestone), `PlayerAscendedByAdminEvent` (admin audit). The power-budget oracle and its inspectors publish no events — pure read tools. See [`../../architecture/03-events.md`](../../architecture/03-events.md).
- **Components** — `ProgressionComponent`, `AscensionComponent` (both `[Persistent]`, player-only — never attached to world content); `MobDataComponent.TierBand` / `ItemDataComponent.TierBand` (world content, never snapshotted in practice). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — Progression itself has none (runtime-accrued state on persistent entities, not authored world content — a mob's value as an XP source is a function of its *existing* scores, read live). Ascension ships mob tier-band authoring: `setmob band`, YAML `band:` field, and the Blazor `MobEditor` band field. The power-budget slice mirrors this for items (`setitem band`, YAML `band:` field, Blazor `ItemEditor` field) and adds the computed power/band readout to both editors — the primary designer observability surface. An admin `setprogress`-style hand-set command is deliberately deferred — no balance/testing task needs it yet — tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Flows

- [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) — the combat-award → threshold-improve → contribute-on-read path.
- [flow-32 — Ascension journey](../../architecture/flows/flow-32-ascension.md) — the tier-up → unlock-record → baseline-fold path.
- [flow-20 — Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md) — `ExperienceAwardHandler`'s trigger (`MobDiedEvent`), alongside `CurrencyLootHandler` and `SpawnSystem`.

## Extending progression

Adding an XP source, tuning the curves, adding a track, or generalizing triggers to a rule table is covered by the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill — the three-layer extensibility model (mechanism = handler-on-event, tuning = constants → YAML, generalization = rule table at the ≥3-source threshold) lives there, not here.

## The program this slice belongs to

Progression substrate (slice prog-1), Ascension (slice prog-2), and the power model + balance inspector (slice prog-3) are the first three of the five-slice **Progression & Balance program**. The offline simulation harness (slice prog-4) will consume the power-budget oracle as its expected-vs-actual outcome oracle; the agentic/balance-doc layer (slice prog-5) follows. See [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md), [`../../roadmap/completed/ascension.md`](../../roadmap/completed/ascension.md), and [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) for the as-built history and design decisions, and [`../../roadmap/plan.md`](../../roadmap/plan.md) for the program's current status.

## Related

- [`progression-system.md`](progression-system.md) · [`ascension-system.md`](ascension-system.md) · [`power-budget-system.md`](power-budget-system.md) — the system design docs.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-24 (contribute-on-read), INV-5/INV-8 (systems return, handlers publish), INV-26 (determinism), INV-22/23 (persistence two-level opt-in; world-content vs persistent domains), INV-2 (core-tier no domain import — the power-budget oracle).
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — Spine E, the design model this instances; §6 R1 for Ascension.
- [`../../roadmap/completed/progression-substrate.md`](../../roadmap/completed/progression-substrate.md) · [`../../roadmap/completed/ascension.md`](../../roadmap/completed/ascension.md) · [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) — as-built history and design decisions.
- **Character stats** — [`../character-stats/stat-system.md`](../character-stats/stat-system.md) — the `IStatSystem`/`IEffectSystem` read seam the contributor folds into.
- **Effects** — [`../effects/effect-system.md`](../effects/effect-system.md) — the `IEffectContributor` port precedent (equipment, abilities).
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — `MobDiedEvent`, this feature's trigger.
- **Mobs** — [`../mobs/mob-system.md`](../mobs/mob-system.md) — the builder/template/writer authoring pattern the tier-band tag mirrors.
