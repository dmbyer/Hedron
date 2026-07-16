# Balance Simulator & Workbench — prog-4 Program Seed

**Status:** planned
**Actors:** Administrator/Designer (authors standards, runs simulations, conforms content) · System (`simulate` run-mode, CI regression invariants) · Blazor editor (`Hedron.Web` — standards editor, sim runner, run history) · Future content generator (programmatic validation caller)
**Module:** `Core/Modules/Simulation/` (new); extends `Core/Modules/BalanceInspection/`; `Core/Systems/` (`IPowerBudgetSystem` tunables promoted to injected data); `Server` (`simulate` run-mode); `Hedron.Web` (new pages)

---

## Description

The **`prog-4` slice of the [progression-and-balance program](progression-and-balance.md), expanded into its own sub-program** after an advisor intake (2026-07-13) surfaced requirements beyond the original "offline batch sim" framing: the simulator must be drivable from the admin editor, the (Tier, Band) expected-power criteria must be designer-editable data (they will shift continuously as skills/abilities/attributes are added), a future procedural mob generator must consume the same criteria and validation path, and the surrounding workbench (content census, out-of-band flags, single/bulk conformance) must close the loop from *observation* to *correction*. The program lands four capabilities in dependency order: (1) a **balance-standards registry** — data-driven reference builds, target power ranges, and expected-outcome tolerances per (Tier, Band); (2) a **simulation engine** in a Core module — deterministic batch combat scenarios over isolated sandbox worlds, validated against `IPowerBudgetSystem` and the standards; (3) **editor integration** — run/inspect simulations and edit standards from `Hedron.Web`; (4) **conformance tooling** — auto-fit flagged templates to their target band, single and bulk. A progression-rate scenario kind (time-to-improve / time-to-tier) rides the same runner. Player-owned-instance reconformance is deferred with its INV-21 policy decided now.

---

## Program shape — five sub-slices

Each sub-slice gets its own transient plan via `/new-plan` against this seed and runs the normal loop (spec gate → implement → code gate → `sync-roadmap`). This seed supersedes the single `prog-4` row in [`progression-and-balance.md`](progression-and-balance.md); `prog-5` (agentic + balance-doc layer) still follows the whole program — its `run-simulation` skill becomes meaningful after `sim-2`.

| # | Slice | Lands | Depends on |
|---|---|---|---|
| **sim-1** | **Balance standards registry — ✅ done** | Data-driven balance standards (Spine F): per-(Tier, Band) **reference builds** (scores + gear assumptions + ability kit), **target power ranges**, **expected-outcome tolerances** (e.g. equal-cell win-rate 50% ± tol; +1 band → X%). YAML-authored with compiled defaults; `IPowerBudgetSystem` tunables (`Weights`/`BandSpan`/`ReferenceBaseScores`/mirrors) promoted from compiled constants to **constructor-injected plain data** composed at startup (oracle stays zero-domain-dependency); load-time mirror-drift validation; `IBalanceAuditSystem` + `powerband` read from it; **standards authoring + readout surface in the Blazor editor** (INV-18). Durable design now lives in [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md); as-built history in [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md). | prog-3b (done) |
| **sim-2** | **Simulation engine core — ✅ done** | `Core/Modules/Simulation/`: data-keyed `ScenarioDefinition` (kind, combatant specs, policies, termination, iterations, seed), sandbox-world factory (isolated `EntityService` + hand-composed system graph per run — never the live world), `ISimCombatantPolicy` with simple built-ins, deterministic batch runner (per-run seeds derived from the scenario seed; parallel across worlds), statistical reporting (win rate, time-to-kill, damage distributions), expected-vs-actual comparison against the standards registry, report artifacts (structured files, run history), `simulate` CLI run-mode (no-chain Initiator, precedent: `generate`), a thin promoted CI-invariant subset in `Hedron.Tests`. Durable design now lives in [`../features/simulation/simulation.md`](../features/simulation/simulation.md) and [`simulation-engine.md`](../features/simulation/simulation-engine.md); as-built history in [`../roadmap/completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md). Surfaced a real Ascension tier-baseline calibration gap (tracked in `backlog.md`, not fixed by this slice). | sim-1 |
| **sim-3** | **Editor integration** | Blazor **Simulation page**: compose/launch scenarios (background execution in `Hedron.Web`), live status, report viewing over the run-history artifacts; **"simulate this" entry points** on `MobEditor`/`ItemEditor` (authored content vs the reference build of its authored cell); standards page gains "re-run baseline sweep" affordances. | sim-2 |
| **sim-4** | **Progression-rate scenarios** | Second scenario **kind** on the same runner: time-to-improvement / time-to-tier sweeps over `ProgressionConstants` curves, optionally consuming kill-rate outputs from combat scenario reports; reports render in the same editor page (kind-generic report shape proves out). | sim-2 (sim-3 for UI) |
| **sim-5** | **Conformance tooling (templates)** | Auto-fit: scale a template's stat vector toward `TargetRange(tier, band)` via the existing projection seams (`IItemPowerProjectionSystem`/`IMobPowerProjectionSystem`), preview → apply, **single and bulk** (over the `IBalanceAuditSystem` flagged set), YAML write-through `IContentDefinitionCatalog`; editor surface on the Integrity page. Templates only — player-owned instance reconform deferred (see backlog + Design note). | sim-1 (oracle/standards); sim-2 recommended first so the target math is sim-validated before bulk writes |

**Order rationale.** Standards first — the engine's default combatants *are* the reference builds, and the criteria must be data before three more consumers hardcode them. Engine before editor — the editor is a thin caller (content-tooling precedent). Conformance last — bulk-rewriting content against target ranges should wait until the sim has validated those ranges against real outcomes.

---

## Design notes

> Durable seam rationale — survives disintegration into `docs/features/` + `docs/design/` on ship.

### One engine, three surfaces — the content-tooling precedent, not a bespoke project

The original program brief predates the editor requirement and chose a dedicated `Hedron.Sim` project. **Revised (advisor intake 2026-07-13, user-confirmed): the engine is a Core module** (`Core/Modules/Simulation/`), registered via `CompositionRoot` so `Hedron.Web` resolves it directly — exactly how `IContentGenerationSystem` serves the `generate` run-mode and the editor, and how `BalanceInspectionModule` serves both hosts. Surfaces are thin callers: the `simulate` CLI run-mode (offline batch + CI), the Blazor Simulation page, and — later — the procedural mob generator calling a programmatic validate-candidate entry. One engine means the expected-outcome math cannot drift between the designer's editor view and the CI gate (INV-19 by construction).

### Sandbox worlds, never the live world

A simulation run composes an **isolated** `EntityService` + system graph (combat, stats, effects, aspects, progression) per run — the same property that makes Tier-3 flow tests cheap, industrialized. The engine never touches the host's live `EntityService`; INV-12's "one live world" applies to the *game* world, and sim worlds are explicitly not it (state this in the feature doc — it is the invariant nuance a reviewer will ask about). Isolation is also what makes runs parallelizable: worlds share nothing, so a sweep fans out across them safely. Determinism (INV-26): every run's `IRandom` is seeded by derivation from the scenario seed + run index; a scenario file re-run is reproducible end to end. The sandbox composition logic deliberately mirrors the `Hedron.Tests` harness (`EntityBuilder`, synthetic ticks, `FakeRandom`) — unifying the two behind one shared factory is a *shaped-for-later* refactor, not a day-one obligation.

### The balance-standards registry is *the criteria seam* — data now, because three consumers and a designer need it now

The user's stated requirement — criteria that "do not yet exist and will shift as content is added/changed/removed," editable in the workbench, feeding the future generator — is precisely the OD-2 promotion trigger the power-budget doc already predicted for prog-4. The registry (Spine F) owns three fact families, all per (Tier, Band):

1. **Reference builds** — the canonical loadout (scores, gear assumptions, ability kit) a baseline character at that cell has. They anchor the band math, are the sim's default combatants, and are the envelope the generator later builds against. This is also the "players, too" input: a reference build *is* a synthetic player.
2. **Target power ranges** — what `TargetRange(tier, band)` returns, now derived from data.
3. **Expected-outcome tolerances** — "equal-cell fight ≈ 50% win rate ± tol," "+1 band attacker wins ≥ X%," TTK envelopes. These live here, **not** hardcoded in the sim, so the same rows drive the editor's verdicts and the promoted CI invariants.

Consumers on day one: the oracle's anchors, `IBalanceAuditSystem`, `powerband`, the sim (sim-2), the editor (sim-1/sim-3) — comfortably past the INV-19 bar. As skills/spells/attributes multiply (the "hundred skills, hundred spells" horizon), reference builds are the *compression*: balance is stated as "what a baseline cell character looks like," not as per-content rules.

### The oracle stays snapshot-only — its tunables become injected plain data, not a registry dependency

Promoting the standards to data must not violate the [snapshot-only extensibility principle](../design/power-model.md) or INV-2. `IPowerBudgetSystem` gains **no** registry, no loader, no domain import: its tunables (`Weights`, `BandSpan`, `BandsPerTier`, `ReferenceBaseScores`, the Ascension mirrors) arrive as a **constructor-supplied plain data record**, composed at startup by the host from the standards data (compiled defaults as fallback). The oracle remains a dumb weighted sum over caller-supplied snapshots. Mirror-sync with `AscensionConstants`/`CharacterDefaultsOptions` moves from a comment discipline to a **load-time validation warning** (drift between the data file and the compiled gameplay constants is surfaced, never silently absorbed).

### The scenario model is the extensibility spine — data-keyed, combatant sources pluggable

`ScenarioDefinition` is data (YAML-authorable, editor-composable, generator-constructable): scenario **kind** (combat now, progression-rate at sim-4 — the kind seam is explicit from day one), combatant specs, per-combatant policy, termination condition, iteration count, seed. A **combatant spec** resolves through one of several sources: (a) an authored **mob template** id; (b) a **reference build** from the standards registry; (c) an **inline stat block** — the entry the future mob generator uses to validate candidates before writing YAML; (d) *deferred*: a live-player snapshot ("simulate my actual player"). The `ISimCombatantPolicy` seam and its future `IAISystem` adapter stand as previously resolved (already backlogged). 1v1 ships first; the scenario shape (lists of combatants per side) is written so N-vs-N is additive data, not a schema break.

### Reports are artifacts — a third durable class, deliberately outside SQLite and world YAML

A run emits a structured report record (inputs echoed, per-cell distributions, expected-vs-actual verdicts) written to a reports directory. Not SQLite (INV-14 is for live entity state), not world content — the same posture as `generate`'s output: files a designer and the editor read, diff, and keep or discard. The editor's run history is a directory listing, not a database.

### Conformance is scaling toward a target, not synthesis — and player-owned reconform is a named, deferred INV-21 exception

Template auto-fit scales an *existing* stat vector until its projected `Estimate` lands in `TargetRange` — preview then apply, through the existing projection seams and `IContentDefinitionCatalog` write path (YAML only; live entities pick it up on `reload`, per the established admin-mutation model). Stat-block *synthesis* from a target range remains procedural-generation scope, per the prog-3b resolution. **Player-owned instances:** INV-21 says admin mutations never retroactively update player-owned instances — correct as the default. The user's requirement (owned items conforming when blueprints change) is **resolved as policy now, mechanism later**: a future *named, admin-triggered, audited* "reconform owned instances" sweep — an explicit INV-21 exception in the same spirit as INV-22's named boundary saves — deferred to the backlog because it touches the persistence domain and deserves its own slice; nothing in this program builds on it.

---

## Architecture brief

> In-flight forward analysis; trimmed on ship. Feeds each sub-slice's planner run.

### Placement & layers

| Piece | Layer | Home |
|---|---|---|
| Balance-standards definitions + registry | Data + Spine F registry | `Core/Modules/BalanceInspection/` (or a `Balance` module — planner decides), YAML-backed with compiled defaults |
| Oracle tunables as injected data record | Core (plain data) | `Core/Systems/` — composed by hosts at startup; oracle keeps zero domain deps (INV-2) |
| `ScenarioDefinition` / combatant specs / report records | Data | `Core/Modules/Simulation/` |
| Sandbox-world factory + batch runner (`ISimulationRunner` or similar) | Domain system (tooling-tier, like `IContentGenerationSystem`) | `Core/Modules/Simulation/Systems/` |
| `ISimCombatantPolicy` + built-ins | Domain seam | `Core/Modules/Simulation/` |
| `simulate` run-mode | Initiator (no-chain, INV-10) | `Server` — precedent: `generate` |
| Blazor Simulation + Standards pages | Surface (thin caller) | `Hedron.Web` |
| Auto-fit / bulk conformance | Domain system + catalog write-through | `Core/Modules/BalanceInspection/` (reuses projection seams + `IContentDefinitionCatalog`) |
| Promoted CI invariants | Tests | `Hedron.Tests` (thin subset only; heavy sweeps stay out of CI) |

### Family disposition

| Concern | Disposition |
|---|---|
| Standards registry keyed by (Tier, Band) per content kind | **Build now** (sim-1) — the criteria seam everything reads |
| Oracle tunables → injected data | **Build now** (sim-1) — OD-2 trigger is real |
| Scenario model, combat kind, 1v1 | **Build now** (sim-2) |
| Scenario-kind seam (progression-rate) | **Build now** as a seam (sim-2); second kind at sim-4 |
| N-vs-N / group scenarios | **Shape for later** — sides-as-lists schema now, group combat when grouping exists |
| Inline-stat-block combatant source (generator entry) | **Build now** (sim-2) — cheap, and the generator's whole validation path |
| Live-player-snapshot combatant source | **Defer** — additive source when wanted |
| Sandbox factory ↔ test-harness unification | **Shape for later** — mirror deliberately; unify on a real ≥3 duplication signal |
| Template auto-fit, single + bulk | **Build now** (sim-5) |
| Player-owned reconform sweep | **Defer** — policy resolved (named INV-21 exception), backlog entry, own slice |
| Real `IAISystem` policy adapter | Already backlogged (unchanged) |
| Balance catalog + `run-simulation`/`balance-tuning` skills | Stays `prog-5` (unchanged) |

### Observers, contributors & event granularity

- The engine **publishes nothing**: systems return results (INV-5); the `simulate` run-mode is a no-chain Initiator (INV-10); editor-run completion is a UI concern in `Hedron.Web`, not a bus fact — no live-world observer exists for an offline sim.
- Conformance writes are YAML-side (editor/catalog), mirroring `SaveAsync` — no live events. If an in-game bulk-conform admin command lands later, it follows the admin audit-event pattern (INV-22 boundary).
- No new contributor ports: the sim *reads* through existing seams (`IStatSystem` inside sandbox worlds, projection systems, the oracle).

### Ordering & timing

Runs advance on synthetic ticks (the heartbeat is never involved); all chance flows through per-run seeded `IRandom` (INV-26). Parallel sweeps must not share `IRandom` or `EntityService` instances across worlds — isolation is the concurrency model. Long editor-triggered runs execute off the UI thread in `Hedron.Web` (background task; the engine itself stays synchronous per run).

### Invariants in tension

- **[INV-2](../architecture/checklist.md) / [power-model.md](../design/power-model.md)** — the oracle's data promotion must arrive as plain injected data, never a registry/domain dependency. *The sim-1 spec gate's central check.*
- **[INV-12](../architecture/checklist.md)** — sandbox worlds are not the live world; the feature doc must state the boundary explicitly so the guard suite / reviewer can hold it (the engine never resolves the host's `EntityService`).
- **[INV-5 / INV-10](../architecture/checklist.md)** — engine systems return results; `simulate` is no-chain.
- **[INV-18](../architecture/checklist.md)** — sim-1 adds authored state (standards YAML) → its authoring/inspection surface ships in the same slice.
- **[INV-19](../architecture/checklist.md)** — one engine + one standards registry serving CLI/editor/CI/generator; no per-surface forks.
- **[INV-21](../architecture/checklist.md)** — sim-5 conformance updates templates (YAML) only; the owned-instance exception is deferred and named.
- **[INV-25 / INV-26](../architecture/checklist.md)** — the engine is itself a system (tests per tier); promoted CI invariants are the thin regression subset; determinism end to end.
- **[INV-20](../architecture/checklist.md)** — sim-2 updates the advisor/planner guidance where it references the old `Hedron.Sim` project shape; the `run-simulation` skill remains `prog-5`.
- **[INV-28](../architecture/checklist.md)** — this seed disintegrates into `docs/features/` (simulation + standards system docs) and `docs/design/` on ship.

### Resolved decisions (advisor intake 2026-07-13 — do not relitigate)

| # | Decision |
|---|---|
| 1 | **Engine home: Core module** (`Core/Modules/Simulation/`) + `simulate` run-mode + Blazor thin caller — supersedes the program brief's dedicated `Hedron.Sim` project. |
| 2 | **Balance standards promoted to editable data now** (sim-1), including the oracle-tunables-as-injected-data move; compiled defaults retained as fallback. |
| 3 | **Conformance: templates in-program (sim-5); player-owned reconform deferred** with the INV-21 named-exception policy recorded now. |
| 4 | **Combat scenarios first; progression-rate scenarios in-program (sim-4)** on the same runner via the explicit scenario-kind seam. |

---

## Open questions

> Load-bearing for sub-slice planners; none block sim-1's spec.

1. **Standards data shape** (sim-1) — ✅ **RESOLVED (2026-07-13): standalone YAML-backed Spine F registry**, not a fifth `ContentKind`. Reuses the `IRegistry<TKey,TDef>` layer where it fits, loads from a dedicated YAML file with compiled defaults as fallback, fail-fast structural validation + mirror-drift warnings at load; the catalog's `IEntityTemplate`/spawn/delete-cascade semantics don't fit balance data. The Blazor standards page gets its own focused save path reusing the validate/warn posture. Also seeds the backlogged "YAML-authored definition pipeline for registry families" direction. See [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md).
2. **Reference-build fidelity** (sim-1/sim-2) — ✅ **RESOLVED (2026-07-13): scores + gear-equivalent stat bonuses day one**; the schema carries an optional ability-kit list field from day one (empty/minimal, validated but consumed by nothing), activating at a later slice without a schema break. See [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md).
3. **Sandbox composition mechanics** (sim-2) — hand-built system graph in a factory (mirroring tests) vs. a scoped DI container per world. Recommend the factory; validate construction cost at 10k-run scale.
4. **Report schema + retention** (sim-2/sim-3) — ✅ **RESOLVED (sim-2): JSON, `SchemaVersion: 1`, `{timestamp}-{scenarioName}-{seed}.json` under `Simulation:ReportDirectory`, run history = directory listing.** Consumed as-is by sim-3's `ISimReportReader` and Simulation page. See [`../roadmap/completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md).
5. **Editor long-run execution model** (sim-3) — ✅ **RESOLVED: background task + polling, not streamed progress.** A singleton `SimulationRunService` in `Hedron.Web` owns a FIFO run registry; pages poll `Snapshot()` on a ~750ms timer. Cancellation is cooperative through an additive `ISimulationRunner.Run` `CancellationToken` parameter — checked between per-iteration runs, writes no report artifact. Rationale (circuit-survival, poll-model parity with the existing directory-listing run history, batch speed making sub-second polling indistinguishable from streaming) lives in [`../architecture/08-blazor.md`](../architecture/08-blazor.md) "Background tooling jobs" and [`../roadmap/completed/simulation-editor-integration.md`](../roadmap/completed/simulation-editor-integration.md).
6. **Auto-fit scaling policy** (sim-5) — which knobs the fitter may touch (item `StatBonuses` only? mob base attributes? HP?) and whether it preserves the template's stat *ratios* or targets specific scores. A mechanic/design question for the user at sim-5 planning.
7. **Audit tolerance source** (sim-1) — ✅ **RESOLVED (2026-07-13): `BalanceAuditConstants.BandDriftTolerance` moves into the standards data** (same fact family); the Integrity page and the editor mismatch flags read it live through the registry, compiled default as fallback. See [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md).

---

**Next:** sim-1, sim-2, and sim-3 shipped — see [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md) / [`../roadmap/completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md) / [`../roadmap/completed/simulation-editor-integration.md`](../roadmap/completed/simulation-editor-integration.md) and [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) / [`../features/simulation/simulation.md`](../features/simulation/simulation.md). Sub-slices sim-4…sim-5 each get their own plan framed against this seed via `/new-plan`, starting with `sim-4` (progression-rate scenarios).
