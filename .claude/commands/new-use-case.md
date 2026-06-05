---
description: Turn a gameplay idea into a docs/use-cases/ file + build plan
argument-hint: <short description of the gameplay scenario>
---

Invoke the **use-case-planner** subagent to plan a new use case for Hedron based on this idea: $ARGUMENTS

> For a non-trivial feature, consider `/advise` (the `architecture-advisor` skill) first — it frames the seams and seeds the use-case doc, and the planner will extend that seed. Skip for a small, well-understood slice.

Follow your agent definition exactly — its workflow, the use-case template in [`docs/use-cases/README.md`](../../docs/use-cases/README.md), and its output format are authoritative. This command is a thin wrapper and deliberately does not restate them.
