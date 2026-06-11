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
8. **Write the tests named in the use-case's Test plan (INV-25).** Use the **add-tests** skill. Cover each new/changed system method (system-unit tier), each Main-Flow postcondition that asserts player-invisible state (the matching tier), each `[Persistent]` shape (round-trip), and each fail-fast validation (throws-test). **On-touch ratchet:** if you modified a previously-untested system, add its tests now too. Then run `dotnet test` — it must be green before the code-review gate. If a system needs an un-injected seam to be testable (randomness, wall-clock, I/O), add the seam (INV-26) — don't skip the test. *(Until the `Hedron.Tests` harness lands — see [backlog](../../../docs/roadmap/backlog.md) — author the Test plan and flag this as the gating prerequisite.)*
9. **Update the use-case doc** — set Status to `implemented` if fully done, keep `partial` if only some paths are live.
10. **Update the use-cases index** — open [docs/use-cases/README.md](../../../docs/use-cases/README.md) and set the status cell in the index table to match the use-case doc's new Status value.
11. **Sync roadmap docs.** Run the **sync-roadmap** skill. Updates `plan.md` (phase summary, slice queue status, current focus), adds a row to `done.md`, and creates `completed/<slug>.md`. This is Phase 3 ground rule 7.
12. **Code-review gate (mandatory).** Run the `architecture-reviewer` agent in **code mode** against the diff before this branch merges. This is Phase 3 ground rule 6; the gate also confirms the Test-plan tests are present and `dotnet test` is green (INV-25). Do not skip it even for "infrastructure-only" slices — the code gate catches drift between the as-built code and the spec that the spec gate cannot see.

## Guard the layer discipline

- A handler is translating step N of the flow → it calls a system method → the system returns a result → the handler publishes the next event.
- If a step's description reads like gameplay rules ("damage is reduced by armor rating", "skill improves based on difficulty"), those belong **inside** a system method, not inside the handler.
- If a step reads like orchestration ("notify witnesses; save state; update UI"), those belong **in separate subscribers**, not inlined in one handler.

## Testability

For each domain system method you add, ask: can I unit-test this with constructed entities (no mocks beyond the injected seams)? If not, the method is doing too much — split until yes. This is no longer just a heuristic: step 8 **ships** the tests (INV-25), and the **add-tests** skill is the how-to. A method that needs an un-injected seam (randomness, wall-clock, external I/O) to be testable gets the seam first (INV-26) — never reach for `Random.Shared` or `DateTime.UtcNow` inside a system. See [docs/architecture/07-testing.md](../../../docs/architecture/07-testing.md) for the full strategy and the test-vs-skip rubric.

## Cross-reference checks

After wiring, verify:
- Every event listed in the use-case doc's "Events fired" has a real handler subscribed.
- Every system listed in "Systems / handlers" has the method signatures the flow calls.
- The handler priorities cohere: state mutations before notifications before persistence.
- If the slice changed how the app is **run or configured** — a new project or run-mode, a new/changed CLI argument, a new/renamed config section or key, or a changed default port/bind or build/run path — update [`README.md`](../../../README.md) to match. Keep it high-level; specific config values live in `appsettings.json`, not the README.

## If the use-case doc is wrong

Use cases are living documents. If reality differs from the doc in a non-trivial way, update the doc as part of the implementation PR — don't silently drift. Particularly:
- If an event name changes, update every doc that referenced it (search `docs/` for the old name).
- If a system signature changes, update [docs/reference/systems.md](../../../docs/reference/systems.md).

See the worked player-death example in [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) — the handler-ordering table there is the gold-standard trace from event → priorities → systems touched.
