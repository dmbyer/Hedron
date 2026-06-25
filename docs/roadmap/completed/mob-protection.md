# Mob protection — slice 12b (completed)

> Implemented on branch `claude/unruffled-nightingale-9fa6e0`, 2026-06-24. Living docs: [`mobs`](../../features/mobs/mobs.md#protection-invulnerability--immunity) (configuration owner), with gates documented in [`combat`](../../features/combat/combat.md) and [`effects`](../../features/effects/effects.md).

## Outcome

Any entity may now be configured **invulnerable** along two independent axes via the cross-cutting `ProtectionComponent` (`[Flags] ProtectionFlags { None, Untargetable, EffectImmune }`): `Untargetable` refuses attacks, `EffectImmune` rejects every effect (beneficial *and* harmful). Each refusal is owned by its rule's domain — the combat system gates attacks through the shared `ICombatSystem.CanBeAttacked` query, the effect system gates `Apply` with a new structured `EffectApplyResult` — so invulnerability is a general entity primitive, not a trade or mob concern. The flags are authored on the mob template (`setmob protection`, the Blazor `MobEditor`, YAML round-trip) and are non-persistent world content. This is the cross-cutting slice that Shopping (12c) consumes to protect safe-area shopkeepers; it wires no shopping code.

## Behavior digest

*As-specified snapshot (the authoritative present-truth lives in the [mobs feature doc](../../features/mobs/mobs.md#protection-invulnerability--immunity)).*

**Preconditions:** mob authoring exists (`setmob`, `MobEditor`, `MobTemplate` round-trip — slices 6/8); combat initiation flows through `KillCommand` → `ICombatSystem` and the ability path through `AbilityInvocationPipeline` → `ICombatSystem`; effect application flows through `IEffectSystem.Apply` (called by `AffectCommand` and `AbilitySystem.Activate`); a target mob is resolvable in the player's room.

**Postconditions:**
- `ProtectionComponent` + `[Flags] ProtectionFlags { None, Untargetable, EffectImmune }` exist under `Core/ECS/Components/`, **not** `[Persistent]`.
- `ICombatSystem.CanBeAttacked(uint targetEntityId)` returns `false` iff the target carries `ProtectionComponent` with `Untargetable` set; `true` otherwise (including no component).
- `KillCommand` and the offensive-target path of `AbilityInvocationPipeline` both consult `CanBeAttacked` before entering combat / resolving a strike; on refusal neither transitions state, attaches `CombatStateComponent`, deducts HP, nor publishes `CombatStartedEvent` / `AbilityStrikeResolvedEvent`.
- `IEffectSystem.Apply` returns a structured `EffectApplyResult` (`Applied(Effect)` / `NotApplied(reason)`); with `EffectImmune` set it returns the immune result (no `Effect`, no `EffectsComponent` change) for **both** beneficial and harmful definitions.
- `AffectCommand` and `AbilitySystem.Activate` surface the immune result (no `EffectAppliedEvent` for an immune target); `AbilitySystem.Activate` excludes the immune effect from its returned `AppliedEffects` (it never publishes — INV-5).
- The actor receives a clear refusal message in each gate; an unprotected mob behaves exactly as before.
- `IMobBuilderSystem.SetMobProtection` dual-writes the live `ProtectionComponent` and `MobTemplate.Protection`; `setmob <id> protection <flags>` and the `MobEditor` author it; `MobTemplate.Protection` survives the YAML write→read round-trip; a re-spawned mob carries no `PersistentEntity` and no SQLite row for the component.

**Main-flow summary:** *Gate A* — `kill shopkeeper` (or an offensive cast/skill) resolves the target, calls `CanBeAttacked`, and on `Untargetable` writes a refusal message and returns before any state change. *Gate B* — `affect shopkeeper empower` (or a routed debuff) calls `IEffectSystem.Apply`, which reads `EffectImmune` at entry and returns `Immune` regardless of effect category; the caller surfaces "immune" and publishes no `EffectAppliedEvent`. *Authoring* — `setmob <bp> protection untargetable,effectimmune` parses the flag tokens, calls `SetMobProtection` (dual-write), reuses the existing publish + YAML-persist path; on reload `MobTemplate.Apply` seeds the component only when flags ≠ `None`.

## Shipped pieces

| Surface | Location |
|---|---|
| `ProtectionComponent` + `[Flags] ProtectionFlags` — cross-cutting, **not** `[Persistent]` | `Core/ECS/Components/ProtectionComponent.cs` (new) |
| `EffectApplyResult` (`Applied`/`NotApplied(reason)`) + `EffectNotAppliedReason` | `Core/Modules/Effects/Effect.cs` |
| `ICombatSystem.CanBeAttacked(uint)` + impl (shared query, ≥2 consumers — INV-19) | `Core/Modules/Combat/Systems/ICombatSystem.cs`, `CombatSystem.cs` |
| Gate A refusal at the two initiators | `Core/Modules/Combat/Commands/KillCommand.cs`, `Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs` |
| Gate B immune path on `Apply` (return-contract change) | `Core/Modules/Effects/Systems/IEffectSystem.cs`, `EffectSystem.cs` |
| Immune-result surfacing | `Core/Modules/Effects/Commands/AffectCommand.cs`, `Core/Modules/Abilities/Systems/AbilitySystem.cs` |
| `IMobBuilderSystem.SetMobProtection` dual-write (mirrors `SetMobType`) | `Core/Modules/Mobs/Systems/IMobBuilderSystem.cs`, `MobBuilderSystem.cs` |
| `MobTemplate.Protection` field + `Apply` opt-in seed (flags ≠ `None`) | `Core/Modules/Mobs/Templates/MobTemplate.cs` |
| `protection` YAML round-trip (flag-name list; log-and-skip unknowns) | `Core/Modules/Mobs/Systems/MobContentWriter.cs`, `Core/Modules/Mobs/MobTemplateDeserializer.cs` |
| `setmob <bp> protection <flags>` branch + help text | `Core/Modules/Mobs/Commands/SetMobCommand.cs` |
| Protection fieldset (two checkboxes) | `Hedron.Web/Components/Pages/MobEditor.razor` |
| Catalog + flow + design-doc updates | `docs/reference/components.md`, `docs/reference/systems.md`, `docs/features/effects/effect-system.md`, `docs/architecture/flows/flow-17-*`, `flow-21-*`, `flow-24-*` |

No new event, handler, or flow file — both gates are command/apply-time refusals; authoring reuses `MobPropertySetByAdminEvent` and the existing `setmob` publish + YAML-persist path.

## Tests shipped

`dotnet test Hedron.sln` green — **828 tests, 0 failures** (up from 813 after WP-1's intermediate green; 801 → 828 net over the slice).

- **System-unit — `CombatSystem.CanBeAttacked`** (`Hedron.Tests/Protection/`): `false` for `Untargetable`; `true` for `EffectImmune`-only; `true` for no component / `None`.
- **System-unit — `EffectSystem.Apply` immune path:** `Immune` for a harmful definition and for a beneficial (positive-magnitude) definition with `EffectImmune` set (no `Effect`/`EffectsComponent` change); `Applied` for an unprotected target (regression) and for `Untargetable`-only (axis independence).
- **Handler/flow — Gate A:** `KillCommand` against an `Untargetable` mob attaches no `CombatStateComponent`, makes no state transition, publishes **no `CombatStartedEvent`**, and writes a refusal message; passes on an unprotected mob.
- **Handler/flow — Gate B:** `AffectCommand` against an `EffectImmune` mob publishes **no `EffectAppliedEvent`** and writes the immune message.
- **System-unit — `MobBuilderSystem.SetMobProtection`** (`Hedron.Tests/Authoring/`): dual-writes live `ProtectionComponent` flags + `MobTemplate.Protection` (add/update/remove cases).
- **Tier-4 round-trip — `MobTemplate.Protection`** (`Hedron.Tests/Modules/Mobs/`): write→YAML→read preserves the flag set; `None`/absent ⇒ no component on `Apply`; unknown flag name skipped. (Template-YAML round-trip, not SQLite — the component is non-`[Persistent]`.)
- **Architecture-guard:** `ProtectionComponent` added to `World_content_components_are_not_persistent`.
- **Skipped (per rubric):** exact refusal/immune prose; the `MobEditor.razor` binding (thin UI, covered by catalog round-trip); `ProtectionFlags` enum values (pure data); the `setmob` arg-parse plumbing beyond the system-level dual-write.

## Decisions

- **Two independent axes, not one bundled bool.** `Untargetable` and `EffectImmune` are separate `[Flags]` so a future entity can have one without the other (a boss immune to crowd-control but killable; a passive NPC affectable but un-attackable). A protected shopkeeper sets both.
- **The flag is data; the refusal lives with each rule's domain (mechanism vs. consequence).** `ProtectionComponent` carries no logic. The attack refusal is a combat-domain decision (`CanBeAttacked`); the effect refusal is `EffectSystem`'s (`Apply` returns immune). Putting the gate in each owning domain — not a shopping or mob system — is what keeps invulnerability a general entity property.
- **Cross-cutting, not shopping-owned.** Invulnerability/immunity is the general primitive behind safe-zone NPCs, future PvP-safe players, room "safe" flags, and sanctuary effects. It ships in its own slice precisely so Shopping (12c) *consumes* it rather than *owning* it.
- **Effect immunity blocks beneficial effects too.** Per explicit design intent, an `EffectImmune` entity rejects positive effects (heals, buffs) as well as negative — protection means "nothing lands," not "nothing harmful lands."
- **Refuse at target/initiation, don't absorb.** An attack on an `Untargetable` mob is refused at resolution with a message, not resolved as a zero-damage hit — clearer to the player and cheaper than a no-op round.
- **Structured `EffectApplyResult`, not overloaded `null`.** `Apply` previously returned `Effect?` where `null` already meant "out-stacked." An immune refusal needs a *distinguishable* reason, so the slice introduced `EffectApplyResult { Applied | NotApplied(reason) }` rather than overloading `null` — the effect-system result-contract gap, closed in-slice (INV-16/INV-28 updated `effect-system.md` in the same PR).
- **Component is cross-cutting and non-persistent** (mob world content): its durable form is `MobTemplate.Protection` YAML; re-spawn re-applies it (`CurrencyLootComponent` precedent). Omits `[Persistent]`; proven by the Tier-4 template round-trip and the architecture guard.
- **Deferred:** *category-granular effect immunity* (immune to `Curse` only, etc.) — a per-`EffectCategory` mask replaces the single bool at the same gate sites later. Parked in [`../backlog.md`](../backlog.md).

## Deviations / Follow-ups

- **Deviations from the plan:** none. Built as specified across WP-1 (component + dual gate) and WP-2 (authoring: builder, command, template round-trip, editor). The code-mode `architecture-reviewer` returned **approve with nits** — three INV-16 `reference/systems.md` drifts (one pre-existing: an erroneous `PersistentEntity` in `CreateMob`'s component list) fixed in the same PR.
- **Follow-ups unlocked:** Shopping (12c) consumes `Untargetable` + `EffectImmune` to protect safe-area shopkeepers. The component is entity-general — future PvP-safe players, room `safe` flags, and sanctuary effects reuse it.
- **Obligations parked:** when mob aggro/AI lands, aggro selection must skip `Untargetable` mobs (no aggro exists yet — noted for that slice, no code here). Category-granular immunity in [`../backlog.md`](../backlog.md).
