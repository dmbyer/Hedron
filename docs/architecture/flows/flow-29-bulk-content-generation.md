# Flow 29 — Headless Bulk Content Generation (`generate` run-mode)

**Trigger:** `dotnet run --project Server -- generate --profile <path> [--seed N]`
**Actor:** Developer (headless CLI)
**Modules:** `Server/` (run-mode shell), `Core/Modules/Authoring/` (`IContentGenerationSystem`), reuses `World`/`Items`/`Mobs` writers + `IContentValidator`

## Summary

A pure offline sweep that composes the engine's DI, generates a connected swath of world-content YAML from a generation profile, validates each emitted definition, prints a summary, and exits. It is a **no-chain Initiator (INV-10)**: it starts no telnet listener or heartbeat, spawns no world entities, and publishes nothing. This is the first headless one-shot run-mode flow; it shares no call chain with Flow 1 (server startup).

## Sequence

```mermaid
sequenceDiagram
    participant CLI as Program.Main
    participant RM as GenerationRunMode
    participant Gen as IContentGenerationSystem
    participant W as I*ContentWriter (×4)
    participant V as IContentValidator

    CLI->>RM: Matches(args) → RunAsync(args, config)
    RM->>RM: parse --profile/--seed, load + deserialize profile YAML
    RM->>RM: services.Register(config) (no gameplay hosted services)
    RM->>Gen: GenerateAsync(profile)
    Gen->>Gen: seed SeededRandom(profile.Seed); compose areas→rooms (exits)→mobs/items
    Gen->>W: WriteAsync(template) per area/room/mob/item (YAML, atomic tmp→rename)
    Gen-->>RM: GenerationResult (counts + blueprint ids)
    RM->>V: Validate(template) per emitted definition (single-definition, in-memory)
    V-->>RM: ValidationReport
    RM->>CLI: print summary; return 0 (clean) / non-zero (validation/load failure)
```

## Steps

1. **Run-mode dispatch.** `Program.Main` calls `GenerationRunMode.Matches(args)`; on the `generate` token it branches **before** building the listener host. `--profile <path>` is required (missing ⇒ usage error, exit 2); `--seed N` overrides the profile's seed.
2. **Load profile.** `GenerationRunMode.LoadProfile` deserializes the profile YAML (camelCase, same convention as content files) into a `GenerationProfile`. A missing file or unknown aspect/scaling value fails fast with a clear message and exit 2.
3. **Compose DI only.** `services.Register(configuration)` composes the engine (pure DI). **`AddGameplayHostedServices` is not called** — no `TelnetServer`, `HeartbeatBackgroundService`, `PersistenceFlushTimer`, or world-content spawn. The Ability/Effect/Aspect/Stat definition registries self-populate at construction, so the validator's cross-ref checks work with no bootstrap (Resolved Decision 4). No `EntityService` world entities are spawned (INV-12/INV-23).
4. **Generate (deterministic).** `IContentGenerationSystem.GenerateAsync(profile)` seeds a `SeededRandom` from `profile.Seed`, composes `AreaTemplate` + child `RoomTemplate`s (rooms wired into an east/west chain, consecutive areas joined up/down — a walkable graph, Resolved Decision 3), and places `MobTemplate`s/`ItemTemplate`s per density, scaled by the curve and level range. Blueprint ids are `prefix + per-kind counter` (e.g. `gen.area.0001`), never `Guid` (INV-26). The system returns a `GenerationResult`; it never publishes (INV-5).
5. **Write YAML.** The system calls each matching `I*ContentWriter.WriteAsync`, emitting files under `content/areas|rooms|mobs|items/` via the writers' existing atomic tmp→rename path. No live-world mutation (INV-12/INV-23).
6. **Validate.** The run-mode re-reads each emitted file, deserializes it through its existing deserializer, and runs `IContentValidator.Validate(template)` (the single-definition, in-memory call mode — no live entities). Failures accumulate.
7. **Report + exit.** The run-mode prints the summary (counts + first 10 blueprint ids + validation result) to stdout and returns `0` (clean) or `1` (validation/write failure) / `2` (arg/profile-load failure). No events published; no listener or heartbeat ever starts (INV-10).

## Invariants

- INV-5: `ContentGenerationSystem` returns a `GenerationResult`; it never touches the event bus.
- INV-8: generation *logic* lives in the Core system; arg parsing, DI composition, validation policy, and the exit code live in the `Server` run-mode (thin Initiator).
- INV-10: no-chain Initiator — composes, runs one operation, writes files, exits; publishes nothing, starts no heartbeat/listener.
- INV-12 / INV-23: no live entities, no `PersistentEntity`, no SQLite — YAML world-content only.
- INV-26: all randomness flows through a per-run `SeededRandom`; blueprint ids are counter-derived, not `Guid`; no wall clock is read. A fixed-seed run is byte-reproducible within a runtime image.

## Cross-references

- Systems: `IContentGenerationSystem` ([`../../reference/systems.md`](../../reference/systems.md)), `IContentValidator`, the four `I*ContentWriter`s.
- Use case: [`../../use-cases/bulk-content-generation.md`](../../use-cases/bulk-content-generation.md).
- Related flows: [Flow 27 — admin area creation](flow-27-admin-area-creation.md) (the writer half this composes).
