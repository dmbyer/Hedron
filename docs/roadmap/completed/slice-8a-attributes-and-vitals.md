# Phase 3 slice 8a — Attributes and vitals (completed)

> Implemented on branch `claude/serene-jepsen-3b5701`. Full feature spec: [`../../implementation-plans/attributes.md`](../../implementation-plans/attributes.md).

## Outcome

Every living entity — player or mob — now carries `AttributesComponent` (Level, Strength, Dexterity, Constitution) and `PoolsComponent` (MaxHp, CurrentHp). These are the two components the combat slice requires: without HP, damage has nowhere to land; without base stats, attack and defense calculations have no ground truth. Players use `score` to inspect their own stats. Admins use `setmob level/hp/str/dex/con` to configure mobs and `setplayer level/hp` for direct stat overrides on connected players during testing. Existing characters and mobs gain the components on hydration/spawn with sensible defaults.

## Shipped pieces

| Surface | Location |
|---|---|
| `AttributesComponent` — `Level: int`, `Strength: int`, `Dexterity: int`, `Constitution: int`; `[Persistent]`; cross-cutting | `Core/ECS/Components/AttributesComponent.cs` |
| `PoolsComponent` — `MaxHp: int`, `CurrentHp: int`; `[Persistent]`; cross-cutting | `Core/ECS/Components/PoolsComponent.cs` |
| `PlayerAttributeSetByAdminEvent` — thin past-tense event record | `Core/Modules/Attributes/Events/PlayerAttributeSetByAdminEvent.cs` |
| `IAttributeSystem` / `AttributeSystem` — read getters for combat seam; `SetLevel`, `SetStrength`, `SetDexterity`, `SetConstitution`, `SetMaxHp` (with CurrentHp clamp); all defaults safe on missing components | `Core/Modules/Attributes/Systems/IAttributeSystem.cs`, `AttributeSystem.cs` |
| `ScoreCommand` — player `score`; reads `CharacterComponent` + `AttributesComponent` + `PoolsComponent`; writes `ScoreDisplayMessage`; no events | `Core/Modules/Attributes/Commands/ScoreCommand.cs` |
| `SetPlayerCommand` — admin `setplayer <characterName> level/hp <n>`; resolves by `ISessionManager.GetAll()` + `CharacterComponent.CharacterName`; calls `IAttributeSystem`; saves + publishes `PlayerAttributeSetByAdminEvent` | `Core/Modules/Attributes/Commands/SetPlayerCommand.cs` |
| `AttributesModule` — DI extension registering `IAttributeSystem`, `ScoreCommand`, `SetPlayerCommand` | `Core/Modules/Attributes/AttributesModule.cs` |
| `ScoreDisplayMessage` — record carrying `CharacterName`, `Level`, `CurrentHp`, `MaxHp`, `Strength`, `Dexterity`, `Constitution`; `OutputCategory.Info` | `Core/Output/ScoreDisplayMessage.cs` |
| `TelnetOutputFormatter.FormatScore` — renders character header + stat block | `Core/Output/TelnetOutputFormatter.cs` |
| `AccountSystem.CreateCharacterAsync` extended — attaches `AttributesComponent { Level=1, Str=10, Dex=10, Con=10 }` + `PoolsComponent { MaxHp=100, CurrentHp=100 }` | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `CharacterHydrationHandler` extended — migration guards for missing `AttributesComponent` and `PoolsComponent` on `WorldContentReadyEvent`; no immediate save (matches slice 6/7 pattern) | `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` |
| `MobTemplate` extended — `Level`, `MaxHp`, `Strength`, `Dexterity`, `Constitution` fields; `Apply` attaches both components with defaults when template values are 0 | `Core/Modules/Mobs/Templates/MobTemplate.cs` |
| `IMobBuilderSystem.SetAttribute` / `MobBuilderSystem.SetAttribute` — mutates live entity + template for `level`, `hp`, `str`, `dex`, `con`; enforces CurrentHp clamp on `hp` (INV-8); no events or persistence (INV-5) | `Core/Modules/Mobs/Systems/IMobBuilderSystem.cs`, `MobBuilderSystem.cs` |
| `MobTemplateDeserializer` extended — reads new optional YAML fields | `Core/Modules/Mobs/MobTemplateDeserializer.cs` |
| `MobContentWriter` extended — writes `level`, `maxHp`, `strength`, `dexterity`, `constitution` to YAML DTO | `Core/Modules/Mobs/Systems/MobContentWriter.cs` |
| `SetMobCommand` extended — handles `level`, `hp`, `str`, `dex`, `con` via `IMobBuilderSystem.SetAttribute`; validates positive integer | `Core/Modules/Mobs/Commands/SetMobCommand.cs` |
| `AdminAuditHandler` extended — `IEventHandler<PlayerAttributeSetByAdminEvent>`; subscribed in `Program.cs` | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| `Program.cs` — `AddAttributesModule()` call; `PlayerAttributeSetByAdminEvent` audit bus subscription | `Server/Program.cs` |
| `docs/reference/components.md` — `AttributesComponent` + `PoolsComponent` rows added to cross-cutting table | — |
| `docs/reference/systems.md` — `IAttributeSystem` entry added; `AccountSystem`, `MobBuilderSystem`, `MobContentWriter` entries updated for slice 8a extensions | — |
| `docs/reference/commands.md` — `score`, `setplayer` entries added; `setmob` entry updated with five new sub-properties | — |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation (use-case doc written as part of slice 8 planning). No blocking findings recorded.

**Code-mode gate:** To be run before merge (architecture-reviewer in code mode against the diff).

## Notable design points

- **`AttributesComponent` and `PoolsComponent` are cross-cutting.** Placed under `Core/ECS/Components/` so `Core/Modules/Combat/` can read them without depending on a domain module. Mirrors `EquipmentComponent` (established in slice 7).
- **Direct-set, no formula.** `MaxHp` is set directly by the template or admin command — no derived formula in this slice. The combat slice will introduce a formula once stat relationships are validated through play. Acknowledged debt.
- **`setplayer` is test tooling.** In production, stats would be driven by level-up events. For the current phase (no progression system), `setplayer` provides a manual override path for test scenarios. Protected by `AdminRequirement`.
- **`hp` sets `MaxHp`, clamps `CurrentHp`.** When an admin sets `hp <n>` on a mob, `MaxHp` is updated and `CurrentHp` is clamped to `min(CurrentHp, n)`. Does not heal the mob to full — healing belongs to a future slice.
- **Migration guard does not save.** `CharacterHydrationHandler` attaches missing components without calling `SaveEntityAsync`. Matches the established pattern from `InventoryComponent` (slice 6) and `EquipmentComponent` (slice 7).
- **Stat naming supersedes `components-planned.md`.** The planned catalog listed `Might`, `Finesse`, `Will`; this slice uses `Strength`, `Dexterity`, `Constitution` — conventional MUD stat names that align with the combat model. The deviation is intentional.
- **Default values.** Level 1, Str/Dex/Con 10, MaxHp 100, CurrentHp 100. Placeholders for the combat slice to calibrate against.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 9 — Combat.** `AttributesComponent` and `PoolsComponent` are now the stable ground truth for attack/defense calculations and HP tracking. The combat slice can read them via `IAttributeSystem` getters without a direct component dependency.
- **Death and respawn (slice 10).** `PoolsComponent.CurrentHp` is the HP value death detection will check.
- **Level-up progression.** Once a progression system exists, `IAttributeSystem.SetLevel` and the stat setters are the write seam it will call — admin commands use the same setters, so the API is validated.
