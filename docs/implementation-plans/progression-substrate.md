# Progression Substrate — Slice 1 (Progression & Balance program)

**Status:** planned
**Actors:** Player (earns per-track experience through use) · System (heartbeat/combat award timing) · Administrator (inspects a player's tracks)
**Module:** `Core/Modules/Progression/` (new) — the domain module; extends the read seam of `Core/Modules/Stats/` (`IStatSystem`) via the existing `IEffectContributor` port; consumes `Core/Modules/Mobs/Events/MobDiedEvent.cs`.

> **Slice 1 of the [Progression & Balance program](progression-and-balance.md).** This is the transient per-slice plan; the durable cross-slice seam rationale (contribute-on-read, character-Tier additive baseline, power oracle, sim harness, the five-slice map) lives in the program brief and is **not duplicated here** (INV-27) — this doc links to it and adds only the slice-1 build. Slices 2–5 each get their own plan against that brief.

---

## Description

Experience-driven progression (gameplay-model [**Spine E**](../design/gameplay-model.md#spine-e--progression-experience-driven-growth--objectives)) for the score substrate that is **already built** (`ScoreId`/`IStatRegistry`, `AttributeSystem`, pools, compute-on-read `IStatSystem`). A new `ProgressionComponent` stores durable **per-track cumulative experience**, keyed directly by `ScoreId`. A new domain `IProgressionSystem` awards experience *on use* and injectable jumps (`AwardExperience`), and auto-improves a track when its cumulative XP crosses a **growing threshold** (`TryImprove`) — each improvement grants a **linear** power step while the threshold to the *next* improvement grows, so power is near-unlimited but the *rate* of gain slows (the curve lives in the threshold, never in the power). The power a track yields is **pulled on read** by `IStatSystem` through a `ProgressionEffectContributor` registered on the existing `IEffectContributor` port ([INV-24](../architecture/checklist.md)) — never materialized into a stored component. Slice 1 wires a single accrual source (combat kills, off `MobDiedEvent`) with a relative-power anti-grind guard, publishes `ExperienceAwardedEvent` and (conditionally) `TrackImprovedEvent`, and ships a `progress` inspector command as the functional-validation "see it work" gate. The character-wide **Tier** scalar, the power-budget oracle, and the sim harness are later slices; base-mutation (discrete permanent growth) stays a shaped-for-later seam and is out of scope here.

---

## Preconditions

- The entity has an `AttributesComponent` and a `PoolsComponent` (every player and mob does; both are already `[Persistent]`).
- `IStatSystem`, `IAttributeSystem`, the `IEffectContributor` DI collection, and `IRandom` are registered (all built).
- For a combat award: a `CombatEndedEvent(Outcome = MobDied)` has already been finalized by `CombatMobDeathHandler`, which publishes `MobDiedEvent` (carrying `MobEntityId`, `BlueprintId`, `KillerEntityId`) while the mob entity is still live.
- The `Progression` module is registered in `Server/Program.cs`, and its XP-award handler is subscribed to `MobDiedEvent`.

## Postconditions

- **Award:** `AwardExperience(entity, track, amount, source)` adds `amount` (after the anti-grind scale) to that track's cumulative XP in `ProgressionComponent`, creating the component/track entry on first award. A non-positive scaled amount is a no-op (no entry created, no event).
- **Improve (invisible internal state):** when a track's cumulative XP reaches its next-improvement threshold, `TryImprove` increments that track's improvement count by exactly one per crossing (a single award that vaults multiple thresholds improves once per threshold crossed, draining XP against each), and the *next* threshold is strictly greater than the one just crossed (growing gap).
- **Contribution (invisible internal state):** `IStatSystem.Get(entity, score)` returns base + effect modifiers + `ProgressionConstants.PowerPerImprovement × improvementCount(score)` for every track score — folded through the `IEffectContributor` port, **pulled on read, never stored** (no `EffectsComponent` entry, no cached field).
- **Anti-grind (invisible internal state):** the awarded amount is scaled by the killer-vs-victim relative-power factor; a victim far below the killer's effective power yields a floored-to-zero award, a peer yields the full base, and the scale never exceeds the configured cap.
- **Events:** exactly one `ExperienceAwardedEvent` is published per awarding action that produced a positive amount; a `TrackImprovedEvent` is published **only** when that action crossed at least one threshold (one event per crossing).
- **Determinism:** any variance in the award amount resolves through the injected `IRandom`; two runs with the same scripted rolls and inputs produce identical XP and improvement counts.
- **Persistence:** `ProgressionComponent` is `[Persistent]`; a player's per-track XP and improvement counts survive a save→load round-trip. It is only ever attached to entities that already carry `PersistentEntity` (players); no world-content entity gains it (INV-23).
- **Inspector:** `progress` writes a typed message listing each track's improvement count, cumulative XP, and XP-to-next-threshold for the invoking entity.

---

## Main flow

1. A player kills a mob; `CombatMobDeathHandler` publishes `MobDiedEvent(MobEntityId, BlueprintId, KillerEntityId)` before destroying the mob (existing flow-20 behavior — unchanged).
2. `ExperienceAwardHandler` (new, priority `HandlerPriority.Domain` = 20) receives `MobDiedEvent`. If `KillerEntityId == 0` it returns (no attributable killer, no award). The mob is still live, so its scores are readable.
3. The handler asks `IProgressionSystem` to resolve the combat award: `AwardCombatExperience(killerEntityId, victimEntityId)`. The system computes the anti-grind relative-power scale (killer vs. victim effective power via `IStatSystem`), applies it to the base combat award, splits the scaled amount across the mapped combat tracks (slice 1: `Body`, plus `HpMax`), and for each track calls the internal `AwardExperience` → threshold-check → `TryImprove` sequence, mutating `ProgressionComponent`.
4. `IProgressionSystem` returns a `CombatAwardResult` record: the per-track `(ScoreId, amountAwarded, improvementsGained, newImprovementCount)` rows. The system publishes nothing (INV-5).
5. `ExperienceAwardHandler` publishes one `ExperienceAwardedEvent(entityId, track, amount, source)` for each row with `amount > 0`, and — for each row with `improvementsGained > 0` — one `TrackImprovedEvent(entityId, track, newImprovementCount)` per improvement (INV-8: the conditional publish is the handler's concern).
6. On the next `IStatSystem.Get(killerEntityId, ScoreId.Body)` (e.g. from `score`, combat, or `progress`), `EffectSystem.GetModifiers` sums the registered contributors; `ProgressionEffectContributor` returns `PowerPerImprovement × improvementCount(Body)` for that entity, pulled on read from `ProgressionComponent`. The effective Body reflects the new step with nothing baked into the base.
7. The player types `progress`; `ProgressCommand` reads `IProgressionSystem` accessors and writes a `ProgressDisplayMessage` listing each track's improvement count, cumulative XP, and XP-to-next.

## Events fired

- **`ExperienceAwardedEvent(uint EntityId, ScoreId Track, int Amount, XpSource Source)`** — past-tense, thin; a track gained XP. Frequent; for prompt/telemetry. Published by `ExperienceAwardHandler`.
- **`TrackImprovedEvent(uint EntityId, ScoreId Track, int NewImprovementCount)`** — past-tense, thin; the discrete milestone a track crossed a threshold into a power step. Published by `ExperienceAwardHandler`, conditionally. This is the fact future slices (prompt highlight, achievements, sim labeling) subscribe to.

`XpSource` is a thin enum (`CombatKill` in slice 1; `Book`, `Trainer`, `Objective` reserved for later sources — declared now so the award signature is stable, but only `CombatKill` is wired).

## Systems / handlers involved

| Piece | Kind | New/Reuse | Note |
|---|---|---|---|
| `ProgressionComponent` | Component (data) | **New** | `[Persistent]`; `Dictionary<ScoreId,int> Xp`, `Dictionary<ScoreId,int> Improvements`. |
| `IProgressionSystem` / `ProgressionSystem` | Domain system | **New** | `AwardExperience`, `TryImprove`, `AwardCombatExperience`, read accessors. Takes `EntityService`, `IStatSystem`, `IRandom`. |
| `ProgressionEffectContributor` | Domain adapter of the core `IEffectContributor` port | **New** | Folds `PowerPerImprovement × improvementCount(score)` into `EffectSystem.GetModifiers`/`GetActive` (INV-24). Registered `AddSingleton<IEffectContributor, …>`. |
| `ProgressionConstants` | Balance constants (Category 3) | **New** | `PowerPerImprovement`, base + growth of the threshold curve, base combat award, anti-grind floor/cap. Co-located with the system. |
| `ExperienceAwardHandler` | Handler | **New** | Subscribes `MobDiedEvent`, priority `HandlerPriority.Domain` (20). Orchestrates only; publishes the two events. |
| `ProgressCommand` | Initiator (command) | **New** | Player verb `progress`; reads accessors; writes a typed message. |
| `IStatSystem.Get` | Domain system read seam | **Reuse (unchanged)** | Already folds `IEffectSystem.GetModifiers`; picks up the new contributor with **no interface change**. |
| `IEffectContributor` / `EffectSystem` | Core port + aggregator | **Reuse (unchanged)** | The DI-collected contributor list already sums equipment + abilities; progression is a third registrant. |
| `MobDiedEvent` | Event | **Reuse (unchanged)** | Carries `KillerEntityId` + `MobEntityId`; sufficient for award + anti-grind (victim scores read while live). |
| `IStatSystem` (read of victim/killer scores) | Domain system | **Reuse** | Anti-grind relative-power proxy; the slice-3 `IPowerBudgetSystem` will later supersede this proxy. |
| `IRandom` | Core system seam | **Reuse** | Injected for award variance (INV-26). |

**Layering check:** `ProgressionSystem` is domain-tier; it composes `IStatSystem` (domain) and `IRandom` (core) — a legal downward path ([INV-1](../architecture/checklist.md)). `ProgressionEffectContributor` implements the **core-owned** `IEffectContributor` interface, so the dependency arrow points domain → core, satisfying [INV-2](../architecture/checklist.md)/[INV-24](../architecture/checklist.md) without `EffectSystem` referencing the Progression module. No system touches the bus (INV-5); the handler and command publish/emit.

---

## Implementation plan — work packages

Three packages; the primary agent runs `architecture-reviewer` (code mode) across the combined diff after all three land.

### WP-1 — Progression domain core (component, system, constants, contributor)

- **Scope:** `ProgressionComponent` (`[Persistent]`); `IProgressionSystem` + `ProgressionSystem` (`AwardExperience`, `TryImprove`, `AwardCombatExperience`, read accessors, `CombatAwardResult`/`AwardOutcome` result records); `ProgressionConstants`; `ProgressionEffectContributor`; `ProgressionModule.AddProgressionModule` registering the system + the contributor (`AddSingleton<IEffectContributor, ProgressionEffectContributor>`). Call `AddProgressionModule` from **`Server/CompositionRoot.Register`** — the shared pure-DI composition both the telnet `Server` and the Blazor `Hedron.Web` host boot — alongside `AddEconomyModule`, **not** from `Program.cs`. Because `ProgressionEffectContributor` is DI-collected into `EffectSystem` via `IEnumerable<IEffectContributor>`, registering only in `Program.cs` would leave the Blazor host's `StatSystem` silently under-counting progression (a latent INV-24 correctness gap). Precedent: `EconomyModule.AddEconomyModule` is registered in `CompositionRoot.Register` for exactly this reason.
- **Files:** `Core/Modules/Progression/Components/ProgressionComponent.cs`, `Core/Modules/Progression/Systems/IProgressionSystem.cs`, `…/ProgressionSystem.cs`, `Core/Modules/Progression/ProgressionConstants.cs`, `Core/Modules/Progression/ProgressionEffectContributor.cs`, `Core/Modules/Progression/ProgressionModule.cs`; module registration wired in `Server/CompositionRoot.cs` (`Register`). The `MobDiedEvent` handler *subscription* lands in `Server/Program.cs` in WP-2 — the correct split (type registration in `CompositionRoot`, bus subscription in `Program.Main`), mirroring `CurrencyLootHandler`.
- **Depends on:** nothing new (all substrate built).
- **Out of scope:** no event publication (WP-2); no Tier term (slice 2); no command (WP-3).
- **Exit criterion:** Tier-1 tests green — threshold + linear-power math, multi-threshold single-award drain, anti-grind scale (floor-to-zero far below, full at peer, capped), and the contributor fold; Tier-4 round-trip green.

### WP-2 — Combat award handler + events + wiring

- **Scope:** `ExperienceAwardedEvent`, `TrackImprovedEvent`, `XpSource` enum; `ExperienceAwardHandler` (subscribes `MobDiedEvent`, priority `Domain`); subscribe it in `Server/Program.cs` (`bus.Subscribe<MobDiedEvent>(handler)`).
- **Files:** `Core/Modules/Progression/Events/ExperienceAwardedEvent.cs`, `…/TrackImprovedEvent.cs`, `Core/Modules/Progression/XpSource.cs`, `Core/Modules/Progression/Handlers/ExperienceAwardHandler.cs`, subscription in `Server/Program.cs`.
- **Depends on:** WP-1.
- **Out of scope:** non-combat sources (`Book`/`Trainer`/`Objective` are declared but unwired); the inspector.
- **Exit criterion:** Tier-2 handler test — `MobDiedEvent` with a live victim fans out the correct `ExperienceAwardedEvent`(s) and conditional `TrackImprovedEvent`(s) via a `RecordingEventBus`; `KillerEntityId == 0` publishes nothing.

### WP-3 — `progress` inspector command (functional-validation gate)

- **Scope:** `ProgressCommand` (`ICommand`, player, `MatchingMode.Partial`, no privileges) + `ProgressDisplayMessage` typed output; register `AddSingleton<ICommand, ProgressCommand>()` in `ProgressionModule`. Reference-catalog + flow doc updates.
- **Files:** `Core/Modules/Progression/Commands/ProgressCommand.cs`, `Core/Modules/Progression/Messages/ProgressDisplayMessage.cs` (+ formatter registration per the output framework), registration in `ProgressionModule`; docs.
- **Depends on:** WP-1 (accessors).
- **Out of scope:** any admin `setprogress`-style mutation (see Content tooling impact — deferred with rationale).
- **Exit criterion:** Tier-3 flow test — kill a fixture mob across the pipeline, then `progress` reflects the gained XP / improvement; message asserted by type/structure, not exact prose.

---

## Content tooling impact

Slice 1 adds gameplay state (`ProgressionComponent`). Per [INV-18](../architecture/checklist.md):

- **Inspect:** the `progress` command is the player/admin read surface — it lists each track's improvement count, cumulative XP, and XP-to-next-threshold. This is also the program's per-slice functional-validation "see it work" gate (program Design notes).
- **Author (world content):** progression is **runtime-accrued state on persistent entities**, not authored world content. There is **no YAML template shape** and **no `TemplateRegistry` entry** — mobs and rooms never carry a `ProgressionComponent` (they are world content, INV-23). A mob's *value as an XP source* is a function of its existing scores (read live during the award), not authored progression data. Nothing to add to the content pipeline.
- **Admin mutation — deferred with rationale (not a gap):** a `setprogress`-style admin command to hand-set a player's track XP/improvement would be an admin **boundary save** (mutate via the domain system → `SaveEntityAsync` → audit event, INV-22). It is **out of slice-1 scope**: the accrual path + inspector fully exercise and observe the state for this slice, and no balance/testing task needs hand-set progression yet. Tracked as a backlog item (`backlog.md`), to land when a designer needs to seed a fixture without grinding — consistent with the admin-mutation pattern used by `setplayer`/`setrespawn`.

---

## Agent tooling impact (INV-20)

Slice 1 introduces the **progression extension pattern** — use-driven accrual · contribute-on-read · handler-on-event XP sources · constants-then-table tuning. Per [INV-20](../architecture/checklist.md), that pattern's agent tooling lands with the pattern:

- **New skill — [`edit-progression-system`](../../.claude/skills/edit-progression-system/SKILL.md):** the actionable how-to for extending/tuning progression — the three-layer model (mechanism = handler-on-event; tuning = `ProgressionConstants` → YAML at OD-2; generalization = advancement-rule table at the ≥3-source INV-19 threshold) plus recipes (add an XP source, tune curves, add a track, promote to the rule table) and the contribute-on-read guardrail. **This is the durable capture of the extensibility design** so it does not live only in a transient plan. Created now as forward tooling documenting the slice-1 target and indexed in `.claude/README.md`; **the implementation PR reconciles it to as-built, and `sync-roadmap` repoints its links to the `docs/features/progression/` home when this slice disintegrates** (INV-28).
- **Existing skills to check for drift (code-review gate confirms):** `add-domain-system` (returns-result system shape — progression conforms), `add-component` (`[Persistent]` runtime-state component), `add-handler` (award-off-event at `HandlerPriority.Domain`), `add-command` (`progress` inspector), `add-tests` (contributor-fold + never-materialized assertions). None appear to need edits for slice 1; the pattern matches their existing guidance.

---

## Cross-cutting surfaces stressed

Per [INV-19](../architecture/checklist.md) / ground rule 9. Each surface classified.

- **Contributor seam on `IStatSystem` (via `IEffectContributor`)** — **Adequate.** The core-owned `IEffectContributor` port already exists and is DI-collected by `EffectSystem`; equipment and abilities are proven registrants. Progression is a third registrant with **zero interface change** to `IStatSystem` or `EffectSystem` — the exact INV-24 pattern the program brief mandates. No hand-rolled aggregation; nothing materialized. *(Note: the progression contribution is a pure `int` per `ScoreId`, so it fits `GetModifiers` directly; `GetActive` yields a synthetic `WhileKnown`-style effect purely for display parity, mirroring `AbilityEffectContributor`.)*
- **Event bus** — **Adequate.** Two thin past-tense events (`ExperienceAwardedEvent`, `TrackImprovedEvent`) published by a handler at `HandlerPriority.Domain`, mirroring `CurrencyLootHandler` on the same `MobDiedEvent`. No new bus capability; no god-handler (INV-7).
- **Command / output framework** — **Adequate.** `progress` is a standard `ICommand` writing a typed `ProgressDisplayMessage` through `context.Output.WriteAsync` — no direct `session.SendLineAsync` (INV-11). A new typed message + formatter is the established output-framework extension path (precedent: `ScoreDisplayMessage`), not a gap.
- **Persistence** — **Adequate.** `ProgressionComponent` is `[Persistent]` and only attaches to entities already carrying `PersistentEntity` (players); it rides the periodic flush — **no caller-initiated `SaveEntityAsync`** in the accrual path (INV-22). See the persistence opt-in audit below.
- **Anti-grind relative-power proxy** — **Acknowledged debt (bounded, with rationale + backlog).** Slice 1 needs a killer-vs-victim power comparison, but the shared `IPowerBudgetSystem` oracle is **slice 3** (program map). Slice 1 therefore uses a **local proxy inside `ProgressionSystem`** (sum of effective attribute scores via `IStatSystem`). This is a deliberate, one-method, tunable stand-in — not a hand-rolled framework repeated ≥3× (it appears once), so it does not trip the INV-19 "build the framework now" bar; the framework is already scheduled. **Slice 3 replaces this proxy with the oracle call** and the proxy is deleted. Backlog entry: "progression anti-grind proxy → `IPowerBudgetSystem` (slice 3)". *(This is surfaced, not silently absorbed — the reviewer should confirm the proxy is isolated behind one private method so the slice-3 swap is a one-line change.)*

### Persistence opt-in audit (INV-22 / INV-23)

- **Level 1 — entity domain:** the only construction path this slice *touches* is adding a `ProgressionComponent` to an entity the first time it earns XP. That entity is always a **player** (the killer on a combat award) — already a persistent entity carrying `PersistentEntity`. Slice 1 attaches `ProgressionComponent` to **no world-content entity**: mobs are the victim (read-only, still live) and never gain the component. No entity changes persistence domain in this slice, so no `ItemContextHandler`-style transition applies.
- **Level 2 — component inclusion:** `ProgressionComponent` holds per-track cumulative XP and improvement counts — durable player state that must survive restart → **`[Persistent]`.** The derived power is *not* stored on it (pulled on read by the contributor), so there is no cached-derived field to persist. No transient sub-field.
- **Level 3 — save-on-change scope:** the accrual path is a runtime state change captured by the `PersistenceFlushTimer` periodic sweep — the handler and the system make **no** `SaveEntityAsync` call (would violate INV-22). The deferred `setprogress` admin command *would* be the permitted admin boundary save, but it is out of scope. No handler save, no non-admin command save.

---

## Flows introduced or modified

Per [INV-17](../architecture/checklist.md). The implementation PR updates `flows/README.md` accordingly.

- **New flow: `flow-31-progression-award.md`** — "Progression journey (combat XP award · threshold improve · contribute-on-read)". Trigger: `MobDiedEvent` fires (mob kill) → `ExperienceAwardHandler` → `IProgressionSystem.AwardCombatExperience` (anti-grind scale → per-track award → `TryImprove`) → handler publishes `ExperienceAwardedEvent`/`TrackImprovedEvent` → later `IStatSystem.Get` folds the contributor on read. Add its row to the `flows/README.md` index. This is a recurring runtime chain (every kill), so it earns a canonical flow entry.
- **Modified: [flow-20 — Death & respawn journey](../architecture/flows/flow-20-mob-death-respawn.md)** — the `MobDiedEvent` fan-out gains a **third** subscriber (`ExperienceAwardHandler`), alongside `SpawnSystem` (slot vacancy) and `CurrencyLootHandler` (loot). No ordering constraint between them (all read the live mob pre-destroy; `HandlerPriority.Domain`). Update the subscriber list / note in flow-20; no diagram restructure needed.
- **Not modified:** flow-17 (combat initiation/round) and flow-03 (command lifecycle — `progress` is an ordinary registered command that plugs into the existing lifecycle unchanged).

---

## Test plan / Verification

Per [INV-25](../architecture/checklist.md) and the tier rubric in [07-testing.md](../architecture/07-testing.md). Every Postcondition asserting invisible internal state maps to a named test.

**Tier 1 — system unit (`ProgressionSystemTests`, `Hedron.Tests/Progression/`):** the core of the suite.
- `AwardExperience` adds the scaled amount to the track's XP; first award creates the component/entry; a non-positive amount is a no-op (no entry, asserted).
- `TryImprove` increments improvement count by exactly one per threshold crossed; a single large award that vaults N thresholds improves N times and drains XP against each (multi-crossing postcondition).
- Growing-threshold math: threshold(k+1) > threshold(k) for the slice-1 curve (asserts the "slowing rate" invariant, not the exact numbers).
- Anti-grind scale: victim far below killer → floored to zero (no award/no event); peer → full base; result never exceeds the configured cap (three cases; the anti-grind postcondition).
- Determinism: identical inputs + scripted `FakeRandom` → identical XP and improvement counts (INV-26).
- **Contributor fold (`ProgressionEffectContributorTests`):** `GetModifiers(entity, score)` returns `PowerPerImprovement × improvementCount(score)` and `0` for unimproved/absent tracks; `IStatSystem.Get` reflects the step with **no** `EffectsComponent` written (assert the component is absent — the "never materialized" postcondition).

**Tier 2 — handler (`ExperienceAwardHandlerTests`):** feed `MobDiedEvent` to the handler with a live fixture victim; assert the `RecordingEventBus` captured one `ExperienceAwardedEvent` per positive-amount track and a `TrackImprovedEvent` **only** when a threshold was crossed; `KillerEntityId == 0` captures nothing. Does **not** re-derive the system's math (Tier 1's job).

**Tier 3 — flow (`ProgressionAwardFlowTests`):** the executable Main Flow. Real system + handler + bus + seeded `IRandom`; kill a fixture mob (or drive `MobDiedEvent` end-to-end), pump the pipeline, assert the Postconditions: killer's `ProgressionComponent` gained XP, crossed a threshold on the expected award, `IStatSystem.Get(killer, Body)` increased by exactly `PowerPerImprovement`, and `progress` output reflects it (message asserted by type/structure).

**Tier 4 — persistence round-trip (add to `RoundTripTests`):** a player with a populated `ProgressionComponent` (non-default XP + improvement counts on ≥2 tracks) survives save→load into a fresh world with per-track values equal; a world-content entity never carries it (already covered by the INV-23 guard posture).

**Tier 5 — architecture-guard:** covered by the existing suite (no-bus-in-systems catches any stray `IEventBus` in `ProgressionSystem`; components-are-data catches logic on `ProgressionComponent`; no-ambient-nondeterminism catches a stray `Random.Shared`). No new guard needed.

**Skipped (rubric-justified):**
- `ProgressionComponent` getters/setters — pure data (INV-3), guaranteed by the components-are-data guard.
- Exact `ProgressDisplayMessage` prose/layout — presentation; asserted by type/structure only.
- Thin `ProgressCommand` parse→read→write body in isolation — covered by the Tier-3 flow; no custom argument resolver to unit-test.
- Per-module DI registration — the DI-smoke guard covers it once.
- `ExperienceAwardedEvent`/`TrackImprovedEvent` records — thin payloads.

**Testability seam:** `ProgressionSystem` takes `IRandom` by constructor injection (INV-26) and reads scores through `IStatSystem` — both substitutable — so every decision is unit-testable with constructed entities and a `FakeRandom`. No un-injected seam is introduced; no seam-fix precedes this slice.

---

## Design notes

> Slice-1 seam rationale. The durable cross-slice rationale (why contribute-on-read, why a character-Tier additive baseline, why the power oracle and sim) lives in the [program brief](progression-and-balance.md#design-notes) and is not restated here (INV-27). These notes cover only decisions this slice makes *within* those seams.

### `TrackId` is `ScoreId` — no new key type

A track is keyed **directly by `ScoreId`** (the brief's "a track is likely keyed by `ScoreId`", confirmed against `IStatRegistry`, whose `ScoreRegistration` already classifies every score by role). Introducing a parallel `TrackId` enum would fork the score vocabulary and force a mapping table for no gain. `AwardExperience(entity, ScoreId track, …)` and the two events carry `ScoreId` directly. If a future track ever needs to exist that is *not* a score (unlikely given Spine E), a `TrackId` wrapper can be introduced then — it is not needed now.

### Initial track set: the four attributes + `HpMax`

Slice 1 progresses the four **primary attributes** (`Mind`, `Body`, `Spirit`, `Attunement`) and the **`HpMax` pool** — exactly the scores Spine E calls out as growing on their own tracks ("HP is a pool with no governing stat — it advances on its own progression track"; attributes "grow via progression tracks"). Derived scores (`AttackPower`, `Defense`) are **not** tracks — they are computed from attributes + gear + effects, so they rise automatically when their inputs' tracks improve (progressing a derived score directly would double-count). The other pools (`Mana`/`Stamina`/`Astra`) are governed by an attribute (`IStatRegistry` records `GoverningAttribute`); their max rising off the governing attribute's track is a later refinement, out of slice-1 scope — slice 1 keeps the pool track to `HpMax` (the ungoverned one) to avoid pre-deciding pool-derivation math.

### Reconciliation with ability improvement — no duplication

Spine E lists ability attunements as advanceable tracks, and the *design* `AbilityDefinition` carries an `ImprovementCurve` field. **In code today there is no ability-improvement mechanism** — `AbilitiesComponent` holds only `Known` + `CooldownRemaining`; no "improve on use" exists. Slice 1 therefore does **not** touch abilities and does **not** duplicate any mechanism. It keeps progression to **attribute/pool `ScoreId` tracks**. When ability-track improvement is later built, it reuses this same `IProgressionSystem` (a per-ability-id track), rather than growing a second improvement engine — the ability id becomes the track key on a future overload. This is the reconciliation the brief asked for, recorded as the durable decision.

### Tier field does **not** land on `ProgressionComponent` this slice

The character-wide Tier scalar is slice 2 (Ascension). Slice 1 places it on **slice 2's `AscensionComponent`**, not on `ProgressionComponent`. Rationale: an unused `[Persistent]` `Tier` field on `ProgressionComponent` now would (a) bloat the slice-1 persistence shape and round-trip test with a dead field, and (b) pre-commit the Tier's home before slice 2 decides Ascension's component boundary. The contributor is written so slice 2 adds the additive-baseline **tier term** as a second contribution source (either a second registrant or a term folded in) without reshaping slice-1 code — the seam is left open, the field is not planted early.

### Contribution rides the existing `IEffectContributor` port, not a new port

The brief names an `IProgressionContributor`. Investigation of the real seam shows `IStatSystem.Get` folds `IEffectSystem.GetModifiers`, which sums a **DI-collected `IEnumerable<IEffectContributor>`** — the port equipment and abilities already implement. Rather than add a *parallel* `IProgressionContributor` that `IStatSystem` would have to be re-plumbed to also fold (a second aggregation path for the same job), slice 1 registers `ProgressionEffectContributor : IEffectContributor`. This is a stricter reading of INV-24 ("*the* core-owned contributor port"), reuses the proven aggregation, and needs **zero** change to `IStatSystem`/`EffectSystem`. The brief's intent (contribute-on-read through the core port) is fully honored; the concrete port is the one that exists. *(Surfaced as a within-seam refinement, not a contradiction — the program brief's Placement table lists `IProgressionContributor` as "a domain adapter of a core-owned port", which `IEffectContributor` is. **Owner-approved (2026-07-04): reuse the existing port.** The spec reviewer validates the INV-24 reading, not the choice between ports.)*

### Math lives in `ProgressionConstants` (Category 3)

`PowerPerImprovement` (linear step), the threshold curve (base + growth), the base combat award, and the anti-grind floor/cap are named constants co-located with the system, per [configuration Category 3](../architecture/05-configuration.md). They change in the same commit as the system that reads them. Promotion to a tunable data file is deferred to the demonstrated need (OD-2) — the sim slice (4) is the likely trigger, not now.

---

## Related

- [progression-and-balance.md](progression-and-balance.md) — **the program brief**: cross-slice seams, the five-slice map, resolved decisions, and open questions (this slice resolves only OQ #4, partially — see below).
- [gameplay-model.md → Spine E](../design/gameplay-model.md#spine-e--progression-experience-driven-growth--objectives) — the progression model this instances; [Substrate — Stats & Scores](../design/gameplay-model.md) for the score roles.
- [stat-system.md](../features/character-stats/stat-system.md) — the built `IStatSystem` read seam the contributor folds into.
- [effect-system.md → the contributor seam](../features/effects/effect-system.md) — the `IEffectContributor` port precedent (equipment, abilities).
- [combat.md](../features/combat/combat.md) / [flow-20](../architecture/flows/flow-20-mob-death-respawn.md) — the `MobDiedEvent` source the award subscribes to.
- [currency-loot-system.md](../features/economy/currency-loot-system.md) — `CurrencyLootHandler`, the award-off-`MobDiedEvent` precedent this handler mirrors.
- [checklist.md](../architecture/checklist.md) — INV-24 (contribute-on-read), INV-2/INV-1 (layering), INV-5/INV-8 (systems return / handlers publish), INV-25/INV-26 (verification + determinism), INV-22/INV-23 (persistence).

---

## Open questions

> **Resolved with the owner (2026-07-04).** The one parked slice-1 question (program OQ #4) is closed — the owner accepted the recommended anti-grind defaults, to be tuned via the slice-4 simulator. **No open question remains for slice 1.** It was never load-bearing for an INV (mechanism, seam, and determinism were fully specified; only the tuning numbers were the fork; SR-5 does not apply).

1. **XP-award sourcing + anti-grind curve — RESOLVED (owner accepted defaults; sim-tuned later).**
   - **Source (slice 1):** combat kills only, via `MobDiedEvent`, awarding **`Body` + `HpMax`** (physical-only). Other attributes (`Mind`/`Spirit`/`Attunement`) accrue when their *own* use-sources land in later slices (e.g. ability use) — not force-fed by melee kills. *(Owner accepted physical-only for slice 1.)*
   - **Anti-grind shape:** `clamp(victimPower / killerPower, 0, cap)` scaling the base award — **linear, floor ≈ 0.25, cap ≈ 1.5**, all in `ProgressionConstants`. Trivial victims round to zero; over-strong victims grant no windfall. *(Owner accepted the default. These are balance numbers, tuned for real once the slice-3 power oracle and slice-4 sim exist — the proxy is replaced then and tuning becomes data-driven.)*
   - **Contributor port:** *(Owner approved reusing the existing `IEffectContributor` port — see the Design note. The spec reviewer validates the INV-24 reading, not the choice between ports.)*
