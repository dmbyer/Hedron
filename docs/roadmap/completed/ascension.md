# Ascension / character-wide tier — slice prog-2 (completed)

> Implemented on branch `claude/reverent-cannon-8a233d`, 2026-07-05. Living docs: [`features/progression/progression.md`](../../features/progression/progression.md) · [`features/progression/ascension-system.md`](../../features/progression/ascension-system.md).

## Outcome

A character-wide Tier scalar (`AscensionComponent.Tier`, `int 0–6`) landed as a new `Ascension` module, confering a flat additive power baseline across `Body`/`HpMax`, pulled on read into `IStatSystem.Get` through a fourth `IEffectContributor` registrant (`AscensionEffectContributor`) — the same contribute-on-read seam slice prog-1 proved. Tier-up runs through an admin `ascend` command (the real player-facing Ascension-Objective gate is deferred); ascending records an (currently empty) unlock table and publishes `AscendedEvent`. Mobs carry a lightweight, authored `TierBand` tag (`setmob band`) so content threat stays emergent from the baseline rather than a separate multiplier. This is slice 2 of the five-slice Progression & Balance program; slices 3–5 (power-budget oracle, sim harness, agentic layer) remain unbuilt.

## Behavior digest

- **Tier default & bounds:** `GetTier` returns 0 for an entity with no component (safe default, creates nothing); `TryAscend` clamps `[0, MaxTier=6]` and is a no-op at max, returning a typed `AtMaxTier` failure reason.
- **Additive baseline fold (INV-24):** `IStatSystem.Get(entity, score)` includes `TierBaselineStep × tier` on top of base + equipment + abilities + progression, pulled fresh from `AscensionEffectContributor` — no `EffectsComponent` entry, no cached field.
- **No reset on ascend:** ascending mutates only `AscensionComponent.Tier` (and unlock-record state); `ProgressionComponent.Xp`/`Improvements` are untouched — the progression power is preserved, the baseline layers on top.
- **Eligibility seam:** `CanAscend` returns `Eligible` or a typed reason (`AtMaxTier`) — the admin path bypasses the deferred Objective gate; the shape is the seam a future objectives slice fills.
- **Unlock-record seam:** `TryAscend` records the new tier's configured unlock ids onto `GrantedUnlocks` idempotently; `AscensionConstants.UnlocksForTier` is an **empty** table — nothing is recorded yet, but the durable state + accessor + `AscendedEvent` a future grant handler consumes all ship now.
- **Events:** exactly one `AscendedEvent(entityId, newTier, previousTier)` and one `PlayerAscendedByAdminEvent` per successful ascend, published by `AscendCommand` (never the system); a rejected/no-op ascend publishes nothing.
- **Persistence:** `AscensionComponent` is `[Persistent]`, attached lazily to a persistent (player) entity on first successful ascend — never world content. The `ascend` command performs exactly one `SaveEntityAsync` (case-b admin boundary save, INV-22), paired with the audit event.
- **Content band tag:** `MobTemplate.TierBand` round-trips through YAML; the live `MobDataComponent.TierBand` is sourced from the template at spawn. Because mob entities never carry `PersistentEntity`, the band never reaches a snapshot despite `MobDataComponent` being `[Persistent]` — its durable form is the YAML template.
- **Main flow:** privileged session issues `ascend [characterName]` → `AscendCommand` resolves target → `IAscensionSystem.CanAscend`/`TryAscend` → one boundary save → publish `AscendedEvent` + `PlayerAscendedByAdminEvent` → `AscensionNarrationHandler`/`AdminAuditHandler` fan out (priority 80) → later `IStatSystem.Get` reflects the baseline.

## Shipped pieces

| Surface | Location |
|---|---|
| `AscensionComponent` (`[Persistent]`) | `Core/Modules/Ascension/Components/AscensionComponent.cs` |
| `IAscensionSystem` / `AscensionSystem` | `Core/Modules/Ascension/Systems/IAscensionSystem.cs` · `AscensionSystem.cs` |
| `AscensionConstants` | `Core/Modules/Ascension/AscensionConstants.cs` |
| `AscensionEffectContributor` (`IEffectContributor`) | `Core/Modules/Ascension/AscensionEffectContributor.cs` |
| `AscendedEvent` / `PlayerAscendedByAdminEvent` | `Core/Modules/Ascension/Events/` |
| `AscendCommand` | `Core/Modules/Ascension/Commands/AscendCommand.cs` |
| `AscensionNarrationHandler` | `Core/Modules/Ascension/Handlers/AscensionNarrationHandler.cs` |
| `AscensionModule.AddAscensionModule` | `Core/Modules/Ascension/AscensionModule.cs` — registered in `Server/CompositionRoot.cs`; handler + audit event subscribed in `Server/Program.cs` |
| `AdminAuditHandler` extension | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` — new `PlayerAscendedByAdminEvent` subscription |
| `MobDataComponent.TierBand` | `Core/ECS/Components/MobDataComponent.cs` |
| `MobTemplate.TierBand` | `Core/Modules/Mobs/Templates/MobTemplate.cs` |
| `IMobBuilderSystem.SetMobBand` / `MobBuilderSystem.SetMobBand` | `Core/Modules/Mobs/Systems/IMobBuilderSystem.cs` · `MobBuilderSystem.cs` |
| `SetMobCommand` `band` property branch | `Core/Modules/Mobs/Commands/SetMobCommand.cs` |
| `MobContentWriter` / `MobTemplateDeserializer` `band:` YAML field | `Core/Modules/Mobs/Systems/MobContentWriter.cs` · `Core/Modules/Mobs/MobTemplateDeserializer.cs` |
| Blazor `MobEditor` band field | `Hedron.Web/Components/Pages/MobEditor.razor` |

## Tests shipped

- **Tier 1** — `AscensionSystemTests` (tier default, ascend clamp/no-op-at-max, eligibility, unlock-record stability, no-reset-on-progression) and `AscensionEffectContributorTests` (modifier fold, never-materialized, additive-on-top-of-progression) in `Hedron.Tests/Ascension/`.
- **Tier 2** — `AscendCommandTests` (success publishes exactly one `AscendedEvent` + one audit event + one save; at-max-tier publishes/saves nothing; omitted-name defaults to invoker; player-not-found no-ops); `SetMobBand` unit tests added to `Hedron.Tests/Authoring/MobBuilderSystemTests.cs`; `SetMobCommandBandTests` (dual-write, out-of-range/negative rejection).
- **Tier 3** — `AscensionFlowTests` (real `IAscensionSystem` + `AscendCommand` + dispatching bus + fake persistence; asserts tier increment, baseline-fold delta, event publication, and the embedded functional-validation gate: a Tier-1-banded mob fixture is "deadly" pre-ascend and "medium" post-ascend via the baseline delta).
- **Tier 4** — `MobTierBandRoundTripTests` (write→YAML→read for a representative value, zero/absent-key round-trip, out-of-range and negative band values logged-and-defaulted, `Apply` seeding); two additions to `Hedron.Tests/Persistence/RoundTripTests.cs` (`AscensionComponent` round-trip; mob fixture never carries it and its band is absent from any snapshot).
- **Tier 5** — covered by the existing architecture-guard reflection suite (no new guards needed): no `IEventBus` field on `AscensionSystem`, DI-smoke resolves the new registrations.
- `dotnet test` green — 963 tests total (up from 957 pre-slice, plus the 6 round-trip tests added during the code-review gate).

## Decisions

- **Additive baseline, no reset (brief open-Q#1 → resolved).** Tier confers a flat additive power baseline; the XP-reset/rescale-on-ascend mechanic the per-attribute design once needed is dropped entirely — the additive baseline does the "fresh climb" work on its own.
- **New `Core/Modules/Ascension/` module, not folded into Progression (R1).** `AscensionComponent` is a scalar + unlock-record state, matching the gameplay-model's resolved decision.
- **Tier baseline rides the existing `IEffectContributor` port (brief open-Q#5 → resolved).** A fourth registrant alongside equipment, abilities, and progression — no new `IScalingSystem`/Spine-D seam. `AscensionSystem`'s backing input is raw `AscensionComponent.Tier`, never `IStatSystem`/`IEffectSystem`, confirming the DI-cycle guardrail `ProgressionSystem` established a second time.
- **Tier-up gate = admin `ascend` + `CanAscend`/`TryAscend`; the real Ascension-Objective gate is deferred** (`IObjectiveSystem` is unbuilt). `CanAscend`'s shape is the seam a future objectives slice fills.
- **Band tagging = mobs only, a lightweight int, not an enum.** `TierBand` is a plain `int 0–6`; item bands are deferred to slice prog-3 alongside the power-budget oracle that consumes them — adding item-band authoring now with no reader would be over-build.
- **Unlocks = seam only.** `AscensionComponent.GrantedUnlocks` + `GetGrantedUnlocks` + `AscendedEvent` ship with an empty `UnlocksForTier` table; the grant-execution seam (`GrantFlag`/`GrantAbility` are unimplemented `EffectKind` values) and concrete unlock content are deferred.
- **`AscendCommand`'s constructor has no null-guards**, matching the `SetwalletCommand` precedent (rather than `SetRespawnCommand`'s guarded style) — both patterns pre-exist in the codebase; the architecture-reviewer code-review gate confirmed this is a pre-existing inconsistency, not a regression, and out of scope to reconcile here.
- **INV-15 doc fix landed in this slice.** `docs/design/gameplay-model.md` (~lines 443, 524) and `docs/reference/components-planned.md:13` both carried a stale "`IdentityComponent.Tier` already exists as the seed" note — there is no `IdentityComponent` in code. Both now point at the real `AscensionComponent`.

## Deviations / Follow-ups

- **No deviations from the spec-passed plan.** All three work packages (module core, command/narration/audit, mob tier-band tag) shipped as scoped; the Test plan's five tiers are all present.
- **Code-review addition:** the initial diff's mob-band test coverage only exercised the in-memory dual-write (`SetMobBand`); the architecture-reviewer code-review gate flagged the missing file-based YAML round-trip (write → real file → read) and the untested out-of-range-band warn-and-default path in `MobTemplateDeserializer`. Both were added (`MobTierBandRoundTripTests.cs`, mirroring `MobProtectionRoundTripTests.cs`) before merge.
- **Follow-up (backlog):** the unlock-grant execution seam + concrete unlock content; the player-facing Ascension-Objective gate (`IObjectiveSystem`); item tier-bands + the `IPowerBudgetSystem` oracle that consumes them (slice prog-3); a possible future Spine-D `IScalingSystem` that could subsume the baseline computation without changing callers. Tracked in [`../backlog.md`](../backlog.md).
