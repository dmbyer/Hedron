# Phase 3 Slice 11-a — Ability Substrate

**PR:** #105 · **Spec:** [`../../implementation-plans/ability-substrate.md`](../../features/abilities/abilities.md)

> Ledger backfilled retroactively (merged in #105 without a `done.md`/`completed/` entry at the time).

## Outcome

Introduced the unified **ability model** — the single primitive covering both skills and spells — and the domain system that learns, activates, and queries abilities. `AbilityDefinition` carries Kind (Skill | Spell), Activation mode, multi-pool Costs (HP/Mana/Stamina/Astra permitted), Targeting, and an Effects list. `IAbilitySystem.Activate` validates state/cooldown/costs atomically, spends costs, ticks a cooldown, and applies effects through `IEffectSystem.Apply`. Passive `WhileKnown` abilities derive their stat contributions on read via the new `IEffectContributor` seam — the first consumer and canonization of INV-24.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `AbilityDefinition`, `AbilityKind`, `Activation`, `Targeting`, `ResourceCost` | `Core/Modules/Abilities/` | data, not code; one activation pipeline |
| `IAbilitySystem`/`AbilitySystem` (domain) | `Core/Modules/Abilities/Systems/` | Activate, Learn, Teach, GetKnown, AdvanceCooldowns |
| `AbilitiesComponent { Known, CooldownRemaining }` (`[Persistent]`, converter writes Known only) | `Core/ECS/Components/` | cross-cutting; `AbilitiesComponentJsonConverter` mirrors `EffectsComponentJsonConverter` |
| `IAbilityRegistry`/`AbilityRegistry` | `Core/Modules/Abilities/` | hardcoded starter set: `kick`, `empower`, `mend`, `blood_pact`, `toughness` |
| `IEffectContributor` (core seam, INV-24) | `Core/Modules/Effects/Systems/` | DI-collected by `EffectSystem`; abilities module implements it |
| `AbilityEffectContributor` | `Core/Modules/Abilities/` | derives `WhileKnown` StatModifier passives on read; no stored state |
| `AbilityCooldownTickHandler` (`HeartbeatTickEvent`, p=20) | `Core/Modules/Abilities/Handlers/` | calls `AdvanceCooldowns(elapsed)`; no events published |
| `teach` admin command | `Core/Modules/Abilities/Commands/` | admin boundary save (INV-22 case b) + `AbilityTaughtByAdminEvent` |
| `useability` admin command | `Core/Modules/Abilities/Commands/` | end-to-end test path; retained for admin use after 11-b |
| `abilities`/`skills`/`spells` player commands | `Core/Modules/Abilities/Commands/` | inspect known abilities with cooldown status |
| `AbilityActivatedEvent`, `AbilityLearnedEvent`, `AbilityTaughtByAdminEvent` | `Core/Modules/Abilities/Events/` | thin, past-tense |
| `AbilitiesModule` | `Core/Modules/Abilities/AbilitiesModule.cs` | DI entry point |
| Flow 24 (ability activation) | `docs/architecture/flows/flow-24-ability-activation.md` | new canonical flow |
| Flow 16 amended | `docs/architecture/flows/flow-16-heartbeat-tick.md` | `AbilityCooldownTickHandler` added |
| `IEffectSystem.GetModifiers`/`GetActive` extended | `Core/Modules/Effects/Systems/EffectSystem.cs` | sums contributor outputs alongside stored effects |

## Spec-review provenance

Spec gate (spec-mode) ran before implementation. The INV-24 contributor seam and the "WhileKnown is derived, not stored" invariant were the primary architectural scrutiny points; both passed. Open questions on starting ability set (deferred to 11-b) and passive magnitude (via `PowerScaling`) resolved with the owner.

## Notable design points

- **One system, not two.** Reconciles the planned `ISkillSystem`/`ISpellSystem` into `IAbilitySystem` (INV-15). Skills and spells differ by `Kind` + `Costs` data, not class hierarchy.
- **`IEffectContributor` — INV-24.** The seam is reused-by-design (equipment-derived effects fold in the same way); it is not a one-off for abilities. Canonized as the third composition shape in `01-layers.md`.
- **Atomic multi-pool cost.** All costs checked before any is spent; no partial spend is possible.
- **Cooldowns are transient.** `CooldownRemaining` is excluded from the `AbilitiesComponent` JSON converter; on reconnect, all cooldowns reset to ready.
- **Deferred:** YAML ability authoring, per-mob ability grants, hit/miss, action economy, triggered/conditional passives, non-Self/Target targeting modes, `GrantAbility` effect kind.

## Deviations from the use-case doc

None — shipped per spec.

## Follow-ups unlocked

- **11-b:** `IAbilitySystem.Activate`, `AbilityKind`/`Activation`, `AbilitiesComponent.Known`, and `IAbilityVerbResolver` (core seam) are the seams 11-b builds on.
- **11-c:** resource regeneration can now make ability costs recoverable.
- A future equipment-bonus contributor implements `IEffectContributor` exactly as `AbilityEffectContributor` does.
