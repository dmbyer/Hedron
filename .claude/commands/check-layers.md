---
description: Run an architecture review on the current branch's diff against master
argument-hint: [optional scope hint — file, folder, or note]
---

Invoke the **architecture-reviewer** subagent with the following task:

Review the architectural discipline of the current branch's changes against `master`.

Scope hint from the user (use this to focus if given, otherwise review the full diff): $ARGUMENTS

Run your standard workflow:
1. Determine the diff: `git diff master...HEAD --stat` and list changed files under `Core/`, `Server/`, `Data/`.
2. For each changed file, read the full file and check against the rules in your agent definition.
3. Grep the full repo for cross-cutting violations (domain systems publishing events, new `is`/`as` entity checks, new uses of legacy `Player`/`Mob`/`ItemWeapon`/`Storage`/`EntityContainer`/`Room`/`Area`/`World` classes).
4. Verify doc coherence: if new systems/handlers/events/components/use-case updates are missing from `docs/reference/` or `docs/architecture/`, call it out as doc drift.
5. Return the standard concise report: Violations (blocking) / Smells / Doc drift / OK.

Do not write code. Do not modify docs. Report only.
