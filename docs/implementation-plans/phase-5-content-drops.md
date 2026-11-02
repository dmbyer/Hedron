# Phase 5 content drops B1 → B2 → B3 (slices 6–8)

- **Status:** planned (requirements + architecture brief + **B1 plan** — spec gate run 2026-11-01, all findings resolved in this doc — B1 ready for `implement-plan`; B2/B3 run off the checklist)
- **Actors:**
  - Administrator: the agent authoring through the real tooling, and the maintainer approving numbers and reviewing each drop.
  - Player: a fresh character walking the loop.
  - System: spawns, respawn, the CI conformance guard.
- **Module:** content only. No `Core/` module changes. Changes land in three places:
  - `content/dev-world/`: areas, rooms, mobs, items, and a new `sim/` scenario folder; `world.yaml` is unchanged.
  - `Server/appsettings.DevWorld.json` and `Hedron.Web/appsettings.DevWorld.json`: `Simulation:ScenarioDirectory` points at `content/dev-world/sim`.
  - `Hedron.Tests/World/`: the dev-world conformance guard.

  Any engine gap found becomes its own R30 slice (program R30).

**Description.** The three content drops of the [Phase 5 program](phase-5-content-baseline-program.md#slice-decomposition), slices 6, 7 and 8. Together they turn dev-world's three-room hub skeleton into the ~45-room Tier-0 baseline region: a finished hub, plus three hunting areas chained hub → B1 → B2 → B3, each with audited mobs, band-scaled coin, found items for the sell loop, and shop gear for the band. An agent authors each drop through the real authoring surfaces (editor, grid editor, builder verbs, authoring API), never by hand-writing YAML, and logs the friction it meets. That log is evidence for the client-tier gate (program R31). The maintainer approves every concrete number before merge. Per the program brief, drops are **checklist PRs, not plan-template slices**: they add no gameplay state or system. This file fixes what each drop contains and how "done" is judged.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-11-01. The advisor may extend it, or confirm that drops go straight to authoring under the checklist. It disintegrates into each drop's `completed/` record on ship. Local requirements are numbered **D1…Dn** so they don't collide with the program's R-numbers; program requirements are cited as "program R*n*".)*

### User stories

- *As a new player*, I want to land in a hub whose first rooms teach me the verbs and point me at an easy hunting ground next door, so I can start without help (program R3, R17).
- *As a player*, I want each hunting area to be clearly harder the farther I walk from the hub, with `consider` and the area names telling me so, so that I choose my risk (program R4, R21).
- *As a player*, I want to find junk I can sell and coin I can loot, and see a better weapon and armor for my band in the store, so that progress feels concrete (program R8, R11–R12).
- *As a player*, I want to die in the far area, wake in the hub, and walk back out with nothing lost, so that death teaches without ending my session (program R16).
- *As the authoring agent*, I want to build each drop through the same editor and verbs a designer would use, and log every workaround, so that the client-tier gate gets real friction evidence (program R31).
- *As the maintainer*, I want each drop's numbers (coin ranges, prices, item values, respawn delays, densities) proposed with their derivation and left for my sign-off, so that balance is my call (program R25).
- *As CI*, I want the dev-world conformance rules checked on every PR, so that a later drop or edit can't silently break an earlier band.

### Scope

**In scope:**
- **B1 drop (slice 6):** finishes the hub (onboarding path, shop, gate toward B1) and adds the B1 area, B1 mobs, B1 found items, the B1 shop rows and the CI conformance guard (D1–D20).
- **B2 drop (slice 7):** adds the B2 area, mobs, found items and shop rows, chained beyond B1.
- **B3 drop (slice 8):** adds the B3 area, mobs, found items and shop rows, chained beyond B2.
- **Per-drop closure:** the drop checklist (D21–D27), including the authoring-friction log and the band's target numbers recorded in prose.

**Out of scope (confirmed):**
- **All program-level exclusions** ([program Scope](phase-5-content-baseline-program.md#scope)): Tier 1+, mob AI and aggro, mob item drops, death penalties, new abilities, a tutorial zone, and so on.
- **World help overlays.** The engine layer already meets program R18 (D18).
- **Starting gear.** Characters start with no items and zero coin (D15).
- **Tuning changes inside a drop.** Oracle, standards, constants, the anti-grind floor and the XP-cost curve are out of drop scope; each tuning need found becomes its own R30 slice under `balance-tuning` (D25).
- **Bot verification of drops.** PT-2 hasn't shipped. Bots start checking drops when it lands; it isn't a drop prerequisite.
- **Thresholds in the `playtestExpectations` standards family.** That family ships with PT-2. Drops record targets in prose (D24), and PT-2 or slice 9 ports them.
- **Hand-written YAML** as the authoring path (D19).

### Behavioral requirements

**A. Region topology (all drops)**
- **D1.** The region totals **about 45 rooms**: hub about 10 (the existing 3 plus an onboarding path of about 5, the shop room, and a gate room toward B1), B1 about 10, B2 about 12, B3 about 12. Every room has grid coordinates (`x`/`y`/`z`) placed through the grid editor.
- **D2.** The areas form a **chain**: hub → B1 → B2 → B3. The only way into B2 is through B1, and the only way into B3 is through B2. The distance gradient (program R4) follows from the geography.
- **D3.** **Full connectivity** (program R6). Every room is reachable from `room.hub.square` and has a path back. There are no one-way exits and no dead ends without a return.
- **D4.** Area names are **functional**: `Band 1 Field`, `Band 2 Woods`, `Band 3 Ridge` (or equivalent generic names chosen at authoring). Each hunting area carries the designation `Tier 0, Band n–n` ([area designation](../features/world/area-model.md#area-designation)). The hub stays undesignated, `pvp: false`, with no hostile spawns (program R3).
- **D5.** Room descriptions at each area boundary say plainly that the next area is more dangerous. The descriptions are generic, with no lore (program R1).

**B. Hub (B1 drop)**
- **D6.** The **onboarding path** is a short sequence of hub rooms starting at `room.hub.square`. Its descriptions name the core verbs in context: `look`/movement, `help`, `score`/`progress`, `inventory`/`wear`, `consider`/`kill`, `list`/`buy`/`sell`, and what happens on death. It leads the player toward the B1 gate (program R17).
- **D7.** A **General Store** room in the hub holds one shopkeeper mob. The shopkeeper:
  - has `protection: untargetable, effectimmune`;
  - has a `shop:` block in the default currency;
  - stocks the per-band base rows (D13);
  - buys any item with `Value > 0` (existing sell behavior).
- **D8.** The world start and the default respawn point stay `room.hub.square`. `world.yaml` is unchanged (program R15).

**C. Mobs (each drop, for its band)**
- **D9.** Each drop adds **at least 3 mob templates** in its (Tier 0, Band n) cell. Each is `type: creature` and tagged with its Tier and Band, and the band-drift audit reports it in-cell (program R7, R23).
- **D10.** Mob names are **generic creature nouns with no band in the name** (e.g. "rat", "wolf"). The band is telegraphed by `consider`, the area name and the descriptions. Each mob has usable keywords for `kill` and `consider` targeting.
- **D11.** Every hunting mob has a **currency-loot range** that rises by band per kill (B3 > B2 > B1, program R8). No hub mob is hostile, and no mob starts combat (program R10; no aggro mechanic exists).
- **D12.** Hunting mobs are placed by **room spawn rules** with respawn delays. One player hunting an area must never leave it permanently empty during a session (program R9).

**D. Items and economy (each drop, for its band)**
- **D13.** The General Store sells **one weapon and one armor piece per band**, each tagged in that band's cell. Each is priced so that the band-n pair is affordable from band-n hunting within the band's stated coin-to-upgrade target (D24; program R11).
- **D14.** Each hunting area has **room spawn slots for found items** (program R12):
  - mostly **junk** with `Value > 0` and no Tier/Band tag;
  - **1–2 gear slots** of modest in-cell gear on longer respawn delays.
- **D15.** **B1 found items include a basic weapon**, so a fresh character with no starting gear and zero coin (program R13) has a free first weapon before the first store upgrade. Characters receive no starting items.
- **D16.** Every authored item that carries power is tagged with its Tier/Band and in-cell under the audit (program R14). Every found item and every shop item has `Value > 0`.

**E. Numbers (each drop)**
- **D17.** The agent **proposes every concrete number** in the drop PR, with its derivation. The derivation uses the oracle's `TargetRange(tier, band)` for mobs and gear, and the band's coin-to-upgrade target for loot ranges and prices. The numbers covered are: mob stats, XP scale, coin ranges, item values and stats, shop prices (derived from Value), spawn counts, respawn delays and densities. **The maintainer approves the numbers before merge.**

**F. Help**
- **D18.** **No world help overlays.** The engine topics (`getting-started`, `combat`, `death`, `shops`, `progression`) meet program R18. Each drop checks that they still read correctly against the real layout. If a topic reads wrong, that is fixed in the engine topic (if it is generic) or flagged. It is not papered over with a world overlay.

**G. Authoring method (each drop)**
- **D19.** The agent authors **only through the real tooling**, never by hand-writing YAML: the Blazor editor and grid editor (driven in the browser pane), the builder verbs on a `DevWorld` host, and the authoring API. If the tooling can't author a needed field, the agent logs the gap (D23) and either works around it through another real surface or spins out an R30 slice (program R30, R31).
- **D20.** **CI conformance guard** (B1 drop). Dev-world's fixture tests are extended so that every PR checks:
  - each designated hunting area has ≥3 mobs in its cell;
  - every hunting mob has a currency-loot range, and the ranges rise by band;
  - every found or shop item has `Value > 0`;
  - every hunting area is designated;
  - the band-drift audit and the out-of-designation check report no findings;
  - connectivity holds (already guarded).

  The rules must be written so the B2 and B3 drops satisfy them without editing the guard. The test shape is the planner's call.

**H. Drop checklist (closes each drop's PR)**
- **D21.** The band-drift audit and the out-of-designation check come back **clean** (program R23).
- **D22.** The **baseline sim sweep** has run against the drop's mobs, and its report is reviewed in the PR (program R24).
- **D23.** The drop's `completed/` record carries an **authoring-friction log**: time spent, tooling pain points, multi-step workarounds, editor defects, and the R30 slices spun out. Entries are tagged **agent-observed**.
- **D24.** The band's **target numbers are recorded in prose** in the drop's `completed/` record and the dev-world README. The targets are time to first kill, kills per track improvement, coin to first upgrade, deaths per hour, and a fresh character's win rate. Each number is either stated or explicitly marked "descriptive". PT-2 (or slice 9) ports them into the `playtestExpectations` family when it lands (program R25).
- **D25.** Any **tuning need** the drop surfaces (the *trivial* floor, `consider` cut-points, the XP-cost curve, oracle weights) is filed as an R30 slice or backlog entry. It is not changed in the drop.
- **D26.** **Connectivity and the CI guard pass**, and `dotnet build` and `dotnet test` are green (INV-25).
- **D27.** A **manual smoke walk** on the `DevWorld` profile walks the band's part of the loop: reach the area, `consider` a mob, kill it, loot coin, collect and sell a found item, and (from B1 on) buy and wear the band's upgrade. The B3 drop also covers dying in B3 and respawning in the hub. The walk is recorded in the PR.

### Edge cases & failure behavior

- **Area temporarily cleared:** spawn rules refill it on their delays (D12). The smoke walk and later bots treat this as waiting, not stuck.
- **Fresh character walks straight to B3:** this is allowed. The chain forces them through B1/B2, and boundary descriptions (D5) plus `consider` are the warning.
- **Death deep in B3:** the character respawns at `room.hub.square` and walks back through B1/B2. Nothing is lost, since there is no aggro.
- **Player sells their B1 found weapon:** this is allowed. The buy-back shelf protects the mistake (existing shop behavior).
- **Shopkeeper targeted:** `kill`/`consider` refusal per `untargetable`. The till-seeding debt ([backlog](../roadmap/backlog.md)) can't be reached.
- **The B1 gear pair is affordable before any B1 kill:** this can't happen, since characters start with zero coin.

### Content & authoring needs

- **Every content field the drops need already has an authoring surface,** according to the docs:
  - area designation (`areaset`, area editor);
  - spawn rules with count (room editor, `spawnrule`);
  - mob Tier/Band/XP scale and currency loot;
  - protection and the shop block with base-stock rows (`setmob`, `MobEditor`);
  - item Value and Tier/Band;
  - grid coordinates (grid editor).

  A field that turns out not to be authorable is a D19/D23 gap, not a reason to hand-edit YAML.
- **The authoring host** is a `DevWorld` profile host, so builder verbs and editor saves write into committed `content/dev-world/` ([dev-world README](../../content/dev-world/README.md)).
- **Target numbers** go in prose (D24) until the `playtestExpectations` family exists.

### Grounding notes

- **All dependencies have shipped:** slices 1 (manifest, hub skeleton, `DevWorld` profile), 2 (rule-keyed spawns with count), 3 (area designation and the out-of-designation check), 4 (help topics, engine layer in `content/help/`), 5 (`consider`, effective-power anti-grind) and 5b (combat on effective scores). See [`done.md`](../roadmap/done.md).
- **Current dev-world:** three hub rooms (`room.hub.square/north/south`) in `area.hub` (`pvp: false`). `DevWorldFixtureTests` already guards the manifest start, grid coordinates, strong connectivity and the non-PvP hub.
- **The playtest program hasn't shipped.** PT-1 and PT-2 are `planned` ([PT-2](playtest-run-mode.md)), and the `playtestExpectations` family ships with PT-2. Hence D24 and the "bots out of scope" call.
- **Shops** ([shop-system](../features/economy/shop-system.md)): prices are computed from `Value` on read. A shop has one implicit shopkeeper per room. Restock is top-up. Shopkeepers are `untargetable` per [mob protection](../features/mobs/mobs.md).
- **Known balance gaps the drops will hit, but not fix** ([balance.md](../design/balance.md#known-gaps-tracked-in-backlogmd)):
  - *trivial* can't be reached in Tier 0 at the 0.25 floor;
  - the `progressionRate` sim can't see use-based XP;
  - progression-rate tolerances aren't promoted;
  - the XP-cost curve is an open program question.

  All of these belong to balance closure (D25).
- **Sim sweeps over authored mobs:** whether the baseline sweep can target the dev-world mob set directly or needs a scenario per band is code-answerable (see Open questions).

### Resolved decisions

*(Confirmed by the user on 2026-11-01. Do not relitigate.)*

1. **Authoring:** the agent authors through the real tooling (editor in the browser pane, builder verbs, authoring API) and never hand-writes YAML. The maintainer reviews. Rejected: the maintainer authors by hand; agent-written YAML plus a human editor pass.
2. **Topology:** a chain, hub → B1 → B2 → B3. Rejected: spokes off the hub with corridors.
3. **Thresholds:** recorded in prose per drop, and ported when PT-2's family lands. Rejected: blocking drops on PT-2; deferring all thresholds to slice 9.
4. **Seed shape:** one file with per-drop scope. Rejected: three seed files.
5. **Defaults approved as proposed:**
   - B1 builds the whole hub;
   - about 45 rooms;
   - a weapon and an armor piece per band;
   - no starting gear;
   - generic mob names without the band;
   - junk plus 1–2 gear found-item slots;
   - no world help overlays;
   - tuning moves become R30 slices;
   - a CI conformance guard.
6. **Numbers:** the agent proposes them with derivations, and the maintainer approves before merge. Rejected: the maintainer states them up front.
7. **Friction evidence:** the agent's log is tagged *agent-observed*. The maintainer adds a human-pass note on at least one drop before the Phase 5 → 6 client-tier gate. Rejected: treating agent friction as sufficient on its own.

## Design notes

*(Durable rationale. It survives into each drop's `completed/` record and the dev-world README.)*

- **The drops are content, not a feature.** They add no component, system, event or persistent state, so most of the checklist has nothing to bite on. That is why B2 and B3 skip the plan template. Only B1 carries non-content work (the guard, the profile key, the scenario convention), and that is all it puts through `/new-plan`.
- **The dev-world conformance guard is policy, not an engine rule.** "≥3 mobs per cell, coin rising by band, every found item worth more than 0" describes what dev-world promises as the baseline. It is not something every world must satisfy, so it lives in the dev-world fixture tests, beside connectivity, and not in `IBalanceAuditSystem` or `AreaPlacementCheck`. The guard reuses the engine's own judgements (the band-drift audit, `AreaPlacementCheck.Sweep`) for "in-cell" and "in-designation". It never re-derives them, so it can't drift from what the editor shows.
  - **Shaped for later:** if operators want coverage checks on their own worlds, the dev-world rules promote to an authorable "content coverage" check in BalanceInspection, with dev-world's thresholds as one configuration. The guard's rules are written as named predicates over loaded templates so that move is a refactor, not a rewrite. No backlog entry is added: nobody has asked for it.
- **Sim scenarios are world content.** A scenario naming dev-world mobs by blueprint id belongs to dev-world, just as PT-2 places playtest scenarios in `content/dev-world/playtest/`. They live in `content/dev-world/sim/`, and the `DevWorld` profile points `Simulation:ScenarioDirectory` there. Then the editor's sim page, the `simulate` run mode and balance closure all see the same committed set. `WorldContentLoader` reads only `areas/`/`rooms/`/`items/`/`mobs/`, so the folder is never loaded as templates. Reports stay under `data/` (gitignored, run output).
  - **Convention per band:** one `Combat` scenario per mob (the cell's reference build vs. the mob, graded by the existing outcome tolerances), plus one `ProgressionRate` scenario per band. The kill-only blind spot for use-based XP stays a known gap ([balance.md](../design/balance.md#known-gaps-tracked-in-backlogmd)).
- **The authoring surface is the Blazor editor, on purpose.** The client-tier gate decides Blazor vs. React from authoring-friction evidence ([client-tier.md](../design/client-tier.md#decision-gate)), so the agent authors through the editor in the browser pane, and its pain points are exactly the evidence the gate needs. The authoring JSON API stays as narrow as it is (mobs only, scoped to the bakeoff). Widening it to make agent authoring cheaper would trade away the evidence and expand a surface the gate hasn't decided on.
- **Playing the game before PT-1 exists is a throwaway telnet script.** The agent drives a `DevWorld` Server through a small TCP client kept in its scratchpad, never committed, for builder verbs and the D27 smoke walk. The transcript goes in the PR. This is deliberately not a harness: PT-1/PT-2 are the harness, and once PT-2 ships, a drop's walk can become a committed playtest scenario.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Placement

| Piece | Home | Drop | Disposition |
|---|---|---|---|
| Hub completion, B1–B3 areas, mobs, items, spawn rules, shop rows | `content/dev-world/`, authored through the Blazor editor (+ builder verbs via the telnet script) | B1 / B2 / B3 | **Build** (content) |
| Dev-world conformance guard (D20) | `Hedron.Tests/World/`, beside `DevWorldFixtureTests` (the planner picks extend vs. sibling class) | B1 | **Build now**; promotion to an engine check **shaped for later** |
| Per-band sim scenarios (D22) | `content/dev-world/sim/` | each drop, for its band | **Build** (content) |
| `Simulation:ScenarioDirectory` → `content/dev-world/sim` | both `appsettings.DevWorld.json` profiles; dev-world README | B1 | **Build now** (config only) |
| Engine help topic corrections (D18) | `content/help/` | any drop, if a topic reads wrong | **Build** (generic text only) |
| Target numbers in prose (D24) | drop `completed/` record + dev-world README | each drop | **Build**; ported by PT-2 or slice 9 |
| Telnet driver script | the agent's scratchpad | — | **Not committed** |

### Observers, contributors, ordering

None. No events, handlers or contributors are added, and no heartbeat work changes. Content already rides the existing spawn, loot, shop and death paths.

### Invariants in tension

- **INV-25.** The B1 drop adds tests (the guard). B2 and B3 add content the guard must pass without edits. The re-pin rule still applies: if a drop's content moves an existing golden (e.g. a sim pin that loads dev-world), that's a deliberate re-pin under `balance-tuning`. Expected: none, since the shipped sim goldens use inline or reference combatants.
- **INV-12 / INV-21.** All live content comes from templates. Spawn rules and shop rows are written through the editor/catalog or the builder verbs, which already dual-write template and entity. No hand-written YAML (D19).
- **INV-18.** Nothing new to author. A field the editor can't reach is an R30 gap slice, not a workaround in YAML.
- **INV-20 / INV-27–30.** Each drop updates the dev-world README (layout, targets, the scenario folder) and writes its `completed/` record with the friction log. The B1 drop also documents the `Simulation:ScenarioDirectory` key in the simulation feature doc's config line.
- **Cross-process staleness (acknowledged debt, [backlog](../roadmap/backlog.md)).** The editor writes to disk; a running `DevWorld` Server sees the change only after `reload`. The agent's loop is: author in the editor → `reload` over the telnet script → verify. The plan writes this down so friction from it is logged as known debt, not rediscovered.

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-11-01. Do not relitigate.)*

1. **Game driver:** a throwaway, uncommitted telnet script in the agent's scratchpad, used for builder verbs and the D27 walk, with the transcript in the PR. All authoring goes through the Blazor editor in the browser pane, and the JSON API is not widened. Rejected: the maintainer walks each drop; blocking drops on PT-2.
2. **Sim scenarios:** committed in `content/dev-world/sim/` (per mob: combat vs. the cell reference build; per band: progression rate), with the `DevWorld` profile pointing `Simulation:ScenarioDirectory` there. Rejected: uncommitted scenarios under `data/`.
3. **Cadence:** `/new-plan` and the spec gate for the B1 drop only. B2 and B3 run off the D21–D27 checklist, each with a code gate on its PR. Rejected: a plan per drop; no plans at all.
4. **Guard scope:** a dev-world fixture test only, reusing the engine's audit and placement judgements. Promotion to an engine check is shaped for later, with no backlog entry. Rejected: a reusable engine coverage check now.

**Advisor calls** *(cheap to veto at planning)*:
- **A gap that blocks a drop parks the drop's branch.** The R30 slice lands on master first, and the drop rebases onto it. A gap with a real-surface workaround is logged and the drop continues.
- **The guard derives its band list from designated hunting areas.** It doesn't hard-code B1–B3. B1 passes alone, and B2/B3 pass without editing the guard.

## B1 drop plan (slice 6)

*(In-flight, produced by `/new-plan` on 2026-11-01. **Every section below is scoped to the B1 drop only.** B2 and B3 don't get these sections: they run off the D21–D27 checklist (advisor resolved decision 3), and the guard and conventions B1 lands are what they're checked against. Like the rest of this file, it disintegrates on ship: the guard's rules → the dev-world README; the scenario key → the configuration and simulation docs; the standards pin → balance.md and the `balance-tuning` skill; the shared fixture → `07-testing.md` and the `add-tests` skill; decisions, numbers and the friction log → `roadmap/completed/phase-5-content-drop-b1.md`.)*

### B1 — Verified grounding (code, 2026-11-01)

These were checked in code before planning. Line numbers are as of `bc796f8`.

- **Existing guard.** `Hedron.Tests/World/DevWorldFixtureTests.cs` copies `content/dev-world/` to a fresh temp directory **per test**: the constructor runs at :30–34 and xUnit builds a new instance for every `[Fact]`. `BootAsync` (:60–83) then loads the copy through the real `WorldContentLoader` into a `TemplateRegistry`. The class already guards these:
  - the manifest start (:92);
  - the non-PvP hub (:102);
  - grid coordinates (:111);
  - strong connectivity (:123);
  - **the out-of-designation check.** `Assert.Empty(new AreaPlacementSystem(registry).Findings())` at :150 already covers that part of D20 ("the out-of-designation check reports no findings"). The new guard doesn't repeat it.
- **Out-of-designation check API.** The pure judgement is `AreaPlacementCheck.Judge(PlacementSite, IAreaPlacementSource)`, and `Sweep(source, areaId?)` runs it over a source (`Core/Modules/World/Systems/AreaPlacementCheck.cs:40,66`). `RegistryPlacementSource` (`RegistryPlacementSource.cs:15`) is public and exposes three methods:
  - `Sites()` yields one `(room, area, rule)` entry per room spawn rule;
  - `DesignationOf(areaId)` returns the area's designation;
  - `TagOf(blueprintId)` returns `(Kind, Tier, Band)`.

  The guard reads the same three as its **inputs** (which mobs and items each area places, and its designation), so "what an area places" means the same thing to the guard as to the check.
- **Band-drift audit API.** `BalanceAuditSystem.Audit()` returns `BalanceAuditReport(Drifted, BucketCounts)` (`Core/Modules/BalanceInspection/Systems/BalanceAuditSystem.cs:36`). It flags authored-vs-computed drift greater than `bandDriftTolerance` (default 1), and skips `Band == 0` (:87). Tests already build it from the registry with these arguments (`Hedron.Tests/Modules/BalanceInspection/BalanceAuditSystemTests.cs:29`):
  - `new PowerBudgetSystem(tunables)`;
  - `new ItemPowerProjectionSystem()`;
  - `new MobPowerProjectionSystem()`;
  - `tunables`;
  - `tolerance`.
- **How `Simulation:ScenarioDirectory` resolves.** `Server/DataPaths.cs:92–96` post-configures `SimulationOptions.ScenarioDirectory` as `Location("Simulation:ScenarioDirectory", "sim/scenarios")`. An explicit key wins, and a relative path resolves against the Hedron root (the folder holding `Hedron.sln`, :69–77). So `"content/dev-world/sim"` in both `appsettings.DevWorld.json` files resolves to the committed folder, whatever the working directory, even when `HEDRON_Paths__DataDirectory` is set. `DataPathsTests.A_per_location_key_overrides_the_data_directory_and_resolves_against_the_root` (:102) already tests that resolution generically. **Neither profile sets the key today:** both files carry only `World:ContentDirectory` and `Persistence:DatabasePath`.
- **How the sim resolves combatants.** `SimCombatantFactory` (`Core/Modules/Simulation/Systems/SimCombatantFactory.cs`) resolves each combatant source differently:
  - **`MobTemplate`** loads through `IContentDefinitionCatalog` (:61), which is rooted at `WorldOptions.ContentDirectory`. Under the `DevWorld` profile, scenarios therefore resolve dev-world mobs by blueprint id. The resolved mob gets an **empty ability kit** (:87–88), and its cell is its own authored tag (:81).
  - **`ReferenceBuild`** returns `Tunables.ReferenceBaseScores + cell.ReferenceBuild.GearBonuses` (:96–97; `BalanceStandardsRegistry.cs:37–48`).
- **What the reference build is under the compiled defaults.** `BalanceStandardsDefaults.Document` has **no cells** (`BalanceStandardsDefaults.cs:27–31`). The registry dense-fills each missing cell with an empty-gear reference build (`BalanceStandardsRegistry.cs:16–17,56–64`). The store uses the defaults whenever the standards file is absent (`BalanceStandardsStore.cs:47–49`), and warns at load if `ReferenceBaseScores` drifts from `CharacterDefaultsOptions` (:168–179). The result: **the (0,1) reference build is exactly an ungeared fresh character's scores.** That is all 10s, 100 HP, AttackPower 5 and Defense 2 (`CharacterDefaultsOptions.cs:5–9`).
- **Scenario store.** `SimScenarioStore.Validate` is structural only. It requires a non-blank `mobBlueprintId` (`SimScenarioStore.cs:220`) and doesn't check that the mob exists; a missing mob fails only when the scenario runs. `SaveAsync` writes into `Simulation:ScenarioDirectory` (`ISimScenarioStore.cs:44`), and that is how the Simulation page and "Simulate vs reference" save. The two checked-in examples live in `data/sim/scenarios/`; under `DevWorld` they won't be listed, which is intended.
- **Editor coverage.** `Hedron.Web/Components/Pages/MobEditor.razor` covers type (:45), protection flags (:115–130), XP scale (:158), coin loot (:375–395) and shop base-stock rows (:223–235, 453–466). No field B1 needs is missing as far as the code shows. The authoring pass is the real test (D19/D23).

### B1 — Preconditions

- Slices 1–5b have shipped ([`done.md`](../roadmap/done.md)). Dev-world is three hub rooms in `area.hub` (`pvp: false`, undesignated), and `world.yaml` names `room.hub.square`.
- These authoring surfaces exist on a `DevWorld` host:
  - the Blazor room, area, mob and item editors, the grid editor, and the Simulation and Standards pages;
  - the builder verbs (`dig`, `mkarea`, `areaset`, `spawnrule`, `setmob`, `setitem`, `reload`, `power`, `powerband`).
- `DevWorldFixtureTests` is green on the current content.
- The balance standards in force on the authoring host are the compiled defaults: B1-OQ1 pins `Balance:StandardsPath` to the absent `content/dev-world/balance/standards.yaml`, and the CI guard loads the same path.
- PT-1/PT-2 haven't shipped. The D27 walk uses the throwaway telnet script (advisor resolved decision 1).

### B1 — Postconditions (the coverage contract)

Each postcondition names the test or checklist item that verifies it (see **B1 — Test plan**).

1. **Hub completion (D6–D8).** The hub has about 10 rooms in `area.hub`:
   - the existing 3;
   - an onboarding path of about 5 rooms from `room.hub.square`, whose descriptions name the D6 verbs in context;
   - a General Store room;
   - a gate room whose exit leads into B1.

   The hub stays undesignated and `pvp: false`. It places no targetable (non-`untargetable`) mob. `world.yaml` is unchanged.
2. **Shopkeeper (D7).** The General Store room has one spawn rule placing one shopkeeper mob template with:
   - `protection: untargetable, effectimmune`;
   - a `shop:` block in the default currency (coin);
   - base-stock rows for the B1 weapon and the B1 armor piece.
3. **B1 area (D1–D5, D12).** One new area (about 10 rooms, a functional name such as `Band 1 Field`) is designated `Tier 0, Band 1–1`. It is reachable only through the hub gate room. Every room has `x`/`y`/`z`, and the boundary rooms' descriptions say plainly that the next area is more dangerous.
4. **B1 mobs (D9–D11).** There are **at least 3** mob templates, each with:
   - `type: creature`;
   - `tier: 0, band: 1` (banded: `Band ≥ 1` is guarded), within band-drift tolerance (guarded), and projecting exactly to `(0,1)` in the editor readout (checklist, WP2 exit);
   - a generic creature-noun name and usable keywords;
   - a coin `CurrencyLoot` range with `Max > 0`.

   Each is placed by B1 room spawn rules with a count and a respawn delay.
5. **Found items (D14–D16).** The B1 rooms carry two kinds of item spawn slots:
   - mostly junk, with `Value > 0` and no Tier/Band tag;
   - 1–2 slots of modest `(0,1)` gear on longer delays. **One of them is a basic weapon** (D15).

   Every authored item has `Value > 0`. Every item that carries power (non-empty stat bonuses) is banded (guarded), within drift tolerance (guarded), and projects exactly to `(0,1)` in the editor readout (checklist).
6. **Shop rows (D13).** One weapon and one armor piece are tagged `(0,1)`, have `Value > 0`, and are within drift tolerance (exact `(0,1)` projection: checklist). They're stocked by the shopkeeper. Their summed buy price (`Value × ShopOptions.BuyRatio`) is the band's stated coin-to-first-upgrade target (D24).
7. **Conformance guard (D20).** A CI test class enforces the dev-world baseline rules over the loaded content. It **derives its band list from the designated hunting areas**, so B1 passes it alone and B2/B3 pass it unedited.
8. **Sim scenarios (D22).** `content/dev-world/sim/` holds the B1 scenarios:
   - one `Combat` scenario per B1 mob (the `(0,1)` reference build vs. the mob, `melee-only` both sides);
   - one `ProgressionRate` scenario for B1.

   All were saved through the Simulation page. Every scenario loads, and every mob it names exists in dev-world. Each scenario has been run, and its report reviewed in the PR.
9. **Profile config.** Both `Server/appsettings.DevWorld.json` and `Hedron.Web/appsettings.DevWorld.json` set `Simulation:ScenarioDirectory` to `content/dev-world/sim` plus `Balance:StandardsPath` set to `content/dev-world/balance/standards.yaml` (B1-OQ1). The two profiles have the same keys and values, and the guard reads its standards from the profile's `Balance:StandardsPath`, rebased onto its temp copy (one source).
10. **Checklist closure (D21–D27).** Before merge:
    - the audit and the placement check are clean;
    - the sim reports have been reviewed;
    - the friction log (agent-observed) and the B1 target numbers in prose are in the PR and in the dev-world README;
    - tuning needs are filed as R30 slices or backlog entries;
    - build and tests are green;
    - the D27 walk transcript is in the PR;
    - **the maintainer has signed off the numbers** (D17).
11. **No engine change.** The diff touches nothing in `Core/`. Any engine gap is its own R30 slice, landed on master first if it blocks (advisor call).

### B1 — Main flow (the drop as executed)

1. **Bring up the hosts.** Bring up `DOTNET_ENVIRONMENT=DevWorld` `Hedron.Web` (the editor, in the browser pane) and `Server` (the game, driven by the scratchpad telnet script). Create an admin character on the dev-world database.
2. **Derive and propose numbers (D17).** Read `TargetRange(0,1)` from the Standards page (or `powerband`), and `ShopOptions` ratios from config. Write the derivation sheet: mob stats, XP scale, coin ranges, item values and stats, prices, spawn counts, delays and densities. Put it in the draft PR. **Maintainer checkpoint 1:** the maintainer approves or edits the proposed numbers. Spawn counts, delays and densities are **provisional** at this point: their ticks-per-kill input needs authored mobs (MobEditor's "Simulate vs reference" runs against the last-saved mob), so they're re-derived after step 5 and confirmed at checkpoint 2.
3. **Lay out the hub completion.** Use the grid editor and room editor (`dig` over telnet where the editor can't create): the onboarding path, the General Store and the gate room. Write the D6 descriptions. Then `reload` and walk the path over telnet.
4. **Author the B1 area.** Create it in the area editor (or `mkarea`), set its designation `Tier 0, Band 1–1` (area editor or `areaset`), and add about 10 rooms with grid placement and exits chained from the gate room. `reload`, then check connectivity with a local `dotnet test --filter DevWorld`.
5. **Author mobs, items and the shopkeeper.** Use the mob and item editors, entering the approved numbers. Keep the readout's projected cell in `(0,1)`. Add the shop block and base-stock rows on the shopkeeper.
6. **Place everything.** Add room spawn rules from the room editor or `spawnrule`: hunting mobs across B1, junk and gear found-item slots, and the shopkeeper in the General Store. The rule-save placement finding must stay empty.
7. **Simulate (D22).** On the Simulation page (or MobEditor's "Simulate vs reference"), compose and save one `Combat` scenario per B1 mob, plus one `ProgressionRate` scenario whose `ticksPerKill` is prefilled from the combat report. Run each and attach its report to the PR.
8. **Walk the loop (D27).** Over the telnet script:
   - reach B1 and `consider` each mob (record the labels against program R21: at or below *even*);
   - kill one and loot coin;
   - pick up and sell a found item;
   - pick up the free weapon, buy and wear the B1 upgrade.

   Put the transcript in the PR.
9. **Close the checklist (D21–D27).** Confirm the Integrity page shows a clean audit and placement report. Log the friction (agent-observed) and record the B1 target numbers in prose in the PR and the dev-world README. File tuning needs as R30 slices or backlog entries. Run `dotnet build` and `dotnet test` until green. **Maintainer checkpoint 2:** the maintainer gives final sign-off on the numbers as authored, then the code-mode `architecture-reviewer` runs on the diff.

### B1 — Events fired

**None new.** No event, publisher or subscriber is added or changed. The authoring verbs fire their existing admin audit events (`SpawnRuleAuthoredByAdminEvent` etc.), and the smoke walk drives the existing combat, death, loot, item and shop events. Neither is a B1 change.

### B1 — Systems / handlers involved

**None new, none changed** (Postcondition 11). Content rides existing paths:
- `SpawnSystem` / rule-keyed slots;
- `CurrencyLootComponent` on kill;
- `ShopSystem` (list/buy/sell, base-stock top-up restock);
- mob protection;
- `IRelativePowerSystem` (`consider`);
- `IProgressionSystem` (XP).

The guard is a **consumer** of `AreaPlacementCheck` / `RegistryPlacementSource` and `BalanceAuditSystem` and `BalanceStandardsStore` (B1-OQ1). It changes none of them.

### B1 — Content tooling impact (INV-18)

- **New data-file shapes: none.** Every field B1 writes already has a loader, writer and editor (see "Editor coverage" above).
- **New content locations:**
  - `content/dev-world/sim/` holds sim scenario YAML in the existing `SimScenarioStore` format. It's authored and inspected through the Simulation page (compose/save/load/run), and run headless with `simulate --scenario content/dev-world/sim/<file>.yaml`. `WorldContentLoader` never reads the folder (it reads only `areas/`, `rooms/`, `items/` and `mobs/`).
  - `content/dev-world/balance/standards.yaml` is the dev-world standards location. **No file is created by B1**: absent means compiled defaults. If one is ever saved, it's authored through the Standards page.
- **New admin commands, `TemplateRegistry` entries, editors: none.** New templates are ordinary area, room, mob and item entries.
- **Authoring method (settled, D19):** the Blazor editor in the browser pane, plus builder verbs over the scratchpad telnet script. The agent never hand-writes YAML. A field that can't be authored is a D23 log entry plus an R30 slice, never a YAML edit.

### B1 — Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Why |
|---|---|---|
| Commands | **Adequate** | No new verbs. Builder verbs and player verbs are exercised as-is. |
| Output | **Adequate** | No new messages. Description prose is content. |
| Persistence (INV-14, INV-22, INV-23) | **Adequate** | **Level 1:** every entity B1 adds is *world content* (areas, rooms, mobs, the shopkeeper, world-spawn items) with no `PersistentEntity`. Found items picked up during the walk enter the persistent domain through the existing `ItemContextHandler` transition, unchanged. Shop purchases ride the shop's existing path. **Level 2:** no component is introduced or touched. **Level 3:** no `SaveEntityAsync` call is added. Admin verbs keep their existing boundary saves. |
| Event bus | **Adequate** | Nothing published or subscribed anew. |
| ECS queries | **Adequate** | No new code-side queries. The guard reads templates, not live components. |
| Broadcast | **Adequate** | Unchanged. |
| Time | **Adequate** | Respawn delays and shop restock are authored values on existing heartbeat paths. The guard reads no clock. |
| Content templates | **Adequate**, with **acknowledged debt** | `TemplateRegistry` is the one world model (INV-12), and editor saves and builder verbs dual-write template and entity (INV-21). Debt: cross-process staleness ([backlog](../roadmap/backlog.md), "Cross-process content-cache coherence"), in **both directions**: (a) the editor writes disk, and a running `Server` sees it only after `reload`; (b) builder verbs over telnet (`dig`, `spawnrule`, …) write disk, and the Web catalog's in-memory index doesn't see it until "Refresh from disk", so an editor save from a stale cache can overwrite an exit or rule added over telnet. The agent's loop is written down for both: editor batch → `reload` on the Server; verb batch → "Refresh from disk" in the editor before its next save. Friction from either is logged as known debt, not rediscovered. |
| Configuration | **Adequate**, with a **gap closed in B1** (B1-OQ1) | `Simulation:ScenarioDirectory` is an existing per-location key, so adding it to the profiles is config only. The gap: the `DevWorld` profile doesn't pin `Balance:StandardsPath`. On a machine with `HEDRON_Paths__DataDirectory` set, a dev-world host loads **the operator's own standards file**. That file drives three things on the authoring host: the oracle tunables behind the editor readouts and the Integrity audit, `consider`/anti-grind labels, and the sim's reference builds and grading. The CI guard sees the compiled defaults, so the two can disagree silently. B1-OQ1 closes it: both profiles pin the path. |
| Sessions | **Adequate** | The telnet script is an ordinary client over an ordinary session. |
| Modules / DI | **Adequate** | No module change. |
| Concurrency (INV-31) | **Adequate** | No background initiator, no web-host call path, no shared singleton state added. Running Web and Server together on one content directory is the existing two-process setup (the staleness debt above). |
| Test infrastructure | **Adequate** after a small extraction | The temp-copy-and-boot setup in `DevWorldFixtureTests` gets a second consumer (the guard). It's extracted into a shared fixture so it isn't duplicated (see the resolution of the guard open question). |

### B1 — Flows introduced or modified (INV-17)

**None.** B1 adds content and one config value. It doesn't change any runtime call path:
- [flow-33 Simulation run](../architecture/flows/flow-33-simulation-run.md) already reads scenarios from `Simulation:ScenarioDirectory`, and pointing the profile elsewhere doesn't change the trace.
- [flow-05 Content reload](../architecture/flows/flow-05-content-reload.md) and [flow-08 Admin authoring](../architecture/flows/flow-08-admin-room-creation.md) are exercised as they stand.
- The walk exercises flows [17](../architecture/flows/flow-17-kill-mob-combat-initiation.md), [20](../architecture/flows/flow-20-mob-death-respawn.md), [09](../architecture/flows/flow-09-item-pickup.md), [13](../architecture/flows/flow-13-wear-item.md), [30](../architecture/flows/flow-30-shopping.md) and [31](../architecture/flows/flow-31-progression-award.md) unchanged.

If the walk shows a flow behaving differently from its doc, that's a finding (an R30 slice or a doc fix), not a B1 edit.

### B1 — Test plan / Verification (INV-25)

**The guard.** A new class `Hedron.Tests/World/DevWorldConformanceTests.cs` plus a shared fixture `Hedron.Tests/World/DevWorldContentFixture.cs`. It's a content-fixture guard: the real loader over a temp copy of committed content, the same posture as `DevWorldFixtureTests`, so it's a **content-fixture guard** (not the tier-3 Main-Flow tier of [`07-testing.md`](../architecture/07-testing.md); WP1 adds `DevWorldContentFixture` to that doc's harness table). The rules are **named, pure predicates** in `Hedron.Tests/World/DevWorldConformanceRules.cs`. Each takes the loaded registry and a placement source, and returns a list of violation strings. Each `[Fact]` asserts that its list is empty, and the message is the joined list.

A **hunting mob** is a mob template placed by any room spawn rule (`RegistryPlacementSource.Sites()` + `TagOf`) whose `Protection` lacks `Untargetable` and whose `IsShop` is false. A **hunting area** is an area whose rooms place at least one hunting mob. The **band list** is the distinct `(Tier, Band)` cells covered by the designated hunting areas. Nothing names B1, B2 or B3.

| Rule (predicate) | Postcondition / D | Assertion |
|---|---|---|
| `EveryHuntingAreaIsDesignated` | P1, P3 / D4, D20 | Every hunting area has a non-null `DesignationOf`. A hunting mob placed in a room with **no area** (`Sites()` yields `AreaBlueprintId = null`) is a violation, never skipped. This makes "the hub places no targetable mob" enforced, not just stated. |
| `EveryHuntingMobIsBanded` | P4 / D9, D20 | Every hunting mob has `Band ≥ 1`. Needed because both engine judgements skip `Band == 0` (`AreaPlacementCheck.cs:49`, `BalanceAuditSystem.cs:87`), so an unbanded mob in a designated area would otherwise pass every rule. |
| `AtLeastOneDesignatedHuntingArea` | P3 / D20 (non-vacuity) | The band list isn't empty. Without it, every per-band rule passes vacuously on an empty world. **It lands with the B1 content** (work package 3), since today's dev-world has no hunting area. |
| `EachDesignatedHuntingAreaHasThreeMobsInCell` | P4 / D9, D20 | There are ≥3 **distinct** hunting-mob blueprints per designated area **whose authored cell the designation contains** (`designation.Contains(...)` over `TagOf`, the same containment the placement check uses). With `EveryHuntingMobIsBanded`, the existing placement assertion (`DevWorldFixtureTests`:150) and the drift rule below, "in designation" is guarded; exact in-cell projection is a checklist item (WP2 exit). |
| `EveryHuntingMobHasCoinLoot` | P4 / D11, D20 | Every hunting mob has `CurrencyLoot[Coin]` with `Max > 0` and `Min ≤ Max`. |
| `CoinLootRisesByBand` | P4 / D11, D20 | Group hunting mobs by authored `(Tier, Band)` and order by `PowerBudgetTunables.GlobalBandIndex`. The mean coin midpoint `(Min+Max)/2` must strictly increase from band to band. With one band (B1 alone) it holds trivially; it starts to bite at B2. |
| `EveryFoundAndShopItemHasValue` | P5, P6 / D16, D20 | Two kinds of item: a *found* item is any item blueprint placed by a room spawn rule, and a *shop* item is any blueprint in any mob's `ShopBaseStock`. Each must resolve to an `ItemTemplate` with `Value > 0`. An unknown blueprint is also a violation. |
| `EveryPoweredItemIsBanded` | P5, P6 / D16 | Every item template with non-empty stat bonuses has `Band ≥ 1`. Junk stays untagged by having no bonuses; an untagged item that carries power is a violation (the audit skips `Band == 0`). |
| `BandDriftAuditIsClean` | P4–P6 / D16, D21 | `BalanceAuditSystem.Audit().Drifted` is empty, i.e. every banded mob and item is **within drift tolerance** (default 1 band). That is weaker than exact in-cell: a B1 tag computing to Band 2 passes, so exact projection stays a checklist item (WP2 exit readouts). The tunables and tolerance come from the standards the dev-world host uses: the profile's `Balance:StandardsPath` rebased onto the temp copy, through `BalanceStandardsStore` (absent, so the compiled defaults). The audit's own judgement is reused, never re-derived. |
| `EveryCommittedScenarioResolves` | P8 / D22 | Every `*.yaml` under `<copy>/sim/` loads through `SimScenarioStore.Load`, and every `MobTemplate` combatant names a mob template in the registry. This catches a rename that would otherwise fail only at run time, because `Validate` is structural (`SimScenarioStore.cs:220`). |

**Already guarded, not repeated:** connectivity (`DevWorldFixtureTests`:123), the out-of-designation check (:150), the hub being non-PvP (:102), grid coordinates (:111).

**Predicate unit tests** (system-unit style, `DevWorldConformanceRulesTests.cs`). These run against a hand-built `TemplateRegistry`, with no disk. Each rule gets **one violating case and one passing case**: an undesignated area that places a targetable mob, a hunting mob in an area-less room, an unbanded hunting mob in a designated area, two mobs in a designated area, three mobs of which one is outside the designation, an untagged item with stat bonuses, a band-2 mob with a lower mean coin midpoint than band 1, a junk item with `Value 0`, a shop row naming an unknown item, a scenario naming a missing mob, an empty world for the non-vacuity rule. Without these, a predicate that is vacuously true (e.g. one that matches no sites) would pass on real content forever, which is exactly the kind of bug neither a player nor a reviewer would see.

**Profile parity** (`Hedron.Tests/Composition/DevWorldProfileTests.cs`, config tier). The test reads both `appsettings.DevWorld.json` files from the repo root and asserts:
- they have the same set of keys and the same values;
- `Simulation:ScenarioDirectory == "content/dev-world/sim"`;
- `Balance:StandardsPath == "content/dev-world/balance/standards.yaml"`.

That keeps the Web profile's sim page from silently drifting from the Server's `simulate`. `DevWorldContentFixture` derives the guard's standards path from the same profile value (rebased onto its temp copy) rather than hard-coding it, so the pin and the guard can't diverge. Resolution itself is not retested: `DataPathsTests`:102 covers per-location keys generically.

**Verified by checklist, not a test:**
- exact `(0,1)` projection of every banded mob and item (editor readouts, WP2 exit; the CI drift rule only bounds it to tolerance);
- the sim verdicts (the reports are advisory, D22; R28 keeps sweeps out of CI);
- the `consider` labels against R21 (the D27 walk, plus the `power <mob>` relation line);
- the onboarding prose (D6);
- the D18 engine help topics read against the real layout (the walk).

**Skipped, and why:**
- **Description wording and area names:** presentation, obvious on the first walk.
- **The specific numbers:** maintainer sign-off (D17), not a test. Pinning them would turn every balance pass into a test edit.
- **Respawn density (D12):** checked by derivation and the walk. A density predicate would re-derive kill rate from the sim and is out of proportion.
- **The DataPaths resolution of the new key:** already covered.

**Goldens:** none should move. The shipped sim goldens use inline or reference combatants and don't load dev-world (brief, INV-25). If one does move, that's a deliberate re-pin under `balance-tuning`, called out in the PR.

### B1 — Implementation plan (work packages)

Each package is sized for one sub-agent. Work packages 2 and 3 are **authoring work** done through the editor in the browser pane and the scratchpad telnet script, never by hand-writing YAML. Their number work is **propose → maintainer approves**; the plan doesn't fix any number. The primary agent runs the code-mode `architecture-reviewer` on the combined diff after work package 3.

**Work package 1: guard, profile key, docs** *(code and config; no content dependency)*
- **Scope:**
  1. Extract the temp-copy and boot code from `DevWorldFixtureTests` (`FindRepoRoot`, `CopyDirectory`, `BootAsync`) into `DevWorldContentFixture`. It's an `IAsyncLifetime` class fixture: it copies once per class, boots once, and exposes the registry, config, temp path and a `RegistryPlacementSource`. Move `DevWorldFixtureTests` onto it via `IClassFixture`; its tests are read-only over the registry, so one boot per class is safe.
  2. Add `DevWorldConformanceRules`, `DevWorldConformanceTests` (every rule except `AtLeastOneDesignatedHuntingArea`) and `DevWorldConformanceRulesTests`.
  3. Add `"Simulation": { "ScenarioDirectory": "content/dev-world/sim" }` to both `appsettings.DevWorld.json` files plus `Balance:StandardsPath` set to `content/dev-world/balance/standards.yaml` (B1-OQ1), and add `DevWorldProfileTests`.
  4. Create `content/dev-world/sim/` with a `.gitkeep`, so that "Save" on the Simulation page has a target before work package 3.
  5. Update the docs:
     - [`features/simulation/simulation.md`](../features/simulation/simulation.md) config and content lines (:84–85): the `DevWorld` profile points scenarios at `content/dev-world/sim/`;
     - [`architecture/05-configuration.md`](../architecture/05-configuration.md) :52, the list of keys the `DevWorld` profile overrides;
     - the [dev-world README](../../content/dev-world/README.md): a `sim/` folder section, and the guard's rules in one line each;
     - [`features/world/world-content.md`](../features/world/world-content.md) :52: replace the example name `Band-2 Wolf` with a band-free creature noun, per D10; and :53, the profile's overrides now include the scenario directory and standards path;
     - the dev-world README :11 and the root `README.md` :103 (the run table's "committed profile" parenthetical): the same override list;
     - [`design/balance.md`](../design/balance.md) :45 (standards location): one line saying the `DevWorld` profile pins `Balance:StandardsPath` to the absent `content/dev-world/balance/standards.yaml`;
     - [`architecture/07-testing.md`](../architecture/07-testing.md) harness table: a `DevWorldContentFixture` row;
     - `.claude/skills/balance-tuning/SKILL.md` :32: the standards path comes from `Balance:StandardsPath`; `DevWorld` pins it to `content/dev-world/balance/standards.yaml` (absent → compiled defaults), so saving one there commits dev-world standards and is itself a balance-tuning change; dev-world scenarios live in `content/dev-world/sim/`;
     - `.claude/skills/add-tests/SKILL.md`: a one-line pointer to `DevWorldContentFixture` for any test over committed dev-world content.
- **Files:**
  - `Hedron.Tests/World/{DevWorldContentFixture,DevWorldConformanceRules,DevWorldConformanceTests,DevWorldConformanceRulesTests}.cs`;
  - `Hedron.Tests/World/DevWorldFixtureTests.cs`;
  - `Hedron.Tests/Composition/DevWorldProfileTests.cs`;
  - both `appsettings.DevWorld.json`;
  - `content/dev-world/sim/.gitkeep`;
  - the docs and skills above (`simulation.md`, `05-configuration.md`, `world-content.md`, dev-world `README.md`, root `README.md`, `balance.md`, `07-testing.md`, `balance-tuning` and `add-tests` skills).
- **Depends on:** nothing (B1-OQ1 decided: pin the standards path).
- **Out of scope:** any `Core/` change; any content; changing `IBalanceAuditSystem` or `AreaPlacementCheck` (the guard only consumes them).
- **Exit criterion:** `dotnet build` and `dotnet test` are green on the **current** three-room dev-world. Every conformance rule passes vacuously or genuinely, and every predicate unit test (violating and passing case) is green.

**Work package 2: hub completion and the B1 area, mobs, items, shop** *(authoring)*
- **Scope:** Main flow steps 1–6.
  1. **Derivation sheet (D17), in the draft PR.** For each kind of number, the derivation:
     - **mob stats:** inside `TargetRange(0,1)` (Standards page or `powerband`), confirmed by the MobEditor readout's projected cell;
     - **XP scale:** 1.0 unless argued otherwise;
     - **shop prices:** the B1 pair's Values, chosen so that `(ValueW + ValueA) × BuyRatio` equals the proposed coin-to-first-upgrade target;
     - **coin per kill:** (that target − the expected found-junk sell income `Σ Value × SellRatio` over the target kill count) ÷ the target kill count, spread into a Min/Max range;
     - **found gear:** in-cell and weaker than or equal to the shop pair;
     - **spawn counts and delays:** sized so one hunter's kill rate (ticks per kill, from a first quick "Simulate vs reference" run × `Heartbeat:IntervalMs`) never exceeds the area's refill rate (D12);
     - **gear delays:** longer than junk delays (D14).
  2. **Maintainer checkpoint 1** on the sheet (spawn counts, delays and densities provisional; see Main flow step 2).
  3. Author through the editor and verbs: `reload` on the Server after each editor batch, and "Refresh from disk" in the editor after each builder-verb batch (the two-direction staleness debt). After the mobs exist, re-derive spawn counts, delays and densities from a real "Simulate vs reference" ticks-per-kill and carry them to checkpoint 2.
  4. Keep a timestamped friction log in the scratchpad as you go (D23 fields).
- **Files:** `content/dev-world/areas/`, `rooms/`, `mobs/` and `items/`. All are written by the editor and the verbs. `world.yaml` is unchanged.
- **Depends on:** work package 1 (the guard gives immediate feedback while authoring), and maintainer checkpoint 1.
- **Out of scope:**
  - B2/B3 content;
  - world help overlays (D18);
  - starting gear (D15);
  - any tuning constant or standards edit (D25);
  - hand-written YAML (D19);
  - widening the JSON API.

  An unauthorable field gets the advisor's call: if it blocks, park the branch and land an R30 slice first; otherwise use a real-surface workaround and log it.
- **Exit criterion:**
  - `dotnet test` is green, including connectivity, placement and every conformance rule;
  - the Integrity page shows no drift and no placement findings;
  - the MobEditor and ItemEditor readouts show each banded template in `(0,1)`.

**Work package 3: sim scenarios, smoke walk, checklist closure** *(authoring and evidence)*
- **Scope:** Main flow steps 7–9.
  1. Compose and save the scenarios on the Simulation page.
  2. Run them, and attach the reports' verdict lines and key metrics to the PR.
  3. Add the `AtLeastOneDesignatedHuntingArea` fact to `DevWorldConformanceTests` (non-vacuity now holds).
  4. Do the D27 walk and paste the transcript.
  5. Write the D24 B1 targets in prose in the dev-world README and the PR: time to first kill, kills per track improvement (from the progression report), coin to first upgrade, deaths per hour (descriptive unless the walk supports a number), and the fresh-character win rate (the combat reports' side-A win rate).
  6. File the D25 tuning needs. A likely one: the equal-cell 50% ± 10% standard means a new player's first unarmed fight is roughly a coin flip. Record it; don't change it.
  7. Finish the friction log.
  8. **Maintainer checkpoint 2:** final numbers sign-off.
- **Files:** `content/dev-world/sim/*.yaml` (saved by the Simulation page; remove `.gitkeep`); `Hedron.Tests/World/DevWorldConformanceTests.cs` (the non-vacuity fact); `content/dev-world/README.md`.
- **Depends on:** work package 2.
- **Out of scope:** committing the telnet script or the reports (reports stay under `data/`); bot or PT-2 work; changing the standards or tolerances.
- **Exit criterion:**
  - `dotnet build` and `dotnet test` are green;
  - every B1 mob has a saved combat scenario, and B1 has a progression scenario, with all of them run;
  - the PR carries the reports, the walk transcript, the friction log and the targets;
  - the maintainer has signed off.

  Then the primary agent runs the code-mode `architecture-reviewer`, and `sync-roadmap` writes `roadmap/completed/phase-5-content-drop-b1.md` with the derivation sheet, the friction log and the targets.

**Build order:** work package 1, then 2, then 3. Work package 1 can merge-review on its own diff but ships in the same PR. The B2 and B3 drops then reuse the work package 1 artifacts unchanged.

### B1 — Design notes (planner)

- **Resolved: reference build vs. fresh character → one subject, the `(0,1)` reference build. No second, ungeared subject.**
  - **Why one subject is enough.** Under the compiled default standards, every cell's reference build is the empty-gear dense fill (`BalanceStandardsRegistry.cs:16–17,56–64`). Its scores are `ReferenceBaseScores`, which mirrors `CharacterDefaultsOptions`, and the store warns at load if it drifts (`BalanceStandardsStore.cs:168–179`). So the `(0,1)` reference build **is** the ungeared fresh character, stat for stat. A second "fresh" subject would be an identical duplicate. That holds only while dev-world's `(0,1)` cell carries no `gearBonuses`, which is why B1-OQ1 matters: it pins which standards dev-world reads.
  - **What the scenario does and doesn't model.** It's the worst case for a new player: no weapon and no ability use. A fresh character's starting abilities (`kick`, `empower`) aren't in the reference kit, and mob templates resolve with an empty kit too (`SimCombatantFactory.cs:87–88`). So `melee-only` on both sides is the like-for-like choice. D15's free weapon only improves on that figure. Modelling an *armed* fresh character would need an `Inline` subject that copies the weapon's stat bonuses. That's a second copy of item data, and it would drift from the item template. It's rejected: the D27 walk covers the armed fight qualitatively.
  - **Why `ProgressionRate` doesn't replace it.** It measures kills per improvement through the analytical award seam, not win rate (`ProgressionScenarioExecutor.cs:21–28`). So it complements the combat scenario. Its `ticksPerKill` is prefilled from the combat report.
  - **If the standards ever gain `(0,1)` gear bonuses,** the reference build stops being "fresh". That would be a `balance-tuning` change, and it re-runs these scenarios by rule 3 of [balance.md](../design/balance.md#keeping-balance-current-the-maintenance-contract).
- **Resolved: guard extend vs. sibling → a sibling class `DevWorldConformanceTests`, sharing an extracted `DevWorldContentFixture`.** Four reasons:
  - **Different subject.** `DevWorldFixtureTests` guards the world's *skeleton*: it boots, has a start, is placed, and is connected. The new class guards the *baseline content policy*. The design notes keep the policy rules shaped for promotion to a BalanceInspection "content coverage" check, and a self-contained predicate set in its own class makes that move a lift.
  - **No duplication.** A second consumer of the temp-copy and boot code would otherwise copy it. Extracting the fixture now also gives PT-2 or slice 9 a ready dev-world boot.
  - **Cheaper runs.** The class fixture copies and boots once per class instead of once per `[Fact]`. The guard's dozen-odd facts would otherwise re-copy dev-world each time.
  - **No overlap.** Connectivity and the placement check stay where they are, and the new class doesn't re-assert them.
- **"In designation" is the engine's judgement; exact in-cell is the readout's.** The guard never computes power itself:
  - placement containment comes from `AreaPlacementCheck` (already asserted), and "≥3 mobs" uses the same `designation.Contains` over authored tags;
  - both engine judgements skip `Band == 0`, so `EveryHuntingMobIsBanded` and `EveryPoweredItemIsBanded` close that hole (spec-gate finding);
  - tag honesty within drift tolerance comes from `BalanceAuditSystem`; exact `(0,1)` projection is checked on the editor readouts at the WP2 exit, not in CI.

  The design notes' no-drift principle holds.
- **The guard derives everything from content.** It finds hunting areas through what they place, and the band list through designations. Coin rises through `GlobalBandIndex` order. B2/B3 pass with no guard edit. The non-vacuity rule ensures a world that loses all designations fails rather than passes.
- **Two maintainer checkpoints** (before authoring, and before merge) split D17 so numbers aren't authored twice in the common case. The second checkpoint is the D17 gate itself.

## Open questions

- **Human-pass drop (user, later).** Which drop gets the maintainer's own authoring-friction note before the client-tier gate (requirements resolved decision 7)?
- ~~**Reference build vs. fresh character (planner, B1).**~~ **Resolved (planner):** one subject, the `(0,1)` reference build, which under dev-world's standards *is* the ungeared fresh character. `melee-only` both sides. No `Inline` armed variant. See **B1 — Design notes (planner)**.
- ~~**Guard extend vs. sibling (planner, B1).**~~ **Resolved (planner):** a sibling `DevWorldConformanceTests`, plus an extracted `DevWorldContentFixture` shared with `DevWorldFixtureTests`. See **B1 — Design notes (planner)**.
- ~~**B1-OQ1: pin dev-world's balance standards.**~~ **Resolved (user, 2026-11-01): pin it.** Both `DevWorld` profiles set `Balance:StandardsPath` to `content/dev-world/balance/standards.yaml`, and the file is not created. Original analysis: The `DevWorld` profile doesn't set `Balance:StandardsPath`, so it falls back to `<Paths:DataDirectory>/balance/standards.yaml`. On a machine with `HEDRON_Paths__DataDirectory` set, that's **the operator's own world's standards**. They then drive dev-world's editor readouts, the Integrity audit, `consider`/anti-grind labels, and the sim's reference builds and grading. Meanwhile CI sees the compiled defaults. The B1 numbers would then be derived against a file outside the repo.
  - **Recommendation:** pin `Balance:StandardsPath` to `content/dev-world/balance/standards.yaml` in both `DevWorld` profiles, **without creating the file**. Absent means the compiled defaults, so dev-world is graded against the engine defaults, reproducibly, everywhere. If dev-world ever needs its own standards, the Standards page saves them there, committed like the scenarios. The guard loads the same path over its temp copy.
  - **This extends the brief's placement table** (it's the same kind of config-only line as the scenario key), so it needs your call.
  - **Alternative:** document the hazard in the dev-world README and require that `HEDRON_Paths__DataDirectory` be unset when authoring dev-world.
- ~~**B1-OQ2: the scenario-coverage rule.**~~ **Resolved (user, 2026-11-01): dropped.** `EveryHuntingMobHasACombatScenario` is not part of the guard; per-mob scenario coverage stays a D22 checklist item. `EveryCommittedScenarioResolves` stays.
