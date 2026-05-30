# Use Case: Effect Substrate

**Status:** planned
**Actors:** Player, Mob, System, Administrator
**Module:** `Core/Modules/Effects/` (new — `IEffectSystem`, `EffectsComponent`, registry, tick handler, commands), `Core/Modules/Stats/` (`IStatSystem` folds effect modifiers), `Core/Modules/Time/` (heartbeat consumer)

**Spine:** gameplay-model **S2** — the bedrock most later spines depend on (skills/spells produce effects, potions apply them, curses/auras are effects, rarity treatments grant them). See [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine C and decisions R5 (stacking + Power) / R6 (lifetimes). **Design lives in the model; this doc is requirements + implementation plan.** On ship, the effect design graduates to a higher-level `architecture/` design doc (R7 — effects warrant one due to complexity).

---

## Description

Introduce the **effect model** and the system that applies, stacks, orders, ticks, and persists effects. An `Effect` is a parameterized instance of a small fixed set of **kinds**, carrying a target `ScoreId` (from S1), a `Category` tag, a computed `Power`, a `Lifetime`, a `StackPolicy`, and a resolution `Phase`. Effects live in a single `EffectsComponent` list; persistence is lifetime-filtered (only `UntilRemoved` entries are written). `IEffectSystem` is a core system that owns effect lifecycle and exposes the modifier seam `IStatSystem` reads; a heartbeat handler ticks periodic effects and expires timed ones.

This slice wires the **load-bearing kinds end-to-end** — `StatModifier` (feeds the stat pipeline), `Instant` (one-shot heal/damage), `Periodic` (HoT/DoT on the heartbeat), and `GrantFlag` (a tag) — which together exercise the entire machinery (apply → Power → stack → phase → tick → expire → persist → dispel). The remaining kinds (`GrantAbility`, `Trigger`, `TransformModifier`) are defined in the enum but their handlers are deferred to the spines that consume them (abilities S4, world events / generation S5). **Aspect typing** is carried on the effect but not resolved here — aspect math is S3; `Instant`/`Periodic` apply raw magnitude for now.

---

## Preconditions

- Slice **9-d (stat & resource substrate)** complete: `ScoreId`, `IStatRegistry`, `IStatSystem.Get(entityId, ScoreId)`, `IAttributeSystem` pool write seams, four attributes (incl. `Attunement`), four pools.
- Slice **9-b (heartbeat)** complete: `IHeartbeatService` / `HeartbeatTickEvent`.
- Slices **5b (two-level persistence)** and **1 (`ComponentSerializer` via `System.Text.Json`)** complete — the `[JsonConverter]` lifetime filter rides the existing serializer with no infra change.
- Reused: `EntityService`, `IStatSystem`, `IAttributeSystem`, `IEventBus`, `IConfiguration`, `IPersistenceSystem`, command framework + `IOutputWriter`, `AdminRequirement`.

---

## Postconditions (requirements)

**Effect model**
- An `Effect` record carries: `Kind`, `Params` (target `ScoreId`, magnitude, aspect, formula ref), `Category`, `Power`, `Source`, `Group?`, `Lifetime`, `Stacking`, `Phase`. Enums: `EffectKind` (`StatModifier`, `Instant`, `Periodic`, `GrantFlag`, `GrantAbility`, `Trigger`, `TransformModifier`), `EffectCategory` (`Buff`, `Debuff`, `Curse`, `Disease`, `Blessing`, `Poison`, `Aura`, …), `EffectLifetime` (`Instant`, `Timed`, `UntilRemoved`, `WhileEquipped`, `WhileKnown`, `WhilePresent` — duration `-1` == `UntilRemoved`), `StackPolicy` (`Stack`, `HighestWins`, `Refresh`, `UniquePerSource`, `Replace`).

**Storage & persistence**
- A single `EffectsComponent { List<Effect> Effects }` holds standalone effects (`Timed` + `UntilRemoved`). It is `[Persistent]`; an `[EffectsComponentJsonConverter]` writes **only** entries with `Lifetime == UntilRemoved` (`System.Text.Json` honors the attribute — no `ComponentSerializer` change). Source-bound lifetimes are never stored.

**System**
- `IEffectSystem` (core, `Core/Modules/Effects/Systems/`): `Apply`, `Remove`, `RemoveByCategory`, `GetActive`, `GetModifiers(entityId, ScoreId)`, `AdvanceTick(elapsed)`. Returns results; **never publishes events, never persists, never calls a domain system** (INV-5, INV-2).
- `Apply(target, definition, source)` computes `Power` from the definition's `PowerScaling` evaluated against the **source's base stats** (read via `EntityService`; *base*, not effective, to avoid an `EffectSystem`↔`StatSystem` cycle), assigns `Phase`, applies the `StackPolicy` (for `HighestWins`/auras a stronger `Power` replaces and refreshes; a weaker re-apply is ignored), and adds the effect (or, for `Instant`, returns the one-shot result without storing).

**Stat integration**
- `IStatSystem.Get(entityId, ScoreId)` sums `StatModifier` effects targeting that `ScoreId` via `IEffectSystem.GetModifiers`, on top of base + equipment — **no interface change** for existing consumers (combat, `score`). `StatSystem` (domain) → `EffectSystem` (core) is a legal dependency.

**Heartbeat**
- An `EffectTickHandler` subscribes to `HeartbeatTickEvent`: calls `IEffectSystem.AdvanceTick` (returns due `Periodic` applications + expired effects, ordered by `Phase` — HoT before DoT), applies each periodic magnitude through `IAttributeSystem` pool writes (domain), and publishes `EffectExpiredEvent` for each expiry. Orchestration only (INV-1).

**Tooling**
- A hardcoded `EffectRegistry` of starter definitions (e.g. `empower` `StatModifier(+Body)`, `regen` HoT, `poison` DoT, `weaken` `StatModifier(−Body)`, `minor_curse`) — all targeting `ScoreId`s that 9-d produces; Category-3 balance data (see Design notes).
- Admin `affect <target> <effectId> [power]` applies a registry effect; `affects` lists active effects (name, category, Power, remaining) for the caller. Both flow through `IEffectSystem`.

**Events**
- `EffectAppliedEvent`, `EffectExpiredEvent` (past-tense, thin). Admin path also publishes `EffectAppliedByAdminEvent` for audit.

---

## Main flow

### Flow 1 — Apply a buff (admin / future ability)
1. `affect <player> empower 5` → `AffectCommand` resolves the target + `EffectRegistry` definition. (`[power]` is an **admin/testing-only** override; the normal apply path lets `Apply` compute Power — see Design notes.)
2. `IEffectSystem.Apply(target, def, source)` computes `Power` via `PowerScaling`, applies `HighestWins` (a stronger `empower` replaces a weaker active one), adds the `Timed StatModifier(Body)` effect, returns the stored effect.
3. `AffectCommand` publishes `EffectAppliedEvent`; persists the target if it carries `PersistentEntity` and the effect is `UntilRemoved`.

### Flow 2 — Effective stat read (consumer-transparent)
1. Combat/UI calls `IStatSystem.Get(entityId, ScoreId.Body)`.
2. `StatSystem` returns base + equipment + `IEffectSystem.GetModifiers(entityId, ScoreId.Body)` (sum of active `StatModifier` effects). The `empower` buff (and thus `AttackPower`, derived from `Body`) is now reflected; no consumer code changed.

### Flow 3 — Periodic tick + phase ordering
1. `HeartbeatTickEvent` fires. `EffectTickHandler` calls `IEffectSystem.AdvanceTick(elapsed)`.
2. `AdvanceTick` returns due periodic applications ordered by `Phase` (a HoT in an earlier phase than a DoT) and the list of effects whose `Timed` duration reached 0.
3. The handler applies each periodic magnitude via `IAttributeSystem` (heal then damage), and publishes `EffectExpiredEvent` for each expired effect (which `AdvanceTick` already removed from the component).

### Flow 4 — Dispel by category
1. `RemoveByCategory(entityId, Curse)` strips every `Category == Curse` effect from the list (the substrate for a future `cleanse`/`remove curse` ability).

### Flow 5 — Persistence round-trip
1. On save, `ComponentSerializer` serializes `EffectsComponent`; the converter writes only `UntilRemoved` entries. On load, those restore; `Timed` effects are absent (dropped, correct). Source-bound effects were never stored and re-derive from their sources (when those sources carry effect data — later slices).

---

## Events fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `EffectAppliedEvent` | `AffectCommand` / future ability handlers | `uint TargetId, EffectId, EffectCategory, int Power` | downstream reactions, broadcast |
| `EffectExpiredEvent` | `EffectTickHandler` | `uint TargetId, EffectId` | cleanup hooks (a player-facing "effect fades" notification is out of scope for this substrate slice — a later slice adds it) |
| `EffectAppliedByAdminEvent` | `AffectCommand` | `uint AdminId, TargetId, EffectId, int Power` | audit |

`IEffectSystem` and `IStatSystem` never publish (INV-5).

---

## Implementation plan — work packages

> **WP-1 lands first** (model + system). **WP-2 and WP-3 depend on WP-1**; WP-3's "remaining/ticking" display reads state WP-2 advances, so prefer WP-2 before WP-3 (or run WP-3 against stored state and accept that durations show only after WP-2). Primary agent runs `architecture-reviewer` (code mode) across the combined diff.

### WP-1 — Effect model + `EffectSystem` core *(depends on S1)*
- **Scope:** the record, enums, component (+ converter), and the core system — no consumers.
- **Files:** `Effect.cs` (record + `EffectKind`/`EffectCategory`/`EffectLifetime`/`StackPolicy`/`EffectSource`/`EffectGroup`), `EffectsComponent.cs` + `EffectsComponentJsonConverter.cs` (`[Persistent]`, lifetime filter), `IEffectSystem.cs`/`EffectSystem.cs` (Apply + Power + stacking + phase + Remove/RemoveByCategory/GetActive/GetModifiers/AdvanceTick), `PowerScaling.cs` + a named-formula registry, `EffectsModule.cs`.
- **Reference & tooling sweep (WP-1 owns the rename):** reconcile the superseded two-component effect names to the single `EffectsComponent` in the doc examples that still reference them — `architecture/06-persistence.md`, `architecture/flows/flow-04-persistence-flush-cycle.md`, `reference/archetypes.md` (Weapon/Armor optional-component row), `use-cases/persistence-substrate.md` (example). (`.claude/skills/add-archetype/SKILL.md` was already reconciled with the design decision per INV-20.) Full list tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md).
- **Out of scope:** stat integration, heartbeat, commands. `GrantAbility`/`Trigger`/`TransformModifier` are enum values with **no handler** here.
- **Exit (testable):** two `StatModifier(Body)` effects of differing `Power` → `HighestWins` keeps the stronger; a `Timed` effect is stored, an `UntilRemoved` effect is stored; serializing `EffectsComponent` writes only the `UntilRemoved` entry; `RemoveByCategory(Curse)` strips curses; `AdvanceTick` returns due/expired sets in `Phase` order.

### WP-2 — Stat summation + heartbeat tick *(depends on WP-1)*
- **Scope:** fold effect modifiers into the read seam; drive periodic/expiry from the heartbeat.
- **Files:** `StatSystem.cs` (`Get` sums `IEffectSystem.GetModifiers`), `EffectTickHandler.cs` (subscribes `HeartbeatTickEvent`; applies periodic magnitudes via `IAttributeSystem`; publishes `EffectExpiredEvent`), `EffectAppliedEvent.cs`/`EffectExpiredEvent.cs`.
- **Out of scope:** commands; source-bound (equipment/ability/area) derivation — wired by the slices that give sources effect data.
- **Exit (testable):** a `+10 Body StatModifier` raises `Get(Body)` by 10 with no consumer change; a `regen` HoT heals each heartbeat then expires on schedule; a HoT + DoT on one entity resolve heal-before-damage.

### WP-3 — Effect registry + admin apply/inspect tooling *(depends on WP-1, +WP-2 for live display)*
- **Scope:** authorable/inspectable surface for the new state (INV-18).
- **Files:** `EffectRegistry.cs` + starter definitions, `AffectCommand.cs` (admin, `Full` match), `AffectsCommand.cs` (player/admin, `Partial`), `EffectDisplayMessage`. **WP-3 owns the consolidated reference-catalog sweep:** `components.md` (`EffectsComponent`, from WP-1), `systems.md` (`EffectSystem` + the `StatSystem` extension, WP-1/WP-2), `handlers.md` (`EffectTickHandler`, WP-2), `commands.md` (`affect`/`affects`).
- **Out of scope:** data-file effect authoring; composites; aspect.
- **Exit (testable):** `affect <player> empower 5` applies and `affects` shows it (category, Power, remaining); `affect <player> empower 8` replaces it (`HighestWins`); `affect <player> poison` ticks damage on the heartbeat; `affect`-ing a `minor_curse` then dispelling by category removes it.

---

## Content tooling impact

- `EffectRegistry` (hardcoded) + `affect`/`affects` are the author + inspect path shipped in-slice (INV-18). Effect **definitions** are Category-3 balance data, hardcoded now; the documented promotion is to a data-file effect catalog when content authoring matures (see the balance-surface + content-editor backlog items). **Editor-forward:** application logic lives in `IEffectSystem` and the registry, never in the command — the future editor and future ability/potion systems are all thin callers.

## Cross-cutting surfaces stressed

- **Persistence — Adequate (resolved).** Field-selective serialization via an attribute `[JsonConverter]` on `EffectsComponent`; `System.Text.Json` honors it natively, so **no `ComponentSerializer` change**. Resolves the EffectsComponent backlog item.
- **Time / heartbeat — Adequate.** `EffectTickHandler` is a standard `HeartbeatTickEvent` subscriber; no heartbeat change.
- **Stat pipeline — Modified (in-slice).** `StatSystem.Get` folds `IEffectSystem.GetModifiers`; the interface is unchanged, so combat/`score` are untouched.
- **Commands / output — Adequate.** Two new commands + one `IOutputMessage`; existing framework.
- **Configuration — Acknowledged debt.** Effect definitions are Category-3 constants (hardcoded registry); promotion to a tunable data file is tracked (balance backlog). Justified: no designer-iteration-without-recompile need yet.
- **Event bus / ECS — Adequate.**

## Flows introduced or modified

- **New flow — effect tick** (periodic application + expiry on the heartbeat): recurs, so promote to `flows/README.md` when implemented; extends **Flow 16 — heartbeat tick**.
- **Modified — effective stat read**: `IStatSystem.Get` now folds effect modifiers (transparent to consumers); a one-line note on the stat-read path.

---

## Design notes

- **Single list, lifetime-filtered (the resolved decision).** One `EffectsComponent` with a single `List<Effect>`; `Lifetime` is the sole determinant of persistence; the `[JsonConverter]` writes only `UntilRemoved`. No `Persistent`/`Transient` component split and no two-list split — `Lifetime` is not duplicated into a bucket choice. See [`../roadmap/backlog.md`](../roadmap/backlog.md) (resolved item) and [02-ecs.md](../architecture/02-ecs.md).
- **`EffectSystem` is core and self-contained.** It reads the **source's base stats** via `EntityService` to evaluate `PowerScaling`; it never calls `IStatSystem` (would create an `EffectSystem`↔`StatSystem` cycle) or `IAttributeSystem` (domain). Base, not effective, Power keeps the dependency a DAG. Periodic magnitudes are written by the **handler** via `IAttributeSystem`, not by the core system.
- **Power: provisional (inherited from model §6).** One `Power` number serves as both the `HighestWins` key and the magnitude scalar; `PowerScaling` is a small registry of named formulas (e.g. `fixed`, `byAttunement`). `Tier` contributes 0 until Ascension (S8) exists; the formula tolerates the missing term.
- **Aspect carried, not resolved.** `Params.aspect` exists so `Instant`/`Periodic` are aspect-ready, but S2 applies raw magnitude; aspect resolution is S3.
- **`[power]` is a testing override (INV-8).** The optional `[power]` arg on `affect` is an admin/testing-only override of the computed value; the sanctioned apply path (abilities, potions, future callers) never hand-passes Power — `Apply` computes it from `PowerScaling`. The arg exists so a tester can force a value, not as a pattern any production caller follows.
- **No `Speed`-targeting effects yet.** The model's canonical buff is haste→`Speed`, but `Speed` is a derived score no slice has created (9-d's `ScoreId` set stops at the four attributes, the pools, and `AttackPower`/`Defense`). This slice's buff examples target `Body` (raising `AttackPower`) instead; haste→`Speed` lands when a combat/initiative slice makes `Speed` a consumed derived score.
- **Kinds wired vs deferred.** `StatModifier`/`Instant`/`Periodic`/`GrantFlag` are fully handled. `GrantAbility` (needs S4), `Trigger` (needs world-event hooks), `TransformModifier` (needs S5 generation) are enum values with deferred handlers — adding a handler later is additive, no model change.
- **Composites deferred.** The `Category` tag and `RemoveByCategory` ship (dispel substrate); the `CompositeEffectDefinition` bundle (one named curse → several effects sharing a `GroupId`) lands with the content slice that needs authored curses/blessings.
- **Source-bound derivation deferred.** `GetModifiers` sums standalone `StatModifier`s only; equipment/ability/area-derived effects fold in when those sources carry effect data (items later, abilities S4, areas S3) — the seam is the same `GetActive`/`GetModifiers` call.
- **On ship:** author `architecture/<effects>.md` (the higher-level effects design doc, R7) and trim this use-case to requirements + durable spec.

---

## Resolved planning inputs

- **Persistence** — single `EffectsComponent`, lifetime-filtered via `[JsonConverter]` (owner-confirmed 2026-05-30).
- **Power** — one number (stack key + magnitude scalar), named-formula `PowerScaling` registry; provisional per model §6, carried into the build.
- **Scope** — load-bearing kinds (`StatModifier`/`Instant`/`Periodic`/`GrantFlag`) wired; others enum-defined, handlers deferred to their spines.

---

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — Spine C (effects), R5 (stacking/Power), R6 (lifetimes); the design this implements.
- [`stat-resource-substrate.md`](stat-resource-substrate.md) — S1; provides `ScoreId`, `IStatRegistry`, the `Get` seam this folds modifiers into.
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` drives the tick handler.
- [`combat.md`](combat.md) — future consumer (reads effect-modified stats transparently; later produces combat effects).
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` + the `[JsonConverter]` lifetime filter.
- [`../architecture/02-ecs.md`](../architecture/02-ecs.md) — effects/computed-stats section (reconciled to the single component).
- **Next:** S3 (aspect foundation) — adds aspect typing + resolution to the `Instant`/`Periodic` effects this slice leaves raw.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
