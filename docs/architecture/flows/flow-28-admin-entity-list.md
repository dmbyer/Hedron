# Flow 28 — Admin Entity List (`list`)

**Command:** `list <area|room>`  
**Actor:** Administrator  
**Module:** `Core/Modules/Admin/`

## Steps

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-admin sessions rejected.
2. **Parse.** `ListCommand` reads the required type token; unknown token → error message, return.
3. **Query.** `EntityService.GetAllComponents<AreaComponent>()` or `GetAllComponents<RoomComponent>()` — direct component scan, no system call.
4. **Format.** `StringBuilder` builds header + one row per entity: Name | Description[:15]+"…" | BlueprintId (or entity ID if no `BlueprintComponent`).
5. **Output.** Single `PlainMessage` written to admin session. No events published.

## Invariants

- INV-10: `ListCommand` is read-only; it publishes no events.
- INV-5: No system call needed — the query is a one-liner component scan with no game-rule logic.
