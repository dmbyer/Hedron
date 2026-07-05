# `.claude/` — Agent tooling for Hedron

Claude-Code-native helpers tuned for this repo. Everything here is optional — the `/docs` tree stands on its own. These exist to speed up common workflows.

For how the docs themselves are organized — what each surface owns and the discipline that keeps them current — see [`docs/architecture/09-documentation.md`](../docs/architecture/09-documentation.md); the canonical doc-map is the Related Documents table in [`docs/architecture/00-overview.md`](../docs/architecture/00-overview.md).

## Skills (`skills/`)

Invoke via model-selected skill triggers or directly in prompts. Each one documents a specific architectural pattern or planning exercise.

| Skill | Use when |
|---|---|
| [`architecture-advisor`](skills/architecture-advisor/SKILL.md) | Framing a feature's architecture *before* planning — where the seams belong, what future work pulls on them; the interactive principal-architect intake |
| [`add-component`](skills/add-component/SKILL.md) | Adding a new ECS component |
| [`add-archetype`](skills/add-archetype/SKILL.md) | Introducing a new entity archetype |
| [`add-event`](skills/add-event/SKILL.md) | Adding a new event type |
| [`add-handler`](skills/add-handler/SKILL.md) | Adding or splitting a handler |
| [`add-domain-system`](skills/add-domain-system/SKILL.md) | Adding a feature (domain) system |
| [`add-core-system`](skills/add-core-system/SKILL.md) | Adding a cross-cutting core system |
| [`add-command`](skills/add-command/SKILL.md) | Adding a player or admin command |
| [`add-tests`](skills/add-tests/SKILL.md) | Writing tests for a slice — picking the tier, the shared harness, the test-vs-skip rubric (INV-25/26) |
| [`edit-progression-system`](skills/edit-progression-system/SKILL.md) | Extending or tuning experience-driven progression — adding an XP source, adjusting curves/anti-grind, adding a track, or promoting triggers to a rule table |
| [`implement-plan`](skills/implement-plan/SKILL.md) | Implementing a full use case end-to-end |
| [`sync-roadmap`](skills/sync-roadmap/SKILL.md) | Updating plan.md, done.md, and completed/ after a slice merges; disintegrating the shipped plan into the living docs |
| [`manage-docs`](skills/manage-docs/SKILL.md) | Creating/updating/moving any docs — the taxonomy, the templates, the disintegrate-on-ship lifecycle, link discipline |

## Subagents (`agents/`)

Launched via the `Agent` tool with `subagent_type` set to the agent's name.

| Agent | Use for |
|---|---|
| [`architecture-reviewer`](agents/architecture-reviewer.md) | Reviewing a diff for 4-layer / ECS / event-bus discipline |
| [`implementation-planner`](agents/implementation-planner.md) | Turning a gameplay idea into an implementation plan + build plan |

## Slash commands (`commands/`)

| Command | Effect |
|---|---|
| `/advise <description>` | Principal-architect intake: frame a feature's seams + future-proofing, seed its implementation plan |
| `/new-plan <description>` | Spawn the implementation-planner on your idea |
| `/check-layers [scope]` | Run architecture review on the current branch |

## How these fit together

For a new feature end-to-end:

1. `/advise <describe the feature>` → interactive principal-architect intake; frames the seams, weighs existing + planned work, and seeds `docs/implementation-plans/<x>.md` with an architectural brief (skip for a small, obvious slice)
2. `/new-plan` → implementation-planner extends the seed into the full plan; then the spec-review gate (`architecture-reviewer` in spec mode)
3. Use `implement-plan` skill → builds each layer using the other skills as sub-patterns (incl. `add-tests` for the plan's Test plan)
4. `/check-layers` → architecture-reviewer flags any violations before merge (incl. INV-25 test presence + `dotnet test` green)

For a bug fix or small change:

- Just work normally; the skills and docs are on hand if you need them.

## Keep it current

When the rules in `docs/architecture/` evolve, update the skills to match — the skills are a short, opinionated restatement of those rules, not a fork of them. Drift here is worse than no skill at all.
