---
name: implement-use-case
description: Use when implementing a full gameplay use case end-to-end (shop purchase, combat pulse, crafting, etc.). Translates a use-case doc into the concrete set of components/systems/handlers/events needed, and sequences the work so each piece is independently testable. Invoke when the user picks a use case to build, or says "let's implement X" where X matches a docs/use-cases/ file.
---

# Implement a Use Case

Every gameplay scenario in [docs/use-cases/](../../../docs/use-cases/README.md) follows a fixed template:
- **Preconditions** → guard checks
- **Postconditions** → what must be true when done
- **Main flow** → the sequence that takes preconditions to postconditions
- **Events fired** → what other features learn about
- **Systems / handlers** → who owns each step

Your job is to turn those sections into real code without slipping gameplay logic into handlers or orchestration into systems.

## Order of implementation

1. **Read the use-case doc carefully.** Note every component, system, handler, and event it names.
2. **Components first.** Anything the flow mentions that isn't yet in [docs/reference/components.md](../../../docs/reference/components.md) → add via the **add-component** skill.
3. **Archetypes.** If the use case introduces a new entity type, use **add-archetype**.
4. **Domain systems next.** Pure resolvers first; state-mutating methods second. See **add-domain-system**.
5. **Events.** Define payloads for every event the use case fires. See **add-event**.
6. **Handlers.** One handler per step that orchestrates; subscribe with priorities. See **add-handler**.
7. **Command (if player-initiated).** Thin; delegates to the first handler. See **add-command**.
8. **Update the use-case doc** — set Status to `implemented` if fully done, keep `partial` if only some paths are live.
9. **Sync roadmap docs.** Run the **sync-roadmap** skill. Updates `plan.md` (phase summary, slice queue status, current focus), adds a row to `done.md`, and creates `completed/<slug>.md`. This is Phase 3 ground rule 7.
10. **Code-review gate (mandatory).** Run the `architecture-reviewer` agent in **code mode** against the diff before this branch merges. This is Phase 3 ground rule 6. Do not skip it even for "infrastructure-only" slices — the code gate catches drift between the as-built code and the spec that the spec gate cannot see.

## Guard the layer discipline

- A handler is translating step N of the flow → it calls a system method → the system returns a result → the handler publishes the next event.
- If a step's description reads like gameplay rules ("damage is reduced by armor rating", "skill improves based on difficulty"), those belong **inside** a system method, not inside the handler.
- If a step reads like orchestration ("notify witnesses; save state; update UI"), those belong **in separate subscribers**, not inlined in one handler.

## Testability

For each domain system method you add, ask: can I unit-test this with mock entities? If not, the method is doing too much. Split until yes.

## Cross-reference checks

After wiring, verify:
- Every event listed in the use-case doc's "Events fired" has a real handler subscribed.
- Every system listed in "Systems / handlers" has the method signatures the flow calls.
- The handler priorities cohere: state mutations before notifications before persistence.

## If the use-case doc is wrong

Use cases are living documents. If reality differs from the doc in a non-trivial way, update the doc as part of the implementation PR — don't silently drift. Particularly:
- If an event name changes, update every doc that referenced it (search `docs/` for the old name).
- If a system signature changes, update [docs/reference/systems.md](../../../docs/reference/systems.md).

See the worked player-death example in [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) — the handler-ordering table there is the gold-standard trace from event → priorities → systems touched.
