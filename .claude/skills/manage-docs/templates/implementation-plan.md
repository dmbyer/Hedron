<!--
TEMPLATE: Implementation plan — docs/implementation-plans/<slug>.md
A TRANSIENT per-slice build artifact: behavior spec + build plan fused. It is the single
work artifact while in flight and is DELETED on ship (content disintegrates into features/,
flows/, reference/, and roadmap/completed/ — see the disintegrate-on-ship lifecycle).
Reference flows; never reproduce a diagram. Delete these comments on copy.
-->
# Implementation Plan: <Title>

**Status:** planned | partial | implemented
**Actors:** Player / Mob / System / Administrator
**Module:** `Core/Modules/<Feature>/` · Feature: [`<feature>`](../features/<feature>/<feature>.md)

## Description
One paragraph — the desired behavior.

## Preconditions
What must already exist.

## Postconditions
The durable coverage contract. A postcondition asserting player-invisible state needs a matching test (INV-25).

## Main Flow
1. Numbered steps. No layer is directed to violate an INV (the spec-review gate checks SR-1…SR-5).

## Events Fired
| Event | Publisher | Payload | Purpose |
|---|---|---|---|

## Systems / handlers involved
Traceable to the [reference catalogs](../reference/).

## Implementation plan — work packages
1–3 independently-executable packages, each sized for a limited-context sub-agent: scope, files, dependencies, out-of-scope bounds, testable exit criterion. The primary agent runs `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact  (INV-18)
Every data-file shape, admin command, `TemplateRegistry` entry. If the slice adds gameplay state, how a designer authors + inspects it in the same PR. ("none" + one sentence if pure-infra.)

## Cross-cutting surfaces stressed  (INV-19)
Each surface classified **Adequate** / **Gap exposed** (resolve before merge) / **Acknowledged debt** (backlog entry).

## Flows introduced or modified  (INV-17)
Every [`flows/`](../architecture/flows/) journey created/extended. Reference it; never reproduce the diagram.

## Test plan / Verification  (INV-25)
Per the rubric in [`07-testing.md`](../architecture/07-testing.md): a test for each new system method, each invisible-state postcondition, each `[Persistent]` shape, each fail-fast validation — with tier. State what is NOT tested and why. "Ship green" includes `dotnet test`.

<!-- On ship: `sync-roadmap` distributes the above into the living docs, records decisions in roadmap/completed/<slice>.md, and DELETES this file. -->
