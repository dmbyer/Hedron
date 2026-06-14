# Communication

> Player-to-player speech in the current room (`say`) and in-game command reference (`help`/`commands`). **Status:** live (Chat module and Help module).

## What it is

A player types `say <message>` to speak to everyone in the same room. Every connected player in that room — including the speaker — immediately receives the line. There is no channel filter, no moderation buffer, and no opt-out; all room occupants see all speech.

The companion surface is the in-game help system: `help` (or its alias `?`) without an argument lists all available commands grouped by category. With a verb argument it shows long-form help for that command, falling through to the ability registry when no command matches (`help kick` shows the `kick` ability definition). The `commands` verb is a terser sibling that prints the same grouped index without the ability fallback. Both are usable while incapacitated, so a downed player can still browse commands.

**Broadcast channel mode** (global chat, newbie channel, tell) is acknowledged backlog debt — it requires channel-membership state that does not yet exist. See [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

Help may later split into its own feature folder if the help registry grows to include hand-authored topics, lore entries, or admin help pages — today it is thin enough to live here alongside Chat.

## How it works

The two pieces are independent:

- **`SayCommand`** (the Initiator) validates that a message argument is present, then publishes `PlayerSaidEvent(playerEntityId, message)`. It performs no output itself.
- **`PlayerSaidHandler`** subscribes to `PlayerSaidEvent`, reads the speaker's `LocationComponent` to find the current room, resolves the display name from `PlayerComponent`, and delegates fan-out to `IBroadcastSystem.SendToRoomAsync` with a `PlainMessage` in the `OutputSeverity.Chat` / `OutputCategory.Chat` slots. No system is involved — the handler calls the core broadcast port directly, which is appropriate because chat is pure output fan-out with no domain decision to make.

The help surface has no event pipeline — all work happens synchronously inside the command:

- **`HelpCommand`** holds `Lazy<IEnumerable<ICommand>>` and `Lazy<IVerbRegistry>` (both lazy to break the circular dependency: `HelpCommand` is itself an `ICommand` registered in the same DI container as `IVerbRegistry`). Without a verb argument it builds a filtered, category-grouped index and writes `HelpIndexMessage`. With a verb argument it runs exact-then-prefix resolution through `IVerbRegistry`, applies a visibility gate (admin commands hidden from players), and writes `HelpEntryMessage`. When no command matches, it falls through to `IAbilityRegistry` prefix-matching. The special topic keywords `skills`, `spells`, and `abilities` append a global ability catalog after the command entry.
- **`CommandsCommand`** is a simpler read-only sibling: same visibility filtering, same index output shape, no verb argument and no ability fallback.

## Systems

There are no domain systems in the Chat or Help modules. Chat routes through the shared `BroadcastSystem` (core tier); Help queries the command and ability registries directly in-command.

| System | Role |
|---|---|
| [`chat-system.md`](chat-system.md) | The `say` verb → `IBroadcastSystem` room broadcast path |
| [`help-system.md`](help-system.md) | The help index / verb lookup / ability fallback design |

## Surfaces

- **Commands** — `say`, `help` / `?`, `commands`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `PlayerSaidEvent` (thin past-tense payload: `PlayerEntityId`, `Message`). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Output shapes** — `HelpIndexMessage`, `HelpEntryMessage`, `HelpIndexEntry` (in `Core/Output/`). See [`help-system.md`](help-system.md).
- **Components** — none; Chat and Help carry no per-entity state.

## Flows

None. `say` is a two-step initiator→handler pipeline, and `help`/`commands` are synchronous in-command reads — neither warrants a dedicated journey.

## Related

- [`../../reference/systems.md`](../../reference/systems.md) — `BroadcastSystem` row (the core fan-out seam Chat uses).
- [`../../features/output/output-framework.md`](../output/output-framework.md) — the full broadcast model and `OutputCategory.Chat` slot design.
- [`../../features/abilities/abilities.md`](../abilities/abilities.md) — `IAbilityRegistry` consumed by `HelpCommand` for ability fallback lookups.
- [`../../roadmap/backlog.md`](../../roadmap/backlog.md) — broadcast channel-mode debt entry.
