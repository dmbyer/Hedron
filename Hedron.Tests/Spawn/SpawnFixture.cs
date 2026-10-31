using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Shared harness for the <see cref="SpawnSystem"/> suites: a real <see cref="EntityService"/>
    /// and <see cref="TemplateRegistry"/>, a <see cref="FakeClock"/>, and helpers for rooms carrying
    /// a <see cref="SpawnConfigComponent"/>.
    /// </summary>
    internal sealed class SpawnFixture
    {
        /// <summary>Applies no components — <see cref="TemplateRegistry.Spawn"/> attaches the <see cref="BlueprintComponent"/>.</summary>
        private sealed class MinimalTemplate : IEntityTemplate
        {
            public MinimalTemplate(string blueprintId) => BlueprintId = blueprintId;
            public string BlueprintId { get; }
            public void Apply(Entity entity, EntityService entityService) { }
        }

        public EntityService Ecs { get; } = new();
        public TemplateRegistry Registry { get; }
        public FakeClock Clock { get; } = new(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        public SpawnSystem System { get; }

        public SpawnFixture()
        {
            Registry = new TemplateRegistry(Ecs);
            System = new SpawnSystem(Ecs, Registry, Clock, NullLogger<SpawnSystem>.Instance);
        }

        public void Template(params string[] blueprintIds)
        {
            foreach (var id in blueprintIds)
                Registry.Register(id, new MinimalTemplate(id));
        }

        /// <summary>A live room (RoomComponent + BlueprintComponent) with the given rules.</summary>
        public uint Room(string roomBlueprintId, params SpawnRule[] rules)
        {
            var roomId = Ecs.CreateEntity().Id;
            Ecs.AddComponent(roomId, new RoomComponent { Name = roomBlueprintId });
            Ecs.AddComponent(roomId, new BlueprintComponent { BlueprintId = roomBlueprintId });
            if (rules.Length > 0)
            {
                var config = new SpawnConfigComponent();
                config.Rules.AddRange(rules);
                Ecs.AddComponent(roomId, config);
            }
            return roomId;
        }

        /// <summary>Replaces the room's live rules (the builder's job in production).</summary>
        public void SetRules(uint roomId, params SpawnRule[] rules)
        {
            if (!Ecs.TryGet<SpawnConfigComponent>(roomId, out var config))
            {
                config = new SpawnConfigComponent();
                Ecs.AddComponent(roomId, config);
            }
            config.Rules.Clear();
            config.Rules.AddRange(rules);
        }

        /// <summary>Live entities of <paramref name="blueprintId"/> located in <paramref name="roomId"/>.</summary>
        public List<uint> InRoom(uint roomId, string blueprintId) =>
            Ecs.GetAllComponents<BlueprintComponent>()
                .Where(e => string.Equals(e.Component.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
                .Where(e => Ecs.TryGet<LocationComponent>(e.EntityId, out var loc) && loc.RoomEntityId == roomId)
                .Select(e => e.EntityId)
                .ToList();

        public int CountOf(string blueprintId) =>
            Ecs.GetAllComponents<BlueprintComponent>().Count(e => e.Component.BlueprintId == blueprintId);

        public void Advance(int seconds) => Clock.Advance(TimeSpan.FromSeconds(seconds));
    }
}
