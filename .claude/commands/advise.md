---
description: Principal-architect intake — frame a feature's architecture (seams, future-proofing) and seed its use-case doc before planning
argument-hint: <short description of the feature or change to frame>
---

Run the **architecture-advisor** skill ([`.claude/skills/architecture-advisor/SKILL.md`](../skills/architecture-advisor/SKILL.md)) on this feature: $ARGUMENTS

Follow the skill definition exactly — its method, the docs it reads live, its interactive probing, its use-case-doc seed + architectural-brief output, and its handoff to `/new-use-case`. This command is a thin wrapper and deliberately does not restate them.
