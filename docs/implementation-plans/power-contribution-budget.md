# Power contribution budget (Phase 5 gap slice 5c, R30)

- **Status:** planned — **framing seed** (architecture-advisor intake, 2026-11-04). **Blocking:** Phase 5 content drops are parked until this ships (resolved decision 4). The mechanism itself is an **open design question** (see Open questions, OQ1): this slice runs `/refine` → `/advise` (resume) → `/new-plan` → spec gate → build → code gate before the B1 drop resumes.
- **Actors:**
  - Designer/administrator: reads an item's (and later an ability's) cell in the editor, `power <item>`, the Integrity audit and the conformance fitter.
  - System: the band-drift audit and the dev-world CI guard (`BandDriftAuditIsClean`).
- **Module:** the power oracle (`Core/Systems/PowerBudget*`, core tier) and its tunables; consumers in `Core/Modules/BalanceInspection/` (audit, conformance fitter, readouts), `Core/Modules/Items/` (projection), `Hedron.Web` (ItemEditor readout, Standards page). Exact placement is the planner's, under the seam decisions below.

**Description.** The power oracle judges an **item** by classifying the item's own stat bonuses on the **whole-character** power scale. A fresh Tier-0 character is 386 power and the Tier-0 bands are 386–425 / 426–465 / 466–505, but a +2 attack-power sword is 16 power and a +1 defense vest is 8. Anything below a tier's reference power floors to Band 1, so **every realistic Tier-0 item reads (0,1)** whatever it is tagged, and every Tier ≥ 1 item reads (0,1) too (the tier term adds only 120). The result:

- the ItemEditor readout and `power <item>` can't size gear;
- the band-drift audit says nothing useful about items: (0,1) tags are trivially "exact", (0,2) tags drift 1 (inside tolerance, but the readout flags a mismatch), and **(0,3) and Tier ≥ 1 tags drift ≥ 2 and are flagged**. That fails the dev-world CI guard and so **blocks the B3 drop**;
- the conformance fitter, asked to fit a (0,2) item, would inflate it to ~426 power (≈ +53 attack power).

The same shape will recur for **skills and spells** (power-model rule (b): "a richer source that estimates its own contribution"), so the maintainer asked for **one simple mechanism that balances raw character stats, skills/spells, and item/gear bonuses together**, rather than an item-only patch.

Found by the Phase 5 B1 drop's checkpoint-1 derivation ([phase-5-content-drops.md](phase-5-content-drops.md#blocking-finding-power-contribution-budget)).

## Design notes

*(Durable rationale; survives into the shipped feature docs.)*

- **Mechanism vs. consequence.** The defect is in the *measurement* (what scale a partial contribution is classified on), not in the audit, the fitter or the readouts that consume it. The fix belongs at the single classification seam those consumers share (today `IPowerBudgetSystem.Classify`/`TargetRange` plus `IItemPowerProjectionSystem`), so all of them change together (INV-19). It never belongs in a consumer, which would let two surfaces disagree.
- **The family: a power source is a contribution to a character, not a character.** A whole stat block (a character, a mob) is measured on the character scale, and that stays correct. A *part* (an item's bonuses, later an ability's estimated contribution, an effect's modifiers) is a **contribution** and must be judged against **its share of a cell's budget**, not against a whole character's range. This is the general case: items are its first instance, and skills/spells are the next.
- **Snapshot-only stays intact** ([power-model.md](../design/power-model.md), INV-2). Whatever the budget is, it is a pure derivation from `PowerBudgetTunables` (plain data), and the oracle learns no domain concept (no "item", "ability" or "slot" type). A contribution kind, if one is needed, is a plain key in the tunables, the same way `ScoreId` keys the weights.
- **Numbers are derived, not authored per cell** (resolved decision 2). Like `TargetRange` today, a cell's contribution budget is computed from a few tunables with compiled defaults, editable on the Standards page; no 21-cell authoring burden.
- **One range per contribution kind, shaped for slot classes** (resolved decision 3). Gear gets one per-item range per cell now. The seam is keyed so a slot-class dimension (weapon / armor / accessory) can arrive as data later without reshaping it; the split is backlogged.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Placement

| Piece | Home | Disposition |
|---|---|---|
| Contribution-budget derivation (per cell, per contribution kind) | Core power oracle (`PowerBudgetMath` / `IPowerBudgetSystem`), fed by new plain-data knobs on `PowerBudgetTunables` | **Build now** (exact API: planner, after OQ1) |
| Item classification against the gear budget | `IItemPowerProjectionSystem` → the new oracle call; consumers switch over | **Build now** |
| Consumers: ItemEditor readout, `power <item>`, `BalanceAuditSystem` (items), `TemplateConformanceSystem.FitItem`, `SimulationPrefill.ForItem` cell resolution | their existing homes | **Build now** (one-call switch each; INV-19) |
| Tunables knobs on the balance-standards document + Standards page + store validation/defaults | `BalanceInspection/Standards`, `Hedron.Web` Standards page | **Build now** (INV-18: a new knob ships its authoring surface) |
| Ability/spell contribution budget | same seam, a second contribution kind | **Shape for later**: keyed so it joins as data when ability power lands (backlog: ability rank granting power, equipped-ability roster) |
| Slot-class split of the gear budget | same seam, an extra key | **Defer** (backlog entry) |
| Mob/character whole-block classification | unchanged | **No change** |

### The candidate shapes (OQ1: to dig into before planning)

1. **Cell headroom split by share** *(advisor's leading candidate).* A cell's headroom over the tier's unequipped reference power (Tier 0: Band 1 = 0–40, Band 2 = 40–80, Band 3 = 80–120) is the power a character in that cell has *beyond* base stats. A tunable share table splits that headroom among contribution kinds (e.g. progression, gear, abilities), and a per-kind expected count divides the gear share per item. An item's power is classified against the per-item gear range. *Pros:* one model across stats, gear and skills; a character built from in-band parts lands in-band by construction; all numbers derive from a handful of knobs. *Cons:* the shares and counts are new design numbers to ground; headroom is 0 at the bottom of Band 1, so the lowest cell needs a floor.
2. **Reference-plus-contribution.** Judge a part as "the tier's reference build wearing it" on the character scale. *Pros:* no new numbers. *Cons:* one item barely moves a character (+16 of a 40-wide band), so items still nearly all read Band 1; it doesn't solve the problem.
3. **Exclude parts from drift; judge them by simulation only.** *Pros:* cheapest. *Cons:* the editor readout, audit and fitter go silent on items, and the sim needs an armed-reference composition per cell anyway.

Option 1 also gives the reference builds' `gearBonuses` a meaning: a cell's reference gear should spend that cell's gear share, which lets the standards store **warn** when they disagree (a natural follow-on, not required here).

### Observers, contributors, ordering

- No events, handlers or heartbeat work. The oracle is pure and called on read.
- **Contributors:** none at runtime. Live effective power (`IRelativePowerSystem`, `consider`, anti-grind) already folds gear through `IStatSystem` and is unchanged; this slice changes how a *part* is classified, not how a live character is measured.

### Invariants in tension

- **INV-2 / power-model.md.** The new budget must be a pure function of plain-data tunables; a contribution "kind" is a data key, never a domain type in the oracle. `ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency` must stay green unedited.
- **INV-19.** Every item-classifying consumer switches in the same slice; a readout that still uses the old scale is a defect.
- **INV-18.** New tunables ship with Standards-page authoring and store validation.
- **INV-25 / balance maintenance contract.** Existing goldens that pin item drift (e.g. `BalanceAuditSystemTests.Audit_flags_an_item_whose_drift_exceeds_tolerance`) move deliberately, as a re-pin under `balance-tuning`, called out in the PR.
- **INV-27.** One home for the budget formula (the oracle), the way `TierSpan()` is the one home for the tier span today.

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-11-04. Do not relitigate.)*

1. **Scope is the family, not items alone.** The mechanism must balance raw character stats, skills/spells and item/gear bonuses together. Rejected: an item-only patch.
2. **Budget numbers are derived from tunables** (new plain-data knobs with compiled defaults, editable on the Standards page). Rejected: authored per cell; derived from reference-build `gearBonuses` (dev-world's pinned compiled standards have empty gear).
3. **One per-item gear range per cell, shaped for slot classes later.** Rejected: per-slot-class budgets now.
4. **Blocking.** The B1 drop stays parked until this slice merges; B1 then resumes in a local desktop session with the browser pane and authors gear against the corrected readout. Rejected: blocking only B3.

## Open questions

- **OQ1 (user, design dig — load-bearing).** Which mechanism: the headroom-share model, or something simpler? Questions to settle in `/refine`:
  - Is "headroom over the unequipped reference" the right unit, and what is the Band-1 floor for the lowest cell?
  - Which contribution kinds exist on day one (progression? gear? abilities?), and what are their default shares?
  - Does the per-item gear range divide by an expected item count, and what is that count at Tier 0?
  - Should a whole character built from in-band parts be guaranteed to classify in-band (a consistency check the standards store could warn on)?
- **OQ2 (planner).** API shape: a new `ClassifyContribution(kind, power, tier)` / `ContributionRange(kind, tier, band)` pair, or a parameter on the existing calls.
- **OQ3 (planner).** Whether the dev-world CI guard keeps reading drift through `BalanceAuditSystem` unchanged (expected: yes, the audit switches internally).
- **Related, not this slice:** the equal-cell outcome target (50% ± 10%) is considered too tight and needs redesign ([backlog](../roadmap/backlog.md)); this slice doesn't touch outcome tolerances.

**Next:** run `/refine` on OQ1 with the maintainer, then resume `/advise` if the seam moves, then `/new-plan` (the `implementation-planner` agent) on this file. It will extend this seed into the full plan, folding the Architecture brief's seam decisions into Design notes and the ground-rule-9 cross-cutting audit. Then the **spec-review gate** (`architecture-reviewer`, spec mode) before any code.
