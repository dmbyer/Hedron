---
description: Run an architecture review on the current branch's diff against master
argument-hint: [optional scope hint — file, folder, or note]
---

Invoke the **architecture-reviewer** subagent in **code mode** against the current branch's diff (`git diff master...HEAD`).

Scope hint from the user (focus here if given; otherwise review the full diff): $ARGUMENTS

Follow your agent definition exactly — its workflow, the [`docs/architecture/checklist.md`](../../docs/architecture/checklist.md) invariants it cites, and its output format are authoritative. This command is a thin wrapper and deliberately does not restate them.
