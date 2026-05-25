# Persistence

Hedron uses a two-level persistence model. The questions "should this entity survive a restart?" and "which of its components are worth saving?" have separate, independent answers.

---

## Level 1 — Does this entity participate in persistence?

Add the `PersistentEntity` marker component to any entity that must survive a server restart. `PersistentEntity` is a zero-data component tagged `[Persistent]`, so it round-trips through the snapshot and is restored on hydration — the entity knows it persists without any extra bookkeeping.

Entities **without** `PersistentEntity` are never written to disk. This applies to: template-spawned mobs that re-spawn from templates on restart, randomly generated dungeon content, session-only transient effects, etc. Same component types, no duplication required — the marker is the only difference.

```csharp
// authored room — survives restart
var room = _entityService.CreateEntity();
_entityService.AddComponent(room.Id, new RoomComponent { ... });
_entityService.AddComponent(room.Id, new PersistentEntity());   // opts in

// generated dungeon room — session-only, same component types
var dungeonRoom = _entityService.CreateEntity();
_entityService.AddComponent(dungeonRoom.Id, new RoomComponent { ... });
// no PersistentEntity — not saved
```

---

## Level 2 — Which components are included in the snapshot?

`[Persistent]` on a component *type* tells `PersistenceSystem` to include that component when serializing an entity that **already** has `PersistentEntity`. It does not cause any entity to be saved on its own.

The attribute is still meaningful: even for entities that do carry `PersistentEntity`, some components must be excluded. `PlayerComponent` holds a transient session reference. `TransientEffectsComponent` is session-only by design. Those stay untagged; `PersistenceSystem` skips them.

```
Entity has PersistentEntity?
  No  → never written, full stop.
  Yes → write all components tagged [Persistent] on that entity.
```

---

## The three save patterns

### Save-on-change

Use for: authored content created or mutated by admin commands (rooms, exits, item templates), and entity lifecycle transitions (item dropped, item picked up, item sold).

The handler calls `SaveEntityAsync` directly. No flush cycle dependency — the entity is durable as soon as the operation completes.

```csharp
// in a handler responding to RoomCreatedByAdminEvent
await _persistence.SaveEntityAsync(e.NewRoomEntityId, ct);
await _persistence.SaveEntityAsync(e.SourceRoomEntityId, ct);
```

Use save-on-change when: (a) the change is infrequent and deliberate, (b) crash-between-change-and-flush is unacceptable, or (c) the entity is authored content that should be durable immediately.

### Area-scoped periodic flush

Use for: runtime state that changes gradually — player character stats, positions, inventory, active mob state. The `PersistenceFlushTimer` fires on the configured interval and saves all `PersistentEntity`-carrying entities in active player areas (the player entity plus entities in the rooms those players occupy).

This bounds the save set to the active player footprint rather than the full world, which keeps flush cost proportional to concurrent sessions rather than total world size.

### Timestamp + lazy recalculation

Use for: offline processes — item decay, crop growth, shop restocking, anything that evolves without requiring a player or system to be present.

Save a `*_at` timestamp on the relevant persistent component when the process starts. On next query (player enters area, system ticks with a player nearby), compute the current state from the elapsed time. No periodic re-save needed between ticks; the timestamp is the durable state.

```csharp
// DroppedItemComponent (Persistent)
public DateTime DroppedAt { get; set; }
public TimeSpan DecayDuration { get; set; }

// on hydration or area-entry, skip expired items
if (DateTime.UtcNow - item.DroppedAt > item.DecayDuration)
    _entityService.DestroyEntity(entityId);
```

---

## Choosing a pattern

| Entity class | Pattern | Why |
|---|---|---|
| Authored rooms, exits, item templates | Save-on-change | Admin operations are rare; immediate durability desirable |
| Player characters | Area-scoped flush | Active sessions are bounded; frequent mutations |
| Dropped items (planned) | Save-on-change at drop when an explicit persistence flag is set; delete on pick-up or expiry | One write per transition; decay is timestamp-lazy. Not yet implemented — dropped items currently vanish on restart by design (see `items-and-inventory.md` Design Notes). |
| Session-only spawned mobs | No `PersistentEntity` — not saved | Re-spawned from templates on restart |
| Generated dungeon content | No `PersistentEntity` — not saved | Same component types as authored content, no duplication |
| Time-based world processes | Timestamp + lazy recalculation | No re-save needed between ticks |

---

## Adding a new persistent entity class

Before writing any code, answer two questions explicitly:

1. **Should instances of this entity class persist?** → If yes, the construction path adds `PersistentEntity`. If some instances persist and others don't (authored vs generated room), the construction path diverges at that decision point — not at the component type level.

2. **For each component on this entity: should it be included in the snapshot?** → If yes, tag the component class `[Persistent]`. If no (transient ref, session-only state, derived/recomputed on load), leave it untagged.

Do not use `[Persistent]` to control whether an entity persists. That is `PersistentEntity`'s job. An entity without `PersistentEntity` is never saved regardless of how many `[Persistent]` components it carries.

---

## Serializers

Two serializers, two audiences — they do not share code:

- **`System.Text.Json`** — component snapshots. Machine round-trip. Used by `PersistenceSystem`.
- **`YamlDotNet`** — designer-authored content files under `data/content/`. Human-readable. Used by `WorldContentLoader`.
