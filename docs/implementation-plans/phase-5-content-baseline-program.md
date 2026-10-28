# Phase 5 — Content baseline → functional MVP (program)

- **Status:** planned (requirements seed — awaiting `/advise`)
- **Actors:** Player, Administrator (designer authoring the baseline), System (spawns, respawn, acceptance bots)
- **Module:** program-level, no single module. The content lives in `content/dev-world/`. The gap slices touch existing modules: Help (topic articles), the power-budget oracle's consumers (`consider`), and the authoring tooling as gaps surface. The advisor places each slice. Feature homes: [`world/`](../features/world/world.md), [`communication/`](../features/communication/communication.md) (help), [`progression/`](../features/progression/progression.md) (`consider` over the oracle).

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
- **Tier 1+ content, a player-facing ascension trigger, and the ascension-baseline calibration fix.** These move to the Phase 6 progression-expansion pillar. Tier 1 is reachable today only through admin `ascend`, and the tier baseline has no real combat effect ([backlog](../roadmap/backlog.md)).
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
- **R3.** A **hub area** marked safe (`isSafe`) contains the **starting room**, where new characters land; the **default respawn point**; the shop(s) (R11); and the onboarding path (R17).
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
  - verb-only `help` with ability fallback and no topic articles ([help-system](../features/communication/help-system.md));
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

## Open questions

- **Default content directory.** Dev-world is now the primary content, but `World:ContentDirectory` still defaults to the gitignored `data/content/`. Should a fresh clone's `dotnet run --project Server` boot dev-world by default? Doing so would make the in-game builder verbs write into the committed fixture by default. *(User + advisor at framing. Recommendation: keep the explicit override and document it, unless the "a stranger can play" story needs zero configuration.)*
- **Slice decomposition and order.** A likely shape: policy amendment + hub skeleton → help topics → `consider` → per-band content drops (B1, B2, B3), each closed by audit + sweep → acceptance golden. Where the playtest substrate interleaves follows the playtest seed's sequencing. *(Advisor.)*
- **Help-topic storage and format.** Whether topics are YAML content under the content directory (which then raises dev-world-vs-operator ownership of engine help text), a committed engine-content location, or something else. Also how the editor inspects them. *(Advisor, who can read the code.)*
- **`consider` label cut-points.** These are the power-ratio/band-delta thresholds behind each label. Should they be designer-tunable (a standards-document row) or constants? *(Advisor; balance.md rule 4 suggests standards.)*
- **CI golden churn as dev-world grows.** R1 makes dev-world grow over time, so the acceptance golden and any dev-world-dependent tests will need re-pinning on content drops. The re-pin policy (pinned seed and stable subset vs. re-pin per drop) needs deciding. *(Advisor + playtest `/refine`.)*
- **Concrete threshold values** for R25. These are designer-stated during the program, as each band's content lands. Until then the metric is reported descriptively. *(User, during authoring.)*
