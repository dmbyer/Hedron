# Configuration Strategy

This document defines how the four distinct categories of configuration in Hedron are handled. Read it before wiring any setting to `IConfiguration`, hardcoding a constant, or adding a new data file.

---

## Why categorize configuration?

Not every "setting" is the same thing. A database connection string, a mob's respawn delay, a damage formula, and a feature-flag toggle all require different change cadences, different ownership, and different tooling. Getting the category wrong leads to:

- Game-balance numbers buried in `appsettings.json` where designers can't find them.
- Infrastructure secrets leaking into content data files.
- Balance formulas hardcoded as magic numbers scattered through a dozen system files.

The categories below are the organizing principle.

---

## Category 1 — Infrastructure / Operational

**Examples:** TCP port, persistence flush interval, persistence data directory path, log level, host shutdown timeout.

**Recommended tool: `IConfiguration` (appsettings.json + environment variables)**

Rationale: These settings are environment-specific, operator-controlled, and already served well by `Microsoft.Extensions.Configuration`. They need to change without recompiling, they are the natural habitat of appsettings/env-var overrides, and they belong to deployment, not design.

**Naming convention:** `"Section:Key"` form, e.g.:

```
"Paths:DataDirectory"                (default "data" — every data location lives under it; see below)
"Persistence:FlushIntervalSeconds"   (default 60)
"World:StartingRoomBlueprintId"      (default "room.crossroads"; fallback behind world.yaml, see Category 2)
"Admin:PrivilegedNames"              (default [], string array — bootstrap admin allowlist)
"Server:Port"                        (default 4000)
```

Defaults live in `appsettings.json`. No magic numbers in C# source for these settings.

### Data locations — one rule

Everything Hedron reads and writes at runtime lives under **one data directory**, `Paths:DataDirectory` (default `data`):

| Location | Path under the data directory | Per-location override key |
|---|---|---|
| World content (YAML) | `content/` | `World:ContentDirectory` |
| Player database (SQLite) | `hedron.db` | `Persistence:DatabasePath` |
| Balance standards | `balance/standards.yaml` | `Balance:StandardsPath` |
| Simulation reports / saved scenarios | `sim/reports/`, `sim/scenarios/` | `Simulation:ReportDirectory`, `Simulation:ScenarioDirectory` |

- **Point Hedron at your data:** set `Paths:DataDirectory`. That one setting moves all five locations.
- **Relative paths resolve against the Hedron root**, never the working directory. The root is your source checkout (the folder holding `Hedron.sln`) when run from source, and the install folder when published. So `data` means `<checkout>/data/` for every host, run mode, IDE and launcher, and `<install>/data/` in a deployment. Absolute paths are used as given.
- **Override a single location** only when one must live apart from the rest. The committed `DevWorld` profile does this for content (`content/dev-world/`) and the database (`data/dev-world.db`). Because an explicit location beats the data directory, the profile works even on a machine whose own data directory is set.
- Implemented once, in `Server/DataPaths` (applied by the composition root that every host and run mode shares), and pinned by `DataPathsTests`.

### Setting values — sources & precedence

Lowest to highest:
1. `appsettings.json` (each host has its own: `Server/`, `Hedron.Web/`);
2. `appsettings.{Environment}.json`, selected by `DOTNET_ENVIRONMENT` (e.g. the committed `DevWorld` profile, or an operator's `Production` file);
3. command-line `--Section:Key=value`;
4. `HEDRON_`-prefixed environment variables (`__` separates sections, e.g. `HEDRON_Paths__DataDirectory`), which win over everything. This is the **one** environment-variable mechanism.

**Recipes:**
- **Local development:** nothing to set; data lives in the checkout's gitignored `data/`.
- **Dev-world baseline:** `DOTNET_ENVIRONMENT=DevWorld`.
- **Your own world on this machine:** `HEDRON_Paths__DataDirectory=<folder>` (that folder holds `content/`, `hedron.db`, …).
- **Deployment / container:** `HEDRON_Paths__DataDirectory=/var/hedron` (or leave the default, `<install>/data`), and mount that one folder as the volume.
- **Tests:** `dotnet test` reads no runtime configuration. Tests construct their options or set temp paths explicitly, so no environment variable affects them.

The Server process (telnet host and `generate`/`simulate`) reads its JSON from its build output, so it loads whatever the working directory; the run modes take their environment from `DOTNET_ENVIRONMENT`. `Hedron.Web` reads its JSON from its content root.

---

## Category 2 — Content / Authored Data

**Examples:** Area respawn rates, mob stat blocks, loot tables, shop inventories, item definitions, room descriptions, spell lists.

**Recommended tool: YAML data files under the content directory (`<Paths:DataDirectory>/content`, default `data/content/`), loaded via `TemplateRegistry`**

Rationale: Content is authored by designers, not operators. It doesn't belong in appsettings.json (wrong audience, wrong diff noise, wrong access model). It is plain text the world's owner can version in their own repository, live-reloadable via `@reload`, and has a schema independent of the C# type system. `TemplateRegistry.Spawn` is the bridge between these files and live entities.

**World-level facts** live in the optional `world.yaml` manifest at the content root, not in a template kind. The starting room is the first: `startingRoomId` is the world's declaration, and the Category 1 `World:StartingRoomBlueprintId` key is only the operator fallback when the manifest is absent or names no live room (see [`../features/world/world-content.md`](../features/world/world-content.md#world-manifest--worldyaml)).

**The engine repository ships no game world.** The content directory is gitignored; the only committed content is the generic development world under `content/dev-world/`. See [content ownership](../features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).

**Format: YAML via `YamlDotNet`.** Resolved by Phase 3 slice 2 ([`../implementation-plans/world-content-loading-and-admin-substrate.md`](../features/world/world.md)). Persistence component snapshots use `System.Text.Json` on a separate code path — content authoring (designer-write, comment-friendly) and runtime persistence (round-trip-fidelity, machine-write) coexist by design and do not share serializer code.

The cross-cutting `IContentSerializer` (in `Core/Systems/`) is a thin kind-dispatcher; per-module `ITemplateDeserializer` implementations live next to the templates they translate (e.g. `RoomTemplateDeserializer` in the World module). Adding a new templated archetype in a future slice means registering a new deserializer in the owning module — not editing the cross-cutting serializer.

The `data/` tree is gitignored. A `seed-content/` directory for first-run bootstrap is deferred — see the slice 2 use-case for the empty-content fallback (single hardcoded "void" room).

---

## Category 3 — System Math / Balance

**Examples:** Skill advancement XP curves, critical success/failure thresholds, damage formula coefficients, flee-chance scaling, level-up bonus tables, skill-point cost tables, potion effectiveness multipliers, shop buy/sell ratios.

**Recommended tool: Named static constants or sealed configuration classes in C#, co-located with the system that owns them**

Rationale: Balance math is not operator configuration and it is not authored content — it is game design encoded as rules. Putting it in appsettings means a typo in a JSON file crashes combat. Putting it in data files requires a loading/schema layer for something that changes infrequently and always in the same PR as the code it governs.

The right home is a sealed class with self-documenting constant names, living next to the system that uses them:

```
Core/Systems/SkillConstants.cs          (thresholds, check math)
Core/Modules/Combat/CombatConstants.cs  (damage coefficients, flee scaling)
Core/Modules/Progression/ProgressionConstants.cs  (XP curves, level caps)
```

When balance changes, the constants change in the same commit as the system code that reads them. The PR diff is self-contained and reviewable. If a set of constants grows large enough to benefit from external tuning (e.g. designer iteration without redeployment), that specific subset can be promoted to a data file at that point — it is not premature to keep them in code until the need is demonstrated.

> Note: The detailed shape of each constant class, and whether any subset deserves promotion to a tunable data file, is an **open decision** for each respective Phase 3 slice. See [Open Decisions](#open-decisions).

---

## Category 4 — Feature Flags

**Examples:** Enable/disable experimental combat rulesets, toggle AI difficulty, switch between prototype and production data directories.

**Recommended tool: `IConfiguration` for flags that vary by environment; constants for flags that gate in-progress features**

Rationale: Environment-varying flags (production vs. dev data paths) are already handled by Category 1. Flags that gate half-built features during development are better as `#if DEBUG` guards or short-lived code branches that are removed when the feature ships. A dedicated feature-flag infrastructure is premature until there is a demonstrated need for runtime toggling in production.

> Note: If Hedron eventually needs runtime feature toggling (A/B experiments, staged rollouts), the correct answer at that point is a thin service wrapping `IConfiguration` booleans. That service does not exist yet and should not be pre-built.

---

## Summary Table

| Category | Tool | Change cadence | Who changes it |
|---|---|---|---|
| Infrastructure / operational | `IConfiguration` (appsettings + env vars) | Per deployment | Operator |
| Content / authored data | Data files (`data/`) loaded via `TemplateRegistry` | Per content update | Designer |
| System math / balance | Named constants in C# co-located with owning system | Per design iteration | Developer |
| Feature flags | `IConfiguration` (env flags) or code guards | Per feature / per deploy | Developer / operator |

---

## The persistence flush interval: definitive answer

**Use `IConfiguration`. Read `"Persistence:FlushIntervalSeconds"` in `PersistenceFlushTimer`. Default to 60.**

Justification: The flush interval is a Category 1 operational setting. It varies legitimately by environment (a development machine may want a 5-second flush for fast iteration; production may want 120 seconds to reduce I/O). It is not game-design math; it controls infrastructure behavior. The `IConfiguration` path is already available (the generic host provides it), requires zero extra infrastructure, and keeps the behavior overridable without recompile. The default in `appsettings.json` protects against misconfiguration.

The database path (`"Persistence:DatabasePath"`, default `"data/hedron.db"`) follows the same pattern.

---

## Open Decisions

These questions are **recorded but not resolved**. Resolution belongs in the phase or slice that first needs the answer.

### ~~OD-1 — Content data file format~~ (resolved by Phase 3 slice 2)

**Resolved: YAML via `YamlDotNet`.** Phase 3 slice 2 picked YAML for designer-friendliness (comments, less punctuation, indentation-driven structure). Persistence (slice 1) keeps `System.Text.Json` for component snapshots — different audience, different change cadence, no shared serializer code. See Category 2 above and the slice 2 use-case ([`../implementation-plans/world-content-loading-and-admin-substrate.md`](../features/world/world.md)) for the implementation shape (`IContentSerializer` + per-module `ITemplateDeserializer`).

### OD-2 — Balance constant promotion threshold — resolved for the power-budget/balance-standards family (sim-1)

At what point, if ever, should balance constants be promoted from sealed C# classes to externally tunable data files? The threshold is "when a designer needs to iterate without a recompile." This is a product decision, not an architectural one. Until it is triggered, constants stay in code.

**Resolved for one family (sim-1, 2026-07-13):** `PowerBudgetConstants`/`BalanceAuditConstants` (`Core/Systems/`, `Core/Modules/BalanceInspection/`) promoted to the **balance-standards registry** — a YAML-authored `BalanceStandardsDocument` (`data/balance/standards.yaml`, Category 2 shape) with compiled defaults as fallback, authored via the Blazor **Standards page**. The trigger was real: three-plus data-hungry consumers (`IPowerBudgetSystem`, `IBalanceAuditSystem`, both content editors, the sim-2 engine to come) *and* a live designer-edit surface existed simultaneously — exactly the threshold this OD names. The oracle itself stays snapshot-only (INV-2, [`power-model.md`](../design/power-model.md)): its tunables arrive as one constructor-injected plain-data record (`PowerBudgetTunables`), composed by the host from the registry, never a registry/loader dependency on the oracle itself. See [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md) and [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md).

**This does not generalize automatically.** Other Category-3 families (`ProgressionConstants`, `CombatConstants`, `AscensionConstants`, …) stay compiled constants — each is promoted individually, case-by-case, only when its own live-edit-surface-plus-≥3-consumers trigger fires. The pattern (a focused YAML store/registry pair, compiled defaults, load-time drift validation against any mirrored gameplay constants) is reusable, not a mandate to promote everything now.

### OD-3 — Area-level respawn rate ownership (Phase 3 slice 6 — Mobs)

Area respawn rates sit on the boundary of Category 2 (content) and Category 3 (balance math). A global base rate is Category 3 (constant). A per-area override is Category 2 (authored data on the area template). The split should be formalized when the mob/respawn slice is planned.

### OD-4 — RNG weight tables: authored vs. hardcoded (Phase 3 slice 6 and beyond)

`RandomGeneratorSystem` operates on `LootTable<T>` values. Where do those tables originate? If they are authored per area/mob, they are Category 2 data-file content. If they are fixed system defaults, they are Category 3 constants. The answer may differ by table type. Decide per feature slice.

### OD-5 — `IConfiguration` vs. options pattern for persistence settings (Phase 3 slice 1)

For the persistence slice, direct `IConfiguration` string reads are acceptable. If the number of Category 1 settings grows beyond ~5 keys, the standard .NET options pattern (`IOptions<PersistenceOptions>`) is cleaner. Promote when the volume justifies it; do not use it prematurely.
