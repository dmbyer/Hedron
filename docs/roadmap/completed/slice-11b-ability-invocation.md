# Phase 3 Slice 11-b — Ability Invocation & Combat Targeting

**PR:** #106 · **Spec:** [`../../use-cases/ability-invocation.md`](../../use-cases/ability-invocation.md)

> Ledger backfilled retroactively (merged in #106 without a `done.md`/`completed/` entry at the time).

## Outcome

Turned the 11-a substrate into the player-facing invocation surface. Players invoke Active Skills as bare verbs (`kick`, `ki` → `kick`) and Active Spells via `cast <spell> [target]`. Both paths converge on `IAbilitySystem.Activate` through a shared `AbilityInvocationPipeline`. Offensive abilities open combat if the actor is not already fighting and route damage through the existing combat defense math (`ICombatSystem.ResolveAbilityStrike`), so ability kills reach the same death path as melee. Starting abilities are granted from config at character creation.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `IAbilityVerbResolver` (core seam) | `Core/Commands/` | read-only; dispatcher third-phase fallback |
| `AbilityVerbResolver` | `Core/Modules/Abilities/` | implements the core seam; filters Skill + Active + prefix-match |
| `SkillInvocationCommand` (internal) | `Core/Modules/Abilities/Commands/` | routes from dispatcher fallback |
| `CastCommand` / `c` | `Core/Modules/Abilities/Commands/` | `Partial`, resolves via `KnownSpellResolver` |
| `KnownSpellResolver : IArgumentResolver` | `Core/Modules/Abilities/Resolvers/` | prefix-matches known Spell-kind Active abilities |
| `AbilityInvocationPipeline` | `Core/Modules/Abilities/Commands/` | shared target-resolution + combat-entry + Activate + strike orchestration |
| `ICombatSystem.ResolveAbilityStrike` | `Core/Modules/Combat/Systems/` | routes offensive ability damage through defense math |
| `AbilityStrikeResolvedEvent` | `Core/Modules/Combat/Events/` | unconditional thin event; terminal outcomes in handler |
| `AbilityStrikeHandler` | `Core/Modules/Combat/Handlers/` | publishes `CombatRoundEvent` + conditional `CombatEndedEvent` |
| `AbilityInvocationHandler` | `Core/Modules/Abilities/Handlers/` | output fan-out for `AbilityActivatedEvent` |
| `MobInRoomResolver : IArgumentResolver` | `Core/Modules/Combat/Resolvers/` | extracted from inline code (INV-19 3rd-consumer) |
| `CharacterDefaults:StartingAbilities` config + creation hook | `Server/` + `Core/Modules/Account/` | new characters receive starter abilities at creation |
| `IAbilitySystem.IsOffensive` helper | `Core/Modules/Abilities/Systems/` | reads effect targets to classify offensive vs. non-offensive |
| Flow 25 (skill bare-verb invocation) | `docs/architecture/flows/flow-25-skill-verb-invocation.md` | new canonical flow |
| Flow 26 (offensive ability opens combat) | `docs/architecture/flows/flow-26-offensive-ability-opens-combat.md` | new canonical flow |
| Flow 24 extended | `docs/architecture/flows/flow-24-ability-activation.md` | player initiators added |

## Spec-review provenance

Spec gate (spec-mode) ran before implementation. The `ResolveOffensiveExternally` flag shape and the dispatcher third-phase resolution were the primary scrutiny points; both passed. Open question on offensive opt-out shape (flag vs. split method) resolved to the flag.

## Notable design points

- **Third dispatcher phase is legal initiator-tier orchestration.** `IAbilityVerbResolver` is a core seam; the dispatcher consults it after both command phases miss. No game rule enters the dispatcher.
- **Command always wins over ability.** Ability resolution only fires when both exact and prefix command phases miss — a registered verb can never be shadowed by a learned ability.
- **`Activate` stays effect-agnostic.** `ResolveOffensiveExternally: true` tells `Activate` to skip applying the offensive damage effect; the caller (pipeline) routes it through `ICombatSystem.ResolveAbilityStrike`. Damage mitigation lives only in `ICombatSystem`.
- **Offensive kills reuse the existing death path.** `AbilityStrikeHandler` publishes `CombatEndedEvent`, which the slice-10 death handlers consume with no new wiring.
- **Acknowledged debt:** in-combat double action (ability + heartbeat melee in same tick) until the action-economy slice; tracked in backlog.
- **Deferred:** hit/miss rolls, distinct ratings, action economy, AoE/Group targeting, triggered reactions.

## Deviations from the use-case doc

None — shipped per spec.

## Follow-ups unlocked

- **11-c:** ability costs are now invocable; resource regeneration makes them recoverable.
- A future **action-economy** slice adds metering over the per-actor action budget per tick.
- A future **combat-depth** slice adds hit/miss rolls and distinct ratings.
