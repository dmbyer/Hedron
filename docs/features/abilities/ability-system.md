# Ability System

> Domain system managing the full ability lifecycle — learn/teach, multi-cost atomic activation, cooldown tracking, and `WhileKnown` passive derivation via the `IEffectContributor` seam. **Authoring checkpoint:** slices 11-a (substrate) + 11-b (invocation). Living document.

## What it is / does

`AbilitySystem` is a **domain-tier** system that owns the ability lifecycle for players and mobs. It handles three responsibilities:

1. **Knowledge** — tracking which abilities an entity knows (`Learn` / `Teach` / `GetKnown` / `IsKnown`).
2. **Activation** — validating and executing an ability (`Activate`): entity-state check → cooldown check → atomic multi-pool cost check → spend costs → apply effects → set cooldown → return a structured result.
3. **Cooldown advancement** — `AdvanceCooldowns(elapsed)` decrements all non-zero `CooldownRemaining` entries each heartbeat tick.

It sits in the domain tier (`Core/Modules/Abilities/Systems/`), depends downward on the core `IEffectSystem` and peer domain systems `IAttributeSystem` / `IEntityStateService` (legal INV-1/INV-2 calls), and never touches the event bus or persistence (INV-5).

## How it works

### The `AbilityDefinition` model

| Field | Type | Notes |
|---|---|---|
| `Id` | `AbilityId` (string) | Registry key; also the bare-verb for skills |
| `Name` | `string` | Display name |
| `Kind` | `AbilityKind` (`Skill \| Spell`) | Discriminator; skills are bare-verb, spells go through `cast` |
| `Activation` | `Activation` (`Active \| Passive \| Triggered`) | Only `Active` abilities are directly invocable |
| `Costs` | `IReadOnlyList<ResourceCost>` | Multi-pool; `{ResourceType, Amount}`. HP is a valid cost |
| `Targeting` | `Targeting` (`Self \| Target`) | `Room`/`Group`/`AspectArea` are deferred |
| `Effects` | `IReadOnlyList<string>` | Effect-registry ids — the only place "what happens" lives |
| `Aspect` | `AspectComposition?` | Carried by offensive abilities for aspect-typed damage |
| `CooldownSeconds` | `float` | Per-ability; no global cooldown |
| `Trigger?` / `Curve` / `LearnReqs` | — | Carried, not yet wired (deferred) |

Skills and spells differ by `Kind` + `Costs` data, not by code class hierarchy. One activation pipeline covers both (INV-15).

### Activation pipeline

`Activate(actorEntityId, abilityId, targetEntityId?, resolveOffensiveExternally)` validates in order:

1. Ability exists in `IAbilityRegistry`. Fails `UnknownAbility`.
2. Actor's `AbilitiesComponent.Known` contains the id. Fails `NotKnown`.
3. `Activation == Active`. Fails `NotActivatable` for `Passive` / `Triggered`.
4. `IEntityStateService.IsInState(actor, Incapacitated)` is false. Fails `StateBlocked`.
5. `CooldownRemaining[abilityId] == 0`. Fails `OnCooldown`.
6. All costs affordable — checked atomically before any spend. Fails `InsufficientResources` (carries the first failing `ResourceType`).

On success: spends every cost via `IAttributeSystem.SetCurrentX`; sets `CooldownRemaining[abilityId] = CooldownSeconds`; calls `IEffectSystem.Apply` for each effect id. When `resolveOffensiveExternally: true`, the offensive damage effect is skipped and its computed Power is returned as `AbilityActivationResult.OffensivePower` — the caller (`AbilityInvocationPipeline`) routes it through `ICombatSystem.ResolveAbilityStrike` with defense mitigation. Returns `AbilityActivationResult { Outcome, AbilityId, AppliedEffects, Spent, CooldownSeconds, FailReason?, OffensivePower? }`.

### Storage and persistence

`AbilitiesComponent { Known: List<string>, CooldownRemaining: Dictionary<string, float> }` lives on both player and mob entities. It is `[Persistent]` with `AbilitiesComponentJsonConverter` that serializes **only** `Known` — cooldowns are transient by design and reset to ready on load. Mob entities carry no `PersistentEntity`, so the component is never written for mobs regardless (INV-23).

### `WhileKnown` passive derivation — the contributor seam

Passive abilities (e.g. `toughness`: `+HpMax` while known) contribute to the stat pipeline **derived on read, never stored** (INV-24).

`EffectSystem` (core) cannot reference the Abilities module (INV-2). The seam is a **core-owned port** `IEffectContributor` (`Core/Modules/Effects/Systems/`), DI-collected by `EffectSystem`. `GetModifiers`/`GetActive` sum stored effects **plus** every contributor's output.

**`AbilityEffectContributor`** (Abilities module, implements `IEffectContributor`) reads `AbilitiesComponent.Known` + `IAbilityRegistry` and returns `StatModifier` contributions for every known `Passive` ability whose effects have `Lifetime == WhileKnown`. No stored state; no invalidation. The dependency arrow points legally: Abilities domain → Effects core interface.

This is the same seam equipment-derived effects will use (see [`../effects/effect-system.md#the-contributor-seam`](../effects/effect-system.md#the-contributor-seam)).

### Cooldown tick

`AbilityCooldownTickHandler` (priority 20, `HeartbeatTickEvent`) calls `IAbilitySystem.AdvanceCooldowns(elapsed)`. Orchestration only — no domain logic in the handler (INV-1). No events published.

### Player invocation routing

The player path is handled by `AbilityInvocationPipeline` (shared by `CastCommand` and `SkillInvocationCommand`):

| Step | Detail |
|---|---|
| Target resolution | `Self` → actor; explicit token → `MobInRoomResolver`; `InCombat` + no token → `CombatStateComponent.OpponentEntityId`; offensive + no token + not in combat → "{ability} whom?" prompt |
| Combat entry | If offensive and actor not already fighting the target: `TryEnterState(InCombat)` on both, `StartCombat`, publish `CombatStartedEvent` |
| Activation | `IAbilitySystem.Activate(actor, abilityId, target, resolveOffensiveExternally: true)` |
| Offensive strike | `ICombatSystem.ResolveAbilityStrike(actor, target, OffensivePower)` — defense-mitigated; result returned via `AbilityStrikeResolvedEvent` to `AbilityStrikeHandler` |
| Narrative | `AbilityInvocationHandler` subscribes to `AbilityActivatedEvent` (priority 80) for first/third-person room broadcast |

**Dispatcher Phase 3** (skill bare-verb): `CommandDispatcher` consults `IAbilityVerbResolver.TryResolve(actorId, verb)` after both command phases miss. A unique Active Skill match routes to `SkillInvocationCommand`, which delegates to the pipeline. A real command verb always wins (INV: command resolution is never shadowed by ability resolution).

**`cast`** (spells): `CastCommand` resolves the spell token via `KnownSpellResolver` (prefix-matched against known Active Spells), then delegates to the same pipeline.

## Interface

The seam self-documents in code — behavior described here, not signatures:

- [`IAbilitySystem.cs`](../../../Core/Modules/Abilities/Systems/IAbilitySystem.cs) — `Activate` / `IsOffensive` / `Learn` / `Teach` / `GetKnown` / `IsKnown` / `GetCooldownRemaining` / `GetCooldowns` / `AdvanceCooldowns`. Pure: returns results, never touches the event bus or persistence, never calls a handler (INV-5).
- [`AbilityVerbResolver.cs`](../../../Core/Modules/Abilities/AbilityVerbResolver.cs) — `IAbilityVerbResolver` seam (`TryResolve` / `GetInvocableVerbs`); used by `CommandDispatcher` Phase 3. Read-only; no mutations or events.
- [`AbilityEffectContributor.cs`](../../../Core/Modules/Abilities/AbilityEffectContributor.cs) — implements `IEffectContributor`; derives `WhileKnown` passive modifiers on read.

## Considerations

- **Atomic cost spend.** All costs are checked before any are spent — no partial spend possible even with multi-pool costs (HP + Mana, etc.).
- **`resolveOffensiveExternally`.** When `true`, `Activate` skips applying the offensive damage effect and returns its raw magnitude as `OffensivePower` for the caller to route through `ICombatSystem.ResolveAbilityStrike`. This keeps damage mitigation in `ICombatSystem` only (INV-8). Admin `useability` always uses `false` (raw apply, unmitigated).
- **No action economy this phase.** An invoked ability fires immediately, cooldown-gated; the heartbeat auto-attack continues in parallel. One-ability-per-round / action-point economy is acknowledged debt tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).
- **Cooldowns are transient.** `CooldownRemaining` is excluded from the JSON converter; on reconnect all cooldowns reset to ready.
- **Determinism.** No randomness in the substrate; any future probabilistic formula routes through `IRandom` (INV-26).

## Extensibility

- **New modifier sources** for passives (equipment, auras, area effects) fold in through the `IEffectContributor` port with no `AbilitySystem` or `EffectSystem` change (same seam).
- **Triggered abilities** (`Activation == Triggered`) are carried on the definition but the reactive evaluation hook is deferred; `Activate` rejects them as `NotActivatable`.
- **YAML ability authoring** (promotion from the hardcoded `AbilityRegistry`) is Category-3 content work tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).
- **Per-mob ability grants** and mob AI ability usage are deferred; `AbilitiesComponent` is uniform across player and mob so the data model is already compatible.
- **Hit/miss rolls, action economy, `Room`/`Group`/`AspectArea` targeting** — all named future concerns; the current shape is forward-compatible.

## Related

- [`abilities.md`](abilities.md) — the holistic feature view and player-facing surfaces.
- [`../../architecture/flows/flow-24-ability-activation.md`](../../architecture/flows/flow-24-ability-activation.md) — the abilities journey (activation · bare-verb · offensive-opens-combat).
- [`../effects/effect-system.md`](../effects/effect-system.md) — `EffectSystem` and the contributor seam this system is the first consumer of.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `AbilitySystem` / `AbilityEffectContributor` / `AbilitiesComponent` catalog rows.
- [`../../roadmap/completed/slice-11a-ability-substrate.md`](../../roadmap/completed/slice-11a-ability-substrate.md) · [`../../roadmap/completed/slice-11b-ability-invocation.md`](../../roadmap/completed/slice-11b-ability-invocation.md) — as-built records and decision history.
