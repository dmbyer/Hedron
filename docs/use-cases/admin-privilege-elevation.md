# Use Case: Admin Privilege Elevation

**Status:** deferred / placeholder
**Actors:** Administrator (existing), Player (target of elevation)
**Module:** `Core/Modules/Admin/`

> **This is a placeholder.** It captures the shape of a future slice so the design is recorded but not yet ready to implement. A later `use-case-planner` run should flesh it out, resolve open questions, and promote the status to `planned` before any code is written. Slice number TBD — the queue is in [`../roadmap/plan.md`](../roadmap/plan.md).

---

## Description

Adds a persisted, in-game-grantable layer to the admin authorization model. The bootstrap allowlist (`Admin:PrivilegedNames` in `appsettings.json`) introduced by [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) remains the floor — anyone listed there is always admin. This slice introduces an `AdminPrivilegeComponent` that an existing admin can attach to another player via a new `@grant` (or `@promote`) command. The component is `[Persistent]` so the elevation survives restart. Revocation via `@revoke` is symmetric.

This slice is a pure tooling / authorization slice. No new gameplay verbs, no new domain rules.

---

## Preconditions

- The world-content-loading-and-admin-substrate slice has merged. `IAdminAuthorizer`, `Admin:PrivilegedNames`, and the `@`-prefix command convention exist.
- A real player-account / display-name resolution path exists (likely from the account/character-creation slice) so `@grant <playerName>` can locate the target reliably.
- `PersistenceSystem` is wired (slice 1 — already merged).

---

## Postconditions

- An admin can elevate another player by display name; the target's player entity gains an `AdminPrivilegeComponent` and `IAdminAuthorizer.IsPrivileged` returns true for the target on the next check (and on every subsequent restart).
- An admin can revoke another player's elevation; the component is removed; the target loses admin rights *unless* their name is in `Admin:PrivilegedNames`, in which case the settings floor still grants admin.
- A privileged session cannot revoke its own access if doing so would leave zero admins reachable (safety check — exact rule TBD).
- The grant/revoke action is audited via `AdminAuditHandler` (existing).

---

## Main Flow (sketch)

1. Admin types `@grant <playerName>`. `GrantCommand` verifies caller is privileged via `IAdminAuthorizer`.
2. Resolves `<playerName>` to a player entity id. If not found, error.
3. Attaches `AdminPrivilegeComponent` to the target entity. Publishes `AdminPrivilegeGrantedEvent`.
4. `PersistenceHandler` (existing) marks the target dirty; the next flush persists the component.
5. `AdminAuditHandler` (existing) logs the action.
6. Symmetric flow for `@revoke <playerName>` — detach the component, publish `AdminPrivilegeRevokedEvent`. Settings floor still applies on the next `IsPrivileged` check.

---

## Events Fired (sketch)

| Event | Publisher | Purpose |
|---|---|---|
| `AdminPrivilegeGrantedEvent(uint GrantorEntityId, uint TargetEntityId)` | `GrantCommand` | Audit, persistence dirty-mark, optional notification to the target. |
| `AdminPrivilegeRevokedEvent(uint RevokerEntityId, uint TargetEntityId)` | `RevokeCommand` | Audit, persistence dirty-mark. |

---

## Systems / Handlers Involved (sketch)

- **`AdminPrivilegeComponent`** — new, cross-cutting, `[Persistent]`. Empty marker component (presence == privilege). Lives at `Core/ECS/Components/AdminPrivilegeComponent.cs`. Name to be confirmed against [`../reference/components.md`](../reference/components.md) at planning time.
- **`AdminAuthorizer`** — existing, extended. `IsPrivileged` becomes `(settings allowlist) OR (entity has AdminPrivilegeComponent)`. The interface signature does not change.
- **`GrantCommand` / `RevokeCommand`** — new admin commands. `Core/Modules/Admin/Commands/`.
- **`AdminAuditHandler`** — existing. Adds `AdminPrivilegeGrantedEvent` and `AdminPrivilegeRevokedEvent` to its subscriptions.
- **`PersistenceHandler`** — existing. Adds the two events to its dirty-tracking subscriptions.

---

## Content Tooling Impact

- New admin commands (`@grant`, `@revoke`).
- No new authored data files.
- Inspection tooling: a future `@whois <playerName>` or `@admins` listing command may be useful, but is out of scope for this slice unless the operator-experience pain demands it.

---

## Open Questions (to be resolved when promoted)

- Should self-revocation be allowed? If so, with what safety net?
- Should grant/revoke broadcast a flavour line to the target's session, or be silent?
- Is there a privilege-tier model (e.g. observer / builder / admin), or is the component a simple boolean marker?
- Do we want a parallel `@grant-area <playerName> <areaId>` for scoped builder access, or is that a later slice?
- Should the audit log entry include the grantor's name as well as ids?

---

## Related

- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — establishes the bootstrap allowlist and `IAdminAuthorizer` seam this slice extends.
- [`persistence-substrate.md`](persistence-substrate.md) — provides the `[Persistent]` mechanism the new component plugs into.
- `account-character-creation.md` — provides reliable player-name → entity resolution.

For the slice queue and ordering rationale, see [`../roadmap/plan.md`](../roadmap/plan.md).
