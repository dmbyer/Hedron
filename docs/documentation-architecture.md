# Documentation Architecture

> Governs how Hedron's docs and agent tooling are organized, what each surface owns, and how they stay current. **Docs are treated like code:** single responsibility, single source of truth, minimal traversal, no silent staleness. AI-first, human-second.
>
> This file is the *explanation*. The enforceable rules live in [`architecture/checklist.md`](architecture/checklist.md) (the documentation-discipline invariants, `INV-D*`) and are checked by the `architecture-reviewer` agent on every slice — the same mechanism that keeps engine architecture on-target. Drift is caught at the per-slice gate, not by periodic audit.

## Why this exists

A growing codebase accretes overlapping docs: the same rule restated in three files, plans that freeze into stale half-truths, catalogs that mix what-exists with what-was-once-imagined. Left alone, that forces a wholesale audit every few months. This spec is the contract that prevents it: each fact has one home, each doc has one job, and the per-slice review enforces both so long stretches can pass without a comprehensive review.

## Principles

1. **Single source of truth.** Every fact has exactly one authoritative home. Other docs *link* to it; they never restate it as if authoritative.
2. **Separation of concerns.** Each surface has one responsibility (see taxonomy). Content that doesn't fit its bucket moves to the bucket that owns it.
3. **Minimal traversal, no duplication.** Prefer one hop to the authority over a local copy. A one-line summary *plus a link* is allowed; an authoritative-sounding restatement is not.
4. **AI-first, human-second.** Optimize for an agent loading the smallest correct context: terse, well-anchored sections; catalog-over-prose where a catalog suffices; stable headings to deep-link.
5. **Lifecycle honesty.** A doc is either current, or explicitly marked (trimmed / superseded / archived). No silent staleness.

## Taxonomy

| Surface | Owns (single responsibility) | Belongs here | Does **not** belong here | Primary consumer |
|---|---|---|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Entry point + AI directives + a *day-to-day summary* of the rules | Pointers, ground-rule summaries (each linking to its INV), where-to-read order | An authoritative rule copy; per-feature detail; history | Every agent session (always loaded) |
| [`architecture/`](architecture/) `00`–`06` | Foundational, cross-cutting, slice-independent design (layers, ECS, events, pitfalls, config, persistence) | The *explanation* of how the engine is shaped | Per-feature framework designs; runtime traces; invariant restatements | Anyone writing a system/handler/event |
| [`architecture/checklist.md`](architecture/checklist.md) | **The only** authoritative invariant list | Every `INV-n` / `SR-n` / `INV-D*`, terse and checkable | Long explanations (those link out to `00`–`06`) | `architecture-reviewer`, planner gates |
| `architecture/subsystems/` | Per-feature framework *design records* (commands, output, future combat/items) | "How feature X's framework is built," scoped to one feature, born in one slice | Cross-cutting rules (→ `00`–`06`); the catalog of what exists (→ `reference/`) | Implementers of/around that feature |
| `architecture/flows/` | Runtime call-chain *traces* (the dynamic axis) | "If X happens, what executes and in what order," one flow per concern | Static structure; design rationale | Anyone tracing behavior; `use-case-planner` |
| [`reference/`](reference/) | Terse catalog of **what exists** | Implemented components/systems/handlers/archetypes/commands | Idealized API for unbuilt features (segregate into a clearly-labeled `*-planned.md` companion file) | Planner ("what can I reuse?"); reviewers |
| [`use-cases/`](use-cases/) | Desired behavior — the *what* | One scenario per file; the per-slice work artifact while in-flight (see lifecycle) | A permanent home for impl plans, flow diagrams, or catalog diffs | Designers; planner; spec-review gate |
| [`roadmap/`](roadmap/) | Direction, status, ledger, deferred work | `plan.md` (strategy/focus), `done.md` (ledger), `completed/` (detail), `backlog.md` (deferred) | Rule definitions; behavior specs (→ `use-cases/`) | Anyone asking "what's next/done/deferred" |
| [`archive/`](archive/) | Retired approaches & point-in-time audits | Superseded designs, kept for history with a banner | Anything currently authoritative | Rarely; historical reference only |
| [`.claude/skills/`](../.claude/skills/) | Recurring **patterns** (how to add a component/command/…) | A short, opinionated restatement of an architecture rule as a how-to | A *fork* of the rule (it links to and tracks the rule) | Agents performing the pattern |
| [`.claude/agents/`](../.claude/agents/) | Recurring **exercises** (review, plan) | Role, workflow, output format for a multi-step exercise | Inline rule copies (agents read `checklist.md` live) | Spawned via the `Agent` tool |
| [`.claude/commands/`](../.claude/commands/) | Recurring **tasks** (one-shot invocations) | A thin wrapper that invokes an agent/skill | A private copy of the wrapped agent's workflow or output format | The user, via `/command` |

## Where each kind of fact lives (single source of truth)

| Fact | Authoritative home | Everyone else |
|---|---|---|
| An architectural rule / invariant | `architecture/checklist.md` | links by `INV-id` |
| Explanation of a layer / ECS / event / config concept | `architecture/00`–`06` | links to the section |
| A per-feature framework's design | `architecture/subsystems/<feature>.md` | links to it |
| A runtime call chain | `architecture/flows/` (one flow) | references "Flow N"; never reproduces the diagram |
| What components/systems/handlers/etc. *exist* | `reference/` (implemented) | links to the row |
| An idealized/planned API not yet built | `reference/<catalog>-planned.md`, clearly labeled | links, never implies it exists |
| Desired behavior | `use-cases/<slug>.md` | links to it |
| Direction / status / ledger | `roadmap/` | links to it |
| A retired approach | `archive/` | links if history is relevant |

## Duplication policy

- **Allowed:** a one-line summary that links to the authority. CLAUDE.md's ground-rule summaries are the canonical example — they exist because CLAUDE.md is always loaded, but each points to its `INV`.
- **Forbidden:** restating an invariant in your own words anywhere but `checklist.md`; reproducing a flow diagram outside `architecture/flows/`; maintaining a second copy of the navigation doc-map.
- **Summary vs. enforcement split:** CLAUDE.md *summarizes*; `checklist.md` *defines*. If they disagree, the checklist wins and the summary is fixed in the same change.

## Use-case lifecycle (trim-on-ship)

A use-case doc moves through three states:

1. **`planned`** — behavior spec drafted; no code.
2. **In-flight** (`planned`/`partial`) — the doc is the *single per-slice work artifact*: behavior spec **plus** implementation plan, "Cross-cutting surfaces stressed," "Flows introduced or modified," and reference-catalog diffs. This fused form is intentional — it is what the `use-case-planner`, the spec-review gate, and `implement-use-case` operate on. Flow content **references `architecture/flows/Flow N`; it never reproduces the diagram.**
3. **`implemented`** — at slice close-out, `sync-roadmap` **trims** the doc to its durable behavior spec: *Status, Actors, Module, Description, Preconditions, Postconditions, Main Flow, Events fired, Design notes, Related*. The implementation-plan, cross-cutting-audit, flow, and catalog-diff sections are **removed** (Design notes stay — they hold non-obvious rationale not captured in code) — they are now authoritative in code, `architecture/flows/`, and `reference/`. A trimmed doc states present truth, not a frozen plan.

A shipped use-case later superseded by a redesign gets a one-line banner pointing to its successor, or is re-trimmed to match the new truth — never left silently stale.

## Maintenance triggers (what to update, when, enforced by what)

| When you… | Update (only) | Enforced by |
|---|---|---|
| add or change an architectural rule | `checklist.md` (+ link from its explanation doc) | `INV-15`, reviewer |
| add/rename/remove a system, handler, component, or event | the matching `reference/` catalog | `INV-16` |
| introduce or change a runtime flow | `architecture/flows/` (body **and** mermaid) | `INV-17` |
| ship a use-case slice | trim the use-case doc; update `done.md` + `completed/`; advance `plan.md` | `sync-roadmap` skill, `INV-D2` |
| repeat a hand-rolled pattern ≥3× | promote it to a framework + skill | `INV-19` |
| change an architectural pattern a skill/agent teaches | that `.claude/` file, same PR | `INV-20` |
| establish a new recurring pattern / exercise / task | a new skill / agent / command | tooling lifecycle (below) |
| restate a rule, reproduce a flow, or fork the doc-map | don't — link instead | `INV-D1` |

> The `INV-D*` (documentation-discipline) series is added to `checklist.md` when this spec is adopted, extending the existing `INV-16/17/19/20` doc rules. A rule this spec implies that should block a merge belongs in the checklist as an `INV-D`, not only here.

## Agent tooling lifecycle (`.claude/`)

**Create** when something recurs:
- a recurring **pattern** → a **skill** (the ≥3× bar from `INV-19` is the trigger);
- a recurring **exercise** → an **agent**;
- a recurring **task** → a **command** (usually a thin wrapper over an agent/skill).

**Maintain:** a skill is a short restatement of an architecture rule *as a how-to*, not a fork — when the rule changes, the skill changes in the same PR (`INV-20`). Agents carry **no** inline rule copy; they read `checklist.md` live. Slash commands **wrap** an agent; they must not carry a private copy of the agent's workflow or output format — they say "invoke the agent; follow your definition."

**Retire:** when a pattern is removed or a skill's guidance is superseded, delete or update it in the same change. Stale tooling produces the next slice's violations. The optional `debt-sweep` agent (see [`roadmap/backlog.md`](roadmap/backlog.md)) is the periodic backstop that also scans `.claude/` for stale guidance; the per-slice `INV-20` check is the integral defense.

## Navigation

The **canonical doc-map** is the "Related Documents" table in [`architecture/00-overview.md`](architecture/00-overview.md). CLAUDE.md's "Where to read next," `roadmap/plan.md`'s "Where to look," and [`.claude/README.md`](../.claude/README.md) link to it — they do not maintain parallel maps.

## Maintaining this file

This spec changes when the *taxonomy* changes — a new bucket, a moved responsibility, a new lifecycle state. Everyday rule changes go to `checklist.md`, not here. If this file and the checklist ever disagree, the checklist wins for *enforcement*; this file is corrected to match.
