# Player Prompt & Output Batching

**Status:** planned

> **Seed from the `architecture-advisor` skill.** This doc carries the architecture-tier framing (Description, Design notes, `## Architecture brief`, Open questions). The `use-case-planner` extends it into the full template (Preconditions/Postconditions, Main flow, Events fired, Systems/handlers, work packages, cross-cutting audit, flows). The `## Architecture brief` block is in-flight and is trimmed on ship.

## Actors

- **Player** — sees a status prompt after their command output and after each combat round; no longer flooded with a prompt per line.
- **System** — the heartbeat flushes batched tick output and emits one trailing prompt.
- **Mob** — a source of async combat output that must batch into the round's single flush.

## Module

Primarily the **Output framework** (`Core/Output/`): the per-session buffer, the flush mechanism, and the new `PromptMessage` shape are core-tier transport plumbing with no domain knowledge. The **prompt composer** reads entity state (`IEntityStateService`) and pools (`IStatSystem`), so it is **domain-aware and must not live in `Core/Output/`** ([INV-2](../architecture/checklist.md)); it lives one tier up (a small `Prompt`/`Player` module or the handler tier) and is wired into the core buffer through a **core-owned port** (`IPromptSource`). See [`subsystems/output.md`](../architecture/subsystems/output.md).

## Description

Give the player a status prompt that trails their output: by default it shows their entity **state** in parentheses only when abnormal — `(Resting)`, `(Incapacitated)` — followed by **current/max for each resource pool** the entity has (HP, Mana, Stamina, Astra). To stop the player being flooded with a prompt after every line, output is **batched per session**: many sources (command responses, room broadcasts, combat rounds, effect ticks) write into one session-scoped buffer that **flushes on defined boundaries** — emitting the buffered lines plus **one** freshly-composed prompt. A command's response flushes immediately (you type `help`, you see help, then your prompt); a combat round's several messages (your strike, the mob's counter, ability results) accumulate over the heartbeat tick and flush together with a single trailing prompt; conversational messages (`say`/`tell`) flush immediately so chat stays snappy.

## Design notes

*(Durable rationale — kept on ship per [INV-D2](../architecture/checklist.md).)*

- **Two layers, decoupled by a port — the central decision.** The *flush mechanism* (a per-session buffer that coalesces and emits typed messages) is pure transport plumbing → it stays **core-tier** in `Core/Output/`, carrying no domain dependency. The *prompt content* is a projection of entity **state + pools** → it is a **domain read** and cannot sit in core ([INV-2](../architecture/checklist.md)). They are joined by a **core-owned `IPromptSource` port** ([INV-24](../architecture/checklist.md)): the core buffer calls the port at flush; a domain-aware composer implements it. This is what lets the buffer self-flush an immediate-category message (a `say`) and still append a prompt without the core knowing anything about state or pools.
- **The prompt is computed on read, never cached.** The composer builds a fresh `PromptMessage` at each flush from current state + pools. This is why no "prompt dirty" flag and no `PromptChangedEvent` are needed: a prompt composed *after* the tick's mutations automatically reflects the post-round HP. Caching the prompt would re-introduce the "did I recompute when HP changed?" bug family ([INV-24](../architecture/checklist.md), compute-on-read).
- **The buffer is session-scoped, not request-scoped.** Today `IOutputWriterFactory.Create(session)` yields a stateless, per-request writer. Cross-source batching requires a **stateful buffer whose lifetime is the session**, because command output, async room broadcasts, and heartbeat combat output for one player must all accumulate in the *same* buffer. This is the structural prerequisite for batching.
- **Flush policy is keyed off `OutputCategory`.** The existing `OutputCategory` on every `IOutputMessage` is the natural classifier: conversational categories (`Chat`) flush immediately; tick categories (combat/effects) batch to end-of-tick. No parallel taxonomy — the category the message already carries decides its flush behavior.
- **Pools are read generically by `ScoreId`.** The composer reads each pool via `IStatSystem.Get(entityId, ScoreId)` (the generalized seam that already exists) rather than hardcoding HP, so the prompt naturally covers HP/Mana/Stamina/Astra and any future pool with no composer change.
- **`PromptMessage` is a typed shape, not a pre-stringified line.** It carries the state label + pool tuples; the telnet formatter renders it as text now, and a future SignalR/web formatter renders the same message as structured gauges — the output framework's transport-key design ([INV-11](../architecture/checklist.md), [`subsystems/output.md`](../architecture/subsystems/output.md) "Seams for future transports").
- **The buffer is thread-safe; flush is atomic.** Three threads can touch one session's buffer concurrently — this player's read loop (their command), other players' read loops (their `say` broadcasting here), and the heartbeat thread (combat/tick). The buffer guards its pending list and performs drain-then-append-prompt atomically. This is a concrete new site for the Phase-4 thread-safety review (backlog).

## Architecture brief

*(In-flight; trimmed on ship.)*

### Seams & recommended homes

| Seam | What it owns | Layer / home | Disposition |
|---|---|---|---|
| **Per-session output buffer** | accumulates `IOutputMessage`s; `Flush()` drains + appends one prompt; category-keyed flush policy; thread-safe | **core** — `Core/Output/` (decorator over / replacement for the immediate `OutputWriter`), session-lifetime | **Build now** |
| **`IPromptSource` port** | core-owned interface the buffer calls at flush to obtain the `PromptMessage` | **core** interface (`Core/Output/`), domain implementation | **Build now** |
| **Prompt composer** | reads `IEntityStateService` + `IStatSystem.Get(ScoreId)`; builds `PromptMessage` fresh (compute-on-read) | **domain-aware** (small `Prompt`/`Player` module or handler tier) — *not* core | **Build now** |
| **`PromptMessage`** | typed shape: state label (omitted when `None`) + per-pool cur/max tuples | **core** message shape (`Core/Output/`), rendered per-transport | **Build now** |
| **Tick flush trigger** | end-of-tick: flush sessions with pending batched output | **handler** on `HeartbeatTickEvent`, **latest priority** | **Build now** |
| **Command flush trigger** | end-of-dispatch: flush after `ExecuteAsync` + its event handlers complete | **initiator** — `CommandDispatcher` | **Build now** |
| **`OutputCategory` + flush-policy map** | classify each category Immediate vs Batched; likely add a `Combat`/tick category | **core** enum + small policy | **Build now** (INV-16 catalog) |

### Family / forward map (siblings that pull on this seam)

Batching is **not** combat-specific — past the ≥3× [INV-19](../architecture/checklist.md) bar: effect ticks ("burned, poisoned"), **AoE** ("scorches 4 foes" — [feature-horizon](../design/feature-horizon.md) §5 explicitly says *summarize, don't spam*), future mob-AI pushes, weather/world-events. Downstream consumers of the prompt/output seam: **combat verbosity / message tuning** (backlog), **channels** + **tell/whisper/yell** ([feature-horizon](../design/feature-horizon.md) §10, Chat-category flush), **player config** (backlog, prompt format), **web/SignalR client** (backlog, typed `PromptMessage`). Building the general buffer now is *less* total code than re-solving coalescing per feature.

### Observers, contributors & event granularity

- **No new event.** Unlike a state-mutation seam, the prompt **observes by reading at flush**, not by subscribing — so this slice adds *no* `PromptChangedEvent`/`PoolDisplayedEvent`. Restraint: the event surface stays empty; correctness comes from compute-on-read.
- **Contributor growth (shape-for-later).** Prompt content is a single composer today. If ≥3 sources later want to inject prompt segments (a target's HP, a status-effect row, an XP bar), `IPromptSource` generalizes into an `IPromptContributor` aggregation (the [INV-24](../architecture/checklist.md) shape) with no caller change. Note it; don't build it.

### Ordering & timing ([INV-7](../architecture/checklist.md))

- The tick flush handler is the **last** handler on `HeartbeatTickEvent` — after combat (p=20), effects (p=20), regen (p=20), and any future output-producing tick handler — so the prompt reflects the *completed* round. (Exact priority number is planner-tier; it must sort after every output producer — see [`reference/handlers.md`](../reference/handlers.md) tiers.)
- Within a flush: **drain buffered lines, then append the prompt**, so the prompt shows post-tick pools.

### Invariants in tension

- **[INV-2](../architecture/checklist.md)** — core buffer must not read domain state → resolved by the **[INV-24](../architecture/checklist.md)** `IPromptSource` port (the load-bearing pairing).
- **[INV-19](../architecture/checklist.md)** — new player-facing surface (prompt) + ≥3× coalescing → framework lands this slice.
- **[INV-11](../architecture/checklist.md)** — prompt and batched combat output are typed messages, never hand-rolled strings.
- **[INV-7](../architecture/checklist.md)** — flush is the latest tick step; drain-then-prompt.
- **[INV-16](../architecture/checklist.md)/[INV-17](../architecture/checklist.md)** — `OutputCategory` + new shapes/handlers update `reference/`; Flows 3, 6, 16, 18 change.

### Resolved decisions (do not relitigate)

1. **Batch breadth = general per-session buffer** (not combat-only aggregation).
2. **Flush latency = category-aware** — `Chat` (and command responses) flush immediately; combat/effect/tick categories batch to end-of-tick. Accepted consequence: a chat flush mid-round emits its own prompt.
3. **Prompt config = fixed format now, per-player config deferred** to the backlog "Multi-step command prompts and player config" item.

## Open questions

*(For the planner / spec gate.)*

- **Does an *immediate* (Chat) flush append a prompt, or only command-end and tick-end flushes?** Trades chat snappiness against prompt-spam when several `say`s arrive in a row. (Recommendation: every flush appends a prompt; revisit if spam shows in play-test.)
- **Which `EntityStateFlags` get a prompt label?** `Resting`/`Incapacitated` are named; should `InCombat` show (e.g. `(Fighting)`) or stay unlabeled? (`None` shows nothing.)
- **Exact `OutputCategory` additions and the Immediate/Batched map** — is a single new `Combat` (or `Tick`) category enough, or do effects/combat want distinct categories?
- **Initial prompt** on connect/login and after `look`/movement — confirm the prompt trails these command flushes (expected: yes, it falls out of command-end flush).
- **Flush-handler priority number** and whether a new "flush" priority tier belongs in `reference/handlers.md`.
- **Work-package split** (planner-owned) — natural seams: (a) session-scoped buffer + category flush policy; (b) `PromptMessage` + `IPromptSource` + composer; (c) tick + command flush triggers + flow updates.

## Related

- [`subsystems/output.md`](../architecture/subsystems/output.md) · [`subsystems/commands.md`](../architecture/subsystems/commands.md)
- Flows [3](../architecture/flows/flow-03-player-command-lifecycle.md), [6](../architecture/flows/flow-06-output-rendering.md), [16](../architecture/flows/flow-16-heartbeat-tick.md), [18](../architecture/flows/flow-18-combat-round-pulse.md)
- [`entity-state-management.md`](entity-state-management.md) · [`stat-resource-substrate.md`](stat-resource-substrate.md)
- [`feature-horizon.md`](../design/feature-horizon.md) §5 (combat verbosity), §10 (social/channels)
