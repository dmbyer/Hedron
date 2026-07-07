---
name: edit-progression-system
description: Use when adding or tuning an experience/advancement source, adjusting progression curves or anti-grind, adding a progression track, or generalizing XP triggers to a rule table. Covers the three-layer extensibility model — mechanism (handler-on-event), tuning (constants→YAML), generalization (rule registry at ≥3 sources) — and the contribute-on-read seam. Invoke when extending or tuning experience-driven progression (new XP source, new track, curve tuning, or the rule-table promotion).
---

# Edit the Progression System

Experience-driven progression (gameplay-model [Spine E](../../../docs/design/gameplay-model.md#spine-e--progression-experience-driven-growth--objectives)): every advanceable score has a per-track experience that accrues **through use**, and on crossing a **growing threshold** grants a **linear** power step — the curve lives in the threshold, never in the power, so power is near-unlimited but the *rate* of gain slows. That power is folded into effective scores **on read** through a contributor port — never baked into base, never cached.

This skill is how you **extend or tune** that system: add an XP source, adjust the curves, add a track, or generalize triggers to a data table.

> **Status.** Documents the as-built pattern from slice `prog-1` of the Progression & Balance program (durable design: [`docs/features/progression/progression.md`](../../../docs/features/progression/progression.md) · [`progression-system.md`](../../../docs/features/progression/progression-system.md); as-built history: [`docs/roadmap/completed/progression-substrate.md`](../../../docs/roadmap/completed/progression-substrate.md)). Symbol names (`IProgressionSystem`, `XpSource`, `ProgressionConstants`, `ProgressionEffectContributor`) match the shipped code.

**Authoritative:** [progression-system.md](../../../docs/features/progression/progression-system.md) · [program brief → Design notes](../../../docs/implementation-plans/progression-and-balance.md#design-notes) (slices 2–5) · [checklist](../../../docs/architecture/checklist.md) (INV-24 contribute-on-read, INV-5/INV-8 return-vs-publish, INV-19 framework-at-3, INV-26 determinism) · [config Category 3](../../../docs/architecture/05-configuration.md) (balance constants).

## The three layers (how progression is extended)

1. **Mechanism = a handler on a game event (code, few).** Every XP source is an action that fires a past-tense event; a thin handler subscribes and calls `IProgressionSystem.AwardExperience`. Adding a *kind* of trigger = wire a handler to that event. Precedent: `CurrencyLootHandler` on `MobDiedEvent`.
2. **Tuning = data (many), in `ProgressionConstants` now → YAML later.** Which track a source feeds, base amount, anti-grind, threshold curve — named constants, changed in the same commit; promote to YAML (Category 2, OD-2) only when recompile-free / per-mob / per-area tuning is a real need.
3. **Generalization = an advancement-rule table (Spine F), at the ≥3-source threshold (INV-19).** Collapse N bespoke handlers into one thin handler reading a registry `event/XpSource → (track(s), amount formula, anti-grind, conditions)`. Don't build it earlier.

## Recipe: add a new XP source

1. **Find or add the event.** Use the past-tense event for the action (`AbilityUsedEvent`, a damage-taken event, item `read`, `CraftCompletedEvent`, `ObjectiveCompletedEvent`). If none exists, the action's Initiator/handler publishes it — a **system never does** (INV-5). See [add-event](../add-event/SKILL.md).
2. **Add an `XpSource` enum value** — the stable key for the source.
3. **Add a thin handler** ([add-handler](../add-handler/SKILL.md)) subscribing to that event at `HandlerPriority.Domain`: resolve the actor + track(s), call `IProgressionSystem.AwardExperience(entity, ScoreId, amount, XpSource)`. The system returns a result and publishes nothing; the **handler** publishes `ExperienceAwardedEvent` and, conditionally, `TrackImprovedEvent` from that result (INV-8).
4. **Put amount / track mapping / anti-grind in `ProgressionConstants`** — never hardcode balance numbers in the handler body.
5. **Determinism:** any award-chance RNG resolves through `IRandom` (INV-26).
6. **Tests** ([add-tests](../add-tests/SKILL.md)): Tier-1 for new award math; Tier-2 for the handler fan-out; extend the Tier-3 flow if it's a headline source.
7. **At the 3rd source, stop adding handlers — promote to the rule table** (below).

## Recipe: tune progression

- `PowerPerImprovement` (linear step), the growing-threshold curve, base awards, and the anti-grind floor/cap ratio thresholds all live in `ProgressionConstants` ([Category 3](../../../docs/architecture/05-configuration.md)); the *power* that ratio is computed from comes from `PowerBudgetConstants.Weights` (slice `prog-3`, co-located with `PowerBudgetSystem`). The **threshold curve is the "slowing rate" knob**; the power step stays **linear** — don't curve the power to slow gains, curve the threshold.
- Validate a tuning change at scale with the slice-4 simulator, not by hand.
- Need recompile-free / per-mob / per-area tuning? Promote those constants to YAML content (OD-2) — don't scatter `IConfiguration` reads.

## Recipe: add a track

- **A track is a `ScoreId`** — no separate key type. An attribute or ungoverned pool becomes a track simply by being awarded to; nothing else to register.
- **Do not track derived scores** (`AttackPower`/`Defense`) — they rise via their inputs; tracking them double-counts.
- **Governed pools** (`Mana`/`Stamina`/`Astra`) grow via their governing attribute's track — don't give them an independent track without deciding the pool-derivation math.
- **Abilities:** a future ability track reuses `IProgressionSystem` with the ability id as the key — do **not** build a second improvement engine.

## Recipe: promote to the advancement-rule table (≥3 sources)

When 3+ XP sources exist, collapse the bespoke handlers into **one** thin advancement handler reading a registry: `XpSource/event → (track(s), amount formula, anti-grind, conditions)`. Hardcoded rows first (Spine F registry shape), YAML rows at OD-2. This is the [INV-19](../../../docs/architecture/checklist.md) "framework at the 3rd repetition" promotion — the `XpSource` key makes it additive, not a rewrite.

## Guardrails

- **Contribute-on-read, not base-mutation.** Progression power is pulled through the `IEffectContributor` port on read; **never** write it into a stored component and never cache it (INV-24). Discrete *permanent* growth (rare-material consumption → +base forever) is a **separate** direct-base-mutation action — different seam, don't conflate.
- **Systems return, handlers publish** (INV-5/INV-8). No `IEventBus` inside `ProgressionSystem`.
- **Don't fork the vocabulary.** Track key = `ScoreId`; no parallel `TrackId` enum.
- **Balance numbers live in constants** (Category 3), not `appsettings.json`, not the handler body.
- **The character-wide Tier baseline** (`AscensionEffectContributor`, shipped slice `prog-2`) is a *second* contribution on the same contribute-on-read seam — additive; don't rebuild the contributor to add it. `AscensionConstants.TierBaselineStep × IAscensionSystem.GetTier(entity)` folds alongside `ProgressionEffectContributor` with zero interface change to `IStatSystem`/`EffectSystem`.
- **`ProgressionSystem` reads raw component data for its own inputs, never `IStatSystem`/`IEffectSystem`.** `ProgressionEffectContributor` is a registrant *on* `IEffectSystem`'s contributor list; if `ProgressionSystem` itself called `IStatSystem.Get` (which folds that same contributor list), it would close a DI cycle back through its own contributor. This generalizes to any future `IEffectContributor`: a contributor's backing system may consume raw component data, but never a computed value from the seam it feeds. Discovered while wiring `prog-1` — see [`progression-system.md`](../../../docs/features/progression/progression-system.md#anti-grind-proxy-reads-raw-attributes). **Confirmed a second time in `prog-2`:** `AscensionSystem` reads only raw `AscensionComponent.Tier` via `EntityService`, never `IStatSystem`/`IEffectSystem` — the same guardrail, now with two independent precedents (Progression, Ascension).
- **The anti-grind proxy's backend is `IPowerBudgetSystem` (shipped slice `prog-3`), not an inline attribute sum.** `ProgressionSystem.GetEffectivePower` builds a **raw-attribute** `PowerSnapshot` (`Mind`/`Body`/`Spirit`/`Attunement` straight off `AttributesComponent`) and calls `IPowerBudgetSystem.Estimate(snapshot)` (no tier — raw only). Injecting `IPowerBudgetSystem` (a **core** system, `Core/Systems/`) introduces no cycle — the guardrail above is about the *snapshot values* staying raw, not about the oracle being un-injected. Balance tuning for the anti-grind ratio now lives in `PowerBudgetConstants.Weights` (co-located with `PowerBudgetSystem`), not a bespoke sum — see [`power-budget-system.md`](../../../docs/features/progression/power-budget-system.md).

## Related

- [progression-system.md](../../../docs/features/progression/progression-system.md) — the as-built design; [progression.md](../../../docs/features/progression/progression.md) — the feature doc.
- [program brief](../../../docs/implementation-plans/progression-and-balance.md) — the durable design for slices 2–5 (mechanism/tuning/generalization, the five-slice map).
- [add-handler](../add-handler/SKILL.md) · [add-event](../add-event/SKILL.md) · [add-command](../add-command/SKILL.md) · [add-domain-system](../add-domain-system/SKILL.md) · [add-tests](../add-tests/SKILL.md) — the sub-patterns each recipe composes.
- [checklist](../../../docs/architecture/checklist.md) — INV-24 / INV-5 / INV-8 / INV-19 / INV-26 (the rules; cite, don't restate).
