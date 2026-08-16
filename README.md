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

Each host reads an `appsettings.json` next to its project — [`Server/appsettings.json`](Server/appsettings.json) and [`Hedron.Web/appsettings.json`](Hedron.Web/appsettings.json). Settings are grouped by section:

| Section | Controls |
|---|---|
| `Server` | Telnet listener port |
| `Web` | Web host bind URL (`Hedron.Web`) |
| `World` | Content directory + starting room blueprint |
| `Persistence` | SQLite database path + flush interval |
| `Heartbeat` | Game-loop tick interval |
| `Admin` | Privileged account names |
| `Balance` | Balance-standards YAML file path (`data/balance/standards.yaml`) |
| `Simulation` | Simulation report output directory (`data/sim/reports/`) + editor-saved scenario directory (`data/sim/scenarios/`) |
| `CharacterDefaults` · `Death` · `Output` · `Logging` · `Shop` | Starting stats/abilities, death tuning, output color, log levels, shop buy/sell ratios |

The concrete keys and defaults live in the `appsettings.json` files and are not duplicated here.

**Environment overrides.** Any setting can be overridden with a `HEDRON_`-prefixed environment variable, using `__` as the section separator — handy for pointing a worktree at a machine-local content/data location without editing tracked files:

```
HEDRON_World__ContentDirectory=C:\hedron-world\content\
HEDRON_Persistence__DatabasePath=C:\hedron-world\hedron.db
```

## Learn more

- [`CLAUDE.md`](CLAUDE.md) — entry point for contributors and agents; architecture ground rules.
- [`docs/`](docs/) — architecture, reference catalogs, use cases, and the roadmap.

## Community

Discussion and development chat: [Discord](https://discord.gg/BafNmpK).
