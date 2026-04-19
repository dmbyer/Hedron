# Backlog

> Living list of follow-up work that doesn't belong in the phase plan. For the sequenced rebuild, see [`plan.md`](plan.md). For the frozen MVP target, see [`mvp.md`](mvp.md).

Status markers: 🟢 ready · 🟡 blocked · 🔵 deferred (Phase 3+)

## Phase-gated items

Most large items live in [`plan.md`](plan.md). This file holds smaller, crosscutting, or yet-to-be-scoped items.

### 🟡 Test framework (xUnit)
Phase 4. Low value until Phase 3 slices produce real systems and handlers to test. Adding earlier risks locking in shapes we haven't lived with yet.

### 🟡 CI wiring
Phase 4. Build-and-test on PR once the test framework lands.

### 🟡 Performance: LINQ in hot paths
Becomes meaningful once `LocationSystem` and `CombatSystem` exist and profiling shows real cost. Not an MVP concern — MVP's hot path is three rooms.

### 🟡 Persistence substrate
First slice of Phase 3. Event-driven dirty tracking, atomic writes. Design sketched in [`../use-cases/game-state-persistence.md`](../use-cases/game-state-persistence.md).

### 🔵 Heartbeat / TimeSystem
Needed for combat pulses, mob AI ticks, effect expiries. Not an MVP concern — MVP is event-driven-on-input only. Lands as part of the first Phase 3 slice that needs scheduled work (probably combat or mob wandering).

## Infrastructure

### 🔵 `System.Text.Json` everywhere
Once persistence exists, use `System.Text.Json` rather than `Newtonsoft.Json`. .NET 8 gives us the richer source-generator APIs.

### 🔵 SignalR / dual-client
If a web client becomes a goal, unify telnet sessions and web sessions behind a common `ISession` abstraction so handlers don't care about transport. Not scoped yet.

### 🔵 Thread safety review
Evaluate after `TimeSystem` exists and concurrency shape is known. May not be needed if the heartbeat stays single-threaded with an event queue.

### 🔵 Admin UI
Either rebuild Blazor Server atop the new architecture, or pick something else. Deferred to Phase 4 or a dedicated Phase 3 slice once we know what authoring operations we actually need.

## Docs

### 🟢 Post-Phase-1 docs drift sweep
Strip Phase 1 will invalidate any reference doc that described legacy shapes. Immediately after the strip commit, reread `docs/architecture/`, `docs/reference/`, `docs/use-cases/` for stale references and fix.

### 🟢 `.claude/README.md` index
Called out in `CLAUDE.md` as "coming in a follow-up." Index the agents, skills, and slash commands actually present so Claude Code can discover them.

## Ideas not yet scoped

### 🔵 Archetype catalogue refresh
The archetype list in [`../reference/archetypes.md`](../reference/archetypes.md) was written against the old component shapes. Needs a rewrite once Phase 2 pins down the new shapes.

### 🔵 Use-case catalogue audit
[`../use-cases/`](../use-cases/) has 17 scenarios authored against the idealized API. Some will be implemented as Phase 3 slices in the order listed in [`plan.md`](plan.md); others may be consolidated or dropped. Re-audit once Phase 2 lands.
