# Power Model — Snapshot-Only Extensibility

> Named principle extracted from the [power-model-revision](../roadmap/completed/power-model-revision.md)
> slice (prog-3b). Not a feature doc — a durable rule for how `IPowerBudgetSystem` may (and may
> not) be extended, so a later slice can't silently violate it the way slice-3's first draft did
> with `AscensionConstants` (caught at code review).

## The rule

`IPowerBudgetSystem` (`Core/Systems/`) is a core-tier, snapshot-only oracle (INV-2). It never
imports a `Core/Modules/<Feature>/` domain type, never resolves an entity id, and never gains a
constructor dependency. Every input arrives one of two ways:

1. **A caller-supplied `PowerSnapshot`** — a plain `ScoreId → magnitude` map the caller builds
   from whatever source it has (a live `IStatSystem` read, an authored template, an editor's
   in-progress form). The oracle only ever weights and sums it.
2. **A caller-supplied tier** — the coarse Ascension scalar, passed as a bare `int`, never read
   from `AscensionComponent`/`IAscensionSystem` directly.

**A future power source is never added by teaching the oracle a new domain concept.** It folds in
one of two ways:

- **(a) A new stat-like `ScoreId`.** If the new source is just another number callers already
  have (a new attribute, a new item stat), add it to `PowerBudgetConstants.Weights` and have
  callers include it in their `PowerSnapshot`. No interface change.
- **(b) A richer source that estimates its own contribution.** If the new source needs its own
  math to turn into a power number (an equipped-ability roster, a temporary buff stack), it
  computes its own estimated contribution *outside* the oracle and the caller sums that into the
  snapshot (or adds it to `Estimate`'s result) before/after calling in. This mirrors the
  `IEffectContributor` precedent — the oracle stays the dumb weighted-sum step; the domain module
  owns translating its own state into a number.

What never happens: `IPowerBudgetSystem` gaining a reference to `Core/Modules/Abilities`,
`Core/Modules/Effects`, `Core/Modules/Combat`, or any other domain system to compute a
contribution itself. If a future slice is tempted to do that, it has violated this principle —
route the contribution through (a) or (b) instead.

## Why this is a single named doc

The rule was implicit in slice-3's `PowerBudgetSystem` (and its architecture-guard test,
`PowerBudgetSystem_has_no_domain_module_dependency`), but only as prose scattered across one
feature file. Writing it down here means the next slice that touches power doesn't have to
re-derive it from the code, and the **add-domain-system** / **add-core-system** skills and the
`architecture-advisor` can point at one place when they ask "does this affect power, and how does
its contribution enter the snapshot?" (INV-20).

## Distinct from effect-`Power`

[`gameplay-model.md` §6](gameplay-model.md#6-resolved-decisions) defines a *different* `Power` —
the effect system's potency/stack-rank scalar (`HighestWins` comparison key, computed at apply
time from a `PowerScaling` spec). The two share an English word and nothing else: one is a
balance-estimation oracle for content authoring, the other is a per-effect runtime magnitude.
Co-locating them would invite exactly the conflation INV-27/INV-30 (one fact, one home) exist to
prevent — so they stay in two homes, cross-linked here and in `power-budget-system.md`.

## Related

- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) —
  the feature doc for `IPowerBudgetSystem`/`Classify`/`TargetRange` (what it computes).
- [`../design/gameplay-model.md`](gameplay-model.md) §6 — the effect-`Power` this doc is
  explicitly kept distinct from.
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-2 (core-tier, no domain
  import), INV-20 (this principle folded into the tooling questions).
