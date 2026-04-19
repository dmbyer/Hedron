# Skill-Based Crafting

**Status:** planned
**Actors:** Player
**Module:** `Core/Modules/Crafting/`

## Description

A player uses a crafting skill (armorsmithing, weaponsmithing, alchemy, etc.) to create an item from materials in inventory. Success, quality, and skill improvement depend on the player's skill level and the recipe's difficulty.

## Preconditions

- Player has a learned crafting `SkillComponent` entry at a sufficient level
- Player has the required material items in `InventoryComponent`
- Player state is Active (not in combat, not stunned)
- A `RecipeComponent` matching the requested item exists

## Postconditions

- Materials are removed from `InventoryComponent` and destroyed
- A new item entity is built bespoke by `ItemGeneratorSystem` (recipe + skill + material quality determine shape)
- Item is added to player inventory
- Player's `SkillComponent` may increase (difficulty-gated via `SkillSystem`)
- Result is persisted automatically because the item carries `[Persistent]` components

## Main flow

1. `craft <item>` command → `CraftingHandler`
2. `CraftingSystem.ValidateRecipe(player, recipeId)` checks skill + materials
3. `InventorySystem.RemoveItems(materials)` consumes inputs
4. `ItemGeneratorSystem.Create(recipe, skillLevel, materialQuality)` builds the item
5. `InventorySystem.AddItem(player, item)` places it
6. `SkillSystem.TryImprove(player, skillId, difficulty)` rolls for improvement
7. `CraftingHandler` publishes `ItemCraftedEvent`
8. `PersistenceHandler` flushes if needed; `NotificationHandler` reports

## Events fired

- `ItemCraftedEvent` _(planned)_
- `SkillImprovedEvent` — if the skill increased
- `ItemDestroyedEvent` — for each consumed material

## Systems / handlers

- `CraftingSystem`, `ItemGeneratorSystem`, `InventorySystem`, `SkillSystem`
- `CraftingHandler` — orchestrator
- `NotificationHandler`, `PersistenceHandler`

## Design notes

- Recipes live as templates in `TemplateRegistry` (a `recipes` catalog), not hard-coded; the in-world recipe entity carries a `RecipeComponent`.
- Crafted items are built bespoke by `ItemGeneratorSystem` via `EntityService.CreateEntity()` + `AddComponent` — they are not spawned from a pre-authored item template, because their stats vary per craft.
- The rule "who can craft what" lives in `CraftingSystem`, not in the handler.

## Related

- [../architecture/02-ecs.md](../architecture/02-ecs.md) — Recipe and crafting data
- [../reference/systems.md](../reference/systems.md) — `SkillSystem`, `ItemGeneratorSystem`
