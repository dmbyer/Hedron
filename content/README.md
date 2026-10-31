# `content/` — engine fixtures and examples, not a game world

Hedron is an engine, so this repository ships **no playable game world**. Whoever runs a Hedron server authors and owns their own world under `Paths:DataDirectory` (default `data/`, which is gitignored), and versions it outside this repository.

Everything committed here is generic development material:

| Path | Purpose |
|---|---|
| [`dev-world/`](dev-world/) | The generic development world and primary playable baseline: lore-free content that acceptance tests, CI and playtest automation run on. Boot it with `DOTNET_ENVIRONMENT=DevWorld`. |
| [`help/`](help/) | The **engine help layer**: generic, lore-free `help <topic>` articles (one `<id>.yaml` per topic) that ship with the binaries. Engine-owned, edited by hand in PRs, never written by tooling. A world overrides or adds topics in its own `<content>/help/`. Bodies stay qualitative about tunables (no numbers). See [help topics](../docs/features/help/help-topics.md). |
| [`profiles/`](profiles/) | Example profiles for the headless `generate` run mode. |

The full policy (what may go in `dev-world/`, and how to point a host at it without mixing it with your own world) is in [content ownership](../docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).
