# `.claude/` — Agent tooling for Hedron

Claude-Code-native helpers tuned for this repo. Everything here is optional — the `/docs` tree stands on its own. These exist to speed up common workflows.

## Skills (`skills/`)

Invoke via model-selected skill triggers or directly in prompts. Each one documents a specific architectural pattern.

| Skill | Use when |
|---|---|
| [`add-component`](skills/add-component/SKILL.md) | Adding a new ECS component |
| [`add-archetype`](skills/add-archetype/SKILL.md) | Introducing a new entity archetype |
| [`add-event`](skills/add-event/SKILL.md) | Adding a new event type |
| [`add-handler`](skills/add-handler/SKILL.md) | Adding or splitting a handler |
| [`add-domain-system`](skills/add-domain-system/SKILL.md) | Adding a feature (domain) system |
| [`add-core-system`](skills/add-core-system/SKILL.md) | Adding a cross-cutting core system |
| [`add-command`](skills/add-command/SKILL.md) | Adding a player or admin command |
| [`implement-use-case`](skills/implement-use-case/SKILL.md) | Implementing a full use case end-to-end |
| [`migrate-legacy-to-ecs`](skills/migrate-legacy-to-ecs/SKILL.md) | Converting legacy inheritance-based code to ECS |

## Subagents (`agents/`)

Launched via the `Agent` tool with `subagent_type` set to the agent's name.

| Agent | Use for |
|---|---|
| [`architecture-reviewer`](agents/architecture-reviewer.md) | Reviewing a diff for 4-layer / ECS / event-bus discipline |
| [`use-case-planner`](agents/use-case-planner.md) | Turning a gameplay idea into a use-case doc + build plan |

## Slash commands (`commands/`)

| Command | Effect |
|---|---|
| `/new-use-case <description>` | Spawn the use-case-planner on your idea |
| `/check-layers [scope]` | Run architecture review on the current branch |

## How these fit together

For a new feature end-to-end:

1. `/new-use-case <describe the feature>` → writes `docs/use-cases/<x>.md` + plan
2. Use `implement-use-case` skill → builds each layer using the other skills as sub-patterns
3. `/check-layers` → architecture-reviewer flags any violations before merge

For a bug fix or small change:

- Just work normally; the skills and docs are on hand if you need them.

## Keep it current

When the rules in `docs/architecture/` evolve, update the skills to match — the skills are a short, opinionated restatement of those rules, not a fork of them. Drift here is worse than no skill at all.
