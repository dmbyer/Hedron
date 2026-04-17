# Combat Pulse Processing

**Status:** partial
**Actors:** System, all combatants
**Module:** `Core/Modules/Combat/`

## Description

On a recurring combat pulse, every entity in a combat session performs its auto-attack against its current target. Damage is calculated, hits land or miss, skills improve, and dead entities are removed.

## Preconditions

- One or more combat sessions are active
- `TimeSystem` is running the combat tick scheduler
- Each combatant has at least one valid target and is not stunned/incapacitated

## Postconditions

- Every eligible combatant resolved exactly one attack this pulse
- Damage applied; HP updated; `DamageEvent`s published
- Deaths resolved ([player-death-and-respawn.md](player-death-and-respawn.md), [mob-death-and-loot.md](mob-death-and-loot.md))
- Skill-improvement rolls processed
- Combat sessions with no opponents close

## Main flow

1. Combat pulse tick fires via `TimeSystem`
2. `CombatHandler` iterates active combat sessions
3. For each session, for each combatant:
   - Resolve current target via `CombatSystem.GetTarget(combatant)`
   - `CombatSystem.ResolveAutoAttack(attacker, target)` → `AttackResult` (hit/miss/damage) — see [combat-skill-vs-defense.md](combat-skill-vs-defense.md)
   - If hit, `CombatSystem.ApplyDamage(target, damage)` → `DamageResult`
   - Handler publishes `DamageEvent`; if killed, publishes `PlayerDeathEvent` or `MobDeathEvent`
   - `SkillSystem.TryImprove` on attacker and defender
4. After all participants resolved, `CombatHandler` closes finished sessions and fires `CombatEndedEvent`
5. `NotificationHandler` batches per-combatant messages into status lines

## Events fired

- `CombatPulseEvent` _(planned)_ — envelope marker for the pulse
- `DamageEvent` — per hit
- `PlayerDeathEvent` / `MobDeathEvent` — on kill
- `SkillImprovedEvent` — on skill tick
- `CombatEndedEvent` — when sessions close

## Systems / handlers

- `CombatSystem`, `SkillSystem`, `DiceSystem`, `TimeSystem`
- `CombatHandler` — orchestrator
- `NotificationHandler`, `PersistenceHandler`

## Design notes

- **One pulse per session, not per combatant.** Iterating sessions keeps ordering deterministic and lets the handler short-circuit when a session ends mid-tick.
- **Attack resolution is pure:** `CombatSystem.ResolveAutoAttack` returns a result; the handler publishes events. Keeps core free of I/O.
- **Notification batching** — collect messages during the pulse, flush once, so a player sees one combined status update rather than a flood.

## Related

- [combat-skill-vs-defense.md](combat-skill-vs-defense.md)
- [spell-casting-in-combat.md](spell-casting-in-combat.md)
- [player-death-and-respawn.md](player-death-and-respawn.md)
- [mob-death-and-loot.md](mob-death-and-loot.md)
