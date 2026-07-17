# Slice prog-5 — Balance catalog + agentic layer (program close-out)

**Shipped:** 2026-07-17 · **Program:** Progression & Balance (final slice) · Docs/tooling only — no code, no test-plan surface (INV-25 n/a).

## What shipped

- **Balance catalog — [`docs/design/balance.md`](../../design/balance.md).** The durable balance
  reference: the author→observe→validate→correct→re-pin loop in one screen, the **knob catalog**
  (every tunable number by home — the standards YAML document, the Category-3 constants classes,
  the balance-relevant settings sections, authored content), the observability surfaces, the
  five-rule maintenance contract that keeps balance a living featureset as content expands, and
  the known-gaps list (each backed by a backlog entry). Companion to
  [`power-model.md`](../../design/power-model.md): that doc governs how power *inputs* extend;
  the catalog governs where the *numbers* live.
- **`balance-tuning` skill — [`.claude/skills/balance-tuning/SKILL.md`](../../../.claude/skills/balance-tuning/SKILL.md).**
  The operational how-to: find the knob's home, tune-and-re-pin, fold a new power source in per
  the power-model rule, land content in a (Tier, Band) cell, and validate with a simulation sweep
  (CLI + Blazor). Cross-linked with `edit-progression-system` (that skill owns progression
  *mechanics*; this one owns the numbers-and-validation loop).
- **INV-20 refresh.** `.claude/README.md` index row; the `architecture-advisor` power question now
  also points at the balance catalog/skill; `edit-progression-system` repointed off the deleted
  program brief onto the living docs.

## Decisions

- **No `run-simulation` skill (scope cut, owner decision 2026-07-17).** The program map listed
  `balance-tuning` + `run-simulation`. Running sweeps is a designer/admin surface (the Blazor
  Simulation page and the `simulate` CLI, both shipped in `prog-4`) — not a step an agent takes
  autonomously in the dev loop. The one dev-loop case (validating a deliberate balance change)
  is a recipe inside `balance-tuning` instead of a standalone skill.
- **Catalog lives at `design/balance.md`, not `reference/`.** It spans several features
  (progression, ascension, combat, economy, simulation) and carries a forward-looking maintenance
  contract — the `design/` cross-cutting-model bucket, exactly where the backlog item and the
  program map placed it. Per-system truth stays in `features/`; the catalog links.
- **Balance-reviewer agent stays a backlog stretch item** (unchanged from the program map): build
  when balance regressions from content expansion become a recurring cost.

## Program close-out (Progression & Balance)

`prog-5` completes the program; its brief (`docs/implementation-plans/progression-and-balance.md`)
disintegrated on ship per INV-28. The program map, for the record:

| Slice | Landed | Record |
|---|---|---|
| `prog-1` Progression substrate | per-track XP, contribute-on-read, anti-grind, `progress` | [`progression-substrate.md`](progression-substrate.md) |
| `prog-2` Ascension | character-wide Tier 0–6, additive baseline, `ascend` | [`ascension.md`](ascension.md) |
| `prog-3` / `prog-3b` Power model | `IPowerBudgetSystem` oracle, Tier × Band, projections, audit | [`power-budget-inspector.md`](power-budget-inspector.md) · [`power-model-revision.md`](power-model-revision.md) |
| `prog-4` Balance simulator & workbench (sub-program `sim-1`–`sim-5`) | standards registry, sim engine, Blazor integration, progression-rate scenarios, conformance | [`balance-standards-registry.md`](balance-standards-registry.md) · [`simulation-engine-core.md`](simulation-engine-core.md) · [`simulation-editor-integration.md`](simulation-editor-integration.md) · [`progression-rate-scenarios.md`](progression-rate-scenarios.md) · [`conformance-tooling.md`](conformance-tooling.md) |
| `prog-5` Balance catalog + agentic layer | this slice | — |

Where the brief's durable content now lives:

- **Progression/Ascension/power design + seam rationale** → [`features/progression/`](../../features/progression/progression.md)
  (feature + system docs) and [`design/power-model.md`](../../design/power-model.md).
- **Advancement-triggers three-layer model** (mechanism / tuning / generalization-at-3) →
  [`edit-progression-system`](../../../.claude/skills/edit-progression-system/SKILL.md), pointed
  to from the progression docs.
- **Simulation design** → [`features/simulation/`](../../features/simulation/simulation.md).
- **Balance knobs + maintenance contract** → [`design/balance.md`](../../design/balance.md) (this slice).
- **Resolved program decisions / open-question dispositions** → the per-slice records above
  (every open question in the brief was resolved and recorded on the slice that resolved it;
  OQ4 — XP sourcing + anti-grind — resolved in `prog-1`).
- **Deferred program threads** → [`backlog.md`](../backlog.md): ascension unlock-grant seam +
  Objective gate, mob-AI sim adapter, ascension-baseline calibration gap, progression-rate
  tolerances, live standards reload, owned-instance reconform sweep, balance-reviewer agent.
