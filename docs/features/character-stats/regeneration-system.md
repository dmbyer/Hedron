# Regeneration System

> Baseline out-of-combat resource regeneration for all four pools (HP, Mana, Stamina, Astra), driven by the heartbeat tick. `Resting` accelerates it; `InCombat` suppresses it entirely. **Status:** live (slice 11-c).

## What it is / does

`RegenerationSystem` is the **heartbeat sweep** that applies per-pool deltas to every entity carrying a `PoolsComponent`. It is a closed mechanical sweep with no downstream chain: it never publishes events (INV-10's no-chain shape) and never persists (INV-5). The `rest`/`stand` commands are the player's surface for entering and exiting the `Resting` state, which is the only meaningful influence a player has on regeneration rate.

This is a deliberately small, hardcoded-rate slice. The rates are Category-3 balance constants baked into the system. A future dedicated regeneration use-case will promote them to configuration/content and add the richer model (per-area rates, stat-derived regen, food/effects interaction).

## How it works

### State-based rate

| Entity state | Regeneration |
|---|---|
| `InCombat` | suppressed entirely |
| `Resting` | `+RegenAmount` to each pool every tick (accelerated) |
| Idle (neither) | `+RegenAmount` to each pool every `IdleIntervalTicks`-th tick |

**Constants (Category-3):** `RegenAmount = 1`, `IdleIntervalTicks = 3`. Idle rate ≈ 1/pool/3 ticks; resting rate ≈ 3× idle.

The idle cadence is global — `tickId % IdleIntervalTicks == 0` — so all idle entities regenerate on the same tick. No per-entity timer or accumulator component is needed.

Every applied delta rides the `[0, max]` clamp in `IAttributeSystem` (INV-8): a pool at max is a no-op.

### Resting state

`rest` enters `Resting` via `IEntityStateService.TryEnterState`. `stand` (alias `wake`) exits it via `ExitState`. Rest is broken automatically:

- **Movement** — `MoveCommand` explicitly calls `ExitState(Resting)` before the move.
- **Combat entry** — `EntityStateService._autoExits` fires `ExitState(Resting)` inside `TryEnterState(InCombat)` regardless of which call site initiates combat. No explicit hook is needed in `KillCommand` or `AbilityInvocationPipeline`.

### Tick integration

`RegenerationTickHandler` subscribes to `HeartbeatTickEvent` (priority `HandlerPriority.Domain` = 20, alongside effect/combat tick handlers) and calls `IRegenerationSystem.ApplyTickRegen(@event.TickId)`. It publishes nothing. The full heartbeat participant sequence is [flow-16-heartbeat-tick](../../architecture/flows/flow-16-heartbeat-tick.md).

### Mobs regenerate too

The `PoolsComponent` sweep includes mobs. A damaged mob that was left alive (e.g., the player fled) heals at the idle rate. Mobs never `rest` (no command), so they only ever get the slow rate.

### Effects stacking

`RegenerationSystem` and `EffectTickHandler` write through `IAttributeSystem` independently. A `regen` spell (`EffectKind.Periodic`, `EffectPhase.Early`) and baseline rest regeneration apply additively (clamped at max). Crucially, `EffectTickHandler` does **not** suppress periodic ticks for `InCombat` entities; `RegenerationSystem` does. A blessed `regen` effect ticks through combat; baseline regeneration does not.

## Interface

- [`IRegenerationSystem.cs`](../../../Core/Modules/Regeneration/Systems/IRegenerationSystem.cs) — `ApplyTickRegen(long tickId)`. Iterates all `PoolsComponent` entities, checks state via `IEntityStateService`, applies deltas through `IAttributeSystem`. Returns nothing; never publishes; never persists.

## Considerations

- **Silent by design.** Per-tick regeneration produces no message. A "you regenerate 1 hp" line every few seconds would be spam; the player observes recovery via `score`. A future "fully rested" notification is an additive handler.
- **Inspection is `score`.** Regeneration's observable state is the pool values `score` already renders. No new inspection surface is needed.
- **Authoring of rates is deferred.** The constants are isolated in `RegenerationSystem` so promotion to configuration is a cheap, localized change. It depends on the backlogged robust-config model — tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Related

- [`character-stats.md`](character-stats.md) — the holistic feature view.
- [`attribute-system.md`](attribute-system.md) — the clamped pool setters regeneration writes through.
- [`../../architecture/flows/flow-16-heartbeat-tick.md`](../../architecture/flows/flow-16-heartbeat-tick.md) — the heartbeat that drives the regen sweep.
- [`../combat/entity-state.md`](../combat/entity-state.md) — `Resting` / `InCombat` flag semantics and the auto-exit table.
- [`../../reference/systems.md`](../../reference/systems.md) — `RegenerationSystem` catalog row.
- [`../../roadmap/completed/slice-11c-resource-regeneration.md`](../../roadmap/completed/slice-11c-resource-regeneration.md) — the as-built record and decision history.
