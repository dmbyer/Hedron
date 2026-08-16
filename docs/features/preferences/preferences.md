# Preferences

> Per-character settings a player can see and change in-game, and the framework any feature uses to make its own output optional. **Status:** live (slice prog-6).

## What it is

Some things the game tells you are useful the first hundred times and noise the thousandth. Preferences let a player turn those off without turning off the thing itself: `config progressionxp off` silences the "you feel your Body grow stronger" line while the experience keeps accruing exactly as before.

A preference belongs to the **character**, not the connection — it survives logout, restart, and reconnection. Bare `config` lists every setting with its current state and a one-line description of what it does; `config <name>` flips one; `config <name> on|off` sets it explicitly. Names can be shortened to any unambiguous prefix.

Two settings ship: `progressionxp` and `progressionimprove`, both on by default.

## How it works

`PlayerConfigurationComponent` stores only preferences a player has *explicitly set*; anything absent resolves to the shipped default in `PreferenceRegistry`. That sparseness is doing real work — changing a default reaches every player who never touched it, adding a preference needs no migration, and a player who never runs `config` carries no configuration state at all.

`IPreferenceSystem` is the read seam. A feature makes its output optional by checking `IsEnabled` **in a notification-priority handler**, never inside a domain system — so a preference can only ever change what is *said*, never what *happens*.

This is the framework the already-documented `PlayerConfigurationComponent` was always meant to be (INV-15 — it was named in `components-planned.md`, placed in the player archetype in `02-ecs.md`, and paired with a `config`/`set` verb in the backlog). It landed as a framework rather than as two bespoke booleans on one handler because player-configurable output is a player-facing surface (INV-19).

## Systems

| System | Role |
|---|---|
| [`preference-system.md`](preference-system.md) | `PlayerConfigurationComponent` (the sparse store), `PreferenceId`/`PreferenceRegistry` (the catalog and defaults), `IPreferenceSystem` (read/write with default fallback), `ConfigCommand` (the verb) |

## Surfaces

- **Commands** — `config` (alias `toggle`), player. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `PreferenceChangedEvent` (thin past-tense fact, published by the command).
- **Components** — `PlayerConfigurationComponent` (`[Persistent]`, player characters only). See [`../../reference/components.md`](../../reference/components.md).
- **Content tooling** — none. Preferences are player-set runtime state, not authored world content; the *catalog* of available preferences is compiled (Category 3), and bare `config` is its inspect surface.

## Flows

- An ordinary command on [flow-03 — player command lifecycle](../../architecture/flows/flow-03-player-command-lifecycle.md).
- First consumer: step 8 of [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md).

## Extending preferences

Add a `PreferenceId` member and a `PreferenceRegistry` row; the command, the persistence, and the default fallback pick it up with no further wiring. Gate the behaviour on `IPreferenceSystem.IsEnabled` at the point of output. Non-boolean preferences and the prompt-template field the planned component also names fold into this same component when a slice needs them — not a second one.

## Related

- [`preference-system.md`](preference-system.md) — the system design doc.
- [`../progression/progression.md`](../progression/progression.md) — the first consumer.
- [`../output/output-framework.md`](../output/output-framework.md) — `IBroadcastSystem.SendToEntityAsync`, the direct-to-entity write gated narration uses.
- [`../../roadmap/completed/progression-use-based-xp.md`](../../roadmap/completed/progression-use-based-xp.md) — as-built history (decision D5).
