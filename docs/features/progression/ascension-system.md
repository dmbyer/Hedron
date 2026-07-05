# Ascension system

> Character-wide Tier scalar, the additive power-baseline contribute-on-read fold, admin tier-up gate, and the mob tier-band content tag. **Authoring checkpoint:** slice prog-2. Living document.

## What it is / does

Domain-tier. `AscensionSystem` owns the tier state machine: `GetTier` (safe-default read), `CanAscend` (the eligibility gate), `TryAscend` (the mutation + unlock-record). `AscensionEffectContributor` is the read-side adapter that turns "current tier" into "power" for any `IStatSystem.Get` caller. Neither touches the event bus (INV-5) — `AscendCommand` is the Initiator that publishes results.

## How it works

**Tier is a single character-wide scalar, `int 0–6`, on `AscensionComponent`** (not per-attribute — evaluated and dropped, see gameplay-model R1). `GetTier` returns 0 for an entity with no component and creates nothing.

**Additive baseline, no reset.** Tier confers a flat additive power baseline — `AscensionConstants.TierBaselineStep × tier` — across every score in `AscensionConstants.TrackedScores`. Ascending never rescales or resets `ProgressionComponent`'s XP/improvements; a maxed lower tier keeps all its per-track progression power, and ascending layers the baseline *on top*. This is what makes the deadly→medium overlap semantics fall out with no extra machinery: a Tier-1 character lacks the Tier-2 baseline step, so a Tier-2-tuned mob out-scales them (deadly); ascending grants the step and the same mob normalizes to medium.

**Tier-up gate: admin now, Objective later.** `CanAscend` returns a structured `AscendEligibility` (`Eligible`, or a typed reason such as `AtMaxTier`) — the seam a future player-facing Ascension-Objective gate will call (`IObjectiveSystem` is unbuilt). The only trigger in this slice is the admin `ascend` command (plain verb, structural privilege gate). `TryAscend` creates `AscensionComponent` lazily, increments and clamps `Tier` to `[0, MaxTier]`, and records the new tier's configured unlock ids onto `GrantedUnlocks` idempotently — `AscensionConstants.UnlocksForTier` is an **empty** table in this slice, so nothing is recorded yet; the durable record shape (and the `AscendedEvent` a future grant handler will consume) ships ahead of any unlock content.

### The tier baseline rides the existing `IEffectContributor` port — not a new scaling system

`AscensionEffectContributor` is a **fourth** registrant on the core-owned `IEffectContributor` port, alongside `EquipmentEffectContributor`, `AbilityEffectContributor`, and `ProgressionEffectContributor`. `GetModifiers` returns `TierBaselineStep × GetTier(entityId)` for a tracked score, pulled fresh every call — never stored, never cached (INV-24). A dedicated Spine-D `IScalingSystem` is deferred; this reuses the exact pattern `ProgressionEffectContributor` proved, and a later scaling system (or the slice-3 `IPowerBudgetSystem`) can subsume the baseline computation without changing any caller.

### DI-cycle guard: a second confirming precedent

`AscensionSystem`'s backing input is **raw `AscensionComponent.Tier`**, read via `EntityService` — never `IStatSystem`/`IEffectSystem`. This mirrors the guard `ProgressionSystem` observes (see [`progression-system.md`](progression-system.md#anti-grind-proxy-reads-raw-attributes)): a contributor's backing system may consume raw component data, but never a computed value from the seam it feeds, or it recreates the `IStatSystem` → `IEffectSystem` → contributors → backing system → `IStatSystem` cycle. Trivially satisfied here (the baseline is a pure function of tier) but stated so nobody wires `IAscensionSystem` → `IStatSystem`.

### Mob tier-band tag — a lightweight content tag, not a power oracle

`MobTemplate.TierBand` (`int 0–6`, `0` = unbanded) is authored via `setmob band <blueprintId> <tier>` (dual-writing the live `MobDataComponent.TierBand` and the template, mirroring `SetMobProtection`) and the Blazor `MobEditor`. Mechanical threat is **emergent from the additive baseline** — a Tier-N mob is simply tuned to Tier-N baseline stats; there is no separate threat multiplier, and bands **overlap** (a maxed lower tier can reach into the next band before formally ascending). `MobDataComponent` is `[Persistent]`, but mob entities never carry `PersistentEntity` (world content), so the band never reaches a snapshot — its durable form is the YAML template, re-applied on each spawn. Item bands are deferred to slice prog-3, alongside the `IPowerBudgetSystem` oracle that consumes them.

## Interface

- [`IAscensionSystem.cs`](../../../Core/Modules/Ascension/Systems/IAscensionSystem.cs) — `GetTier`, `CanAscend`, `TryAscend`, `GetGrantedUnlocks`. Returns result records (`AscendEligibility`, `AscendResult`); publishes nothing.
- [`AscensionEffectContributor.cs`](../../../Core/Modules/Ascension/AscensionEffectContributor.cs) — the `IEffectContributor` registrant.
- [`AscensionConstants.cs`](../../../Core/Modules/Ascension/AscensionConstants.cs) — `MaxTier`, `TierBaselineStep`, `TrackedScores`, `UnlocksForTier` (empty in this slice).
- [`IMobBuilderSystem.SetMobBand`](../../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — the mob tier-band dual-write.

## Considerations

- **Persistence:** `AscensionComponent` is `[Persistent]`, attached lazily to a persistent (player) entity on first successful `TryAscend` — never to world content. The admin `ascend` command performs exactly one `SaveEntityAsync` (case-b admin boundary save, INV-22), paired with the `PlayerAscendedByAdminEvent` audit event.
- **Determinism (INV-26):** no chance/time-dependent logic — the baseline is `step × tier` and ascend is a clamped increment, both pure functions of component state. No `IRandom` seam needed.
- **Registration:** `AscensionModule.AddAscensionModule` registers `IAscensionSystem`, the `IEffectContributor`, `AscensionNarrationHandler`, and the `ascend` command. Called from `Server/CompositionRoot.Register` (not `Program.cs`) — the same reason `ProgressionModule` is: the Blazor content-authoring host's `StatSystem` needs the contributor too.
- **Acknowledged debt:** the unlock-*grant execution* seam (`GrantFlag`/`GrantAbility` are unimplemented `EffectKind` values) and concrete unlock content are deferred; the real player-facing Ascension-Objective gate (`IObjectiveSystem`) is deferred; item tier-bands are deferred to slice prog-3. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Extensibility

The character-wide Tier baseline is designed as a second contribution on the same contributor port `ProgressionEffectContributor` uses — see the [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md) skill, which documents both contributors and the shared DI-cycle guardrail.

## Related

- Flow: [flow-32 — Ascension journey](../../architecture/flows/flow-32-ascension.md); [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) for the contribute-on-read leg this slice adds a second contributor to.
- Reference rows: [`systems.md`](../../reference/systems.md), [`components.md`](../../reference/components.md), [`handlers.md`](../../reference/handlers.md), [`commands.md`](../../reference/commands.md).
- [`progression-system.md`](progression-system.md) — the DI-cycle guard precedent this system confirms a second time.
- [`stat-system.md`](../character-stats/stat-system.md) · [`effect-system.md`](../effects/effect-system.md) — the read seam this contributor folds into.
- [`../../design/gameplay-model.md`](../../design/gameplay-model.md) — §6 R1 (Ascension: vertical scalar 0–6).
- [`../mobs/mob-system.md`](../mobs/mob-system.md) — the builder/template/writer authoring pattern the band tag mirrors (`SetMobProtection`).
- [`../../roadmap/completed/ascension.md`](../../roadmap/completed/ascension.md) — as-built history and design decisions.
