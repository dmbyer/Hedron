---
description: Turn a gameplay idea into a docs/use-cases/ file + build plan
argument-hint: <short description of the gameplay scenario>
---

Invoke the **use-case-planner** subagent with the following task:

Plan a new use case for Hedron based on this idea: $ARGUMENTS

Follow your standard workflow:
1. Check `docs/use-cases/` for overlap with existing cases.
2. Read the reference catalogs in `docs/reference/` before inventing names.
3. Write a new `docs/use-cases/<slug>.md` using the template defined in `docs/use-cases/README.md`.
4. Return a concise build plan (components, systems, events, handlers, commands) grouped by layer, marking reuse vs. new.
5. Call out any open questions I should decide before implementation begins.

Do not write C# code. The output is the use-case file + the plan.
