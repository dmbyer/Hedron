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
"Persistence:FlushIntervalSeconds"   (default 60)
"Persistence:DataDirectory"          (default "data/entities/")
"Server:Port"                        (default 4000)
```

Defaults live in `appsettings.json`. Overrides live in environment variables or `appsettings.Production.json`. No magic numbers in C# source for these settings.

---

## Category 2 — Content / Authored Data

**Examples:** Area respawn rates, mob stat blocks, loot tables, shop inventories, item definitions, room descriptions, spell lists.

**Recommended tool: Data files in a versioned format (JSON/YAML) under `data/`, loaded via `TemplateRegistry`**

Rationale: Content is authored by designers, not operators. It doesn't belong in appsettings.json (wrong audience, wrong diff noise, wrong access model). It should be versioned alongside code, live-reloadable in a future admin tooling slice, and have a clear schema independent of the C# type system. `TemplateRegistry.Spawn` is the bridge between these files and live entities; the format is the concern of Phase 3 slice 3 (world-content loading).

> Note: The exact file format (JSON, YAML, or a custom DSL), the schema versioning approach, and the hot-reload story are **open decisions** deferred to Phase 3 slice 3. See [Open Decisions](#open-decisions) below.

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

The data directory path (`"Persistence:DataDirectory"`, default `"data/entities/"`) follows the same pattern.

---

## Open Decisions

These questions are **recorded but not resolved**. Resolution belongs in the phase or slice that first needs the answer.

### OD-1 — Content data file format (Phase 3 slice 3)

What format do authored templates live in? Candidates: JSON (simple, standard, verbose), YAML (readable, whitespace-sensitive), a hand-rolled DSL, or a hybrid. The choice affects `TemplateRegistry`'s loader, schema validation, hot-reload feasibility, and the admin tooling story. Decide before slice 3 implementation begins.

### OD-2 — Balance constant promotion threshold (Phase 3 slices 8–12)

At what point, if ever, should balance constants be promoted from sealed C# classes to externally tunable data files? The threshold is "when a designer needs to iterate without a recompile." This is a product decision, not an architectural one. Until it is triggered, constants stay in code.

### OD-3 — Area-level respawn rate ownership (Phase 3 slice 7 — Mobs)

Area respawn rates sit on the boundary of Category 2 (content) and Category 3 (balance math). A global base rate is Category 3 (constant). A per-area override is Category 2 (authored data on the area template). The split should be formalized when the mob/respawn slice is planned.

### OD-4 — RNG weight tables: authored vs. hardcoded (Phase 3 slice 7 and beyond)

`RandomGeneratorSystem` operates on `LootTable<T>` values. Where do those tables originate? If they are authored per area/mob, they are Category 2 data-file content. If they are fixed system defaults, they are Category 3 constants. The answer may differ by table type. Decide per feature slice.

### OD-5 — `IConfiguration` vs. options pattern for persistence settings (Phase 3 slice 1)

For the persistence slice, direct `IConfiguration` string reads are acceptable. If the number of Category 1 settings grows beyond ~5 keys, the standard .NET options pattern (`IOptions<PersistenceOptions>`) is cleaner. Promote when the volume justifies it; do not use it prematurely.
