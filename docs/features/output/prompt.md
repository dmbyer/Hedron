# Prompt System

> The status prompt the player sees trailing every flush — state label + resource pool readout. **Authoring checkpoint:** slice 12-a WP-B. Living document.

## What it is / does

After every flush — whether triggered by command-end, a Chat-immediate flush, or `OutputFlushTickHandler` — the session buffer calls `IPromptSource.GetPrompt(playerEntityId)` and appends the returned `PromptMessage` to the outgoing line batch. The prompt shows the player's current **state label** (when non-normal) and **HP/Mana/Stamina/Astra** current/max pairs:

```
(Fighting) HP: 42/80 Mana: 30/50 Stamina: 20/40
```

Pools with `max == 0` are omitted. Unbound sessions (`playerEntityId == 0`) receive `null` — no prompt is appended.

## How it works

`IPromptSource` is a **core-owned port** (`Core/Output/IPromptSource.cs`). The core buffer cannot read domain state (INV-2), so the boundary is the port: the core buffer calls it; a domain-aware implementation provides the content. This is the [INV-24](../../architecture/checklist.md) compute-on-read contributor shape — one source today, generalizable to `IPromptContributor` aggregation if three or more segments appear later.

`PromptComposerSystem` (`Core/Modules/Prompt/Systems/`) implements `IPromptSource`:

1. Returns `null` when `playerEntityId == 0`.
2. Calls `IEntityStateService.GetStates(entityId)` — maps flags to a state label: `Incapacitated` takes priority over `InCombat` over `Resting`; no label when no flags are set.
3. For each pool pair (`HpCurrent`/`HpMax`, `ManaCurrent`/`ManaMax`, `StaminaCurrent`/`StaminaMax`, `AstraCurrent`/`AstraMax`): calls `IStatSystem.Get(entityId, scoreId)` for current and max; skips pairs where `max == 0`.
4. Returns `new PromptMessage(stateLabel, pools)`.

`PromptMessage` is a typed `IOutputMessage` shape rendered by `TelnetOutputFormatter`: state label in `<system>` color (omitted when null), pool pairs as plain text. Future transports render the same typed value as structured gauges with no composer change.

**Compute-on-read, no cache.** A prompt composed after the tick's mutations automatically reflects post-round HP. No "dirty" flag and no `PromptChangedEvent` — that bug family is killed by the compute-on-read design (INV-24).

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IPromptSource.cs`](../../../Core/Output/IPromptSource.cs) — `GetPrompt(uint playerEntityId) → PromptMessage?`. Core-owned port; no domain reference.
- [`PromptComposerSystem.cs`](../../../Core/Modules/Prompt/Systems/PromptComposerSystem.cs) — domain implementation; reads `IEntityStateService` + `IStatSystem`. Registered as `IPromptSource` singleton in `Server/Program.cs`.

## Message shape

```
PromptMessage(StateLabel: string?, Pools: IReadOnlyList<PoolDisplay>)
PoolDisplay(Name: string, Current: int, Max: int)
```

`TelnetOutputFormatter` renders: optional `(StateLabel)` in `<system>` color, followed by `HP: x/y Mana: a/b ...` pool pairs in plain text.

## Extensibility

- **New state labels** — add a mapping in `PromptComposerSystem` when a new `EntityStateFlags` value lands (no interface change).
- **New pools** — add a `ScoreId` pair to the pool loop; the formatter renders generically.
- **Multi-segment prompt** — if three or more independent sources want to inject prompt content, generalize `IPromptSource` → `IPromptContributor` aggregation (INV-24 shape). Not built until a consumer needs it.

## Related

- [`output.md`](output.md) — holistic output feature; flush boundaries and the session buffer.
- [`output-framework.md`](output-framework.md) — how `FlushAsync` calls `IPromptSource` and appends the result.
- [`../../reference/systems.md`](../../reference/systems.md) — `PromptComposerSystem` catalog row.
- [`../../roadmap/completed/output-batching.md`](../../roadmap/completed/output-batching.md) — as-built record including the `IPromptSource` port design rationale.
