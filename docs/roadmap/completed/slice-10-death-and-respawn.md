# Phase 3 Slice 10 — Death and Respawn

**PR:** #102 (code) · #101 (spec + INV-22 boundary-save categories) · **Spec:** [`../../implementation-plans/death-and-respawn.md`](../../implementation-plans/death-and-respawn.md)

> Ledger backfilled retroactively (merged in #102 without a `done.md`/`completed/` entry at the time).

## Outcome

Introduced the player **incapacitation → bleed-out → death → respawn** lifecycle and the **mob-death reward seam**. Death is HP-pool-driven, not command-driven: any path that drives `CurrentHp` to 0 (a combat round, a `poison` DoT tick, an admin `setplayer hp 0`) incapacitates the player; while incapacitated they can issue no commands and bleed 1 HP/tick; at the death floor (−10) they respawn at a per-player persisted location with state reset, impermanent effects expired, and all four pools restored to 25% of max. This turns the slice-9 "clamp to 1 HP" stub into a real terminal outcome.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `IDeathSystem`/`DeathSystem` (domain) | `Core/Modules/Death/Systems/` | `OnHpChanged` (the single HP-threshold seam), `Respawn`, `SetRespawn`; returns `DeathTransition`; never publishes |
| `RespawnComponent { RoomBlueprintId }` (`[Persistent]`) | `Core/ECS/Components/` | blueprint-id (cross-restart), player-only |
| `DeathTickHandler` (heartbeat → bleed) | `Core/Modules/Death/Handlers/` | publishes `PlayerBleedingEvent`/`PlayerDiedEvent` |
| `PlayerDeathHandler` (respawn orchestration) | `Core/Modules/Death/Handlers/` | calls `Respawn`, publishes `PlayerRespawnedEvent` |
| `DeathNarrationHandler` (priority 80) | `Core/Modules/Death/Handlers/` | death/bleed/respawn output fan-out |
| Dispatcher incapacitation gate + `ICommand.UsableWhileIncapacitated` (default `false`) | `Core/Commands/` | default-deny; `help`/`commands`/`score` allowlisted |
| `IAttributeSystem.SetCurrentHp` floor `0`→`Death:HpFloor` (−10) | `Core/Modules/Attributes/` | combat's clamp-to-1 stub removed |
| `IEffectSystem.RemoveImpermanent` | `Core/Modules/Effects/` | drops non-`UntilRemoved` on death |
| `MobDiedEvent` += `KillerEntityId` | `Core/Modules/Mobs/` | reward seam (no consumer yet) |
| `setrespawn` admin command + `DeathOptions` (`Death:*` config) | `Core/Modules/Death/` | admin boundary save; `HpFloor`/`BleedPerTick`/`RespawnPoolPercent` |
| `PlayerIncapacitatedEvent`/`PlayerBleedingEvent`/`PlayerDiedEvent`/`PlayerRespawnedEvent`/`PlayerRespawnSetByAdminEvent` | `Core/Modules/Death/Events/` | thin, past-tense |
| Flows 22 (incapacitation/bleed-out) + 23 (player death/respawn) | `docs/architecture/flows/` | added to the catalog |

## Notable design points

- **HP threshold, not a death command** — `IDeathSystem.OnHpChanged` is the single decision seam, **called by the Initiator/Handler that mutated HP** (combat tick, effect tick), never by `IAttributeSystem` (a compute seam must not chain into a domain decision; INV-5). This is why combat *and* DoT both reach one pipeline with no duplicated death logic.
- **Dispatcher gate is default-deny** — `UsableWhileIncapacitated` defaults `false` so a new command fails safe; the gate lives in the dispatcher (transient *state*, not a *privilege*), keeping the `help` visibility filter intact.
- **Respawn stores blueprint id, not entity id** — mirrors `LocationComponent`'s cross-restart model; resolved to a live room at respawn time.
- **Impermanent vs permanent reuses `EffectLifetime`** — death-expiry and persistence-inclusion share one definition of "permanent" (`UntilRemoved`), so they cannot drift.
- **Soft death** — relocate + 25% restore; no corpse, item loss, or XP loss (deferred). `KillerEntityId` is the single seam a future `RewardSystem` subscribes to.
- Introduced the **INV-22 boundary-save categories** (construction / admin / session-end) in the same effort (#101).

## Deviations from the use-case doc

None — shipped per spec; reward/loot/corpse logic explicitly scoped out (seam only).

## Follow-ups unlocked

- **11-b (ability invocation):** offensive ability kills reuse the `CombatEndedEvent` → slice-10 death path with zero new wiring.
- A future **RewardSystem** (XP/loot) subscribes to `MobDiedEvent.KillerEntityId`.
- A future harsher-death / corpse-retrieval slice builds on the soft-death substrate (`PlayerDiedEvent` carries the death room).
