# Gameplay Systems Model — Design North-Star

> **Status: draft / forward-looking design intent. Not authoritative, not yet built.**
> This is a *model*, not a spec and not a catalog of what exists. It captures the gameplay
> concepts (Ascension, Aspects, skills/spells, effects, progression, rarity, generated
> content) as a small set of **reusable spines** so that the future slices that implement
> them compose rather than duplicate. It feeds [`../roadmap/plan.md`](../roadmap/plan.md):
> each spine decomposes into use-case slices that go through the normal per-slice loop
> (use-case doc → spec gate → implement → code gate). Same posture as the
> [`../reference/*-planned.md`](../reference/components-planned.md) files — design intent,
> explicitly not a claim that any of this exists.
>
> When a spine graduates into real slices, the durable parts migrate to their permanent
> homes: cross-cutting design → `architecture/`, per-feature framework → `architecture/subsystems/`,
> behavior → `implementation-plans/`, what-exists → `reference/`. This document is then trimmed to the
> overlap map + open questions, or archived. It must never become a second source of truth for
> a rule that lives in [`../architecture/checklist.md`](../architecture/checklist.md).

---

## 1. The problem this model solves

The game design calls for skills, spells, effects, elemental attunement, rarity-scaled
mobs/items/areas, experience-driven progression, Ascension tiers, quests, and generated
content. Built independently, these features would each reinvent the same machinery — a
buff, a curse, an item bonus, an aura, and an area aura are *the same idea* wearing five
different costumes; a skill and a spell differ by a noun; a Veteran wolf, a magic sword, and
a "more dangerous" random cellar are one scaling transform applied to three targets.

Two failure modes to avoid:

- **Spaghetti / N-systems** — a `BuffSystem`, `CurseSystem`, `AuraSystem`, `ItemBonusSystem`,
  `AreaEffectSystem` that each re-derive stacking, expiry, and stat math.
- **The everything-is-data engine** — 500 hand-authored effect definitions, or a scripting
  pyramid where every interaction is a configurable layer. Dynamism at the cost of a system
  nobody can reason about.

The resolution is a **small set of orthogonal primitives that compose**, each backed by a
registry, with a deliberate line between *behavior* (code, few) and *variation* (data, many).
The rest of this document names those primitives, shows every surface feature mapping onto
them, and sketches how they fit Hedron's existing 4-layer + ECS + computed-stats model.

---

## 2. Design principles (the line we ride)

1. **Compose primitives; don't add a system per feature.** New gameplay content should be a
   new *instance* of an existing primitive, not a new system. Adding a fire spell is data.
   Adding a new *kind* of effect behavior is code — and deliberately rare.
2. **Behavior is code (few); variation is data (many).** The spine — effect *kinds*, targeting
   *modes*, lifetime *kinds*, stacking *policies*, the scaling *function*, aspect *resolution* —
   is a capped set of code. The 500 spells/items/curses are parameterized instances in
   registries. This is the explicit answer to "I don't want to define 500 effects, but I don't
   want a pyramid of layers." The pyramid is capped at the primitive set; the 500 live as data.
3. **Compute on read; never cache stat mutations.** Effective scores are recomputed from base +
   active effects on every read. This is already Hedron's model
   ([02-ecs.md → Effects: Computed Stats](../architecture/02-ecs.md#effects-computed-stats-split-persistence))
   and it sidesteps the "equipment changed / buff expired / did I recalc?" bug family. This
   model extends it, it does not replace it.
4. **One vocabulary for "elementally typed."** Every place that is fire/Ichor/Dream-flavored —
   damage, resistance, attunement, area, ability, tier theme — references the same `Aspect`
   value and resolves through one function. No `FireSystem`.
5. **Registries, even hardcoded, are the lookup spine.** Each trait family (Aspects, Stats,
   Abilities, Effect kinds, Rarity tiers, Resources, Ascension tiers, Objective templates) is a
   registry keyed by a stable id. Definitions may be hardcoded in a module or authored in YAML;
   the registry is the single resolve point. "Spread across the codebase" stops mattering once
   every family has one front door.
6. **Fit the layers, don't fight them.** Primitives slot into the existing taxonomy
   (INV-1/INV-2): generic resolution → **core systems**; game semantics → **domain systems**;
   orchestration + event publication → **handlers/initiators**; state → **components**. Nothing
   here introduces a new layer.

---

## 3. The spines

Six primitives carry the entire design. Each section says what it *is*, what it is **not**
(the over-engineering guardrail), and where it plugs into the existing architecture. First,
though, the **substrate** the spines read and write.

### Substrate — Stats & Scores (what the spines act on)

Stats are **not a spine** — a spine is reusable *behavior*; stats are the addressable *target
surface*. Every score has a stable `ScoreId` and a **role**:

| Role | Examples | Stored | Grows via |
|---|---|---|---|
| **Primary attribute** | **Mind, Body, Spirit, Attunement** (the four — governance below) | base value | progression tracks (Spine E), rare items, objectives |
| **Derived score** | damage / attack power, armor, damage reduction, defense, speed, crit | **not stored — computed** | a function of attributes + equipment + effects |
| **Resource pool** | **HP, Mana, Stamina, Astra** | current stored, max computed | HP on its own track; the rest tied to a stat (below) |
| **Aspect score** | affinity / resist per Aspect (Spine A) | base + computed | attunement growth, gear, effects |

The unifying seam: **a `StatModifier` effect's target field *is* a `ScoreId`** (Spine C). That
one fact is why there is no `BuffSystem` / `CurseSystem` — "+20 HP from a ring," "−Strength from a
curse," "+Speed from haste," "+fire resist from gear" are the *same* `StatModifier` kind pointed
at different `ScoreId`s. `IStatSystem.Get(entity, scoreId)` resolves the effective value: read
base (primary/pool) or run the derive function (derived) → sum every `StatModifier` whose target
is that `ScoreId` → apply aspect adjustments.

This is **partly built already**: `IStatSystem.GetEffectiveAttackPower = strength/2 + weaponBonus`
([stat-system.md](../implementation-plans/stat-system.md)) is one derived score; `AttributesComponent` holds
primaries; `PoolsComponent` holds pools. The model generalizes "one hardcoded derived stat" into
"any `ScoreId` with a registered derive function." Strength is a *primary*, Damage and Speed are
*derived*, HP is a *pool* — all addressed by `IStatSystem`, all modifiable by the one
`StatModifier` kind. The canonical score set lives in `IStatRegistry` (Spine F).

**The four attributes and their pools** (decided — supersedes the built `Str`/`Dex`/`Con` stub, which migrates):

| Attribute | Governs | Pool |
|---|---|---|
| **Mind** | skill learning, crafting, some skills/spells | Mana |
| **Body** | physical attack rating / damage | Stamina |
| **Spirit** | certain resistances, certain skills/spells | — (none) |
| **Attunement** | tier-related powers, aspect power (Spine A) | Astra |

**HP** is a pool with **no governing stat** — it advances on its own progression track. The built
`AttributesComponent` (`Strength`/`Dexterity`/`Constitution`, slice 8a) is a stub the stat-system
extension slice migrates to these four.

### Spine A — Aspect (the elemental vocabulary)

**What it is.** An `Aspect` is a registry-backed identifier with a definition — never a system.

```
AspectDefinition {
  AspectId Id;                 // stable key, e.g. Ichor, Plague, Fire, Void
  string   Name;
  AspectCategory Category;     // Corporeal | Abstract | Mundane
  AspectId? Opposes;           // declared for future use; v1 sets no pairs — resolution treats aspects independently
  ThemingHints Theme;          // flavor: room-desc tints, message color, etc.
}
```

The draft set: **Corporeal** (Ichor, Plague, Flesh, Thorn, Mirror), **Abstract** (Essence,
Void, Astral, Psychic, Dream), **Mundane** (Fire, Water, Air, …). Mundane elements and the
named Aspects share one machinery — they are different rows in one registry, not different code
paths.

Aspect is a **value used as a tag/key**. Elemental *identity* is an optional **aspect
composition** — a normalized set of `AspectId → weight` (empty = no affinity, a single = 100,
a blend = fractions summing to 100). Entities, damage packets, and areas each carry one. One
resolution function turns "aspect-composed magnitude from a source" into "effective magnitude
against a target." **Resistance is a separate, independent per-aspect dimension** — *not* derived
from the composition — because aspects are semantic tags that matter beyond damage typing
(decided R8).

```
IAspectSystem (core)
  int  Resolve(int magnitude, AspectComposition damage, uint sourceId, uint targetId);
       // for each aspect a present in `damage`: apply that fraction of magnitude through the
       // source's affinity in a and the target's (independent) resist to a; sum and clamp.
  AspectComposition Affinity(uint entityId);        // the entity's normalized aspect makeup (identity + outgoing attunement)
  int  Resist(uint entityId, AspectId aspect);      // INDEPENDENT per-aspect incoming reduction (base + effects)

AspectComposition                                   // optional, normalized
  // empty (no affinity) — OR — (AspectId -> weight) whose non-zero weights sum to 100.
  // The Spine F startup validation pass asserts every authored composition is empty or sums to 100.
```

**Where Aspect shows up** (all through the one resolution function):

| Surface | How Aspect is used |
|---|---|
| Damage packet | Damage is `(magnitude, AspectComposition)`, not a bare int — typed by a normalized makeup (empty / single / blend). |
| Resistance | An **independent** per-aspect reduction score, computed on read from base + effects (not derived from the composition). |
| Player/mob attunement | The entity's normalized aspect **composition** (identity + outgoing attunement); resistance is tracked separately. |
| Ability typing | An ability carries an Aspect → its damage/effects are aspect-typed and benefit from caster attunement. |
| Area attunement | An area carries an Aspect → ambient effect + scaling bias + theming. |
| Ascension theming | A tier may be themed by an Aspect (see Spine F open question on vertical vs horizontal). |

**What Aspect is NOT.** Not a per-element system. Not damage *types* hardcoded in combat. Not
an inheritance hierarchy. New element = new registry row. The resolution function never grows
an arm per aspect. But an aspect is **more than a damage type**: `AspectDefinition` is shaped to
carry future aspect-unique ability/effect riders (Spine B × C) — an aspect-typed ability may be
enhanced in aspect-specific ways beyond a damage scalar (shape-for-later; v1 lands typing +
affinity + independent resistance only).

**Layer fit.** `IAspectSystem` is a **core system** (generic resolution, no game semantics
beyond the math). Attunement *scores* live in a component and are summed by the stat pipeline.

---

### Spine B — Ability (skills and spells, unified)

**What it is.** Skills and spells are **one shape**. An `AbilityDefinition` (registry):

```
AbilityDefinition {
  AbilityId Id;
  string Name;
  AbilityKind Kind;            // Skill | Spell — REQUIRED discriminator: drives invocation (skill = run like a command; spell = `cast <name>`) + pool/stat
  Activation Activation;       // Active | Passive | Triggered
  ResourceCost[] Costs;        // one or more (ResourceType, amount) — Stamina(Body) skills, Mana(Mind) spells, Astra(Attunement) tier powers; HP (blood) is a permitted cost (e.g. a spell costing HP + Mana). Governing stat is independent of the cost pool.
  Targeting Targeting;         // Self | Target | Room | Group | AspectArea
  AspectId? Aspect;            // optional typing (Spine A)
  TriggerCondition? Trigger;   // for Triggered passives (dodge, riposte): a condition + chance/scaling
  Effect[] Effects;            // what it does (Spine C) — the ONLY place "what happens" lives
  ImprovementCurve Curve;      // how it grows with use/XP (Spine E)
  Requirement[] LearnReqs;     // attunement/stat/ascension gates
}
```

The mechanical difference between a skill and a spell is **data**: which resource pool it
draws, which stat governs it, and how it is invoked (a skill executes like a command; a spell via
`cast <name>`). `Kind` is a **required discriminator**, so deeper divergence later costs nothing.
There is one `IAbilitySystem.Activate(actor, abilityId, target)` that checks state/cost/cooldown,
then produces the ability's `Effects` and resolves them through the Effect + Aspect pipelines.

`Activation` is a field, not a class hierarchy:
- **Active** — invoked by a command (`kick`, `cast fireball`). Produces effects now.
- **Passive** — its effects apply *while known* (lifetime `WhileKnown`, Spine C). "Sword
  master" = a conditional `StatModifier` effect active while wielding the right weapon type.
- **Triggered** — a `TriggerCondition` (a small registry of condition kinds) fires the effects
  reactively, with a chance/scaling formula keyed off stats. "Dodge" = on incoming attack, roll
  against agility-derived chance; on success apply an avoid effect. The "logic decides when and
  how effective" is this condition + formula — kept to a thin evaluation hook, not a rules engine.

**Worked examples** (all data, no new code):

| Ability | Kind | Activation | Targeting | Effects |
|---|---|---|---|---|
| Kick | Skill | Active | Target | `Instant` damage (Aspect Flesh/Thorn) |
| Sword Master | Skill | Passive | Self | `StatModifier` attack while wielding sword (conditional) |
| Dodge | Skill | Triggered | Self | on-incoming-attack → avoid `Effect`, agility-scaled chance |
| Fireball | Spell | Active | AspectArea/Room | `Instant` damage (Aspect Fire) to all hostiles |
| Haste | Spell | Active | Self/Group | `Timed StatModifier` speed |

**What Ability is NOT.** Not a method per skill. Not a `SkillSystem` and a separate `SpellSystem`
with duplicated cost/cooldown/targeting logic (the [planned](../reference/systems-planned.md)
`ISkillSystem` + `ISpellSystem` are reconciled here into one ability pipeline; spells are the
aspect/mana-flavored variant). Not a scripting language — every ability is a fixed set of
targeting modes producing a list of effect primitives.

**Layer fit.** `IAbilitySystem` is a **domain system** (it knows game concepts: cost, cooldown,
learn requirements). It composes core systems (`IAspectSystem`, `IEffectSystem`, `IDiceSystem`).
A learned-ability `AbilitiesComponent` (generalizing the planned `SkillsComponent`) holds known
abilities, improvement progress, and cooldowns.

---

### Spine C — Effect (the unified modifier / reaction model)  ★ centerpiece

This is where the "500 effects vs. pyramid of layers" tension is actually resolved.

**What it is.** An Effect is **data describing a modification or reaction** — an *instance* of a
small, fixed set of **effect kinds**. Few kinds (code). Many instances (data, authored on items,
spells, mobs, areas).

**The effect kinds (the capped primitive set):**

| Kind | Does | Covers |
|---|---|---|
| `StatModifier` | add/scale a score field | item +20 HP, haste (speed), curse stat alteration, aura buff, armor/resist/affinity bonus |
| `Instant` | one-shot aspect-typed magnitude | direct heal, direct damage, mana burn |
| `Periodic` | aspect-typed magnitude per tick | DoT, HoT, regen, poison |
| `GrantAbility` / `GrantFlag` | grant an ability or a tag/flag | waterbreathing, see-invisible, behavioral flags ("mobs more likely to attack") |
| `Trigger` | on a world/combat event, do X | passive procs, "Dream area occasionally rewrites room descriptions", on-hit curses |
| `TransformModifier` | bias a generation/resolution roll | loot luck, "instances entered while cursed bias more rare", crafting quality |

Each **Effect instance** carries the dimensions that every "buff/curse/aura/bonus" needs — so
none of them need their own system:

```
Effect {
  EffectKind Kind;            // one of the six above
  EffectParams Params;        // target ScoreId, magnitude (may scale with Power), aspect, formula ref
  EffectCategory Category;    // registry tag: Buff | Debuff | Curse | Disease | Blessing | Poison | Aura …
  int Power;                  // potency of the source (weak caster vs Tier 6 PC); REQUIRED, computed at apply time
  EffectSource Source;        // who applied it (ability/item/area/mob) — for stacking, dispel, attribution
  EffectGroup? Group;         // composite handle: effects sharing a GroupId apply/tick/dispel as one unit
  EffectLifetime Lifetime;    // Instant | Timed(d) | UntilRemoved(d=-1) | WhileEquipped | WhileKnown | WhilePresent
  StackPolicy Stacking;       // Stack | HighestWins(by Power) | Refresh | UniquePerSource | Replace
  int Phase;                  // resolution order: shields < heals < damage, HoT before DoT
}
```

- **Lifetime decides storage and persistence** — one `EffectsComponent` holds the *standalone*
  effects; the `Lifetime` field alone decides what persists (no two-component split):
  - `WhileEquipped` / `WhileKnown` / `WhilePresent` → **not stored at all**; derived on read from
    the source (worn item, known ability, present area). They survive restart automatically
    because the *source* is persisted and re-derived — no effect data is saved.
  - `Timed` (potion buffs, combat buffs) → held in `EffectsComponent` so the heartbeat can tick
    them, but **not persisted** (drop on relog).
  - `UntilRemoved` (`duration = -1`: curses, quest debuffs, sourceless persistent effects) → held
    in `EffectsComponent` and **persisted** (no live source to re-derive from). Cleared only by an
    explicit action (dispel / cleanse / cure) — never auto-expires.
  - **Persistence is lifetime-filtered, not component-split.** `EffectsComponent` is `[Persistent]`;
    a `[JsonConverter]` on it writes only the `UntilRemoved` entries (`System.Text.Json` honors the
    attribute natively — no new persistence infra). `Lifetime` is the single source of truth for
    what survives a save.
  - **There is no `Permanent` effect.** Permanently changing a base stat (consume a rare material
    → +HP forever) is a **direct state-modification action**, not an effect: it mutates the base
    component once through the progression/attribute write seam (Spine E) and leaves nothing
    behind. Effects only ever *modify* a computed value; they never rewrite base. This is the rule
    that keeps the effect list from growing forever.
- **Stacking** handles "stackable but highest-wins" directly. Timed buffs (potion / spell / skill)
  and auras use `HighestWins` keyed on **`Power`**: a *stronger* haste replaces a weaker active one
  and refreshes duration; a weaker re-apply is ignored. DoTs / HoTs `Stack` `UniquePerSource` (each
  caster ticks independently); equipment `Stack` (slot-bounded); curses `UniquePerSource`. Defaults
  are per source type (see §6 R5), overridable per effect.
- **Power** is the potency of the source — a haste from a weak caster is not a Tier 6 PC's. Every
  effect carries an `int Power` that is both the `HighestWins` comparison key and the scalar that
  scales magnitude / duration. **Power is required and computed at apply time, never hand-passed by
  callers:** effects are constructed only through `IEffectSystem.Apply(target, definition, source)`,
  which evaluates the definition's `PowerScaling` against the source's stats (Attunement / Tier /
  ability rank). Making it a required apply-time output guarantees every caller fulfills the
  calculation without duplicating it (INV-8: the rule lives in the system).
- **Phase** gives the explicit ordering the design requires: a player with both a HoT and a DoT
  resolves the heal first, then the damage, because the HoT effect sits in an earlier phase.

**The big reuse:** *every* application surface produces Effects and flows through one pipeline.

| Source | Lifetime | Example |
|---|---|---|
| Item (worn) | `WhileEquipped` | +20 max HP while worn |
| Potion | `Instant` + `Timed` | heal 25 now + haste 30s |
| Ability (Spine B) | per-ability | kick damage, haste buff, sword-master passive |
| Mob | `UntilCured` / `Trigger` | curse altering stats; on-hit poison |
| Area | `WhilePresent` / `Trigger` | Ichor area → +combat speed; Dream area → rewrite descriptions |
| Aura | `WhilePresent` (group-scoped) | group leader's aura buffs the party |
| Ascension / rare item | *not an effect* | permanent base growth is a direct mutation action (Spine E), never an effect |
| Rarity treatment (Spine D) | grants effects | Champion mob → an aura effect |

**Classification + composites.** Two thin additions make "a curse" a first-class thing without a
new kind or a new layer:

- **`Category`** — every effect carries a registry tag (Buff, Curse, Disease, Aura…). It is *data
  for systems to act on*, not behavior: dispel/cleanse strips by category (`remove curse` →
  `Category=Curse`); AI queries it ("aggro cursed players"); output groups/colors by it; immunity
  keys off it. A handful of systems *read* the tag; none branch on a hardcoded per-category path.
- **Composite (`Group`)** — a named affliction like "Curse of the Wretch" is a
  `CompositeEffectDefinition` (registry) that emits a *group* of primitive effects sharing a
  `GroupId` and `Category` — e.g. `StatModifier(−Strength)` + `GrantFlag(aggro-magnet)` +
  `TransformModifier(rarity bias)`. They apply, tick, and dispel as one unit. A curse is therefore
  **not a primitive kind** — it is a bundle of the six kinds with a name and a type. The same
  composite concept covers blessings, diseases, multi-part auras, and gear set-bonuses.

This stays flat, not a pyramid: one registry type (`CompositeEffectDefinition`) + two fields on
`Effect`, read by ~3 systems. No new layer.

**What Effect is NOT.** Not 500 bespoke effect classes. Not a scripting engine. Not a
`BuffSystem` + `CurseSystem` + `AuraSystem`. If a desired interaction genuinely can't be a
parameterized instance of the six kinds, the answer is a **new kind** (a rare, reviewed code
change), never a new layer of indirection.

**Layer fit.** `IEffectSystem` / `EffectTracker` is a **core system** — it applies, stacks,
orders, queries, and ticks effects without knowing what any effect *means* (this matches the
planned [`IEffectTracker`](../reference/systems-planned.md): "Doesn't know what effects *mean* —
only tracks presence and duration"). `StatModifier` effects are summed at read time by the stat
pipeline ([`IStatSystem`](../implementation-plans/stat-system.md), already built — extend it to fold effects
+ aspect scores). `Periodic`/`Trigger` effects are processed on the heartbeat
([`IHeartbeatService`](../implementation-plans/time-system.md), already built).

---

### Spine D — Scaling / Rarity (the spawn-time transform)

**What it is.** Rarity is **not a type** — a Veteran wolf is not a different entity class. It is a
base template plus a `ScalingTreatment` applied **at spawn time**.

```
RarityTier {                        ScalingTreatment {
  RarityId Id;  // Standard,          double StatBudget;     // multiply scores
  string Name;  // Veteran,           Effect[] GrantedEffects; // e.g. Champion → aura
  ...           // Champion …         LootBias LootWeighting;  // better drops
  RarityBudget Budget;             }  AffinityBias AspectBias; // optional attunement bump
}
```

One transform, three targets — the user's "extend it to items… even extend to areas":

| Target | Transform |
|---|---|
| **Mob** | base template + treatment → scaled scores, granted effects (an aura), better loot table |
| **Item** | base + treatment → affixes (which *are* Effects, Spine C) + value scaling |
| **Area / instance** | rarity *bias* on the generation context → everything inside spawns with higher rarity weight |

The scalar budget is a function of `(base level, ascension tier, rarity)` — **one scaling
function, parameterized**, not a curve per target. And the loop closes with Spine C: a player's
curse can carry a `TransformModifier` effect that biases the area generation context's rarity —
the "cursed players enter more dangerous instances" mechanic falls out for free.

```
IScalingSystem (core)
  void Apply(uint entityId, ScalingTreatment treatment);     // mobs, items
  GenContext Bias(GenContext ctx, IEnumerable<TransformModifier> mods);  // areas/instances
```

**What Scaling is NOT.** Not a subclass per rarity. Not a separate generator per target. Not
baked at author time — it is applied when content is *born* (spawn / generation), so the same
base template yields Standard or Champion depending on the roll.

**Layer fit.** `IScalingSystem` is a **core system** (generic transform). *Choosing* a rarity
(loot tables, magic-find, area bias) is **domain** logic in the spawn/loot/generation systems
that call it — composing the planned `IRandomGeneratorSystem` / `ILootSystem` /
`IItemGeneratorSystem`.

---

### Spine E — Progression (experience-driven growth + objectives)

Two sub-models sharing primitives.

**E1 — Experience-driven growth.** No classic levels. *Every* advanceable score — stats,
abilities, aspect attunements, vitals — has an experience/improvement **track**.

```
IProgressionSystem (domain)
  void AwardExperience(uint entityId, TrackId track, int amount, XpSource source);
  bool TryImprove(uint entityId, TrackId track);   // when a track crosses its curve threshold
```

This generalizes the planned [`IAdvancementSystem`](../reference/systems-planned.md) from "levels"
to "any track." Growth sources unify too: combat kills, objective rewards, and **consuming a rare
magic item** are all `AwardExperience` calls or a **direct base-stat mutation** through the same
write seam. Permanent growth is an *action that rewrites base state once*, never a lingering effect
(Spine C) — the effect system only modifies computed values, it never bakes base.

**E2 — Objectives / quests / gates / encounters.** Quests, mini-boss gates, puzzles, and
daily-collection loops are **one Objective shape**:

```
Objective {
  Condition[] Conditions;   // KillMob(x)×N, CollectItem(y)×N, EnterRoom(r), PuzzleState(s), DailyCooldown
  Reward[]    Rewards;      // XP, item, GrantFlag, GrantAbility, AscensionKeyFragment, unlock
}
```

- **Designed** quests (hand-authored YAML) and **random** sidequests (generated from objective
  templates) are the same shape with different authoring.
- The Ascension example — *3 mini-bosses unlock a room → daily-limited rare material → craft an
  ascension key* — is `Objective`s chained with a `DailyCooldown` flag and an
  `AscensionKeyFragment` reward. No bespoke encounter code.
- The statue-dragging puzzle is `Objective(Condition = PuzzleState(statues in target rooms))`
  fed by **Triggers** (see below).

**Triggers** are the world-scoped twin of Spine C's `Trigger` effect: *world event → state change
/ effect*. They are the substrate for puzzles (statue moved → check state → unlock), daily resets,
encounter gates, and area procs ("Dream area occasionally rewrites descriptions" is a `Trigger`
effect *on the area*). Same primitive, area/world scope.

**Ascension** is a **gated milestone, not a separate engine.** Current tier is a scalar (on a
component — `IdentityComponent.Tier` already exists as the seed). Advancing requires completing an
Ascension Objective (craft + use a key). Ascending raises the scaling baseline (Spine D), unlocks
content/aspects, and may re-theme. It sits at the top of the Objective graph.

**Layer fit.** `IProgressionSystem`, `IObjectiveSystem`, `IAscensionSystem` are **domain
systems**. Objective advancement is driven by **handlers** subscribing to existing events
(`MobDiedEvent`, item-collected, room-entered) — orchestration only, no domain logic in the
handler (INV-1).

---

### Spine F — Registry layer (the lookup spine)

**What it is.** One uniform pattern: each trait family is an `IRegistry<TDefinition>` keyed by a
stable id, with a single resolve point.

| Registry | Holds |
|---|---|
| `IAspectRegistry` | every Aspect definition |
| `IStatRegistry` | the four attributes (Mind, Body, Spirit, Attunement), derived scores, and pools |
| `IAbilityRegistry` | every skill + spell definition |
| `IRarityRegistry` | every rarity tier + budget |
| `IResourceRegistry` | resource pools: HP, Mana, Stamina, Astra (expandable — a new pool is a row, not code) |
| `IAscensionRegistry` | the (6) tiers + their unlock/theme |
| `IObjectiveRegistry` | quest/objective templates |
| (effect *kinds* are an enum + a switch in `EffectSystem`, not a registry — they're behavior, not data) |

Definitions may be **hardcoded** in a module (registered at startup, like DI) **or** authored in
YAML via the existing per-module `ITemplateDeserializer` pattern. Hardcoded is fine and expected
for the spine families. The registry is what makes "definitions spread across modules" a non-issue:
one front door per family.

**What the registry is NOT.** Not an everything-is-data engine. Registries hold *definitions*
(declarative variation); the *kinds and behaviors* stay in code. This is the data/code line
(principle §2.2) made structural.

---

## 4. The overlap map — every surface feature onto the spines

This is the holistic view the whole exercise is for. Read it as: *"feature X is not its own
system — it is spine Y with parameters Z."*

| Surface feature | Spine(s) | Shared primitive it reduces to |
|---|---|---|
| Player elemental attunement | A + C | `StatModifier` on an Aspect affinity/resist score |
| Spell attuned to an element | A + B | `Ability(Aspect)`; damage typed → Aspect resolution |
| Area attunement effect | A + C + E | Area `Aspect` + `WhilePresent` effect + `Trigger` |
| Item grants +HP while worn | C | `Effect(StatModifier, WhileEquipped)` |
| Potion: heal + temporary haste | C | `Effect(Instant)` + `Effect(Timed StatModifier)` |
| Mob curse: alters stats + "mobs attack more" | C | composite `Category=Curse`: `StatModifier(UntilCured)` + `GrantFlag` sharing a `GroupId` |
| Group aura | C | `Effect(WhilePresent)` scoped to the group |
| HoT resolves before DoT | C | effect `Phase` ordering |
| "Highest wins" buff rule | C | `StackPolicy.HighestWins` keyed on effect `Power` |
| Veteran / Champion mob | D (+C) | `ScalingTreatment` (which grants Effects) |
| Magic item affixes | D + C | `ScalingTreatment` → Effects |
| Randomly-generated instance is "more rare" | D | rarity `Bias` on the gen context |
| Cursed player → more dangerous instances | C + D | curse `TransformModifier` biases the gen context |
| Kick / Sword-master / Dodge | B (+C) | `Ability` Active / Passive-`StatModifier` / Triggered |
| Mini-boss gate → daily material → ascension key | E | `Objective`s + `Trigger`s + `DailyCooldown` flag |
| Statue-dragging puzzle | E | `Objective(PuzzleState)` fed by `Trigger`s |
| Consume rare item → permanent power | E | direct base-stat mutation action (not an effect) + XP award |
| Ascension tier-up | E (+D, A) | `Objective` gate → tier scalar → scaling baseline + theming |
| Armor / DR / resistance / speed / damage scores | A + C | computed-on-read base + `StatModifier` effects (aspect-keyed where typed) |

Nineteen surface features; six spines; one effect pipeline doing most of the work.

---

## 5. How it maps to ECS, layers, and the roadmap

### Components (sketch — idealized, fits [02-ecs.md](../architecture/02-ecs.md))

| Component | Holds | Persist | Notes |
|---|---|---|---|
| `AttributesComponent` / `PoolsComponent` (built) | primary attributes + pools (base) | yes | extend with new `ScoreId`s as scores are added; the substrate |
| `AspectAffinitiesComponent` | base `Aspect → affinity/resist` scores | yes | summed with effects on read |
| `AbilitiesComponent` | learned abilities, improvement, cooldowns | yes (cooldowns transient) | generalizes planned `SkillsComponent` |
| `EffectsComponent` | standalone effects (`Timed` + `UntilRemoved`); source-bound effects derived, not stored | yes — lifetime-filtered (`[JsonConverter]` writes only `UntilRemoved`) | one component, not a Persistent/Transient split |
| `ProgressionComponent` | per-track experience | yes | stats/abilities/attunement tracks |
| `ObjectiveLogComponent` | active/done objectives, daily flags | yes | player |
| `AscensionComponent` *or* `IdentityComponent.Tier` | current tier + unlocks | yes | tier scalar already seeded |
| (area) `AreaDataComponent` += `Aspect`, rarity bias | area attunement + generation bias | yes | extends existing planned component |
| combat scores (armor/DR/resist/speed/damage) | **no new stored component** | — | computed on read from base + effects (no caching) |

### Systems by layer

- **Core (generic mechanics):** `IAspectSystem` (resolution), `IEffectSystem`/`EffectTracker`
  (apply/stack/order/tick), `IScalingSystem` (treatment transform), `IRegistry<T>` infra,
  `IDiceSystem` + `IRandomGeneratorSystem` (planned), and `IStatSystem` (built — extended to fold
  effects + aspect scores). *Core systems must not depend on domain systems (INV-2).*
- **Domain (game semantics):** `IAbilitySystem`, `IProgressionSystem`, `IObjectiveSystem`,
  `IAscensionSystem`, and the loot/generation systems that *choose* rarity. They compose core
  systems; domain-on-domain coupling goes through events (INV-2).
- **Handlers / initiators (orchestrate + publish):** combat tick consumes
  ability→effect→aspect; the heartbeat ticks `EffectTracker`; an objective handler advances
  objectives off `MobDiedEvent` / item-collected / room-entered; an ascension handler gates
  tier-up. Systems return results; handlers/initiators publish events (INV-5, INV-8).

### Events (past-tense thin facts — INV per [03-events.md](../architecture/03-events.md))

`AbilityActivatedEvent`, `EffectAppliedEvent`, `EffectExpiredEvent`, `DamageDealtEvent` (carries
Aspect), `ExperienceAwardedEvent`, `ObjectiveAdvancedEvent`, `ObjectiveCompletedEvent`,
`EntityScaledEvent` (at spawn), `AscendedEvent`.

### Suggested slice decomposition (dependency-ordered) → feeds [`plan.md`](../roadmap/plan.md)

Each becomes a use-case doc through the normal loop. Ordering respects what already exists
(combat, heartbeat, stat pipeline are built) and what unlocks the most downstream work.

1. **Aspect foundation** — `IAspectRegistry` + aspect-typed damage + resistance/affinity scores.
   Combat (built) is the first consumer; small, high-leverage.
2. **Effect model** — formalize kinds/lifetime/stacking/phase; graduate
   `EffectsComponent` (single list, lifetime-filtered) + `EffectSystem`. *Most later slices
   depend on this — do it early.* (Reframes/precedes the queued Skills and Potions slices.)
3. **Ability system** — skills first, spells as the aspect/mana variant. (This is the queued
   **slice 11 — Skills**, reframed as one ability pipeline.)
4. **Scaling / Rarity** — mobs first (the queued mob/elite work), then item affixes, then area bias.
5. **Progression** — generalize advancement into experience tracks; add aspect-attunement growth.
6. **Objectives / quests / triggers** — designed + generated; puzzles and gates.
7. **Ascension** — tier-up gating, scaling baseline, theming.
8. **Generated content / overworld / instances** — consumes Aspect + Scaling + Objectives.

Overlaps with the current queue: queued **slice 11 (Skills)** is spine B; **slice 13 (Crafting,
potions)** consumes spine C; the **mob elite/rarity** idea is spine D. Those queued slices should
be specified *against* this model so they land reusable.

---

## 6. Resolved decisions

The §6 forks were resolved with the owner (2026-05-30); the slices inherit them.

| # | Decision |
|---|---|
| **R1 — Ascension** | **Vertical scalar** (tier = 0–6 int) is the spine; **horizontal = theming** via per-area Aspect attunement. `AscensionComponent` stays a scalar + unlock flags. |
| **R2 — Aspect oppositions** | Registry carries an `Opposes` field, but **v1 defines no pairs** and resolution treats aspects independently. A later hook, not a current rule. |
| **R3 — Resources** | Pools: **HP** (no governing stat, own track), **Mana** (Mind), **Stamina** (Body), **Astra** (Attunement). Backed by an expandable `ResourceType` registry. |
| **R4 — Attributes** | **Four:** Mind, Body, Spirit, Attunement (governance table in §3 Substrate). The built `Str`/`Dex`/`Con` stub migrates to these. |
| **R5 — Stacking + Power** | Timed buffs (potion / spell / skill) **and** auras → `HighestWins` keyed on **`Power`**, refreshing on equal-or-stronger re-apply. DoTs / HoTs `Stack` `UniquePerSource`; equipment `Stack`; curses `UniquePerSource`. Per-source defaults, overridable per effect. |
| **R6 — Permanent growth** | **No `Permanent` effect.** Persistent-but-removable = `UntilRemoved` (`duration = -1`). True base growth (rare-material consumption) is a **direct state-modification action** that rewrites base once and leaves no effect (Spine E). |
| **R7 — Docs** | Keep `docs/design/` (taxonomy row added). Scoped-system design → `architecture/subsystems/`; complex systems (Effects) get a higher-level design doc; on ship, a use-case **graduates its design into subsystem / architecture docs** and is trimmed to requirements + implementation plan. Retroactive conversion is a backlogged audit. |
| **R8 — Aspect representation** (2026-06-06) | Elemental identity/affinity is an optional **normalized aspect composition** (`AspectId → weight`; empty, single = 100, or a blend summing to 100), carried by entities, damage packets, and areas. **Resistance is an independent per-aspect score** (base + effects), decoupled from the composition — aspects are semantic tags beyond damage types. `AspectDefinition` is shaped to carry aspect-unique ability/effect riders later (not built in the foundation slice). Lands in the Aspect & Registry Foundation slice ([`../implementation-plans/aspect-foundation.md`](../implementation-plans/aspect-foundation.md)). |

### Power — the one new concept R5 introduced (provisional sub-decisions)

`Power` is the potency of an effect's source (a weak caster's haste ≠ a Tier 6 PC's). It is the
`HighestWins` comparison key **and** scales magnitude / duration, computed at apply time. Two
sub-points carry **provisional defaults** — confirm before the Effect slice is specced:

- **One number, not two.** Provisional: a single `Power` is *both* the stack-rank key and the
  magnitude scalar. Split into separate "potency" and "stack-rank" fields only if a case needs them
  to differ.
- **Computed, not hand-passed (INV-8).** Each effect *definition* declares a `PowerScaling` spec;
  `IEffectSystem.Apply` evaluates it from source context. Provisional: a small registry of named
  scaling formulas suffices; revisit if designers need per-effect expressions.

---

## 7. Related

- [`../architecture/02-ecs.md`](../architecture/02-ecs.md) — computed-stats + two-level effect
  persistence this model extends.
- [`../implementation-plans/stat-system.md`](../implementation-plans/stat-system.md) — the built `IStatSystem` read
  seam that folds in effects + aspect scores.
- [`../implementation-plans/combat.md`](../features/combat/combat.md) / [`../implementation-plans/time-system.md`](../implementation-plans/time-system.md)
  — built consumers (combat tick, heartbeat) of Aspect + Effect.
- [`../reference/systems-planned.md`](../reference/systems-planned.md) — the idealized
  `EffectTracker` / `SkillSystem` / `SpellSystem` / `AdvancementSystem` / `LootSystem` /
  `ItemGeneratorSystem` interfaces this model reconciles into the spines.
- [`../reference/components-planned.md`](../reference/components-planned.md) — target component
  set (`SkillsComponent`, effects components, `MobDataComponent` level effects) this model builds on.
- [`../roadmap/plan.md`](../roadmap/plan.md) — where the spine decomposition lands as slices.
- [`../architecture/checklist.md`](../architecture/checklist.md) — the authoritative invariants
  (`INV-*`) every spine slice is reviewed against.
