using System;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Persistence
{
    /// <summary>
    /// Tier 4 — persistence round-trip tests.
    /// Each test uses a fresh <see cref="PersistenceTestHarness"/> (isolated in-memory SQLite db)
    /// to verify that save→load behavior matches the two-level opt-in model:
    ///   Level 1: <c>PersistentEntity</c> opts the entity in.
    ///   Level 2: <c>[Persistent]</c> on a component type opts its data into the snapshot.
    /// Coverage contract: postconditions of <c>docs/use-cases/persistence-reform.md</c>.
    /// </summary>
    public sealed class RoundTripTests
    {
        // ── Test 1: Player entity round-trip ─────────────────────────────────────

        /// <summary>
        /// A player entity with <c>[Persistent]</c> components survives save→load with
        /// all component field values equal to what was saved.
        /// Covers: CharacterComponent, PoolsComponent, AttributesComponent, LocationComponent.
        /// </summary>
        [Fact]
        public async Task Player_entity_Persistent_components_survive_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var characterName = "Aldric";
            var accountId = 42u;
            var created = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var lastLogin = new DateTime(2026, 3, 10, 8, 30, 0, DateTimeKind.Utc);

            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .WithPools(hp: 120, mana: 60, stamina: 45, astra: 8)
                .WithAttributes(mind: 14, body: 16, spirit: 12, attunement: 11, level: 3)
                .InRoom(99u)
                .Build();

            // Patch CharacterComponent fields that AsPlayer() left at defaults.
            var character = ecs.Get<CharacterComponent>(id);
            character.CharacterName = characterName;
            character.AccountEntityId = accountId;
            character.CreatedAtUtc = created;
            character.LastLoginUtc = lastLogin;

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            // CharacterComponent
            Assert.True(fresh.HasComponent<CharacterComponent>(id),
                "CharacterComponent must survive round-trip (INV-14).");
            var reloadedCharacter = fresh.Get<CharacterComponent>(id);
            Assert.Equal(characterName, reloadedCharacter.CharacterName);
            Assert.Equal(accountId, reloadedCharacter.AccountEntityId);
            Assert.Equal(created, reloadedCharacter.CreatedAtUtc);
            Assert.Equal(lastLogin, reloadedCharacter.LastLoginUtc);

            // PoolsComponent — HP and Mana
            Assert.True(fresh.HasComponent<PoolsComponent>(id),
                "PoolsComponent must survive round-trip (INV-14).");
            var pools = fresh.Get<PoolsComponent>(id);
            Assert.Equal(120, pools.MaxHp);
            Assert.Equal(120, pools.CurrentHp);
            Assert.Equal(60, pools.MaxMana);
            Assert.Equal(60, pools.CurrentMana);
            Assert.Equal(45, pools.MaxStamina);
            Assert.Equal(8, pools.MaxAstra);

            // AttributesComponent — Body and Mind
            Assert.True(fresh.HasComponent<AttributesComponent>(id),
                "AttributesComponent must survive round-trip (INV-14).");
            var attrs = fresh.Get<AttributesComponent>(id);
            Assert.Equal(14, attrs.Mind);
            Assert.Equal(16, attrs.Body);
            Assert.Equal(12, attrs.Spirit);
            Assert.Equal(11, attrs.Attunement);
            Assert.Equal(3, attrs.Level);

            // LocationComponent — RoomBlueprintId (RoomEntityId is [JsonIgnore]; not stored)
            Assert.True(fresh.HasComponent<LocationComponent>(id),
                "LocationComponent must survive round-trip (INV-14).");
            var loc = fresh.Get<LocationComponent>(id);
            Assert.Equal("99", loc.RoomBlueprintId);
        }

        // ── Test 2: Transient components absent after reload ──────────────────────

        /// <summary>
        /// <c>CombatStateComponent</c> is NOT tagged <c>[Persistent]</c>.
        /// Even when present at save time, it must be absent after reload.
        /// </summary>
        [Fact]
        public async Task CombatStateComponent_is_absent_after_reload()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs)
                .AsPlayer()
                .WithPools(hp: 80)
                .Build();

            // Add the transient combat component before saving.
            ecs.AddComponent(id, new CombatStateComponent { OpponentEntityId = 999u });

            Assert.True(ecs.HasComponent<CombatStateComponent>(id),
                "Precondition: CombatStateComponent must be present before save.");

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.False(fresh.HasComponent<CombatStateComponent>(id),
                "CombatStateComponent must be absent after reload — it is not [Persistent] (INV-14).");
        }

        // ── Test 3: World-content entity (no PersistentEntity) not persisted ─────

        /// <summary>
        /// An entity without <c>PersistentEntity</c> writes no row.
        /// After reload the entity id is absent from the fresh <see cref="EntityService"/>.
        /// </summary>
        [Fact]
        public async Task World_content_entity_without_PersistentEntity_writes_no_row()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // World-content entity: has [Persistent]-tagged components but NO PersistentEntity marker.
            var id = new EntityBuilder(ecs)
                .WithPools(hp: 50)
                .WithAttributes(body: 8)
                .Build();

            Assert.False(ecs.HasComponent<PersistentEntity>(id),
                "Precondition: no PersistentEntity opt-in.");

            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            // The entity should not exist in the reloaded world at all.
            Assert.False(fresh.HasComponent<PoolsComponent>(id),
                "PoolsComponent must not be present — entity was not opted into persistence (INV-14).");
            Assert.False(fresh.HasComponent<AttributesComponent>(id),
                "AttributesComponent must not be present — entity was not opted into persistence (INV-14).");
        }

        // ── Test 4: Multiple entities round-trip ─────────────────────────────────

        /// <summary>
        /// Two player entities saved together; both reload with their own distinct component values.
        /// </summary>
        [Fact]
        public async Task Multiple_player_entities_round_trip_with_distinct_values()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var idA = new EntityBuilder(ecs)
                .AsPlayer()
                .WithPools(hp: 100, mana: 40)
                .WithAttributes(mind: 15, body: 10)
                .InRoom(1u)
                .Build();
            var charA = ecs.Get<CharacterComponent>(idA);
            charA.CharacterName = "Aldric";
            ecs.AddComponent(idA, new PersistentEntity());

            var idB = new EntityBuilder(ecs)
                .AsPlayer()
                .WithPools(hp: 200, mana: 80)
                .WithAttributes(mind: 8, body: 20)
                .InRoom(2u)
                .Build();
            var charB = ecs.Get<CharacterComponent>(idB);
            charB.CharacterName = "Brynn";
            ecs.AddComponent(idB, new PersistentEntity());

            await harness.SaveAsync(idA);
            await harness.SaveAsync(idB);

            var fresh = await harness.ReloadIntoFreshWorld();

            // Both entities are present.
            Assert.True(fresh.HasComponent<PoolsComponent>(idA), "Entity A must reload.");
            Assert.True(fresh.HasComponent<PoolsComponent>(idB), "Entity B must reload.");

            // Values are distinct — no cross-contamination.
            var poolsA = fresh.Get<PoolsComponent>(idA);
            var poolsB = fresh.Get<PoolsComponent>(idB);
            Assert.Equal(100, poolsA.CurrentHp);
            Assert.Equal(200, poolsB.CurrentHp);

            var attrsA = fresh.Get<AttributesComponent>(idA);
            var attrsB = fresh.Get<AttributesComponent>(idB);
            Assert.Equal(15, attrsA.Mind);
            Assert.Equal(8, attrsB.Mind);

            var locA = fresh.Get<LocationComponent>(idA);
            var locB = fresh.Get<LocationComponent>(idB);
            Assert.Equal("1", locA.RoomBlueprintId);
            Assert.Equal("2", locB.RoomBlueprintId);

            var nameA = fresh.Get<CharacterComponent>(idA).CharacterName;
            var nameB = fresh.Get<CharacterComponent>(idB).CharacterName;
            Assert.Equal("Aldric", nameA);
            Assert.Equal("Brynn", nameB);
        }

        // ── Test 5: Component equality after round-trip ──────────────────────────

        /// <summary>
        /// After save→reload, a <c>[Persistent]</c> component's field values equal what was saved —
        /// not merely "the component type exists".
        /// Uses distinct non-default values to guard against false positives from default initialization.
        /// </summary>
        [Fact]
        public async Task Component_field_values_are_equal_after_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // Use unusual non-default values to detect serialization bugs.
            var id = new EntityBuilder(ecs)
                .WithPools(hp: 77, mana: 33, stamina: 22, astra: 7)
                .WithAttributes(mind: 17, body: 13, spirit: 9, attunement: 6, level: 7)
                .InRoom(555u)
                .Build();

            // Modify current values after construction (MaxHp = CurrentHp from builder, but let's
            // mutate CurrentHp to a distinct value to verify current-vs-max both round-trip).
            var pools = ecs.Get<PoolsComponent>(id);
            pools.CurrentHp = 45;   // deliberately differs from MaxHp (77)
            pools.CurrentMana = 10; // deliberately differs from MaxMana (33)

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            // Pools: both max and current values survived.
            var reloadedPools = fresh.Get<PoolsComponent>(id);
            Assert.Equal(77, reloadedPools.MaxHp);
            Assert.Equal(45, reloadedPools.CurrentHp);
            Assert.Equal(33, reloadedPools.MaxMana);
            Assert.Equal(10, reloadedPools.CurrentMana);
            Assert.Equal(22, reloadedPools.MaxStamina);
            Assert.Equal(22, reloadedPools.CurrentStamina);
            Assert.Equal(7, reloadedPools.MaxAstra);
            Assert.Equal(7, reloadedPools.CurrentAstra);

            // Attributes: all five fields survived.
            var reloadedAttrs = fresh.Get<AttributesComponent>(id);
            Assert.Equal(17, reloadedAttrs.Mind);
            Assert.Equal(13, reloadedAttrs.Body);
            Assert.Equal(9, reloadedAttrs.Spirit);
            Assert.Equal(6, reloadedAttrs.Attunement);
            Assert.Equal(7, reloadedAttrs.Level);

            // Location: blueprint id survived.
            var reloadedLoc = fresh.Get<LocationComponent>(id);
            Assert.Equal("555", reloadedLoc.RoomBlueprintId);
        }

        // ── Test 6: PersistentEntity marker itself round-trips ───────────────────

        /// <summary>
        /// <c>PersistentEntity</c> is itself tagged <c>[Persistent]</c>, so a reloaded entity
        /// automatically re-acquires its opt-in marker without any special handling.
        /// </summary>
        [Fact]
        public async Task PersistentEntity_marker_survives_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs)
                .WithPools(hp: 60)
                .Build();

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<PersistentEntity>(id),
                "PersistentEntity marker must round-trip so the reloaded entity is itself persistent (INV-14).");
        }

        // ── Test 7: DestroyEntity removes persisted rows ─────────────────────────

        /// <summary>
        /// When a persistent entity is destroyed via <c>DestroyEntity</c>, its rows are deleted
        /// from SQLite. A subsequent reload finds no trace of the entity.
        /// </summary>
        [Fact]
        public async Task DestroyEntity_removes_persisted_rows()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs)
                .WithPools(hp: 90)
                .Build();

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            // Destroy the entity — the PersistenceSystem registered OnPersistentEntityDestroying,
            // so this triggers the SQLite DELETE synchronously.
            ecs.DestroyEntity(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.False(fresh.HasComponent<PoolsComponent>(id),
                "Destroyed entity must not appear after reload — auto-delete must have fired (INV-14).");
            Assert.False(fresh.HasComponent<PersistentEntity>(id),
                "Destroyed entity's PersistentEntity marker must also be absent after reload.");
        }

        // ── Test 8: LocationComponent.RoomEntityId is NOT persisted ──────────────

        /// <summary>
        /// <c>LocationComponent.RoomEntityId</c> is <c>[JsonIgnore]</c> — runtime-only.
        /// After reload, <c>RoomEntityId</c> must be 0 (default) even if set at save time,
        /// while <c>RoomBlueprintId</c> is preserved.
        /// </summary>
        [Fact]
        public async Task LocationComponent_RoomEntityId_is_not_persisted()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs)
                .InRoom(77u) // sets both RoomEntityId=77 and RoomBlueprintId="77"
                .Build();

            ecs.AddComponent(id, new PersistentEntity());
            await harness.SaveAsync(id);

            var fresh = await harness.ReloadIntoFreshWorld();

            var loc = fresh.Get<LocationComponent>(id);
            Assert.Equal("77", loc.RoomBlueprintId);
            // RoomEntityId is [JsonIgnore] — runtime-only, must be 0 (default) after reload.
            Assert.Equal(0u, loc.RoomEntityId);
        }

        // ── Test 9 (T-P1): equipped player + gear bonuses round-trip; nothing WhileEquipped is stored ──

        /// <summary>
        /// A persistent player wearing a weapon and armor round-trips its <c>EquipmentComponent.Slots</c>
        /// and each player-owned item's <c>ItemDataComponent.StatBonuses</c>. The synthetic
        /// <c>WhileEquipped</c> effects the contributor derives are NEVER stored — no
        /// <c>EffectsComponent</c> is written for them (derived-on-read). After reload the gear bonuses
        /// re-derive from the persisted sources, so the contributor reproduces the same modifiers.
        /// </summary>
        [Fact]
        public async Task Equipped_player_gear_bonuses_round_trip_and_are_not_stored_as_effects()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            // Player-owned weapon and armor are persistent entities carrying their authored bonuses.
            var weapon = ecs.CreateEntity();
            ecs.AddComponent(weapon.Id, new ItemDataComponent
            {
                Name = "broadsword",
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 6) },
            });
            ecs.AddComponent(weapon.Id, new PersistentEntity());

            var armor = ecs.CreateEntity();
            ecs.AddComponent(armor.Id, new ItemDataComponent
            {
                Name = "breastplate",
                StatBonuses = { new EquipmentStatBonus(ScoreId.Defense, 4) },
            });
            ecs.AddComponent(armor.Id, new PersistentEntity());

            var player = new EntityBuilder(ecs)
                .AsPlayer().WithAttributes(body: 10)
                .With(new EquipmentComponent
                {
                    Slots = { [WornSlot.MainHand] = weapon.Id, [WornSlot.Chest] = armor.Id },
                })
                .Build();
            ecs.AddComponent(player, new PersistentEntity());

            await harness.SaveAsync(weapon.Id);
            await harness.SaveAsync(armor.Id);
            await harness.SaveAsync(player);

            var fresh = await harness.ReloadIntoFreshWorld();

            // Equipment slot mapping survived on the player.
            Assert.True(fresh.HasComponent<EquipmentComponent>(player));
            var slots = fresh.Get<EquipmentComponent>(player).Slots;
            Assert.Equal(weapon.Id, slots[WornSlot.MainHand]);
            Assert.Equal(armor.Id, slots[WornSlot.Chest]);

            // Each item's authored bonuses survived.
            Assert.Equal(
                new EquipmentStatBonus(ScoreId.AttackPower, 6),
                Assert.Single(fresh.Get<ItemDataComponent>(weapon.Id).StatBonuses));
            Assert.Equal(
                new EquipmentStatBonus(ScoreId.Defense, 4),
                Assert.Single(fresh.Get<ItemDataComponent>(armor.Id).StatBonuses));

            // Nothing WhileEquipped was materialized: the player has no stored EffectsComponent.
            Assert.False(fresh.HasComponent<EffectsComponent>(player),
                "Worn-gear modifiers are derived-on-read — none may be written to EffectsComponent.");

            // The bonuses re-derive from the persisted sources after restart.
            var contributor = new EquipmentEffectContributor(fresh);
            Assert.Equal(6, contributor.GetModifiers(player, ScoreId.AttackPower));
            Assert.Equal(4, contributor.GetModifiers(player, ScoreId.Defense));
        }
    }
}
