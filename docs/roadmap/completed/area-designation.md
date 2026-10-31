# Phase 5 slice 3 — Area designation (completed)

> Implemented on branch `claude/area-designation-slice-3-393e13`, 2026-10-31. Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (R4, R23; the "Area designation" seam). Living docs: [`area-model.md`](../../features/world/area-model.md#area-designation) · [`world-content.md`](../../features/world/world-content.md) · [`admin-commands.md`](../../features/admin-authoring/admin-commands.md) · [`content-authoring.md`](../../features/admin-authoring/content-authoring.md) · [`content-tooling.md`](../../features/admin-authoring/content-tooling.md#iareadesignationaudit--out-of-designation-findings-on-disk).

## Outcome

An area can now carry an optional **designation**: one tier plus a band range within it (for example "Tier 0, Bands 1-2"), the (Tier, Band) cells the area is meant to hold. A room spawn rule placing a banded mob or found item whose *authored* (Tier, Band) falls outside its area's designation is an **out-of-designation finding**, always a warning, never an error. One pure judgement (`AreaPlacementCheck`) feeds every surface:
- the boot validation report;
- the admin `area` verb;
- warning lines from `spawnrule`/`mkmob`/`mkitem`;
- the area editor's findings list;
- a new Integrity-page category;
- non-blocking warnings on web spawn-rule saves.

The designation is authored in YAML, in the area editor, and through the new general admin area setter `areaset`. A malformed designation fails fast at load and is refused at save. This is the Phase 5 pre-adoption of tiered-overworld R2/R22 and the gradient guard for content drops 6–8.

## Behavior digest

**Preconditions (as specified).**
- Slices 1 and 2 had shipped. Room spawn rules were the only placement, and `IRoomBuilderSystem`, `SpawnRuleAuthoringOutcome`, `ISpawnRuleAuthoring` and the "Spawns in" panel existed.
- Mob and item templates carry an authored `Tier`/`Band`; `Band = 0` means unbanded.
- A room's durable area membership is `RoomTemplate.AreaId`.
- Shape errors go through `ContentShapeException` → `ContentLoadException`, a whole-or-nothing staged read on boot and `reload`.

**Postconditions (as specified).**
1. **P1, field and YAML.** `AreaTemplate.Designation` (optional) round-trips as `designation: { tier, bandMin, bandMax }`. An absent key means undesignated, and the writer omits the key for an undesignated area.
2. **P2, fail-fast shape.** A designation is malformed when its tier is outside `0..MaxTier`, a band is outside `1..3`, `bandMin > bandMax`, the cells span tiers, or a key is missing. It is refused in three places:
   - the area deserializer throws, so boot fails and `reload` refuses;
   - `IContentValidator.Validate(AreaTemplate)` refuses an editor save;
   - `IAreaBuilderSystem.SetAreaDesignation` refuses an admin mutation, changing nothing.
3. **P3, live component.** `AreaTemplate.Apply` attaches `AreaDesignationComponent` if and only if the area is designated. The component is not `[Persistent]`, and area entities never gain `PersistentEntity`.
4. **P4, the judgement.** It returns a finding only when all of these hold: the room has an area, the area is designated, the placed blueprint is a known mob or item, it is banded, and its authored cell is outside the designation. It never consults the power oracle.
5. **P5, the sweep.** One finding per offending (room, rule), ordered by room blueprint id then rule id. It can be filtered to one area (case-insensitive).
6. **P6, source parity.** The registry source and the catalog source yield identical findings for identical content.
7. **P7, boot report.** `ValidateRegistry` adds one warning per finding and no errors, so boot still starts.
8. **P8, the setter.** `areaset <area> name|description|designation <value>` updates the registered template and the live area, writes the YAML, publishes one `AreaPropertySetByAdminEvent`, and echoes the result. `designation clear` removes both the template field and the component. A bad value or an unknown area changes nothing, writes nothing and publishes nothing.
9. **P9, `area` verb.** It prints the designation, or "undesignated", plus that area's findings. An `areaset` is reflected without a `reload`.
10. **P10, in-game rule-save warnings.** `AddSpawnRule`/`UpdateSpawnRule` still succeed and carry the finding on `SpawnRuleMutationResult.Findings`; `RemoveSpawnRule` carries none. `spawnrule add|set`, `mkmob` and `mkitem` print each finding as a `Warning:` line.
11. **P11, web rule-save warnings.** `UpsertSpawnRuleAsync`, `SaveAsync(room)` and `SaveRoomAsync` write the file, succeed, and fold each finding into `ContentWriteResult.Warnings`. This applies only to rules that are new or changed relative to the prior on-disk room. A save with unchanged rules warns nothing, even when the room moved area.
12. **P12, web inspection.** `IAreaDesignationAudit.Findings(area?)` sweeps disk. The area editor binds the designation and lists the area's findings. The Integrity page shows a third category with room and area edit links.
13. **P13, rename.** A catalog rename carries `Designation` to the new id.
14. **P14, dev-world.** The hub stays undesignated, and the whole-world sweep is clean.

**Main flow (summary).**
- `areaset area.wilds designation 0 1-2` → `AreaDesignation.TryParseOrClear` → `IAreaBuilderSystem.SetAreaDesignation` (validate → template + live component) → `IAreaContentWriter.WriteAsync` → `AreaPropertySetByAdminEvent` → `AdminAuditHandler` log → echo.
- `spawnrule add mob.ogre` in that area → `RoomBuilderSystem` commits, then calls `IAreaPlacementSystem.JudgeRule` → `SpawnRuleAuthoringOutcome.CompleteAsync` (YAML + audit event, unchanged) → the confirmation → `WriteFindingsAsync` prints the `Warning:` line.
- On the web, a "Spawns in" add goes through `UpsertSpawnRuleAsync` → `SaveRoomCoreAsync`. That captures the prior rules, writes the room, and judges each new or changed rule over a `CatalogPlacementSource`; the findings go into `Warnings`.
- The area editor and Integrity read `IAreaDesignationAudit`.
- Boot: the deserializer fails fast on a malformed designation. `ValidateRegistry` logs the findings as warnings. `reload` re-derives the component through `Apply`.

## Shipped pieces

| Surface | Location |
|---|---|
| `AreaDesignation` (min/max `PowerBand`, `Of`, `Contains`, `TryParse`, `TryParseOrClear`, `ToString`) + `AreaDesignationValidation.Errors`, the single shape check | `Core/Modules/World/AreaDesignation.cs` |
| `AreaDesignationComponent`, the live form (not `[Persistent]`) | `Core/Modules/World/Components/AreaDesignationComponent.cs` |
| `AreaTemplate.Designation`; deserializer (fail-fast `DesignationDto`); writer (key omitted when null) | `Core/Modules/World/Templates/AreaTemplate.cs`, `AreaTemplateDeserializer.cs`, `Core/Modules/World/Systems/AreaContentWriter.cs` |
| `AreaPlacementCheck` (`Judge`, `Sweep`) + `PlacementFinding.Describe()` | `Core/Modules/World/Systems/AreaPlacementCheck.cs` |
| `IAreaPlacementSource` + `PlacementSite` / `PlacedTag` / `PlacedKind` | `Core/Modules/World/Systems/IAreaPlacementSource.cs` |
| `RegistryPlacementSource` (in-game source) | `Core/Modules/World/Systems/RegistryPlacementSource.cs` |
| `IAreaPlacementSystem` / `AreaPlacementSystem` (`Findings`, `JudgeRule`), registered in `WorldModule` | `Core/Modules/World/Systems/` |
| `ContentValidator`: `Validate(AreaTemplate)` reports shape and affinity errors together; `ValidateRegistry` step 6 adds the warnings | `Core/Modules/World/Systems/ContentValidator.cs` |
| `IAreaBuilderSystem.SetAreaName` / `SetAreaDescription` / `SetAreaDesignation` → `AreaPropertyResult` | `Core/Modules/Admin/Systems/IAreaBuilderSystem.cs`, `AreaBuilderSystem.cs` |
| `SpawnRuleMutationResult.Findings`; `RoomBuilderSystem` judges Add/Update after commit (new `IAreaPlacementSystem` dependency) | `Core/Modules/Admin/Systems/IRoomBuilderSystem.cs`, `RoomBuilderSystem.cs` |
| `SpawnRuleAuthoringOutcome.WriteFindingsAsync`; called by `spawnrule`, `mkmob`, `mkitem` | `Core/Modules/Admin/Commands/`, `Core/Modules/Mobs/Commands/MkMobCommand.cs`, `Core/Modules/Items/Commands/MkitemCommand.cs` |
| `areaset` (`AreaSetCommand`), registered in `AdminModule` | `Core/Modules/Admin/Commands/AreaSetCommand.cs` |
| `area`: designation + findings output | `Core/Modules/Admin/Commands/AreaCommand.cs` |
| `AreaPropertySetByAdminEvent` + `AdminAuditHandler` handler + `Server/Program.cs` subscription | `Core/Modules/Admin/Events/`, `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`, `Server/Program.cs` |
| `ISpawnRuleAuthoring.AllSpawnSites()` | `Core/Modules/Authoring/Systems/ISpawnRuleAuthoring.cs`, `ContentDefinitionCatalog.cs` |
| `CatalogPlacementSource` (web source; per-sweep memo; tolerates unreadable files) | `Core/Modules/Authoring/Systems/CatalogPlacementSource.cs` |
| `IAreaDesignationAudit` / `AreaDesignationAudit`, registered in `AuthoringModule` | `Core/Modules/Authoring/Systems/` |
| Catalog rule-save warnings (`PriorRoomRules` + `DesignationWarnings` in `SaveCoreAsync`/`SaveRoomCoreAsync`); rename carries `Designation` | `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` |
| `ContentIntegritySweepService`: third sweep (`PlacementFindings`) | `Hedron.Web/Services/ContentIntegritySweepService.cs` |
| Area editor designation fieldset + findings list; Integrity category; room editor keeps create-time warnings on the new + bidirectional path | `Hedron.Web/Components/Pages/AreaEditor.razor`, `Integrity.razor`, `RoomEditor.razor` |
| `add-command` skill: `areaset`, findings output on spawn-rule mutations (INV-20) | `.claude/skills/add-command/SKILL.md` |

## Tests shipped

All system-unit tests use the real `EntityService` and `TemplateRegistry`, and temp content directories where disk is involved. Flow tests reuse the `SpawnWorld` harness, extended with an area writer, the area builder, the placement system and the `areaset`/`area` commands.

- **`AreaDesignationTests`:** containment, `TryParse`/`TryParseOrClear` accept and reject cases, display text, and every shape violation.
- **`AreaPlacementCheckTests`:** each no-finding case, findings carrying every R9 field, sweep order, per-placement judgement across two areas, the area filter (including a mixed-case `AreaId`), and `AreaPlacementSystem.JudgeRule`.
- **`AreaTemplateDeserializerTests`** (extended): parse/absent, six malformed shapes → `ContentShapeException`, the component only when designated, no `[Persistent]`, and a writer round-trip with the key omitted when undesignated.
- **`ContentValidatorTests`** (extended): an area save reports shape and affinity errors together, and `ValidateRegistry` gives one warning while staying valid.
- **`DevWorldFixtureTests`** (extended): the hub is undesignated and the world sweep is clean.
- **`ContentDefinitionCatalogTests`** (extended): rename preserves `Designation`.
- **`AreaBuilderSystemTests`** (extended): set/replace/clear designation on the template and live entity; malformed or unknown refused; name/description; template-only when not spawned.
- **`AreaDesignationCommandFlowTests`** (flow):
  - `areaset` designation/clear/name/description, including the YAML and the single event;
  - bad values, unknown property and unknown area change nothing;
  - the `spawnrule` warning, with the rule still saved and audited;
  - in-range rules and removals warn nothing;
  - builder findings on Add/Update;
  - the `area` output before and after `areaset`.
- **`AreaDesignationCatalogTests`:**
  - an upsert warning;
  - unchanged rules plus an area move warn nothing;
  - exactly the changed rule warns;
  - a case-only edit is unchanged;
  - the disk audit (filter, and a designation saved without restart);
  - malformed area/mob files tolerated;
  - **registry/catalog source parity**.
- **`ContentIntegritySweepServiceTests`** (extended): a completed sweep carries the findings, and a failed one leaves them empty.
- **Not tested:**
  - Blazor markup (thin binding);
  - exact prose (substring asserts instead);
  - the `AdminAuditHandler` log line (the log-only skip rule; the event's publication is asserted).

`dotnet build` and `dotnet test` are green: **1670 tests** (65 new; 1605 before this slice). A manual smoke on a temp copy of dev-world covered designate → "Spawns in" warning → area-editor list → Integrity category, and a malformed designation failing a `DevWorld` boot with the per-file report.

## Decisions

- **The check is a content-consistency rule, not a balance computation.** It compares two *authored* facts and never calls `IPowerBudgetSystem`, so it lives in World beside the other warn-only authored-data checks, not in `IBalanceAuditSystem`. That keeps band drift (authored vs. computed) and designation fit (authored vs. area) independent and composable (R10). A mis-tagged *and* misplaced mob shows in both Integrity categories, as the smoke run confirmed.
- **One judgement, many callers (INV-19).** `AreaPlacementCheck` is pure and sits beside the designation type, following the `SpawnRuleValidation` precedent. Every surface calls it, and `PlacementFinding.Describe()` is the one text, so no two surfaces can disagree on the rule or its wording.
- **The source follows the surface (resolved decision 6).**
  - In-game surfaces read the template registry, which admin verbs mutate (INV-21).
  - Web surfaces read disk through the catalog, which is what editors write. This avoids the boot-time-registry staleness the drift audit has.
  - The seam is one small read interface (`IAreaPlacementSource`) with two adapters. A parity test pins identical results.
  - Rejected: registry everywhere (web warnings stale until Apply); disk everywhere (breaks the in-game mutation pattern).
- **Shape: min/max `PowerBand` cells plus a same-tier rule.** The YAML stays "tier + band range" (requirements decision 1). Cross-tier ranges for tiered-overworld borders are shaped for later: allowing them means deleting one validation rule plus a YAML widening. Containment uses lexicographic (Tier, Band) order, the same total order as `GlobalBandIndex` for in-range bands. `GlobalBandIndex` is an instance method on the DI-built `PowerBudgetTunables`, which a static check and the deserializer cannot reach.
- **Optional designation; the hub stays undesignated.** Undesignated means unchecked, not a warning. Unbanded placements (`Band 0`, such as junk items) are skipped. A malformed designation fails fast; a misplaced spawn only warns (requirements decision 5).
- **Live component, mirroring `aspectAffinities`.** INV-21 already required the setter to update the live entity. Later consumers (the Stage A boundary banner, `map` region view, entrance telegraph) will read the live area. The component is world content and never persistent.
- **A general area setter (resolved decision 7).** Areas were the only content kind with no in-game property setter. `areaset` joins the `setitem`/`setmob` family with `name`, `description` and `designation`, rather than a one-off `designate` verb. `setarea` was already taken (it assigns a room to an area). Further area properties (`pvp`, `respawnRate`) join as property tokens when they gain consumers.
- **Authoring-time warnings on spawn-rule saves only (resolved decision 8).**
  - On the web, a "rule save" is a diff against the prior on-disk room: a rule whose id is new, or whose blueprint, count or delay changed, compared case-insensitively.
  - So a room-editor Save that only renames, or moves the room to another area, warns nothing; the area editor list and Integrity cover it.
  - Rejected: warnings on every placement-changing edit (`setarea`, re-tags, designation saves).
- **Boot-only registry warnings.** `ValidateRegistry` runs only at host start, and `reload` never ran it. The new findings match the two existing warning categories. Malformed designations still fail fast on `reload`, through the deserializer. The pre-existing gap this exposed (malformed *affinities* pass `reload`) is logged in the backlog, not widened.
- **Unreadable files are tolerated on the web (spec-gate finding).** `ContentDefinitionCatalog.Load` deserializes without a catch. `CatalogPlacementSource` logs a failed load and treats the area or target as unknown. Otherwise one hand-edited bad file would blank the whole Integrity sweep or throw from a rule save after its write.
- **Placement and authoring requirements:** R1–R14 of the slice requirements, R4/R23 of the program. Rejected at intake: a cell-to-cell or single-cell shape; mobs-only checking; flagging unbanded placements; Integrity-only feedback.

## Deviations / Follow-ups

- **Deviations from the plan:**
  - `ContentValidator` does **not** take an `IAreaPlacementSystem` constructor dependency. Step 6 runs `AreaPlacementCheck.Sweep(new RegistryPlacementSource(_templateRegistry))` over the registry it already holds. This is the same judgement, and the 8 manual `new ContentValidator(` test sites stayed untouched.
  - **Code-gate fixes folded in before merge:**
    - the `Sweep` area filter is case-insensitive;
    - the catalog's "unchanged rule" diff compares ids case-insensitively;
    - the area editor raises band max to match band min;
    - the `clear`/`none` grammar moved into `AreaDesignation.TryParseOrClear`;
    - the drop-checklist item was added to the program file.
  - The in-game `areaset` path was covered by flow tests with the real loader and writers, but not driven over telnet.
- **Follow-ups unlocked:**
  - content drops 6–8 designate their hunting areas (the checklist item is in the program file);
  - tiered-overworld Stage A (boundary banner, gradient warning), Stage B (generator designations, pools) and Stage E (`map` region view) read the live designation;
  - the program's relative-power read can take a designation's cells as a target power through `TargetRange`.
- **Debt parked in [`../backlog.md`](../backlog.md):**
  - "`reload` skips registry validation, so malformed area affinities pass it", found by the spec review.
  - The area editor's findings list runs synchronously on the Blazor circuit thread; a cold catalog index sweeps the rooms directory. It is recorded in [`08-blazor.md`](../../architecture/08-blazor.md)'s off-circuit inventory.
  - The live-area lookup adds one call site to the existing "Blueprint-scan index" backlog debt.
