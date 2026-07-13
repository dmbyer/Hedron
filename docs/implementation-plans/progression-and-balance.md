# Progression & Balance — Program Architecture Brief

**Status:** planned
**Actors:** Player (earns progression through use) · Administrator/Designer (authors + inspects balance) · System (heartbeat/combat award timing) · Simulator (offline batch validation)
**Module:** `Core/Modules/Progression/` (new); `Core/Modules/Ascension/` (new, or folded into Progression); `Core/Systems/` (`IPowerBudgetSystem`); extends `Core/Modules/Attributes/` + `IStatSystem`; new `Hedron.Sim` project (offline)

---

## Description

Experience-driven progression (gameplay-model [**Spine E**](../design/gameplay-model.md)) plus the balance-observability tooling that keeps it tunable as content expands. Every advanceable score (attributes, abilities, aspect attunements, vitals) carries a per-track experience that **accrues through use** (and injectable jumps — books, trainers); crossing a **growing improvement threshold** grants a **linear** power step, so power is near-unlimited but the *rate* of gain slows (the curve lives in the threshold, never in the power). A character-wide **Tier** scalar (Ascension, [R1](../design/gameplay-model.md#6-resolved-decisions)) confers an additive power baseline step that re-contextualizes tier-banded content. On top of progression sit two tooling deliverables: a shared **power-budget estimator** (`IPowerBudgetSystem`) that projects any item/mob/loadout onto the tier power bands ("build a sword, get a read"), and an offline **simulation harness** (`Hedron.Sim`) that runs large parameter-swept combat batches and validates real outcomes against the bands to drive tuning. The whole thing is a **program of ~5 slices**, front-loaded so the two shared spines (progression math, power model) land and are reviewed before the incremental work.

The substrate this writes to is **already built** — `ScoreId`/`IStatRegistry`, the four attributes via `AttributeSystem`, pools, and the compute-on-read `IStatSystem` (slices 9-d/9-e/11). Progression itself is **greenfield**: there is no `Progression`/`Ascension` module and no `IdentityComponent.Tier` in code today (the gameplay-model references that field as a "seed," but it does not yet exist — this program builds it; the doc note is corrected under INV-15).

---

## Program shape — five slices

This doc is the **architecture seed for the program**: it holds the durable cross-slice seam decisions and seeds **slice 1**. Each slice gets its own transient plan via `/new-plan` and runs the normal per-slice loop (spec gate → implement → code gate). On ship, the program-level design migrates to `docs/design/` + a new `docs/features/progression/` per R7 / [INV-28](../architecture/checklist.md); this file disintegrates like any plan.

| # | Slice | Lands | Depends on |
|---|---|---|---|
| **1** | **Progression substrate — ✅ done** | `ProgressionComponent` (per-track XP), `IProgressionSystem` (use-driven `AwardExperience`/`TryImprove`), `ProgressionConstants` (linear step + growing threshold), `ProgressionEffectContributor` folded into `IStatSystem` via the existing `IEffectContributor` port (reused, not a parallel `IProgressionContributor` — see [`../roadmap/completed/progression-substrate.md`](../roadmap/completed/progression-substrate.md)), XP-award off combat, `progress` inspector, `ExperienceAwardedEvent`/`TrackImprovedEvent`. Durable design now lives in [`../features/progression/progression.md`](../features/progression/progression.md); as-built history in [`../roadmap/completed/progression-substrate.md`](../roadmap/completed/progression-substrate.md). | substrate (built) |
| **2** | **Ascension (character-wide tier) — ✅ done** | Tier scalar + additive power baseline (rides the same contribute-on-read seam), tier-up gate, mobs-only content band tagging, overlap semantics, unlock-record seam (grant-execution + content deferred), `AscendedEvent`. Durable design now lives in [`../features/progression/ascension-system.md`](../features/progression/ascension-system.md); as-built history in [`../roadmap/completed/ascension.md`](../roadmap/completed/ascension.md). | 1 |
| **3** | **Power model + balance inspector — ✅ done** | `IPowerBudgetSystem` (core-tier shared oracle, zero constructor dependencies), tier power bands derived from a mirrored reference build, in-game `power`/`powerband` inspector + Blazor editor readout, item tier-band tag, anti-grind-proxy rewire. Durable design now lives in [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md); as-built history in [`../roadmap/completed/power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md). **Superseded by `3b` below — see the revision note.** | 1, 2 |
| **3b** | **Power model revision — Tier × Band + calibration + audit tooling — ✅ done** | Two-axis `Classify` output (`Tier 0–6` × `Band 1–3` within each tier — a D&D-Challenge-Rating-style model, not a finer leveling system); a `(Tier, Band) → target power range` inverse query for forward design + future procedural generation; `TierBand` became a `Tier`+`Band` pair on `MobDataComponent`/`ItemDataComponent`/templates/YAML/`setmob`/`setitem`/both Blazor editors (clean break, no migration); band-count-tolerance drift detection (soft — an upgraded editor mismatch flag, plus a new `IBalanceAuditSystem` bulk audit report extending the existing Integrity sweep, never a build-blocking gate) that doubles as free "how much content exists at power level X" reporting; recalibrated `PowerBudgetConstants` against real design targets; and a written-down extensibility principle (`docs/design/power-model.md`) for future power inputs so the oracle stays snapshot-only and never gains a domain import — pulled forward from slice 5's INV-20 scope. Durable design now lives in [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) and [`../design/power-model.md`](../design/power-model.md); as-built history in [`../roadmap/completed/power-model-revision.md`](../roadmap/completed/power-model-revision.md). | 3 |
| **4** | **Simulation harness — expanded into its own sub-program** | Superseded by the [`balance-simulator`](balance-simulator.md) program seed (advisor intake 2026-07-13): the sim engine moved from a dedicated `Hedron.Sim` project to a Core module drivable by the Blazor editor, plus a data-driven balance-standards registry, editor integration, progression-rate scenarios, and template conformance tooling — five sub-slices (`sim-1`–`sim-5`). The combatant-policy seam and promoted-CI-invariant decisions carry over unchanged. | 3b (done) |
| **5** | **Agentic + doc layer** | Balance catalog (`design/balance.md`), `balance-tuning` + `run-simulation` skills, remaining INV-20 updates to advisor/planner/reviewer for the sim surfaces (the power-model extensibility piece moved to `3b`) | threads 1–4 |

Slices 1 and 3 establish the two shared spines every later balance/expansion touch reads; getting those seams right is the whole game. Once they exist, expanding items/skills/mobs becomes "add data → it shows in the inspector and the sim automatically." **Update (2026-07-06):** a post-merge design conversation found slice 3's one-axis Band model undershot the actual requirement (a two-axis Tier × Band CR-style system) — see `3b` above and Open questions 7–12. Slices 1 and 2 are unaffected; only slice 3's Band/calibration surface needs revision before slice 4 builds on it.

> **Agent tooling note.** The `edit-progression-system` skill (how to add/tune XP sources, tracks, and curves — the "Advancement triggers" Design note below) lands in **slice 1** with the pattern it documents, per INV-20 — *not* slice 5. Slice 5's agentic layer adds the *balance* tooling (`balance-tuning` / `run-simulation` skills, the balance catalog) and the remaining INV-20 refresh of the advisor/planner/reviewer for the sim surfaces. The piece of that refresh covering the power-model's own extensibility (how future power inputs — abilities, effects, speed, defense — fold into the oracle without it gaining a domain dependency) is **pulled forward into slice `3b`**, since the revision itself needs to establish and follow that principle, not wait for slice 5.

---

## Design notes

> Durable seam rationale — the non-obvious "why here" a cold-start session must not re-derive or reverse. Survives disintegration into `docs/design/` + `docs/features/progression/`.

### Progression is Spine E writing to the existing substrate — not a new stack

`ScoreId`, `IStatRegistry`, `AttributeSystem`, pools, and compute-on-read `IStatSystem` are built and are the write target. Progression is a **domain** system that feeds power *into* the stat pipeline; it does not re-implement stat math.

### Contribute-on-read is the central seam (INV-24), not base mutation

`ProgressionComponent` stores durable state — per-track XP (and the character Tier scalar). The **power** a track yields is **pulled on read** by `IStatSystem` through a new `IProgressionContributor`, exactly the [INV-24](../architecture/checklist.md) contributor-port pattern already proven by `IEffectContributor` and `EquipmentEffectContributor`. Rationale:

- **Compute-on-read** — no base rewrite, so no "did I recompute when it changed?" bug family; a tier change is a pure state edit with nothing to un-bake.
- **One power function, reused** — the same computed contribution feeds the power-budget tool (slice 3) and the sim oracle (slice 4) for free.

**Direct base-mutation stays available, coexisting.** A discrete *permanent* growth action (consume a rare material → +1 base attribute forever) mutates the base component once and leaves nothing behind — Spine E's model, kept on the table for edge cases you named. The line: **continuous progression = contribute-on-read; discrete permanent growth = direct mutation.** They are different inputs to the same computed score.

### Use-driven accrual, not a purchase

`AwardExperience(entity, trackId, amount, source)` is called *on use* (and by injectable sources — books, trainers). `TryImprove` fires automatically when a track's cumulative XP crosses the next threshold; each improvement grants a **linear** power increment, and the **threshold to the next** grows — that growing gap *is* the slowing rate. There is no spend/buy verb and no mechanical "cost." The linear increment and the threshold curve are the two tuning knobs, co-located in `ProgressionConstants` ([Category 3 — System Math/Balance](../architecture/05-configuration.md), promotable to data per OD-2 only when designer-without-recompile is a real need).

### Advancement triggers — how XP sources are configured (code-thin, data-tunable, table at scale)

An *advancement trigger* is the action that feeds a track. Configuring triggers is not a new burden — it is the existing event→handler idiom the codebase already uses everywhere, with a documented promotion path to data. Three layers, matching the [config strategy](../architecture/05-configuration.md) and the layering:

1. **Mechanism = a handler on a game event (code, few).** Every XP source is a game action that publishes a past-tense event — `MobDiedEvent` (kill, slice 1), and later `AbilityUsedEvent` (cast/skill → `Mind`/`Attunement`/`Body`), a damage-taken event (→ `HpMax`), item `read` (book → flat XP jump), `CraftCompletedEvent`, `ObjectiveCompletedEvent`. A thin handler subscribes and calls `IProgressionSystem.AwardExperience(entity, track, amount, XpSource)`. Adding a *kind* of trigger = subscribe a handler to that event (INV-1 orchestration; `CurrencyLootHandler` on `MobDiedEvent` is the precedent). The `XpSource` enum is the stable key for every source.
2. **Tuning = data (many), in `ProgressionConstants` now → YAML later.** Which track a source feeds, the base amount, the anti-grind curve, and the improvement-threshold curve are named constants ([Category 3](../architecture/05-configuration.md)), changed in the same commit as the source. When recompile-free iteration is needed (heavy sim-driven tuning, or per-mob/per-area overrides), promote to YAML content (Category 2, OD-2) via the editor.
3. **Generalization = an advancement-rule table (Spine F), at the ≥3-source threshold (INV-19).** Once 3+ sources exist, promote the N bespoke handlers to one thin advancement handler reading a **rule registry**: `XpSource/event → (track(s), amount formula, anti-grind, conditions)`. Hardcoded rows first, YAML rows at OD-2. There, "configuring a trigger" = adding/editing a row, not writing a handler. Slice 1 ships one source, so a bespoke handler is correct (restraint); the `XpSource` key makes the promotion additive, not a rewrite.

**Transparency (the front-loaded pattern a sub-agent follows).** To add an XP source in any later slice: publish/consume the event → subscribe a handler → `AwardExperience(track, amount from constants)` → add an `XpSource` value; at the 3rd source, promote to the rule table. The effective config stays observable end-to-end: `progress` shows tracks moving (slice 1), the balance catalog documents every trigger's math (slice 5), and the sim sweeps advancement rates (slice 4).

### Character-wide Tier (Ascension, R1) with an additive baseline — per-attribute tiers dropped

Tier is a **single character scalar**, not per-property (per-attribute tiers were evaluated and dropped: clunky overhead + clunky UX, minimal value). It confers an **additive power baseline step** that rides the same contribute-on-read seam (a tier term in the progression contribution). This makes your overlap/threat semantics fall out cleanly:

- A maxed **Tier-1** has high *track* power but lacks the *baseline* step → a Tier-2 trash mob (tuned to the Tier-2 baseline) out-scales them and is **deeply threatening**.
- **Ascending** grants the baseline step → the same trash normalizes to a **medium** challenge (the fresh "initial climb" feel at the new tier).
- A Tier-2 character in Tier-1 content keeps the step → **0 issues** (comfortably over-scaled).

Content is tagged by tier band; **bands overlap** so a maxed lower-tier can reach up into the next band (dangerously) before formally ascending — the "pinnacle activity → jump into higher difficulty" hook. **Simplification win:** under the additive-baseline model, the XP-reset/re-base-on-ascend mechanic the *per-attribute* design needed is **probably unnecessary** — the baseline lift does that work (recorded as an open question, not a requirement). "Specialization through tiers" = ascension **grants unlocks** (aspects/abilities/flags via the effect grant seam); the *selection* mechanism is deferred.

### `IPowerBudgetSystem` — one shared power oracle, three consumers

A **core-tier** estimator: given an entity or a content definition (item/mob/loadout), compute a power scalar from its scores + effects + tier baseline and classify it into a tier band. Consumed on day one by (a) the in-game inspector, (b) the Blazor editor readout, and (c) the sim's expected-outcome oracle — three consumers, so it clears the [INV-19](../architecture/checklist.md) "build the framework now" bar and must be **one** function or it drifts three ways. It is a **heuristic, tunable, transparent budget** (your "loosely bounded"), not a precise truth; it reuses the values `IStatSystem` already exposes and never re-derives stat math. Core-tier means no domain-system dependency ([INV-2](../architecture/checklist.md)) — validate during slice 3 that its inputs stay generic.

### `Hedron.Sim` — offline batch simulation, cheap *because of* the architecture

A **new project referencing `Core`** (not in the game runtime, not in `Hedron.Tests`). Systems are already near-pure functions driven by injected `IRandom` and synthetic ticks — the same property that makes Tier-1 tests trivial — so a batch simulator is "run the [Tier-3 flow harness](../architecture/07-testing.md) 10,000× with a parameter sweep and assert on the *distribution*," reusing `EntityBuilder`/`FakeRandom`/`RecordingEventBus`/synthetic ticks. It sweeps build × mob × tier, emits statistical reports (win rate, time-to-kill, damage bell curves for micro + macro tuning), and uses `IPowerBudgetSystem` as its expected-vs-actual oracle. A **thin subset** of derived invariants (e.g. "a Tier-N baseline PC beats a Tier-N baseline mob within band X") is promoted into `Hedron.Tests` as CI regression gates — keeping heavy sweeps out of the test suite while still catching balance regressions.

**Combatant-policy seam (shaped for mob AI).** The sim drives each actor through an `ISimCombatantPolicy` ("what does this actor do this round") with simple built-ins now — round-robin, cooldown-first — so behavior is not hardcoded. When mob AI lands, a future adapter binds the real `IAISystem` behind the same seam. Out of scope now; the seam keeps it additive.

### Per-slice functional-validation gate

Beyond the [INV-25](../architecture/checklist.md) xUnit tests, each slice ships a **lightweight "see it work" hook** — an inspector command output or a smoke-sim run — so functional behavior is observable while content is still thin (the peace-of-mind check you asked for). Slice 1: `progress` shows a track gaining XP and improving. Slice 2: ascend a fixture, watch a banded mob shift deadly→medium. Slice 3: `power <item>` returns a band + a golden-number test. Slice 4: the sim *is* the gate (a smoke run producing a sane curve).

---

## Architecture brief

> In-flight forward analysis; trimmed on ship. Feeds the planner's Design notes + ground-rule-9 audit.

### Placement & layers

| Piece | Layer | Home |
|---|---|---|
| `ProgressionComponent` (per-track XP; Tier scalar) | Component (data) | `Core/Modules/Progression/Components/` — `[Persistent]` |
| `IProgressionSystem` (`AwardExperience`/`TryImprove`/read) | Domain | `Core/Modules/Progression/Systems/` |
| `IProgressionContributor` (folds progression power on read) | Domain adapter of a core-owned port | registered for `IStatSystem` (INV-24) |
| `AscensionComponent` / `IAscensionSystem` (tier state + ascend gate) | Component + Domain | `Core/Modules/Ascension/` (or folded into Progression) |
| `IPowerBudgetSystem` (generic budget math + banding) | **Core** | `Core/Systems/` — no domain deps (INV-2) |
| `progress` / `power` / `powerband` inspectors | Initiator (command) | feature `Commands/` |
| XP-award + ascend-gate handlers | Handler (orchestrate) | subscribe to combat/objective events |
| `Hedron.Sim` (runner, policies, reporting) | Offline tool | new project → `Core` |

### Family disposition

| Concern | Disposition |
|---|---|
| Progression tracks keyed by `ScoreId`/`TrackId` | **Build now** — general from day one |
| Contribute-on-read progression power (INV-24) | **Build now** |
| Character Tier additive baseline | **Build now** (slice 2) |
| `IPowerBudgetSystem` shared oracle | **Build now** (3 consumers → INV-19) |
| Direct base-mutation (permanent growth) | **Shape for later** — Spine E seam exists; wire when a discrete-permanent-growth consumer appears |
| XP-reset / rescale-on-ascend | **Defer** — additive baseline likely obviates; open question, not a requirement |
| Specialization-on-ascend *selection* | **Defer** — grant seam now, selection UX later |
| Sim → real `IAISystem` adapter | **Shape for later** — policy seam + built-ins now |
| Per-attribute tiers | **Dropped** (evaluated, low value) |
| YAML-authored curve/tuning | **Defer** — constants now; promote per OD-2 |
| Balance-reviewer agent | **Defer** — stretch candidate |

### Observers, contributors & event granularity

- **`ExperienceAwardedEvent`** (a track gained XP) — thin, frequent; for prompt/telemetry.
- **`TrackImprovedEvent`** (a track crossed a threshold → a power step) — the **discrete milestone** others subscribe to (prompt, future achievements, sim labeling).
- **`AscendedEvent`** (tier changed) — milestone; drives unlock grants, band re-tag, output.
- **Contributor:** `IProgressionContributor` pulled on read by `IStatSystem` — never a materialized/cached field ([INV-24](../architecture/checklist.md)).
- **Granularity call:** award is continuous → keep it thin; **improvement and ascend are the discrete facts** worth broadcasting. Do **not** publish a fat "progression changed" per use.

### Ordering & timing

`AwardExperience` → threshold check → `TryImprove` resolve **inside** the domain system (returns a result record); the Initiator/handler publishes `ExperienceAwardedEvent` and, *conditionally*, `TrackImprovedEvent` (INV-5/INV-8 — the conditional publish is a handler/initiator concern, not the system's). No hard intra-tick ordering constraint beyond "award after the kill resolves." Any award-chance or improvement RNG resolves through `IRandom` ([INV-26](../architecture/checklist.md)).

### Invariants in tension

- **[INV-24](../architecture/checklist.md)** — progression + tier baseline enter `IStatSystem` through the contributor port, pulled on read, never materialized. *Central.*
- **[INV-2](../architecture/checklist.md)** — `IPowerBudgetSystem` is core-tier; it must read computed values/definitions, not call domain systems. Validate inputs stay generic (slice 3).
- **[INV-19](../architecture/checklist.md)** — power oracle has ≥3 consumers → framework lands with it, not hand-rolled per caller.
- **[INV-25/26](../architecture/checklist.md)** — each slice ships its Test plan; award/improve RNG behind `IRandom`. Sim promotes a thin CI-invariant subset.
- **[INV-15](../architecture/checklist.md)** — correct the gameplay-model substrate note ("primary attribute grows via progression tracks," implying base mutation) to contribute-on-read, in the slice that lands it.
- **[INV-20](../architecture/checklist.md)** — update `add-domain-system`/`add-core-system`/`add-component`/`add-command`/`add-tests`/`manage-docs` and the advisor/planner/reviewer for the power-model + sim surfaces.
- **[INV-28](../architecture/checklist.md)** — this program doc disintegrates into `docs/design/` + `docs/features/progression/` on ship.

### Resolved decisions

| # | Decision |
|---|---|
| Mechanic | Use-driven accrual (not purchase); linear power step; growing threshold = the slowing rate; books/trainers inject XP jumps. |
| Fork 1 — Tier | **Character-wide scalar** (Ascension, R1). Per-attribute tiers dropped. Additive baseline model. |
| Fork 2 — Power write | **Contribute-on-read** default; **direct base-mutation kept available** for discrete permanent edits (coexist). |
| Fork 3 — Sim home | **Dedicated `Hedron.Sim`** project + thin promoted CI invariants (no test-suite bloat; enables bell-curve analysis). |
| Fork 4 — Sequencing | **5-slice program**, front-loaded spines; **lightweight functional-validation gate per slice**. Balance-reviewer agent = stretch. |
| Sim/AI | Combatant-policy seam with simple built-ins now; real `IAISystem` adapter shaped for later. |

---

## Open questions

> Load-bearing for later slice specs; none block slice 1.

1. **Additive tier baseline vs rescale-on-ascend** (slice 2) — ✅ **RESOLVED (2026-07-05): additive baseline, no reset** (rescale/XP-reset dropped). See [`completed/ascension.md`](../roadmap/completed/ascension.md) and [`features/progression/ascension-system.md`](../features/progression/ascension-system.md).
2. **Power-budget formula + band definition** (slice 3) — ✅ **RESOLVED (2026-07-06): weighted sum over `PowerBudgetConstants.Weights`; bands derived from `Estimate(ReferenceBaseScores, tier) − BandSpan`**, a constant mirroring `CharacterDefaultsOptions`. Heuristic and tunable — the prog-4 sim is the expected retuning trigger. See [`completed/power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md) and [`features/progression/power-budget-system.md`](../features/progression/power-budget-system.md).
3. **Specialization-on-ascend selection mechanism** (slice 2+) — ✅ **RESOLVED (2026-07-05): deferred.** prog-2 ships the unlock-*record* seam only (empty table; grant-execution seam + selection UX deferred). See [`completed/ascension.md`](../roadmap/completed/ascension.md) and [`features/progression/ascension-system.md`](../features/progression/ascension-system.md).
4. **XP-award sourcing + anti-grind** (slice 1) — which actions award which tracks, and diminishing-returns guards on trivial targets.
5. **Tier-baseline home** (slice 2) — ✅ **RESOLVED (2026-07-05): rides the existing `IEffectContributor` port** (4th registrant, `AscensionEffectContributor`); no new Scaling/Spine-D seam. See [`completed/ascension.md`](../roadmap/completed/ascension.md) and [`features/progression/ascension-system.md`](../features/progression/ascension-system.md).
6. **`IPowerBudgetSystem` tier** (slice 3) — ✅ **RESOLVED (2026-07-06): stays core-tier-generic**, confirmed by the snapshot-input design — every consumer expresses its input as a plain `ScoreId → int` map. Code review found one implementation slip (the oracle read `AscensionConstants` directly instead of a mirrored constant) and it was fixed to match this resolution, not to move the oracle to domain. See [`completed/power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md).

> **Slice `3b` open questions — added 2026-07-06, from a post-merge design conversation.** None of these existed when slice 3 was planned/reviewed; they surfaced from a manual spot-check (`power` on a near-blank character read Tier 4) plus a clarifying discussion of what Tier/Band are actually supposed to mean. They are the seed for `/advise`/`/new-plan` on slice `3b`.

7. **Band is a second axis inside Tier, not a synonym for it** (slice 3b) — ✅ **RESOLVED & SHIPPED: Tier (0–6, character-wide, Ascension-gated) stays the coarse "chunk of levels" axis; Band (1–3, within each tier) is a finer subdivision — low/mid/high — "enough progression to feel meaningful, but not introducing an entire leveling system."** `Classify` now returns `PowerBand(Tier, Band)` with ~21 anchors (3 sub-anchors per tier gap). See [`completed/power-model-revision.md`](../roadmap/completed/power-model-revision.md) and [`features/progression/power-budget-system.md`](../features/progression/power-budget-system.md).
8. **Band tolerance metric** (slice 3b) — ✅ **RESOLVED & SHIPPED: band-count, not raw-power-percentage** (`BalanceAuditConstants.BandDriftTolerance` over `GlobalBandIndex`). "More than N bands off" is simpler and stays self-consistent with the Tier×Band vocabulary the rest of the design is in.
9. **What does authoring a band actually unlock, and is it worth keeping as data** (slice 3b) — ✅ **RESOLVED & SHIPPED: kept persisted (Option A), made enforceable via two soft layers.** (a) the per-item/mob editor mismatch flag, upgraded from exact-match to band-count tolerance; (b) the new `IBalanceAuditSystem` bulk sweep feeding the Blazor Integrity page, listing all content past tolerance and bucketing counts by `(Tier, Band)`. Neither is a hard blocker — variance within a band is expected, not a bug. See [`completed/power-model-revision.md`](../roadmap/completed/power-model-revision.md).
10. **Calibration of the actual numbers** (slice 3b) — ✅ **RESOLVED & SHIPPED as a disposition: oracle estimation only.** `3b` recalibrated `PowerBudgetConstants` (`Weights`/`BandSpan`/`BandsPerTier`) for real headroom + deliberate 3-band spacing but left `AscensionConstants` (real gameplay tier power) untouched — the mirror stays in sync; concrete numbers are hand-derived (documented still-heuristic) and sim-validation is `prog-4`'s job. See [`completed/power-model-revision.md`](../roadmap/completed/power-model-revision.md).
11. **Power-model extensibility for future inputs** (slice 3b) — ✅ **RESOLVED & SHIPPED: the oracle stays snapshot-only; it never gains a domain import to learn about abilities/effects/speed/defense directly.** Future power sources fold in one of two ways: (a) a genuinely stat-like quantity becomes a new `ScoreId` vocabulary member callers can weight; or (b) a richer source computes its **own** estimated power contribution and a caller sums it into the snapshot before calling `Estimate` — mirroring the existing `IEffectContributor` port precedent. Written down as a named principle in [`../design/power-model.md`](../design/power-model.md).
12. **Pulling prog-5 scope forward** (slice 3b) — ✅ **RESOLVED & SHIPPED: partially.** The INV-20 doc/skill piece that is a *direct dependency* of item 11 above (teaching `add-domain-system`/`add-core-system`/the advisor to ask "does this affect power, and how does its contribution enter the snapshot") shipped in `3b`. The balance catalog (`design/balance.md`) and the `balance-tuning`/`run-simulation` skills stay at slice 5, since `run-simulation` has no meaning before `Hedron.Sim` (slice 4) exists.

---

## Slice 1 scope — what `/new-plan` extends first

- `ProgressionComponent` — per-track XP map (`[Persistent]`); the Tier scalar may land here or in slice 2's Ascension component (planner decides).
- `IProgressionSystem` — `AwardExperience(entity, TrackId, amount, XpSource)`, `TryImprove(entity, TrackId)`, read accessors; math in `ProgressionConstants` (linear increment + growing threshold).
- `IProgressionContributor` — folded by `IStatSystem.Get` (INV-24).
- XP-award handler off combat (`MobDiedEvent` / `DamageDealtEvent`).
- Events: `ExperienceAwardedEvent`, `TrackImprovedEvent`.
- Inspector: `progress` command (and/or `score` extension) — the functional-validation hook.
- Test plan: T1 (threshold + linear-power math, contributor fold), T2 (award handler fan-out), T3 flow (use → award → improve → power step), `IRandom` for any award RNG.

---

**Next:** run `/new-plan` (the `implementation-planner` agent) on this doc, scoped to **slice 1 (Progression substrate)**. It will extend this seed into the full plan — Preconditions/Postconditions, Main flow, Events, Systems/handlers, work packages, Content tooling impact, Cross-cutting surfaces, Flows, and the Test plan — folding the Architecture brief's seam decisions into Design notes and running the ground-rule-9 audit. Then the **spec-review gate** (`architecture-reviewer`, spec mode) before any code. Slices 2–5 each get their own plan, framed against this brief.
