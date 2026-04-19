# Combat: Skill vs Defense Resolution

**Status:** partial
**Actors:** Attacker (Player or Mob), Defender (Player or Mob)
**Module:** `Core/Modules/Combat/`

## Description

Resolves a single auto-attack: hit/miss, raw damage, mitigation from armor and defensive skills, and skill-improvement rolls for both sides. Pure calculation — no state mutation inside resolution.

## Preconditions

- Attacker and Defender are both in the same combat session
- Attacker has either equipped weapon stats in `EquipmentComponent` or innate mob stats in `MobDataComponent`
- Defender has `PoolsComponent`, `AttributesComponent`, and possibly `EquipmentComponent` / `SkillComponent` entries for defense

## Postconditions

- `AttackResult` returned: hit flag, damage before mitigation, final damage, crit flag, contributing skill IDs
- No state mutated during resolution — the handler applies the result

## Main flow (inside `CombatSystem.ResolveAutoAttack`)

1. Gather attacker stats: weapon skill level, attribute bonus, weapon damage range
2. Gather defender stats: dodge/parry/shield skill, armor rating, attribute bonus
3. Compute hit chance: `BaseHit + AttackerBonus - DefenderBonus` (formula lives in `CombatFormulas`)
4. Roll via `DiceSystem.Roll`; miss → return `AttackResult { Hit = false }`
5. Roll weapon damage range (`DiceSystem`)
6. Crit check: compare roll vs `CombatFormulas.CritThreshold`
7. Apply mitigation: `FinalDamage = max(1, RawDamage - ArmorReduction - DefenseSkillReduction)`
8. Return `AttackResult { Hit = true, RawDamage, FinalDamage, IsCrit, AttackerSkillId, DefenderSkillId }`

The calling handler (combat pulse):
- Applies the damage: `CombatSystem.ApplyDamage(defender, result.FinalDamage)`
- Publishes `DamageEvent`
- Calls `SkillSystem.TryImprove` for both `AttackerSkillId` and `DefenderSkillId`

## Events fired

(none from resolution itself; events fire at the pulse level — see [combat-pulse-processing.md](combat-pulse-processing.md))

## Systems / handlers

- `CombatSystem.ResolveAutoAttack` — pure resolver
- `DiceSystem` — random
- `AttributeCalculator` — final modified attributes
- `SkillSystem` — improvement rolls at the call site
- `CombatHandler` — caller and event publisher

## Design notes

- **Pure resolvers are testable.** `ResolveAutoAttack` takes two entities and returns a struct — no I/O, no logging, no events. Unit tests can sweep stat combinations.
- **Formulas live in `CombatFormulas`**, a plain static class — decouples design tuning from orchestration.
- **Skill improvement is called from the handler**, not from within `ResolveAutoAttack`, because improvement is a mutation and the resolver is pure.

## Related

- [combat-pulse-processing.md](combat-pulse-processing.md)
- [../reference/systems.md](../reference/systems.md) — DiceSystem, SkillSystem, AttributeCalculator
