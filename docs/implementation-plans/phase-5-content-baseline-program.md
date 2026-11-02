# Phase 5 — Content baseline → functional MVP (program)

- **Status:** planned (requirements + architecture brief — awaiting `/new-plan` per slice)
- **Actors:** Player, Administrator (designer authoring the baseline), System (spawns, respawn, acceptance bots)
- **Module:** program-level umbrella, decomposed into the slices in the [Architecture brief](#slice-decomposition). Code lands in existing modules:
  - `Core/Modules/World` (world manifest, area designation);
  - `Core/Modules/Spawn` + `World` builders (spawn rules);
  - `Core/Modules/Help` (topic registry);
  - `Core/Modules/Progression` (relative-power seam, `consider`);
  - `Core/Modules/BalanceInspection` (audit check, `power` migration);
  - `Hedron.Web` (editors).

  Content lands in `content/dev-world/` (the world) and a new committed engine help layer. Feature homes: [`world/`](../features/world/world.md), [`communication/`](../features/communication/communication.md), [`progression/`](../features/progression/progression.md), [`admin-authoring/`](../features/admin-authoring/admin-authoring.md).

**Description.** The Phase 5 program from [`roadmap/plan.md`](../roadmap/plan.md#current-focus-phase-5--content-baseline--functional-mvp). It turns the engine into something a stranger can play end to end on committed content:
- create a character;
- learn the verbs along an authored onboarding path;
- hunt Tier-0 mobs whose danger rises across Bands 1→3;
- earn XP and coin, buy and sell, and die and recover.

The engine's built mechanics get exercised by authored content, and the functional-MVP acceptance session proves it, executed by bots. The **generic `content/dev-world/` fixture is the primary content** for this program and beyond. The operator's private world is outside Phase 5's definition of done. This file owns the MVP definition: scope, region targets, and acceptance criteria. The companion [playtest-automation program](playtest-automation-program.md) owns *how* the acceptance session is executed and graded, and gets its own `/refine` → `/advise` immediately after this one.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-10-28. The advisor extends this file with the architecture brief and slice decomposition. The planner(s) absorb the relevant R-numbers into each slice's Preconditions/Postconditions and Main flow. Cite items as R-numbers.)*

### User stories

- *As a new player*, I want to connect, create a character, and land somewhere that teaches me what to type, so I can start playing without an admin's help.
- *As a new player*, I want to read my danger before I pick a fight (`consider`, descriptions), so that dying is a choice I made rather than a surprise.
- *As a player*, I want a hunting loop: kill mobs, earn XP and coin, find sellable items, and buy a better weapon. That way progress is visible and the next band of mobs becomes beatable.
- *As a player*, I want to die to something too strong, wake up safe in town, recover, and go back out, so that death teaches without ending my session.
- *As a designer/administrator*, I want to author the baseline region with the existing tooling (grid editor, `MobEditor`, `generate`, builder verbs), and keep every mob and item in its (Tier, Band) cell under the band-drift audit. That way the content is balanced by construction, and authoring at volume tells me where the tooling falls short.
- *As a designer*, I want to state the expected outcomes (time to first kill, kills per improvement, deaths per hour by band, coin to first upgrade) in the standards document, so the acceptance session is graded against numbers I chose.
- *As the system*, I want a seeded bot session to walk the full acceptance loop on the dev world and report pass/fail per criterion, so the MVP is proven repeatably, in CI, not by a long manual playtest.

### Scope

**In scope:**
- **Content-policy amendment** for `content/dev-world/`: generic and lore-free, but at full volume and the primary playable content (R1–R2).
- **The baseline region in dev-world:** a safe hub plus three hunting areas, one per Tier-0 band, on the grid fabric (R3–R6).
- **Banded mobs, currency loot, spawn slots, shop gear, and found-item spawns** for the sell loop (R7–R14).
- **Onboarding:** an authored first-rooms path and a small authored `help <topic>` article layer (R17–R19).
- **`consider <mob>` danger gauge**, pulled from the backlog as a gap slice (R20–R22).
- **Balance closure on Tier-0 content:** sweeps, audit-clean content, and the first real standards expectations (R24–R25).
- **The functional-MVP acceptance definition:** bot-checkable, seeded, run on dev-world, with thresholds in the standards document (R26–R29).
- **Content-driven gap slices** through the per-slice loop, and the client-tier gate fed by real authoring evidence (R30–R31).

**Out of scope (confirmed exclusions):**
- **Tier 1+ content, a player-facing ascension trigger, and ascension tier tuning.** These move to the Phase 6 progression-expansion pillar. Tier 1 is reachable today only through admin `ascend`. The tier baseline now reaches combat (slice 5b), but its step is untuned ([backlog](../roadmap/backlog.md)).
- **The operator's private world** as part of the definition of done.
- **Mob AI:** aggro, wandering, threat. Danger is opt-in.
- **Mob item drops, corpses, and general loot tables.** These remain the Phase 6 `ILootSystem` work; currency loot only.
- **Death penalties.** Soft death stays as shipped.
- **New abilities and the ability YAML pipeline.** The existing starter set stays.
- **Scripted tutorial triggers and a tutorial zone** (horizon entry). Onboarding is authored content plus help topics.
- **Grouping/parties.**
- **Area-entry banners and the rest of tiered-overworld Stage A.** Only the R22 pre-adoption applies.
- **Already punted in [plan.md](../roadmap/plan.md):** crafting/potions, the web/SignalR client, and channels/socials beyond `say`.
- **The playtest harness's design:** substrate, bot policies, LLM personas, CI mechanics, and the "no autonomous sweeps" ruling. These belong to the [playtest program](playtest-automation-program.md).

### Behavioral requirements

**A. Content policy**
- **R1.** `content/dev-world/` remains **generic**: no lore, setting, or narrative, and functional names (`Training Yard`, `General Store`, `Band-2 Wolf`). Its **minimum-volume** and **"never the basis for a game"** clauses are lifted. It grows into, and is maintained as, the full playable baseline, and is expanded as development proceeds. The policy text in [`world-content.md`](../features/world/world-content.md#content-ownership--the-engine-ships-no-game-world), [CLAUDE.md](../../CLAUDE.md), and the [plan.md](../roadmap/plan.md) Phase 5 paragraph is amended in the program's first slice.
- **R2.** Operator-world content rules are unchanged: never committed, never un-ignored. The operator's world is simply not part of Phase 5's definition of done.

**B. Region**
- **R3.** A **hub area** that is **safe by construction** contains the **starting room**, where new characters land; the **default respawn point**; the shop(s) (R11); and the onboarding path (R17). "Safe by construction" means: no hostile spawns, `pvp: false`, no mob initiates combat (R10), and hub shopkeepers carry the existing `untargetable` mob protection. There is no safe-room flag. The engine has none; the earlier `isSafe` wording came from a doc error, which slice 1 corrects.
- **R4.** **Three hunting areas**, one per Tier-0 band (B1, B2, B3), are laid out as a **distance gradient** from the hub: B1 adjacent to the hub, B3 farthest. Each area carries its (Tier 0, Band n) region designation, consistent with [tiered-overworld R22](tiered-overworld-program.md).
- **R5.** The region totals **about 40–50 rooms**. Every room carries grid coordinates and is authored in the grid editor (tiered-overworld R22).
- **R6.** **Full connectivity:** every room is reachable from the starting room, and every room has a path back to the hub. There are no one-way traps and no dead-end rooms without a return.

**C. Mobs**
- **R7.** There are at least **3 mob templates per (Tier 0, Band n) cell** (9 or more in total). Each is tagged with its Tier/Band, and the **band-drift audit reports all of them in-cell**.
- **R8.** Every hostile mob carries a **currency-loot range scaled by band** (B3 > B2 > B1 per kill), auto-awarded on kill (the existing `CurrencyLootComponent` path).
- **R9.** Hunting-area mobs respawn through **spawn slots**. Delays are set so one player hunting an area does not leave it permanently empty during a session; the exact delays are designer tuning.
- **R10.** No mob initiates combat. Every fight is opened by the player.

**D. Items & economy**
- **R11.** A hub shop sells **at least one weapon or armor upgrade per band**. Each is priced so that the B-n upgrade is affordable from B-n hunting within the standards-document threshold (R25).
- **R12.** Hunting areas carry **room spawn slots for sellable "found" items**: mostly junk, occasionally modest gear, all with `Value > 0`. They respawn on timers and are the **source of the sell loop**. No mob item drops.
- **R13.** New characters start with **zero coin**. The first purchase is funded by loot and sales.
- **R14.** Every authored item is tagged with its Tier/Band where it carries power, and is in-cell under the audit.

**E. Character & death**
- **R15.** At creation a character's location and respawn point are the hub's starting room. The character receives the existing starter abilities (`kick`, `empower`, via `CharacterDefaults:StartingAbilities`). No new abilities.
- **R16.** **Soft death is unchanged:** the character is relocated to the hub respawn point with pools restored to the configured 25%, and loses no XP or items. Recovery is through existing regeneration in the safe hub.

**F. Onboarding**
- **R17.** An **onboarding path** is a short sequence of hub rooms starting at the starting room. Their descriptions name the core verbs in context: looking and movement, `help`, `score`/`progress`, `inventory`/`wear`, `consider`/`kill`, `list`/`buy`/`sell`, and what happens on death. They point the player toward the B1 area.
- **R18.** `help <topic>` returns an **authored topic article** for at least these topics: *getting started*, *combat*, *death & respawn*, *shops & money*, *progression*. The no-argument `help` index lists the available topics alongside the verbs.
- **R19.** Topic articles are **authorable, inspectable content**, not compiled strings (INV-18). Their text is engine-generic (mechanics, no lore), so they are safe to commit.

**G. `consider`**
- **R20.** `consider <mob>` (player) resolves a mob in the room and prints a **coarse diegetic danger label** for the target relative to the player (e.g. *trivial / easy / even / dangerous / deadly*). The label is derived from the existing power-budget oracle. It **never shows raw numbers**, consistent with the backlog entry's design and prog-3's admin-gating of raw power.
- **R21.** Labels track the gradient: for a fresh character, B1 mobs read at or below *even*, and B3 mobs read *dangerous* or worse.
- **R22.** `consider` is read-only and usable whether or not the player is in combat.

**H. Balance closure**
- **R23.** All authored dev-world mobs and items are **clean under the band-drift audit**.
- **R24.** The baseline **sim sweeps run against the dev-world mob set**, and their reports are reviewed as part of closing each band's content drop.
- **R25.** The program authors the **first real outcome expectations into the standards document** ([balance.md](../design/balance.md) rule 4). These include the acceptance thresholds: time to first kill, kills per track improvement, coin to first upgrade, deaths per hour by band, and win rate of a fresh character by band. All values are designer-stated; until a value is stated, the harness reports that metric descriptively.

**I. Functional-MVP acceptance**
- **R26.** The MVP is accepted by a **seeded, bot-executed session on dev-world**. Starting from character creation, a fresh character must:
  1. **A1** — land in the hub starting room and traverse the onboarding path using the core verbs, with no failed commands on that path;
  2. **A2** — kill a B1 mob and receive XP and coin;
  3. **A3** — reach at least one track improvement;
  4. **A4** — buy a shop upgrade with earned coin and wear or wield it;
  5. **A5** — sell a found item;
  6. **A6** — die to a B3 mob, respawn in the hub, recover, and resume hunting;
  7. **A7** — show measurably rising danger B1 → B2 → B3 (deaths and/or loss rate, and the `consider` labels per R21);
  8. **A8** — encounter no stuck state: no unreachable rooms (R6) and no no-progress loop.
- **R27.** Each criterion is **graded against the standards-document thresholds** (R25), not hard-coded in tests.
- **R28.** A small seeded acceptance golden runs in CI on dev-world. Larger sweeps run on demand and per content drop, and stay advisory (the playtest program's posture).
- **R29.** R26–R28 are the **contract the playtest program consumes**. That program decides the harness, policies, and grading mechanics; this program fixes *what* is checked.

**J. Program process**
- **R30.** Gaps found while authoring at volume (a missing editor field, an audit blind spot, a needed room flag) each land as a **thin slice through the normal per-slice loop**, not as ad-hoc fixes inside content drops.
- **R31.** The baseline is **authored with the repaired tooling** (editor, grid editor, `generate`, builder verbs), so the [client-tier gate](../design/client-tier.md) gets real authoring-friction evidence. The gate fires at the Phase 5 → 6 boundary once this baseline is authored.

### Edge cases & failure behavior

- **Death far from the hub:** respawn is always at the hub (R15). If the stored respawn room can't be resolved, the existing fallback to the configured starting room applies.
- **Area cleared out:** spawn slots refill on their delays (R9). The acceptance bot must treat a temporarily empty area as waiting, not stuck (A8 distinguishes them).
- **Shop till cannot afford a sale:** the sale is refused with the existing affordability failure. Tills are seeded large, so this is not expected in normal play.
- **Item worth nothing:** found items always have `Value > 0` (R12), so `sell` never hits a worthless-item refusal on the intended loop.
- **`consider` failures:** no argument, or no matching mob in the room, gives the command framework's standard target-resolution messaging. Considering a player or a non-mob is refused with a short message. *(Adopted default.)*
- **`help` name collisions:** if a topic name matches a verb, verb help wins, and the topic stays reachable by its full topic name. Lookup order is verb → topic → ability. *(Adopted default; the advisor may revise.)*
- **Logout mid-hunt / restart:** the character persists at the room it last occupied (existing accounts behavior). World spawns reseed on boot (existing spawn behavior).
- **Fresh character wanders into B3:** this is allowed (no gating). `consider` (R21) and the B3 area's distance and descriptions are the warning.

### Content & authoring needs

- **The whole baseline region is authored with the existing tooling and inspected through it:** areas, rooms with coordinates, spawn slots, mobs with Tier/Band, XP scale and currency loot, shop config, items with Value and Tier/Band. The audit, the conformance fitter and the sim validate it. Any field the tooling can't author becomes an R30 gap slice.
- **Help topic articles** need an authoring and inspection path, in content form and editable without a recompile (R19).
- **Acceptance thresholds** are authored on the existing Standards page / standards document (R25).
- **`consider`** has nothing new to author: it is derived from the oracle. Whether its label cut-points are designer-tunable is an open question below.
- **Running against dev-world** stays documented and explicit (an override of `World:ContentDirectory` plus its own database). See the open question on defaults.

### Grounding notes

- **Exists (live):**
  - accounts and character creation with configurable starter abilities ([accounts](../features/accounts/accounts.md));
  - combat, and soft death/respawn with a stored respawn room ([death-system](../features/combat/death-system.md));
  - spawn slots with respawn delays ([spawn-system](../features/world/spawn-system.md));
  - wallets, currency loot, and shops with buy/sell/buy-back ([economy](../features/economy/economy.md), [shop-system](../features/economy/shop-system.md));
  - use-based progression, the power oracle, and Tier×Band tags ([progression](../features/progression/progression.md));
  - verb-only `help` with ability fallback and no topic articles ([help-system](../features/help/help-system.md));
  - the authoring tooling: editor, grid editor, `generate`, audit, fitter, simulator, standards ([content-tooling](../features/admin-authoring/content-tooling.md), [balance.md](../design/balance.md)).
- **Planned/backlogged and pulled in:** `consider` danger gauge ([backlog](../roadmap/backlog.md), "Player-facing `consider` danger-gauge").
- **Planned/backlogged and explicitly left out:**
  - ascension Objective gate and calibration gap;
  - general mob loot table;
  - ability authoring pipeline;
  - mob AI adapter;
  - tutorial/onboarding zone ([feature-horizon](../design/feature-horizon.md));
  - broadcast/newbie channels.
- **Collisions resolved:**
  - **Content policy vs. "dev-world as primary content":** resolved by amending the policy (R1), keeping it generic.
  - **plan.md's Tier 0–1 range vs. the admin-only ascension trigger:** resolved to Tier 0 only.
  - **plan.md's "fix the ascension-calibration gap" bullet:** moves to Phase 6 with Tier 1, since Tier-0 content never exercises the tier baseline.
  - **[tiered-overworld R22](tiered-overworld-program.md)** says "the Phase-5 curated starting region": it now means this dev-world baseline region.
- **Companion program:** [`playtest-automation-program.md`](playtest-automation-program.md). It consumes R26–R29 and resolves its own decisions (autonomous-sweeps ruling, LLM cost model, findings home, balance-reviewer relationship) at its own `/refine`.

### Resolved decisions

*(Confirmed by the user during intake. Do not relitigate downstream.)*

1. **Structure:** this file is the Phase 5 umbrella that owns the MVP definition. The playtest harness stays a separate program, refined right after, consuming this file's acceptance criteria. Rejected: one combined intake; a content-only intake with loose acceptance.
2. **Test world:** acceptance is tested and validated **against the dev-world fixture only**. Dev-world is the primary content until much later in development, created now and expanded as development proceeds. Rejected: the operator world as the test world; both worlds.
3. **Content policy:** dev-world stays **generic and lore-free, at full volume**. Rejected: neutral-flavor prose; splitting into a separate `content/baseline-world/`.
4. **Tier range:** **Tier 0, Bands 1–3**. Tier 1, a player-facing ascension trigger and the calibration fix go to Phase 6. Rejected: Tier 0–1 with player ascension; Tier 0–1 admin-gated.
5. **Onboarding:** an **authored first-rooms path plus authored help topics**. Rejected: content-only; a scripted tutorial zone.
6. **Sell loop:** **room-spawned found items**. Rejected: pulling in minimal mob drops; sell-back only.
7. **`consider`:** **in Phase 5 as a gap slice**. Rejected: descriptions-only telegraphing.
8. **Volume and acceptance defaults approved as proposed:**
   - hub plus three band areas, about 40–50 rooms;
   - 3 or more audited mobs per band, all dropping band-scaled coin;
   - a gear upgrade per band;
   - existing starter abilities;
   - soft death unchanged;
   - acceptance A1–A8 with thresholds in the standards document;
   - exclusions as listed under Scope.

**Adopted defaults** *(not individually reviewed; cheap to veto at advisor framing)*:
- zero starting coin (R13);
- `consider` usable in combat (R22);
- `consider` failure messaging;
- help lookup order verb → topic → ability;
- the B1-adjacent / B3-farthest layout (R4).

## Design notes

*(Durable seam rationale. These survive disintegration into the slices' `completed/` records and the living docs.)*

- **A world declares its own start.** A `world.yaml` manifest at the content root names the starting room. `World:StartingRoomBlueprintId` becomes the fallback, and the existing void-room fallback stays last.
  - **Why:** the starting room is a fact about the *world*, not the host. With dev-world now the primary content, and operator worlds, the playtest harness and (Phase 6) instances all booting different content, a host setting that must be overridden in lock-step with `ContentDirectory` breaks in every new launch context.
  - **Breadth:** one field now (*shape for later*). The manifest is the home for future world-level facts, such as a default respawn distinct from the start, or a display name, but none is added speculatively.
  - Everything that reads the resolved starting room gets the resolved value: creation-time respawn, death fallback, and the catalog's rename guard.
- **One spawn truth: the room's spawn rule, keyed by rule.** Spawn slots key by *rule identity*, not `(room, blueprint)`, and a rule's count may exceed 1.
  - **Why:** the `(room, blueprint)` key caps a room at one of each mob and silently drops duplicate rules. A mob template's `spawnRoomBlueprintId` places it once per boot but never respawns it unless a matching room rule also exists. Two placement mechanisms, one of them non-respawning, is a trap at hunting-area volume.
  - **Shaped for later:** tiered-overworld Stage B's weighted area pools become *a rule whose blueprint is drawn from a pool*. Keying slots by rule rather than by blueprint is what makes that a refactor, not a rewrite.
  - The mob-side spawn room is retired or demoted: either it becomes an editor shortcut that writes a room rule, or validation warns when no rule covers it. The planner picks which, but the room rule stays the single truth.
- **Area designation is a (Tier, Band-range) record, shaped for tiered-overworld R2.** Areas gain a designation of tier plus band range. Phase 5 authors single-band ranges and Tier 0; the theme stays the existing `aspectAffinities` (biome is Phase 6).
  - **Consumers:** the editor shows it, and the balance audit gains a check: a spawn rule placing a mob whose (Tier, Band) falls outside its area's designation **warns**. This follows the established warn-not-error posture and closes the "audit blind spot" class R30 anticipates.
- **Help topics are an engine layer plus a world overlay over one definition store.**
  - Topics are non-entity definitions: a `DefinitionRegistry` family (Spine F), never `TemplateRegistry` (INV-12).
  - They load from an ordered list of roots: the committed **engine help layer** (generic mechanic topics, engine-owned, outside any world's content directory), then `<ContentDirectory>/help/`. The world's copy wins by topic id.
  - Lookup order is verb → topic → ability.
  - Help topics are the **third** hand-rolled per-family YAML load/validate/save path (balance standards #1, sim scenarios #2), which crosses the ≥3 bar in the backlogged "YAML-authored definition pipeline" entry. The shared store is therefore **extracted now** (INV-19), with standards and scenarios migrating onto it in the same or the adjacent slice.
  - The entry's coexistence/override question is answered for this family by layer order. Compiled-plus-YAML coexistence for Ability/Effect is still that entry's to decide.
- **One relative-power scale, on effective power, owned by Progression.** A new domain-tier relative-power read composes `IStatSystem` (effective scores: gear, progression, effects, tier) with the core `IPowerBudgetSystem` oracle. It returns the subject/target power relationship. Three consumers share it:
  1. `consider`;
  2. anti-grind;
  3. the admin `power` command's live-entity snapshot (today hand-rolled in the command).
  - `consider`'s **trivial** label *is* the anti-grind floor (one constant, one meaning: "this won't teach you anything"). The upper labels are cut-points beside it in `ProgressionConstants`, catalogued in [balance.md](../design/balance.md) as tunables.
  - **Why the seam moves:** anti-grind today reads raw base attributes because `ProgressionSystem` reading `IStatSystem` closes a DI cycle (`IStatSystem → IEffectSystem → ProgressionEffectContributor → IProgressionSystem → IStatSystem`). That makes anti-grind blind to progression and gear: a character's ratio against a mob never moves. The fix is to keep `ProgressionSystem` free of `IStatSystem` and have the **orchestration boundary** gather the relationship. The kill/use award handlers, and the sandbox's progression executor, call the relative-power read and pass the ratio into the award, so the live and sandbox kill paths still cannot diverge.
  - This is the mechanism-vs-consequence split: the power relationship is a read over stats; XP scaling and danger labels are the two consequences that consume it.
  - **Shaped for later:** the read takes a target *power*, not only a target entity. Cell anchors therefore feed it too: tiered-overworld R10 entrance telegraphs, area banners, and later mob-AI flee/threat decisions.
- **Progression is gated by challenge, not by repetition or time.** The intended rule: as a character or skill gets stronger, *easier* content advances it more slowly, and progress continues at full pace against content near its own level.
  - **Two levers can deliver this.** (a) Each improvement costs more XP. (b) Easier content awards less XP.
  - **Lever (b) is the primary mechanism.** It is anti-grind's `victimPower / earnerPower` ratio on effective power: zero below the floor, proportional up to the cap. It applies to **every rule with an opponent**: the kill rule (as today), plus the **ability-use and damage-taken rules whenever the trigger names an opponent**. For those two it is a rule-table flag (`AppliesAntiGrindPowerRatio`), not new code. An ability used with no opponent keeps today's behaviour: no ratio, and only its per-rank chance decay.
  - **Lever (a) is not used for kill XP as built, and stays that way for now.** The cumulative threshold `ThresholdBase + k × ThresholdIncrement` grows, but the *per-improvement* cost is flat (100, then 50 per step). Kill XP therefore advances at a constant rate per improvement. Use-based XP gets its own sub-linear slowdown from the per-rank chance decay. Whether steps should cost more is a **pacing** question: it slows peer-level progress too. It is decided from evidence during balance closure (R24–R25), not now. See Open questions.
  - **No repeat-count or time-window diminishing returns,** now or planned. Grinding the *same* peer-level content at a steady rate is acceptable. Only *outgrown* content stops paying.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Seams and homes

| Seam | Kind | Home / layer | Disposition |
|---|---|---|---|
| World manifest (`world.yaml`: starting room) | New content-root definition; resolved at load | `Core/Modules/World`: loader resolves manifest → config → void | **Build now** (one field; the shape is for later) |
| Dev-world launch profile | Host configuration (content dir + own DB) | `Server` launch profile / environment appsettings; documented in the dev-world README | **Build now** (no engine code) |
| Spawn rule keyed by rule, with count | Change to `SpawnConfigComponent.Rules` / `SpawnSystem` slot model | `Core/Modules/Spawn` (domain); authoring through the room builder system + `RoomContentWriter`; room-editor UI; an admin verb as a thin caller | **Build now**; area pools **shaped for later** (Stage B) |
| Area designation (tier + band range) | Area template field + content-consistency check | `Core/Modules/World` template/writer, live component and the check. It moved out of `IBalanceAuditSystem` at the slice's `/advise` because it compares authored tags only ([`completed/area-designation.md`](../roadmap/completed/area-designation.md#decisions)). Area editor field; general area setter verb | **Build now**; theme/biome **shaped for later** |
| Shared YAML definition store | Generic load/validate/save | Extracted from `IBalanceStandardsStore` / `SimScenarioStore`; core infrastructure beside the registry base | **Build now** (≥3 trigger) |
| Help topic registry (engine + world layers) | `DefinitionRegistry` family over the shared store | `Core/Modules/Help`; `HelpCommand` gains the topic step; re-derived on `reload`; Blazor help-topic editor (world layer editable, engine layer shown read-only with an "override" action) | **Build now** |
| Relative-power read | New domain-tier read (stats + oracle + tier) | `Core/Modules/Progression`, as a sibling system that `ProgressionSystem` must **not** depend on (guard test) | **Build now** |
| Anti-grind input | Award methods take the ratio as input; handlers gather it | `AdvancementHandler` and the use-award handlers; `ProgressionScenarioExecutor` | **Build now**; a **deliberate balance change** |
| Anti-grind coverage | `AppliesAntiGrindPowerRatio` set on the `AbilityUse` and `DamageTaken` rows (applied only when the trigger names an opponent) | `ProgressionConstants.Rules` | **Build now**; a **deliberate balance change** |
| `consider <mob>` | Player command + label mapping | `Core/Modules/Progression/Commands`; label cut-points in `ProgressionConstants` next to `AntiGrindFloorRatio` | **Build now** |
| `power` live snapshot | Migrate onto the relative-power read's snapshot | `BalanceInspection/Commands/PowerCommand` | **Build now** (the third consumer, INV-19) |
| Acceptance expectations | New standards-document family | Owned by the [playtest program](playtest-automation-program.md) | **Hand off** |

### Family dispositions

- **Relative danger:** general now in its *input* (power-keyed, so entities and cells both feed it). Its consumers beyond `consider` and anti-grind (entrance telegraph, banners, AI) are **shaped for later**.
- **Spawn:** rule-keyed slots now. Pools are **shaped for later** in tiered-overworld Stage B; no new backlog entry is needed.
- **Definition store:** extracted now for the YAML families. Ability/Effect going YAML stays in the existing backlog entry, which now has its framework.
- **World manifest:** one field. Further world-level facts are added when a consumer needs them, not speculatively.

### Observers, contributors, ordering

- **No new gameplay events are required.** The only new event is slice 2's admin audit event, `SpawnRuleAuthoredByAdminEvent`, on the established `*ByAdminEvent` → `AdminAuditHandler` fan-in ([`spawn-authoring.md`](../roadmap/completed/spawn-authoring.md)).
  - Spawn keeps the `MobDiedEvent` / `ItemPickedUpEvent` vacancy path.
  - `consider` is read-only output.
  - Help is read-only.
  - The award's existing events (`ExperienceAwardedEvent` / `TrackImprovedEvent`) are unchanged; only their *input* moves.
- **Ordering (INV-7):**
  - The kill-award handler must gather the power relationship **before** the victim is destroyed. It already runs on `MobDiedEvent` ahead of destruction, alongside `SpawnSystem`'s "must run before `DestroyEntity`" handler. The planner confirms the priority and that the victim's effective scores are still readable.
  - The use-award handlers' gathering happens at the point their opponent is known.
- **Contributors:** no new `IEffectContributor`. The relative-power read *consumes* the contributor-folded scores; it never feeds them (INV-24).

### Invariants in tension

- **INV-1 / INV-5:** the relative-power gathering moves into handlers (orchestration), and systems still only compute. `ProgressionSystem` must not gain an `IStatSystem` dependency; the existing architecture-guard test is extended to cover the new sibling read.
- **INV-19:** two thresholds cross in this program, the YAML store (third instance) and the live power snapshot (third consumer). Both land their framework in their slice, not as debt.
- **INV-18:** each schema change ships editor and admin authoring in the same slice: manifest, spawn rules, area designation, help topics. A spawn rule mutated by an admin verb updates both the template and the live room entity (INV-21).
- **INV-25:** two anti-grind changes shift progression outcomes: the switch to effective power, and its extension to the use-based rows.
  - The sandbox's pinned progression goldens and any anti-grind-dependent tests re-pin deliberately, under the [`balance-tuning`](../../.claude/skills/balance-tuning/SKILL.md) skill with a sweep.
  - The **zero-draws rule** still holds (an ineligible candidate consumes no `IRandom` draws), now on the use-based rows too. Their draw-sequence pins in [progression-system.md](../features/progression/progression-system.md) therefore change wherever a use-based candidate becomes ineligible. Re-pin them deliberately, with new tests covering:
    - a below-floor ability use with an opponent;
    - an ability use with no opponent (unchanged).
- **INV-20 / doc accuracy:**
  - **Anti-grind guidance.** The "reads raw attributes" guidance is rewritten to "the handler gathers effective power; the system stays stat-free" in:
    - [progression-system.md](../features/progression/progression-system.md);
    - [power-budget-system.md](../features/progression/power-budget-system.md);
    - the `edit-progression-system` and `balance-tuning` skills.
  - **"Growing threshold" wording.** Fix it so it no longer implies a rising per-step cost: only the cumulative total grows, and each step costs a flat increment. It appears in:
    - [progression.md](../features/progression/progression.md);
    - [flow-31](../architecture/flows/flow-31-progression-award.md) ("slows twice over: the threshold grows *and* the chance decays");
    - the `edit-progression-system` skill.
  - **Challenge-gating model.** Record it in progression-system.md (lever (b) is the mechanism; no repeat or time-window penalties).
- **INV-31:** the help registry and manifest are re-derived on `reload` and read by session threads and Blazor circuits. They take the content catalog's posture: build a new immutable snapshot, then swap it atomically. No new background thread.
- **INV-17:** flows touched:
  - flow-01 (manifest load);
  - flow-07 (starting room from the manifest);
  - flow-20 (spawn slot model);
  - flow-31 (ratio gathered in the handler);
  - flow-03 / help (topic step);
  - flow-05 (reload re-derives topics and manifest);
  - flow-29 (new editors);
  - flow-33 (the sandbox progression executor supplies the ratio).
- **INV-23 / INV-14:** the manifest, help topics and area designation are world-content definitions (YAML only, never `PersistentEntity`).

### Slice decomposition

Each gap slice runs the full per-slice loop (`/new-plan` → spec gate → build → code gate → sync). Content drops are lighter (see below).

| # | Slice | Contents | Requirements | Depends on |
|---|---|---|---|---|
| 1 | ✅ [`dev-world-foundation`](../roadmap/completed/dev-world-foundation.md) (shipped) | Policy amendment (world-content.md, CLAUDE.md, plan.md, dev-world README); world manifest; dev-world launch profile; hub skeleton with starting room and respawn | R1–R3, R15 | — |
| 2 | ✅ [`spawn-authoring`](../roadmap/completed/spawn-authoring.md) (shipped) | Rule-keyed slots + count; room-editor and admin authoring; mob-side spawn-room removal | R9, R12, R30 | 1 |
| 3 | ✅ [`area-designation`](../roadmap/completed/area-designation.md) (shipped) | Optional area (Tier, band-range) designation + YAML/editor/`areaset` authoring; out-of-designation check (warn-only) on room spawn rules in boot report, `area`, Integrity, area editor and rule saves | R4, R23 | 1 (parallel with 2) |
| 4 | ✅ [`help-topics`](../roadmap/completed/help-topics.md) (shipped) | Shared YAML store extraction (`Core/Systems/Definitions/`) + standards/scenarios/world-manifest migration; warn-only `IContentReloadParticipant` port; topic registry with engine + world layers; `IHelpLookupSystem` topic step; topic editor; engine topic set | R18–R19 | — (parallel) |
| 5 | ✅ [`relative-power-consider`](../roadmap/completed/relative-power-consider.md) (shipped) | Relative-power read; anti-grind on effective power, extended to the ability-use and damage-taken rows when there's an opponent (balance re-pin); `consider`; `power` migration; progression doc-accuracy fixes | R20–R22 | — (parallel) |
| 5b | ✅ [`combat-effective-scores`](../roadmap/completed/combat-effective-scores.md) (shipped; gap slice, R30) | Melee attack power, defense and the hit roll derive from effective `Body`; a `ResourceType`-keyed effective pool cap on `IAttributeSystem` (clamped reads, a heartbeat clamp-down, spawn/creation fill and respawn restore at the cap); `score` shows Attack/Defense; tier now reaches combat (tuning stays Phase 6); deliberate re-pin | R21, R26 (A3/A7) | — (parallel with 5; before 6) |
| 6–8 | Content drops B1 → B2 → B3 ([requirements seed](phase-5-content-drops.md)) | Hub onboarding rooms + hub shop rows for the band, hunting area, mobs, found items, help topic text | R5–R8, R10–R14, R16–R17, R24–R25 | 1–3 (4 and 5 by the drop that needs them) |
| 9 | Acceptance golden | A1–A8 on dev-world, graded against the standards family | R26–R29 | playtest substrate + 6–8 |

- **Playtest track interleaving:** the playtest program's substrate slice can start right after slice 1, since it needs a bootable dev-world with a starting room. Its bots then check each content drop. Its own `/refine` → `/advise` fixes the exact order.
- **Content drops are not plan-template slices.** They add no gameplay state or system, so INV-18/19/25 have nothing to check. Each drop is one PR closed by a short checklist:
  - the band-drift audit and the new designation check come back clean;
  - the drop's hunting area carries its (Tier 0, Band n) designation ([area designation](../features/world/area-model.md#area-designation));
  - the baseline sweep has run and its report is reviewed;
  - the band's standards expectations are stated (or explicitly left descriptive);
  - connectivity is verified (R6);
  - the drop's [`completed/`](../roadmap/completed/) record carries an **authoring-friction log**: time spent, tooling pain points, and gaps spun out as R30 slices. That log is the client-tier gate's evidence (R31).

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-10-28. Do not relitigate.)*

1. **Help ownership:** an engine help layer plus a world overlay, with the world winning by id. Rejected: topics in world content only.
2. **`consider` scale:** one shared scale with anti-grind; *trivial* is the anti-grind floor. Rejected: a danger-only scale.
3. **Power basis:** **effective power for both** `consider` and anti-grind. The handler gathers it, the system stays stat-free, and the change is accepted as a deliberate balance change with re-pins. Rejected: raw power for both (labels never move); a split basis.
4. **Spawning:** room spawn rules keyed by rule, with count, authored through the editor and admin verbs; area pools stay in Phase 6 (Stage B). Rejected: pulling pools forward.
5. **Launch:** the world declares its starting room through a `world.yaml` manifest; a dev-world launch profile; the default content directory is **unchanged**. Rejected: a launch profile only; flipping the default. *(This resolves the requirements seed's "default content directory" question.)*
6. **Progression gating model:** gated by challenge. Outgrown content advances a character or skill more slowly, down to zero, through the effective-power anti-grind ratio. There are **no repeat-count or time-window penalties**. Diminishing returns from repetition alone are not a goal.
7. **Anti-grind on skills:** the ability-use and damage-taken rules apply the anti-grind ratio whenever the trigger names an opponent. No-opponent uses keep today's behaviour (only per-rank chance decay). Approved as a deliberate balance change in slice 5.
8. **XP-cost curve:** the flat per-improvement cost stays for now. Whether steps should cost more is decided from evidence during balance closure, not up front.

**Advisor calls** *(cheap to veto at planning)*:
- the shared YAML store extraction lands in the help slice;
- area designation is a band *range*;
- `consider` cut-points live in `ProgressionConstants`, not the standards document. They are tuning knobs, not designer *expectations*, so balance.md rule 4 doesn't apply.
- content drops use a checklist instead of the plan template;
- acceptance-golden churn policy: the golden asserts **graded pass/fail per criterion at a pinned seed**, not exact metric values. A content drop re-pins it only if it breaks the loop, and exact-number pins stay on the stable mechanic subset. The playtest `/refine` confirms this.

## Open questions

- ~~**Mob-side `spawnRoomBlueprintId`.**~~ **Resolved in [`spawn-authoring`](../roadmap/completed/spawn-authoring.md):** the field is removed. The mob/item editors' "Spawns in" panel writes room rules instead. Legacy `spawnRoomBlueprintId`/`spawnRoomId` keys are a hard-break load error, and the operator converts by hand.
- ~~**Rule identity.**~~ **Resolved in [`spawn-authoring`](../roadmap/completed/spawn-authoring.md):** an authored id, unique within the room, defaulting to the blueprint id. Explicit ids are required when two rules share a blueprint.
- ~~**Engine help layer location.**~~ **Resolved in [`help-topics`](../roadmap/completed/help-topics.md#decisions):** committed at `content/help/`, configured by `Help:EngineTopicDirectory` (default `content/help`), resolved by `DataPaths` against the Hedron root (not under `Paths:DataDirectory`), and copied to the Server and `Hedron.Web` output for installs.
- **Use-based award gathering:** where exactly each use-based award handler resolves its opponent to gather the ratio. For damage taken the opponent is the attacker; for an ability use it is the resolved target. The behaviour is settled (resolved decision 7): with no opponent, the ratio is not applied. *(Planner, slice 5.)*
- **XP-cost curve shape:** whether each improvement should cost progressively more XP (lever (a)), and by how much. To be answered from evidence once the B1–B3 content exists, during balance closure (R24–R25): the simulator's `progressionRate` sweep, and the playtest bots' XP-per-hour and kills-per-improvement metrics. The question is whether peer-level progress feels too fast. If the curve changes, it is a `ThresholdIncrement`/curve change under the `balance-tuning` skill, with re-pins. *(User + designer, during balance closure.)*
- **Concrete threshold values** for R25. These are designer-stated during the content drops, and the metric is reported descriptively until then. *(User, during authoring.)*
