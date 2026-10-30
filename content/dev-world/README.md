# Development world

**The primary playable baseline: generic, lore-free, with functional names.**

Acceptance tests, CI goldens, playtest automation and fresh-clone smoke tests run on this world. It grows as development proceeds, and it never carries lore, setting or narrative. Your own world lives under your own `Paths:DataDirectory` (default `data/`, gitignored) and in your own repository.

`world.yaml` declares the starting room (`room.hub.square`, in the non-PvP `area.hub`). Every room carries `x`/`y`/`z` and is reachable from the start and back; `DevWorldFixtureTests` guards both.

## Running against it

Select the committed `DevWorld` profile. It points the host at this directory and gives it its own database (`data/dev-world.db`), so its players never mix with your own world's. It works from any directory, and even when your machine sets `HEDRON_Paths__DataDirectory` for your own world:

```bash
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Hedron.Web
DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server -- generate --profile <path>
```

Only a per-location override (`HEDRON_World__ContentDirectory` / `HEDRON_Persistence__DatabasePath`) beats the profile, so prefer `HEDRON_Paths__DataDirectory` for your own world. While a host points here, the builder verbs (`dig`, `mkmob`, `set*`, `startroom`, …) and the editor write their changes into this committed directory. That's what you want when you are maintaining the baseline, and a mistake otherwise.

Policy: [content ownership](../../docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).
