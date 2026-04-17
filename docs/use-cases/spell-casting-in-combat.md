# Spell Casting in Combat

**Status:** planned
**Actors:** Player (caster), Mob or Player (target)
**Module:** `Core/Modules/Spell/`

## Description

During combat, a caster invokes a known spell against a valid target. Resource pools are consumed, the spell resolves (damage, debuff, heal, etc.), skill rolls happen, and combat continues normally.

## Preconditions

- Caster knows the spell (entry in `SkillComponent` or `SpellbookComponent`)
- Caster has sufficient resource in `PoolsComponent` (energy/mana) for the spell cost
- A valid target is in range and is of a valid target type
- Caster state allows casting (not silenced/interrupted)

## Postconditions

- Resource pool debited on caster
- Spell effect applied to target (damage, effect, heal)
- Caster's casting skill may improve
- Combat messages dispatched to caster, target, and witnesses
- Combat pulse continues

## Main flow

1. `cast <spell> <target>` command → `SpellHandler`
2. `SpellSystem.ValidateCast(caster, spellId, target)` — gates cost, range, state
3. `PoolsSystem.Spend(caster, cost)`
4. `SpellSystem.Resolve(caster, spellId, target)` → `SpellResult { damage, effects, heal }`
5. If `SpellResult.damage > 0`: `CombatSystem.ApplyDamage(target, damage)` → `DamageEvent`
6. For each effect: `EffectTracker.ApplyEffect(target, effect)` → `SpellEffectAppliedEvent`
7. `SkillSystem.TryImprove(caster, spellSkillId, difficulty)`
8. `SpellHandler` publishes `SpellCastEvent`
9. `NotificationHandler` messages involved parties

## Events fired

- `SpellCastEvent` — envelope (caster, spell, target)
- `DamageEvent` — if offensive
- `SpellEffectAppliedEvent` — per applied effect
- `SkillImprovedEvent` — if skill ticked

## Systems / handlers

- `SpellSystem`, `PoolsSystem`, `CombatSystem`, `EffectTracker`, `SkillSystem`
- `SpellHandler` — orchestrator
- `NotificationHandler`

## Design notes

- **Shared effect channel with potions.** Both spells and potions emit `SpellEffectAppliedEvent` so effect-reactive systems don't care about the source.
- **Resolve first, apply second.** `SpellSystem.Resolve` returns a pure result; the handler publishes events. Keeps spell math unit-testable.
- **Interrupts** (stun, silence) are `EffectsComponent` flags checked by `SpellSystem.ValidateCast` — no special-case handler code.

## Related

- [combat-pulse-processing.md](combat-pulse-processing.md)
- [potion-consumption.md](potion-consumption.md) — shared effect channel
