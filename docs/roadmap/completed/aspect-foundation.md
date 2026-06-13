# Phase 3 Slice 11-d — Aspect & Registry Foundation

**PR:** (this branch) · **Spec:** [`../../implementation-plans/aspect-foundation.md`](../../features/aspects/aspects.md)

## Outcome

Landed two gameplay-model spines in one slice. **Spine F (Registry layer):** a generic `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` base extracted from three divergent hand-rolled registries (the rule-of-three threshold for INV-19); `AbilityRegistry`, `EffectRegistry`, and `StatRegistry` now all subclass it with no per-registry lookup plumbing. **Spine A (Aspect):** a fixed elemental vocabulary (`AspectId` enum + `AspectDefinition` + `AspectRegistry`) and a core `IAspectSystem` that makes combat damage aspect-typed — attacker affinity boost and defender independent per-aspect resistance applied on every melee round and ability strike. `AspectAffinitiesComponent` is `[Persistent]` and attaches empty to every new character. A startup `RegistryValidationBootstrap` enforces referential integrity (dangling ability→effect/aspect refs + malformed `AspectComposition`s abort boot with a full report), and a generic `defs` admin inspector covers every registry.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `IRegistry<TKey, TDef>` + `DefinitionRegistry<TKey, TDef>` | `Core/Systems/DefinitionRegistry.cs` | Generic base; instance-held rows (reload-shaped); `TryGet`/`Get`/`AllIds`/`All` |
| `AbilityRegistry` retrofit | `Core/Modules/Abilities/AbilityRegistry.cs` | Extends `DefinitionRegistry<string, AbilityDefinition>`; `IAbilityRegistry : IRegistry<string, AbilityDefinition>` |
| `EffectRegistry` retrofit | `Core/Modules/Effects/EffectRegistry.cs` | Extends `DefinitionRegistry<string, EffectDefinition>`; `IEffectRegistry : IRegistry<string, EffectDefinition>` |
| `StatRegistry` retrofit | `Core/Modules/Stats/StatRegistry.cs`, `IStatRegistry.cs` | Extends `DefinitionRegistry<ScoreId, ScoreRegistration>`; `IStatRegistry : IRegistry<ScoreId, ScoreRegistration>` |
| `AspectId` + `AspectCategory` enums | `Core/Modules/Aspects/AspectId.cs` | `Fire`, `Ice`, `Lightning`, `Void`, `Nature`, `Light`; categories Elemental/Primal/Arcane |
| `AspectDefinition` | `Core/Modules/Aspects/AspectDefinition.cs` | `record(AspectId, Name, Description, AspectCategory)` |
| `AspectComposition` | `Core/Modules/Aspects/AspectComposition.cs` | Normalized `AspectId → weight`; empty, single (100), or blend summing to 100; `IsValid` + `ToString` |
| `IAspectRegistry` / `AspectRegistry` | `Core/Modules/Aspects/AspectRegistry.cs` | 6 starter rows; extends `DefinitionRegistry<AspectId, AspectDefinition>` |
| `IAspectSystem` / `AspectSystem` | `Core/Modules/Aspects/Systems/` | Core system: `Resolve`/`Affinity`/`Resist`; pure math, no events (INV-2, INV-5) |
| `AspectAffinitiesComponent` | `Core/ECS/Components/AspectAffinitiesComponent.cs` | `[Persistent]`; `AffinityWeights` + `BaseResistances` (`Dictionary<AspectId, int>`); serialized by name (INV-23) |
| `AspectsModule` | `Core/Modules/Aspects/AspectsModule.cs` | `AddAspectsModule(IServiceCollection)` DI entry point |
| `AbilityDefinition.Aspect` migration | `Core/Modules/Abilities/AbilityDefinition.cs` | `string?` → `AspectComposition?`; all ability rows updated |
| `CombatSystem` aspect threading | `Core/Modules/Combat/Systems/CombatSystem.cs` | `ExecuteRound` + `ResolveAbilityStrike` call `IAspectSystem.Resolve`; `CombatRoundResult.AspectComposition` field |
| `CombatRoundEvent` enriched | `Core/Modules/Combat/Events/CombatRoundEvent.cs` | `AspectComposition?` (point-in-time capture, INV-6) |
| `AbilityStrikeResolvedEvent` enriched | `Core/Modules/Combat/Events/AbilityStrikeResolvedEvent.cs` | `AspectComposition?` (point-in-time capture, INV-6) |
| `CombatTickHandler` updated | `Core/Modules/Combat/Handlers/CombatTickHandler.cs` | Propagates `AspectComposition` to both event publishes |
| `AbilityInvocationPipeline` updated | `Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs` | Reads `def.Aspect`, passes to `ResolveAbilityStrike`, propagates to `AbilityStrikeResolvedEvent` |
| `AccountSystem.CreateCharacterAsync` updated | `Core/Modules/Account/Systems/AccountSystem.cs` | Attaches `AspectAffinitiesComponent` (empty) to new characters |
| `RegistryValidationBootstrap` | `Server/RegistryValidationBootstrap.cs` | Hosted service; fail-fast sweep (cross-refs + composition normalization); after `WorldContentBootstrap` |
| `DefsCommand` | `Core/Modules/Admin/Commands/DefsCommand.cs` | Admin `defs <family> [id]`; maps family name → registry; `Full` matching, `AdminRequirement` |
| `AdminModule` + `Program.cs` updated | `Core/Modules/Admin/AdminModule.cs`, `Server/Program.cs` | `DefsCommand` registered; `AddAspectsModule` + `RegistryValidationBootstrap` wired |
| Flow 01 updated | `docs/architecture/flows/flow-01-server-startup.md` | `RegistryValidationBootstrap` added to mermaid, step 2 list, new step 8; old steps 8–11 renumbered 9–12 |
| Flow 18 updated | `docs/architecture/flows/flow-18-combat-round-pulse.md` | `IAspectSystem` participant + `Affinity`/`Resolve` calls in mermaid; steps 4–5 updated |
| Flow 24 updated | `docs/architecture/flows/flow-24-ability-activation.md` | `resolveOffensiveExternally` section updated with `def.Aspect` composition threading |
| Flow 26 updated | `docs/architecture/flows/flow-26-offensive-ability-opens-combat.md` | `ResolveAbilityStrike` call updated with `def.Aspect`; steps 6–7 updated |
| `add-core-system/SKILL.md` updated | `.claude/skills/add-core-system/SKILL.md` | "Definition registries" section: enum-vs-string key-type rule, selector pattern, precedents, validation companion (INV-20) |
| Reference catalogs | `docs/reference/systems.md`, `components.md`, `commands.md` | `DefinitionRegistry`/`IRegistry`, `AspectRegistry`, `IAspectSystem`, `RegistryValidationBootstrap`, `defs` added; existing entries updated |

## Spec-review provenance

**Spec gate (spec-mode):** Ran before implementation. Flagged INV-20: the definition registry pattern must be documented in `add-core-system/SKILL.md` in the same slice. Addressed in the implementation PR.

**Code gate (code-mode):** Ran after all four work packages landed.

Blocking findings resolved:
- **INV-17 (flow drift — four flows):** `flow-01-server-startup.md` updated with `RegistryValidationBootstrap`; `flow-18-combat-round-pulse.md` updated with `IAspectSystem.Resolve` path; `flow-24-ability-activation.md` and `flow-26-offensive-ability-opens-combat.md` updated with `def.Aspect` composition threading through `ResolveAbilityStrike`.
- **INV-28 (use-case trim):** `aspect-foundation.md` trimmed to its durable behavior spec; in-flight sections removed.

Non-blocking findings addressed:
- **INV-19 acknowledged debt:** `// TODO migrate` comment added to `EffectParams.Aspect`; backlog entry filed.
- **Doc inaccuracy:** `IStatRegistry.cs` comment corrected (the narrowing from `IReadOnlyList` to `IReadOnlyCollection` loses index access, not gains; the consumer-safe claim is about `foreach` not about superset membership).
- **INV-20:** `add-core-system/SKILL.md` updated with the "Definition registries" sub-section.

## Notable design points

- **Enum key for fixed vocabularies, string key for open/persisted families.** The two-type-parameter generic lets each registry pick the key type that matches its family's nature — compile-time safety for developer-controlled vocabularies (`AspectId`, `ScoreId`), data-author-friendly strings for persisted-by-reference families (`AbilityId`, `EffectId`). The rule is documented in the use-case Design notes and the `add-core-system/SKILL.md` skill.
- **`Func<TDef, TKey>` key selector on the base constructor** avoids requiring a shared `IHasId<TKey>` interface across the three retrofit families, whose key property names differ (`Id`, `EffectId`, `ScoreId`).
- **Per-aspect resistance is owned by `IAspectSystem.Resist`, not by `ScoreId`.** Resistance is parameterized by `AspectId × value`, a dimension the flat `ScoreId` enum cannot carry. Adding `FireResist`/`VoidResist`/… rows to `ScoreId` would couple the closed score enum to the open-ended aspect set and violate the spine's "new aspect = one registry row, not code" invariant. `IAspectSystem` is the aggregator for the aspect dimension, folding base + contributors on read without caching (INV-24).
- **`AspectComposition` as point-in-time capture (INV-6).** Rather than a new event, `CombatRoundEvent` and `AbilityStrikeResolvedEvent` carry `AspectComposition?` (null when empty), matching the `CombatEndedEvent.DefenderName` precedent. A separate event would force all witnesses to correlate two events for one strike.
- **`RegistryValidationBootstrap` in `Server/`, not `Core/`.** `IHostedService` requires `Microsoft.Extensions.Hosting` (not just Abstractions), which `Core.csproj` does not reference. Follows the `PersistenceBootstrap` and `WorldContentBootstrap` precedent.
- **Deferred: `EffectParams.Aspect` migration.** `string? Aspect` remains on `EffectParams` with a `// TODO migrate` comment; the field is unused for damage typing. Tracked in backlog.

## Deviations from the use-case doc

None — shipped per spec. The postconditions note that `EffectParams.Aspect` migration "may be deferred" — it was deferred, as documented.

## Follow-ups unlocked

- **Aspect-typed ability/effect riders:** `AspectDefinition` is shaped for aspect-unique riders (e.g. Fire abilities deal burning DoT). A future slice adds rider fields and wires them to `EffectSystem`.
- **Per-entity affinity/resistance authoring:** `AspectAffinitiesComponent` attaches empty to all characters. An admin `setaffinity`/`setresistance` command lands when per-entity tuning is needed.
- **YAML-authored definition pipeline:** `DefinitionRegistry` holds instance rows (reload-shaped). A YAML authoring path for the string-keyed families (Ability, Effect) is tracked in backlog.
- **Shopping (slice 12):** No Aspect dependency; `IRegistry<TKey,TDef>` is available for any registries that slice introduces.
