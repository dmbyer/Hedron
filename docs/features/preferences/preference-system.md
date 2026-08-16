# Preference system

> Per-character configurable settings and the `config` verb that reads and writes them. **Authoring checkpoint:** slice prog-6. Living document.

## What it is / does

Domain-tier. `PreferenceSystem` is the single place anything asks "does this player want to see X?" A preference is a named boolean owned by the **character**, not the connection — it survives logout, restart, and reconnection.

It exists because the alternative was two bespoke booleans on the progression narration handler. Player-configurable output is a player-facing surface, so it lands its framework rather than its first instance (INV-19), and the framework it lands is the one already documented as the target: `PlayerConfigurationComponent`, named in `reference/components-planned.md`, placed in the player archetype in `architecture/02-ecs.md`, and paired with a `config`/`set` verb in the backlog (INV-15 — write against the documented target rather than inventing a parallel one).

## How it works

**The component is sparse.** `PlayerConfigurationComponent.Preferences` stores only preferences the player has *explicitly set*. An absent key resolves to the shipped default from `PreferenceRegistry`. Two consequences follow, both deliberate: changing a shipped default immediately takes effect for every player who never touched it, and adding a preference needs no migration.

**Reads never mutate.** `IsEnabled` on an entity with no component returns the registry default and attaches nothing; only `Set` creates the component. This keeps the two-level persistence opt-in honest (INV-14/INV-23) — a player who never runs `config` carries no configuration state at all.

**The registry is the catalog.** `PreferenceRegistry.All` holds one `PreferenceDefinition` per `PreferenceId` — the name the player types, the shipped default, and the one-line description `config` lists. Adding a preference is an enum member plus a row; the command, the persistence, and the default-fallback all pick it up with no further wiring (configuration Category 3 — compiled rows).

**Name resolution accepts prefixes.** `TryResolve` matches the full name case-insensitively first, then falls back to an *unambiguous* prefix. An ambiguous prefix is rejected rather than guessed — with two `progression*` settings shipped, `config progression` is an error, `config progressionxp` is not.

**`config` is an Initiator.** Bare `config` lists everything; `config <name>` flips; `config <name> on|off` sets explicitly. It reads and writes through the system (which publishes nothing, INV-5) and publishes `PreferenceChangedEvent` itself (INV-9).

## Interface

- [`IPreferenceSystem.cs`](../../../Core/Modules/Preferences/Systems/IPreferenceSystem.cs) — `IsEnabled`, `Set`, `GetAll`. Returns values, publishes nothing.
- [`PreferenceId.cs`](../../../Core/Modules/Preferences/PreferenceId.cs) · [`PreferenceRegistry.cs`](../../../Core/Modules/Preferences/PreferenceRegistry.cs) — the vocabulary and the shipped catalog.
- [`PlayerConfigurationComponent.cs`](../../../Core/Modules/Preferences/Components/PlayerConfigurationComponent.cs) — the `[Persistent]` store.
- [`ConfigCommand.cs`](../../../Core/Modules/Preferences/Commands/ConfigCommand.cs) — the `config` / `toggle` verb.

## Shipped preferences

| Name | Default | Effect |
|---|---|---|
| `progressionxp` | on | A line each time a track gains experience. |
| `progressionimprove` | on | A line each time an attribute or ability improves. |

## Considerations

- **Module home.** Its own `Core/Modules/Preferences/` rather than a corner of `Modules/Session/`: preferences are per-**character** persistent state, not per-connection, and `Session` has no module entry point (its handler is wired directly in `CompositionRoot`).
- **Persistence:** `PlayerConfigurationComponent` is `[Persistent]` and attached only to player characters, which already carry `PersistentEntity` (INV-23). Keys serialize by enum name, so a renamed `PreferenceId` member would be a breaking change — add, never rename.
- **Scope this slice:** booleans only. Non-boolean preferences and the prompt-template field the planned component also names fold in when a slice needs them — into this same component, not a second one.

## Extensibility

Add an enum member and a `PreferenceRegistry` row. Gate the behaviour on `IPreferenceSystem.IsEnabled` at the point of output — in a **notification-priority handler**, never inside a domain system, so the preference never changes what happens, only what is said about it.

## Related

- Flow: an ordinary command on [flow-03 — player command lifecycle](../../architecture/flows/flow-03-player-command-lifecycle.md); its first consumer is step 8 of [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md).
- Reference rows: [`systems.md`](../../reference/systems.md), [`components.md`](../../reference/components.md), [`commands.md`](../../reference/commands.md), [`archetypes.md`](../../reference/archetypes.md).
- [`../progression/progression-system.md`](../progression/progression-system.md) — the first consumer.
- [`../output/output-framework.md`](../output/output-framework.md) — `IBroadcastSystem.SendToEntityAsync`, the direct-to-entity write the gated narration uses.
- [`../../roadmap/completed/progression-use-based-xp.md`](../../roadmap/completed/progression-use-based-xp.md) — as-built history and design decisions.
