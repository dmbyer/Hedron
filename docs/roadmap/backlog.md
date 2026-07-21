# Backlog

> Living list of follow-up work that doesn't belong on the active phase plan. For the phase strategy and current focus, see [`plan.md`](plan.md). For what's already shipped, see [`done.md`](done.md).

Status markers: 🟢 ready · 🟡 blocked · 🔵 deferred

## Docs refinement & cleanup (✅ complete)

Multi-session program restructuring the docs into the feature/system taxonomy, consolidating flows, trimming reference catalogs, and moving to the disintegrate-on-ship plan lifecycle. Governing spec: [`../architecture/09-documentation.md`](../architecture/09-documentation.md). Per-feature packages (WP-3…N) are self-contained and may run in any order once WP-1/WP-2 land.

- [x] **WP-1 — Structure + spec.** `docs/features/` skeleton + README; renamed the use-cases folder to `implementation-plans/`; rewrote `architecture/09-documentation.md` (moved from docs root), `checklist.md` (`INV-28`/`INV-30`), `CLAUDE.md`, `00-overview.md` doc-map, `.claude/README.md`.
- [x] **WP-2 — Templates + `manage-docs` skill + tooling repoint.** 6 templates in `.claude/skills/manage-docs/templates/`; new `manage-docs` skill; repointed `sync-roadmap` (disintegrate-on-ship), `implementation-planner`, `implement-plan`, `architecture-reviewer`, `architecture-advisor`, `.claude/README`, `add-*`. Agent/command *file names* kept (repoint-only); rename later if desired.
- [x] **WP-3…N — Per-feature migration.** All 13 features migrated to `docs/features/<feature>/` (feature + system docs); flows consolidated into feature journeys; reference catalogs trimmed (interface dumps → `.cs` links); every implemented plan disintegrated + deleted with decisions verified in `roadmap/completed/`.
  - [x] **effects** (4.8 exemplar) — `features/effects/effects.md` + `effect-system.md` (← `architecture/effects.md`); flow-21 de-detailed → "Effects journey"; `EffectSystem`/`AbilityEffectContributor` reference interface-dumps trimmed to links; `effect-substrate.md` plan deleted (decisions verified in `completed/slice-9e`).
  - [x] **manifests** authored + consumed (the transient `docs-refinement-manifests.md` map was deleted at WP-Z once all features had migrated).
  - [x] **combat** (4.6) — `features/combat/` (combat.md + combat-system / death-system / entity-state); flow-17 → Combat journey, flow-20 → Death & respawn journey (18/19/22/23 deleted); reference trimmed; 3 plans deleted.
  - [x] **character-stats** (4.6) — `features/character-stats/` (4 docs; stat-system ← subsystems/stats.md); 4 plans deleted; reference trimmed.
  - [x] **items** (4.6) — `features/items/` (items + item-inventory-system + equipment-system); flow-09 → Items journey, flow-13 → Equipment journey (10/11/14 deleted); 2 plans deleted.
  - [x] **world** (4.6) — `features/world/` (world + world-content/movement/area-model/spawn/time-system; added missing MovementSystem ref row); 4 plans deleted; infra flows kept.
  - [x] **mobs** (4.6) — `features/mobs/` (mobs + mob-system); mobs.md plan deleted.
  - [x] **abilities** (4.6, finished by 4.8 after the sub-agent hit a session limit) — `features/abilities/` (abilities + ability-system); flow-24 → Abilities journey (25/26 deleted, broken `.cs` links fixed to real files); 2 plans deleted.
  - [x] **aspects** (4.6) — `features/aspects/` (aspects + aspect-system); aspect-foundation.md plan deleted.
  - [x] **accounts** (4.6) — `features/accounts/` (accounts + account-system + login-flow); Core/Sessions reference gap closed; flow-07 → Login journey; account plan deleted.
  - [x] **admin-authoring** (4.6) — `features/admin-authoring/` (admin-authoring + admin-commands/content-authoring/content-tooling); flow-08 → Admin authoring journey, flow-29 → Content-tooling journey (12/15/27/28/30 deleted); 3 plans deleted (admin-area-authoring + admin-privilege-elevation kept).
  - [x] **output** (4.6) — `features/output/` (output + output-framework ← subsystems/output.md + prompt); flow-06 → Output journey; BroadcastSystem/Output-infra dumps trimmed; 2 plans deleted.
  - [x] **commands** (4.6) — `features/commands/` (commands + command-framework ← subsystems/commands.md); flow-03 → Command journey; INV-11 checklist link fixed; 2 plans deleted.
  - [x] **communication** (4.6) — `features/communication/` (communication + chat-system + help-system); no plans/flows; reference trimmed. **All 13 features migrated.**
- [x] **WP-Z — Closing sweep.** `subsystems/` removed (all 3 docs migrated into `features/`); `Core/Sessions` reference home added; the 3 implemented infra plans (persistence-substrate, persistence-two-level-model, testing-harness-and-backfill) disintegrated into `06-persistence.md`/`07-testing.md` + their `completed/` records and deleted; transient `docs-refinement-manifests.md` removed; `flows/README.md` finalized (feature journeys retitled). Repo-wide link check clean (only the deliberate `plan.md → shopping.md` "to be authored" placeholder remains). The 5 cross-cutting runtime-infra flows (`flow-01/02/04/05/16`) were de-detailed to the systems/events altitude (kept numbered, titles unchanged).

## Phase 4 — Hardening (residual items)

Phase 4's standing concerns resolved into per-slice discipline: **testing + CI shipped and are enforced on every PR** (INV-25, [`../architecture/07-testing.md`](../architecture/07-testing.md)); the **thread-safety review was closed 2026-07-17** by converting it into the standing invariant **INV-31** (declared concurrency posture, reviewer-enforced — see [`../architecture/checklist.md`](../architecture/checklist.md) and [`04-pitfalls.md` "Threading Model"](../architecture/04-pitfalls.md#threading-model--cross-thread-state); the per-session output buffer's three-writer concurrency shipped guarded with output-batching and is a named INV-31 precedent). What remains here are the two bounded engineering items:

### 🟡 Performance: LINQ in hot paths

Becomes meaningful when profiling shows real cost — likely once content volume and population make `LocationSystem`/`CombatSystem` sweeps non-trivial. Nothing measured yet.

### 🟡 World-state threading model (ECS component storage)

The one open decision left from the retired thread-safety review (the rest became INV-31): live ECS component storage (`ComponentRepository`'s nested `Dictionary`s) is **unguarded**, while per-session command threads and the heartbeat thread both read and mutate live world state concurrently. It hasn't bitten because structural mutations cluster at startup/login/admin actions — but it is a latent race, not a proven-safe design. Two candidate fixes, one decision:

- **Guard:** make `ComponentRepository` internally thread-safe (fine-grained locks or `ConcurrentDictionary`), accepting that logical read-modify-write races (two threads both damaging HP) remain and are handled per-system.
- **Marshal:** funnel all live-world mutation onto a single game-loop/queue (commands enqueue; the heartbeat thread drains), making world state single-writer by construction — the classic MUD model, also the enabler for the deferred live-edit content editor.

Decide before any feature that adds a *new* concurrent writer to world state (mob AI on its own scheduler, live web editing, instanced-content workers); until then INV-31's "don't widen the exposure" clause holds the line. Marshal is the architecturally cleaner endpoint; guard is the cheaper patch.

## Phase 3+ ideas (not yet a slice)

### 🔵 Crafting & potions (punted out of the MVP, 2026-07-17)

Formerly Phase 3 slice 13 ("Crafting, potions" — content depth on items + inventory). **Deliberately punted out of the MVP scope** when the content-baseline → MVP strategy was set ([`plan.md`](plan.md) Phase 5/6): crafting is content *depth* that doesn't gate the core play loop (explore → fight → progress → trade → die/recover), and its real fan-out (gathering, materials, stacking, recipes, stations — see [`../design/feature-horizon.md`](../design/feature-horizon.md) §9) deserves its own advisor-framed program rather than a single squeezed slice. Potions themselves remain the canonical cheap Spine-C consumer and could ship earlier as a consumables-only slice if healing economy needs them. Re-frame with `/advise` when scheduled — likely during or after Phase 6 (it pairs naturally with loot/salvage and the economy sinks).

### ~~🔵 Heartbeat / TimeSystem~~ — promoted to slice 9-b

Promoted to an active slice and shipped. `IHeartbeatService` + `HeartbeatTickEvent` landed as Phase 3 slice 9-b — see [`done.md`](done.md).

### 🔵 Web / SignalR dual client

If a web client becomes a goal, unify telnet sessions and web sessions behind the existing `ISession` abstraction so handlers don't care about transport. Deferred to the Phase-7 scale-out in [`plan.md`](plan.md). The admin-tooling resolution (in-game commands, not a web UI) makes this strictly optional rather than blocking.

### 🔵 Broadcast channel mode (global / newbie chat)

Acknowledged debt from Phase 3 slice 4 ([`../implementation-plans/output-framework.md`](../features/output/output.md)). Slice 4's broadcast expansion ships room-scope-with-audience-filter and system-wide `SendToAllAsync`, but **channel mode** (global/newbie chat membership) is deferred: it requires per-entity channel-membership state that no slice has introduced yet. Lands with whichever later slice introduces channel membership (likely alongside or after account / character creation, slice 5). The `IBroadcastSystem` interface shaped in slice 4 should accommodate a `SendToChannelAsync` addition without breaking the room/system modes.

### 🔵 Command-arg log redaction (acknowledged debt from slice 3)

`CommandExecutedEvent.ArgsSummary` ([`../implementation-plans/command-framework.md`](../features/commands/commands.md)) logs parsed args in plaintext. Slice 3 ships with no redaction — acceptable only because the sole free-text verb is `say` and the logger is local. **Prerequisite for any retained/forwarded log sink.** Proposed fix: a per-command `[NoLogArgs]` / `RedactArgs` declaration the dispatcher honors before building `ArgsSummary`. Lands with whichever slice first adds a non-local logging sink, an auth-bearing verb (`password`, account linking), or `tell`/private channels — whichever comes first.

### 🔵 CommandPipeline middleware refactor (deferred smell from slice 3)

`CommandDispatcher` carries five injected dependencies and owns authorization, parsing, output, event publication, and exception trapping (spec-mode review smell S1). A middleware/pipeline chain would isolate these concerns. Deferred from slice 3 to avoid ballooning the 12-command refactor. Revisit when a sixth concern would be added to the dispatcher, or if testing the dispatcher becomes painful.

### 🔵 Combat action-economy & command queue (acknowledged debt from slice 11-b)

Slice 11-b ([`../implementation-plans/ability-invocation.md`](../features/abilities/abilities.md)) lets an offensive ability fire immediately (cooldown-gated) — so an actor already in combat gets the ability strike **plus** the heartbeat auto-attack in the same ~2s tick (no one-ability-per-round metering). Intentional and bounded for 11-b's "minimal combat touch." The full action economy — a per-actor combat command **queue** (max ~10, with a `clear` verb), one-combat-ability-per-round, immediate-first-then-metered, cooldown-blocks-queue, plus the Speed-attribute / Action-Points scaling that paves the way to an optional real-time combat mode — is its own follow-up use-case (gameplay-model combat depth). Lands when combat depth is scheduled.

### 🔵 Combat depth — resolution & reactions (follow-up to slice 11-b)

The combat-flavored ability mechanics 11-b deliberately deferred: **hit/miss/partial-success** resolution, distinct **offensive vs defensive ratings**, and **triggered** abilities (dodge/parry/riposte) wired into the round with a stat-scaled trigger chance. 11-b ships only a defense-mitigated landed strike (no to-hit roll) and carries the `Triggered` activation mode as data-not-wired. Lands as one or two combat-depth use-cases after the ability cluster.

### 🔵 Configurable / richer resource regeneration (deferred from slice 11-c)

Slice 11-c ([`../implementation-plans/resource-regeneration.md`](../features/character-stats/character-stats.md)) ships flat, **hardcoded** out-of-combat regeneration (idle 1/pool/3-ticks; resting ~3×) so ability resource costs are recoverable. Surfacing the rates as configuration — and the richer model (per-area/terrain rates, stat-derived regen, food/effect interaction, a "fully rested" notification) — is a dedicated regeneration use-case that depends on a more robust configuration model (a separate backlog concern). Until then the constants live isolated in `RegenerationSystem` for a cheap later promotion.

### 🔵 Tabular output helper — defer until third consumer

Acknowledged debt from the admin-area-authoring slice (`docs/implementation-plans/admin-area-authoring.md`). `ListEntitiesCommand` (the admin `listents` verb) hand-rolls `StringBuilder`-based tabular output (header + rows, 15-char description truncation). Two admin commands now share this pattern (`AreaCommand` produces a similar structured listing; `ListEntitiesCommand` is the second). At the third consumer, extract a shared `TableBuilder` or `ColumnFormatter` helper (INV-19: ≥3-consumer threshold). Until then the inline implementation is intentional.

### 🔵 Migrate combat + ability targeting onto `MobInRoomResolver` (deferred from shopping, slice 12c)

The shopping slice (12c) relocated `MobInRoomResolver` to the shared `Core/Modules/Mobs/Resolvers/` home and wired the shopping `list` command to it as the optional `shopkeeper` argument resolver — but that is currently its **only** active consumer. `KillCommand` and `AbilityInvocationPipeline`/`UseAbilityCommand` still resolve room mobs through the inline `ICombatSystem.TryFindTargetInRoom` path; the shopping `buy`/`sell` verbs resolve the implicit shopkeeper directly (no named argument, so a resolver doesn't fit them). So the INV-19 "≥3-consumer" threshold the original slice plan cited is **not yet genuinely crossed** — the relocation is ahead of need but harmless (one real consumer, shared home).

The deferred work: migrate `KillCommand` and the ability-targeting pipeline from inline `TryFindTargetInRoom` to `MobInRoomResolver` (binding it as their target argument resolver), at which point the resolver has three genuine consumers and the extraction is fully justified. Lands when combat/ability command-argument resolution is next touched, or sooner if a third explicit mob-targeting argument appears. See [`completed/`](completed/) shopping record and [`../features/combat/combat-system.md`](../features/combat/combat-system.md).

### 🔵 Atomic multi-file content cascade (acknowledged debt from content-reference-integrity slice)

The content-editor reference-integrity slice ([`completed/content-editor-integrity.md`](completed/content-editor-integrity.md)) ships **best-effort** cascade-clear on delete: deleting a referenced definition rewrites every referrer's YAML, then deletes the target file, each write atomic on its own (tmp → rename) but the *set* not transactional. If a rewrite mid-cascade fails (disk error, permissions), earlier rewrites have already landed — leaving a partially-cascaded state. Acceptable for v1: the operation is offline, single-author, loopback-only, and the integrity/health page surfaces any resulting broken link on the next sweep. The full fix — a transactional cascade (stage all edits, commit-or-rollback the set) — lands if/when the editor gains multi-author/concurrent use or the content set grows large enough that a partial cascade is hard to recover by hand.

**Update (blueprint-id-editing).** The blueprint-id rename cascade (`RenameAsync`, [`../implementation-plans/blueprint-id-editing.md`](../implementation-plans/blueprint-id-editing.md)) shares this **exact** best-effort posture — it cascade-*rewrites* `oldId → newId` across referrers (the same traversal delete uses, generalized so clear = rewrite-to-empty), writes the new-id file, then deletes the old, none of it transactional. Covered by the same transactional-cascade fix; no separate work.

### 🔵 Live-world / persistent blueprint-id rename (deferred from blueprint-id-editing)

The blueprint-id-editing slice ([`../implementation-plans/blueprint-id-editing.md`](../implementation-plans/blueprint-id-editing.md)) ships **YAML-only** rename in the offline editor: it rewrites every YAML reference to the old id, and the live world adopts the new ids on the next `reload`. References that live **outside** YAML are deliberately **warned, not rewritten** — `LocationComponent.RoomBlueprintId` (persistent player/item state, SQLite) and `World:StartingRoomBlueprintId` (`appsettings.json`). A player saved in a renamed room falls back to the starting room via `CharacterHydrationHandler`'s existing unresolvable-`RoomBlueprintId` recovery; the config must be hand-updated. This matches the editor's YAML-only posture (INV-22/23) and delete's existing behavior.

The deferred work is a rename that reaches **beyond YAML**: rewriting SQLite `LocationComponent.RoomBlueprintId` on persistent entities, re-keying the live `TemplateRegistry`, and/or a telnet `rename` command that mutates the running world so a rename lands without a `reload` and without orphaning parked players. It crosses INV-22/23 and the editor's YAML-only boundary, needs its own persistence + concurrency design (it mutates the persistent-entity domain and would add a new live-world writer — relates to the [world-state threading-model decision](#-world-state-threading-model-ecs-component-storage) above), and pairs naturally with the [live / instant-preview editing](#-full-featured-content-editor-transition-from-command-driven-authoring) deferral. Lands when live-world rename (or a config-aware rename) is an actual need rather than a `reload`-tolerable inconvenience.

### 🔵 Locale enhancements

Deferred from slice 5a (bare-bones content spawning). Remaining capabilities:

- **Coordinate system** — a `CoordinateComponent` (`int X, int Y, int Z`) on room entities, enabling map generation and cardinal distance queries. The **authoring-side half** (optional `X/Y/Z` on `RoomTemplate` YAML) lands with the visual grid area editor ([`completed/world-editor-grid.md`](completed/world-editor-grid.md)); this item retains the runtime component (reading that same YAML field), cardinal-distance queries, and any exit↔coordinate consistency *enforcement* (the editor treats coordinates as advisory layout).
- **Area-level properties enforcement** — PvP flag, respawn rate, ambient lighting — present on `AreaComponent` but not yet enforced by any slice.

(Room-to-area membership — `RoomComponent.AreaEntityId`, `IAreaSystem`, `setarea`/`area` commands, aspect-affinity YAML, and `RegistryValidationBootstrap` area sweep — landed in the area-model WP-2 slice.)

### 🔵 Multi-area world-view grid editor (deferred from world-editor-grid)

The visual grid area editor ([`completed/world-editor-grid.md`](completed/world-editor-grid.md)) is deliberately scoped to one area at a time, with each area its own local coordinate space. A cross-area "world view" — rendering multiple areas on one map with inter-area exits — needs the runtime coordinate system (above) plus an overworld/locale design decision (per-area origin offsets or a shared world space; see [feature-horizon §1](../design/feature-horizon.md)). Per-area local coordinates compose with a future per-area origin offset, so the deferral forecloses nothing. Revisit when overworld/wilderness travel or the world map is scheduled.

### ✅ Equipment slot expansion — shipped (wearable-equipment-expansion)

Resolved. The wearable-equipment-expansion slice added 9 `WornSlot` values — `Legs`, `Hands`, `Arms`, `Waist`, `Neck`, `Finger`, `Finger2`, `Wrist`, `Wrist2` (doubled rings/wrists are distinct enum values; no model change) — alongside the worn-gear stat-contribution seam. Further slots remain a pure enum + YAML extension if content ever needs them.

### 🔵 In-game item-definition inspector (`defs item` / `iteminfo`)

`DefsCommand` inspects only the registry families (aspect/ability/effect/score); items are `TemplateRegistry` templates with no telnet read-back of authored fields (`StatBonuses`, `WornSlots`, `DamageBonus` before it). Today an item's authored bonuses are inspected via the Blazor content editor + the `setitem` confirmation echo. A dedicated in-game inspector — either a `defs item <blueprintId>` family or an `iteminfo` admin command dumping `ItemDataComponent`/`ItemTemplate` fields — is a small, self-contained follow-up. Surfaced during wearable-equipment-expansion (the parity bar was the Blazor editor, which the slice updated to the new `StatBonuses` rows). Revisit when builders need at-the-keyboard item inspection without the web editor.

### 🔵 Subtype-based argument matching ("get sword" = any sword)

Deferred from slice 6 (`items-and-inventory.md`). `ItemType` enum lands as data on `ItemDataComponent` in slice 6, but no special matching behavior uses it. A future `ItemTypeArgumentResolver` could resolve `"sword"` → all entities of `ItemType.Weapon` with keyword "sword". Requires clarifying whether sub-type matching is a command-level concern (different resolvers per command) or a global upgrade to `IArgumentResolver`. Revisit when content needs it or when the keyword-matching miss rate in play-testing becomes notable.

### 🔵 Multi-step command prompts and player config

Deferred from slice 7 design notes. Two related capabilities:
- **State-machine prompts**: a command can have confirmation steps (e.g. "You are already wearing X. Replace it? [yes/no]"). Requires per-session prompt state beyond what the current I/O loop supports.
- **Player config**: per-character preferences (e.g. `autoswap yes`, `autoconfirm itemswap`). Requires a `PlayerConfigurationComponent` (planned in `components-planned.md`) and a `config`/`set` player command.

Both are meaningful improvements to UX but would bloat slices 6–7. Revisit when the number of "are you sure?" flows justifies the infrastructure cost.

### ~~🟢 `IOptions<T>` sweep — typed config options across Core~~ — completed

Surfaced during slice 10 architecture review. Every configuration block with multiple consumers in `Core/` should be bound via a typed options class + `services.Configure<T>(configuration.GetSection("X"))` rather than via raw `IConfiguration["X:Key"]` reads scattered across constructors. Raw reads have no IDE navigation, no compile-time safety, and duplicate default values across files.

**Known sites** (as of slice 10):

| Config section | Files using raw reads | Typed class exists? |
|---|---|---|
| `Death:` | `DeathSystem`, `DeathTickHandler`, `AttributeSystem` | ✅ `DeathOptions` — **wired in slice 10**; `AttributeSystem` cross-module dependency flagged below |
| `World:` | `WorldContentLoader`, `RoomContentWriter`, `ItemContentWriter`, `MobContentWriter` | ✅ `WorldOptions` — **wired; raw reads replaced** |
| `Persistence:` | `PersistenceSystem`, `PersistenceFlushTimer` | ✅ `PersistenceOptions` — **wired; raw reads replaced** |
| `CharacterDefaults:` | `AccountSystem` | ✅ `CharacterDefaultsOptions` — **wired; raw reads replaced** |

**Env-var override for data paths.** `Program.cs` registers the `HEDRON_` environment variable prefix via `cfg.AddEnvironmentVariables("HEDRON_")`. To point all worktrees at a machine-local world outside the git tree set these env vars (using `__` as the section separator on Windows):
```
HEDRON_World__ContentDirectory=C:\Users\dmbye\hedron-world\content\
HEDRON_Persistence__DatabasePath=C:\Users\dmbye\hedron-world\hedron.db
```

**Remaining follow-up: `AttributeSystem` cross-module dependency.** `AttributeSystem` still injects `IOptions<DeathOptions>` to read the HP-floor clamp — a cross-module dependency (`Attributes` → `Death`). Two options remain:
- **(a)** Move `HpFloor` to an `AttributeOptions` class so `AttributeSystem` owns its floor config independently. Requires adding `Attributes:HpFloor` to `appsettings.json` and keeping it in sync with `Death:HpFloor`.
- **(b)** Remove the floor clamp from `AttributeSystem` entirely (`SetCurrentHp` clamps to `[0, MaxHp]`) and have `DeathSystem.OnHpChanged` enforce the floor. Requires updating `DeathTickHandler` (which re-reads HP after the `SetCurrentHp` clamp) and the `AttributeSystem` tests that cover the HpFloor clamp invariant.

Option (b) is the cleaner layer but touches the death pipeline. Not yet resolved.

### 🟢 Persistence save-on-change cleanup + manual `save`/`quit` commands

Follow-up from the death-and-respawn slice (slice 10), where INV-22 was reworded to name **three** permitted `SaveEntityAsync` boundary categories — construction, admin boundary, session-end. Two cleanup items and two new commands remain:

**Cleanup — migrate stray runtime saves to the flush.**
- `WearCommand` and `RemoveCommand` (`Core/Modules/Items/Commands/`, lines ~90/~80) call `SaveEntityAsync` after an equip/unequip. Equipment changes are ordinary runtime inventory mutations and do **not** warrant an immediate save — drop these calls and let the periodic flush cover them.
- Audit `CharacterHydrationHandler` (`Core/Modules/Account/Handlers/`, ~line 70), which calls `SaveEntityAsync` in its startup error-recovery path (unresolvable `RoomBlueprintId` → reset to starting room → persist the correction). Decide whether this is a legitimate startup/hydration boundary (if so, name it as a fourth INV-22 category) or should be restructured. It is currently the one `SaveEntityAsync` site that does not fit the three named categories.

**New `save` command (admin).** An admin-gated command that forces an immediate persistence write, with arguments selecting scope: a specific player and/or the world. Player save → `SaveEntityAsync(playerEntityId)` (admin boundary save, paired with an audit event). "World" save → a full flush (`FlushAllAsync`) and/or YAML write of authored content (exact scope to be designed). Admin-gated; audited.

**New `quit` command (player).** A player command that force-saves the player (session-end boundary save) and then disconnects gracefully. Today a raw disconnect is already force-saved by `PlayerSessionHandler`; `quit` makes the player-initiated graceful exit explicit. **Cross-ref:** when this lands it should be flagged `UsableWhileIncapacitated = true` so an incapacitated/dying player can still quit — the death-and-respawn slice ([`../implementation-plans/death-and-respawn.md`](../features/combat/combat.md)) deliberately omitted `quit` from its allowlist because no `quit` command existed yet.

### 🔵 Mob death / respawn approach (INV-21)

When mob combat death lands (slice 9+), the death/respawn slice must decide:

- **Reset in place:** HP restored on the same entity; `BlueprintComponent` is never touched. Simple; entity ID is reused, so save-file references to the dead entity slot survive.
- **Destroy and re-seed:** The dead entity is destroyed; a new entity is spawned from the `MobTemplate`. `BlueprintComponent` is **not** explicitly cleared before `DestroyEntity` — INV-21 says `BlueprintComponent` is preserved as an origin record and must **not** be cleared on mob death or item pickup; it disappears naturally when the entity is destroyed. Spawn-slot vacancy is tracked by `SpawnSystem` via domain events (`MobDiedEvent`), not by checking `BlueprintComponent` on live entities.

The chosen approach must be called out explicitly in the death/respawn use-case doc's Design Notes.

### 🟢 EffectsComponent persistence — RESOLVED (single list, lifetime-filtered) → slice 9-e

**Decision (2026-05-30).** One `EffectsComponent` with a **single `List<Effect>`** of standalone effects — no `Persistent`/`Transient` component split, and no two-list split. Persistence is **lifetime-filtered**: the component is `[Persistent]`, and a `[JsonConverter]` on it writes only entries whose `Lifetime == UntilRemoved`. `System.Text.Json` (already used by `ComponentSerializer`) honors the attribute natively — **no new persistence infrastructure**. Source-bound effects (`WhileEquipped`/`WhileKnown`/`WhilePresent`) are not stored at all — derived on read from their persisted source. Rationale: two near-identical components were duplication; `Lifetime` is already the single source of truth for what survives a save. Design in [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine C; built in slice 9-e ([`../implementation-plans/effect-substrate.md`](../features/effects/effects.md)).

**Reference-sweep when 9-e lands.** These docs use the old two-component names as examples/stubs and must reconcile to the single `EffectsComponent` (already updated with the decision: the model, `02-ecs.md`, `components-planned.md`, and `.claude/skills/add-archetype/SKILL.md`): `architecture/06-persistence.md` (excluded-component example), `architecture/flows/flow-04-persistence-flush-cycle.md`, `reference/archetypes.md` (Weapon/Armor optional component), `implementation-plans/persistence-substrate.md` (example).

### 🔵 General mob loot table — weighted item + currency drops (Spine D, deferred from currency-foundation)

The currency-foundation slice ([`../features/economy/currency-loot-system.md`](../features/economy/currency-loot-system.md)) ships an **opt-in `CurrencyLootComponent`** — a configurable currency range rolled and auto-awarded to the killer on `MobDiedEvent`. That is a deliberately **narrow instance** of the general **Mob loadouts & loot tables** feature (gameplay-model Spine D; [`../design/feature-horizon.md`](../design/feature-horizon.md) §6): weighted drop tables that roll **items** (with rarity/affixes via the Scaling spine) *and* currency, optionally scaled by mob rarity, optionally landing on a corpse/pile rather than auto-awarded.

The deferred work is the general `LootComponent` + `ILootSystem` (weighted tables, item generation, rarity scaling) and the corpse/pile-and-pickup path it implies. The currency-foundation seam is shaped to keep this **additive, not a rewrite**: its `CurrencyLootHandler` subscribes to `MobDiedEvent` and deposits via the shared `IWalletSystem`, so a future general `LootHandler` slots in as a peer/superset on the same event, with currency loot becoming one contributor to the table. Lands when item drops + the Scaling/rarity spine (slice queue / Spine D) are scheduled; premature before items roll affixes and corpses exist.

### 🔵 Multi-currency item value (deferred from item-value, slice 12a)

`ItemDataComponent.Value` ships as a single base-unit `long` denominated in the launch currency (**Coin**). Items priced in a non-Coin family (faction marks, an Astral currency) need the field to carry a `CurrencyId`. The field is shaped to grow — a later migration adds the currency key **without moving the value off `ItemDataComponent`** or changing the compute-on-read price derivation. Premature while Coin is the sole trade currency; revisit when a second tradeable currency family or a non-Coin vendor is real. See [`completed/item-value.md`](completed/item-value.md) and the [items feature](../features/items/items.md).

### 🔵 Category-granular effect immunity (deferred from mob-protection, slice 12b)

`ProtectionComponent.EffectImmune` is an all-or-nothing axis: an effect-immune entity rejects **every** effect, beneficial or harmful. Selective immunity (immune to curses only, vulnerable to blessings, etc.) aligns with the effect system's "immunity keys off `Category`" note ([`../features/effects/effect-system.md`](../features/effects/effect-system.md)). The two-axis `[Flags]` model is shaped to extend — a per-`EffectCategory` mask replaces the single bool without changing the gate sites (`IEffectSystem.Apply`, the combat check). Lands when content needs an entity protected from a *subset* of effect categories. See [`completed/mob-protection.md`](completed/mob-protection.md) and the [mobs feature](../features/mobs/mobs.md#protection-invulnerability--immunity).

### 🔵 Per-shop pricing overrides (deferred from shopping, slice 12c)

The sell / buy-back price ratio ships as an app-wide `ShopOptions` value — every shop applies the same spread over an item's `Value`. A per-shop override (a luxury vendor that pays less, a black-market fence that pays more) is an optional field on `ShopComponent` the price calc prefers over the global default. Deferred to keep the shopping slice's config surface flat; `ShopComponent` is shaped to carry the override unused. Revisit when shops need to differ economically. See [`../features/economy/shop-system.md`](../features/economy/shop-system.md) and [`completed/shopping.md`](completed/shopping.md).

### ✅ Balance & tuning surface + reference doc — shipped (Progression & Balance program, closed by `prog-5`)

As the gameplay-model spines land (effect Power-scaling, ability costs, rarity/scaling budgets, progression XP curves, character defaults), each introduces tunable numbers — [`../architecture/05-configuration.md`](../architecture/05-configuration.md) **Category 3 (System Math / Balance)**. Today these live as co-located `*Constants` per the config strategy (and `CharacterDefaults`, slice 9-d, is the first set surfaced as settings under the OD-2 promotion trigger). Worth describing as a standalone concern because the knobs accumulate across systems:

- **Reference doc** — a balance catalog (likely `reference/balance.md` or under `design/`) listing every tunable knob, its owning system, current value, and design rationale, so tuning is coherent rather than archaeology across a dozen constant classes.
- **Promotion tracking (OD-2)** — when designer iteration without recompile is needed for a subset, promote those constants to an authored content definition (Category 2), editable by the future content editor.

Not a runtime "module" — balance math stays co-located with its owning system (Category 3); this item is the *documentation + promotion discipline* around it. Becomes worthwhile once 2–3 spines (effects, abilities, scaling) have introduced enough knobs to justify the catalog — likely around slices 11–13.

**Resolved (2026-07-17).** Fulfilled by slice `prog-5` ([`completed/balance-doc-layer.md`](completed/balance-doc-layer.md)): the balance catalog shipped at [`../design/balance.md`](../design/balance.md) (every tunable knob by home, observability surfaces, the maintenance contract) with the `balance-tuning` skill as the operational how-to. The OD-2 promotion-tracking discipline is the catalog's maintenance contract; a planned `run-simulation` skill was deliberately dropped (sim runs are a designer/admin surface — the dev-loop case is a `balance-tuning` recipe).

### 🔵 Simulation harness → real mob-AI adapter (deferred from the progression-and-balance program)

The balance-simulator program's **sim-2 simulation engine** (`Core/Modules/Simulation/`, shipped) drives each actor's per-round choice through a **combatant-policy seam** (`ISimCombatantPolicy`). Sim-2 ships **simple built-in policies only** — `melee-only`, `round-robin`, `cooldown-first` — sufficient for balance sweeps against the current shallow combat model. When mob AI lands (threat tables, behavior trees — see [`../design/feature-horizon.md`](../design/feature-horizon.md) §6), a thin adapter binds the real `IAISystem` behind the same `ISimCombatantPolicy` seam so simulated combatants behave like live ones. Additive by construction (the seam exists from sim-2); premature before any `IAISystem` exists. Lands alongside or after the mob-AI slice.

### 🔵 Ascension tier baseline has no real combat effect (calibration gap found by sim-2)

Discovered 2026-07-15, the first time the tier baseline was ever exercised through a real
simulated fight (`SimulationInvariantTests.OneBandHigher_ReferenceBuild_WinRate_PinnedPendingBalanceTuning`,
`Hedron.Tests/Simulation/`): `AscensionEffectContributor` folds the tier baseline onto
`ScoreId.Body`/`HpMax` via `IStatSystem.Get` (`AscensionConstants.TrackedScores`), but
`StatSystem.GetEffectiveAttackPower`/`GetEffectiveDefense` read the **raw** `AttributesComponent.Body`
(not `Get(Body)`), and `CombatSystem`'s HP/death check reads the **raw** `PoolsComponent` values (not
`Get(HpMax)`). So a reference build's tier baseline currently has **zero** measurable effect on real
combat outcomes — a one-tier-higher reference build wins at the same rate as an equal-cell fight
(pinned at 53% against a design-target 65% floor at the fixed CI seed). This is pre-existing shipped
behavior (Ascension, prog-2/prog-3b), not a sim-2 regression; sim-2 pinned today's number rather than
silently recalibrating `AscensionConstants`/`PowerBudgetTunables` or patching `StatSystem`, since
either fix is a live-gameplay balance decision outside a plumbing slice's scope. A future
balance-tuning slice must decide: (a) extend `AscensionConstants.TrackedScores`/the contributor so
the baseline also folds into `AttackPower`/`Defense` (and reads `Get(HpMax)` for the death/pool
check), or (b) recalibrate `HigherBandWinRateFloor` down to a value the current mechanic can actually
clear, or (c) some combination. See [`completed/simulation-engine-core.md`](completed/simulation-engine-core.md)
for the discovery record.

### 🔵 Live balance-standards reload (deferred from balance-standards-registry, sim-1)

The balance-standards registry's oracle-tunables composition happens once, at DI-construction time (`PowerBudgetSystem`'s ctor-injected `PowerBudgetTunables`, sourced from `IBalanceStandardsRegistry`) — the correct shape for a snapshot-only, no-service-dependency oracle (INV-2, [`../design/power-model.md`](../design/power-model.md)). A saved Standards-page edit therefore applies on the **next host start** (restart-to-apply), not immediately; the page states this. A live-reload path — either a `reload`-triggered re-composition of the registry/oracle singletons, or a provider indirection (`IPowerBudgetTunablesProvider` the oracle re-reads per call) — was evaluated at sim-1 planning and rejected for that slice: it works against the resolved ctor-injection shape and the editing cadence (occasional balance tuning, not live iteration) doesn't yet justify it. Revisit if/when balance tuning becomes frequent enough during a single session that a restart between edits is real friction — likely alongside or after the sim-2/sim-3 engine + editor integration, once designers are iterating against real sim output. See [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) and [`completed/balance-standards-registry.md`](completed/balance-standards-registry.md).

### 🔵 Player-owned instance reconform sweep (deferred from the balance-simulator program, prog-4)

INV-21's default is correct: admin template mutations never retroactively update player-owned instances. But the balance workbench vision (the now-shipped `prog-4` balance-simulator program — see [`conformance-tooling.md`](completed/conformance-tooling.md)) includes bringing already-owned items into conformance when their blueprints are rebalanced — otherwise a balance pass leaves legacy out-of-band gear in player hands forever. **Policy resolved at the prog-4 advisor intake (2026-07-13): this becomes a *named, admin-triggered, audited* exception to INV-21** — an explicit "reconform owned instances of blueprint X" sweep (opt-in per change, never automatic, paired with an audit event), in the same spirit as INV-22's named boundary saves. Deferred to its own slice because it mutates the persistent-entity domain (SQLite-backed player inventory) and needs its own careful spec: matching instances via `BlueprintComponent.BlueprintId`, deciding which fields conform vs. which player-specific state survives, and the INV-21/INV-22 checklist amendments. Nothing in the balance-simulator program builds on it; lands after the program's conformance slice (sim-5) proves the template-side fit math.

### 🔵 Mob projection-vs-spawn attribute defaulting divergence (surfaced at sim-5 planning)

`MobTemplate.Apply` defaults zero attributes to 10 at spawn, while `IMobPowerProjectionSystem` (and therefore the balance audit, the editor readout, and the sim-5 conformance fitter) reads **raw** template values — so a template authoring an attribute as 0 projects weaker than it spawns. Pre-existing since prog-3b; the sim-5 spec gate flagged that the conformance fitter becomes the divergence's first *writer*: a fitted mob with an authored-zero attribute spawns at 10, slightly stronger than the power it was conformed to. Internally consistent today (the fitter scales exactly what the flagging audit saw), but reconcile eventually: either project with the same spawn-defaulting rule, or validate/refuse authored-zero attributes at the catalog. Small, self-contained; natural to fold into any slice touching `MobPowerProjectionSystem` or mob template validation. See [`completed/conformance-tooling.md`](completed/conformance-tooling.md) Decisions and `MobTemplate.Apply`.

### 🔵 Progression-rate sweep — event-source generalization (forward notes from sim-4 planning)

Sim-4, as shipped ([`completed/progression-rate-scenarios.md`](completed/progression-rate-scenarios.md)), models exactly one XP event kind: combat kill → one call to the real `IProgressionSystem.AwardCombatExperience` per modeled event, victim = `Sides[1]`, cap = `maxKillsPerRun`. Captured at planning time (2026-07-16) so later progression work lands additively — no course correction is needed now; the kill-specific shape is the deliberate single-source special case (scenario YAML has one serializer, `SimScenarioStore`, and is transient designer data — cheap to evolve at the second source).

- **New XP sources (skill/ability use, crafting, books/trainers).** The executor's kill-event loop generalizes to a modeled-event-source list on `ProgressionSettings` (per-source rate + parameters; the victim side becomes the *combat source's* parameter, since only combat needs an opponent for the anti-grind ratio), each event dispatched to the same real seam the live handler calls. Because the sweep executes the real `IProgressionSystem` — never re-derived math — award-amount, anti-grind, and threshold changes inside Progression are swept correctly with **zero** sim changes. **Alignment trigger:** when the ≥3-source advancement-**rule table** lands in Progression (the three-layer advancement-triggers model — [`edit-progression-system`](../../.claude/skills/edit-progression-system/SKILL.md) layer 3), repoint the sweep's event model at the same `XpSource`/rule vocabulary **in the same slice** — bespoke per-source executor branches drifting from the rule table is the one real drift risk. At that point the `TargetTrack ∈ ProgressionConstants.CombatTracks` validation also becomes source-derived ("awardable by the scenario's modeled sources") — a one-line relaxation.
- **Time-to-tier targets.** Already pre-shaped in the plan: a second target discriminator on `ProgressionSettings`, activating when an XP/objective-based ascension gate exists (`IAscensionSystem.CanAscend` is the named seam — see the unlock-grant entry above). The sandbox graph already composes `AscensionSystem`; whatever gate lands is measured as implemented, not modeled.
- **Non-combat domains (crafting-rate, gathering, …).** A domain whose award path reads new systems needs those composed into `SandboxWorldFactory` (additive — it already hand-builds the graph). If a domain's rate question outgrows the `ProgressionRate` kind, the plan's recorded third-kind trigger (executor-strategy seam in the runner + per-kind report payload sections, `SchemaVersion` 2) is the landing zone.

### 🔵 Progression-rate expectation tolerances (deferred from sim-4)

`ISimOutcomeEvaluator.EvaluateProgressionRate` ships exactly one standards-free verdict (`targetReached` — did the sweep complete under `maxKillsPerRun`) and one **permanently-skipped** verdict (`progressionRateExpectation`), reason naming this gap. Sim-4's Design notes ("Verdicts: descriptive-first, with the gap named on every report") deliberately did not invent kills-to-improvement tolerance numbers — the sim-1 posture puts expected-outcome tolerances in the balance-standards document, but no designer has ever stated a progression-rate expectation, so authoring one now would ship speculative authored state (INV-18) nobody can ground. Promote when real observed rates exist (post-sim-4 usage against live content): add a tolerance family to `IBalanceStandardsRegistry`'s document (mirroring `OutcomeTolerances`'s equal-cell/higher-band shape, e.g. an expected kills-to-improvement range per (Tier, Band) cell or per track), extend the standards-editor page, and flip `EvaluateProgressionRate`'s second verdict from skipped to a real pass/fail against it — only the evaluator and the standards store/editor change (INV-19 by construction, same seam sim-2's combat verdicts already prove out). See [`completed/progression-rate-scenarios.md`](completed/progression-rate-scenarios.md) Decisions and [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) (the standards registry this would extend).

### 🔵 Balance-reviewer agent (stretch — from the progression-and-balance program)

A `.claude/agents/balance-reviewer.md` (analogous to `architecture-reviewer`) that, when a slice touches balance-affecting numbers (a new ability, item affix, mob scaling, progression curve), runs the relevant sim-2 simulation-engine sweep and flags outcome outliers against the tier power bands — the automated backstop that keeps balance a living featureset through content expansion. Floated in the Progression & Balance program (agentic layer — see [`completed/balance-doc-layer.md`](completed/balance-doc-layer.md)) as a **stretch candidate**, not committed: its prerequisites now exist (the sim engine and the [`../design/balance.md`](../design/balance.md) catalog); what's still missing is enough content to make the sweeps meaningful. Build when balance regressions from expansion become a real, recurring cost. Kin to the on-demand architectural-debt-sweep agent below (heavy-context, out of the per-slice loop).

### 🔵 `setprogress` admin mutation (deferred from progression-substrate, slice 1)

The [`progression-substrate`](completed/progression-substrate.md) slice ships the accrual path + the `progress` inspector, which fully exercise and observe per-track XP/improvement — but **no admin command to hand-set** a player's progression. A `setprogress`-style verb (set a track's XP / improvement count) is the admin **boundary save** pattern (mutate via `IProgressionSystem` → `SaveEntityAsync` → audit event, INV-22), mirroring `setplayer`/`setrespawn`. Deferred because nothing in slice 1 needs hand-set progression; lands when a designer needs to seed a fixture (e.g. a mid-progression test character) without grinding. See [`completed/progression-substrate.md`](completed/progression-substrate.md).

### 🔵 Ascension unlock-grant execution seam + Objective gate (deferred from ascension, slice prog-2)

The [`ascension`](completed/ascension.md) slice ships only the unlock-*record* seam: `AscensionComponent.GrantedUnlocks` + `IAscensionSystem.GetGrantedUnlocks` + `AscendedEvent`, with an **empty** `AscensionConstants.UnlocksForTier` table. Two things are deferred:

1. **Grant-execution seam.** `GrantFlag`/`GrantAbility` are unimplemented `EffectKind` enum values (`Core/Modules/Effects/Effect.cs`) — there is no callable "grant X to entity" path yet. When concrete unlock content is designed (aspects/abilities/flags), it wires into `TryAscend`/`AscendedEvent` without changing this slice's shape.
2. **Player-facing Ascension-Objective gate.** The only trigger today is the admin `ascend` command; `IAscensionSystem.CanAscend` is deliberately shaped as the seam a future objectives slice (`IObjectiveSystem`, currently unbuilt) will call.

Also deferred: the **selection UX** for specialization-on-ascend. (Item tier-bands, mobs-only in prog-2, shipped alongside the power-budget oracle in prog-3 — see [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md).) See [`completed/balance-doc-layer.md`](completed/balance-doc-layer.md) for the program-level disposition.

### 🔵 Player-facing `consider` danger-gauge (deferred from prog-3 power model)

The power-budget-inspector slice (prog-3, shipped — see [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md)) gates `power`/`powerband` to admin/designer — raw balance internals (power scalars, band anchors) stay out of players' hands. Players still want a *rough* danger read before engaging ("how deadly is this fight?"). That is a deferred, **decoupled** player command `consider <mob>` that is a **thin consumer of the same `IPowerBudgetSystem` oracle**: gather the player's and the target's effective scores into two snapshots, call `Estimate`/`Classify` on each, and map the *relationship* (power ratio / band delta) to a coarse **diegetic label** ("trivial / even / dangerous / deadly") — never surfacing the raw numbers. The public `Estimate`/`Classify`/`BandAnchor` interface prog-3 shipped already suffices with **no interface change**, so the capability is preserved without building it now (restraint — no in-slice consumer). If the danger-label logic later wants a shared home, add a small comparison helper on `IPowerBudgetSystem` *then*, when `consider` is the consumer. Lands when a player-facing threat-assessment verb is scheduled. Recorded per the owner's decision to keep the big picture (prog-3 resolved slice Q3).

### 🔵 YAML-authored definition pipeline for the big registry families (deferred from aspect-foundation; instance #1 shipped sim-1)

Deferred from the aspect-foundation slice ([`../implementation-plans/aspect-foundation.md`](../features/aspects/aspects.md)), which lands the Spine F registry layer (`IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base) with **hardcoded** definitions only — correct and expected for the spine families per [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine F ("hardcoded is fine and expected").

The deferred work is a **YAML authoring path** for the genuinely content-shaped, string-keyed families (Ability, Effect, and later Objective), analogous to the existing per-module `ITemplateDeserializer` pattern (which today produces `IEntityTemplate` spawn-templates, not trait definitions — so this is an *analogous* seam, not a literal reuse). It carries one real design decision the aspect-foundation slice deliberately did **not** make: **hardcoded-and-YAML coexistence + override/reload order** — when a definition exists in both a code registration and a YAML file, which wins, and how `@reload` re-derives the registry (cf. `ITemplateRegistry.Clear`).

The aspect-foundation generic is built to keep this additive: rows are **instance-held** (not baked into a `static readonly` field), so a future `Reload(rows)` slots in without reshaping the base. Enum-keyed families (Aspect/Score/Resource) are out of scope here — they are fixed code vocabularies and never YAML-authored. Lands when designer-authored content **without recompile** is an actual need (likely alongside a crafting/content-volume slice or the future content editor).

**Update (sim-1, 2026-07-13).** The balance-standards registry (`IBalanceStandardsStore`/`IBalanceStandardsRegistry`, [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md)) is **instance #1** of this pattern: a focused, hand-rolled YAML load/validate/save path outside `IContentDefinitionCatalog` (a single-document criteria file, not a per-blueprint family) and outside the compiled-rows `DefinitionRegistry` construction. Its Load/Validate/Save shape was written to be extractable when instance #2/#3 (Ability/Effect definitions going YAML) prove the ≥3-instance trigger — still not yet crossed (one real instance).

**Update (sim-3, 2026-07-16).** `SimScenarioStore` gaining `SaveAsync`/`List` ([`../features/simulation/simulation-engine.md`](../features/simulation/simulation-engine.md)) is **instance #2**: a second hand-rolled per-family YAML save path, same posture as instance #1. Still two families — below the ≥3 trigger; this entry still stands.

### 🔵 Web background-job service generalization (promotion trigger recorded, sim-3)

`SimulationRunService` (`Hedron.Web/Services/`, [`../architecture/08-blazor.md`](../architecture/08-blazor.md) "Background tooling jobs") is the web host's first background-job pattern: a singleton FIFO queue with per-run status, progress, and cooperative cancellation over a long-running engine call. It is deliberately sim-specific — a generic web-job framework now would be premature (INV-19's bar is ≥3 instances, or a new player-facing surface *framework* need; this is one designer-facing instance).

**Promotion trigger:** if sim-5's bulk conformance apply (or any second long-running editor job) wants the same queue/progress/cancel shape, generalize `SimulationRunService` into a shared web-job service rather than hand-rolling a second one. Until then, leave it as-is.

### 🔵 `EffectParams.Aspect` migration to `AspectComposition?` (deferred from aspect-foundation)

`EffectParams.Aspect` (in `Core/Modules/Effects/Effect.cs`) remains a `string? Aspect = null` stub. The aspect-foundation slice migrated `AbilityDefinition.Aspect` to `AspectComposition?` but deferred `EffectParams.Aspect` to avoid diff expansion; a `// TODO migrate` comment marks the site. Migrate when an effect-based aspect consumer is introduced — the field is currently unused for damage typing.

### 🔵 `ResourceType` data-authored expandability (deferred from aspect-foundation)

The gameplay-model intends resource pools to be "expandable — a new pool is a row, not code" ([`../design/gameplay-model.md`](../design/gameplay-model.md) Spine F / R3). Today `ResourceType` is a closed enum (`Hp`, `Mana`, `Stamina`, `Astra`), and the aspect-foundation slice keeps it that way — folded onto the registry generic as `IRegistry<ResourceType, …>`, enum-keyed, which is the right call while pools are a fixed developer vocabulary (compile-time safety, small/stable persistence surface).

The deferred work is migrating `ResourceType` from an enum to a **string-keyed** registry row **if and when** a new pool needs to be added as data rather than code — at which point it inherits the same string-key trade-offs as the Ability/Effect families (data-authorable, persisted-by-reference, validated at startup rather than compile time) and the YAML pipeline entry above. Premature until a concrete need for a non-developer-defined pool appears; revisit alongside that need.

### 🔵 Full-featured content editor (transition from command-driven authoring)

Content authoring today is command-driven (`mkmob`/`setmob`/`mkitem`/`setitem`/`dig`/`set`, …); a full-featured editor is a known future (Ticket B resolution in [`plan.md`](plan.md): in-game commands first, web/desktop editor deferred alongside the SignalR/dual-client transport). To keep that transition cheap, the established convention — reinforced by slice 9-d — is that **all authoring logic lives in builder/writer *systems*** (`IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem`, `*ContentWriter`), with the command as a thin caller. The editor becomes a second thin caller of the same systems; no authoring logic is trapped in command classes. New content-mutating features must add their logic to a system, not a command body. Revisit building the editor itself once the dual-client transport lands (it shares that deferral).

**Update (2026-06).** The **offline, file-authoring portion** of this editor is being activated via the [`../implementation-plans/content-tooling-platform.md`](../features/admin-authoring/admin-authoring.md) brief: an in-process **Blazor Server** editor that reads/edits/validates/writes the YAML content definitions, applied to the live world through the existing `reload` path. That brief factors a shared **content-definition layer** out of the builders/writers (so the editor isn't a re-implementation) and a callable `IContentValidator` out of `RegistryValidationBootstrap`. What remains deferred from *this* item is **live / instant-preview editing** — mutating the running world from the editor without a `reload` — which needs world-mutating work marshaled onto the single-threaded game loop (see "Thread-safety review" above). Revisit live-edit once instant preview is an actual need.

### 🔵 REST / public content API

Deferred from the [`../implementation-plans/content-tooling-platform.md`](../features/admin-authoring/admin-authoring.md) brief. That platform hosts **Blazor Server in-process** with the engine, so the authoring UI calls the content-definition systems **directly via DI** — no HTTP/REST layer is needed, and none is built. A REST (or SignalR) **public** content API — for external/programmatic third-party access, a separately-hosted front-end, or CI tooling that can't run in-process — becomes worthwhile only if one of those consumers is real. When it lands it is a thin transport adapter (a new Initiator kind) over the **same** content-definition systems the Blazor host and bulk generator already use; it must not re-implement authoring logic, and it carries its own auth + DTO surface. Distinct from the deferred "Web / SignalR dual client" item, which is the *player-facing* transport. Revisit when an out-of-process content consumer is concrete.

### 🔵 Blueprint-scan index (`BlueprintComponent` lookup by blueprint ID)

The pattern `foreach (var (id, bp) in entityService.GetAllComponents<BlueprintComponent>()) { if (bp.BlueprintId == target) ... }` has reached 11+ call sites after the area-model slice (added in `AreaCommand`, `SetAreaCommand`, `RoomBuilderSystem`, `WorldContentLoader`). Each scan is O(n) over all blueprint-bearing entities.

Promote when the per-command overhead is measurable or when a new feature adds a 12th site: add a `TryResolveEntity(string blueprintId, out uint entityId)` method on `EntityService` (or a thin `IBlueprintIndex` singleton maintained by `EntityService`). This is a pure optimization; correctness is unaffected today at MUD-scale entity counts.

### 🔵 Archetype catalogue refresh

The archetype list in [`../reference/archetypes.md`](../reference/archetypes.md) was written against the old component shapes. Re-audit once a few Phase 3 slices have landed real components.

### ✅ Use-case → feature/system-doc conversion (superseded by the docs-refinement program)

**Done.** This audit was the seed of the docs-refinement program above. Every implemented use-case's design was disintegrated into its [`../features/<feature>/`](../features/) feature + system docs (not the old `subsystems/`, which was absorbed into `features/`); the use-case folder became `implementation-plans/` with a disintegrate-on-ship lifecycle; `sync-roadmap` and the checklist (`INV-28`) were updated to make it binding. See [`../architecture/09-documentation.md`](../architecture/09-documentation.md).

### 🔵 Use-case catalogue audit

The 17 scenarios from before the strip were retired. Use cases are now authored one at a time as each slice begins. Periodically audit the catalogue for gaps or scenarios that have become obsolete.

### 🔵 On-demand "architectural-debt sweep" agent

A `.claude/agents/debt-sweep.md` (or similar name) that walks the codebase looking for repeated hand-rolled patterns that should be promoted to a framework. Heavy-context agent; **on-demand only** — not part of the per-slice loop. Periodic sanity check, not an integral development gate.

Detection heuristics it should run:

- ≥3 files with the same shape of inline argument parsing (`Trim()`/`Split()`/manual `Enum.TryParse`) → command-framework regression candidate.
- ≥3 files with the same shape of session output formatting (`session.SendLineAsync($"{prefix} {body}")`) → output-framework promotion candidate.
- ≥3 files iterating `[Persistent]`-tagged components with identical loops → core-helper candidate.
- New player-facing surface (verb, prompt, output type) introduced without an `ICommand` / `ICommandDispatcher` / `IOutputMessage` / equivalent registration → infrastructure-discipline-parity violation.
- A `.claude/skills/*.md` or `.claude/agents/*.md` that references a rule, path, or pattern no longer matching [`../architecture/checklist.md`](../architecture/checklist.md) or the code → stale-tooling candidate (`INV-20`). The spec for this lives in [`../architecture/09-documentation.md`](../architecture/09-documentation.md).

Output: a punch list of promotion candidates with evidence (file:line for each instance) and a recommended slice to absorb the work. Does **not** modify code or docs — surface only.

The slice-by-slice ground rule 9 check (implementation-planner + architecture-reviewer) is the integral development-cycle defence; this agent is the periodic backstop for whatever slips through. Build when there's been enough drift to make it useful — likely after several Phase 3 slices have shipped.

## Done — moved out of this file

The following items were on the backlog and have shipped or been superseded. Kept here as a brief note so old links resolve cleanly:

- **Persistence substrate** — shipped as Phase 3 slice 1. See [`completed/slice-1-persistence-substrate.md`](completed/slice-1-persistence-substrate.md).
- **`System.Text.Json` adoption** — shipped with the persistence substrate; `ComponentSerializer` already uses `System.Text.Json` end-to-end. No external JSON library is in use.
- **Post-Phase-1 docs drift sweep** — completed alongside Phase 1.5 (Ticket A) and folded into [`completed/phase-1-strip.md`](completed/phase-1-strip.md).
- **`.claude/README.md` index** — now exists at [`../../.claude/README.md`](../../.claude/README.md).
- **Admin UI scope (Ticket B)** — resolved. In-game admin commands (telnet) ship as part of slice 2; a web/desktop editor remains optional and is folded into the deferred SignalR/dual-client slice. See [`plan.md`](plan.md) "Resolved tickets".
- **`Hedron.Tests` harness + backfill** — shipped as Phase 4 testing slice. `Hedron.Tests` project + full shared harness, architecture-guard suite, `IClock` seam, Wave 1 + Wave 2 backfill (566 tests), CI workflow. See [`completed/testing-harness-and-backfill.md`](completed/testing-harness-and-backfill.md).
- **CI wiring** — shipped alongside the testing harness. `.github/workflows/ci.yml` runs build + test on every PR. See above.
- **Injected clock seam (`IClock`)** — shipped as WP-3 of the testing harness slice. `IClock`/`SystemClock` in `Core/Systems/`; `SpawnSystem` and `AccountSystem` refactored off `DateTime.UtcNow`. INV-26 time clause fully satisfied; wall-clock guard enabled in the architecture-guard suite.
