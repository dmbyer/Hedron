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
| **1** | **Progression substrate** | `ProgressionComponent` (per-track XP), `IProgressionSystem` (use-driven `AwardExperience`/`TryImprove`), `ProgressionConstants` (linear step + growing threshold), `IProgressionContributor` into `IStatSystem`, XP-award off combat, `progress` inspector, `ExperienceAwardedEvent`/`TrackImprovedEvent` | substrate (built) |
| **2** | **Ascension (character-wide tier)** | Tier scalar + additive power baseline (rides the same contribute-on-read seam), tier-up gate, content band tagging, overlap semantics, ascension unlock-grants, `AscendedEvent` | 1 |
| **3** | **Power model + balance inspector** | `IPowerBudgetSystem` (core-tier shared oracle), tier power bands, in-game `power`/`powerband` inspector + Blazor editor readout | 1, 2 |
| **4** | **Simulation harness** | `Hedron.Sim` project (parameter-sweep runner, statistical reporting, combatant-policy seam), promoted CI regression invariants | 3 (uses power oracle) |
| **5** | **Agentic + doc layer** | Balance catalog (`design/balance.md`), `balance-tuning` + `run-simulation` skills, INV-20 updates to `add-*` and advisor/planner/reviewer | threads 1–4 |

Slices 1 and 3 establish the two shared spines every later balance/expansion touch reads; getting those seams right is the whole game. Once they exist, expanding items/skills/mobs becomes "add data → it shows in the inspector and the sim automatically."

> **Agent tooling note.** The `edit-progression-system` skill (how to add/tune XP sources, tracks, and curves — the "Advancement triggers" Design note below) lands in **slice 1** with the pattern it documents, per INV-20 — *not* slice 5. Slice 5's agentic layer adds the *balance* tooling (`balance-tuning` / `run-simulation` skills, the balance catalog) and the INV-20 refresh of the advisor/planner/reviewer for the power-model + sim surfaces.

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

1. **Additive tier baseline vs rescale-on-ascend** (slice 2) — recommend additive-baseline (removes XP-reset); confirm before speccing tier-up.
2. **Power-budget formula + band definition** (slice 3) — the heuristic's shape and how tier bands are drawn/tuned; expect iteration.
3. **Specialization-on-ascend selection mechanism** (slice 2+) — what the player customizes and how; likely deferred beyond the grant seam.
4. **XP-award sourcing + anti-grind** (slice 1) — which actions award which tracks, and diminishing-returns guards on trivial targets.
5. **Tier-baseline home** (slice 2) — inside `IProgressionContributor` now vs a nascent Scaling/Spine-D seam (Spine D is unbuilt; riding the progression contributor first is the cheaper start).
6. **`IPowerBudgetSystem` tier** (slice 3) — confirm it stays core-tier-generic; if it needs game-semantic inputs it moves to domain.

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
