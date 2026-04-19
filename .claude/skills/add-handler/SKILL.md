---
name: add-handler
description: Use when adding a new handler or splitting an existing one. Covers responsibilities (orchestration only), priority choice, subscription registration, and the rule that handlers call systems and publish events but never contain domain logic. Invoke when the user asks to add a handler, route an event, or orchestrate a new flow.
---

# Add a Handler

A handler orchestrates. It turns an input (command, event) into calls on domain systems and publishes events. It does **not** contain gameplay rules — those live in systems.

Authoritative rules: [docs/architecture/01-layers.md](../../../docs/architecture/01-layers.md), [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) · catalog: [docs/reference/handlers.md](../../../docs/reference/handlers.md).

## Handler shape

```csharp
public class FooHandler : IEventHandler<BarEvent>
{
    private readonly IFooSystem _foo;
    private readonly IEventBus _bus;

    public FooHandler(IFooSystem foo, IEventBus bus) { _foo = foo; _bus = bus; }

    public void Handle(BarEvent e)
    {
        var result = _foo.Do(e.SubjectId);
        if (result.Success) _bus.Publish(new FooHappenedEvent(e.SubjectId));
    }
}
```

Key properties:
- Dependencies injected; no static singletons.
- One input method; no cross-feature orchestration in one handler.
- Returns void (handlers are fire-and-forget).

## Picking a priority

Priorities are coarse buckets (10/20/50/80/90/95). See [docs/architecture/03-events.md](../../../docs/architecture/03-events.md#handler-priority).

- **Lower priority = runs earlier.**
- State-mutating handlers (combat state changes, inventory moves) should run before notification handlers.
- Persistence typically runs last so it captures final state.

If you find yourself reaching for a fine-grained priority (like 37), it's a sign you should split into a phased event instead.

## Steps

1. File location: `Core/Modules/<Feature>/Handlers/<X>Handler.cs`.
2. Register the handler in the feature's `AddXModule(IServiceCollection)` extension (e.g. `Core/Modules/<Feature>/<Feature>Module.cs`), and subscribe it via `eventBus.Subscribe<Event>(handler, priority)` in the same place.
3. Inject only the systems you actually need.
4. Update [docs/reference/handlers.md](../../../docs/reference/handlers.md) with a one-line row for the new handler.
5. If this handler is the orchestrator for a use case, update the use-case file's "Systems / handlers" section.

## Don't

- **Don't put domain logic in a handler.** If a rule like "armor reduces damage by X" lives in a handler, move it into the relevant system.
- **Don't create a "god handler"** that handles many unrelated events. Split along feature lines.
- **Don't call other handlers directly.** Publish events instead — that's what the bus is for.
- **Don't skip the event bus** even for "obvious" reactions ("just call the notification code directly"). Every cross-feature effect goes through the bus.

See the god-handler anti-pattern in [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
