# Hedron

Hedron is a C# **MUD (Multi-User Dungeon) game engine** targeting **.NET 8**. It serves a live, event-driven world over a telnet listener, with content authored as YAML data files and editable through an in-process Blazor web UI. The codebase is under active rebuild — see [`CLAUDE.md`](CLAUDE.md) and [`docs/`](docs/) for architecture and roadmap.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Any OS supported by the .NET 8 runtime (developed on Windows)

## Project structure

| Project | Output | Responsibility |
|---|---|---|
| [`Core/`](Core/) | library | The engine: ECS components, systems, handlers, events, commands, and the content/persistence modules. No transport or hosting. |
| [`Server/`](Server/) | console exe | Telnet host. Owns DI composition (`CompositionRoot`) and the headless `generate`/`simulate` run-modes. |
| [`Hedron.Web/`](Hedron.Web/) | web app | Blazor Server **content-authoring UI** (browse / create / edit YAML content). Boots the same engine via `Server`'s composition root; loopback-only by default. |
| [`Hedron.Tests/`](Hedron.Tests/) | xUnit | System / handler / flow / persistence / architecture-guard test suite. |

Persistence (SQLite) and content loading currently live inside `Core/` modules; a standalone test bot is deferred. See [`docs/roadmap/plan.md`](docs/roadmap/plan.md).

## Your world, your content

Hedron is an engine, so **this repository ships no game world**. If you run a server, you author your own world (rooms, areas, mobs, items) under `Paths:DataDirectory` (default `data/`, gitignored) and keep it in your own repository. The only committed content is generic development material in [`content/`](content/): the [`dev-world/`](content/dev-world/) baseline (generic and lore-free; boot it with `DOTNET_ENVIRONMENT=DevWorld`), the engine's generic [`help/`](content/help/) topics (shipped with the binaries; a world overrides or extends them in its own `help/` folder), and example generation profiles. A fresh clone pointed at an empty content directory boots into a single placeholder "void" room, ready for authoring. See [content ownership](docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).

## Build & test

```bash
dotnet build Hedron.sln
dotnet test Hedron.sln
```

## Running

### Game server (telnet)

```bash
dotnet run --project Server
```

Listens on `Server:Port` (default `4000`). Connect with any telnet client, e.g. `telnet localhost 4000`.

### Content-authoring web UI

```bash
dotnet run --project Hedron.Web
```

Serves the Blazor authoring editor at `Web:BindUrl` (default `http://127.0.0.1:5050`, loopback only). Edits write YAML to the content directory; use the editor's **Apply** action (or restart the game server) to load changes into a live world. The editor's **Simulation** page (`/simulation`) composes, launches, and inspects batch combat or progression-rate runs in the background — the same engine and report artifact the headless `simulate` run-mode below produces — with "Simulate vs reference" entry points on the mob/item editors and a "Re-run baseline sweep" affordance on the Standards page.

> **The same port also serves an unauthenticated JSON API at `/api`** — mob create/save/delete plus read-only area/room lookups, over the same content catalog. There is no authentication; the protection is loopback binding plus a same-origin/JSON-content-type filter, so **any process on your machine can write content while this host is running**. Do not bind it to a non-loopback address. Its contract is published at [`Hedron.Web/Hedron.Web_authoring.json`](Hedron.Web/Hedron.Web_authoring.json).
>
> That document is **generated on every `dotnet build` of `Hedron.Web` and checked in**, so editing an API DTO leaves the file dirty in your working tree — commit it. CI regenerates it and fails the build on any difference.

### Bulk content generation (headless)

Generate a swath of areas / rooms / mobs / items from a profile — for testing and scaling work — without starting the listener or heartbeat:

```bash
dotnet run --project Server -- generate --profile <path-to-profile.yaml> [--seed N]
```

- `--profile <path>` (**required**) — a generation profile (see [`content/profiles/example.yaml`](content/profiles/example.yaml)).
- `--seed N` (optional) — overrides the profile's seed; a fixed seed produces identical output (reproducible test worlds).

Writes validated YAML under the content directory and exits `0` on success, non-zero on a validation or I/O failure.

### Balance simulation (headless)

Run a deterministic batch of combat scenarios — an isolated sandbox world per run, never the live world — and validate the outcomes against the balance-standards registry, without starting the listener or heartbeat:

```bash
dotnet run --project Server -- simulate --scenario <path-to-scenario.yaml> [--seed N]
```

- `--scenario <path>` (**required**) — a scenario definition (see [`data/sim/scenarios/example-equal-cell.yaml`](data/sim/scenarios/example-equal-cell.yaml)).
- `--seed N` (optional) — overrides the scenario's seed; a fixed seed reproduces byte-identical results regardless of parallelism.

Prints a win-rate/time-to-kill/verdict summary and writes a JSON report artifact under `Simulation:ReportDirectory` (default `data/sim/reports/`). Exits `0` on a clean run, `1` on an engine failure, `2` on a usage or scenario-validation error.

## Configuration

Each host reads its own `appsettings.json` — [`Server/appsettings.json`](Server/appsettings.json) (the telnet host and the `generate`/`simulate` run modes) and [`Hedron.Web/appsettings.json`](Hedron.Web/appsettings.json). Settings are grouped by section:

| Section | Controls |
|---|---|
| `Server` | Telnet listener port |
| `Web` | Web host bind URL (`Hedron.Web`) |
| `Paths` | `DataDirectory` — where all data lives (see below) |
| `World` | Starting room blueprint (the fallback behind a world's `world.yaml`); optional content-directory override |
| `Persistence` | Flush interval; optional database-path override |
| `Help` | Optional `EngineTopicDirectory` override for the engine help topics (default `content/help`, relative to the Hedron root — not under the data directory) |
| `Heartbeat` | Game-loop tick interval |
| `Admin` | Privileged account names |
| `CharacterDefaults` · `Death` · `Output` · `Logging` · `Shop` | Starting stats/abilities, death tuning, output color, log levels, shop buy/sell ratios |

The concrete keys and defaults live in the `appsettings.json` files and are not duplicated here.

**Where data lives — one setting.** Everything Hedron reads and writes at runtime (world content, the player database, balance standards, simulation reports) lives under `Paths:DataDirectory`, default `data`. A relative path resolves against the Hedron root: your checkout when run from source, the install folder when published. It never depends on the working directory or launcher.

| Situation | Set |
|---|---|
| Local development | nothing — data lives in the checkout's gitignored `data/` |
| The dev-world baseline | `DOTNET_ENVIRONMENT=DevWorld` (committed profile: [`content/dev-world/`](content/dev-world/) + `data/dev-world.db`) |
| Your own world elsewhere on this machine | `HEDRON_Paths__DataDirectory=C:\hedron-world` (holds `content\`, `hedron.db`, …) |
| A deployment / container | `HEDRON_Paths__DataDirectory=/var/hedron`, mounted as one volume |

**Overrides.** `appsettings.{Environment}.json` (selected by `DOTNET_ENVIRONMENT`) layers over `appsettings.json`, and any setting can be overridden with a `HEDRON_`-prefixed environment variable (`__` separates sections), which wins over every file. Full rules: [`05-configuration.md`](docs/architecture/05-configuration.md#data-locations--one-rule).

## Learn more

- [`CLAUDE.md`](CLAUDE.md) — entry point for contributors and agents; architecture ground rules.
- [`docs/`](docs/) — architecture, reference catalogs, use cases, and the roadmap.

## Community

Discussion and development chat: [Discord](https://discord.gg/BafNmpK).
