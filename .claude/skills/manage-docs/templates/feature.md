<!--
TEMPLATE: Feature doc — docs/features/<feature>/<feature>.md
Holistic, player-facing, orchestration-level. Defers system internals to the <system>.md docs beside it.
Low code, low duplication: link reference rows and interface files; never restate an INV (cite by id).
Delete these comments when you copy the template.
-->
# <Feature>

> One line: the player-facing capability this feature delivers. **Status:** live | partial | planned.

## What it is

The outcome and experience, from the player's side. What can a player *do*; what does the world *do* in response. No internals.

## How it works

Orchestration level only: which systems this feature composes and how they fit together (this command calls that system; that handler reacts to this event). Name the pieces; defer the *how* to the system docs.

## Systems

| System | Role |
|---|---|
| [`<system>.md`](<system>.md) | one line |

## Surfaces

Commands, events, and components this feature exposes — **link the [`reference/`](../../reference/) row**, don't restate it.

- Commands: …
- Events: …
- Components: …

## Flows

- [`<feature>-journey`](../../architecture/flows/<feature>-journey.md) — the runtime journey for this feature.

## Related

Foundational arch docs (`00`–`08`), the design model it realizes, adjacent features, and `roadmap/completed/` history if rationale matters.
