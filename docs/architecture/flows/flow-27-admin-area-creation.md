# Flow 27 — Admin Area Creation (`mkarea`)

**Command:** `mkarea [name]`  
**Actor:** Administrator  
**Module:** `Core/Modules/Admin/`

## Steps

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-admin sessions rejected.
2. **Parse.** `MkareaCommand` reads rest-of-line name; defaults to `"New Area"` if absent.
3. **Create.** `IAreaBuilderSystem.CreateArea(name)` generates `area.adhoc.<base36>`, creates entity with `AreaComponent` + `BlueprintComponent`, registers `AreaTemplate` in `TemplateRegistry`. Returns `AreaCreationResult`.
4. **Persist.** `IAreaContentWriter.WriteAsync(result.Template)` writes `content/areas/<blueprintId>.yaml` atomically (tmp → rename). File is written before the audit event fires.
5. **Event.** `MkareaCommand` publishes `AreaCreatedByAdminEvent { AdminEntityId, AreaEntityId, BlueprintId }`. `AdminAuditHandler` logs it.
6. **Confirm.** Command writes blueprint ID to admin's session.

## Invariants

- INV-5: `AreaBuilderSystem` returns results; it never publishes events.
- INV-23: Area entities carry no `PersistentEntity` (world content, not player state).
- INV-1: Writer (`World` layer) called by command (`Admin` layer) — no downward call from World into Admin.
