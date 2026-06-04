# Use Case: Ability Substrate

**Status:** implemented
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

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — Spine B (Ability); the design this implements (note: `Costs` list + HP cost edit is already in the model).
- [`effect-substrate.md`](effect-substrate.md) — S2; provides `IEffectSystem.Apply`/`GetModifiers`/`GetActive`, `EffectLifetime.WhileKnown` (this slice supplies the deferred S4 derivation), and the `IEffectContributor` fold point.
- [`stat-resource-substrate.md`](stat-resource-substrate.md) — S1; provides `ResourceType`, the `IAttributeSystem` clamped pool setters costs are spent through, and the `ScoreId` effects target
- [`combat.md`](combat.md) — context only; 11-a does **not** touch combat (targeting/initiation is 11-b and later).
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` drives the cooldown tick.
- [`../architecture/effects.md`](../architecture/effects.md) — effect model design; the `WhileKnown` derivation and `IEffectContributor` seam land notes here on ship.
- **Next:** `ability-invocation.md` (11-b) — player verb invocation, dynamic verb registration into `IVerbRegistry`, `cast`, state-aware/offensive targeting, combat initiation.
- **Next:** `resource-regeneration.md` (11-c) — resource pool regeneration + `rest`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
