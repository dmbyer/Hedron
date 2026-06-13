# Canonical Flows

> Living catalog of end-to-end runtime flows in Hedron. The static architecture lives in [00-overview.md](../00-overview.md) through [05-configuration.md](../05-configuration.md); the inventory of components/systems/handlers lives under [`../reference/`](../../reference/). **This file traces what actually happens at runtime** — the dynamic call chains a developer or designer needs to understand "if I do X, what executes, and in what order?"
>
> **Update rule.** Every slice's PR must add or update the relevant flow file(s) to reflect the as-built code for any flow it introduces, modifies, or extends. CLAUDE.md ground rule 9 makes this a merge gate; the architecture-reviewer agent verifies the doc matches the diff.

---

## Index

| # | Flow | Trigger | Slice introduced |
|---|---|---|---|
| 1 | [Server startup](flow-01-server-startup.md) | `dotnet run --project Server` | Phase 2 (extended in slice 2) |
| 2 | [Player connection](flow-02-player-connection.md) | TCP client connects on the configured port | Phase 2 |
| 3 | [Player command lifecycle](flow-03-player-command-lifecycle.md) | Player sends a line of input | Phase 2 (replaced by slice 3 command framework; output leg updated in slice 4; prefix resolution added in slice 3a) |
| 4 | [Persistence flush cycle](flow-04-persistence-flush-cycle.md) | `PersistenceFlushTimer` ticks, or shutdown | Phase 3 slice 1 |
| 5 | [Content reload](flow-05-content-reload.md) | Privileged session sends `reload` | Phase 3 slice 2 (gate moved to dispatcher in slice 3) |
| 6 | [Output rendering](flow-06-output-rendering.md) | A command/system writes a typed `IOutputMessage` | Phase 3 slice 4 |
| 7 | [Login / character flow](flow-07-login-character-flow.md) | TCP client connects, new or returning player | Phase 3 slice 5 |
| 8 | [Admin room creation (`dig`)](flow-08-admin-room-creation.md) | Privileged session sends `dig <direction> [name]` | Phase 3 slice 5a |
| 9 | [Items journey (pickup · drop · inventory)](flow-09-item-pickup.md) | Player sends `get`/`drop`/`inventory` | Phase 3 slice 6; updated persistence reform Stage C; source: [../../features/items/items.md](../../features/items/items.md) |
| 12 | [Admin item creation (`mkitem`)](flow-12-admin-item-creation.md) | Privileged session sends `mkitem [name]` | Phase 3 slice 6 |
| 13 | [Equipment journey (wear · remove)](flow-13-wear-item.md) | Player sends `wear`/`remove` | Phase 3 slice 7; source: [../../features/items/items.md](../../features/items/items.md) |
| 15 | [Admin mob creation (`mkmob`)](flow-15-admin-mob-creation.md) | Privileged session sends `mkmob [name]` | Phase 3 slice 8 |
| 16 | [Heartbeat tick](flow-16-heartbeat-tick.md) | `PeriodicTimer` fires in `HeartbeatBackgroundService` | Phase 3 slice 9-b |
| 17 | [Combat journey (initiation · round pulse · flee)](flow-17-kill-mob-combat-initiation.md) | Player sends `kill <mob>`; heartbeat drives rounds; `flee` exits | Phase 3 slice 9; source: [../../features/combat/combat.md](../../features/combat/combat.md) |
| 20 | [Death & respawn journey (mob death · incapacitation · bleed-out · player death/respawn)](flow-20-mob-death-respawn.md) | Mob or player HP reaches zero | Phase 3 slices 9, 10; source: [../../features/combat/combat.md](../../features/combat/combat.md) |
| 21 | [Effects journey (apply · tick · expire)](flow-21-effect-tick.md) | An effect is applied, then ticked/expired on `HeartbeatTickEvent` | [effects](../../features/effects/effects.md) feature |
| 24 | [Ability activation](flow-24-ability-activation.md) | Admin sends `useability` (11-a); player sends `cast`/skill verb (11-b) | Phase 3 slice 11-a (extended 11-b) |
| 25 | [Skill bare-verb invocation](flow-25-skill-verb-invocation.md) | Player types a skill id/prefix (e.g. `kick`, `ki`) | Phase 3 slice 11-b |
| 26 | [Offensive ability opens combat](flow-26-offensive-ability-opens-combat.md) | Player casts offensive spell or uses skill against a new target | Phase 3 slice 11-b |
| 27 | [Admin area creation](flow-27-admin-area-creation.md) | `mkarea [name]` creates an area entity and writes content/areas/<id>.yaml | [flow-27-admin-area-creation.md](flow-27-admin-area-creation.md) |
| 28 | [Admin entity list](flow-28-admin-entity-list.md) | `list <area\|room>` prints a tabular view of all entities of a given type | [flow-28-admin-entity-list.md](flow-28-admin-entity-list.md) |
| 29 | [Headless bulk content generation](flow-29-bulk-content-generation.md) | `dotnet run --project Server -- generate --profile <path> [--seed N]` | [flow-29-bulk-content-generation.md](flow-29-bulk-content-generation.md) |
| 30 | [Offline content edit → save → apply](flow-30-offline-content-edit.md) | Designer edits/creates an area or room in the loopback `Hedron.Web` Blazor editor | content-authoring-editor (WP-2) |

Flows that don't yet exist (mob wander tick, etc.) get added by the slice that introduces them.

---

## Adding a new flow

When a slice introduces a recurring runtime call chain (combat round, player death, item pickup, mob wander tick, save-on-mutation pulse, etc.), create a new file `flow-NN-<slug>.md` in this directory following the format of the existing files:

1. **Summary** (1–3 sentences)
2. **Trigger**
3. **Mermaid sequence diagram** — keep participants to ≤ 7 boxes; if the flow is too wide for that, you're describing two flows
4. **Steps** — numbered prose with file references
5. **Cross-references** — links to the relevant systems, handlers, and use cases

Then add a row to the index table above.

The implementation-planner agent surfaces flow additions as part of its workflow; the architecture-reviewer agent verifies the doc matches the diff. Drift between code and this file is a merge gate.
