# Features

Holistic, **player-facing** documentation — one folder per capability. A feature doc takes the outcomes-and-experience view and stays at the **orchestration level**: it names the systems it composes and defers their internals to the per-system design docs that live beside it. This layer sits *above* the `Core/Modules/` tree; a feature may span several modules.

> **Where each thing lives in a feature folder:**
> - `<feature>.md` — the holistic feature doc (what it is, how it orchestrates its systems, the surfaces it exposes). Template + workflow: the [`manage-docs`](../../.claude/skills/manage-docs/SKILL.md) skill.
> - `<system>.md` — per-system design doc(s); the heart of the logic. Low-code, links to the `.cs` interface rather than dumping signatures. Template via the same skill.
>
> Feature docs **link down** to system docs, **out** to [`../reference/`](../reference/) catalog rows and [`../architecture/flows/`](../architecture/flows/) journeys, and **up** to [`../architecture/`](../architecture/) foundational docs. They never restate an invariant (cite the `INV` by id) or reproduce a flow diagram — see [`09-documentation.md`](../architecture/09-documentation.md).

## Canonical feature list

| Feature | Modules it spans (`Core/Modules/…` unless noted) | System docs |
|---|---|---|
| [`combat/`](combat/) | Combat, Death | combat-system, death-system, entity-state |
| [`effects/`](effects/) | Effects | effect-system |
| [`abilities/`](abilities/) | Abilities | ability-system |
| [`aspects/`](aspects/) | Aspects | aspect-system |
| [`character-stats/`](character-stats/) | Stats, Attributes, Regeneration | stat-system, attribute-system, regeneration-system |
| [`items/`](items/) | Items (incl. inventory + equipment) | item-inventory-system, equipment-system |
| [`world/`](world/) | World, Movement, Spawn, Time | world-content, movement-system, spawn-system, area-model, time-system |
| [`mobs/`](mobs/) | Mobs | mob-system |
| [`accounts/`](accounts/) | Account, Session, `Core/Sessions/` | account-system, login-flow |
| [`admin-authoring/`](admin-authoring/) | Admin, Authoring | admin-commands, content-authoring, content-tooling |
| [`communication/`](communication/) | Chat, Help | chat-system, help-system |
| [`output/`](output/) | Prompt, `Core/Output/` | output-framework, prompt |
| [`commands/`](commands/) | `Core/Commands/` | command-framework |

**Infrastructure that stays foundational (no feature folder):** Persistence → [`../architecture/06-persistence.md`](../architecture/06-persistence.md) + [`../reference/systems.md`](../reference/systems.md). The ECS substrate, event bus, and registry/serializer infrastructure under `Core/Systems/` are catalogued in [`../reference/`](../reference/) and explained in [`../architecture/`](../architecture/) `00`–`08`.

## Growing the taxonomy

A new feature folder is added when a new `Core/Modules/` capability is built. The menu of *future* capabilities is [`../design/feature-horizon.md`](../design/feature-horizon.md) — its themes (§1 World, §5 Combat depth, §6 NPCs, §8 Economy, §9 Crafting, §11 Guilds, §12 Quests, §13 Housing, …) map cleanly onto this taxonomy: each theme is either an existing folder, a cross-cutting spine (Aspect/Ability/Effect → `aspects`/`abilities`/`effects`), or an unbuilt capability that will become its own folder (economy, crafting, guilds, quests, housing). The horizon is the *future-feature* menu; this README is the *built-feature* index.

Two current groupings are deliberate **interim homes** that may graduate to their own folder as they deepen: **Movement** (under `world/` today — splits out when mounts / following / encumbrance land, horizon §2) and **Progression** (the Spine E experience-track machinery — under `character-stats/` today, gets its own `progression/` folder when built, horizon §4).

> This list is the single source of truth for the feature taxonomy. The canonical navigation doc-map remains the "Related Documents" table in [`../architecture/00-overview.md`](../architecture/00-overview.md); this README is the feature-layer index, not a parallel map.
