# Progression

> Use-driven experience: every attribute/pool track accrues XP through play and grants a linear power step on crossing a growing threshold. A character-wide Tier scalar layers an additive power baseline on top, and a shared power-budget oracle turns any of it into an inspectable power scalar + tier band. **Status:** live (slice prog-1 — substrate; slice prog-2 — Ascension/tier; slice prog-3 — power model + balance inspector; slice prog-6 — use-based XP, chance gating, visibility and tuning scales; program continues).

## What it is

A player earns experience just by playing, on two kinds of track: **score tracks** (the four attributes `Mind`/`Body`/`Spirit`/`Attunement` plus the `HpMax` pool) and **ability tracks** (one per skill or spell). Three sources feed them — killing something, using an ability, and taking damage. Crossing a track's next threshold grants a permanent, linear power step to a score; the threshold to the *next* step grows, so power is near-unlimited but the *rate* of gain slows over time — the curve lives in the threshold, never in the power. Ability tracks share that same curve, but their rank is **display-only**: it is shown beside the skill and grants no power.

Use-based gain is **chance-gated**, not deterministic: each qualifying action rolls for an award, and the chance decays as the track's rank rises — so progression is sub-linear in action count rather than a grind counter. A trivial victim (far weaker than the killer) grants little or no XP; an over-strong victim never grants a windfall beyond a capped multiplier.

Every award and every improvement **narrates** to the earning player, and either class of line can be silenced with `config` (see [preferences](../preferences/preference-system.md)). Players inspect their standing with `progress`, and their per-ability rank with `skills` / `spells` / `abilities`.

On top of per-track progression sits **Ascension** (slice prog-2): a character-wide Tier scalar (`0`–`6`) that confers a flat additive power baseline across the same tracked scores — layered on top of progression power, never replacing or resetting it. An admin `ascend` command is the interim tier-up trigger (the real player-facing Ascension-Objective gate is deferred); mobs carry a lightweight, authored tier-band tag so content threat is emergent from the baseline. See [`ascension-system.md`](ascension-system.md).

**Power model + balance inspector** (slice prog-3) turns all of the above into an inspectable, comparable number. `IPowerBudgetSystem` is a core-tier, generic oracle: given a score snapshot (never an entity id), it computes a weighted-sum power scalar and classifies it into a derived tier band. Three consumers read it — admin-gated `power`/`powerband` in-game inspectors, a computed power/band readout on the Blazor `ItemEditor`/`MobEditor` (the primary designer observability surface), and the `ProgressionSystem` anti-grind proxy, rewired off its inline attribute sum onto the shared oracle. Items now carry the same lightweight, authored tier-band tag mobs already had. See [`power-budget-system.md`](power-budget-system.md).

## How it works

**One handler, one rule table.** `AdvancementHandler` subscribes to all four trigger events (`MobDiedEvent`, `AbilityActivatedEvent`, `CombatRoundEvent`, `AbilityStrikeResolvedEvent`) and does nothing but translate each into a `UseAwardContext` before calling `IProgressionSystem.AwardUseExperience`. Which tracks are candidates, whether the action qualifies, its chance, and its amount are all data on an `AdvancementRule` looked up by `XpSource` — so adding a fourth source is a row plus a mapping, never a fourth handler (INV-19). The handler publishes `ExperienceAwardedEvent` (every positive award) and `TrackImprovedEvent` (every threshold crossed) — both thin, past-tense, orchestrated by the handler, never by the system (INV-5/INV-8). `ProgressionNarrationHandler` turns each into a preference-gated line for the earner.

**Two tiers of tuning.** One `GlobalXpScalar` moves all progression at once; per-source, per-ability (`AbilityDefinition.XpScale`) and per-mob (`MobDataComponent.XpScale`) scales tune individual pieces. All four multiply into every award inside the system, so no call site can bypass them.

**The kill path is unchanged.** The `CombatKill` rule row reproduces the pre-slice award exactly — same tracks, same range, same anti-grind scaling, and the same `IRandom` draw sequence at a fixed seed, because a rule whose chance is a certainty short-circuits its roll without consuming a draw.

The power a track has earned is **never stored as a stat**. `ProgressionEffectContributor` — a third registrant on the same `IEffectContributor` port `EquipmentEffectContributor` and `AbilityEffectContributor` already use — is pulled on read by `IStatSystem.Get` and returns `PowerPerImprovement × improvementCount(track)` for that score, fresh every call (INV-24). This is the whole mechanism: nothing precomputes or caches a "current effective score with progression baked in."

## Systems

| System | Role |
|---|---|
| [`progression-system.md`](progression-system.md) | `ProgressionComponent` (per-track XP/improvement counts), `IProgressionSystem` (award/improve/read), `ProgressionEffectContributor` (the contribute-on-read fold), `ProgressionConstants` (the tuning knobs) |
| [`ascension-system.md`](ascension-system.md) | `AscensionComponent` (tier scalar + unlock records), `IAscensionSystem` (tier read/ascend gate), `AscensionEffectContributor` (the additive-baseline contribute-on-read fold), `AscensionConstants` (tier baseline step, unlock table); mob `Tier`/`Band` content tags |
| [`power-budget-system.md`](power-budget-system.md) | `IPowerBudgetSystem`/`PowerBudgetSystem` (core-tier oracle — `Estimate`/`Classify`→`PowerBand`/`TargetRange`/`BandAnchor`), `PowerBudgetTunables` (injected plain-data weight table, band span, `BandsPerTier`, reference-build + tier constants, composed from the balance-standards registry), `IBalanceStandardsStore`/`IBalanceStandardsRegistry` (sim-1 YAML standards document), `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem` (shared projection seams), `IBalanceAuditSystem` (bulk drift sweep); `power`/`powerband` inspectors; the Standards page; item `Tier`/`Band` content tags |

## Surfaces

- **Commands** — `progress` (player) — lists every score track's improvement count, cumulative XP, and XP-to-next-threshold, then a separate ability-rank block. `skills` / `spells` / `abilities` (player) — each ability line ends with rank and XP-to-next. `config [<name> [on|off]]` (player) — list or toggle the narration preferences. `setmob xpscale <blueprintId> <value>` (admin) — author a mob's per-kill XP scale. `ascend [characterName]` (admin) — ascends the target one tier. `setmob tier|band <blueprintId> <value>` / `setitem tier|band <blueprintId> <value>` (admin) — author a mob's/item's Tier×Band tags. `power [target]` / `powerband [tier]` (admin) — inspect computed power + `(Tier, Band)`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `ExperienceAwardedEvent` (a track gained XP — frequent, thin; `Track` is a `ProgressionTrack`, score or ability), `TrackImprovedEvent` (a track crossed a threshold — the discrete milestone other slices/systems subscribe to), `PreferenceChangedEvent` (a player toggled a setting), `AscendedEvent` (tier changed — milestone), `PlayerAscendedByAdminEvent` (admin audit). The power-budget oracle, its inspectors, and the drift audit publish no events — pure read tools. See [`../../architecture/03-events.md`](../../architecture/03-events.md).
- **Components** — `ProgressionComponent` (keyed by `ProgressionTrack`), `AscensionComponent`, `PlayerConfigurationComponent` (all `[Persistent]`, player-only — never attached to world content); `MobDataComponent.Tier`/`.Band`/`.XpScale` / `ItemDataComponent.Tier`/`.Band` (world content, never snapshotted in practice). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — Progression's authored surface is the granular XP scales: per-mob `XpScale` (`setmob xpscale`, YAML `xpScale:`, the Blazor `MobEditor` field) and per-ability `XpScale`/`XpAttributeTrack` (compiled `AbilityRegistry` rows, inspected via `defs ability <id>` — abilities have no YAML pipeline yet, and building one is a slice of its own; see the backlog). The advancement rule table and `GlobalXpScalar` are likewise compiled (Category 3), because their values are pinned by CI simulation goldens. The rest of progression is runtime-accrued state on persistent entities, not authored world content. Ascension ships mob Tier×Band authoring: `setmob tier`/`band`, YAML `tier:`/`band:` fields, and the Blazor `MobEditor` tier/band fields. The power-budget slice mirrors this for items (`setitem tier`/`band`, YAML `tier:`/`band:` fields, Blazor `ItemEditor` fields) and adds the computed power/`(Tier, Band)` readout to both editors — the primary designer observability surface — plus the bulk drift-audit report on the Blazor Integrity page. An admin `setprogress`-style hand-set command is deliberately deferred — no balance/testing task needs it yet — tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Flows

- [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) — the rule-table award → chance roll → threshold-improve → narrate → contribute-on-read path.
- [flow-32 — Ascension journey](../../architecture/flows/flow-32-ascension.md) — the tier-up → unlock-record → baseline-fold path.
- [flow-20 — Death & respawn journey](../../architecture/flows/flow-20-mob-death-respawn.md) — the kill trigger (`MobDiedEvent`), alongside `CurrencyLootHandler` and `SpawnSystem`.
- [flow-24 — Ability activation](../../architecture/flows/flow-24-ability-activation.md) — the ability-use trigger (`AbilityActivatedEvent`).

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
