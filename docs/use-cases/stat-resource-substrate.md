# Use Case: Stat & Resource Substrate

**Status:** implemented
**Actors:** Player, Mob, System, Administrator
**Module:** `Core/ECS/Components/` (`AttributesComponent`, `PoolsComponent`), `Core/Modules/Attributes/` (`IAttributeSystem`, `score`, `setplayer`), `Core/Modules/Stats/` (`IStatSystem`, new `IStatRegistry`/`ScoreId`), `Core/Modules/Mobs/` (`setmob`, builder, content writer, template), `Core/Modules/Account/` (character defaults), `Core/Modules/Combat/` (stat references)

**Spine:** gameplay-model **S1** — the substrate every later spine writes to. See [`../design/gameplay-model.md`](../design/gameplay-model.md) §3 Substrate and decisions R3 (resources) + R4 (attributes). **Design lives in the model; this doc is requirements + implementation plan** (per the docs lifecycle — on ship, a `stats` subsystem design doc is authored).

---

## Description

Replace the interim three-stat model (`Strength` / `Dexterity` / `Constitution`, slice 8a) with the gameplay-model's **four primary attributes** — **Mind, Body, Spirit, Attunement** — and add the **three new resource pools** (**Mana, Stamina, Astra**) alongside the existing **HP**. Introduce a `ScoreId` vocabulary and an `IStatRegistry` enumerating every addressable score (attributes, pools, derived scores), and generalize `IStatSystem` into a `ScoreId`-addressable read seam.

This slice builds **only the score/resource model and its read/write seams** — no effects, aspects, abilities, or progression. It exists first because every later spine depends on this surface: Effect (`S2`) `StatModifier`s target a `ScoreId`; Abilities (`S4`) cost a `ResourceType` and are governed by an attribute; Aspect scores (`S3`) are `ScoreId`s; Progression (`S6`) advances scores and pools. Pulling it ahead of death/respawn (slice 10) lets death penalties draw on the finished stat/pool model.

---

## Preconditions

- Slices 8a (`AttributesComponent`, `PoolsComponent`, `IAttributeSystem`, `score`, `setplayer`, `setmob` attribute properties), 9-c (`IStatSystem`), and 9 (combat) are complete.
- Reused/extended: `AttributesComponent` (`Level`, `Strength`, `Dexterity`, `Constitution`), `PoolsComponent` (`MaxHp`, `CurrentHp`), `IAttributeSystem`, `IStatSystem`, `ICombatSystem`, `AccountSystem.CreateCharacterAsync`, `ScoreCommand`/`ScoreDisplayMessage`, `SetPlayerCommand`, `SetMobCommand`, `IMobBuilderSystem.SetAttribute`, `IMobContentWriter`, `MobTemplate`, `IPersistenceSystem`.

---

## Postconditions (requirements)

**Attributes**
- `AttributesComponent` carries `Mind`, `Body`, `Spirit`, `Attunement` (`int`). `Strength`/`Dexterity`/`Constitution` are removed. Starting values come from configuration (see **Configuration** below), not hardcoded literals.
- `Level` is retained but **vestigial** — flagged for supersession by Ascension tier (`S8`). No new feature depends on it.

**Resources**
- `PoolsComponent` carries four pools, each with a current + max value: **HP** (existing `CurrentHp`/`MaxHp`), **Mana**, **Stamina**, **Astra**.
- A `ResourceType` registry/enum exists: `Hp`, `Mana`, `Stamina`, `Astra`. It is the expandable seam R3 calls for (a future pool is a new entry, not new code).
- **HP has no governing attribute** (advances on its own track). Mana↔Mind, Stamina↔Body, Astra↔Attunement are the *governance* associations recorded in `IStatRegistry`; this slice does **not** derive pool maxima from attributes — it stores them as base values (derivation is a later progression/effect concern).

**Configuration**
- Starting attribute and pool values are **not hardcoded** — they bind from a `CharacterDefaults:` section (`IConfiguration` → `CharacterDefaultsOptions`), per [`../architecture/05-configuration.md`](../architecture/05-configuration.md). `appsettings.json` defaults: attributes **10**; HP **100**; Mana/Stamina **50**; Astra **10**. (These are balance values surfaced as settings for tuning-without-recompile — the documented OD-2 promotion trigger; the end-state promotes them to an authored content definition when the content editor lands — see Design notes.)

**Score seam**
- A `ScoreId` vocabulary identifies every addressable score (the four attributes, the four pools' current+max, and the derived scores `AttackPower`/`Defense`).
- `IStatRegistry` enumerates each `ScoreId` with its role (Primary / Pool / Derived) and governing attribute (where any).
- `IAttributeSystem` exposes getters/setters for the four attributes and the four pools (current + max), with the existing `[0, max]` / clamp invariants preserved (INV-8).
- `IStatSystem` exposes typed effective getters for the four attributes plus a **generalized `Get(uint entityId, ScoreId score)`** seam. `GetEffectiveAttackPower` is governed by **Body** (`Body / 2 + MainHand DamageBonus`); `GetEffectiveDefense` is interim-governed by **Body** (`Body / 4`) — flagged, since the dedicated evasion/armor governance is a later slice.

**Consumers**
- `CombatSystem` reads **Body** wherever it previously read `Dexterity` (hit roll, defense). Behavior is otherwise unchanged.
- `AccountSystem.CreateCharacterAsync` attaches the four attributes and four pools using the `CharacterDefaults` configuration values (above).

**Surfaces**
- `score` displays the four attributes and the four pools (current/max).
- `setplayer` sets any of the four attributes and the four pools on a connected player.
- `setmob` / `IMobBuilderSystem.SetAttribute` / `IMobContentWriter` / `MobTemplate` set and round-trip `mind` / `body` / `spirit` / `attunement` and the pools to YAML.

**Persistence**
- Existing persisted `AttributesComponent` rows (carrying `Strength`/`Dexterity`/`Constitution`) load without crashing; the removed fields are ignored by `System.Text.Json` and the new attributes take their configured defaults. **No migration shim** — dev-data reset (nothing live to preserve).

---

## Main flow

### Flow 1 — `score` (player read)
1. Player types `score`. `ScoreCommand` reads `AttributesComponent` + `PoolsComponent` via `IStatSystem`/`IAttributeSystem`.
2. Renders a `ScoreDisplayMessage` with Mind/Body/Spirit/Attunement and HP/Mana/Stamina/Astra (current/max). Absent components → defaults.

### Flow 2 — Character creation defaults
1. `AccountSystem.CreateCharacterAsync` reads `CharacterDefaultsOptions` and attaches `AttributesComponent` + `PoolsComponent` populated from it (defaults: attributes 10; HP 100; Mana/Stamina 50; Astra 10). Persistence is the caller's responsibility (INV-5), unchanged.

### Flow 3 — Admin set (player + mob)
1. `setplayer <name> body 15` → `IAttributeSystem.SetBody`; clamps/persists per existing path; publishes `PlayerAttributeSetByAdminEvent` (new property names).
2. `setmob <bp> mind 12` → `IMobBuilderSystem.SetAttribute` mutates the live entity + `MobTemplate`; `IMobContentWriter.WriteAsync` round-trips YAML; publishes `MobPropertySetByAdminEvent`.

### Flow 4 — Combat reads Body
1. `CombatSystem.ExecuteRound` computes the hit roll and defense from **Body** (via `IStatSystem`) instead of Dexterity. No formula change beyond the stat source.

### Flow 5 — Generalized read (S2 forward-hook)
1. A consumer calls `IStatSystem.Get(entityId, ScoreId.Body)` (or `.AttackPower`, `.ManaMax`, …) and receives the effective value. In this slice the value is base only; `S2` makes the same seam sum `StatModifier` effects with no interface change.

---

## Events fired

| Event | Publisher | Change | Purpose |
|---|---|---|---|
| `PlayerAttributeSetByAdminEvent` (extended) | `SetPlayerCommand` | new property names (`mind`/`body`/`spirit`/`attunement`/`mana`/`stamina`/`astra`) | audit |
| `MobPropertySetByAdminEvent` (extended) | `SetMobCommand` | same new property names | audit |

No new event types. `IStatSystem`/`IAttributeSystem` never publish (INV-5).

---

## Implementation plan — work packages

> **Sub-agent execution.** Each package is sized for an independent run by a limited-context model. **WP-1 lands first** (it defines the seam); **WP-2 and WP-3 depend only on WP-1, not on each other**, so they can run in parallel. The **primary agent runs the architecture review (`architecture-reviewer`, code mode) across the combined diff** after all three land — sub-agents do not self-review.

### WP-1 — Substrate model + read/write seam *(no external consumers)*
- **Scope:** the score/resource model and its seams, nothing that consumes them.
- **Files:** `AttributesComponent.cs` (4 attrs + `Level`), `PoolsComponent.cs` (4 pools, current+max), `ResourceType.cs` (new, `Core/ECS/Components/`, co-located with `PoolsComponent` — no `Resources` module exists, so the simple name is collision-free per the CLAUDE.md namespace/type rule; if such a module is later added, rename then), `ScoreId.cs` + `StatRegistry.cs`/`IStatRegistry` (new, `Core/Modules/Stats/`), `AttributeSystem.cs`/`IAttributeSystem` (getters/setters for attrs + pools, clamps), `StatSystem.cs`/`IStatSystem` (typed effective getters renamed + `Get(entityId, ScoreId)`), `StatsModule`/`AttributesModule` registration.
- **Depends on:** nothing (lands first).
- **Out of scope:** combat, commands, account, content, persistence migration.
- **Exit (testable):** solution builds; `IStatSystem.Get(e, ScoreId.Body)` returns Body; `IAttributeSystem` reads/writes all four pools with clamps intact; `IStatRegistry` enumerates the score set.

### WP-2 — Wire existing consumers + config defaults *(depends on WP-1)*
- **Scope:** point the live consumers at the new seam; supply configurable starting values; confirm existing saves load.
- **Files:** `CombatSystem.cs` (Dexterity→Body for hit/defense), `CharacterDefaultsOptions.cs` (new) + `appsettings.json` keys (`CharacterDefaults:*`), `AccountSystem.cs` (`CreateCharacterAsync` reads the options for the 4 attrs + 4 pools).
- **Depends on:** WP-1.
- **Out of scope:** player/admin/content surfaces (WP-3). **No persistence migration shim** — dev-data reset; `System.Text.Json` defaults the removed fields on load.
- **Exit (testable):** a freshly-created character has four attributes and four pools at the configured defaults (overridable in `appsettings.json`); a combat round resolves using Body; an existing persisted character loads without exception (new attrs defaulted).

### WP-3 — Player / admin / content surfaces *(depends on WP-1)*
- **Scope:** everything a designer/player sees and sets.
- **Files:** `ScoreCommand.cs` + `ScoreDisplayMessage` (show 4 attrs + 4 pools), `SetPlayerCommand.cs` (+ attribute/pool properties), `SetMobCommand.cs` + `IMobBuilderSystem.SetAttribute` + `IMobContentWriter` + `MobTemplate` (`mind`/`body`/`spirit`/`attunement` + pools). **WP-3 owns the consolidated reference-catalog sweep across all three packages:** `components.md` (`AttributesComponent`/`PoolsComponent`), `systems.md` (`AttributeSystem`, `StatSystem`, `CombatSystem`), `commands.md` (`score`/`setplayer`/`setmob` — currently still describe "Strength, Dexterity, Constitution").
- **Depends on:** WP-1 (and WP-2's defaults for a populated display, but not its code).
- **Out of scope:** combat, account, the core seam.
- **Exit (testable):** `score` renders the new model; `setplayer <name> astra 30` and `setmob <bp> spirit 14` work and (for mob) round-trip to YAML; catalogs match the code.

---

## Content tooling impact

- `setmob` gains `mind`/`body`/`spirit`/`attunement` (+ pool) properties; `IMobContentWriter` YAML DTO gains the matching fields (replacing `strength`/`dexterity`/`constitution`). `setplayer` gains the same for live players. This is the authoring + inspection path for the new state, shipped in the same slice (INV-18). `score` is the inspection surface for players; `setplayer`/`setmob` for admins.
- **Editor-forward.** All authoring runs through the builder/writer **systems** (`IMobBuilderSystem`, `IMobContentWriter`, the player attribute setter), never logic bound to the command itself — so the planned full-featured content editor (deferred; Ticket B / [`../roadmap/backlog.md`](../roadmap/backlog.md)) reuses the same systems with no rework. The command is one thin caller; the editor will be another. Properties added here go on the *system*, with the command as a pass-through.

---

## Cross-cutting surfaces stressed

- **Persistence — Acknowledged debt.** Changing `AttributesComponent`'s fields is a breaking change to persisted JSON. The game is not live; resolution is a **dev-data reset** (no shim). `System.Text.Json` defaults the removed fields on load, so even un-reset data loads safely.
- **Commands — Adequate.** `score`/`setplayer`/`setmob` are extended in place; no framework change. Output via existing `ScoreDisplayMessage`.
- **Combat — Modified (in-slice).** `CombatSystem` stat references move from Dexterity to Body; no new combat behavior. WP-2 owns this so the change is reviewed with its persistence siblings.
- **ECS / content templates / output — Adequate.** New fields on existing components; existing YAML deserializer + `ScoreDisplayMessage` extend without new infrastructure.
- **Configuration — Adequate.** Starting defaults bind via the existing `IConfiguration` host (`CharacterDefaults:` / `CharacterDefaultsOptions`), per [`../architecture/05-configuration.md`](../architecture/05-configuration.md); no new config infrastructure. Promotes to a content definition with the editor (backlog).

---

## Flows introduced or modified

- No **new canonical flow** in `flows/README.md`. Modifies the *stat source* inside the existing **Flow 18 — combat round pulse** (Body, not Dexterity) and the *content* of the `score` display. The combat-round flow doc gets a one-line note; no diagram change.

---

## Design notes

- **`ScoreId` is the S2 hook.** The single reason the generalized `Get(entityId, ScoreId)` seam ships now (rather than with effects) is that S2's `StatModifier` targets a `ScoreId`. Building it here means the Effect slice adds effect-summation *inside* `StatSystem` with **no interface churn** for combat/UI consumers. Typed getters are kept as thin wrappers over `Get` so existing call sites are untouched.
- **`Defense` governance is interim.** With Dexterity gone, `GetEffectiveDefense` is provisionally `Body / 4`. A dedicated evasion/armor score and its governing attribute land with the combat-depth/aspect slices; flagged so it isn't mistaken for final.
- **Pools are stored, not derived (yet).** Mana/Stamina/Astra maxima are base values this slice; the Mind/Body/Attunement governance is recorded in `IStatRegistry` but not yet applied as a formula. Derivation is a progression/effect concern (`S6`).
- **`Level` is left in place** to avoid pulling Ascension forward; it is inert and will be superseded by the tier scalar (`S8`).
- **Persistence: dev reset, no shim.** `System.Text.Json` ignores the removed `Strength`/`Dexterity`/`Constitution` keys on load and defaults the new attributes. Nothing is live to preserve, so there is **no migration shim** — dev character data is reset.
- **Starting defaults are Category-3 balance, surfaced as settings.** Starting attributes/pools are balance values ([`../architecture/05-configuration.md`](../architecture/05-configuration.md) Category 3) surfaced via `IConfiguration` (`CharacterDefaults:`) so the owner can tune them without a recompile — this slice is the OD-2 "promote when iteration is needed" trigger. The documented end-state promotes them to an authored content definition (Category 2) once the content editor exists; the `CharacterDefaultsOptions` shape stays simple to make that migration cheap. See the balance-surface backlog item.
- **Editor-forward authoring.** Authoring logic lives in the builder/writer systems, not the commands, so the future content editor reuses them (see Content tooling impact).
- **On ship:** author `architecture/subsystems/stats.md` (the living design of the stat/score/resource system) and trim this doc to requirements + the durable behavior spec (docs lifecycle, R7).

---

## Resolved planning inputs

Settled with the owner (2026-05-30):
1. **Starting defaults** — configurable via `CharacterDefaults:` (appsettings): attributes 10, HP 100, Mana/Stamina 50, Astra 10.
2. **Persistence** — dev-data reset; **no migration shim** (nothing live to preserve).
3. **Defense** — interim `Body / 4`; revisited with the evasion/armor score in a later slice.

---

## Related

- [`../design/gameplay-model.md`](../design/gameplay-model.md) — S1 design (§3 Substrate, R3, R4); this doc implements its substrate.
- [`attributes.md`](attributes.md) — slice 8a; the components/system this slice migrates.
- [`stat-system.md`](stat-system.md) — slice 9-c; `IStatSystem` generalized here.
- [`combat.md`](combat.md) — slice 9; consumes Body via the seam (WP-2).
- [`mobs.md`](mobs.md) — slice 8; `setmob`/builder/writer/template extended (WP-3).
- [`account-character-creation.md`](account-character-creation.md) — slice 5; creation defaults (WP-2).
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; the field-migration consideration.
- **Next:** `effect-substrate.md` (S2) — the bedrock that targets this slice's `ScoreId`s.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
