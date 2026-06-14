---
name: add-command
description: Use when adding a new player or admin command (wear, drink, cast, craft, buy, etc.). Covers the ICommand shape, argument schema, privilege declaration, output via IOutputWriter, and how it connects to the dispatcher and event pipeline. Invoke when the user asks to add a command, wire up a verb, or extend the command dispatcher.
---

# Add a Command

A command is the thinnest possible layer: it declares its argument schema and privilege requirements, then delegates to a domain system or publishes an event. It does **not** contain gameplay logic and does **not** call `session.SendLineAsync` directly.

Authoritative rules: [`docs/features/commands/command-framework.md`](../../../docs/features/commands/command-framework.md) · layer discipline: [`docs/architecture/01-layers.md`](../../../docs/architecture/01-layers.md) · avoid god commands: [`docs/architecture/04-pitfalls.md`](../../../docs/architecture/04-pitfalls.md).

## Shape

```csharp
public sealed class DrinkCommand : ICommand
{
    private readonly IPotionSystem _potions;
    private readonly IEventBus _eventBus;

    public string Name => "drink";
    public IReadOnlyList<string> Aliases { get; } = Array.Empty<string>();
    public CommandCategory Category => CommandCategory.Player;
    public CommandMatchingMode MatchingMode => CommandMatchingMode.Partial; // Partial for player; Full for admin
    public string ShortDescription => "Drink a potion from your inventory.";
    public string LongDescription => "Consumes a potion by name from your inventory, applying its effect immediately.";
    public string Usage => "drink <itemName>";
    public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
        Array.Empty<IAuthorizationRequirement>(); // empty = public
    public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
    {
        new CommandArgument("itemName", typeof(string), CommandArgumentKind.Token,
            Required: true, "Name of the potion to drink."),
    });

    public DrinkCommand(IPotionSystem potions, IEventBus eventBus)
    {
        _potions = potions;
        _eventBus = eventBus;
    }

    public async Task ExecuteAsync(CommandContext context)
    {
        var itemName = context.Args.Get<string>("itemName");

        var result = _potions.Drink(context.InvokerEntityId, itemName);
        if (!result.Success)
        {
            await context.Output.WriteAsync(
                new PlainMessage(result.ErrorMessage ?? "You don't have that.", OutputSeverity.Error, OutputCategory.System))
                .ConfigureAwait(false);
            return;
        }

        await context.Output.WriteAsync(
            new PlainMessage($"You drink the {itemName}.", OutputSeverity.Confirmation, OutputCategory.System))
            .ConfigureAwait(false);

        await _eventBus.PublishAsync(new PotionConsumedEvent(context.InvokerEntityId, result.ItemId))
            .ConfigureAwait(false);
    }
}
```

**Growing past ~30 lines is a smell, not a hard limit.** If the extra lines are game-rule logic, extract them into a domain system. If they're mechanical null-guards or arg validation, the command is still correctly thin.

## Steps

1. **File location:** `Core/Modules/<Feature>/Commands/<X>Command.cs` (feature-owned) or `Core/Commands/<X>Command.cs` (cross-cutting like `look` or `who`).
2. **Implement `ICommand`** — all eight interface members. Use `CommandArgumentSchema.Empty` for no-arg commands.
3. **Argument kinds:**
   - `Token` — single whitespace-delimited token (or double-quoted group). Works for strings, ints, uints, and enums.
   - `RestOfLine` — everything after previous tokens. Use for `say`, `tell`, multi-word freeform input.
   - `Quantified` — leading count + token. Deferred; not yet used.
4. **Matching mode:**
   - Player command: `MatchingMode => CommandMatchingMode.Partial` — prefix resolution enabled (e.g. `dr` → `drink`).
   - Admin command: `MatchingMode => CommandMatchingMode.Full` — exact match required; prevents accidental prefix dispatch of destructive verbs.
   - This is a required interface member — omitting it is a compile error.
5. **Privilege:**
   - Public command: `RequiredPrivileges = Array.Empty<IAuthorizationRequirement>()`
   - Admin command: `RequiredPrivileges = new IAuthorizationRequirement[] { new AdminRequirement() }`
   - **Never** call `IAdminAuthorizer.IsPrivileged` inside the command body — the dispatcher handles it.
6. **Output:** always write via `context.Output.WriteAsync(IOutputMessage)`. Use `PlainMessage(text, severity, category)` for text — three arguments are required. Severity: `Error` for failures, `Confirmation` for success, `System` for neutral messages. Category: `System` for command responses; `Chat` for social messages (triggers immediate flush); `Help` for help content.
7. **Register** in the feature module: `services.AddSingleton<ICommand, DrinkCommand>();`
8. **Subscribe** the handler that consumes the event the command publishes (if any) in `Server/Program.cs`.
9. **Docs:** add a row to `docs/reference/commands.md`.

## Admin commands

```csharp
public string Name => "grant";
public CommandMatchingMode MatchingMode => CommandMatchingMode.Full; // required: exact match for admin commands
public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
    new IAuthorizationRequirement[] { new AdminRequirement() };
```

No `IsPrivileged` call in the body. Privilege is enforced by the dispatcher before `ExecuteAsync` is called. The compiler forces you to declare `RequiredPrivileges` (it is a required interface member); empty list = public.

Admin commands declare `MatchingMode.Full` so they are never accidentally dispatched by a partial prefix — typing `d` reaches `down` (a player command alias), not `dig`.

## Persistence from a command (INV-22)

Two persistence domains exist; which one the command touches determines what it does for durability:

**World content commands (`dig`, `mkitem`, `mkmob`, `set`, `setitem`, `setmob`).**
World entities carry **no** `PersistentEntity` — they are never in the SQLite flush pool. Their sole durable form is the YAML file. A world-content command calls the domain system (which mutates the live entity) and writes the YAML template; it never calls `SaveEntityAsync`.

```csharp
// ✅ admin room creation — YAML is the only durable form, no SaveEntityAsync
var result = _roomBuilder.CreateRoom(context.InvokerEntityId, direction);
await _eventBus.PublishAsync(new RoomCreatedByAdminEvent(...));
await _roomContentWriter.WriteAsync(result.Template, ct); // writes data/content/rooms/<id>.yaml
// no SaveEntityAsync — the YAML file is the room's durable state
```

There are **three** permitted `SaveEntityAsync` call categories (INV-22); every other state change relies on the periodic flush.

**1. Account/character construction commands (`LoginFlow`, `AccountSystem.CreateCharacterAsync`).**
These create entities that must survive restart. They add `PersistentEntity` to the entity, then call `SaveEntityAsync` **once** to make the ID durable before the flow returns.

```csharp
// ✅ character construction — make the new persistent entity durable before returning
_entityService.AddComponent(entityId, new PersistentEntity());
await _persistence.SaveEntityAsync(entityId, ct);
```

**2. Admin boundary save (`setplayer`, `setrespawn`).**
An admin-gated command that mutates an *already-persistent* entity's state through a domain system may call `SaveEntityAsync` **once** after the mutation, so the deliberate, out-of-band administrative change lands durably without waiting for the next flush. The shape is: mutate via the system → `SaveEntityAsync` → publish an audit event. This applies **only** behind the admin privilege gate — it does not license ordinary gameplay commands or handlers to save runtime mutations.

```csharp
// ✅ admin boundary save — mutate via system, persist, then audit
_deathSystem.SetRespawn(playerEntityId, roomBlueprintId);
await _persistence.SaveEntityAsync(playerEntityId);
await _eventBus.PublishAsync(new PlayerRespawnSetByAdminEvent(...));
```

**3. Session-end boundary save (`quit`, raw disconnect).**
When a player session ends, the player is force-saved **once** so their final state is durable before they leave. The player `quit` command force-saves then disconnects; a raw disconnect is handled by `PlayerSessionHandler`. This is the only save category that legitimately runs in a handler.

```csharp
// ✅ quit command — force-save the player, then disconnect
await _persistence.SaveEntityAsync(context.InvokerEntityId);
await _eventBus.PublishAsync(new PlayerQuitEvent(...)); // the session layer tears down the connection
```

Runtime state changes (movement, HP, inventory) outside these three categories are covered by the `PersistenceFlushTimer` periodic sweep — no command or handler calls `SaveEntityAsync` for them.

See [docs/architecture/06-persistence.md](../../../docs/architecture/06-persistence.md) and INV-22 for the full rules.

## Admin blueprint-authoring commands (INV-21)

When the command creates or mutates a **blueprint** — a template that seeds live entities on startup (rooms, items, mobs) — two responsibilities apply beyond the standard command shape:

**1. Persist the template definition to disk.**
Write the YAML file immediately via the appropriate content writer. Without this the blueprint definition evaporates on restart.

**2. Keep template and live entity in sync on mutation.**
A `set*` command that changes a property must update both the live entity (via the domain system) and the YAML file on disk. Player-owned instances are independent — they were promoted to persistent when picked up and are NOT retroactively updated.

**`BlueprintComponent` is NOT cleared on pickup (INV-21).**
When a player picks up a world-spawn item, `ItemContextHandler` promotes the item entity to persistent by adding `PersistentEntity`. `BlueprintComponent` stays on the entity as an origin record. Spawn-slot vacancy is tracked by `SpawnSystem` via `ItemPickedUpEvent`, not by inspecting `BlueprintComponent` on live entities. Do not remove `BlueprintComponent` from item entities.

```csharp
// ✅ blueprint-authoring command: write YAML, no SaveEntityAsync
var result = _itemBuilder.CreateItem(name, roomEntityId);
await _itemContentWriter.WriteAsync(result.Template, ct); // write to data/content/items/
// no SaveEntityAsync — world content is never persistent
await _eventBus.PublishAsync(new ItemCreatedByAdminEvent(...));
```

## Non-ICommand dispatcher-internal services (Phase 3 pattern)

Some verbs are **not** registered as `ICommand` at all — they are internal services routed by the dispatcher after its two standard command-resolution phases miss. The current example is `SkillInvocationCommand` (slice 11-b).

**When to use this pattern:** A verb is *per-actor* (what a specific player can invoke depends on their state, not a global registry), and registering a global `ICommand` for it would either be impossible (you don't know the verb at startup) or wrong (the verb would show up in `help`/`commands` for all players). Active-Skill bare verbs are the canonical case: `kick` is only a valid verb for a player who has learned it.

**How it works:**
1. `IAbilityVerbResolver` (a core seam, `Core/Commands/`) maps the typed verb to an ability id for the invoking entity at dispatch time.
2. `CommandDispatcher` Phase 3 calls `TryResolve`; on a unique hit it calls `SkillInvocationCommand.InvokeAsync(session, actorId, abilityId, rawTail, output)` directly.
3. `SkillInvocationCommand` is a singleton registered as its concrete type (NOT as `ICommand`): `services.AddSingleton<SkillInvocationCommand>()`.
4. It is not enumerable by `IVerbRegistry` and does not appear in `help`/`commands`. Discovery is via the player-specific `skills` command.

**Shared invocation pipeline:** When two or more commands share non-trivial orchestration (e.g. `CastCommand` and `SkillInvocationCommand` both need target resolution + combat entry + `Activate` + event publication), extract the shared logic into an **initiator-tier helper** (e.g. `AbilityInvocationPipeline`). Register it as a singleton concrete type. It is called exclusively by command-tier code and inherits their event-publish permission. All events it publishes must be unconditional consequences of a sequential step — branch-on-state conditional publishing belongs in a handler.

See [`docs/features/commands/command-framework.md`](../../../docs/features/commands/command-framework.md) for the architectural rationale (command-vs-ability precedence, why registered commands always win).

See [INV-21](../../../docs/architecture/checklist.md) for the full invariant.

## What NOT to do

- **No `session.SendLineAsync` calls.** Use `context.Output.WriteAsync(new PlainMessage(text, severity, category))`.
- **No gameplay rules in the command.** Rules live in the domain system.
- **No game-rule orchestration inside a command.** If the sequence involves conditional branching based on game state — "if skill check succeeds, publish X, else publish Y" — that logic belongs in a handler/system, not the command. *Exception (INV-8):* a command may publish multiple events when every event is an unconditional, direct consequence of the command's action with no game-rule branch between them. Test: "Would extracting this into a handler reveal game logic, or would the handler just mechanically re-publish?" If the latter, keep it in the command.
- **No `IAdminAuthorizer.IsPrivileged` calls.** Declare `RequiredPrivileges`; the dispatcher checks it.
- **No argument parsing by hand.** Declare `CommandArgumentSchema` and read via `context.Args.Get<T>(name)`.
