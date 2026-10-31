# Area designation (Phase 5 slice 3)

- **Status:** implemented (WP1-WP3; code-mode gate passed with nits, all fixed; awaiting `sync-roadmap`)
- **Actors:** Administrator (designer labelling areas and placing content), System (load-time validation, the content checks)
- **Module:** placed at advisor intake (see [Design notes](#design-notes)):
  - `Core/Modules/World`: the designation value type, the area template field + deserializer/writer, the live area component, the pure placement judgement and the placement sweep, the validator wiring;
  - `Core/Modules/Admin`: the general area-property setter verb + its builder operation; the `area` verb's extra output;
  - `Core/Modules/Authoring`: catalog-sourced findings for the web host (editor warnings on rule saves, the per-area list);
  - `Hedron.Web`: area editor field and findings list, the Integrity page category, rule-save warnings.

  **Not** `BalanceInspection`: the check never consults the power oracle (see Design notes).

**Description.** Each area can carry an optional **designation**: one tier plus a band range within that tier (for example `Tier 0, Band 2` or `Tier 0, Bands 1–2`). It states the power range the area is meant to hold. The balance audit gains an **out-of-designation check**: a room spawn rule placing a mob or found item whose *authored* (Tier, Band) falls outside its room's area designation produces a **warning**, never an error. The designer sees it:
- on the Integrity page;
- on the area's own surfaces (editor and `area` verb);
- at the moment a spawn rule is authored.

This is the Phase 5 pre-adoption of [tiered-overworld R2/R22](tiered-overworld-program.md). It also gives the content drops (slices 6–8) a guard for the B1 → B3 gradient. Program requirements served: [R4 and R23](phase-5-content-baseline-program.md#behavioral-requirements). Players see nothing new in this slice.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-10-30. The advisor extends this file. The planner absorbs it into Preconditions/Postconditions and Main flow. Cite items as R-numbers. These are **slice-local** R-numbers; program requirements are cited as "program R4" etc.)*

### User stories

- *As a designer*, I want to label a hunting area with the power range it is meant to hold, so the B1 → B3 gradient is a written-down fact rather than something I have to remember.
- *As a designer*, I want a warning when I place a mob or found item in an area whose label it doesn't fit. I want it when I author the rule, on the area I'm looking at, and in the Integrity sweep, so a mis-banded placement is caught while I author at volume and not during playtest.
- *As an administrator in-game*, I want to set, clear and inspect an area's designation with a builder verb, so the telnet surface has the same authoring reach as the editor (INV-18).
- *As the system*, I want to reject a malformed designation at load, and only *warn* about an out-of-designation placement. That way broken data never boots, and authoring is still never blocked by a balance judgement.

### Scope

**In scope:**
- an optional area designation field (one tier plus a band range), authored in YAML, the area editor and an admin verb;
- load/save validation of the field's shape;
- the out-of-designation check, run on every room spawn rule placing a banded mob or item;
- display of the check's findings on the Integrity page, on the area editor and `area` verb (per area), and as a warning when a spawn rule is authored.

**Out of scope (confirmed exclusions):**
- the "You have entered ⟨area⟩" boundary banner (tiered-overworld R2, Stage A);
- the adjacent-tier gradient warning (tiered-overworld R3). Phase 5 is Tier 0 only;
- theme and biome (the theme stays the existing `aspectAffinities`; biome is Phase 6);
- designations in `generate` profiles and generated content (that belongs to the Stage B generator);
- any player-visible use of the designation (`look`, `consider`, `map`);
- *enforcing* a designation (blocking or refusing a placement). It is advisory only;
- cross-tier ranges (for example T0 B3 – T1 B1);
- designating dev-world's hunting areas. They don't exist yet; content drops 6–8 designate them as they are authored.

### Behavioral requirements

**A. The designation**
- **R1.** An area may carry a **designation** made of a **tier** and a **band range** `bandMin..bandMax` within that tier. A single band is written as `bandMin == bandMax`. The designation is **optional**. An area without one is **undesignated**.
- **R2.** A designation is valid when:
  - the tier is in `0..MaxTier`;
  - both bands are in `1..3`;
  - `bandMin ≤ bandMax`.

  A malformed designation is a **fail-fast load error**: boot and `reload` refuse it with a full report, following the same posture as the room spawn-rule shape check (`RoomTemplateDeserializer` throws `ContentShapeException`). *(Corrected at spec review: an earlier draft cited area-affinity normalization, which is a boot-only `ValidateRegistry` error that `reload` does not check.)* Saving a malformed designation from the editor or the admin verb is refused with the reason, and nothing is written.
- **R3.** The designation is world content: it is durable in the area's YAML and never in SQLite (INV-23). An area authored before this slice loads unchanged as undesignated. No migration is needed.

**B. Authoring and inspection**
- **R4.** **Area editor:** the area editor shows and edits the designation (tier, band min, band max, or "none"). Saving goes through the normal validate-then-write path.
- **R5.** **Admin verb:** an admin can set and clear a designation in-game. *(Revised at advisor intake, resolved decision 7 below: this is a **general area-property setter** in the `setitem`/`setmob` family, not a dedicated verb.)* The working shape is `areaset <areaBp> designation <tier> <band>[-<band>]` and `areaset <areaBp> designation clear`. The same verb also sets `name` and `description`, which have no in-game setter today. The verb name is a placeholder the planner may change, but `setarea` is taken (room → area) and there is no `@` sigil. The verb:
  - writes the area YAML;
  - takes effect for the live area **without a `reload`** (template and live entity both updated, INV-21);
  - echoes the resulting designation.

  A bad argument prints the R2 reason and changes nothing. A missing area prints a not-found message.
- **R6.** **`area` verb:** `area [areaBp]` additionally prints the designation, or "undesignated". It also prints the area's current **out-of-designation placements** (R9 format), or a clean line when there are none.
- **R7.** **Area editor findings:** the area editor lists that area's current out-of-designation placements: the same findings as the audit, filtered to this area.

**C. The out-of-designation check**
- **R8.** For every **room spawn rule** whose room belongs to a **designated** area, the check compares the placed mob's or item's **authored** (Tier, Band) against the designation. It warns when:
  - the tier differs; or
  - the band falls outside `bandMin..bandMax`.

  The check is skipped for:
  - rooms in undesignated areas;
  - rooms with no area;
  - placements of **unbanded** (authored `Band = 0`) mobs or items. This matches the band-drift check's exclusion.
- **R9.** Each placement is evaluated on its own. A blueprint placed in several areas yields one finding per offending placement. A finding names:
  - the room;
  - the spawn rule's id;
  - the placed blueprint and its kind (mob or item);
  - the area;
  - the authored (Tier, Band);
  - the area's designation.
- **R10.** The check uses **authored** tags only. Whether an authored tag matches computed power is the existing band-drift check's job; the two checks are independent and compose. A mob that is mis-tagged *and* mis-placed shows up in both.
- **R11.** Findings are **warnings**, consistent with the audit's posture: they are advisory and never a build, reload or CI gate. Content drops must leave them clean (program R23 and the drop checklist).

**D. Where the designer sees findings**
- **R12.** **Integrity page:** out-of-designation findings appear as a new, distinct category beside the band-drift list, with edit links to the room (and so the rule) and the area.
- **R13.** **At authoring time:** when a spawn rule is created or changed and the resulting placement is out of designation, the authoring surface shows a **non-blocking warning** in the R9 format, and the save still succeeds. This covers:
  - the room spawn-rules editor (and the grid editor's room panel that embeds it);
  - the mob and item editors' "Spawns in" panel;
  - the `spawnrule`, `mkmob` and `mkitem` verbs' output.
- **R14.** Changing a designation (R4/R5) changes which placements are findings. The next audit, area view or rule save reflects it, and no reload is needed for the in-game `area` view.

### Edge cases & failure behavior

- **Undesignated area / roomless rule / room with no area:** no check, and no finding (R8). Undesignated is not itself a warning.
- **Unbanded mob or item (Band 0) in a designated area:** skipped, not flagged (R8). Junk found items may legitimately be unbanded.
- **A mob placed in two areas with different designations:** each placement is judged against its own area. One may be clean and the other a finding (R9).
- **A room moved to another area (`setarea`, editor area picker):** its placements are judged against the new area from then on. Authoring-time feedback for a room move is not required (only rule saves, R13); the audit and the area views pick it up.
- **A designation narrowed after content is placed:** existing placements become findings. Nothing is removed or blocked (R11, R14).
- **A mob or item re-tagged (Tier/Band edited):** placements are re-judged on the next audit or area view. Authoring-time feedback on the *mob/item* save is not required.
- **Malformed designation hand-edited into YAML:** boot or `reload` fails fast with the full report (R2). A `reload` is whole-or-nothing, so the running world is unchanged.
- **The dev-world hub:** stays undesignated. It is not a hunting area: it has no hostile spawns, and its shopkeepers can't be targeted (program R3).

### Content & authoring needs

- A designer can author the designation in YAML, the area editor and the admin verb, and inspect it in the area editor, the `area` verb and the Integrity page.
- **Dev-world:** this slice adds no designations (the hub stays undesignated). The content-drop checklist (program slice decomposition, drops 6–8) gains the item **"the drop's hunting area carries its (Tier 0, Band n) designation and the out-of-designation check is clean."**
- A dev-world fixture assertion that each hunting area is designated is a natural drop-time addition. It is not required here, since no hunting area exists yet.

### Grounding notes

- **Program framing (grounded):** the [program design notes](phase-5-content-baseline-program.md#design-notes) fix the designation as a (Tier, Band-range) record shaped for tiered-overworld R2, with the theme staying `aspectAffinities`. The audit check *warns*. Its seam row and slice-decomposition row (slice 3: R4, R23; depends on slice 1) are in the same file. INV-18 there requires editor and admin authoring in the same slice, and INV-23 makes it YAML-only world content.
- **Areas today:** the YAML fields are `name/description/respawnRate/pvp/rooms/aspectAffinities` ([world-content.md](../features/world/world-content.md)). The `area` and `setarea` verbs and the affinity fail-fast validation are described in [area-model.md](../features/world/area-model.md). `mkarea` creates an area; no verb sets area fields ([admin-commands.md](../features/admin-authoring/admin-commands.md)).
- **The audit:** `IBalanceAuditSystem.Audit()` runs authored-vs-computed band drift over item and mob templates. It excludes `Band = 0`, is advisory only, and is surfaced on the Integrity page off the circuit thread ([power-budget-system.md](../features/progression/power-budget-system.md), [content-authoring.md](../features/admin-authoring/content-authoring.md) step 12).
- **Placement:** room spawn rules are the only placement since [spawn-authoring](../roadmap/completed/spawn-authoring.md). The catalog's `SpawnSitesFor` already enumerates every (room, area, rule) site for a blueprint ([content-tooling.md](../features/admin-authoring/content-tooling.md)). The authoring surfaces R13 names were shipped by that slice.
- **Collision noted:** [tiered-overworld R22](tiered-overworld-program.md) says "Tier 0–1 region designations", but the program fixed Phase 5 at **Tier 0** (program resolved decision 4). The field supports any tier; Phase 5 content authors Tier 0 only.
- **Shaped-for-later consumers (not built here):** the Stage A boundary banner and gradient warning; Stage B generator designations and weighted pools; `map` region view.

### Resolved decisions

*(Confirmed by the user during intake on 2026-10-30. Do not relitigate downstream.)*

1. **Shape:** one tier plus a band range within it. Rejected: a cell-to-cell range that can span tiers; a single cell only.
2. **Check scope:** mobs **and** found items placed by room spawn rules, judged by authored Tier/Band; unbanded entries skipped. Rejected: mobs only; flagging unbanded placements.
3. **Feedback:** Integrity page **plus** non-blocking authoring-time warnings on spawn-rule saves. Rejected: Integrity page only.
4. **Per-area view:** the area editor and `area` verb list the area's current findings. Rejected: Integrity page as the only findings surface.
5. **Defaults approved as proposed:**
   - the designation is optional (undesignated means unchecked);
   - the hub stays undesignated;
   - a malformed designation fails fast while an out-of-designation placement only warns;
   - the check uses authored tags (independent of band drift);
   - each placement is judged separately;
   - surfaces: Integrity category, area editor field, `area` verb, a new admin setter verb;
   - the exclusions listed under Scope.

## Preconditions

- Slice 1 ([dev-world-foundation](../roadmap/completed/dev-world-foundation.md)) and slice 2 ([spawn-authoring](../roadmap/completed/spawn-authoring.md)) have shipped. That gives:
  - room spawn rules as the only placement (`RoomTemplate.SpawnRules`, `SpawnRuleValidation`);
  - `IRoomBuilderSystem` spawn-rule mutations and the `SpawnRuleAuthoringOutcome` helper;
  - `ISpawnRuleAuthoring` (`SpawnSitesFor`, `UpsertSpawnRuleAsync`) and the "Spawns in" panel.
- Mob and item templates carry authored `Tier`/`Band`. `Band = 0` means unbanded.
- An area is authored in YAML, the area editor or `mkarea`, and loaded through `AreaTemplateDeserializer` → `AreaTemplate.Apply`. A room's durable area membership is `RoomTemplate.AreaId`, the area's blueprint id ([area-model.md](../features/world/area-model.md)).
- Load-time shape errors go through `ContentShapeException` → `ContentLoadException`, a whole-or-nothing staged read on boot and `reload` ([Flow 1](../architecture/flows/flow-01-server-startup.md), [Flow 5](../architecture/flows/flow-05-content-reload.md)).
- The invoking session satisfies `AdminRequirement` for `areaset`, `area`, `spawnrule`, `mkmob` and `mkitem`.

## Postconditions

*(The coverage contract. Each item maps to a named test in [Test plan](#test-plan--verification).)*

- **P1 — Field and YAML (R1, R3).**
  - `AreaTemplate.Designation` is an optional `AreaDesignation`.
  - The YAML `designation: { tier, bandMin, bandMax }` round-trips through the writer and the deserializer without loss.
  - An area file with no `designation` key loads with `Designation == null`, and the writer emits no `designation` key for it.
- **P2 — Fail-fast shape (R2).** A designation that fails `AreaDesignationValidation` has three outcomes:
  - **Load:** `AreaTemplateDeserializer` throws `ContentShapeException`, so boot and `reload` refuse the whole corpus with the per-file report.
  - **Editor save:** `IContentValidator.Validate(AreaTemplate)` returns the error and the catalog writes nothing.
  - **Admin verb:** `IAreaBuilderSystem.SetAreaDesignation` refuses and changes nothing.

  A designation key missing from the YAML (`tier`, `bandMin` or `bandMax`) is a shape error, never a silent default.
- **P3 — Live component (R3, INV-23).**
  - `AreaTemplate.Apply` attaches an `AreaDesignationComponent` if and only if the template is designated.
  - The component type is not `[Persistent]`.
  - No area entity gains `PersistentEntity`.
- **P4 — The judgement (R8, R10).**
  - `AreaPlacementCheck.Judge` returns a finding only when all of these hold: the site's room has an area, the area is designated, the placed blueprint is a known mob or item, it is banded (`Band ≠ 0`), and its authored (Tier, Band) is not contained in the designation.
  - Every other case returns `null`.
  - It never consults `IPowerBudgetSystem`.
- **P5 — The sweep (R9).**
  - `AreaPlacementCheck.Sweep` yields one finding per offending (room, rule). Each finding carries the room, rule id, blueprint, kind, area, authored (Tier, Band) and designation.
  - Findings are ordered by room blueprint id (ordinal), then rule id.
  - An area filter restricts the findings to rooms whose `AreaId` matches.
- **P6 — Source parity (resolved decision 6).** For identical content, the registry source and the catalog source produce identical finding lists.
- **P7 — Boot report (R11).** `IContentValidator.ValidateRegistry` adds one `Warnings` entry per finding and no `Errors`, so a boot with findings still starts.
- **P8 — The setter (R5, R14, INV-21).**
  - On success, `areaset <area> designation|name|description <value>`:
    - updates the registered `AreaTemplate`;
    - updates the live area entity: `AreaDesignationComponent` is added, replaced or removed, or `AreaComponent.Name`/`Description` is changed;
    - writes the area YAML through `IAreaContentWriter`;
    - publishes exactly one `AreaPropertySetByAdminEvent`;
    - echoes the resulting value.
  - `designation clear` removes both the template field and the component.
  - A bad argument (parse or R2 shape) or an unknown area changes nothing, writes nothing and publishes nothing.
- **P9 — `area` verb (R6, R14).** `area [areaBp]` prints the designation, or `undesignated`. It then prints that area's findings in R9 form, or a clean line. An `areaset` is reflected on the next `area`, with no `reload`.
- **P10 — In-game rule-save warnings (R13).**
  - `IRoomBuilderSystem.AddSpawnRule` / `UpdateSpawnRule` still succeed for an out-of-designation placement, and `SpawnRuleMutationResult.Findings` carries the authored rule's finding. `RemoveSpawnRule` carries none.
  - `spawnrule add|set`, `mkmob` and `mkitem` print each finding as a warning line after their confirmation.
- **P11 — Web rule-save warnings (R13, resolved decision 8).**
  - `UpsertSpawnRuleAsync`, `SaveAsync(room)` and `SaveRoomAsync` write the file, return `Success`, and fold each finding's `Describe()` text into `ContentWriteResult.Warnings`. This applies to every rule that is **new or changed** relative to the room's prior on-disk version.
  - A room save that leaves its rules unchanged adds no designation warning, even if the room moved area.
- **P12 — Web inspection (R4, R7, R12).**
  - `IAreaDesignationAudit.Findings(areaBp?)` sweeps disk through the catalog.
  - The area editor binds the designation (tier, band min, band max, or none) and lists the area's findings.
  - `ContentIntegritySweepService`'s status carries the findings. The Integrity page renders them as a third category, with edit links to the room and the area.
- **P13 — Rename preserves.** Catalog rename (`CloneWithNewId`) carries `Designation` to the new id.
- **P14 — Dev-world.** The hub area stays undesignated, and the whole-world sweep over dev-world yields no findings.

## Main flow

*(The primary designer journey. The boot path is step 10. Refusal branches are noted inline.)*

1. **Designate.** The admin types `areaset area.wilds designation 0 1-2`. `AreaSetCommand` (Initiator) resolves the property token and parses the value with `AreaDesignation.TryParse`. On a parse error it prints the reason and stops.
2. **Mutate.** The command calls `IAreaBuilderSystem.SetAreaDesignation("area.wilds", d)`, which:
   - validates with `AreaDesignationValidation.Errors`, and on a refusal returns an error and changes nothing;
   - sets `AreaTemplate.Designation`;
   - adds or replaces the `AreaDesignationComponent` on the live area entity (INV-21).
3. **Write and audit.** The command writes the template with `IAreaContentWriter.WriteAsync`. It then publishes `AreaPropertySetByAdminEvent`, which `AdminAuditHandler` logs (Notification priority), and echoes `Area 'area.wilds' designation set to Tier 0, Bands 1-2.`
4. **Author a rule in-game.** Standing in a room of `area.wilds`, the admin types `spawnrule add mob.ogre`, where the ogre is tagged T0 B3.
   - `RoomBuilderSystem.AddSpawnRule` commits the rule as today.
   - It then calls `IAreaPlacementSystem.JudgeRule(roomBp, rule)`, which applies `AreaPlacementCheck.Judge` over `RegistryPlacementSource`, and returns the finding in `SpawnRuleMutationResult.Findings`.
5. **Warn and save.** `SpawnRuleCommand` calls `SpawnRuleAuthoringOutcome.CompleteAsync`, which writes the room YAML and publishes `SpawnRuleAuthoredByAdminEvent`, both unchanged. It prints the confirmation, then calls `SpawnRuleAuthoringOutcome.WriteFindingsAsync`, which prints `Warning: <finding.Describe()>`. The rule is saved.
6. **Inspect in-game.** `area area.wilds` → `AreaCommand` reads `AreaComponent` and `AreaDesignationComponent` (live), plus `IAreaPlacementSystem.Findings("area.wilds")`, and prints the designation and the finding list (R6).
7. **Author a rule on the web.** In the mob editor's "Spawns in" panel, the designer adds `mob.ogre` to a `area.wilds` room.
   - `ISpawnRuleAuthoring.UpsertSpawnRuleAsync` → `SaveRoomCoreAsync` validates and writes as today.
   - It then diffs the saved rules against the prior on-disk room. For each new or changed rule, it calls `AreaPlacementCheck.Judge` over a `CatalogPlacementSource` and folds `Describe()` into `ContentWriteResult.Warnings`.
   - The panel renders `Warnings` exactly as it does today.
8. **Edit and inspect the area on the web.** The area editor binds tier, band min, band max, or none to `AreaTemplate.Designation`.
   - Save → `IContentDefinitionCatalog.SaveAsync` → `IContentValidator.Validate` refuses a malformed shape (R2).
   - The page lists `IAreaDesignationAudit.Findings(areaBp)` on load and after each save (R7).
9. **Integrity.** "Run checks" → `ContentIntegritySweepService` runs `SweepBroken`, `Audit` and `IAreaDesignationAudit.Findings()` on its background task. The page renders the third category, "Out-of-designation placements", with room and area edit links (R12).
10. **Boot and reload.**
    - `AreaTemplateDeserializer` throws `ContentShapeException` for a malformed designation, so boot fails and `reload` refuses; the per-file report is unchanged in shape (R2).
    - On a clean boot, `RegistryValidationBootstrap` → `ValidateRegistry` logs one warning per finding and the host starts (R11).
    - `reload` re-derives the component through `Apply`. It does not run `ValidateRegistry`, which is the existing behaviour, see [Planner refinements](#planner-refinements-new-plan).

## Events fired

| Event | Publisher (Initiator) | Subscribers | Payload | New? |
|---|---|---|---|---|
| `AreaPropertySetByAdminEvent` | `AreaSetCommand` | `AdminAuditHandler` (Notification) | `AdminEntityId`, `AreaEntityId` (0 if no live entity), `AreaBlueprintId`, `PropertyName` (`designation`/`name`/`description`), `NewValue` (canonical text, `clear` for removal) | **New**: `Core/Modules/Admin/Events/AreaPropertySetByAdminEvent.cs`. Subscription line added in `Server/Program.cs` beside the other 21 audit subscriptions |
| `SpawnRuleAuthoredByAdminEvent` | `SpawnRuleAuthoringOutcome` (unchanged) | `AdminAuditHandler` | unchanged | Reused. The finding is **not** added to the event (it is advisory output, not an audit fact) |

No gameplay events. No domain or core system publishes (INV-5).

## Systems / handlers involved

| Piece | Kind | Location | New / reused |
|---|---|---|---|
| `AreaDesignation` (record: `Min`/`Max` `PowerBand`, `Of`, `Contains`, `TryParse`, `ToString`) + `AreaDesignationValidation.Errors` | Value type + pure shape check | `Core/Modules/World/AreaDesignation.cs` | **New** |
| `AreaTemplate.Designation`, `AreaTemplateDeserializer`, `AreaContentWriter` | Template field + YAML I/O | `Core/Modules/World/Templates/`, `Core/Modules/World/Systems/AreaContentWriter.cs` | Extended |
| `AreaDesignationComponent` | Live component (world content, **not** `[Persistent]`) | `Core/Modules/World/Components/AreaDesignationComponent.cs` | **New** |
| `AreaPlacementCheck` (`Judge`, `Sweep`) + `PlacementFinding`, `PlacementSite`, `PlacedTag`, `PlacedKind`, `IAreaPlacementSource` | Pure judgement + sweep + source seam | `Core/Modules/World/Systems/AreaPlacementCheck.cs` (+ `IAreaPlacementSource.cs`) | **New** |
| `RegistryPlacementSource` | Adapter: `ITemplateRegistry` → `IAreaPlacementSource` | `Core/Modules/World/Systems/` | **New** |
| `IAreaPlacementSystem` / `AreaPlacementSystem` (`Findings`, `JudgeRule`) | Domain system, registry-backed, stateless | `Core/Modules/World/Systems/`; registered in `WorldModule` beside `IContentValidator` | **New** |
| `IContentValidator` / `ContentValidator` | `Validate(AreaTemplate)` adds the shape errors; `ValidateRegistry` adds step 6, the finding warnings | `Core/Modules/World/Systems/` | Extended. *As built (WP1):* no constructor change. Step 6 calls `AreaPlacementCheck.Sweep(new RegistryPlacementSource(_templateRegistry))` over the registry it already holds (the spec review's suggested alternative), so the same judgement runs and the 8 manual `new ContentValidator(` test sites are untouched |
| `IAreaBuilderSystem` / `AreaBuilderSystem` (`SetAreaName`, `SetAreaDescription`, `SetAreaDesignation` → `AreaPropertyResult`) | Domain system | `Core/Modules/Admin/Systems/` | Extended |
| `IRoomBuilderSystem` / `RoomBuilderSystem` (`SpawnRuleMutationResult.Findings`) | Domain system | `Core/Modules/Admin/Systems/` | Extended (gains an `IAreaPlacementSystem` dependency) |
| `SpawnRuleAuthoringOutcome.WriteFindingsAsync` | Initiator-tier helper | `Core/Modules/Admin/Commands/` | Extended |
| `AreaSetCommand` (`areaset`) | Admin command (Initiator) | `Core/Modules/Admin/Commands/AreaSetCommand.cs`; registered in `AdminModule` | **New** |
| `AreaCommand` (`area`) | Admin command (read-only) | `Core/Modules/Admin/Commands/` | Extended |
| `SpawnRuleCommand`, `MkMobCommand`, `MkitemCommand` | Admin commands | Admin / Mobs / Items `Commands/` | Extended (one `WriteFindingsAsync` call each) |
| `AdminAuditHandler` | Handler (Notification) | `Core/Modules/Admin/Handlers/` | Extended (+ `IEventHandler<AreaPropertySetByAdminEvent>`) |
| `ISpawnRuleAuthoring.AllSpawnSites()` | Lock-free cached read | `Core/Modules/Authoring/Systems/` (implemented by `ContentDefinitionCatalog`) | Extended |
| `CatalogPlacementSource` | Adapter: catalog → `IAreaPlacementSource` (per-sweep memo) | `Core/Modules/Authoring/Systems/` | **New** |
| `IAreaDesignationAudit` / `AreaDesignationAudit` (`Findings(areaBp?)`) | Authoring system, catalog-backed, stateless | `Core/Modules/Authoring/Systems/`; registered in `AuthoringModule` | **New** |
| `ContentDefinitionCatalog` | Rule-save warnings in `SaveCoreAsync` (room) and `SaveRoomCoreAsync`; `CloneWithNewId` carries `Designation` | `Core/Modules/Authoring/Systems/` | Extended |
| `ContentIntegritySweepService` / `IntegritySweepStatus` | Background sweep, gains a third category | `Hedron.Web/Services/` | Extended |
| `AreaEditor.razor`, `Integrity.razor`, `RoomEditor.razor` | Thin Blazor binding | `Hedron.Web/Components/Pages/` | Extended |

### Shapes (signatures the implementer builds against)

```csharp
// Core/Modules/World/AreaDesignation.cs
public sealed record AreaDesignation(PowerBand Min, PowerBand Max)
{
    public const int MinBand = 1, MaxBand = 3;               // same bounds as the mob/item Band tag
    public static AreaDesignation Of(int tier, int bandMin, int bandMax); // no validation; see Errors
    public bool Contains(PowerBand cell);                     // Min <= cell <= Max, (Tier, Band) lexicographic
    public static bool TryParse(string text, out AreaDesignation? designation, out string? error); // "<tier> <band>[-<band>]"
    public override string ToString();                        // "Tier 0, Band 2" | "Tier 0, Bands 1-2"
}
public static class AreaDesignationValidation
{   // tier in 0..AscensionConstants.MaxTier; bands in MinBand..MaxBand; Min.Band <= Max.Band; Min.Tier == Max.Tier
    public static IReadOnlyList<string> Errors(string areaBlueprintId, AreaDesignation designation);
}

// Core/Modules/World/Systems/AreaPlacementCheck.cs (+ IAreaPlacementSource.cs)
public enum PlacedKind { Mob, Item }
public readonly record struct PlacementSite(string RoomBlueprintId, string? AreaBlueprintId, SpawnRule Rule);
public readonly record struct PlacedTag(PlacedKind Kind, int Tier, int Band);
public sealed record PlacementFinding(
    string RoomBlueprintId, string RuleId, string BlueprintId, PlacedKind Kind,
    string AreaBlueprintId, PowerBand Authored, AreaDesignation Designation)
{
    public string Describe(); // R9 one-liner, the single text every surface shows
}
public interface IAreaPlacementSource
{
    IEnumerable<PlacementSite> Sites();                  // every (room, area, rule)
    AreaDesignation? DesignationOf(string areaBlueprintId); // null = undesignated or unknown area
    PlacedTag? TagOf(string blueprintId);                // null = unknown, or not a mob/item
}
public static class AreaPlacementCheck
{
    public static PlacementFinding? Judge(PlacementSite site, IAreaPlacementSource source);
    public static IReadOnlyList<PlacementFinding> Sweep(IAreaPlacementSource source, string? areaBlueprintId = null);
}

// Core/Modules/World/Systems/IAreaPlacementSystem.cs (registry-backed)
public interface IAreaPlacementSystem
{
    IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null);
    PlacementFinding? JudgeRule(string roomBlueprintId, SpawnRule rule);
}

// Core/Modules/World/Components/AreaDesignationComponent.cs  (NOT [Persistent])
public sealed class AreaDesignationComponent : IComponent { public AreaDesignation Designation { get; set; } = null!; }

// Core/Modules/Admin/Systems/IAreaBuilderSystem.cs (additions)
AreaPropertyResult SetAreaName(string areaBlueprintId, string name);
AreaPropertyResult SetAreaDescription(string areaBlueprintId, string description);
AreaPropertyResult SetAreaDesignation(string areaBlueprintId, AreaDesignation? designation); // null = clear
public sealed record AreaPropertyResult(bool Success, string? Error, string AreaBlueprintId, uint AreaEntityId, AreaTemplate? Template);

// Core/Modules/Admin/Systems/IRoomBuilderSystem.cs (change)
public sealed record SpawnRuleMutationResult(bool Success, string? Error, SpawnRule? Rule, string RoomBlueprintId,
    SpawnSyncResult Sync, IReadOnlyList<PlacementFinding>? Findings = null) // null/empty = none; set on Add/Update

// Core/Modules/Authoring/Systems
IReadOnlyList<SpawnSite> ISpawnRuleAuthoring.AllSpawnSites();         // cached room sweep, lock-free
public interface IAreaDesignationAudit { IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null); }
```

**YAML shape** (all three keys are required when `designation` is present; absent = undesignated):

```yaml
id: area.wilds
name: The Wilds
designation:
  tier: 0
  bandMin: 1
  bandMax: 2
```

**Verb grammar:** `areaset <areaBlueprintId> <name|description|designation> <value>`. The schema is `Token`, `Token`, `RestOfLine`, identical to `setmob`.
- `name` and `description` take free text.
- `designation` takes `<tier> <band>` (single band), `<tier> <band>-<band>` (range), or `clear` (alias `none`).

## Implementation plan — work packages

Three packages. WP2 and WP3 depend only on WP1 and touch disjoint files, so they can run in parallel after it. When all three land, the primary agent runs `architecture-reviewer` (code mode) across the combined diff.

### WP1 — World: the designation, the judgement, boot findings

*✅ Implemented 2026-10-31. Build and `dotnet test` are green (1643 tests, 38 new). A malformed designation on a temp copy of dev-world fails the `DevWorld` boot with the per-file `ContentLoadException` report.*

- **Scope:**
  - `AreaDesignation` + `AreaDesignationValidation`.
  - `AreaTemplate.Designation`.
  - Deserializer: nullable-int DTO; a missing key or a failed `Errors` throws `ContentShapeException` naming the area.
  - Writer: emits the key only when designated.
  - `Apply` → `AreaDesignationComponent`.
  - `ContentValidator.Validate(AreaTemplate)`: restructure the `case` so shape errors and affinity errors are both reported.
  - `ContentValidator.ValidateRegistry` step 6 (warnings).
  - `AreaPlacementCheck`, the finding and site types, `IAreaPlacementSource`, `RegistryPlacementSource`, `IAreaPlacementSystem` + `WorldModule` registration.
  - `ContentDefinitionCatalog.CloneWithNewId` carries `Designation`. This is a one-line Authoring touch kept here so rename never drops the field.
  - The dev-world fixture assertion.
- **Files:**
  - `Core/Modules/World/AreaDesignation.cs`;
  - `Core/Modules/World/Components/AreaDesignationComponent.cs`;
  - `Core/Modules/World/Templates/AreaTemplate.cs`, `AreaTemplateDeserializer.cs`;
  - `Core/Modules/World/Systems/AreaContentWriter.cs`, `AreaPlacementCheck.cs`, `IAreaPlacementSource.cs`, `RegistryPlacementSource.cs`, `IAreaPlacementSystem.cs`, `AreaPlacementSystem.cs`, `ContentValidator.cs`, `IContentValidator.cs` (doc comment);
  - `Core/Modules/World/WorldModule.cs`;
  - `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` (`CloneWithNewId` only);
  - tests under `Hedron.Tests/World/` (plus the rename test in `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs`).
  - *As built:* `ContentValidator`'s constructor is unchanged (see the Systems table), so no `new ContentValidator(` site needed editing.
- **Depends on:** nothing.
- **Out of scope:** any command, Blazor or catalog-save change. Do not touch `IBalanceAuditSystem`. Do not add `ValidateRegistry` to `reload`.
- **Exit:** `dotnet build` + `dotnet test` green. The WP1 tests in the Test plan pass, including the dev-world fixture. A hand-edited malformed designation fails `DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server` boot with the per-file report.

### WP2 — Admin / in-game surfaces

*✅ Implemented 2026-10-31. Flow tests cover `areaset`, `area` and the spawn-rule warning lines, and builder tests cover the template and live mutation.*

- **Scope:**
  - `IAreaBuilderSystem.SetAreaName`, `SetAreaDescription`, `SetAreaDesignation`. Each resolves the registered `AreaTemplate`, refuses an unknown one, and updates the live entity when present (`BlueprintComponent` scan, existing debt).
  - `AreaSetCommand` + `AdminModule` registration.
  - `AreaPropertySetByAdminEvent` + `AdminAuditHandler` interface + `Server/Program.cs` subscription.
  - `AreaCommand` designation and findings output.
  - `SpawnRuleMutationResult.Findings`: populated in `RoomBuilderSystem.Commit` for Add/Update via `IAreaPlacementSystem.JudgeRule`, after a successful commit.
  - `SpawnRuleAuthoringOutcome.WriteFindingsAsync(CommandContext, SpawnRuleMutationResult)` writes each finding as `PlainMessage("Warning: " + Describe(), OutputSeverity.System, OutputCategory.System)`.
  - `SpawnRuleCommand`, `MkMobCommand`, `MkitemCommand` each call it after their confirmation.
- **Files:**
  - `Core/Modules/Admin/Systems/IAreaBuilderSystem.cs`, `AreaBuilderSystem.cs`, `IRoomBuilderSystem.cs`, `RoomBuilderSystem.cs`;
  - `Core/Modules/Admin/Commands/AreaSetCommand.cs`, `AreaCommand.cs`, `SpawnRuleAuthoringOutcome.cs`, `SpawnRuleCommand.cs`;
  - `Core/Modules/Mobs/Commands/MkMobCommand.cs`, `Core/Modules/Items/Commands/MkitemCommand.cs`;
  - `Core/Modules/Admin/Events/AreaPropertySetByAdminEvent.cs`, `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`, `Core/Modules/Admin/AdminModule.cs`;
  - `Server/Program.cs` (one `Subscribe` line);
  - `.claude/skills/add-command/SKILL.md` (INV-20). Two changes:
    - the "Spawn-rule mutations" guidance: the command also prints findings through `SpawnRuleAuthoringOutcome.WriteFindingsAsync`;
    - the world-content command list gains `areaset`;
  - tests under `Hedron.Tests/Admin/` and `Hedron.Tests/Spawn/`, including the sites broken by `RoomBuilderSystem`'s new dependency: `Hedron.Tests/Authoring/RoomBuilderSystemTests.cs` and `Hedron.Tests/Spawn/SpawnWorld.cs`.
- **Depends on:** WP1.
- **Out of scope:**
  - `pvp`/`respawnRate` property tokens;
  - warnings on `setarea`, `setmob`/`setitem` tier|band, or `areaset designation` (resolved decision 8);
  - `SaveEntityAsync` anywhere (INV-22: area content is YAML-only).
- **Exit:** `dotnet test` green with the WP2 tests. On DevWorld, `areaset area.hub designation 0 1` → `area` shows it with no `reload` → `areaset area.hub designation clear` restores undesignated. Revert the YAML afterward: the hub stays undesignated (P14).

### WP3 — Authoring + web surfaces

*✅ Implemented 2026-10-31. Smoke-tested in the editor on a temp copy of dev-world: designate `area.hub`, add a Band-3 mob through "Spawns in" (warning shown, rule saved), see it in the area-editor list and the Integrity category. The same mob also appears in the band-drift list, the two checks staying independent (R10).*

- **Scope:**
  - `ISpawnRuleAuthoring.AllSpawnSites()`.
  - `CatalogPlacementSource`, constructed per sweep with a local memo of area and target loads; `TagOf` tries `Load(Mob)` then `Load(Item)`.
    - **Unreadable files are tolerated** (spec review). `ContentDefinitionCatalog.Load` deserializes without a catch.
    - So `DesignationOf` and `TagOf` catch a load failure, log it, and treat the area or target as unknown (`null`). That yields no finding, as `Judge` already does for unknown ids.
    - Without this, a hand-edited malformed area, mob or item would blank the whole Integrity sweep (including the broken-ref list that points at the bad file), throw from the area editor list, and throw from a rule save *after* its write.
  - `ContentIntegritySweepService`'s class comment: "two corpus sweeps" → three.
  - `IAreaDesignationAudit` / `AreaDesignationAudit` + `AuthoringModule` registration.
  - Catalog rule-save warnings: inside `SaveCoreAsync`, when the template is a `RoomTemplate`, and inside `SaveRoomCoreAsync`, capture the prior on-disk room with `Load` **before** writing. After a successful write, judge each rule whose id is absent from the prior room or whose fields differ, and append `Describe()` to `Warnings`. `UpsertSpawnRuleAsync` inherits this through `SaveRoomCoreAsync`.
  - `ContentIntegritySweepService`: the third call; `IntegritySweepStatus` gains `IReadOnlyList<PlacementFinding> PlacementFindings`.
  - `Integrity.razor`: a new category with room and area edit links.
  - `AreaEditor.razor`:
    - a designation fieldset with a "Designated" checkbox, a tier select (`0..MaxTier`) and band-min / band-max selects (`1..3`). It binds `AreaDesignation.Of(...)` or `null`; the select ranges are presentation, and validation stays in the catalog save;
    - a findings list from `IAreaDesignationAudit.Findings(BlueprintId)`, refreshed on load and after save.
  - `RoomEditor.razor`: on the new + bidirectional path, show the union of the create result's and the follow-up `SaveRoomAsync` result's warnings, so the create-time designation warning is not overwritten.
- **Files:**
  - `Core/Modules/Authoring/Systems/ISpawnRuleAuthoring.cs`, `ContentDefinitionCatalog.cs`, `CatalogPlacementSource.cs`, `IAreaDesignationAudit.cs`, `AreaDesignationAudit.cs`;
  - `Core/Modules/Authoring/AuthoringModule.cs`;
  - `Hedron.Web/Services/ContentIntegritySweepService.cs`;
  - `Hedron.Web/Components/Pages/Integrity.razor`, `AreaEditor.razor`, `RoomEditor.razor`;
  - tests under `Hedron.Tests/Authoring/` and `Hedron.Tests/Web/`.
- **Depends on:** WP1.
- **Out of scope:**
  - any change to the authoring JSON API (areas are listing-only there);
  - generator (`ContentGenerationSystem`) designations;
  - warnings on mob/item saves or room re-area.
- **Exit:** `dotnet test` green with the WP3 tests, including the source-parity test. In the editor on DevWorld: designate a scratch area, add an out-of-band rule from "Spawns in", and see the warning and the area-editor list entry; "Run checks" shows the Integrity category. Clean up the scratch content.

## Content tooling impact

*(INV-18: the slice adds world-content state, the designation, and ships authoring and inspection for it on every surface.)*

- **Data-file shape:** `areas/*.yaml` gains an optional `designation: { tier, bandMin, bandMax }`.
  - It is fail-fast on a malformed or partial block (R2).
  - Existing files load unchanged (R3), and no migration is needed.
  - `schemaVersion` stays 1, because the field is additive and optional.
- **`TemplateRegistry`:** no new template kind. `AreaTemplate` gains `Designation`.
- **Admin commands:**
  - **new** `areaset <areaBp> <name|description|designation> <value>`: author and clear the designation in-game, and set name/description, which had no in-game setter;
  - **extended** `area [areaBp]`: shows the designation and the area's findings;
  - **extended** `spawnrule add|set`, `mkmob`, `mkitem`: print warning lines. For `mkmob`/`mkitem` the list is empty today, because a fresh ad-hoc template is unbanded (`Band 0`); they are wired for parity, through the same helper.
- **Editor:** the area editor gains a designation field and a findings list. Integrity gains the "Out-of-designation placements" category. The room spawn-rules editor (including the grid editor's room panel) and the "Spawns in" panel show rule-save warnings through their existing `Warnings` rendering.
- **Authoring JSON API:** no change. Areas are exposed as `ContentSummary` listings only.
- **Generator:** no change. Generated areas stay undesignated (Scope exclusion).
- **Dev-world:** no designation is added, and the hub stays undesignated. The fixture test pins this and the whole-world clean sweep.
- **Program checklist:** the slice PR adds the item *"the drop's hunting area carries its (Tier 0, Band n) designation"* to the content-drop checklist in [phase-5-content-baseline-program.md](phase-5-content-baseline-program.md#slice-decomposition). The checklist's existing "designation check comes back clean" item already covers the check.

## Cross-cutting surfaces stressed

| Surface | Classification | Why |
|---|---|---|
| **Commands** | Adequate | `areaset` uses `Token, Token, RestOfLine`, the exact `setmob`/`setitem` schema. It is **property-keyed**, not a sub-action verb, so it does not count toward the "command sub-action schema support" promotion trigger ([backlog.md](../roadmap/backlog.md)). `Full` matching, `AdminRequirement`. |
| **Output** | Adequate | `PlainMessage` with `System`/`Confirmation`/`Error` severities, as every admin verb uses. The advisory line is `System` severity with a `Warning:` prefix. There is no `Warning` severity today, and one advisory line type does not justify one. |
| **Persistence** | Adequate | No SQLite shape. Area content is YAML-only (INV-23). See the persistence audit below. |
| **Event bus** | Adequate | One `*ByAdminEvent` on the established audit fan-in. The subscription is hand-wired in `Server/Program.cs` like the other 21. PT-1 ([playtest-world-substrate.md](playtest-world-substrate.md)) plans a subscription extraction; whichever lands second rebases the one line. |
| **ECS queries** | Acknowledged debt (existing) | The builder resolves the live area by a `BlueprintComponent` scan, the same O(n) pattern `AreaCommand`/`SetAreaCommand` use. It is already tracked as backlog "Blueprint-scan index (`BlueprintComponent` lookup by blueprint ID)"; this slice adds one call site and no new pattern. Components are queried with `TryGet`/`HasComponent` (INV-4). |
| **Broadcast** | Adequate | None; the output is the admin's own session only. |
| **Time** | Adequate | No time or randomness in any new logic (INV-26 untouched). |
| **Content templates** | Adequate | Template field + deserializer/writer + `Apply` component mirror the `aspectAffinities` path. The "one shape check, three callers" pattern (deserializer / validator / builder) reuses the `SpawnRuleValidation` precedent rather than inventing a fourth shape. |
| **Validation reporting** | Adequate | Findings join the existing warn-not-error carriers (`ValidationReport.Warnings`, `ContentWriteResult.Warnings`, `SpawnRuleMutationResult`), with no new report type. `PlacementFinding.Describe()` is the single text, so no two surfaces word it differently. |
| **Configuration** | Adequate | No new keys. Bounds are `AscensionConstants.MaxTier` (the constant the standards store pins `maxTier` to) and band `1..3`. That band bound is the same one the mob/item `Band` tag uses (`setmob`/`setitem` 0–3), so the designation cannot hold a band the tags it judges cannot. |
| **Sessions** | Adequate | Not touched. |
| **Modules** | Adequate | No new module. Admin → World and Authoring → World references already exist (`RoomCoordinateCollisions` precedent). The judgement sits in World, so no cycle is introduced. **One new module reference:** `AreaDesignationValidation` reads `AscensionConstants.MaxTier`, so World → Ascension is new. It is a domain → domain reference, acyclic (Ascension does not reference World) and legal (INV-1/INV-2). |
| **Web host / Blazor** | Adequate | `ContentIntegritySweepService` gains a third call inside its existing progress-only background task. The [08-blazor.md](../architecture/08-blazor.md) promotion trigger (queue/progress/cancel) is **not** fired. Components bind only: every rule and the "changed rule" diff live in Core (INV-8 Blazor extension). |
| **Authoring catalog** | Adequate | One additive lock-free read (`AllSpawnSites`) over the already-cached room sweep. Rule-save warnings ride the existing `Warnings` channel, so no editor needs new rendering. |

**Persistence opt-in audit (INV-14 / INV-22 / INV-23):**
- **Level 1, entity domain:** area entities are **world content**: fresh-spawned from YAML and never `PersistentEntity`. This slice adds no construction path; the builder mutates existing area entities. No domain transition.
- **Level 2, components:**
  - `AreaDesignationComponent` is **not** `[Persistent]`: it sits on world content, and its durable form is the YAML field.
  - `AreaComponent` (name/description mutated by the setter) is already non-`[Persistent]`, which is correct.
  - No other component is touched.
- **Level 3, save-on-change:** no `SaveEntityAsync` anywhere. The setter's durability is the YAML write through `IAreaContentWriter`. That is a content write, not an INV-22 entity save, so it is outside the admin-boundary-save case.

## Concurrency posture (INV-31)

- **No new thread, timer or background initiator.**
- **Server:**
  - `areaset`, `area` and the rule-save judgement run on the existing session-command path.
  - `ValidateRegistry` runs once in `RegistryValidationBootstrap.StartAsync`.
  - `AreaPlacementSystem` and `RegistryPlacementSource` are stateless.
  - The builder's template + component mutation stays on the session path, the same as `setmob`.
- **Web:**
  - `IAreaDesignationAudit` is stateless. It reads the catalog through its lock-free cached reads (`AllSpawnSites`, `Load`), which are already safe for concurrent circuits and request threads (thread-local deserializers).
  - It is called from circuit threads (area editor) and from the Integrity background task, the existing declared posture of `ContentIntegritySweepService`.
  - `CatalogPlacementSource`'s memo is local to one sweep, so it is confined.
  - Rule-save warnings are computed **inside** the catalog's write gate, using only `Load` (a read, not a re-entrant mutator).
- **No shared mutable singleton state added.**

## Flows introduced or modified

*(INV-17: the implementation PR updates each flow below, body and mermaid, plus the [flows index](../architecture/flows/README.md) "added" column.)*

- **[Flow 8, Admin authoring journey](../architecture/flows/flow-08-admin-room-creation.md)**:
  - add `areaset` (builder → YAML write → `AreaPropertySetByAdminEvent` → audit) as a leg;
  - note the `area` output gaining designation + findings;
  - add the `spawnrule`/`mk*` warning step after `SpawnRuleAuthoringOutcome.CompleteAsync`;
  - add `areaset` to the index row's title list.
- **[Flow 29, Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)**:
  - offline edit: area editor designation field + findings list; catalog rule-save warnings (diff vs. prior on-disk room → `AreaPlacementCheck.Judge` → `Warnings`);
  - Integrity: third sweep category.
- **[Flow 1, Server startup](../architecture/flows/flow-01-server-startup.md)**:
  - step 4: a malformed area designation joins the `ContentShapeException` list;
  - registry validation logs out-of-designation warnings, which never fail boot.
- **[Flow 5, Content reload](../architecture/flows/flow-05-content-reload.md)**: step 3 lists a malformed area designation among the shape errors. There is no new step: `Apply` re-derives the component through the existing re-spawn (step 6).
- **No new canonical flow.** This slice adds no recurring runtime path beyond these legs.

**Living-doc diffs at close-out** (for `sync-roadmap`):
- [area-model.md](../features/world/area-model.md): designation + component + `area`/`areaset`;
- [world-content.md](../features/world/world-content.md): YAML field list;
- [admin-commands.md](../features/admin-authoring/admin-commands.md): `areaset`, `area`;
- [content-authoring.md](../features/admin-authoring/content-authoring.md) / [content-tooling.md](../features/admin-authoring/content-tooling.md): editor, Integrity, rule-save warnings;
- [spawn-system.md](../features/world/spawn-system.md): warnings on rule authoring;
- [power-budget-system.md](../features/progression/power-budget-system.md): Integrity now carries a sibling check that is *not* the band-drift audit;
- [08-blazor.md](../architecture/08-blazor.md): "two corpus sweeps" → three;
- reference catalogs: `systems.md` (`IAreaPlacementSystem`, `IAreaDesignationAudit`, `AreaPlacementCheck`, `IContentValidator` + `IAreaBuilderSystem` rows), `components.md` (`AreaDesignationComponent`), `handlers.md` (`AdminAuditHandler` subscription list);
- the program doc's drop checklist (item added at the code gate);
- `docs/reference/commands.md`: `areaset`, and `area`'s new output (code-gate finding);
- `docs/architecture/03-events.md`: the `AreaPropertySetByAdminEvent` catalog row beside the sibling `*ByAdminEvent`s (code-gate finding);
- `reference/systems.md`: the `IRoomBuilderSystem` row (`SpawnRuleMutationResult.Findings`) and `ISpawnRuleAuthoring.AllSpawnSites` (code-gate finding);
- `08-blazor.md`: the area editor's findings list runs `IAreaDesignationAudit.Findings` synchronously on the circuit thread (a cold catalog index sweeps the rooms directory). Record it in the off-circuit trigger's inventory (code-gate note);
- `.claude/skills/add-command/SKILL.md` (INV-20; done in WP2) and, optionally, one line in `.claude/skills/add-core-system/SKILL.md`: an advisory content check can be a pure judgement over a source seam that both the registry and the catalog implement.

## Test plan / Verification

*(INV-25. Tiers per [07-testing.md](../architecture/07-testing.md). Every postcondition maps to at least one test.)*

**WP1**

| Tier | Target | Asserts | Covers |
|---|---|---|---|
| system-unit | `AreaDesignationTests` | `Contains`: in range, below, above, other tier → false; single band. `TryParse`: `0 2`, `0 1-2` succeed; `x`, `0`, `0 1-`, `0 a-b` fail with a reason. `ToString`: singular vs. range | P4 |
| system-unit | `AreaDesignationValidationTests` | Tier −1 and `MaxTier+1`, band 0 and 4, `bandMin > bandMax`, and cross-tier `Min`/`Max` → each yields an error; a valid designation → none | P2 |
| system-unit | `AreaTemplateDeserializerTests` (extended) | Designation parsed; absent → `null`; each R2 violation and each missing key → `ContentShapeException` naming the area | P1, P2 |
| system-unit (content round-trip, temp dir) | `AreaContentWriter` ↔ `AreaTemplateDeserializer` | Designated round-trips equal; undesignated writes no `designation` key and reloads `null` | P1 |
| system-unit | `AreaTemplate.Apply` | Component present iff designated, carrying the template's value. `AreaDesignationComponent` has no `[Persistent]` attribute; the area entity has no `PersistentEntity` | P3 |
| system-unit | `AreaPlacementCheckTests` | `Judge`: undesignated, no-area room, unknown area, unknown blueprint, unbanded, in-range → `null`; band above or below, other tier → finding with every R9 field. `Sweep`: one finding per offending rule; a blueprint in two areas → one clean, one finding; area filter; deterministic order | P4, P5 |
| system-unit | `ContentValidatorTests` (extended) | `Validate(AreaTemplate)` malformed → error, and affinity errors are still reported alongside; `ValidateRegistry` with an offending placement → one warning, `IsValid` true | P2, P7 |
| system-unit | `ContentDefinitionCatalogTests` (rename) | Renaming a designated area keeps `Designation` | P13 |
| flow (fixture) | `DevWorldFixtureTests` (extended) | `area.hub` has `Designation == null`; `AreaPlacementSystem.Findings()` over the booted registry is empty | P14 |

**WP2**

| Tier | Target | Asserts | Covers |
|---|---|---|---|
| system-unit | `AreaBuilderSystemTests` (extended) | `SetAreaDesignation` updates template + live component; `null` removes both; malformed → refused, template and component unchanged; unknown area → refused. `SetAreaName`/`Description` update template + `AreaComponent` | P8 |
| system-unit | `RoomBuilderSystem` spawn-rule tests | Add/Update of an out-of-designation banded mob → `Success` and one finding; in-range → none; `Remove` → none; undesignated area → none | P10 |
| flow | `AreaSetCommandFlowTests` | `designation 0 1-2` → template, live component and YAML file updated, one `AreaPropertySetByAdminEvent`, confirmation. `clear` → removed. Bad parse / bad shape / unknown area → error output, file unchanged, no event. `name`/`description` → template + `AreaComponent` + YAML | P8 |
| flow | `AreaCommand` flow | After `areaset`, output contains the designation and the offending rule id; undesignated → contains `undesignated` (substring, not exact prose) | P9 |
| flow | `SpawnRuleCommandFlowTests` (extended) | `spawnrule add` of an out-of-designation mob → rule in the room YAML, `SpawnRuleAuthoredByAdminEvent` published, output contains a `Warning:` line naming the rule id | P10 |

**WP3**

| Tier | Target | Asserts | Covers |
|---|---|---|---|
| system-unit (temp dir) | `ContentDefinitionCatalogTests` (extended) | `UpsertSpawnRuleAsync` out of designation → `Success`, file written, `Warnings` has the finding. `SaveAsync(room)` with rules unchanged (including an `AreaId` change) → no designation warning. `SaveRoomAsync` with one changed rule → exactly that rule warned | P11 |
| system-unit (temp dir) | `AreaDesignationAuditTests` | Findings over disk; area filter; reflects a designation saved through the catalog without restart | P12 |
| system-unit (temp dir) | `CatalogPlacementSource` tolerance | A malformed area file and a malformed mob file on disk → sweep completes, no finding for the affected placements, no throw; a rule save naming the malformed mob succeeds with no designation warning | P11, P12 |
| system-unit (temp dir) | `PlacementSourceParityTests` | One temp corpus booted through `WorldContentLoader` into a registry **and** read through the catalog → `RegistryPlacementSource` and `CatalogPlacementSource` sweeps are equal | P6 |
| system-unit | `ContentIntegritySweepServiceTests` (extended) | A completed status carries `PlacementFindings`; a failure leaves it empty | P12 |

**Not tested (and why):**
- Blazor markup (the area editor fieldset, the Integrity category table, edit links) and the `RoomEditor` warning union: thin presentation binding, with all logic in Core (07-testing skip rule).
- Exact prose of `Describe()`, the echo lines and the audit log line: presentation. Tests assert fields and substrings instead.
- The `mkmob`/`mkitem` warning line: these always yield an empty list today (fresh templates are unbanded). They share `WriteFindingsAsync` with `spawnrule`, which is tested.
- `AdminAuditHandler` (untested today; the on-touch ratchet would normally pull it in): the new `IEventHandler<AreaPropertySetByAdminEvent>` is a log-only line in the handler's existing shape, with no branching. It is skipped under the 07-testing log-only rule. The `AreaSetCommandFlowTests` assertion that the event is published covers the subscription's input.
- No persistence round-trip and no HTTP-integration test: there is no `[Persistent]` shape, and the authoring JSON API is unchanged.

**Testability:** no un-injected seam. The judgement and the sweep are pure and take their content source as a parameter. No randomness or clock is involved (INV-26).

## Design notes

*(Durable seam rationale. These survive disintegration into the living docs.)*

- **The check is a content-consistency rule, not a balance computation.** It compares two *authored* facts: the area's designation and the placed blueprint's Tier/Band. It never calls `IPowerBudgetSystem`.
  - It therefore lives in `Core/Modules/World` beside the other warn-only authored-data checks in `IContentValidator`: room-coordinate collisions, and "spawn rule names a registered template" ([systems.md](../reference/systems.md#icontentvalidator)).
  - It does **not** live in `IBalanceAuditSystem`, which the [program brief](phase-5-content-baseline-program.md#seams-and-homes) first named. That system owns authored-vs-*computed* drift. Keeping the two apart is what makes R10 ("independent checks that compose") structural rather than a convention.
  - The Integrity page still shows both categories side by side (R12); only their homes differ.
- **One judgement, many callers (INV-19).** There is one pure function: given a designation (or none) and a placed blueprint's authored (Tier, Band), it returns a finding or nothing.
  - It sits beside the designation type, so World, Admin and Authoring share it without a module cycle. The precedent is `SpawnRuleValidation`, the one rule-shape check that the deserializer, validator and builder all call ([spawn-system.md](../features/world/spawn-system.md)).
  - A sweep built on it enumerates (room → area → rule → blueprint) placements and yields the R9 findings.
  - Every surface calls the sweep or the judgement: the boot validator report (boot-only, see [Planner refinements](#planner-refinements-new-plan)), the `area` verb, the area editor list, the Integrity category, and the rule-save warnings. None re-derives the rule, so no two surfaces can disagree on what counts as out of designation.
- **The source follows the surface (resolved decision 6).** The sweep is parameterized by its content source, not bound to one.
  - On the Server, in-game verbs read the **template registry**, because admin verbs mutate the registry and the live entity (INV-21).
  - On the web host, editors and Integrity read **disk through `IContentDefinitionCatalog`**, because that is what the editors write. This avoids the boot-time-registry staleness the drift audit has ([power-budget-system.md](../features/progression/power-budget-system.md), template conformance).
  - So each surface agrees with what the designer just changed. The judgement is identical across sources.
- **Shape: a range of (Tier, Band) cells, stored as min/max cells.** The YAML stays "tier + band range" (requirements resolved decision 1). Internally the designation is a `min`/`max` pair of `PowerBand` cells.
  - Containment uses lexicographic (Tier, Band) order. That is the same total order the drift check's `GlobalBandIndex` induces, without depending on BalanceInspection (see [Planner refinements](#planner-refinements-new-plan)).
  - Phase 5 adds one validation rule: both cells share a tier.
  - **Shaped for later:** cross-tier ranges (tiered-overworld borders) are a deletion of that rule plus a YAML widening, not a reshape.
  - Expressing it in oracle cells also means a designation maps to a power window through `TargetRange`. That is the "cell anchor" input the program's relative-power read is shaped to accept, for Stage A entrance telegraphs and banners.
- **The designation is live state, not template-only.** `AreaTemplate.Apply` attaches it as a component on the area entity when present, mirroring `aspectAffinities` → `AspectAffinitiesComponent` ([area-model.md](../features/world/area-model.md)).
  - **Why now:** INV-21 already requires the admin setter to update the live entity, and the `area` verb reads live state.
  - **Shaped for later:** the Stage A boundary banner, `map` region view and entrance telegraph all read the *live* area.
  - The component is world content: never `[Persistent]`, never `PersistentEntity` (INV-23 / INV-14).
- **The admin setter is general (resolved decision 7).** Areas are the only content kind with no in-game property setter. Items and mobs have `setitem`/`setmob <bp> <property> <value>`, and rooms have `set`. The setter lands as the area member of that family, with `designation`, `name` and `description` properties, rather than as a one-off `designate` verb.
  - Further area properties (`pvp`, `respawnRate`, the backlogged "area-level properties enforcement") join as property tokens, not new verbs.
  - It follows the established mutation path: builder system mutates template + live component → the command writes YAML → publishes an `*ByAdminEvent` for `AdminAuditHandler`.

### Planner refinements (new-plan)

*(These refine two advisor calls against the code. Neither changes a resolved decision.)*

- **Containment uses (Tier, Band) lexicographic order, not a `GlobalBandIndex` call.** This refines the advisor call "containment over `GlobalBandIndex`".
  - `GlobalBandIndex` is an *instance* method on `PowerBudgetTunables` (core, `Core/Systems/`). The instance is a DI singleton that `BalanceInspectionModule` composes from the standards file. A static World judgement that the deserializer also calls has no way to receive that instance.
  - For in-range bands, lexicographic (Tier, Band) order is the **same total order** `GlobalBandIndex` induces, so `Contains` keeps the semantics and the cross-tier-ready shape.
- **Findings are boot-only in `ValidateRegistry`, not boot + reload.** This narrows the advisor call "boot/`reload` findings are a warning category in `ValidateRegistry`".
  - In code, `ValidateRegistry` runs only from `RegistryValidationBootstrap` (host start). `reload` stages the corpus (shape errors) but never runs it, so the coordinate-collision and unknown-blueprint warnings are also boot-only today.
  - This slice keeps that posture. R2's fail-fast is fully covered on reload by the deserializer, and R14's "next audit / area view" is served by `area` and Integrity.
  - Making `reload` print registry warnings would change all three warning categories at once, which is a separate change.
- **`SpawnRuleValidation` lives in `Core/ECS/Components`**, not World, beside `SpawnRule`. The precedent still holds: one shape check with three callers. `AreaDesignationValidation` lives beside `AreaDesignation` in World, because the area type is World-owned.
- **What counts as a "rule save" on the web** is a diff against the prior on-disk room: a rule whose id is new, or whose blueprint, count or delay changed.
  - This is how decision 8 ("spawn-rule saves only") is honoured on the room editor, whose single Save also carries non-rule edits: a name edit, or a re-area with untouched rules, warns nothing.
  - The diff lives in the catalog (Authoring, Core), not in a component.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Seams and homes

| Seam | Kind | Home / layer | Disposition |
|---|---|---|---|
| Designation value (min/max `PowerBand`, same-tier rule, `Contains`) + shape validation | Value type + pure helper | `Core/Modules/World` (domain; references core `PowerBand`, domain → core is legal, INV-2) | **Build now**; cross-tier **shaped for later** |
| `AreaTemplate.Designation` + deserializer/writer + live area component via `Apply` | Template field + component | `Core/Modules/World` | **Build now** |
| Malformed designation | Fail-fast shape error (R2) | Area deserializer (throws at load, as room spawn-rule shape does); `IContentValidator.Validate(AreaTemplate)` refuses a save; the builder refuses an admin mutation | **Build now** |
| Placement judgement + placement sweep over a content source | Pure function + domain-tier sweep | `Core/Modules/World`, beside the type; the sweep takes a source abstraction (registry or catalog) | **Build now** (one rule, every surface) |
| Boot/`reload` findings | Warning category in the `IContentValidator.ValidateRegistry` report | World; logged, never fails boot (R11) | **Build now** |
| Web findings: Integrity category, area editor list, rule-save warnings | Catalog-sourced sweep; warnings on the rule-save results | `Core/Modules/Authoring` (catalog side) + `Hedron.Web` (thin rendering, INV-8 as extended to Blazor) | **Build now** |
| In-game findings: `area` verb, `spawnrule`/`mkmob`/`mkitem` warning lines | Registry-sourced sweep/judgement surfaced by initiators | `Core/Modules/Admin`; the judgement comes from World, never written into a command body (INV-8) | **Build now** |
| General area-property setter (`designation`, `name`, `description`) | Admin command + builder operation + `*ByAdminEvent` | `Core/Modules/Admin` (`IAreaBuilderSystem` gains the mutation; the command writes via `IAreaContentWriter`) | **Build now**; further properties **shaped for later** |
| Authoring-time warnings on non-rule edits (`setarea`, `setmob`/`setitem` tier\|band, designation saves) | — | — | **Rejected** (resolved decision 8); the R6/R7/R12 views cover them |
| Boundary banner, gradient warning, generator designations, `map` region view | Consumers of the live designation | Tiered-overworld Stage A/B/E | **Shaped for later**; already tracked in [tiered-overworld-program.md](tiered-overworld-program.md), no new backlog entry |

### Family dispositions

- **Designation is an instance of "area-level properties."** It is the first of them with consumers. The general setter makes the family's authoring surface general now. *Enforcement* of the other properties (`pvp`, `respawnRate`) stays in its existing backlog entry ([backlog.md](../roadmap/backlog.md), "Area-level properties enforcement").
- **The check is an instance of warn-not-error authored-data consistency rules**, a family with two members today (coordinate collisions, unknown spawn-rule blueprint). It joins that family rather than starting a new one.
- **The (Tier, Band) cell-range is the region-tier substrate** that tiered-overworld R2 pre-adopts. It is keyed by oracle cells so its later consumers reach it through the same vocabulary as items and mobs.

### Observers, contributors, ordering

- **No gameplay events.** One admin audit event for the setter, on the established `*ByAdminEvent` → `AdminAuditHandler` fan-in (precedents `RoomAreaAssignedByAdminEvent`, `SpawnRuleAuthoredByAdminEvent`). The planner picks the name, for example `AreaPropertySetByAdminEvent`.
- **No contributors.** The designation feeds no score or power, and adds no `IEffectContributor` (INV-24 untouched).
- **Ordering:**
  - **Boot.** The findings need area, room and mob/item templates all registered. They run inside `ValidateRegistry`, which already runs after world content is ready (`RegistryValidationBootstrap` ordering, [Flow 1](../architecture/flows/flow-01-server-startup.md)).
  - **Reload.** `reload` re-derives the live component through `Apply`, the same as affinities. Nothing new is ordered on a shared event.

### Invariants in tension

- **INV-8:** the rule-save warnings in `spawnrule`/`mkmob`/`mkitem` must come from the World judgement, surfaced through the builder result or the shared `SpawnRuleAuthoringOutcome` helper. A command must not compare tiers and bands itself. The same holds for Blazor components (the INV-8 extension in [content-authoring.md](../features/admin-authoring/content-authoring.md)).
- **INV-18:** authoring and inspection must all ship in this slice: the YAML field, the area editor field, the general setter verb, and the `area` verb / Integrity / editor findings.
- **INV-19:** one judgement, five surfaces. The general setter avoids a fourth hand-rolled property setter shape. The Integrity page gains a third sweep category; the planner checks whether that crosses the `ContentIntegritySweepService` generalization note in [08-blazor.md](../architecture/08-blazor.md).
- **INV-21:** the setter updates the area template and the live area entity, both `AreaComponent` (name/description) and the designation component.
- **INV-23 / INV-14:** the designation is world content, YAML only, never `[Persistent]`.
- **INV-25:** tests are needed for:
  - the judgement (in/out of range, unbanded skip, undesignated skip, tier mismatch);
  - shape validation (fail-fast deserializer, save refusal);
  - the sweep over both sources, giving identical findings for identical content;
  - the setter's template + live + YAML mutation;
  - a dev-world fixture assertion that the hub stays undesignated with the check clean.
- **INV-31:**
  - **Web.** Findings run off the circuit thread via `ContentIntegritySweepService` for Integrity. The editor and rule-save paths use the catalog's existing lock-free cached reads.
  - **Server.** In-game reads and the setter's mutation run on the session-command path. No new thread or timer; exposure is not widened.
- **INV-17:** flows touched:
  - the admin authoring journey (the setter, and the `area` output);
  - the content-tooling journey (flow-29: area editor, Integrity category, rule-save warnings);
  - flow-01 (boot validation warning category);
  - flow-05 (reload re-derives the component).

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-10-30. Do not relitigate. Numbered after the requirements-tier decisions 1–5 above.)*

6. **Content source:** the one judgement is fed by whatever each surface edits. In-game reads the registry; web editors and Integrity read disk via the catalog. Rejected: registry everywhere (web warnings stale until Apply); disk everywhere (breaks the in-game mutation pattern).
7. **Admin verb:** a **general area-property setter** in the `setitem`/`setmob` family, with `designation`, `name` and `description` now. Rejected: a designation-only `designate` verb; a general verb shipping only `designation`.
8. **Authoring-time warnings:** on **spawn-rule saves only** (R13 as written). Room re-area, mob/item re-tag and designation edits surface through the `area` verb, the area editor list and Integrity. Rejected: warnings on every placement-changing edit.

**Advisor calls** *(cheap to veto at planning)*:
- the check's home is World (content consistency), not BalanceInspection (balance computation);
- the designation is stored as min/max `PowerBand` cells with a same-tier rule;
- the designation is attached as a live area component via `Apply`;
- boot findings are a warning category in `ValidateRegistry`'s report (narrowed from boot + reload at planning; see Planner refinements).

## Open questions

*(All resolved at `/new-plan`, 2026-10-30. None is a user-level fork.)*

- ~~**Verb name and argument grammar.**~~ **Resolved: `areaset <areaBlueprintId> <name|description|designation> <value>`.**
  - The `set<kind>` slot is taken: `setarea` assigns a room to an area. Renaming that shipped verb is out-of-scope churn.
  - `areaset` sits beside `area` (inspect) in the admin listing. The add-command skill has no naming rule beyond `Full` matching for admin verbs, which removes any prefix-collision risk with `area`.
  - The grammar is `setmob`'s exact `Token, Token, RestOfLine` schema. The `designation` values are `<tier> <band>`, `<tier> <band>-<band>`, or `clear` / `none`.
  - Parsing lives in `AreaDesignation.TryParse` (World), so the command holds no grammar logic.
- ~~**Rule-save warning delivery.**~~ **Resolved: on the existing results, computed in Core.** No command or component compares tiers and bands.
  - **In-game:** `SpawnRuleMutationResult` gains `Findings`, which `RoomBuilderSystem.Commit` fills on Add/Update through `IAreaPlacementSystem.JudgeRule`. The three commands print them through one shared helper, `SpawnRuleAuthoringOutcome.WriteFindingsAsync`.
  - **Web:** the catalog folds `PlacementFinding.Describe()` into the existing `ContentWriteResult.Warnings` for new or changed rules (diff vs. prior on-disk room) in `SaveCoreAsync` (room) and `SaveRoomCoreAsync`. `UpsertSpawnRuleAsync` inherits it. Every editor already renders `Warnings`; the only Blazor change is `RoomEditor`'s create + bidirectional union.
- ~~**Source abstraction shape.**~~ **Resolved: one small World read interface, `IAreaPlacementSource`, with two thin adapters.**
  - The interface has three members: `Sites()`, `DesignationOf(areaBp)` and `TagOf(blueprintId)`.
  - The adapters are `RegistryPlacementSource` (World) and `CatalogPlacementSource` (Authoring).
  - Neither `ITemplateRegistry` nor `IContentDefinitionCatalog` implements it directly, so neither widens.
  - Each host gets a stateless facade: `IAreaPlacementSystem` (World, registry) for Server callers and `IAreaDesignationAudit` (Authoring, catalog) for web callers.
  - The catalog needs one additive read, `ISpawnRuleAuthoring.AllSpawnSites()`, over its already-cached room sweep.
- ~~**Does the general setter need `pvp`/`respawnRate` now?**~~ **Resolved: no.**
  - Resolved decision 7 fixed the property set at `designation`/`name`/`description`.
  - The two are not free: each needs a builder method, a value rule and tests, and nothing consumes them yet.
  - They join as property tokens with the backlogged "Area-level properties enforcement" slice.

## Related

- [phase-5-content-baseline-program.md](phase-5-content-baseline-program.md): the program (R4, R23); slice-decomposition row 3; drop checklist.
- [tiered-overworld-program.md](tiered-overworld-program.md): R2/R22 (pre-adopted here) and the Stage A/B consumers of the live designation.
- [spawn-authoring](../roadmap/completed/spawn-authoring.md): rule-keyed placement, `SpawnRuleAuthoringOutcome`, the "Spawns in" panel.
- [dev-world-foundation](../roadmap/completed/dev-world-foundation.md): the dev-world baseline and fixture tests.
- [playtest-world-substrate.md](playtest-world-substrate.md): PT-1's subscription extraction touches the same `Server/Program.cs` subscription list.
- Features:
  - [area-model.md](../features/world/area-model.md)
  - [world-content.md](../features/world/world-content.md)
  - [spawn-system.md](../features/world/spawn-system.md)
  - [admin-commands.md](../features/admin-authoring/admin-commands.md)
  - [content-authoring.md](../features/admin-authoring/content-authoring.md)
  - [content-tooling.md](../features/admin-authoring/content-tooling.md)
  - [power-budget-system.md](../features/progression/power-budget-system.md)
- Architecture:
  - [checklist.md](../architecture/checklist.md) (INV-8, 14, 17–19, 21–23, 25, 31)
  - [07-testing.md](../architecture/07-testing.md)
  - [08-blazor.md](../architecture/08-blazor.md)
- Flows:
  - [Flow 1](../architecture/flows/flow-01-server-startup.md)
  - [Flow 5](../architecture/flows/flow-05-content-reload.md)
  - [Flow 8](../architecture/flows/flow-08-admin-room-creation.md)
  - [Flow 29](../architecture/flows/flow-29-bulk-content-generation.md)
