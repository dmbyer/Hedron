# Tiered overworld, generated wilderness & area instances (program)

- **Status:** planned
- **Actors:** Player, Administrator (designer), System
- **Module:** `Core/Modules/World` for the fabric/map surfaces; instancing and generation likely warrant new module homes — the advisor places them. Feature home: [`docs/features/world/`](../features/world/world.md).

**Description.** The world gains a single continuous overworld: curated areas occupy footprints on a shared coordinate fabric, pre-generated (baked-to-YAML) wilderness fills the space between them, and difficulty rises with distance from the starting region using the existing Tier×Band vocabulary. Scattered through both curated and generated space are entrances to **area instances** — party-scoped, ephemeral local scopes stamped from curated dungeon templates or generated on the fly — in the Diablo mold: seamless walking in the shared world, a portal-style crossing into private local space, and a map view whose context follows you. This is an umbrella **program** (the Phase 6 "generated content alongside curated" pillar of [`roadmap/plan.md`](../roadmap/plan.md)); its stages are expected to decompose into per-stage implementation plans at advisor framing.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake. The advisor extends this file with the architecture brief; the planner absorbs this section into Preconditions/Postconditions and Main flow. Cite items as R-numbers.)*

### User stories

- *As a player*, I want to walk out of the curated starting region into open wilderness — with no loading boundary, mode switch, or visible seam — so the world feels like one continuous place.
- *As a player*, I want danger to grow with distance from home in a legible way (banners, map, entrance descriptions telegraphing tier), so I can choose risk deliberately.
- *As a player*, I want to find dungeon entrances in the world, enter one with my party, and get our own private copy — curated or randomly generated — so group content is neither camped nor contested.
- *As a player*, I want a `map` that shows my surroundings room-by-room, zooms out to region scale, and switches to the local layout when I'm inside an instance, so I always know where I am at both scales.
- *As a designer*, I want to define a region's bounds, tier, and theme, press "generate," and get ordinary, inspectable, hand-editable YAML content — indistinguishable from hand-authored rooms to the runtime and the balance audit — so finite authoring yields large playable space I can still curate.
- *As a designer*, I want mob and loot pools tagged by tier, band, and theme (biome + aspect), so a Tier-1 forest spawns woodland creatures and a Tier-3 dream desert spawns something else, without hand-placing every spawn.
- *As the system*, I want instances created on entry, tracked per party, and torn down when abandoned, so private space never leaks or accumulates.

### Scope

**In scope (staged):**
1. **Stage A — Overworld fabric & region tiers**: global room-coordinate fabric, curated-area footprints, region (Tier, Band) + theme designations, boundary banners, authoring validation.
2. **Stage B — Pre-generated wilderness**: the offline generator baking area/room/spawn YAML from region parameters; themed mob/loot pools; entrance placement.
3. **Stage C — Curated instances**: the instancing substrate (create/join/teardown, party scope, entrance UX, death policy) proven on stamped copies of authored dungeon templates.
4. **Stage D — Generated instances**: on-the-fly seeded layouts through the same substrate, reusing the Stage-B generator's parameter model.
5. **Stage E — Map surface**: ASCII `map` with local, region, and instance-local contexts. (May land earlier; its substrate is Stage A.)
- **Phase-5 pre-adoption (immediate):** the starting region is authored on the coordinate fabric with region tier designations (R22) — nothing else pulls forward.

**Out of scope (confirmed exclusions):**
- Runtime/lazy *overworld* generation and boot-seeded generation — overworld space is always baked to files (instances are the only on-the-fly generation).
- A second movement scale (coarse overworld tiles, travel ticks, encounter rolls) — walking rooms is the only ground truth; the "upper tier" is presentation/travel QoL.
- Vehicles/mounts, weather, day/night, resource nodes — separate horizon items, not required by this program.
- Fully synthesized mobs (generated stats/abilities) — pools of existing audited templates only; variant/affix rolls belong to the Phase-6 rarity/affix family.
- Instance persistence across restarts; player housing instances; PvP rules.
- Exploration memory / fog-of-war on the map (parked — see Open questions).

### Behavioral requirements

**A. Overworld fabric & difficulty tiers**
- **R1.** Every overworld room occupies a position on a shared world coordinate space; curated areas occupy non-overlapping footprints within it. Moving between a curated area and adjacent wilderness is ordinary directional movement — no mode switch, no special command, no visible boundary beyond the banner (R2).
- **R2.** Every area carries a **region designation**: a (Tier, Band) range and a theme (biome + aspect affinity). Crossing an area boundary shows a "You have entered ⟨area⟩" banner line.
- **R3.** Tier placement forms a gradient: authoring-time validation **warns** (never errors, matching the established warn-not-error posture) when two adjacent areas differ by more than one tier, and when curated footprints overlap on the world fabric.

**B. Pre-generated wilderness**
- **R4.** A generator, invoked from the offline editor/CLI, fills a designated region with ordinary area/room/spawn YAML — same schema as hand-authored content, indistinguishable to the runtime, loader, validator, band-drift audit, and grid editor — which a designer can inspect and hand-edit afterward.
- **R5.** Generation is parameterized per region: bounds on the world fabric, (Tier, Band), theme tags, room density/connectivity, mob-pool and loot-pool references, entrance frequency.
- **R6.** Spawn rules in generated space draw from **weighted mob pools** tagged by (Tier, Band) and theme; every mob spawned is an existing authored template already priced by the balance audit. Pools are themselves authorable, inspectable content.
- **R7.** Loot in generated space draws from loot pools tagged the same way (sequenced with the Phase-6 weighted-loot-table work — see Open questions).
- **R8.** Re-generating a region is a deliberate authoring act: it must not disturb curated footprints, and collisions are surfaced as validation warnings before content is written.
- **R9.** The generator can place instance entrances (R10) in generated space per the region's parameters.

**C. Area instances**
- **R10.** An entrance is a **visible room feature** ("A crumbling stairway descends into the Howling Crypt."); `look` at it telegraphs its Tier/Band and theme; `enter <entrance>` crosses into the instance. Entrances appear in both curated and generated space.
- **R11.** Instances are **party-scoped**: entering creates a live instance for the entrant's party (solo = party of one) or joins the party's existing live instance for that entrance; different parties at the same entrance always get separate copies.
- **R12.** An instance's content is either a **stamped copy of a curated dungeon template** (Stage C) or a **layout generated at entry** from a seed plus the same parameter model as R5 (Stage D).
- **R13.** Instances are **ephemeral**: torn down after being empty for a configurable grace period; never persisted. After a server restart, any character whose location was inside an instance is placed at the entrance room.
- **R14.** The instance's entry room offers a return path (`leave` / a return portal) that places the player back at the entrance room in the shared world.
- **R15.** Each instance template declares its **death policy**: `normal` (standard death/respawn rules; the instance keeps living and members may re-enter while it lives — the default) or an eject/fail policy (death or party wipe collapses the run). The policy set is declared per template, not hardcoded per kind.
- **R16.** Instance population draws from the same pool machinery as R6; all instance mobs/loot land in the Tier×Band cells the audit prices.
- **R17.** Instance occupants remain part of the one game world for global surfaces (chat, who, admin visibility); only their *space* is scoped.

**D. Map surface**
- **R18.** A `map` command renders an ASCII map from the coordinate fabric: a **local view** of rooms around the player, and a **zoomed region view** of areas and their tier designations.
- **R19.** Map context follows scope automatically: inside an instance, `map` shows only the instance's local space; back in the shared world, the overworld views return.

**E. Phase-5 pre-adoption**
- **R22.** The Phase-5 curated starting region is authored on the coordinate fabric (grid editor) with Tier 0–1 region designations. No generation, instances, or map work is required for Phase 5.

### Edge cases & failure behavior

- Entering an entrance while in combat is refused with standard "you are fighting" messaging *(adopted default)*.
- `enter` with no matching entrance in the room, or an ambiguous name, fails with the command framework's normal target-resolution messaging.
- A party member who was offline when the instance despawned logs in at the entrance room (same rule as restart, R13).
- Two parties at one entrance: strict copy isolation (R11) — no cross-instance visibility of players, mobs, or loot.
- Generator failure or invalid region parameters: no partial content is written; errors surface in the editor/CLI *(adopted default: all-or-nothing per region bake)*.
- Coordinate collisions (curated-vs-generated or curated-vs-curated) and tier-adjacency jumps: warn-not-error at validation (R3, R8), consistent with the registry-validation warnings channel.

### Content & authoring needs

- Region definitions (bounds, tier/band, theme, density, pool refs, entrance frequency) are authorable and inspectable in the offline editor.
- Mob pools and loot pools are first-class authorable content with tier/band/theme tags and weights, inspectable and priceable by the existing audit.
- Generator invocation with preview-before-write from the editor/CLI; generated output opens in the existing area/grid editors.
- A **multi-area world view** in the grid editor (the deferral this program un-defers) so designers see footprints and gradients across the fabric.
- Entrance authoring on rooms (description, target template or generation parameters, death policy) via editor and admin verbs.
- An area/dungeon template can be flagged instantiable with its instance metadata.

### Grounding notes

- **Exists (live):** YAML world content + template registry + spawn slots ([`world.md`](../features/world/world.md)); area membership + aspect affinities ([`area-model.md`](../features/world/area-model.md)); Tier 0–6 × Band 1–3 authoring, power oracle, band-drift audit, conformance fitter ([`balance.md`](../design/balance.md)); authoring-side room `X/Y/Z` + grid editor ([`world-editor-grid` record](../roadmap/completed/world-editor-grid.md) — its "per-area origin offset (overworld design)" note anticipates exactly this program); warn-not-error validation warnings channel (same record).
- **Planned/backlogged:** Instancing, Procedural/generated areas, Overworld travel, auto-map, area-property enforcement — all catalogued in [`feature-horizon.md`](../design/feature-horizon.md) §1; runtime coordinate system + multi-area world view deferred in [`backlog.md`](../roadmap/backlog.md); this program **is** the Phase 6 "generated content alongside curated" bullet of [`roadmap/plan.md`](../roadmap/plan.md), including its named INV-12 scoped-sub-world design decision.
- **Collisions surfaced:** current focus is Phase 5 (curated starting region) — this program stays Phase 6 except the deliberate R22 pre-adoption; the backlog's world-state threading decision (guard vs. marshal) explicitly lists "instanced-content workers" as a trigger — it must be resolved no later than Stage C framing.

### Resolved decisions

*(Confirmed by the user during intake — do not relitigate downstream.)*

1. "Tiered" means **both**: a difficulty gradient in the existing Tier×Band vocabulary **and** a scale-layered presentation — but the upper layer is map view + travel QoL over one room fabric, never a second world model (Diablo framing: seamless shared world, portal-crossings into local scopes, map context follows).
2. The overworld is **one continuous room grid**; curated areas sit in footprints with generated wilderness between (rejected: coarse tile layer as world model; rejected: stitched areas without coordinates).
3. Non-instance generation is **baked to YAML offline** through the existing content pipeline (rejected: boot-seeded; rejected: lazy runtime chunks).
4. This document is an **umbrella program** with staged pillars; stages decompose at advisor framing.
5. Instances are **party-scoped and ephemeral** (rejected: player-scoped; rejected: shared-until-empty).
6. Mob variety = **weighted pools per (Tier, Band) cell restricted by theme tags** (biome + aspect); pools also drive loot. Variant/affix rolls deferred to the rarity/affix family.
7. Phase 5 pre-adopts **grid coordinates + region tier designations only**.
8. Entrance UX **as R10** (visible feature, `enter`, tier telegraph, return portal) — rejected: plain-exit entrances.
9. Death policy is **configurable per instance template**, defaulting to normal-death (chosen over a single global rule).
10. **Curated instances land before generated instances** (substrate proven before runtime generation).
11. The **map surface is in scope** as its own stage, with the three contexts of R18–R19.

**Adopted defaults** *(not individually reviewed — veto cheaply at advisor framing):* combat blocks `enter`; restart/despawn relocation to entrance room; all-or-nothing region bakes; instance occupants visible to global chat/who; v1 map renders actual surroundings without exploration memory.

## Open questions

- **Party primitive.** Instances are party-scoped, but no grouping/following mechanism exists yet (horizon §2/§11). Does Stage C carry a minimal party primitive, or must a grouping slice precede it? *(User + advisor at framing — load-bearing for Stage C's size.)*
- **World-state threading.** Whether instance creation/teardown introduces a new concurrent writer, and how it lands against the backlog's guard-vs-marshal decision. *(Advisor — code-level; the backlog says decide before any such feature.)*
- **Coordinate mechanics.** Global coordinates stored per room vs. per-area origin offsets composed at load (the grid-editor record anticipates offsets); how instance-local coordinate spaces relate. *(Advisor.)*
- **Generator internals.** Algorithm, determinism/seeding contract, and how much of the offline generator is reusable for Stage D's at-entry generation. *(Advisor/planner.)*
- **Loot-pool sequencing.** R7 depends on the general weighted-loot-table (`ILootSystem`) work also slated for Phase 6 — which program owns it? *(Advisor at framing.)*
- **Exploration memory.** Whether the map ever gains fog-of-war/discovered-rooms memory (a persistence question). *(Parked for the user, post-MVP.)*
