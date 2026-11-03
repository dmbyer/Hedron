# Development world

**The primary playable baseline: generic, lore-free, with functional names.**

Acceptance tests, CI goldens, playtest automation and fresh-clone smoke tests run on this world. It grows as development proceeds, and it never carries lore, setting or narrative. Your own world lives under your own `Paths:DataDirectory` (default `data/`, gitignored) and in your own repository.

`world.yaml` declares the starting room (`room.hub.square`, in the non-PvP `area.hub`). Every room carries `x`/`y`/`z` and is reachable from the start and back; `DevWorldFixtureTests` guards both.

## Running against it

Select the committed `DevWorld` profile. It points the host at this directory, gives it its own database (`data/dev-world.db`) so its players never mix with your own world's, reads sim scenarios from `sim/` here, and pins the balance standards to `balance/standards.yaml` here (absent, so the compiled defaults). It works from any directory, and even when your machine sets `HEDRON_Paths__DataDirectory` for your own world:

```bash
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Hedron.Web
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server -- generate --profile <path>
```

Only a per-location override (`HEDRON_World__ContentDirectory` / `HEDRON_Persistence__DatabasePath`) beats the profile, so prefer `HEDRON_Paths__DataDirectory` for your own world. While a host points here, the builder verbs (`dig`, `mkmob`, `set*`, `startroom`, …) and the editor write their changes into this committed directory. That's what you want when you are maintaining the baseline, and a mistake otherwise.

## Sim scenarios (`sim/`)

Committed simulation scenarios over dev-world's own mobs, in the `SimScenarioStore` YAML format. The `DevWorld` profile sets `Simulation:ScenarioDirectory` to this folder, so the editor's Simulation page lists and saves here and `simulate --scenario content/dev-world/sim/<file>.yaml` runs them headless. Convention per band: one `Combat` scenario per hunting mob (the cell's reference build vs. the mob, `melee-only` both sides) plus one `ProgressionRate` scenario. The world loader never reads this folder; reports stay under `data/` (gitignored).

## Baseline conformance (CI guard)

`DevWorldConformanceTests` enforces the baseline content policy on every PR. A *hunting mob* is any room-spawned mob that is neither `untargetable` nor a shop; a *hunting area* is an area whose rooms place one. Nothing names a band — the rules derive from content:

- **Every hunting area is designated** — and no hunting mob spawns in an area-less room.
- **Every hunting mob is banded** (`Band ≥ 1`).
- **At least one designated hunting area** exists (non-vacuity).
- **≥3 distinct hunting mobs per designated area** inside its designation.
- **Every hunting mob drops coin** (`Max > 0`, `Min ≤ Max`).
- **Coin rises by band** — the mean coin midpoint strictly increases band to band.
- **Every found and shop item has `Value > 0`** and resolves to an item template.
- **Every item carrying stat bonuses is banded.**
- **The band-drift audit is clean** under the pinned standards.
- **Every committed scenario in `sim/` loads** and names existing mobs.

Connectivity, grid placement, the non-PvP hub and the out-of-designation check stay in `DevWorldFixtureTests`.

Policy: [content ownership](../../docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).
