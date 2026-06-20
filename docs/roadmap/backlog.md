# Backlog

> Living list of follow-up work that doesn't belong on the active phase plan. For the sequenced rebuild, see [`plan.md`](plan.md). For what's already shipped, see [`done.md`](done.md).

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

## Phase 4 — Hardening

These are tracked for Phase 4. Most become useful only after a handful of Phase 3 slices have stressed the architecture; **testing is the exception — it is active now** (first item below).


### 🟡 Performance: LINQ in hot paths

Becomes meaningful once `LocationSystem` and `CombatSystem` exist and profiling shows real cost. The current hot path is too small to measure usefully.

### 🟡 Thread-safety review

Evaluate after `TimeSystem` exists and concurrency shape is known. May not be needed if the heartbeat stays single-threaded with an event queue.

**Concrete site — per-session output buffer.** The prompt/output-batching slice ([`../implementation-plans/prompt-and-output-batching.md`](../features/output/output.md)) introduces a session-scoped output buffer that three threads can touch concurrently: the player's own command read-loop, *other* players' read-loops (a `say` broadcasting into this session), and the heartbeat background thread (combat/effect/tick output). The buffer must guard its pending list and perform drain-then-append-prompt atomically. This is a known concurrency site to fold into the review (it ships with its own buffer-level lock; the review confirms it composes correctly with the session write lock and the event bus under background-service access).

## Phase 3+ ideas (not yet a slice)

### ~~🔵 Heartbeat / TimeSystem~~ — promoted to slice 9-b

Promoted to an active slice. `IHeartbeatService` + `HeartbeatTickEvent` land as Phase 3 slice 9-b. See [`plan.md`](plan.md) slice queue.

### 🔵 Web / SignalR dual client

If a web client becomes a goal, unify telnet sessions and web sessions behind the existing `ISession` abstraction so handlers don't care about transport. Listed as the deferred slice in [`plan.md`](plan.md). The admin-tooling resolution (in-game commands, not a web UI) makes this strictly optional rather than blocking.

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

Acknowledged debt from the admin-area-authoring slice (`docs/implementation-plans/admin-area-authoring.md`). `ListCommand` hand-rolls `StringBuilder`-based tabular output (header + rows, 15-char description truncation). Two admin commands now share this pattern (`AreaCommand` produces a similar structured listing; `ListCommand` is the second). At the third consumer, extract a shared `TableBuilder` or `ColumnFormatter` helper (INV-19: ≥3-consumer threshold). Until then the inline implementation is intentional.

### 🔵 Atomic multi-file content cascade (acknowledged debt from content-reference-integrity slice)

The content-editor reference-integrity slice ([`../implementation-plans/content-reference-integrity-and-delete.md`](../implementation-plans/content-reference-integrity-and-delete.md)) ships **best-effort** cascade-clear on delete: deleting a referenced definition rewrites every referrer's YAML, then deletes the target file, each write atomic on its own (tmp → rename) but the *set* not transactional. If a rewrite mid-cascade fails (disk error, permissions), earlier rewrites have already landed — leaving a partially-cascaded state. Acceptable for v1: the operation is offline, single-author, loopback-only, and the integrity/health page surfaces any resulting broken link on the next sweep. The full fix — a transactional cascade (stage all edits, commit-or-rollback the set) — lands if/when the editor gains multi-author/concurrent use or the content set grows large enough that a partial cascade is hard to recover by hand.

### 🔵 Locale enhancements

Deferred from slice 5a (bare-bones content spawning). Remaining capabilities:

- **Coordinate system** — a `CoordinateComponent` (`int X, int Y, int Z`) on room entities, enabling map generation and cardinal distance queries.
- **Area-level properties enforcement** — PvP flag, respawn rate, ambient lighting — present on `AreaComponent` but not yet enforced by any slice.

(Room-to-area membership — `RoomComponent.AreaEntityId`, `IAreaSystem`, `setarea`/`area` commands, aspect-affinity YAML, and `RegistryValidationBootstrap` area sweep — landed in the area-model WP-2 slice.)

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

### 🔵 Balance & tuning surface + reference doc

As the gameplay-model spines land (effect Power-scaling, ability costs, rarity/scaling budgets, progression XP curves, character defaults), each introduces tunable numbers — [`../architecture/05-configuration.md`](../architecture/05-configuration.md) **Category 3 (System Math / Balance)**. Today these live as co-located `*Constants` per the config strategy (and `CharacterDefaults`, slice 9-d, is the first set surfaced as settings under the OD-2 promotion trigger). Worth describing as a standalone concern because the knobs accumulate across systems:

- **Reference doc** — a balance catalog (likely `reference/balance.md` or under `design/`) listing every tunable knob, its owning system, current value, and design rationale, so tuning is coherent rather than archaeology across a dozen constant classes.
- **Promotion tracking (OD-2)** — when designer iteration without recompile is needed for a subset, promote those constants to an authored content definition (Category 2), editable by the future content editor.

Not a runtime "module" — balance math stays co-located with its owning system (Category 3); this item is the *documentation + promotion discipline* around it. Becomes worthwhile once 2–3 spines (effects, abilities, scaling) have introduced enough knobs to justify the catalog — likely around slices 11–13.

### 🔵 YAML-authored definition pipeline for the big registry families (deferred from aspect-foundation)

Deferred from the aspect-foundation slice ([`../implementation-plans/aspect-foundation.md`](../features/aspects/aspects.md)), which lands the Spine F registry layer (`IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base) with **hardcoded** definitions only — correct and expected for the spine families per [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine F ("hardcoded is fine and expected").

The deferred work is a **YAML authoring path** for the genuinely content-shaped, string-keyed families (Ability, Effect, and later Objective), analogous to the existing per-module `ITemplateDeserializer` pattern (which today produces `IEntityTemplate` spawn-templates, not trait definitions — so this is an *analogous* seam, not a literal reuse). It carries one real design decision the aspect-foundation slice deliberately did **not** make: **hardcoded-and-YAML coexistence + override/reload order** — when a definition exists in both a code registration and a YAML file, which wins, and how `@reload` re-derives the registry (cf. `ITemplateRegistry.Clear`).

The aspect-foundation generic is built to keep this additive: rows are **instance-held** (not baked into a `static readonly` field), so a future `Reload(rows)` slots in without reshaping the base. Enum-keyed families (Aspect/Score/Resource) are out of scope here — they are fixed code vocabularies and never YAML-authored. Lands when designer-authored content **without recompile** is an actual need (likely alongside a crafting/content-volume slice or the future content editor).

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
