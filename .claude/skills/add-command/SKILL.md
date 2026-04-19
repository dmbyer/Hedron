---
name: add-command
description: Use when adding a new player or admin command (wear, drink, cast, craft, buy, etc.). Covers parsing, the thin-command rule (parse -> call system -> publish event), and how it connects to the handler pipeline. Invoke when the user asks to add a command, wire up a verb, or extend the command dispatcher.
---

# Add a Command

A command is the thinnest possible layer: it parses player input, resolves a target from the world, and asks a domain system to do the work. It does **not** contain gameplay logic.

Authoritative rules: [docs/architecture/01-layers.md](../../../docs/architecture/01-layers.md) · command pipeline rule: [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) · avoid god commands: [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).

## Shape

```csharp
public class DrinkCommand : ICommand
{
    private readonly IInventorySystem _inv;
    private readonly IPotionSystem _potions;
    private readonly IEventBus _bus;

    public void Execute(uint playerId, string args)
    {
        var potionId = _inv.FindByName(playerId, args);
        if (potionId == 0) { _bus.Publish(new CommandFailedEvent(playerId, "You don't have that.")); return; }
        var result = _potions.Drink(playerId, potionId);
        _bus.Publish(new ItemDestroyedEvent(potionId));
        // further events per result
    }
}
```

Target size: **≤ 30 lines**. If it grows, the logic belongs in a system, not in the command.

## Steps

1. File location: `Core/Modules/<Feature>/Commands/<X>Command.cs` (feature-owned) or `Core/Commands/<X>Command.cs` (cross-cutting like `look` or `who`).
2. Register the verb + aliases with the command dispatcher.
3. Parse args; resolve target IDs via `InventorySystem.FindByName`, `LocationSystem.FindInRoom`, etc.
4. Call the relevant domain system (`PotionSystem.Drink`, `EquipmentSystem.Equip`, `CombatSystem.InitiateCombat`).
5. Publish the resulting events (see **add-event** skill for payload shape).
6. If the command implements a use case, add the use-case file's "Main flow" step that invokes this command.

## What NOT to do

- **Don't put gameplay rules in the command.** Rules live in the domain system.
- **Don't do multi-step orchestration inside a command.** If you're publishing event A, then calling system X, then publishing event B conditional on a second system — you're writing a handler, not a command. Move it.
- **Don't bypass the event bus for "simple" cross-cutting effects.** Notifications, persistence, AI updates all listen on events; a command that skips the bus will skip those.
- **Don't duplicate parsing logic across commands.** Share resolvers like `InventorySystem.FindByName`.

## Access control

Commands that mutate restricted state (admin verbs, container access) must call `AccessControlSystem.CanAccess` **before** any mutation.
