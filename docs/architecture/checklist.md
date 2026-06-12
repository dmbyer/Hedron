# Architecture Invariant Checklist

> **This is the single authoritative list of every architectural invariant in Hedron.** It is the *enforcement list* — terse, numbered, checkable. The *explanations* live in [00-overview.md](00-overview.md) through [08-blazor.md](08-blazor.md), plus the [features/](../features/) per-feature/per-system docs and [flows/](flows/README.md); the *workflow obligations* live in [`CLAUDE.md`](../../CLAUDE.md) ground rules and [`../roadmap/plan.md`](../roadmap/plan.md). Those documents must not restate invariants in their own words — they link here.
>
> **Consumers of this list:** the `architecture-reviewer` agent (both spec-review and code-review modes), the `implementation-planner` agent's ground-rule-9 audit, and the future on-demand debt-sweep agent. A rule change lands *here once*; every consumer picks it up. No agent prompt carries a private copy.
>
> Each invariant has a stable ID in a **single `INV-n` sequence** (`INV-1`…`INV-30`). The `A`–`H` section letters below are organizational only — they are **not** part of any ID (there is no `INV-A1`/`INV-D2`; the documentation-discipline rules are simply `INV-27`–`INV-30` in section G). The separate `SR-n` series at the end are **spec-review heuristics**, not invariants. Cite IDs in reviews (e.g. "INV-7 violated at `Foo.cs:42`"). "How to check" distinguishes **spec mode** (reviewing an implementation plan before implementation) from **code mode** (reviewing a diff) only where the technique differs.

---

## A. Layering & dependency direction

**INV-1 — Downward-only dependencies through the processing stack.** Handlers → Domain Systems → Core Systems → Components. Higher may call lower; never the reverse. Explanation: [01-layers.md](01-layers.md#cross-layer-dependency-rules).
- *Spec:* does any Main-Flow step describe a system calling a handler, or a core system calling a domain system?
- *Code:* import/call graph in changed `Core/` files.

**INV-2 — Core-tier Systems never depend on Domain-tier Systems.** A **core-tier** system — whether it lives at `Core/Systems/` *or* inside a feature module at `Core/Modules/<Feature>/Systems/` (e.g. `EffectSystem`, a core-tier mechanic co-located with its feature for cohesion) — may not reference or call any **domain-tier** system. Tier is a *role* (generic mechanic vs. game-rule decision), not a path. When a core-tier aggregator must consume domain-supplied data, it inverts the dependency through a core-owned port (INV-24), never a direct reference. Explanation: [01-layers.md](01-layers.md#cross-layer-dependency-rules), [00-overview.md](00-overview.md).
- *Spec:* does any step direct a core-tier system to call, reference, or import a domain-tier system instead of inverting through a port?
- *Code:* a core-tier system (wherever it resides) importing a `Core/Modules/<Feature>/` domain type.

**INV-24 — Cross-cutting contributions enter a core aggregator through a core-owned port, pulled on read, never materialized.** When a core-tier system aggregates contributions from multiple domain-owned sources to compute a value (effective score = base + Σ modifiers from effects, equipment, abilities, auras, areas…), it exposes a **core-owned contributor interface** that sources implement and register (DI-collected `IEnumerable<IContributor>`); the aggregator sums what is registered and stays **closed for modification** as sources are added. The dependency arrow points domain → core interface, satisfying INV-2 without the core system referencing any domain module. Derived contributions are **pulled at read time, never materialized/cached** into a stored component — preserving compute-on-read (the rule that kills the "did I recompute when the source changed?" bug family). Precedent: `IEffectContributor` (slice 11-a) folds passive-ability `WhileKnown` modifiers into `EffectSystem.GetModifiers`/`GetActive`. Explanation: [effects.md](effects.md#the-contributor-seam), [01-layers.md](01-layers.md#the-three-composition-shapes).
- *Spec:* does a new modifier source materialize derived effects into a stored component, or make a core aggregator reference a domain module, instead of implementing/registering the contributor port?
- *Code:* a core-tier aggregator importing a domain module to read contributions; a derived (source-bound) modifier written into a stored component instead of pulled on read.

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

**INV-11 — No direct `session.SendLineAsync` from command bodies once the command framework lands (slice 3+).** Output goes through the output writer / typed messages. Until slice 3 merges this is aspirational; after, it is enforced — including dispatcher-internal call sites (e.g. the unknown-command branch). Explanation: [command-framework.md](../implementation-plans/command-framework.md).

## D. One world, templates, identity, persistence

**INV-12 — One live world.** Every live entity is in `EntityService`. Authored content spawns via `TemplateRegistry`; bespoke entities are built by the owning feature. Explanation: [02-ecs.md](02-ecs.md), [00-overview.md](00-overview.md).

**INV-13 — Entity identity is `uint`, wrapped as `Entity(uint Id)` at call sites.** Components store `uint` ids when referencing other entities. Explanation: [02-ecs.md](02-ecs.md).

**INV-14 — Persistence is a two-level opt-in.** An entity persists iff it carries the `PersistentEntity` marker component. `[Persistent]` on a component *type* controls which components are included in the snapshot for entities that are already opted in — it does not cause an entity to be saved on its own. Do not use `[Persistent]` to make an entity persistent; add `PersistentEntity` to the entity. Persistence uses SQLite (`Microsoft.Data.Sqlite`); content authoring uses YAML (`YamlDotNet`). The two are completely separate — YAML is for designer-authored templates, SQLite is for runtime entity state. Explanation: [06-persistence.md](06-persistence.md), [02-ecs.md](02-ecs.md), [05-configuration.md](05-configuration.md), CLAUDE.md ground rule 7.

**INV-22 — Persistence lifecycle belongs to `EntityService`; handlers and commands never call save or delete directly.** `EntityService.AddComponent<PersistentEntity>` registers an entity into the SQLite persistence pool. `EntityService.DestroyEntity` deletes the entity from SQLite automatically if it carried `PersistentEntity`. **Three narrow categories of caller-initiated `SaveEntityAsync` are permitted; every other state change relies on the periodic flush:**
1. **Construction-time save.** Account/character construction paths (`LoginFlow`, `AccountSystem.CreateCharacterAsync`) call `IPersistenceSystem.SaveEntityAsync` **once**, immediately after adding `PersistentEntity`, to make the entity ID durable before the operation returns.
2. **Admin boundary save.** An admin-gated command that mutates a persistent entity's state through a domain system (e.g. `setplayer`, `setrespawn`) may call `SaveEntityAsync` **once** after the mutation, so the deliberate, out-of-band administrative change lands durably without waiting for the next flush. This is the admin-mutation pattern (mutate via system → `SaveEntityAsync` → publish audit event) and applies **only** to commands behind the admin privilege gate — never to ordinary player commands.
3. **Session-end boundary save.** When a player session ends — logout, raw disconnect, or the `quit` command — the player entity is force-saved **once** so their final state is durable before they leave. This is the **one** boundary save that legitimately runs in a handler (the disconnect/session-end handler, `PlayerSessionHandler`), as well as in the `quit` command; it does not license any other handler to save.

World content commands (`dig`, `mkitem`, `mkmob`, `set`, etc.) do **not** call `SaveEntityAsync` — world entities carry no `PersistentEntity`; YAML is their sole durable form. All other state changes — HP damage, movement, inventory mutations, crop growth, stat changes — are captured by the `PersistenceFlushTimer`'s periodic full sweep; outside these three boundary saves, handlers and player commands make no persistence call for runtime state changes. Explanation: [06-persistence.md](06-persistence.md), [docs/implementation-plans/persistence-reform.md](../implementation-plans/persistence-reform.md).
- *Spec:* does any step in the main flow direct a handler or a **non-admin** command to call `SaveEntityAsync` outside the three permitted boundary saves (construction, admin boundary, session-end)? (An admin-gated command pairing a single post-mutation `SaveEntityAsync` with an audit event is the permitted admin boundary save; a player logout/disconnect/`quit` force-save is the permitted session-end save.) Does any step direct a caller to issue a delete or cleanup call?
- *Code:* `SaveEntityAsync` or `IPersistenceSystem` called in a handler or a non-admin command body outside the three permitted boundary saves (construction, admin boundary, session-end); an admin boundary save that mutates state inline (not via a domain system) or omits the audit event; any SQLite delete called outside of `EntityService.DestroyEntity`.

**INV-23 — All entities belong to exactly one of two persistence domains; never mix them.** (1) *World content* (rooms, areas, mobs, world-spawn items): never carry `PersistentEntity`; always fresh-spawned from YAML/templates on startup; no SQLite row. `RoomComponent` and `AreaComponent` must not be `[Persistent]`. (2) *Persistent entities* (players, accounts, player-owned items, player-placed content, crops, items in persistent containers): carry `PersistentEntity` with `[Persistent]` coverage for all state that must survive restart. **Cross-domain stable reference:** `LocationComponent.RoomBlueprintId` (`string?`, `[Persistent]`) is the cross-restart room reference; `LocationComponent.RoomEntityId` (`uint`, NOT `[Persistent]`) is resolved at startup from `RoomBlueprintId` by `CharacterHydrationHandler`. Every code path that moves an entity into a room must set both fields. Explanation: [06-persistence.md](06-persistence.md), [docs/implementation-plans/persistence-reform.md](../implementation-plans/persistence-reform.md).
- *Spec:* does the use case add `PersistentEntity` to any world content entity (room, area, mob, world-spawn item)? Does it tag `RoomComponent` or `AreaComponent` as `[Persistent]`? Does any placement operation omit setting `RoomBlueprintId`?
- *Code:* `new PersistentEntity()` attached to a room, area, mob, or world-spawn item; `[Persistent]` on `RoomComponent` or `AreaComponent`; a move/placement operation that sets `RoomEntityId` without also setting `RoomBlueprintId`.

## E. Idealized-API & documentation discipline

**INV-15 — Idealized-API first.** New code is written against the documented target on first attempt. If the target is wrong, fix the doc *first*, in the same change. Explanation: CLAUDE.md ground rule 1, [00-overview.md](00-overview.md).

**INV-16 — Reference catalogs stay current.** A new/changed component, system, or handler updates the matching `docs/reference/*.md` in the same PR. Explanation: [../reference/](../reference/).

**INV-17 — Canonical flows stay current.** Any change to a runtime flow specified in [flows/README.md](flows/README.md) updates that file (body *and* mermaid) in the same PR. A new recurring flow is added there. Drift blocks the review. Explanation: [flows/README.md](flows/README.md), CLAUDE.md ground rule 9.

## F. Slice discipline (ground rules 8 & 9)

**INV-18 — Content-tooling discipline.** A slice adding gameplay state ships the tooling to author and inspect it (data-file shape, admin commands, `TemplateRegistry` entries). The implementation plan's **Content tooling impact** section is the check. Explanation: CLAUDE.md ground rule 8, [../roadmap/plan.md](../roadmap/plan.md).

**INV-19 — Infrastructure-discipline parity.** A slice introducing a new player-facing surface (commands, prompts, output formats, content schemas), or repeating a hand-rolled pattern ≥3 times, lands the supporting framework in the same or an adjacent slice. The implementation plan's **Cross-cutting surfaces stressed** section is the check; gap-exposed surfaces resolve before merge; "acknowledged debt" requires a backlog entry with rationale. Explanation: CLAUDE.md ground rule 9.

**INV-21 — Blueprint definition and blueprint instance are separate.** A blueprint template (content file or in-memory registration) is the durable definition of what to spawn; a blueprint instance is the live entity created from that template, tracked via `BlueprintComponent.BlueprintId`. `BlueprintComponent` is **not** cleared on item pickup or player possession — it is preserved as an origin record. Spawn slot vacancy is tracked by `SpawnSystem` via domain events (`ItemPickedUpEvent`, `MobDiedEvent`, etc.), not by checking `BlueprintComponent` on live entities. An admin mutation (`setitem`, `setroom`, etc.) updates both the template definition (YAML file) and the live entity's data components; player-owned instances are not retroactively updated. Explanation: [06-persistence.md](06-persistence.md), [docs/implementation-plans/persistence-reform.md](../implementation-plans/persistence-reform.md).
- *Spec:* does the use case describe admin mutations? Does it commit to updating both the YAML template and the live entity?
- *Code:* does any admin mutation update the entity without also updating the template definition? Does any code clear `BlueprintComponent` on an item entity (this is no longer the spawn-slot mechanism — flag as a violation)?

**INV-20 — Agent tooling stays current with architecture.** Any slice that introduces, clarifies, or changes an architectural rule or layer pattern updates the relevant `.claude/skills/*.md` and `.claude/agents/*.md` files in the same PR. Skills and agents are developer tooling — stale guidance produces the next slice's violations.
- *Spec:* does the plan introduce or depend on a pattern any existing skill advises? If the spec changes that pattern, commit to updating the relevant skill file.
- *Code:* do changed files establish a pattern not yet reflected in any skill? Does any change contradict guidance in an existing skill or agent?

## G. Documentation architecture

The docs-as-code rules. Full explanation: [`09-documentation.md`](09-documentation.md). Checked like any other INV — drift blocks the review.

**INV-27 — One fact, one home.** An architectural rule is stated authoritatively only in this checklist; a runtime flow's diagram lives only in [flows/README.md](flows/README.md); the navigation doc-map lives only in [00-overview.md](00-overview.md). Elsewhere, a one-line summary + link is allowed — a restated or duplicated copy is not. Explanation: [`09-documentation.md`](09-documentation.md).
- *Code:* a diff that restates an invariant in its own words outside this file, reproduces a flow mermaid outside `flows/`, or forks the doc-map.

**INV-28 — Implementation-plan disintegrate-on-ship.** A slice's implementation plan ([`../implementation-plans/`](../implementation-plans/)) is a transient build artifact. At close-out, `sync-roadmap` distributes its durable content into the living docs — behavior/orchestration → [`../features/`](../features/) feature + `<system>` docs; runtime path → [`flows/`](flows/README.md); catalog diffs → [`../reference/`](../reference/); decisions/rationale/as-built → [`../roadmap/completed/<slice>.md`](../roadmap/completed/) — and then **deletes the plan**. The `roadmap/completed/<slice>.md` record is the single historical artifact and must be verified to capture the slice's design decisions *before* the plan is deleted. No trimmed spec is retained in `implementation-plans/`. Explanation: [`../implementation-plans/README.md`](../implementation-plans/README.md) lifecycle; enforced by the `sync-roadmap` skill.
- *Code:* an `implemented` plan still present in `implementation-plans/`; a deleted plan whose design decisions never landed in its `roadmap/completed/` record; durable behavior with no home in `features/`.

**INV-29 — Reference catalogs list only what exists.** `reference/*.md` describes implemented components/systems/handlers/commands; idealized/planned designs live in the matching `*-planned.md` companion, clearly labeled. A planned entry must not read as if it ships. Complements INV-16.
- *Code:* a not-yet-built design sitting in the implemented catalog (move it to `*-planned.md`); a shipped system/handler/component missing from `reference/*.md`.

**INV-30 — Content lives in its bucket.** Each doc has one responsibility per the taxonomy in [`09-documentation.md`](09-documentation.md): foundational rules and explanations in `00`–`08`, holistic feature docs and per-system design docs in [`../features/<feature>/`](../features/), runtime traces in `flows/`, catalogs in `reference/`, transient build artifacts in `implementation-plans/`, direction/status/history in `roadmap/`, retired material in `archive/`. Content in the wrong bucket moves to the bucket that owns it.

## H. Testing & verification

The testing discipline. Full explanation: [`07-testing.md`](07-testing.md). Checked like any other INV — a missing or dishonest Test plan, or a missing/red test, blocks the review.

**INV-25 — Verification discipline.** A slice that adds or changes a domain or core system, a persistence shape, a registry/validation rule, or a slice's Main Flow ships automated tests covering it, per the rubric in [07-testing.md](07-testing.md). The implementation plan's **Test plan** section is the spec-mode check (parallel to INV-18 content tooling). "Ship green" includes `dotnet test` green. **On-touch ratchet:** a slice that modifies a previously-untested system adds that system's tests before merge — coverage ratchets up as code is touched. Explanation: [07-testing.md](07-testing.md).
- *Spec:* does the **Test plan** section name a test for each new system public method, each Main-Flow postcondition that asserts internal state, each `[Persistent]` shape, and each fail-fast validation — skipping only what the rubric permits (presentation, plumbing, pure-data components)? Is it honest given the Postconditions? A postcondition asserting invisible internal state with no matching test is a finding.
- *Code:* are the tests named in the Test plan present and green? Does a system this slice modifies — but that had no prior tests — gain coverage (ratchet)?

**INV-26 — Determinism seam.** Chance- and time-dependent decisions inside a System (wherever it resides — `Core/Systems/` or `Core/Modules/<Feature>/Systems/`) resolve through an injected seam: `IRandom` for chance; the heartbeat-supplied `Elapsed`/`Timestamp` (or an injected clock) for time. Never `Random.Shared`/`new Random()`, nor a direct wall-clock read (`DateTime`/`DateTimeOffset` `.Now`/`.UtcNow`/`.Today`), inside a system. This preserves the pure-system property (INV-3, INV-5) and makes outcomes deterministically testable. Event records stamping `OccurredAt = DateTime.UtcNow` are payloads, not systems — out of scope. Explanation: [07-testing.md](07-testing.md#determinism-inv-26).
- *Spec:* does any Main-Flow step direct a system to roll randomness or branch on wall-clock time without an injected seam?
- *Code:* `Random.Shared`/`new Random()`, or a `DateTime`/`DateTimeOffset` `.Now`/`.UtcNow`/`.Today` read, inside a file under a `Systems/` path. (Randomness is fully sealed by the `IRandom` seam; pre-existing wall-clock reads in `AccountSystem` and `SpawnSystem` are acknowledged debt for a future injected clock — see [`../roadmap/backlog.md`](../roadmap/backlog.md).)

---

## Spec-review failure modes (code-mode reviewers: skip)

When reviewing an implementation plan ([`../implementation-plans/`](../implementation-plans/)) *before* implementation, beyond the per-INV spec checks above, flag:

- **SR-1 — The spec directs a layer to violate an INV.** e.g. "the dispatcher computes damage," "this domain system publishes the event." The spec must be corrected before code is written.
- **SR-2 — The spec preserves a pre-existing violation without calling it out.** "Continues to work as today" is not acceptable if "today" violates an INV. The spec must either fix it or explicitly record it as acknowledged debt with a backlog pointer.
- **SR-3 — The spec contradicts an established skill or convention.** e.g. it specifies a command shape that the `add-command` skill forbids. One of them is wrong; resolve before implementation.
- **SR-4 — A referenced flow or catalog entry doesn't exist or won't be updated.** The "Flows introduced or modified" / "Cross-cutting surfaces stressed" sections name flows/surfaces; verify each is real and that the spec commits to updating it.
- **SR-5 — An open question is load-bearing.** If a deferred decision determines whether an INV is satisfiable, it is not deferrable — it blocks implementation.

---

## Maintenance

Add an invariant here when a new architectural rule is established (typically when a slice's architecture-review surfaces a gap, as INV-5/INV-8–11 did from the slice-3 command-tier gap). Update the explanation doc in the same change and link back here. Never let an invariant live only in an agent prompt or only in CLAUDE.md — those point here.
