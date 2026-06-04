# Phase 3 Slice 11-c — Resource Regeneration + `rest`

**PR:** (this branch) · **Spec:** [`../../use-cases/resource-regeneration.md`](../../use-cases/resource-regeneration.md)

## Outcome

Added out-of-combat baseline regeneration for all four resource pools (HP/Mana/Stamina/Astra) and the `rest`/`stand` commands that accelerate it. Every entity not in combat slowly regenerates each pool; an entity in the `Resting` state regenerates every tick (≈3× the idle rate). This closes the loop on cluster 11: abilities now have a cost, are invocable, and are recoverable. The slice also hardened the `EntityStateService` with a centralized auto-exit table — entering `InCombat` unconditionally clears `Resting` regardless of which call site initiates combat.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `IRegenerationSystem`/`RegenerationSystem` (domain) | `Core/Modules/Regeneration/Systems/` | `ApplyTickRegen(tickId)`; state-based rate; no events, no persistence |
| `RegenerationTickHandler` (`HeartbeatTickEvent`, p=20) | `Core/Modules/Regeneration/Handlers/` | no-chain sweep; calls `ApplyTickRegen`, publishes nothing |
| `RestCommand` (`rest`) | `Core/Modules/Regeneration/Commands/` | `TryEnterState(Resting)` + message + `EntityStateChangedEvent` |
| `StandCommand` (`stand` / `wake`) | `Core/Modules/Regeneration/Commands/` | `ExitState(Resting)` + message + `EntityStateChangedEvent` |
| `RegenerationModule` | `Core/Modules/Regeneration/RegenerationModule.cs` | DI entry point |
| `EntityStateService._autoExits` table | `Core/Modules/EntityState/Systems/EntityStateService.cs` | `InCombat → Resting`; applied in `TryEnterState` after validation |
| `MoveCommand` rest-break guard | `Core/Modules/Movement/Commands/MoveCommand.cs` | explicit `ExitState(Resting)` + `EntityStateChangedEvent` before move |
| `entity-state-management.md` rule table amended | `docs/use-cases/entity-state-management.md` | added Auto-exits column + design note (INV-24 analogue for mutual exclusion) |
| Flow 16 amended | `docs/architecture/flows/flow-16-heartbeat-tick.md` | `RegenerationTickHandler` added to participant list, mermaid, and prose |
| Reference catalogs | `docs/reference/commands.md`, `systems.md`, `handlers.md` | `rest`, `stand`/`wake`, `RegenerationSystem`, `RegenerationTickHandler` added; six MoveCommand entries updated |

## Spec-review provenance

Spec gate (spec-mode) ran before implementation. Two nits addressed: the movement hook `EntityStateChangedEvent` publication was made explicit in the WP-2 description; the Flow 16 mermaid update was clarified to include the diagram (not just prose). Code gate (code-mode) ran after implementation. Two nits addressed: all six MoveCommand catalog entries updated with the new dependency and conditional event; use-case doc trimmed to implemented state.

## Notable design points

- **Auto-exit table centralizes mutual exclusion.** Rather than requiring every combat-initiation call site to explicitly `ExitState(Resting)`, `EntityStateService._autoExits` fires the clear inside `TryEnterState` after validation passes. The caller's `EntityStateChangedEvent` publication is unaffected (captures `oldStates` before, `newStates` after; single event reflects both the cleared flag and the entered flag). Future mutual-exclusion pairs add one row to the static table.
- **Movement requires an explicit hook.** Movement does not call `TryEnterState`, so it cannot benefit from the auto-exit table; `MoveCommand` explicitly calls `ExitState(Resting)` before attempting the move. Rest is broken even if the exit is blocked.
- **No per-entity timer.** The `tickId % IdleIntervalTicks` cadence means all idle entities regenerate on the same tick — no new component or accumulator required.
- **Effects stacking.** `RegenerationSystem` and `EffectTickHandler` write through `IAttributeSystem` independently. A `regen` spell and baseline rest regeneration apply additively (clamped at max). Crucially, `EffectTickHandler` does NOT suppress periodic ticks for `InCombat` entities; `RegenerationSystem` does. A blessed `regen` effect ticks through combat; baseline regeneration does not.
- **Mobs regenerate too.** The `PoolsComponent` sweep includes mobs; a damaged mob that combat left alive (e.g. the player fled) heals at the idle rate. Mobs never `rest` (no command), so they only ever get the slow rate.
- **Rates are Category-3 constants.** `RegenAmount = 1`, `IdleIntervalTicks = 3`. Promotion to configuration deferred to the dedicated regeneration use-case (depends on backlogged robust-config model).

## Deviations from the use-case doc

None — shipped per spec (auto-exit approach was spec-amended pre-implementation with the owner).

## Follow-ups unlocked

- **Slice 12 (Shopping):** ability costs are recoverable; all cluster-11 prerequisites are met.
- A future **dedicated regeneration use-case** promotes rates to configuration/content, adds per-area rates, stat-derived regen, and food/effect interaction.
