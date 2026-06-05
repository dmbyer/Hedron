# Backlog

> Living list of follow-up work that doesn't belong on the active phase plan. For the sequenced rebuild, see [`plan.md`](plan.md). For what's already shipped, see [`done.md`](done.md).

Status markers: 🟢 ready · 🟡 blocked · 🔵 deferred

## Phase 4 — Hardening

These are tracked for Phase 4. They become useful only after a handful of Phase 3 slices have stressed the architecture.

### 🟡 Test framework (xUnit)

Low value until Phase 3 slices produce real systems and handlers to test. Adding earlier risks locking in shapes we haven't lived with yet.

### 🟡 CI wiring

Build-and-test on PR once the test framework lands.

### 🟡 Performance: LINQ in hot paths

Becomes meaningful once `LocationSystem` and `CombatSystem` exist and profiling shows real cost. The current hot path is too small to measure usefully.

### 🟡 Thread-safety review

Evaluate after `TimeSystem` exists and concurrency shape is known. May not be needed if the heartbeat stays single-threaded with an event queue.

**Concrete site — per-session output buffer.** The prompt/output-batching slice ([`../use-cases/prompt-and-output-batching.md`](../use-cases/prompt-and-output-batching.md)) introduces a session-scoped output buffer that three threads can touch concurrently: the player's own command read-loop, *other* players' read-loops (a `say` broadcasting into this session), and the heartbeat background thread (combat/effect/tick output). The buffer must guard its pending list and perform drain-then-append-prompt atomically. This is a known concurrency site to fold into the review (it ships with its own buffer-level lock; the review confirms it composes correctly with the session write lock and the event bus under background-service access).

## Phase 3+ ideas (not yet a slice)

### ~~🔵 Heartbeat / TimeSystem~~ — promoted to slice 9-b

Promoted to an active slice. `IHeartbeatService` + `HeartbeatTickEvent` land as Phase 3 slice 9-b. See [`plan.md`](plan.md) slice queue.

### 🔵 Web / SignalR dual client

If a web client becomes a goal, unify telnet sessions and web sessions behind the existing `ISession` abstraction so handlers don't care about transport. Listed as the deferred slice in [`plan.md`](plan.md). The admin-tooling resolution (in-game commands, not a web UI) makes this strictly optional rather than blocking.

### 🔵 Broadcast channel mode (global / newbie chat)

Acknowledged debt from Phase 3 slice 4 ([`../use-cases/output-framework.md`](../use-cases/output-framework.md)). Slice 4's broadcast expansion ships room-scope-with-audience-filter and system-wide `SendToAllAsync`, but **channel mode** (global/newbie chat membership) is deferred: it requires per-entity channel-membership state that no slice has introduced yet. Lands with whichever later slice introduces channel membership (likely alongside or after account / character creation, slice 5). The `IBroadcastSystem` interface shaped in slice 4 should accommodate a `SendToChannelAsync` addition without breaking the room/system modes.

### 🔵 Command-arg log redaction (acknowledged debt from slice 3)

`CommandExecutedEvent.ArgsSummary` ([`../use-cases/command-framework.md`](../use-cases/command-framework.md)) logs parsed args in plaintext. Slice 3 ships with no redaction — acceptable only because the sole free-text verb is `say` and the logger is local. **Prerequisite for any retained/forwarded log sink.** Proposed fix: a per-command `[NoLogArgs]` / `RedactArgs` declaration the dispatcher honors before building `ArgsSummary`. Lands with whichever slice first adds a non-local logging sink, an auth-bearing verb (`password`, account linking), or `tell`/private channels — whichever comes first.

### 🔵 CommandPipeline middleware refactor (deferred smell from slice 3)

`CommandDispatcher` carries five injected dependencies and owns authorization, parsing, output, event publication, and exception trapping (spec-mode review smell S1). A middleware/pipeline chain would isolate these concerns. Deferred from slice 3 to avoid ballooning the 12-command refactor. Revisit when a sixth concern would be added to the dispatcher, or if testing the dispatcher becomes painful.

### 🔵 Combat action-economy & command queue (acknowledged debt from slice 11-b)

Slice 11-b ([`../use-cases/ability-invocation.md`](../use-cases/ability-invocation.md)) lets an offensive ability fire immediately (cooldown-gated) — so an actor already in combat gets the ability strike **plus** the heartbeat auto-attack in the same ~2s tick (no one-ability-per-round metering). Intentional and bounded for 11-b's "minimal combat touch." The full action economy — a per-actor combat command **queue** (max ~10, with a `clear` verb), one-combat-ability-per-round, immediate-first-then-metered, cooldown-blocks-queue, plus the Speed-attribute / Action-Points scaling that paves the way to an optional real-time combat mode — is its own follow-up use-case (gameplay-model combat depth). Lands when combat depth is scheduled.

### 🔵 Combat depth — resolution & reactions (follow-up to slice 11-b)

The combat-flavored ability mechanics 11-b deliberately deferred: **hit/miss/partial-success** resolution, distinct **offensive vs defensive ratings**, and **triggered** abilities (dodge/parry/riposte) wired into the round with a stat-scaled trigger chance. 11-b ships only a defense-mitigated landed strike (no to-hit roll) and carries the `Triggered` activation mode as data-not-wired. Lands as one or two combat-depth use-cases after the ability cluster.

### 🔵 Configurable / richer resource regeneration (deferred from slice 11-c)

Slice 11-c ([`../use-cases/resource-regeneration.md`](../use-cases/resource-regeneration.md)) ships flat, **hardcoded** out-of-combat regeneration (idle 1/pool/3-ticks; resting ~3×) so ability resource costs are recoverable. Surfacing the rates as configuration — and the richer model (per-area/terrain rates, stat-derived regen, food/effect interaction, a "fully rested" notification) — is a dedicated regeneration use-case that depends on a more robust configuration model (a separate backlog concern). Until then the constants live isolated in `RegenerationSystem` for a cheap later promotion.

### 🔵 Locale enhancements

Deferred from slice 5a (bare-bones content spawning). Three related capabilities held together because they share a data-model decision:

- **Room-to-area membership** — a `RoomComponent.AreaId` field or a dedicated component linking each room to an `AreaComponent` entity. `RoomCreatedByAdminEvent` and `mkroom` logic would eventually set area membership at creation time.
- **Coordinate system** — a `CoordinateComponent` (`int X, int Y, int Z`) on room entities, enabling map generation and cardinal distance queries.
- **Area-level properties** — PvP flag, respawn rate, ambient lighting — currently on `AreaComponent` but not yet instantiated or enforced by any slice.

These are deferred together because adding coordinates without area membership is premature, and area properties without coordinates have limited value. Revisit when the mob-wandering slice (slice 8) or a mapping command surfaces a concrete need.

### 🔵 Equipment slot expansion

Additional worn slots deferred from slice 7 (`equipment.md`): `Legs`, `Hands`, `Neck`, `Ring` (and potentially `Waist`, `Wrist`, `Shoulders`). Adding them is a pure `WornSlot` enum + YAML extension. Revisit when the combat slice (9) needs resistance slots, or when content authoring requires them.

### 🔵 Subtype-based argument matching ("get sword" = any sword)

Deferred from slice 6 (`items-and-inventory.md`). `ItemType` enum lands as data on `ItemDataComponent` in slice 6, but no special matching behavior uses it. A future `ItemTypeArgumentResolver` could resolve `"sword"` → all entities of `ItemType.Weapon` with keyword "sword". Requires clarifying whether sub-type matching is a command-level concern (different resolvers per command) or a global upgrade to `IArgumentResolver`. Revisit when content needs it or when the keyword-matching miss rate in play-testing becomes notable.

### 🔵 Multi-step command prompts and player config

Deferred from slice 7 design notes. Two related capabilities:
- **State-machine prompts**: a command can have confirmation steps (e.g. "You are already wearing X. Replace it? [yes/no]"). Requires per-session prompt state beyond what the current I/O loop supports.
- **Player config**: per-character preferences (e.g. `autoswap yes`, `autoconfirm itemswap`). Requires a `PlayerConfigurationComponent` (planned in `components-planned.md`) and a `config`/`set` player command.

Both are meaningful improvements to UX but would bloat slices 6–7. Revisit when the number of "are you sure?" flows justifies the infrastructure cost.

### 🟢 `IOptions<T>` sweep — typed config options across Core

Surfaced during slice 10 architecture review. Every configuration block with multiple consumers in `Core/` should be bound via a typed options class + `services.Configure<T>(configuration.GetSection("X"))` rather than via raw `IConfiguration["X:Key"]` reads scattered across constructors. Raw reads have no IDE navigation, no compile-time safety, and duplicate default values across files.

**Known sites** (as of slice 10):

| Config section | Files using raw reads | Typed class exists? |
|---|---|---|
| `Death:` | `DeathSystem`, `DeathTickHandler`, `AttributeSystem` | ✅ `DeathOptions` — **wired in slice 10**; `AttributeSystem` cross-module dependency flagged below |
| `World:` | `WorldContentLoader` (×2), `RoomContentWriter`, `ItemContentWriter`, `MobContentWriter` | ❌ |
| `Persistence:` | `PersistenceSystem` | ❌ |
| `CharacterDefaults:` | `AccountSystem` (slice 9-d) | ❌ |

**Specific follow-up for `AttributeSystem`.** It currently injects `IOptions<DeathOptions>` solely to read the HP-floor clamp — a cross-module dependency (`Attributes` → `Death`). The right long-term shape is either: (a) move `HpFloor` to an `AttributeOptions` class so `AttributeSystem` owns its own floor config, or (b) remove the floor clamp from `AttributeSystem` entirely and let callers (`DeathTickHandler`) enforce the floor before calling `SetCurrentHp`. Option (b) is the cleaner layer: `AttributeSystem` becomes a pure setter with `[0, Max]` clamping, and the death floor is a Death-module concern only.

**Work.** For each section without a typed class: create the options class, wire `services.Configure<T>()` in the owning module's `AddXModule()` method (or in `Server/Program.cs` following the `OutputConfiguration` + `DeathOptions` pattern), and replace raw string reads with `IOptions<T>` injection. Then resolve the `AttributeSystem` cross-module dependency.

### 🟢 Persistence save-on-change cleanup + manual `save`/`quit` commands

Follow-up from the death-and-respawn slice (slice 10), where INV-22 was reworded to name **three** permitted `SaveEntityAsync` boundary categories — construction, admin boundary, session-end. Two cleanup items and two new commands remain:

**Cleanup — migrate stray runtime saves to the flush.**
- `WearCommand` and `RemoveCommand` (`Core/Modules/Items/Commands/`, lines ~90/~80) call `SaveEntityAsync` after an equip/unequip. Equipment changes are ordinary runtime inventory mutations and do **not** warrant an immediate save — drop these calls and let the periodic flush cover them. ([`../use-cases/equipment.md`](../use-cases/equipment.md) steps 4–5 also spec the save and must be updated to match.)
- Audit `CharacterHydrationHandler` (`Core/Modules/Account/Handlers/`, ~line 70), which calls `SaveEntityAsync` in its startup error-recovery path (unresolvable `RoomBlueprintId` → reset to starting room → persist the correction). Decide whether this is a legitimate startup/hydration boundary (if so, name it as a fourth INV-22 category) or should be restructured. It is currently the one `SaveEntityAsync` site that does not fit the three named categories.

**New `save` command (admin).** An admin-gated command that forces an immediate persistence write, with arguments selecting scope: a specific player and/or the world. Player save → `SaveEntityAsync(playerEntityId)` (admin boundary save, paired with an audit event). "World" save → a full flush (`FlushAllAsync`) and/or YAML write of authored content (exact scope to be designed). Admin-gated; audited.

**New `quit` command (player).** A player command that force-saves the player (session-end boundary save) and then disconnects gracefully. Today a raw disconnect is already force-saved by `PlayerSessionHandler`; `quit` makes the player-initiated graceful exit explicit. **Cross-ref:** when this lands it should be flagged `UsableWhileIncapacitated = true` so an incapacitated/dying player can still quit — the death-and-respawn slice ([`../use-cases/death-and-respawn.md`](../use-cases/death-and-respawn.md)) deliberately omitted `quit` from its allowlist because no `quit` command existed yet.

### 🔵 Mob death / respawn and `BlueprintComponent` clearing (INV-21)

When mob combat death lands (slice 9+), the death/respawn slice must decide:

- **Reset in place:** HP restored on the same entity; `BlueprintComponent` is never cleared. Simple, but the entity ID is reused, which means save-file references to the dead entity slot survive.
- **Destroy and re-seed:** The dead entity is destroyed (or archived); a new entity is spawned from the `MobTemplate`; `BlueprintComponent` is cleared from the corpse before the corpse entity is left or destroyed. Required by INV-21 ("when a player interaction makes an instance independent, clear `BlueprintComponent`").

The chosen approach must be called out explicitly in the death/respawn use-case doc's Design Notes. If destroy-and-re-seed is chosen, `BlueprintComponent` must be cleared on the corpse entity before the new entity is spawned so the blueprint slot is free for the next spawn cycle (INV-21).

### 🟢 EffectsComponent persistence — RESOLVED (single list, lifetime-filtered) → slice 9-e

**Decision (2026-05-30).** One `EffectsComponent` with a **single `List<Effect>`** of standalone effects — no `Persistent`/`Transient` component split, and no two-list split. Persistence is **lifetime-filtered**: the component is `[Persistent]`, and a `[JsonConverter]` on it writes only entries whose `Lifetime == UntilRemoved`. `System.Text.Json` (already used by `ComponentSerializer`) honors the attribute natively — **no new persistence infrastructure**. Source-bound effects (`WhileEquipped`/`WhileKnown`/`WhilePresent`) are not stored at all — derived on read from their persisted source. Rationale: two near-identical components were duplication; `Lifetime` is already the single source of truth for what survives a save. Design in [`../design/gameplay-model.md`](../design/gameplay-model.md) Spine C; built in slice 9-e ([`../use-cases/effect-substrate.md`](../use-cases/effect-substrate.md)).

**Reference-sweep when 9-e lands.** These docs use the old two-component names as examples/stubs and must reconcile to the single `EffectsComponent` (already updated with the decision: the model, `02-ecs.md`, `components-planned.md`, and `.claude/skills/add-archetype/SKILL.md`): `architecture/06-persistence.md` (excluded-component example), `architecture/flows/flow-04-persistence-flush-cycle.md`, `reference/archetypes.md` (Weapon/Armor optional component), `use-cases/persistence-substrate.md` (example).

### 🔵 Balance & tuning surface + reference doc

As the gameplay-model spines land (effect Power-scaling, ability costs, rarity/scaling budgets, progression XP curves, character defaults), each introduces tunable numbers — [`../architecture/05-configuration.md`](../architecture/05-configuration.md) **Category 3 (System Math / Balance)**. Today these live as co-located `*Constants` per the config strategy (and `CharacterDefaults`, slice 9-d, is the first set surfaced as settings under the OD-2 promotion trigger). Worth describing as a standalone concern because the knobs accumulate across systems:

- **Reference doc** — a balance catalog (likely `reference/balance.md` or under `design/`) listing every tunable knob, its owning system, current value, and design rationale, so tuning is coherent rather than archaeology across a dozen constant classes.
- **Promotion tracking (OD-2)** — when designer iteration without recompile is needed for a subset, promote those constants to an authored content definition (Category 2), editable by the future content editor.

Not a runtime "module" — balance math stays co-located with its owning system (Category 3); this item is the *documentation + promotion discipline* around it. Becomes worthwhile once 2–3 spines (effects, abilities, scaling) have introduced enough knobs to justify the catalog — likely around slices 11–13.

### 🔵 Full-featured content editor (transition from command-driven authoring)

Content authoring today is command-driven (`mkmob`/`setmob`/`mkitem`/`setitem`/`dig`/`set`, …); a full-featured editor is a known future (Ticket B resolution in [`plan.md`](plan.md): in-game commands first, web/desktop editor deferred alongside the SignalR/dual-client transport). To keep that transition cheap, the established convention — reinforced by slice 9-d — is that **all authoring logic lives in builder/writer *systems*** (`IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem`, `*ContentWriter`), with the command as a thin caller. The editor becomes a second thin caller of the same systems; no authoring logic is trapped in command classes. New content-mutating features must add their logic to a system, not a command body. Revisit building the editor itself once the dual-client transport lands (it shares that deferral).

### 🔵 Archetype catalogue refresh

The archetype list in [`../reference/archetypes.md`](../reference/archetypes.md) was written against the old component shapes. Re-audit once a few Phase 3 slices have landed real components.

### 🔵 Use-case → subsystem-doc conversion audit (docs lifecycle, 2026-05)

The docs lifecycle changed: a slice now graduates a system's *design* into [`../architecture/subsystems/`](../architecture/subsystems/) (or a higher-level [`../architecture/`](../architecture/) doc for a complex system such as effects), leaving the use-case as a requirements + implementation-plan artifact. See [`../documentation-architecture.md`](../documentation-architecture.md) ("Use-case lifecycle" → "Design graduates to its durable home"). Existing implemented use-cases predate this split and still carry their design inline. Audit them and convert the durable design into subsystem docs — prioritizing the systems most likely to be extended (stats, combat, items). Two enforcement surfaces still need a matching update to make the new lifecycle binding: the `sync-roadmap` skill's step list, and a checklist clause (extend `INV-D2`). Forward slices follow the new split natively; this is the retroactive cleanup.

### 🔵 Use-case catalogue audit

The 17 scenarios from before the strip were retired. Use cases are now authored one at a time as each slice begins. Periodically audit the catalogue for gaps or scenarios that have become obsolete.

### 🔵 On-demand "architectural-debt sweep" agent

A `.claude/agents/debt-sweep.md` (or similar name) that walks the codebase looking for repeated hand-rolled patterns that should be promoted to a framework. Heavy-context agent; **on-demand only** — not part of the per-slice loop. Periodic sanity check, not an integral development gate.

Detection heuristics it should run:

- ≥3 files with the same shape of inline argument parsing (`Trim()`/`Split()`/manual `Enum.TryParse`) → command-framework regression candidate.
- ≥3 files with the same shape of session output formatting (`session.SendLineAsync($"{prefix} {body}")`) → output-framework promotion candidate.
- ≥3 files iterating `[Persistent]`-tagged components with identical loops → core-helper candidate.
- New player-facing surface (verb, prompt, output type) introduced without an `ICommand` / `ICommandDispatcher` / `IOutputMessage` / equivalent registration → infrastructure-discipline-parity violation.
- A `.claude/skills/*.md` or `.claude/agents/*.md` that references a rule, path, or pattern no longer matching [`../architecture/checklist.md`](../architecture/checklist.md) or the code → stale-tooling candidate (`INV-20`). The spec for this lives in [`../documentation-architecture.md`](../documentation-architecture.md).

Output: a punch list of promotion candidates with evidence (file:line for each instance) and a recommended slice to absorb the work. Does **not** modify code or docs — surface only.

The slice-by-slice ground rule 9 check (use-case-planner + architecture-reviewer) is the integral development-cycle defence; this agent is the periodic backstop for whatever slips through. Build when there's been enough drift to make it useful — likely after several Phase 3 slices have shipped.

## Done — moved out of this file

The following items were on the backlog and have shipped or been superseded. Kept here as a brief note so old links resolve cleanly:

- **Persistence substrate** — shipped as Phase 3 slice 1. See [`completed/slice-1-persistence-substrate.md`](completed/slice-1-persistence-substrate.md).
- **`System.Text.Json` adoption** — shipped with the persistence substrate; `ComponentSerializer` already uses `System.Text.Json` end-to-end. No external JSON library is in use.
- **Post-Phase-1 docs drift sweep** — completed alongside Phase 1.5 (Ticket A) and folded into [`completed/phase-1-strip.md`](completed/phase-1-strip.md).
- **`.claude/README.md` index** — now exists at [`../../.claude/README.md`](../../.claude/README.md).
- **Admin UI scope (Ticket B)** — resolved. In-game admin commands (telnet) ship as part of slice 2; a web/desktop editor remains optional and is folded into the deferred SignalR/dual-client slice. See [`plan.md`](plan.md) "Resolved tickets".
