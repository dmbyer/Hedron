using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Xunit;

namespace Hedron.Tests.Harness
{
    /// <summary>
    /// Fluent builder that creates test entities directly in a given <see cref="EntityService"/>.
    /// </summary>
    public sealed class EntityBuilder
    {
        private readonly EntityService _ecs;
        private readonly List<(System.Type Type, IComponent Component)> _components = new();

        public EntityBuilder(EntityService ecs)
        {
            _ecs = ecs;
        }

        /// <summary>Marks the entity as a player character by adding a <see cref="CharacterComponent"/>.</summary>
        public EntityBuilder AsPlayer()
        {
            _components.Add((typeof(CharacterComponent), new CharacterComponent()));
            return this;
        }

        /// <summary>Marks the entity as a mob by adding a <see cref="MobDataComponent"/>.</summary>
        public EntityBuilder AsMob(string name, IEnumerable<string>? keywords = null)
        {
            var mob = new MobDataComponent { Name = name };
            if (keywords != null) mob.Keywords.AddRange(keywords);
            _components.Add((typeof(MobDataComponent), mob));
            return this;
        }

        /// <summary>Adds a <see cref="PoolsComponent"/> with the given values.</summary>
        public EntityBuilder WithPools(
            int hp = 100, int mana = 50, int stamina = 50, int astra = 10)
        {
            _components.Add((typeof(PoolsComponent), new PoolsComponent
            {
                MaxHp = hp, CurrentHp = hp,
                MaxMana = mana, CurrentMana = mana,
                MaxStamina = stamina, CurrentStamina = stamina,
                MaxAstra = astra, CurrentAstra = astra,
            }));
            return this;
        }

        /// <summary>Adds an <see cref="AttributesComponent"/> with the given values.</summary>
        public EntityBuilder WithAttributes(
            int mind = 10, int body = 10, int spirit = 10, int attunement = 10, int level = 1)
        {
            _components.Add((typeof(AttributesComponent), new AttributesComponent
            {
                Mind = mind, Body = body, Spirit = spirit,
                Attunement = attunement, Level = level,
            }));
            return this;
        }

        /// <summary>
        /// Adds a <see cref="LocationComponent"/> placing the entity in the given room entity.
        /// Sets both <c>RoomEntityId</c> and <c>RoomBlueprintId</c> (as a string of the uint).
        /// </summary>
        public EntityBuilder InRoom(uint roomEntityId)
        {
            _components.Add((typeof(LocationComponent), new LocationComponent
            {
                RoomEntityId = roomEntityId,
                RoomBlueprintId = roomEntityId.ToString(),
            }));
            return this;
        }

        /// <summary>Adds an <see cref="EquipmentComponent"/> with an item in the MainHand slot.</summary>
        public EntityBuilder Wielding(uint itemEntityId)
        {
            _components.Add((typeof(EquipmentComponent), new EquipmentComponent
            {
                Slots = { [WornSlot.MainHand] = itemEntityId },
            }));
            return this;
        }

        /// <summary>Adds an arbitrary component.</summary>
        public EntityBuilder With<T>(T component) where T : IComponent
        {
            _components.Add((typeof(T), component));
            return this;
        }

        /// <summary>Creates the entity in the <see cref="EntityService"/> and returns its id.</summary>
        public uint Build()
        {
            var entity = _ecs.CreateEntity();
            foreach (var (type, component) in _components)
                _ecs.AddComponent(entity.Id, type, component);
            return entity.Id;
        }
    }

    // ── Self-test ────────────────────────────────────────────────────────────────

    public sealed class EntityBuilderTests
    {
        [Fact]
        public void AsPlayer_WithPools_produces_entity_with_correct_hp_and_CharacterComponent()
        {
            var ecs = new EntityService();
            var id = new EntityBuilder(ecs).AsPlayer().WithPools(hp: 50).Build();

            Assert.True(ecs.HasComponent<CharacterComponent>(id));
            var pools = ecs.Get<PoolsComponent>(id);
            Assert.Equal(50, pools.CurrentHp);
            Assert.Equal(50, pools.MaxHp);
        }

        [Fact]
        public void AsMob_sets_name_and_keywords()
        {
            var ecs = new EntityService();
            var id = new EntityBuilder(ecs)
                .AsMob("goblin", new[] { "goblin", "creature" })
                .Build();

            Assert.True(ecs.HasComponent<MobDataComponent>(id));
            var mob = ecs.Get<MobDataComponent>(id);
            Assert.Equal("goblin", mob.Name);
            Assert.Contains("goblin", mob.Keywords);
        }

        [Fact]
        public void InRoom_sets_both_entity_and_blueprint_ids()
        {
            var ecs = new EntityService();
            var id = new EntityBuilder(ecs).AsPlayer().InRoom(42u).Build();

            var loc = ecs.Get<LocationComponent>(id);
            Assert.Equal(42u, loc.RoomEntityId);
            Assert.Equal("42", loc.RoomBlueprintId);
        }

        [Fact]
        public void Wielding_places_item_in_MainHand_slot()
        {
            var ecs = new EntityService();
            var id = new EntityBuilder(ecs).AsPlayer().Wielding(99u).Build();

            var equip = ecs.Get<EquipmentComponent>(id);
            Assert.True(equip.Slots.ContainsKey(WornSlot.MainHand));
            Assert.Equal(99u, equip.Slots[WornSlot.MainHand]);
        }

        [Fact]
        public void WithAttributes_sets_all_fields()
        {
            var ecs = new EntityService();
            var id = new EntityBuilder(ecs)
                .WithAttributes(mind: 12, body: 14, spirit: 8, attunement: 11, level: 5)
                .Build();

            var attr = ecs.Get<AttributesComponent>(id);
            Assert.Equal(12, attr.Mind);
            Assert.Equal(14, attr.Body);
            Assert.Equal(8, attr.Spirit);
            Assert.Equal(11, attr.Attunement);
            Assert.Equal(5, attr.Level);
        }
    }
}
