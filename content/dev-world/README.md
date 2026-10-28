# Development world

**This is a generic test fixture. It is not a game world.**

It contains just enough deliberately plain content to exercise engine mechanics: smoke-testing a fresh clone, flow and playtest automation, CI goldens, and demonstrating a mechanic in a PR. It has no lore, setting, or narrative, and it isn't meant as a starting point for one. Build your own world under your own `World:ContentDirectory` (default `data/content/`, gitignored), and keep it in your own repository.

The fixture is empty until a slice needs content to exercise a mechanic. Add content here sized to that mechanic, with placeholder-grade names (`Test Crossroads`, `Training Dummy`, `Iron Sword`).

## Running against it

Point a host at this directory, and give it its own database so its players never mix with your own world's:

```bash
HEDRON_World__ContentDirectory=content/dev-world/ \
HEDRON_Persistence__DatabasePath=data/dev-world.db \
dotnet run --project Server
```

The same overrides work for `Hedron.Web` and for the `generate` run mode. While a host points here, the builder verbs (`dig`, `mkmob`, `set*`, …) and the editor write their changes into this committed directory. That's what you want when you are maintaining the fixture, and a mistake otherwise.

Policy: [content ownership](../../docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).
