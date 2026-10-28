# `content/` — engine fixtures and examples, not a game world

Hedron is an engine, so this repository ships **no playable game world**. Whoever runs a Hedron server authors and owns their own world under `World:ContentDirectory` (default `data/content/`, which is gitignored), and versions it outside this repository.

Everything committed here is generic development material:

| Path | Purpose |
|---|---|
| [`dev-world/`](dev-world/) | The generic development world: unbranded fixture content for smoke-testing a fresh clone, flow and playtest automation, and CI. |
| [`profiles/`](profiles/) | Example profiles for the headless `generate` run mode. |

The full policy (what may go in `dev-world/`, and how to point a host at it without mixing it with your own world) is in [content ownership](../docs/features/world/world-content.md#content-ownership--the-engine-ships-no-game-world).
