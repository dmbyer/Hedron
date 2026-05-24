---
name: add-command
description: Use when adding a new player or admin command (wear, drink, cast, craft, buy, etc.). Covers the ICommand shape, argument schema, privilege declaration, output via IOutputWriter, and how it connects to the dispatcher and event pipeline. Invoke when the user asks to add a command, wire up a verb, or extend the command dispatcher.
---

# Add a Command

A command is the thinnest possible layer: it declares its argument schema and privilege requirements, then delegates to a domain system or publishes an event. It does **not** contain gameplay logic and does **not** call `session.SendLineAsync` directly.

Authoritative rules: [`docs/architecture/subsystems/commands.md`](../../../docs/architecture/subsystems/commands.md) · layer discipline: [`docs/architecture/01-layers.md`](../../../docs/architecture/01-layers.md) · avoid god commands: [`docs/architecture/04-pitfalls.md`](../../../docs/architecture/04-pitfalls.md).

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
                new PlainMessage(result.ErrorMessage ?? "You don't have that.", OutputSeverity.Error))
                .ConfigureAwait(false);
            return;
        }

        await context.Output.WriteAsync(
            new PlainMessage($"You drink the {itemName}.", OutputSeverity.Confirmation))
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
6. **Output:** always write via `context.Output.WriteAsync(IOutputMessage)`. Use `PlainMessage` for text. Use `OutputSeverity.Error` for failures, `Confirmation` for success, `System` for neutral messages.
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

## Persistence from a command

A command (Initiator) may call `await _persistence.SaveEntityAsync(entityId, ct)` directly — but only under the **no-chain variant (INV-10):** the command makes a closed mutation with no downstream event fan-out needed.

Use this when:
- The command creates or mutates authored content (e.g. `dig`, `set`) and immediate durability is required.
- There is no handler that should react to the save — the command is the end of the chain.

Do **not** use this to replace event-driven persistence for runtime state that is covered by the area-scoped periodic flush.

```csharp
// ✅ admin command creating authored content — save-on-change
var result = _roomBuilder.CreateRoom(context.InvokerEntityId, direction);
await _eventBus.PublishAsync(new RoomCreatedByAdminEvent(...));
await _persistence.SaveEntityAsync(result.NewRoomEntityId, ct);
await _persistence.SaveEntityAsync(result.SourceRoomEntityId, ct);
```

See [docs/architecture/06-persistence.md](../../../docs/architecture/06-persistence.md) for when each pattern applies.

## What NOT to do

- **No `session.SendLineAsync` calls.** Use `context.Output.WriteAsync(new PlainMessage(...))`.
- **No gameplay rules in the command.** Rules live in the domain system.
- **No game-rule orchestration inside a command.** If the sequence involves conditional branching based on game state — "if skill check succeeds, publish X, else publish Y" — that logic belongs in a handler/system, not the command. *Exception (INV-8):* a command may publish multiple events when every event is an unconditional, direct consequence of the command's action with no game-rule branch between them. Test: "Would extracting this into a handler reveal game logic, or would the handler just mechanically re-publish?" If the latter, keep it in the command.
- **No `IAdminAuthorizer.IsPrivileged` calls.** Declare `RequiredPrivileges`; the dispatcher checks it.
- **No argument parsing by hand.** Declare `CommandArgumentSchema` and read via `context.Args.Get<T>(name)`.
