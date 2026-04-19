# Group Combat Initiation

**Status:** planned
**Actors:** Player A, Player B, Mob
**Module:** `Core/Modules/Combat/` + `Core/Modules/Group/`

## Description

Two or more players share a `GroupComponent`. When one group member initiates combat, the entire group enters combat with the mob, and loot/XP distribution follows group rules.

## Preconditions

- All group members share a `GroupComponent.GroupId`
- Group members are in the same room as the targeted mob
- Initiator is in Active state (not already in combat)
- Target is attackable (`MobDataComponent`, not friendly)

## Postconditions

- All eligible group members' states flip to Combat
- Combat state includes all participants and the mob
- The mob AI picks a target (usually the initiator; influenced by threat table)
- Loot distribution and XP splits follow `GroupSystem` rules

## Main flow

1. Player B issues `kill <mob>` → `CombatHandler`
2. `CombatSystem.InitiateCombat(attacker, target)` opens a combat session
3. `GroupSystem.GetMembersInSameRoom(attacker)` returns eligible members
4. `CombatSystem.JoinCombat(member, session)` for each eligible member
5. `CombatHandler` publishes `CombatStartedEvent` with all participants
6. `AIHandler` updates the mob's threat table from session participants
7. `NotificationHandler` messages everyone involved

## Events fired

- `CombatStartedEvent` — all participants attached
- `ThreatUpdatedEvent` _(planned)_ — when AI updates threat

## Systems / handlers

- `CombatSystem`, `GroupSystem`, `AISystem` (via `AIHandler`)
- `CombatHandler` — orchestrator
- `NotificationHandler`

## Design notes

- **Group membership is a component, not a handler concern.** `GroupSystem` answers "who is in the group and where are they?"
- Loot distribution for group kills is resolved by `LootSystem.DistributeForGroup` — see [mob-death-and-loot.md](mob-death-and-loot.md).
- **Join-on-aggro** (mob picks up a non-initiator member) is covered by `CombatSystem.JoinCombat` reacting to `DamageEvent`, not by duplicating group logic in every handler.

## Related

- [combat-pulse-processing.md](combat-pulse-processing.md)
- [mob-death-and-loot.md](mob-death-and-loot.md)
