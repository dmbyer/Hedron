# Architecture Invariant Checklist

> **This is the single authoritative list of every architectural invariant in Hedron.** It is the *enforcement list* — terse, numbered, checkable. The *explanations* live in [00-overview.md](00-overview.md) through [06-flows.md](06-flows.md); the *workflow obligations* live in [`CLAUDE.md`](../../CLAUDE.md) ground rules and [`../roadmap/plan.md`](../roadmap/plan.md). Those documents must not restate invariants in their own words — they link here.
>
> **Consumers of this list:** the `architecture-reviewer` agent (both spec-review and code-review modes), the `use-case-planner` agent's ground-rule-9 audit, and the future on-demand debt-sweep agent. A rule change lands *here once*; every consumer picks it up. No agent prompt carries a private copy.
>
> Each invariant has a stable ID (`INV-n`). Cite the ID in reviews (e.g. "INV-7 violated at `Foo.cs:42`"). "How to check" distinguishes **spec mode** (reviewing a use-case doc before implementation) from **code mode** (reviewing a diff) only where the technique differs.

---

## A. Layering & dependency direction

**INV-1 — Downward-only dependencies through the processing stack.** Handlers → Domain Systems → Core Systems → Components. Higher may call lower; never the reverse. Explanation: [01-layers.md](01-layers.md#cross-layer-dependency-rules).
- *Spec:* does any Main-Flow step describe a system calling a handler, or a core system calling a domain system?
- *Code:* import/call graph in changed `Core/` files.

**INV-2 — Core Systems never depend on Domain Systems.** A `Core/Systems/` type may not reference any `Core/Modules/<Feature>/` type. Explanation: [01-layers.md](01-layers.md), [00-overview.md](00-overview.md).

**INV-3 — Components are pure data.** No methods that do work, no event subscriptions, no references to other components or systems. Explanation: [02-ecs.md](02-ecs.md).

**INV-4 — Component queries, not type checks.** Never `entity is Player` / `as Mob`. Use `entityService.HasComponent<T>(id)` / `TryGet<T>`. Explanation: [02-ecs.md](02-ecs.md).

## B. Events & the orchestration boundary

**INV-5 — Only Initiators and Handlers publish events.** Domain & Core systems compute and return; they never touch `IEventBus`. This is the exact scope of "services return results; handlers publish events" — it constrains *systems*, not the orchestration boundary. An Initiator (command, scheduled tick) or a Handler publishing is correct. Explanation: [03-events.md](03-events.md#services-return-results-handlers-publish-events), [01-layers.md](01-layers.md#initiators--entry-points).
- *Spec:* does any step direct a domain/core system to publish? Does it direct a non-system (command/dispatcher/handler) to publish — that's allowed, not a violation.
- *Code:* `IEventBus`/`PublishAsync` injected or called inside a file under `Systems/`.

**INV-6 — Events are past-tense, thin facts.** Named for what happened (`PlayerMovedEvent`, not `MovePlayerEvent`). Payload is minimal unless point-in-time capture is required for correctness. Explanation: [03-events.md](03-events.md).

**INV-7 — Intra-event ordering via priority or multi-phase events, never a god-handler.** Multiple handlers on one event use explicit `Priority`; sequenced phases use ordered past-tense events. No single handler that calls every system in sequence. Explanation: [03-events.md](03-events.md#handler-priorities--ordering), [04-pitfalls.md](04-pitfalls.md).

## C. Initiators (commands & heartbeat)

**INV-8 — Initiators are thin and rule-free.** Parse/gather → resolve target via a domain-system lookup → call the domain system → publish the resulting event(s). A command body growing past ~30 lines is a smell worth inspecting — not a hard failure. Ask: is the length coming from game-rule logic (→ belongs in a system), or from mechanical infrastructure like null-guards and arg resolution (→ fine to stay)? If the former, extract; if the latter, the command is still thin. No game-rule logic; no conditional branching on game state to decide which events to fire (that's a handler). Publishing multiple events is acceptable when every event is an unconditional, direct consequence of the command's action — the test is: "would a handler here contain any game logic, or just mechanically re-publish?" If the latter, keep it in the command. Explanation: [01-layers.md](01-layers.md#initiators--entry-points).

**INV-9 — Initiators never call Handlers directly.** An initiator publishes an event; the bus routes it. Explanation: [01-layers.md](01-layers.md).

**INV-10 — The no-chain variant is the only exception to "publish your outcome."** An initiator whose work is a closed mechanical sweep with no game-rule fan-out (e.g. `PersistenceFlushTimer` → `FlushAsync`) may call a system directly and publish nothing. The moment another concern must react, it becomes an event. Explanation: [01-layers.md](01-layers.md#initiators--entry-points).

**INV-11 — No direct `session.SendLineAsync` from command bodies once the command framework lands (slice 3+).** Output goes through the output writer / typed messages. Until slice 3 merges this is aspirational; after, it is enforced — including dispatcher-internal call sites (e.g. the unknown-command branch). Explanation: [command-framework.md](../use-cases/command-framework.md).

## D. One world, templates, identity, persistence

**INV-12 — One live world.** Every live entity is in `EntityService`. Authored content spawns via `TemplateRegistry`; bespoke entities are built by the owning feature. Explanation: [02-ecs.md](02-ecs.md), [00-overview.md](00-overview.md).

**INV-13 — Entity identity is `uint`, wrapped as `Entity(uint Id)` at call sites.** Components store `uint` ids when referencing other entities. Explanation: [02-ecs.md](02-ecs.md).

**INV-14 — Persistence is a two-level opt-in.** An entity persists iff it carries the `PersistentEntity` marker component. `[Persistent]` on a component *type* controls which components are included in the snapshot for entities that are already opted in — it does not cause an entity to be saved on its own. Do not use `[Persistent]` to make an entity persistent; add `PersistentEntity` to the entity. Persistence uses `System.Text.Json`; content authoring uses YAML (`YamlDotNet`). The two serializers do not share code. Explanation: [08-persistence.md](08-persistence.md), [02-ecs.md](02-ecs.md), [05-configuration.md](05-configuration.md), CLAUDE.md ground rule 7.

## E. Idealized-API & documentation discipline

**INV-15 — Idealized-API first.** New code is written against the documented target on first attempt. If the target is wrong, fix the doc *first*, in the same change. Explanation: CLAUDE.md ground rule 1, [00-overview.md](00-overview.md).

**INV-16 — Reference catalogs stay current.** A new/changed component, system, or handler updates the matching `docs/reference/*.md` in the same PR. Explanation: [../reference/](../reference/).

**INV-17 — Canonical flows stay current.** Any change to a runtime flow specified in [06-flows.md](06-flows.md) updates that file (body *and* mermaid) in the same PR. A new recurring flow is added there. Drift blocks the review. Explanation: [06-flows.md](06-flows.md), CLAUDE.md ground rule 9.

## F. Slice discipline (ground rules 8 & 9)

**INV-18 — Content-tooling discipline.** A slice adding gameplay state ships the tooling to author and inspect it (data-file shape, admin commands, `TemplateRegistry` entries). The use-case doc's **Content tooling impact** section is the check. Explanation: CLAUDE.md ground rule 8, [../roadmap/plan.md](../roadmap/plan.md).

**INV-19 — Infrastructure-discipline parity.** A slice introducing a new player-facing surface (commands, prompts, output formats, content schemas), or repeating a hand-rolled pattern ≥3 times, lands the supporting framework in the same or an adjacent slice. The use-case doc's **Cross-cutting surfaces stressed** section is the check; gap-exposed surfaces resolve before merge; "acknowledged debt" requires a backlog entry with rationale. Explanation: CLAUDE.md ground rule 9.

---

## Spec-review failure modes (code-mode reviewers: skip)

When reviewing a use-case doc *before* implementation, beyond the per-INV spec checks above, flag:

- **SR-1 — The spec directs a layer to violate an INV.** e.g. "the dispatcher computes damage," "this domain system publishes the event." The spec must be corrected before code is written.
- **SR-2 — The spec preserves a pre-existing violation without calling it out.** "Continues to work as today" is not acceptable if "today" violates an INV. The spec must either fix it or explicitly record it as acknowledged debt with a backlog pointer.
- **SR-3 — The spec contradicts an established skill or convention.** e.g. it specifies a command shape that the `add-command` skill forbids. One of them is wrong; resolve before implementation.
- **SR-4 — A referenced flow or catalog entry doesn't exist or won't be updated.** The "Flows introduced or modified" / "Cross-cutting surfaces stressed" sections name flows/surfaces; verify each is real and that the spec commits to updating it.
- **SR-5 — An open question is load-bearing.** If a deferred decision determines whether an INV is satisfiable, it is not deferrable — it blocks implementation.

**INV-20 — Agent tooling stays current with architecture.** Any slice that introduces, clarifies, or changes an architectural rule or layer pattern updates the relevant `.claude/skills/*.md` and `.claude/agents/*.md` files in the same PR. Skills and agents are developer tooling — stale guidance produces the next slice's violations.
- *Spec:* does the use-case introduce or depend on a pattern any existing skill advises? If the spec changes that pattern, commit to updating the relevant skill file.
- *Code:* do changed files establish a pattern not yet reflected in any skill? Does any change contradict guidance in an existing skill or agent?

---

## Maintenance

Add an invariant here when a new architectural rule is established (typically when a slice's architecture-review surfaces a gap, as INV-5/INV-8–11 did from the slice-3 command-tier gap). Update the explanation doc in the same change and link back here. Never let an invariant live only in an agent prompt or only in CLAUDE.md — those point here.
