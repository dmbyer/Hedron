# Use Case: Ability Substrate

**Status:** planned
**Actors:** Player, Mob, System, Administrator
**Module:** `Core/Modules/Abilities/` (new — `IAbilitySystem`, `AbilitiesComponent`, `AbilityRegistry`, cooldown tick handler, `AbilityEffectContributor`, admin + inspect commands, events), `Core/Modules/Effects/` (new core-side `IEffectContributor` seam folded into `GetModifiers`/`GetActive`), `Core/Modules/Stats/`/`Core/Modules/Attributes/` (read seams reused, no change)

**Spine:** gameplay-model **S4** — Ability (skills and spells, unified). See [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine B (`AbilityDefinition`, the three `Activation` modes, worked examples, layer fit). **Design lives in the model; this doc is requirements + implementation plan.** This is sub-slice **11-a (substrate)** — the kit primitive, provable without the verb framework; **11-b (invocation/combat-targeting)** and **11-c (resource-regen)** follow.

---

## Description

Introduce the **ability model** — the single unified shape for skills and spells — and the domain system that learns, activates, and queries abilities. An `AbilityDefinition` is a registry-backed record carrying a `Kind` (Skill | Spell, the required discriminator), an `Activation` mode (Active | Passive | Triggered), a **list** of resource `Costs` (multi-pool; HP/blood permitted), a `Targeting` mode (Self | Target), an optional `Aspect`/`Trigger`, an `ImprovementCurve`, `LearnReqs`, and an `Effects[]` list — the **only** place "what happens" lives. Abilities **produce** effects: `IAbilitySystem.Activate` spends every cost through `IAttributeSystem` (clamped pool writes), sets a transient cooldown, and calls `IEffectSystem.Apply` for each of the ability's effects (source = the actor; Power is computed by the effect system from caster stats — no new potency math). Known abilities and per-ability cooldowns live in a single `AbilitiesComponent` on both Player and Mob.

This slice wires the **load-bearing ability mechanics end-to-end without the player verb framework** (mirroring how slice 9-e shipped admin `affect`/`affects` before any real consumer): learn/teach, multi-cost active activation, cooldowns ticked on the heartbeat, and **unconditional passive abilities whose `WhileKnown` effects are derived on read** and folded into the existing `IEffectSystem.GetModifiers`/`GetActive` seam. The remaining ability surface — player-facing verb invocation, dynamic verb registration, `cast`, state-aware/offensive targeting, combat initiation — is **11-b**; resource regeneration + `rest` is **11-c**. `Trigger`/`Aspect`/`Curve`/`LearnReqs` are carried on the definition but not yet wired/resolved/applied; the `GrantAbility` effect kind stays deferred (no MVP consumer).

This slice also reconciles the legacy [`ISkillSystem`](../reference/systems-planned.md) + [`ISpellSystem`](../reference/systems-planned.md) (two systems with duplicated cost/cooldown/targeting logic) into the single `IAbilitySystem` (INV-15), and generalizes the planned [`SkillsComponent`](../reference/components-planned.md) into `AbilitiesComponent`.

---

## Preconditions

- Slice **9-d (stat & resource substrate)** complete: `ScoreId`, `IStatRegistry`, `IStatSystem.Get(entityId, ScoreId)`, `ResourceType` (`Hp`/`Mana`/`Stamina`/`Astra`), and the `IAttributeSystem` clamped pool write seams (`SetCurrentHp`/`SetCurrentMana`/`SetCurrentStamina`/`SetCurrentAstra`, and the matching getters).
- Slice **9-e (effect substrate)** complete: `IEffectSystem.Apply(target, definition, source)` (computes Power from source stats), `GetModifiers`/`GetActive`, `EffectDefinition`/`EffectRegistry`, `EffectLifetime.WhileKnown` (defined, derivation deferred to S4 — landed here), `EffectsComponent`. **This slice supplies the `WhileKnown` derivation the effect doc explicitly deferred to S4.**
- Slice **9-b (heartbeat)** complete: `IHeartbeatService` / `HeartbeatTickEvent` (carries `Elapsed`); `EffectTickHandler` is the precedent for a per-ability decrement handler.
- Slice **9-a (entity state)** complete: `IEntityStateService.IsInState`/`GetStates` (the activation guard reads entity-state, e.g. Incapacitated blocks activation).
- Reused (no change): `EntityService`, `IAttributeSystem`, `IStatSystem`, `IEffectSystem`, `IEntityStateService`, `IEventBus`, `IPersistenceSystem`, command framework + `IOutputWriter`/`PlainMessage`, `AdminRequirement`, `ISessionManager`, `HandlerPriority`, `ComponentSerializer` + `[Persistent]` + the `[JsonConverter]` lifetime-filter precedent (`EffectsComponentJsonConverter`).

---

## Postconditions (requirements)

**Ability model**
- An `AbilityDefinition` record carries: `AbilityId Id`, `string Name`, `AbilityKind Kind` (`Skill | Spell` — **required** discriminator; drives pool/stat tendency and future invocation style), `Activation Activation` (`Active | Passive | Triggered`), **`IReadOnlyList<ResourceCost> Costs`** (each `ResourceCost { ResourceType Resource, int Amount }` — multi-pool; HP/blood is a permitted cost), `Targeting Targeting` (**`Self | Target` only** this slice — `Room`/`Group`/`AspectArea` deferred), `string? Aspect` (carried, not resolved), `TriggerCondition? Trigger` (carried, not wired), `IReadOnlyList<string> Effects` (effect-registry ids — the only place "what happens" lives), `ImprovementCurve Curve` (carried, not applied), `IReadOnlyList<Requirement> LearnReqs` (carried, not applied), and a `float CooldownSeconds`. Enums `AbilityKind`, `Activation`, `Targeting` are co-located with the definition.
- Skill/spell is **data, not code**: which pool the costs draw and how it is invoked is `Kind` + `Costs`, not a class hierarchy. There is **one** activation pipeline.

**Storage & persistence**
- A single `AbilitiesComponent` holds known abilities + transient per-ability cooldown state, on both Player and Mob:
  - `IReadOnlyList<string> Known` (or `List<string>`) — known `AbilityId`s. **Durable.**
  - `Dictionary<string, float> CooldownRemaining` — seconds remaining per ability id; absent/zero = ready. **Transient.**
- `AbilitiesComponent` is `[Persistent]`. A `[JsonConverter]` (`AbilitiesComponentJsonConverter`, mirroring `EffectsComponentJsonConverter`) writes **only** the `Known` list — cooldown state is never serialized (transient by design; resets to ready on load). For **mobs** (world content, no `PersistentEntity`) the component is never written regardless of the converter — correct (INV-23). See **Cross-cutting surfaces stressed → Persistence**.

**System (`IAbilitySystem` — DOMAIN, `Core/Modules/Abilities/Systems/`)**
- `Activate(uint actorEntityId, string abilityId, uint? targetEntityId = null) → AbilityActivationResult` — the one pipeline. In order:
  1. Resolve the definition from `IAbilityRegistry`; fail (`UnknownAbility`) if absent.
  2. Confirm the actor `IsKnown(actor, abilityId)`; fail (`NotKnown`) if not.
  3. Reject `Activation.Passive`/`Triggered` definitions as not directly activatable (`NotActivatable`) — only `Active` abilities are invoked.
  4. Check entity-state via `IEntityStateService` (e.g. Incapacitated blocks); fail (`StateBlocked`, carries reason) if blocked.
  5. Check cooldown (`CooldownRemaining` > 0 → fail `OnCooldown`, carries remaining).
  6. Check **all** costs in the list are affordable against current pools (`IAttributeSystem` getters); fail (`InsufficientResources`, carries the first failing `ResourceType`) if any cost cannot be paid — **atomic: no cost is spent unless every cost can be paid.**
  7. On success: spend **every** cost via the matching `IAttributeSystem.SetCurrentX(current − amount)` clamped setter; set `CooldownRemaining[abilityId] = definition.CooldownSeconds`; for each effect id in `definition.Effects`, look it up in `IEffectRegistry` and call `IEffectSystem.Apply(resolvedTarget, effectDef, source: actor)` (resolvedTarget = actor for `Self`, `targetEntityId` for `Target`); collect the applied effects.
  8. Return `AbilityActivationResult { Outcome, AbilityId, IReadOnlyList<Effect> AppliedEffects, IReadOnlyList<ResourceCost> Spent, float CooldownSeconds, string? FailReason }`.
- `Learn(uint entityId, string abilityId) → bool` — adds to `Known` (idempotent; returns false if already known or unknown id). `Teach(uint teacherEntityId, uint studentEntityId, string abilityId) → bool` — the admin/teacher path; same effect on the student (teacher gate is the command's privilege check, not a system rule this slice).
- `GetKnown(uint entityId) → IReadOnlyList<string>`; `IsKnown(uint entityId, string abilityId) → bool`.
- `GetCooldownRemaining(uint entityId, string abilityId) → float`; `GetReadyAbilities` / cooldown enumeration sufficient for the inspect command.
- `AdvanceCooldowns(TimeSpan elapsed) → void` (or returns the set that became ready) — decrements every entity's `CooldownRemaining`, clamping at 0, removing zeroed entries. Iterates `EntityService.GetAllComponents<AbilitiesComponent>()` (mirrors `EffectSystem.AdvanceTick`).
- **Returns results; never publishes events; never persists; never calls a handler (INV-5, INV-1).** It composes core `IEffectSystem` and peer domain systems `IAttributeSystem` / `IEntityStateService` directly — all legal downward/peer calls (INV-1/INV-2: domain → core, and domain → domain via direct call is permitted for the leaf substrate; no cycle).

**Passive-derivation seam (the key architectural design point — see Design notes)**
- Unconditional `Passive` abilities contribute their `WhileKnown` effects to the stat pipeline **derived on read, never stored** (INV: "WhileKnown is derived, not stored", effect doc).
- `EffectSystem` is **CORE** and must not reference `Core/Modules/Abilities/` (INV-2). The seam is a **core-defined contributor interface** `IEffectContributor` (new, `Core/Modules/Effects/`), DI-collected by `EffectSystem` as `IEnumerable<IEffectContributor>`. `EffectSystem.GetModifiers(entityId, scoreId)` sums its own stored `StatModifier` effects **plus** `contributor.GetModifiers(entityId, scoreId)` across all contributors; `GetActive(entityId)` likewise unions stored effects with `contributor.GetActive(entityId)`.
- The **Abilities module** implements `AbilityEffectContributor : IEffectContributor` (lives in `Core/Modules/Abilities/`; depends on `Core/Modules/Effects/` core types — a legal downward dependency) and registers it in DI. It reads the actor's `AbilitiesComponent.Known` + `IAbilityRegistry`, and for each known `Passive` ability whose effect definitions are `StatModifier` with `Lifetime == WhileKnown`, computes the contribution (Power via the same `PowerScaling`/`IEffectSystem.Apply`-style evaluation, source = the actor itself) and returns it. **No upward dependency**, and it is the same seam equipment-derived effects will later use (effect doc's "same `GetModifiers` call" intent).
- This keeps the dependency a DAG: `StatSystem` (domain) → `IEffectSystem` (core) → `IEffectContributor` (core interface) ← `AbilityEffectContributor` (Abilities domain, implements the core interface). No core→domain reference.

**Heartbeat cooldown tick**
- An `AbilityCooldownTickHandler` subscribes to `HeartbeatTickEvent` (priority `HandlerPriority.Domain` = 20) and calls `IAbilitySystem.AdvanceCooldowns(@event.Elapsed)`. Cooldowns are measured in **seconds** (matching Effect `Elapsed`/`Duration`); decremented by the tick's `Elapsed`. **No global cooldown.** Orchestration only — no domain logic in the handler (INV-1). It publishes nothing this slice (no consumer needs a "cooldown ready" event yet; if 11-b needs one it is additive).

**Tooling (INV-18 — mandatory)**
- A hardcoded `AbilityRegistry` (`IAbilityRegistry`, mirroring `EffectRegistry`) of starter definitions: **≥2 skills, ≥2 spells, ≥1 unconditional passive, and ≥1 multi-pool spell costing HP + Mana**. Concretely (Category-3 balance data, promotion deferred — see Design notes):
  - `toughness` — **Skill, Passive, Self**, effects `[<WhileKnown StatModifier +HpMax>]`, no cost. (Demonstrates the passive-derivation fold.)
  - `kick` — **Skill, Active, Target**, cost `[Stamina N]`, effects `[<Instant -HpCurrent>]`, cooldown.
  - `empower` — **Spell, Active, Self**, cost `[Mana N]`, effects `[empower]` (the existing `Timed StatModifier(+Body)` from `EffectRegistry`), cooldown. (Demonstrates active self-buff: buff applies, Mana spent, cooldown set.)
  - `mend` — **Spell, Active, Self**, cost `[Mana N]`, effects `[<Instant +HpCurrent>]`, cooldown.
  - `blood_pact` — **Spell, Active, Self, multi-pool**, cost `[Hp X, Mana Y]`, effects `[empower]` (or a stronger buff), cooldown. (Demonstrates HP **and** Mana both deducted in one activation.)
  - The effect ids these reference are added to `EffectRegistry` where they don't already exist (`empower` exists; `kick`/`mend`/`toughness` magnitudes are new `EffectDefinition` rows with `Lifetime` via `Duration` — `WhileKnown` for `toughness` needs the registry/`Apply` path to emit a `WhileKnown` effect, see Open questions on the `toughness` derivation detail).
- Admin `teach <player> <abilityId>` — resolves a connected player by name (or entity id) like `affect`, calls `IAbilitySystem.Teach` (or `Learn` on the target), then performs the **admin boundary save** (INV-22 case b: mutate via domain system → `SaveEntityAsync(student)` once → publish `AbilityTaughtByAdminEvent`).
- Player inspect command `abilities` (aliases `skills`, `spells`) — lists the caller's known abilities, each with Kind/Activation/Targeting, costs, and remaining cooldown (`ready` when zero). Reads through `IAbilitySystem` only; no events.
- **Admin/test activation path** so the pipeline is end-to-end testable before 11-b's player verbs exist: admin `useability <abilityId> [target]` (Full match, `AdminRequirement`) — resolves the invoker as actor, optional target, calls `IAbilitySystem.Activate`, then publishes `AbilityActivatedEvent` (and renders the result). This is the admin analogue of `affect`; **see Open questions** on whether it is a dedicated command vs. folding into an existing path.

**Events** (past-tense, thin)
- `AbilityLearnedEvent { uint EntityId, string AbilityId }` — fired when an ability is learned (by `teach`).
- `AbilityActivatedEvent { uint ActorEntityId, string AbilityId, uint? TargetEntityId }` — fired by the activation initiator after a successful `Activate`.
- `AbilityTaughtByAdminEvent { uint AdminEntityId, uint StudentEntityId, string AbilityId }` — admin audit (consumed by `AdminAuditHandler`).
- Effect application already fires `EffectAppliedEvent` from inside the effect path's callers — **but note** `IEffectSystem.Apply` itself does not publish (INV-5); the activation initiator publishes `AbilityActivatedEvent`, and per-effect `EffectAppliedEvent` publication for ability-produced effects is the initiator's responsibility, mirroring `AffectCommand`. **See Open questions** on whether ability-produced effects re-publish `EffectAppliedEvent` per effect.

---

## Main flow

### Flow 1 — Teach (admin grants an ability)
1. Admin types `teach <player> empower`. `TeachCommand` resolves the connected player (by name or entity id, like `affect`).
2. Calls `IAbilitySystem.Teach(adminEntityId, studentEntityId, "empower")` → adds `"empower"` to the student's `AbilitiesComponent.Known` (creating the component if absent). Returns `true`.
3. `TeachCommand` performs the **admin boundary save**: `IPersistenceSystem.SaveEntityAsync(studentEntityId)` (INV-22 case b), then publishes `AbilityLearnedEvent` + `AbilityTaughtByAdminEvent`. Confirmation line to the admin.

### Flow 2 — Activate an active self-buff spell
1. Admin (testing) types `useability empower`. `UseAbilityCommand` resolves the invoker as actor, no target.
2. Calls `IAbilitySystem.Activate(actor, "empower", null)`. The system: resolves the def; confirms known; not passive; entity-state ok; cooldown ready; the single `Mana` cost is affordable → spends it via `IAttributeSystem.SetCurrentMana(current − amount)`; sets `CooldownRemaining["empower"] = CooldownSeconds`; calls `IEffectSystem.Apply(actor, empowerDef, source: actor)` → the `Timed StatModifier(+Body)` is applied. Returns `Outcome = Activated` with the applied effect + spent cost.
3. `UseAbilityCommand` publishes `AbilityActivatedEvent` (and, per the resolved Open question, `EffectAppliedEvent` for each applied effect). Renders "You invoke empower (cost: 10 mana). [+Body buff applied]."

### Flow 3 — Passive read via `score` (the derivation fold)
1. Player has learned `toughness` (Passive, `WhileKnown StatModifier +HpMax`). Player types `score`.
2. `ScoreCommand` reads HP max via `IStatSystem` (→ `IAttributeSystem.GetMaxHp` + `IStatSystem.Get(entityId, ScoreId.HpMax)` folding effect modifiers).
3. `IStatSystem.Get` → `IEffectSystem.GetModifiers(entityId, ScoreId.HpMax)` → sums stored `StatModifier` effects **plus** `AbilityEffectContributor.GetModifiers`, which derives `toughness`'s `+HpMax` from the known-abilities list **on read** (nothing stored). `score` shows the raised HP max. No consumer code changed.

### Flow 4 — Cooldown tick
1. `HeartbeatTickEvent` fires (carries `Elapsed`). `AbilityCooldownTickHandler` (priority 20) handles it.
2. Calls `IAbilitySystem.AdvanceCooldowns(@event.Elapsed)` — for every entity with `AbilitiesComponent`, decrements each `CooldownRemaining` by the elapsed seconds, clamps at 0, removes zeroed entries. No event published.
3. A subsequent `abilities` shows the decremented/`ready` cooldown; a subsequent `useability` of a still-cooling ability fails `OnCooldown`.

### Flow 5 — Multi-pool spend (HP + Mana)
1. Admin teaches `blood_pact` (cost `[Hp X, Mana Y]`), then `useability blood_pact`.
2. `Activate` checks **both** costs affordable against current HP and Mana; if either is short, fails `InsufficientResources` (carries the short pool) and **spends nothing**. If both affordable, spends HP via `SetCurrentHp(current − X)` **and** Mana via `SetCurrentMana(current − Y)` (both clamped), sets cooldown, applies the buff. `score` confirms HP **and** Mana both dropped.

### Flow 6 — Persistence round-trip
1. On the periodic flush (or admin boundary save from `teach`), `ComponentSerializer` serializes the player's `AbilitiesComponent`; `AbilitiesComponentJsonConverter` writes **only** the `Known` list. Cooldowns are not written.
2. On restart + character hydration, `Known` restores; `CooldownRemaining` is empty (all abilities ready — correct). Passive effects (`toughness`) re-derive automatically because they were never stored — the known-abilities list is the source. Mob `AbilitiesComponent` is never written (mobs carry no `PersistentEntity`); a fresh-spawned mob gets its known set from its template/registry at spawn (mob ability authoring deferred — see Out of scope).

---

## Events fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `AbilityLearnedEvent` | `TeachCommand` (and any future learn initiator) | `uint EntityId, string AbilityId` | downstream reactions (e.g. future "you have learned X" notification, progression hooks) |
| `AbilityActivatedEvent` | `UseAbilityCommand` (11-b: `cast`/skill verbs) | `uint ActorEntityId, string AbilityId, uint? TargetEntityId` | combat/broadcast/observers (11-b consumers); audit |
| `AbilityTaughtByAdminEvent` | `TeachCommand` | `uint AdminEntityId, uint StudentEntityId, string AbilityId` | admin audit (`AdminAuditHandler`) |

`IAbilitySystem`, `IEffectSystem`, `AbilityEffectContributor`, and `IStatSystem` never publish (INV-5). `EffectAppliedEvent` for ability-produced effects is published by the activation initiator (mirrors `AffectCommand`), pending the Open question.

---

## Implementation plan — work packages

> **Sub-agent execution.** Each package is sized for an independent run by a limited-context model. **WP-1 lands first** (it defines the model + system + the core `IEffectContributor` seam — everything else depends on it). **WP-2 and WP-3 depend only on WP-1, not on each other**, so they may run in parallel. The **primary agent runs `architecture-reviewer` (code mode) across the combined diff** after all three land — sub-agents do not self-review. Mirror the WP structure of [`stat-resource-substrate.md`](stat-resource-substrate.md).

### WP-1 — Ability model + system + passive-derivation seam *(no player/admin surface)*
- **Scope:** the ability model, the domain system, the cooldown tick, the registry, and the core-side effect contributor seam — nothing a player/admin sees.
- **Files:**
  - `Core/Modules/Abilities/AbilityDefinition.cs` — `AbilityDefinition` record + `AbilityKind`/`Activation`/`Targeting` enums + `ResourceCost` record + carried-not-wired `TriggerCondition`/`ImprovementCurve`/`Requirement` placeholder types.
  - `Core/ECS/Components/AbilitiesComponent.cs` — `Known` + `CooldownRemaining`; `[Persistent]`; co-located with `PoolsComponent`/`ResourceType` in `Core/ECS/Components/` (cross-cutting, lives on Player **and** Mob; no `Abilities` namespace collision since the type name differs from the module — but **verify** per the CLAUDE.md namespace/type rule and rename if `AbilitiesComponent` collides with `Core/Modules/Abilities/`).
  - `Core/Modules/Abilities/AbilitiesComponentJsonConverter.cs` — writes only `Known` (mirror `EffectsComponentJsonConverter`); referenced via `[JsonConverter]` on the component.
  - `Core/Modules/Abilities/AbilityRegistry.cs` (`IAbilityRegistry` + `AbilityRegistry`) — the starter set (`toughness`, `kick`, `empower`, `mend`, `blood_pact`).
  - `Core/Modules/Abilities/Systems/IAbilitySystem.cs` + `AbilitySystem.cs` — the pipeline above; deps `EntityService`, `IAbilityRegistry`, `IEffectSystem`, `IEffectRegistry`, `IAttributeSystem`, `IEntityStateService`.
  - `Core/Modules/Effects/Systems/IEffectContributor.cs` (new core interface) + fold into `EffectSystem.GetModifiers`/`GetActive` (inject `IEnumerable<IEffectContributor>`).
  - `Core/Modules/Abilities/AbilityEffectContributor.cs` — implements `IEffectContributor`; derives known-`Passive` `WhileKnown` `StatModifier`s on read.
  - `Core/Modules/Abilities/Handlers/AbilityCooldownTickHandler.cs` — `HeartbeatTickEvent` → `AdvanceCooldowns`.
  - `Core/Modules/Abilities/Events/AbilityLearnedEvent.cs`, `AbilityActivatedEvent.cs`, `AbilityTaughtByAdminEvent.cs`.
  - `Core/Modules/Abilities/AbilitiesModule.cs` — `AddAbilitiesModule(IServiceCollection)` registers system, registry, contributor, tick handler, events' consumers; **add any new `EffectDefinition` rows** the starter abilities reference to `EffectRegistry` (or supply them through the ability registry — see Open questions). Call `AddAbilitiesModule()` from `Server/Program.cs`.
- **Depends on:** nothing new (consumes 9-d/9-e/9-b surfaces). Lands first.
- **Out of scope:** all commands; mob ability authoring; combat targeting; regeneration.
- **Exit (testable):** solution builds; `IAbilitySystem.Activate` spends all costs atomically, sets cooldown, applies effects; `IAbilitySystem.Learn`/`IsKnown`/`GetKnown`/cooldown queries work; `AdvanceCooldowns` decrements; `IEffectSystem.GetModifiers(e, HpMax)` for an entity that knows `toughness` returns the passive's `+HpMax` (derived, not stored) and **does not** if the entity doesn't know it; `EffectSystem` carries **no** reference to `Core/Modules/Abilities/`.

### WP-2 — Admin + test surfaces (`teach`, `useability`) *(depends on WP-1)*
- **Scope:** the admin authoring + the end-to-end test activation path; their events' audit wiring.
- **Files:**
  - `Core/Modules/Abilities/Commands/TeachCommand.cs` — `teach <player> <abilityId>` (Full, `AdminRequirement`); resolve target like `AffectCommand`; `Teach` → admin boundary save → publish `AbilityLearnedEvent` + `AbilityTaughtByAdminEvent`.
  - `Core/Modules/Abilities/Commands/UseAbilityCommand.cs` — `useability <abilityId> [target]` (Full, `AdminRequirement`); resolve actor = invoker, optional target; `Activate` → publish `AbilityActivatedEvent` (+ per-effect `EffectAppliedEvent` pending Open question); render result.
  - `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` — add `AbilityTaughtByAdminEvent` to its subscription list (slice-11-a row).
  - Register both commands in `AbilitiesModule`.
- **Depends on:** WP-1.
- **Out of scope:** the player inspect command (WP-3); player verbs (11-b).
- **Exit (testable):** `teach bob empower` adds it to bob's known set and survives a save/reload; `useability empower` applies the buff, spends mana, sets cooldown; `useability blood_pact` deducts HP **and** Mana; activating on cooldown or unaffordable fails with the right message; `AbilityTaughtByAdminEvent` is audited.

### WP-3 — Player inspect surface (`abilities`/`skills`/`spells`) + catalog sweep *(depends on WP-1)*
- **Scope:** what a player sees; the consolidated reference-catalog + reconciliation updates across all three packages.
- **Files:**
  - `Core/Modules/Abilities/Commands/AbilitiesCommand.cs` — `abilities` (aliases `skills`, `spells`; Partial); lists known abilities with Kind/Activation/Targeting/costs/remaining-cooldown via `IAbilitySystem`. Add an `AbilitiesDisplayMessage` (mirror `EffectDisplayMessage`) for typed output (INV-11).
  - Register in `AbilitiesModule`.
  - **Catalog + reconciliation sweep (WP-3 owns it for the whole slice):**
    - `docs/reference/components.md` — add `AbilitiesComponent` (`[Persistent]`, `Known` durable / cooldowns transient via converter) + `ResourceCost`/ability enums note.
    - `docs/reference/systems.md` — add `AbilitySystem` (domain), `AbilityRegistry`, `AbilityEffectContributor`; extend the `EffectSystem` entry to note the `IEffectContributor` fold.
    - `docs/reference/handlers.md` — add `AbilityCooldownTickHandler`; add `AbilityTaughtByAdminEvent` to `AdminAuditHandler`'s row.
    - `docs/reference/commands.md` — add `teach`, `useability` (admin) and `abilities`/`skills`/`spells` (player).
    - `docs/reference/systems-planned.md` — **supersede** `ISkillSystem` + `ISpellSystem` with a note pointing to the shipped `IAbilitySystem` (INV-15/INV-D3). `docs/reference/components-planned.md` — mark `SkillsComponent` superseded by `AbilitiesComponent`.
- **Depends on:** WP-1 (and WP-2's `teach` for a populated list, but not its code).
- **Out of scope:** combat, the core seam, regeneration.
- **Exit (testable):** `abilities` renders known abilities + remaining cooldowns; `skills`/`spells` resolve to the same command; catalogs match the code and the planned-systems reconciliation is recorded.

---

## Content tooling impact

- **New gameplay state** (`AbilitiesComponent` — known abilities + cooldowns) ships its authoring + inspection in the same slice (INV-18): admin `teach` authors it on a live player; player `abilities`/`skills`/`spells` inspects it; admin `useability` exercises the full activation pipeline. The hardcoded `AbilityRegistry` is the starter content (≥2 skills, ≥2 spells, ≥1 passive, ≥1 HP+Mana spell), Category-3 balance data analogous to `EffectRegistry`.
- **Editor-forward.** All authoring runs through `IAbilitySystem` (`Teach`/`Learn`/`Activate`), never logic bound to a command — so the future content editor and the 11-b player verbs reuse the same system with no rework. Commands are thin pass-throughs (INV-8).
- **Deferred authoring, explicitly noted:** YAML ability authoring, granting abilities to YAML/instance mobs, and a data-file ability catalog are deferred (the registry stays hardcoded this slice). Mobs *carry* `AbilitiesComponent` data but there is **no mob AI to use abilities** and **no per-mob grant tooling** yet — both deferred. These are tracked as the natural follow-ons in **Design notes / Out of scope**, not as in-slice tooling gaps, because no consumer exists yet (the substrate is provable via the admin path).

---

## Cross-cutting surfaces stressed

- **Commands — Adequate.** `teach`/`useability` follow the existing admin command shape (`AffectCommand` is the exact template: `AdminRequirement`, Full match, `CommandArgumentSchema`, target resolution via `ISessionManager`). `abilities`/`skills`/`spells` follow the player inspect shape (`AffectsCommand`). No framework change. **Note:** dynamic *verb registration* (a learned skill becoming an invokable verb) is **11-b** and is **not** attempted here — this slice's activation is the fixed admin `useability` verb, so it does not stress `IVerbRegistry`. This is the deliberate "provable without the verb framework" boundary; calling it out so the spec gate sees the seam is consciously deferred, not missed.
- **Output — Adequate.** New typed messages (`AbilitiesDisplayMessage`, activation/teach confirmations via `PlainMessage`) ride the existing `IOutputWriter`/`IOutputMessage` pipeline (INV-11). Mirrors `EffectDisplayMessage`.
- **Persistence — Adequate (with a deliberate transient-split via converter).** `AbilitiesComponent` is `[Persistent]`; its `[JsonConverter]` writes only `Known` (cooldowns transient) — the **exact** pattern slice 9-e established for `EffectsComponent` (`[Persistent]` + lifetime-filtering converter). No new persistence infra. **Persistence opt-in audit:**
  - **Level 1 (entity domain):** This slice adds no new entity construction path. `AbilitiesComponent` attaches to **players** (persistent domain — already carry `PersistentEntity`) and **mobs** (world-content domain — never carry `PersistentEntity`, fresh-spawned from YAML/template each startup, INV-23). The component is written only for players; for mobs the converter is irrelevant because the entity is never in the flush pool. No `PersistentEntity` is added to any mob (INV-23 preserved).
  - **Level 2 (component inclusion):** `AbilitiesComponent` is `[Persistent]` — its `Known` list is player state that must survive restart. `CooldownRemaining` is transient combat-adjacent state (resets to ready on relog, matching `Timed` effects dropping on relog) — excluded by the converter, not by a separate component. Rationale for `[Persistent]` on a component that also sits on world-content mobs: the attribute is harmless on mobs (they are never serialized); the player case requires it. This mirrors `EffectsComponent`, which is likewise `[Persistent]` and lives on both Player and Mob.
  - **Level 3 (save-on-change scope):** The only caller-initiated `SaveEntityAsync` is in `teach` — an **admin boundary save** (INV-22 case b: admin-gated command mutating a persistent entity through a domain system, single post-mutation save paired with the `AbilityTaughtByAdminEvent` audit). `useability` (admin, but a *runtime* state change — pool spend, cooldown, effect apply) **does not** save; those changes ride the periodic flush, exactly like `affect`/combat HP changes. **No handler saves** (the cooldown tick handler never persists). This satisfies INV-22.
- **Event bus — Adequate.** Three thin past-tense events; published only by initiators/handlers (INV-5/INV-6). `AbilityTaughtByAdminEvent` slots into the existing `AdminAuditHandler` multi-event subscription (no new audit infra).
- **ECS queries — Adequate.** `AdvanceCooldowns` and `AbilityEffectContributor` use `EntityService.GetAllComponents<AbilitiesComponent>()` / `TryGet<AbilitiesComponent>` — the established query seam (mirrors `EffectSystem`). No `is`/`as` (INV-4).
- **Time — Adequate (folds into the existing heartbeat flow).** The cooldown decrement is a new `HeartbeatTickEvent` subscriber at priority 20, alongside `EffectTickHandler`/`CombatTickHandler`/`DeathTickHandler`. No change to the heartbeat itself; the per-tick fan-out already supports independent priority-20 domain handlers. See **Flows**.
- **Effect-system extension (`IEffectContributor`) — Gap exposed → resolved in-slice (framework lands with the slice).** Folding passive-ability effects into `GetModifiers`/`GetActive` without a core→domain dependency requires a **new core seam** that does not exist today (`EffectSystem.GetModifiers` currently reads only `EffectsComponent.Effects`). Per ground rule 9, the supporting framework lands **in the same slice** (WP-1) — it is **not** hand-rolled or deferred. The seam is reused-by-design: equipment-derived effects (deferred in 9-e) fold in through the **same** `IEffectContributor`, so this is not a one-off. **This is the surface the spec gate must scrutinize** (the slice-2-style miss would be to instead make the Abilities system push effects into `EffectsComponent`, or to let `EffectSystem` reference the Abilities module — both rejected here). If the owner prefers a different seam shape, see Open question 2.
- **Configuration — Adequate.** Cost amounts and cooldown seconds are Category-3 balance data baked in the hardcoded `AbilityRegistry` (same posture as `EffectRegistry`); no new config keys. Promotion to a data file is deferred (backlog), matching the effects precedent.
- **Sessions — Adequate.** Target/teacher/student resolution reuses `ISessionManager.GetAll()` + `CharacterComponent` name match exactly as `AffectCommand`.
- **Modules — Adequate.** New `AbilitiesModule` with `AddAbilitiesModule(IServiceCollection)`, called from `Server/Program.cs` (the standard feature-module composition; no `IModule` interface).

---

## Flows introduced or modified

- **New canonical flow — Flow 24, "Ability activation."** `useability`/`teach` initiator → `IAbilitySystem.Activate` (resolve → state/cooldown/cost checks → spend costs via `IAttributeSystem` → set cooldown → `IEffectSystem.Apply` per effect) → initiator publishes `AbilityActivatedEvent` (+ `EffectAppliedEvent` per effect). This is a recurring chain that **11-b's `cast`/skill verbs will reuse**, so it is promoted to `flows/README.md` (new `flow-24-ability-activation.md` + index row). The implementation PR must add it (INV-17; the architecture-reviewer blocks on drift).
- **Modified — Flow 16 (heartbeat tick).** Add `AbilityCooldownTickHandler` to the list of priority-20 `HeartbeatTickEvent` subscribers (alongside Effect/Combat/Death tick handlers). The cooldown decrement **folds into the existing heartbeat fan-out** — it does **not** warrant its own flow (it is a single `AdvanceCooldowns` call with no fan-out, the no-chain shape). Flow 16's body gets a one-line addition; no diagram change beyond listing the new subscriber.
- **Modified — the stat-read fold.** The `IStatSystem.Get` → `IEffectSystem.GetModifiers` path (described in [`effects.md`](../architecture/effects.md) and Flow 21's persistence/stat notes, not a standalone flow) now additionally sums `IEffectContributor` output. No standalone flow file owns this read path today; the change is captured in the `effects.md` "Stat integration seam" + "Source-bound derivation" sections (update both) rather than a new flow. If the reviewer judges the contributor fold a distinct runtime trace worth a flow, promote it; the planner's assessment is that it is a read-time computation, not an event-driven flow.

---

## Design notes

- **One system, not two (INV-15 reconciliation).** The planned `ISkillSystem` (skill checks) and `ISpellSystem` (mana/cast/AoE) in [`systems-planned.md`](../reference/systems-planned.md) are **superseded** by the single `IAbilitySystem` — skills and spells differ by `Kind` + `Costs` + (future) invocation style, all data. This slice records that reconciliation in `systems-planned.md` (WP-3) so the planned catalog stops implying two systems. The planned `SkillsComponent` is likewise generalized into `AbilitiesComponent`.
- **Passive-derivation seam — `IEffectContributor` (now canon: INV-24).** This slice is the **first consumer** of the contributor seam, canonized as **INV-24** (pattern in [`effects.md`](../architecture/effects.md#the-contributor-seam); composition doctrine in [`01-layers.md`](../architecture/01-layers.md#the-three-composition-shapes)). `EffectSystem` is core and cannot reference the Abilities module (INV-2). A core-defined `IEffectContributor` (DI-collected by `EffectSystem`) lets the Abilities module *register* a contributor that derives `WhileKnown` passive effects on read, with the dependency arrow pointing the legal way (Abilities domain → Effects core interface). "WhileKnown is derived, not stored" is honored — nothing is written to `EffectsComponent` for passives; the known-abilities list is the source. This is the **same seam** equipment-derived effects (deferred in 9-e) will use, so it is not a one-off (ground rule 9). **One viable alternative exists** and is surfaced for the owner (Open question 2): a non-interface variant where `EffectSystem` reads a generic, core-owned `DerivedEffectsComponent` that the Abilities tick/learn path populates — rejected by the planner because it *stores* derived effects (violates "derived, not stored") and reintroduces a refresh-on-change burden. The contributor seam is preferred; flagged so the owner can confirm.
- **Costs are a list; HP/blood is a permitted cost.** Matches the just-edited gameplay model (`Costs` is a `IReadOnlyList<ResourceCost>`; HP permitted). Activation checks **all** costs and spends atomically (no partial spend). The governing stat (Mind for spells, Body for skills) is independent of the cost pool — recorded via `Kind` for now, not yet used to derive cost or power (that is progression/combat depth).
- **Power is the effect system's job (no new math).** Abilities produce effects via `IEffectSystem.Apply(target, def, source = actor)`; `Apply` computes Power from the actor's stats via the existing `PowerScaling`. The ability system never computes Power or magnitude — it only chooses the effect ids and the target, and spends costs. This keeps INV-8's "rule lives in the system" intact (effect math in `EffectSystem`, cost/cooldown rules in `AbilitySystem`).
- **Cooldowns: transient, per-ability, seconds, no global cooldown.** Mirrors Effect `Elapsed`/`Duration` (seconds, heartbeat-decremented). Stored in `AbilitiesComponent.CooldownRemaining`, excluded from persistence by the converter. A global cooldown / combat command-queue / one-ability-per-round is deferred to the combat use-cases.
- **Carried-not-X decisions (design for the seam, do not wire):** `Trigger?` is **carried, not wired** (Triggered abilities are rejected by `Activate` as not-directly-activatable; the reactive evaluation hook is a later slice). `Aspect?` is **carried, not resolved** (aspect math is S3). `Curve`/`LearnReqs` are **carried, not applied** (the progression slice gates learning and grows abilities). The `GrantAbility` **effect kind** stays **deferred** (no MVP consumer) — explicitly **distinct** from the `WhileKnown` passive derivation, which **is** implemented here: passives contribute effects *because they are known*; `GrantAbility` would *grant a new ability from an effect*, which nothing yet needs.
- **Mobs carry the data, nothing uses it.** `AbilitiesComponent` lives on Mob so the model is uniform and 11-b/combat can later let mobs use abilities, but **no mob AI** invokes abilities this slice and **no per-mob grant tooling** exists. World-content mobs never persist the component (INV-23).
- **Provable without the verb framework (the 9-e parallel).** Exactly as 9-e shipped `affect`/`affects` before any ability consumer, this slice ships `teach`/`useability`/`abilities` so the learn → activate → multi-cost-spend → cooldown → passive-derivation pipeline is end-to-end testable before 11-b adds `cast` and dynamic skill verbs.
- **On ship:** author `architecture/subsystems/abilities.md` (the living design of the ability system — kinds, activation pipeline, cooldown model) and trim this doc to requirements + the durable behavior spec (docs lifecycle, R7; INV-D2). The `IEffectContributor` seam is **already canon** (INV-24; [`effects.md`](../architecture/effects.md#the-contributor-seam); [`01-layers.md`](../architecture/01-layers.md#the-three-composition-shapes); the `add-core-system` skill) — WP-1 implements the first contributor; WP-3's catalog sweep updates `systems.md`/`effects.md`.

---

## Resolved planning inputs

Settled with the owner (the ratified 11-a scope):
1. **Scope split** — 11-a substrate (this doc) → 11-b invocation/combat-targeting → 11-c resource-regen. 11-a is provable via the admin path without player verbs.
2. **`Costs` is a list; HP/blood permitted** — already edited into the gameplay model; this doc treats the model as current.
3. **`Kind` (Skill|Spell) is a required discriminator.** **Targeting is `Self | Target` only** this slice. Passives are **unconditional only** (conditional "while wielding a sword" deferred).
4. **Cooldowns transient (not `[Persistent]`), seconds, no global cooldown.**
5. **Effects are produced via `IEffectSystem.Apply`; Power is computed by the effect system from caster stats** — no new potency math in 11-a.
6. **Legacy `ISkillSystem`/`ISpellSystem` reconciled into `IAbilitySystem`** (INV-15); `SkillsComponent` generalized into `AbilitiesComponent`.
7. **Planner-surfaced questions 1–5 resolved** (owner-confirmed 2026-06-02): (1) none-until-taught — no `AccountSystem` creation hook; (2) `IEffectContributor` seam confirmed (spec gate still validates INV-2 independently); (3) dedicated admin `useability`; (4) active effects publish one `EffectAppliedEvent` each while derived `WhileKnown` passives stay silent; (5) `toughness` magnitude via `PowerScaling.Evaluate`. Detail in **Resolved questions** below.
8. **Contributor seam canonized (2026-06-02):** the passive-derivation mechanism is now first-class architecture — **INV-24** (checklist), the third composition shape ([`01-layers.md`](../architecture/01-layers.md#the-three-composition-shapes)), the pattern + cross-source-resolution-deferred note ([`effects.md`](../architecture/effects.md#the-contributor-seam)), and a how-to in the `add-core-system` skill. **INV-2** was also restated in tier terms (core-tier ⇏ domain-tier, wherever it resides). 11-a is the first consumer.

---

## Resolved questions (owner-confirmed 2026-06-02)

> All five planner-surfaced questions were ratified by the owner. Each "Planner recommendation" below is the **accepted decision**; the alternatives are retained as the rationale that was weighed. **Decisions:** (1) **none-until-taught** for 11-a — WP-1/WP-2 add no `AccountSystem` creation hook; (2) **`IEffectContributor`** seam confirmed — the spec gate still independently validates INV-2 ("let the reviewer surface anything"); (3) **dedicated admin `useability`**; (4) active effects publish one `EffectAppliedEvent` each, **derived `WhileKnown` passives stay silent**; (5) **`toughness` magnitude via `PowerScaling.Evaluate`** (numbers are tunable Category-3 balance).

1. **Starting ability set at character creation — how is it granted?** Options: (a) a config list à la `CharacterDefaults` (e.g. `CharacterDefaults:StartingAbilities`); (b) a hardcoded starter grant in `AccountSystem.CreateCharacterAsync`; (c) **none-until-taught** (every ability learned via `teach`/future trainers). **Planner recommendation: (c) for 11-a** — it keeps `AccountSystem` untouched and the substrate clean; the admin `teach` path fully exercises learning. *Non-blocking* (the pipeline is testable via `teach` regardless), but the owner should confirm so WP-1/WP-2 don't add a creation hook speculatively. If (a)/(b) is wanted, it is a small additive change to `CreateCharacterAsync` (a construction-time concern, already a legitimate save site, INV-22 case a).
2. **Passive-derivation seam shape — confirm `IEffectContributor`.** The planner chose a core-defined, DI-collected `IEffectContributor` that the Abilities module implements (rationale in Design notes; the only clean way to fold passive effects into `GetModifiers`/`GetActive` without a core→domain dependency, and the same seam equipment will reuse). The one alternative — a core-owned `DerivedEffectsComponent` the Abilities path populates — is rejected because it *stores* derived state (violates "WhileKnown is derived, not stored"). *Potentially blocking* (it determines the WP-1 effect-module change and whether INV-2 is satisfiable) — but the planner believes the contributor seam is unambiguously correct; owner confirmation requested rather than a true fork.
3. **Admin/test activation — dedicated `useability` command vs. fold into an existing path?** The planner proposes a dedicated `useability <abilityId> [target]` (admin, the `affect` analogue). Alternative: extend an existing admin command. **Planner recommendation: dedicated `useability`** — it is the cleanest end-to-end test surface and 11-b can later retire/replace it with player verbs. *Non-blocking.*
4. **Do ability-produced effects re-publish `EffectAppliedEvent` per effect?** `AffectCommand` publishes one `EffectAppliedEvent` per applied effect; `IEffectSystem.Apply` itself never publishes (INV-5). The planner proposes the `UseAbilityCommand` initiator publishes `AbilityActivatedEvent` **and** one `EffectAppliedEvent` per applied effect (so existing `EffectAppliedEvent` consumers see ability-sourced effects uniformly). *Non-blocking* but should be confirmed so the event contract is stable for 11-b consumers. **Open sub-detail:** the `toughness` `WhileKnown` passive is derived (never `Apply`-ed), so it fires **no** `EffectAppliedEvent` — confirm that is the intended contract (derived effects are silent; only actively-applied effects publish).
5. **`toughness` `WhileKnown` magnitude source.** A `WhileKnown StatModifier` is never persisted and is derived by `AbilityEffectContributor`. The contributor needs to compute the `+HpMax` magnitude/Power — either by evaluating the same `PowerScaling` formula the effect's `EffectDefinition` declares (preferred — reuses `PowerScaling.Evaluate` against the source's base stats) or by a fixed magnitude on the ability definition. *Non-blocking*; the planner recommends reusing `PowerScaling` so passive and active effects share the magnitude path. Owner to confirm the effect-registry rows the starter abilities reference (the exact `+HpMax`/`kick`/`mend` numbers are Category-3 balance, tunable later).

---

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — Spine B (Ability); the design this implements (note: `Costs` list + HP cost edit is already in the model).
- [`effect-substrate.md`](effect-substrate.md) — S2; provides `IEffectSystem.Apply`/`GetModifiers`/`GetActive`, `EffectLifetime.WhileKnown` (this slice supplies the deferred S4 derivation), and the `IEffectContributor` fold point.
- [`stat-resource-substrate.md`](stat-resource-substrate.md) — S1; provides `ResourceType`, the `IAttributeSystem` clamped pool setters costs are spent through, and the `ScoreId` effects target. **WP structure mirrored from this doc.**
- [`combat.md`](combat.md) — context only; 11-a does **not** touch combat (targeting/initiation is 11-b and later).
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` drives the cooldown tick.
- [`../architecture/effects.md`](../architecture/effects.md) — effect model design; the `WhileKnown` derivation and `IEffectContributor` seam land notes here on ship.
- **Next:** `ability-invocation.md` (11-b) — player verb invocation, dynamic verb registration into `IVerbRegistry`, `cast`, state-aware/offensive targeting, combat initiation.
- **Next:** `resource-regeneration.md` (11-c) — resource pool regeneration + `rest`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
