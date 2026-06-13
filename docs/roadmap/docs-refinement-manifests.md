# Docs-refinement — per-feature migration manifests

> **TRANSIENT working file — delete at WP-Z.** Low-inference source→target maps so a Sonnet 4.6 sub-agent can migrate one feature by *mirroring the committed `effects` exemplar* ([`../features/effects/effects.md`](../features/effects/effects.md) + [`effect-system.md`](../features/effects/effect-system.md)) and following the [`manage-docs`](../../.claude/skills/manage-docs/SKILL.md) skill. Run **sequentially** (features share `reference/*.md`, `flows/README.md`, `done.md` — parallel runs collide). Per feature: write docs → consolidate flows → trim reference rows → verify `completed/` holds decisions → `git rm` the plan → repoint inbound links → run the link checker (in `manage-docs`) until clean.

**Universal rules (every feature):**
- Feature doc = holistic/player-facing/orchestration; system doc(s) = the design meat. Split exactly as `effects.md` vs `effect-system.md`.
- Trim interface **signature dumps** from `reference/*.md` → `.cs` links (keep behavior prose + design tables). Mirror the `EffectSystem` row.
- De-detail each flow to systems/events (mirror the rewritten `flow-21`). Keep flow **filenames numbered** for now (global renumber is WP-Z); retitle the H1 + the `flows/README.md` row.
- Cross-feature "Related" links to **not-yet-migrated** features → non-link mentions (avoid dangling); add the real link when both exist.
- `planned`/`deferred` plans are **NOT** migrated or deleted (they stay in `implementation-plans/`).
- Verify the `completed/<slice>.md` record holds the plan's Design Notes **before** `git rm`; enrich it if anything's missing.

Order: ~~effects ✅~~ → combat → character-stats → items → world → mobs → abilities → aspects → accounts → admin-authoring → output → commands → communication → (persistence + testing infra).

---

## combat/  (modules: Combat, Death, EntityState)
- **Plans to disintegrate+delete:** `combat.md` (verify `completed/slice-9-combat.md`), `death-and-respawn.md` (`completed/slice-10-death-and-respawn.md`), `entity-state-management.md` (`completed/slice-9a-entity-state-management.md`).
- **System docs to write:** `combat-system.md`, `death-system.md`, `entity-state.md` (EntityState is cross-cutting state flags; brief).
- **Feature doc:** `combat.md` — the fight loop + flee + death/incapacitation, player-facing.
- **Flows:** consolidate `flow-17` (kill init) + `flow-18` (round pulse) + `flow-19` (flee) → **combat journey** (keep file `flow-17`, retitle; delete 18, 19). Consolidate `flow-20` (mob death) + `flow-22` (incap/bleedout) + `flow-23` (player death/respawn) → **death & respawn journey** (keep `flow-20`, retitle; delete 22, 23). Update `flows/README.md`.
- **Reference trim:** `CombatSystem`, `DeathSystem`, `IEntityStateService` (systems.md); `CombatStateComponent`, `EntityStateComponent` (components.md); combat/death handlers (handlers.md); `kill`/`flee` (commands.md).
- **Gotcha:** `flow-18` currently has the most over-detail (formulae, exact calls) — de-detail hard. INV-24/contributor + stat reads link to `character-stats` (not yet migrated → non-link mention or link to `reference/systems.md`).

## character-stats/  (modules: Stats, Attributes, Regeneration)
- **Plans to delete:** `stat-system.md` (`completed/slice-9c-stat-system.md`), `stat-resource-substrate.md` (`slice-9d`), `attributes.md` (`slice-8a-attributes-and-vitals.md`), `resource-regeneration.md` (`slice-11c-resource-regeneration.md`).
- **Design-doc move:** `architecture/subsystems/stats.md` → `features/character-stats/stat-system.md` (restructure to system template; fix all links for new depth; trim interface bits).
- **System docs:** `stat-system.md` (← subsystems/stats.md), `attribute-system.md`, `regeneration-system.md`.
- **Feature doc:** `character-stats.md` — attributes, pools/vitals, derived scores, the `score` command, out-of-combat regen.
- **Flows:** none dedicated (stats read transparently; regen rides heartbeat). No journey; link `flow-16` (heartbeat) for regen.
- **Reference trim:** `StatSystem`/`IStatRegistry`, `AttributeSystem`, `RegenerationSystem` (systems.md); `AttributesComponent`, `PoolsComponent` (components.md).
- **Gotcha:** `stat-system.md` is the doc `effect-system.md` already forward-links to — make sure that link resolves after this lands.

## items/  (module: Items — incl. inventory + equipment)
- **Plans to delete:** `items-and-inventory.md` (`slice-6-items-and-inventory.md`), `equipment.md` (`slice-7-equipment.md`).
- **System docs:** `item-inventory-system.md`, `equipment-system.md`.
- **Feature doc:** `items.md` — items, carrying/inventory, wear/remove equipment + worn slots.
- **Flows:** `flow-09` (pickup) + `flow-10` (drop) + `flow-11` (inventory) → **items journey** (keep `flow-09`, retitle; delete 10, 11). `flow-13` (wear) + `flow-14` (remove) → **equipment journey** (keep `flow-13`, retitle; delete 14). `flow-12` (mkitem) is **admin** → leave for admin-authoring.
- **Reference trim:** `IInventorySystem`/`IEquipmentSystem` (systems.md); `InventoryComponent`/`EquipmentComponent`/`ItemDataComponent` (components.md); item/equipment handlers; `get`/`drop`/`inventory`/`wear`/`remove` commands.

## world/  (modules: World, Movement, Spawn, Time)  — heaviest; spans 4 modules
- **Plans to delete:** `world-content-loading-and-admin-substrate.md` (`slice-2-…`; **shared with admin-authoring** — disintegrate the world-loading half here, leave the admin-command catalog to admin-authoring), `area-model.md` (`completed/area-model.md`), `time-system.md` (`slice-9b-time-system.md`), `bare-bones-content-spawning.md` (`slice-5a-…`).
- **System docs:** `world-content.md` (loader/reload/migration), `movement-system.md`, `area-model.md`, `spawn-system.md`, `time-system.md` (heartbeat service).
- **Feature doc:** `world.md` — rooms/areas, movement, spawning, the world tick.
- **Flows:** the runtime-infra flows `flow-01` (startup), `flow-05` (reload), `flow-16` (heartbeat) are **cross-cutting (WP-Z keeps them numbered)** — world *links* them, doesn't own/delete them. No new world journey unless movement warrants one (none today).
- **Reference trim:** `WorldContentLoader`, `IAreaSystem`, `IMovementSystem`, `SpawnSystem`, `IHeartbeatService`/time (systems.md); `RoomComponent`/`AreaComponent`/`LocationComponent` (components.md).
- **Gotcha:** `RoomComponent`/`AreaComponent` persistence rules are INV-23 — link the checklist, don't restate. Time/heartbeat is foundational; keep `time-system.md` design here but link `flow-16`.

## mobs/  (module: Mobs)
- **Plans to delete:** `mobs.md` (`slice-8-mobs.md`).
- **System docs:** `mob-system.md` (mob data + builder; behavior is minimal today).
- **Feature doc:** `mobs.md` — what mobs are, spawning, the combat target surface.
- **Flows:** `flow-15` (mkmob) is **admin** → admin-authoring. No mob-behavior flow today.
- **Reference trim:** `MobBuilderSystem` (systems.md); `MobDataComponent` (components.md); mob handlers.

## abilities/  (module: Abilities)
- **Plans to delete:** `ability-substrate.md` (`slice-11a-ability-substrate.md`), `ability-invocation.md` (`slice-11b-ability-invocation.md`).
- **System docs:** `ability-system.md` (the unified skill/spell kit — activation modes, cost/cooldown, produces Effects via `EffectSystem.Apply`; the `IEffectContributor` for `WhileKnown` passives — link `effects/effect-system.md#the-contributor-seam`).
- **Feature doc:** `abilities.md` — skills & spells as one kit; `cast`/bare-verb invocation.
- **Flows:** `flow-24` (activation) + `flow-25` (skill bare-verb) + `flow-26` (offensive opens combat) → **abilities journey** (keep `flow-24`, retitle; delete 25, 26).
- **Reference trim:** `IAbilitySystem`, `AbilityVerbResolver`, `AbilityEffectContributor` (already trimmed in effects pass) (systems.md); `AbilitiesComponent` (components.md); ability commands.
- **Gotcha:** `flow-25`/`flow-26` have **broken `.cs` links** (pre-existing) — fix them: check actual files under `Core/Modules/Abilities/` (`CastCommand`, `AbilityInvocationPipeline`, `AbilityVerbResolver`, etc.) and link the ones that exist; drop the rest.

## aspects/  (module: Aspects)
- **Plans to delete:** `aspect-foundation.md` (`completed/aspect-foundation.md`).
- **System docs:** `aspect-system.md` (affinity/resistance composition; `IAspectSystem.Affinity`/`Resolve`; the registry).
- **Feature doc:** `aspects.md` — elemental typing of damage/effects.
- **Flows:** none dedicated (resolution is inside the combat round) → link the combat journey.
- **Reference trim:** `IAspectSystem`, `AspectRegistry` (systems.md); `AspectAffinitiesComponent` (components.md).

## accounts/  (modules: Account, Session, Core/Sessions)
- **Plans to delete:** `account-character-creation.md` (`slice-5-account-character-creation.md`).
- **System docs:** `account-system.md`, `login-flow.md` (`LoginFlow` orchestration). **Close the Core/Sessions gap:** add an `ISession`/`ISessionManager` reference entry to `reference/systems.md` (per the WP-Z audit) and mention it here.
- **Feature doc:** `accounts.md` — account + character creation, login, session lifecycle.
- **Flows:** `flow-07` (login/character) → **login journey** (keep `flow-07`, retitle; consider folding `flow-02` player-connection if it's purely account — else leave `flow-02` as infra). 
- **Reference trim:** `AccountSystem`, `IPasswordHasher` (systems.md); `AccountComponent`/`CharacterComponent` (components.md).
- **Gotcha:** `AccountSystem` has acknowledged wall-clock debt (INV-26) — link backlog, don't restate.

## admin-authoring/  (modules: Admin, Authoring)
- **Plans to delete:** `content-authoring-editor.md`, `content-tooling-platform.md` (`completed/content-tooling-platform.md`), `bulk-content-generation.md`. **Leave** `admin-area-authoring.md` (planned) and `admin-privilege-elevation.md` (deferred). The admin-command catalog from `world-content-loading-and-admin-substrate` (`slice-2`) is referenced here but that plan is deleted by **world**.
- **System docs:** `admin-commands.md` (dig/mkitem/mkmob/mkarea/set/list — the builder verbs + privilege gate), `content-authoring.md` (the Blazor editor — link `08-blazor.md`), `content-tooling.md` (`IContentDefinitionCatalog`/`IContentValidator`/`generate` run-mode).
- **Feature doc:** `admin-authoring.md` — building/editing world content in-game + the offline editor.
- **Flows:** `flow-08`(dig)+`flow-12`(mkitem)+`flow-15`(mkmob)+`flow-27`(mkarea)+`flow-28`(list) → **admin authoring journey** (keep `flow-08`, retitle; delete 12,15,27,28). `flow-29` (bulk generate) + `flow-30` (offline edit) → **content-tooling journey** (keep `flow-29`, retitle; delete 30).
- **Reference trim:** admin/authoring systems + the mk*/set/list commands (commands.md).
- **Gotcha:** privilege gate is structural (no `@` prefix — see memory/`feedback_admin_command_prefix`). Blazor discipline links `08-blazor.md`.

## output/  (module: Prompt + Core/Output)
- **Plans to delete:** `output-framework.md` (`slice-4-output-framework.md`), `prompt-and-output-batching.md` (`completed/output-batching.md`). *(Confirm: `prompt-and-output-batching` shows `Status: implemented`; its completed record is `output-batching.md`.)*
- **Design-doc move:** `architecture/subsystems/output.md` → `features/output/output-framework.md`.
- **System docs:** `output-framework.md` (← subsystems/output.md — `IOutputMessage` catalog, formatter pipeline, broadcast, batching), `prompt.md`.
- **Feature doc:** `output.md` — how the game talks to the player (typed messages, color, prompt, batching).
- **Flows:** `flow-06` (output rendering) → **output journey** (keep `flow-06`, retitle, de-detail).
- **Reference trim:** `BroadcastSystem`, output infrastructure (systems.md); the `*Message` shapes are design — keep concise.

## commands/  (module: Core/Commands)
- **Plans to delete:** `command-framework.md` (`slice-3-command-framework.md`), `command-prefix-matching.md` (`slice-3a-…`).
- **Design-doc move:** `architecture/subsystems/commands.md` → `features/commands/command-framework.md`.
- **System docs:** `command-framework.md` (← subsystems/commands.md — `ICommand`, arg schema, resolvers, dispatcher, prefix matching, privilege gate).
- **Feature doc:** `commands.md` — how a player verb becomes an action.
- **Flows:** `flow-03` (player command lifecycle) → **command journey** (keep `flow-03`, retitle, de-detail).
- **Reference trim:** dispatcher/parser/resolvers (systems.md); the command catalog in commands.md stays (it's the catalog).
- **Gotcha:** `INV-11` (no direct `session.SendLineAsync`) explanation currently points at `implementation-plans/command-framework.md` (checklist line 47) — repoint to `features/commands/command-framework.md` when this lands.

## communication/  (modules: Chat, Help)
- **Plans to delete:** none (Chat/Help shipped without their own plan).
- **System docs:** `chat-system.md` (say/channels surface), `help-system.md` (help index/entries).
- **Feature doc:** `communication.md` — player communication + in-game help.
- **Flows:** none.
- **Reference trim:** chat/help systems + `say`/`help` commands + help message shapes.
- **Gotcha:** broadcast channel mode is acknowledged backlog debt — link it, don't restate. Help may later split from chat (note in feature doc).

---

## Infra disintegration (not feature folders — fold into WP-Z)
- **Persistence:** delete `persistence-substrate.md` (`slice-1-…`) + `persistence-two-level-model.md` (`slice-5b-…`) — forward content already in `architecture/06-persistence.md`; verify, then `git rm`. **Leave** `persistence-reform.md` (planned). `flow-04` (persistence flush) stays as a numbered infra flow.
- **Testing:** delete `testing-harness-and-backfill.md` — home is `architecture/07-testing.md` + `completed/testing-harness-and-backfill.md`; verify, then `git rm`.
- **Cross-cutting runtime flows kept (numbered):** `flow-01` startup, `flow-02` connection, `flow-04` persistence-flush, `flow-05` reload, `flow-16` heartbeat. De-detail them; finalize `flows/README.md`; the numbered-vs-journey naming reconciliation is the WP-Z call.
