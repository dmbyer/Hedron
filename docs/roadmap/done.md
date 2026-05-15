# Done

> Short ledger of completed roadmap items. One line per item; full detail in [`completed/`](completed/). For what's next, see [`plan.md`](plan.md).

| Phase / slice | Outcome | Detail |
|---|---|---|
| **Phase 1 — Strip** | Demolished legacy code; bumped to `net8.0`; scrapped Blazor; removed `Data` and `Bot` projects; kept ECS primitives only where they matched the idealized API. | [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **Phase 1.5 — Ticket A (ECS redesign)** | One-world model, `Entity(uint Id)` wrapper, `TemplateRegistry`, archetypes restricted to validation + detection, `[Persistent]` per-component, effects split persistent/transient. | Folded into [`completed/phase-1-strip.md`](completed/phase-1-strip.md) |
| **Phase 2 — Foundation / MVP** | Built target architecture from scratch; shipped multi-user telnet walking simulator with `look`, six-direction movement, and `say`. Acceptance test passed. | [`completed/phase-2-mvp.md`](completed/phase-2-mvp.md) |
| **Phase 3 slice 1 — Persistence substrate** | `PersistenceSystem` with `[Persistent]`-tagged components, `System.Text.Json` serializer, dirty-tracking, atomic writes, silent hydration with `EntityHydratedEvent` / `WorldLoadedEvent`. No gameplay change; unlocks every subsequent slice. | [`completed/slice-1-persistence-substrate.md`](completed/slice-1-persistence-substrate.md) · spec: [`use-cases/persistence-substrate.md`](../use-cases/persistence-substrate.md) |
| **Phase 3 slice 2 — World content loading + admin substrate** | YAML content pipeline (`TemplateRegistry` + per-module `ITemplateDeserializer`); `WorldContentLoader` with empty-dir void fallback; admin substrate (`IAdminAuthorizer` + `@spawn`/`@teleport`/`@dig`/`@reload`); `AdminAuditHandler`; hardcoded `WorldBootstrap` removed. Resolves Ticket B. | [`completed/slice-2-world-content-and-admin-substrate.md`](completed/slice-2-world-content-and-admin-substrate.md) · spec: [`use-cases/world-content-loading-and-admin-substrate.md`](../use-cases/world-content-loading-and-admin-substrate.md) |
