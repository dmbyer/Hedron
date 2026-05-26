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

## Phase 3+ ideas (not yet a slice)

### 🔵 Heartbeat / TimeSystem

Needed for combat pulses, mob AI ticks, effect expiries. Lands as part of the first Phase 3 slice that needs scheduled work — currently expected to be the mob-wandering slice (slice 8 in [`plan.md`](plan.md)).

### 🔵 Web / SignalR dual client

If a web client becomes a goal, unify telnet sessions and web sessions behind the existing `ISession` abstraction so handlers don't care about transport. Listed as the deferred slice in [`plan.md`](plan.md). The admin-tooling resolution (in-game commands, not a web UI) makes this strictly optional rather than blocking.

### 🔵 Broadcast channel mode (global / newbie chat)

Acknowledged debt from Phase 3 slice 4 ([`../use-cases/output-framework.md`](../use-cases/output-framework.md)). Slice 4's broadcast expansion ships room-scope-with-audience-filter and system-wide `SendToAllAsync`, but **channel mode** (global/newbie chat membership) is deferred: it requires per-entity channel-membership state that no slice has introduced yet. Lands with whichever later slice introduces channel membership (likely alongside or after account / character creation, slice 5). The `IBroadcastSystem` interface shaped in slice 4 should accommodate a `SendToChannelAsync` addition without breaking the room/system modes.

### 🔵 Command-arg log redaction (acknowledged debt from slice 3)

`CommandExecutedEvent.ArgsSummary` ([`../use-cases/command-framework.md`](../use-cases/command-framework.md)) logs parsed args in plaintext. Slice 3 ships with no redaction — acceptable only because the sole free-text verb is `say` and the logger is local. **Prerequisite for any retained/forwarded log sink.** Proposed fix: a per-command `[NoLogArgs]` / `RedactArgs` declaration the dispatcher honors before building `ArgsSummary`. Lands with whichever slice first adds a non-local logging sink, an auth-bearing verb (`password`, account linking), or `tell`/private channels — whichever comes first.

### 🔵 CommandPipeline middleware refactor (deferred smell from slice 3)

`CommandDispatcher` carries five injected dependencies and owns authorization, parsing, output, event publication, and exception trapping (spec-mode review smell S1). A middleware/pipeline chain would isolate these concerns. Deferred from slice 3 to avoid ballooning the 12-command refactor. Revisit when a sixth concern would be added to the dispatcher, or if testing the dispatcher becomes painful.

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

### 🔵 Mob death / respawn and `BlueprintComponent` clearing (INV-21)

When mob combat death lands (slice 9+), the death/respawn slice must decide:

- **Reset in place:** HP restored on the same entity; `BlueprintComponent` is never cleared. Simple, but the entity ID is reused, which means save-file references to the dead entity slot survive.
- **Destroy and re-seed:** The dead entity is destroyed (or archived); a new entity is spawned from the `MobTemplate`; `BlueprintComponent` is cleared from the corpse before the corpse entity is left or destroyed. Required by INV-21 ("when a player interaction makes an instance independent, clear `BlueprintComponent`").

The chosen approach must be called out explicitly in the death/respawn use-case doc's Design Notes. If destroy-and-re-seed is chosen, `BlueprintComponent` must be cleared on the corpse entity before the new entity is spawned so the blueprint slot is free for the next spawn cycle (INV-21).

### 🔵 Archetype catalogue refresh

The archetype list in [`../reference/archetypes.md`](../reference/archetypes.md) was written against the old component shapes. Re-audit once a few Phase 3 slices have landed real components.

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
