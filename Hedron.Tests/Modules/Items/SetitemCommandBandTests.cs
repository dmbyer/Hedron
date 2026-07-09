using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Commands;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Modules.Items
{
    /// <summary>
    /// Tier 2 — <c>tier</c>/<c>band</c> property-branch tests for <see cref="SetitemCommand"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md (WP-B, Tier 2) — the
    /// <c>setitem tier</c>/<c>setitem band</c> dual-write assertions and out-of-range/negative
    /// rejection. Mirrors <c>SetMobCommandBandTests</c>.
    /// </summary>
    public sealed class SetitemCommandBandTests
    {
        private sealed class NoOpContentWriter : IItemContentWriter
        {
            public int WriteCount;
            public Task WriteAsync(ItemTemplate template, CancellationToken ct = default)
            {
                WriteCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public TemplateRegistry Registry { get; }
            public ItemBuilderSystem Builder { get; }
            public NoOpContentWriter Writer { get; }
            public RecordingEventBus Bus { get; }
            public SetitemCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Registry = new TemplateRegistry(Ecs);
                Builder = new ItemBuilderSystem(Ecs, Registry, NullLogger<ItemBuilderSystem>.Instance);
                Writer = new NoOpContentWriter();
                Bus = new RecordingEventBus(dispatch: false);
                Command = new SetitemCommand(Builder, Writer, Ecs, Registry, Bus);
            }
        }

        private static ParsedArguments MakeArgs(string blueprintId, string property, string value)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;

            var values = new Dictionary<string, object?>
            {
                ["blueprintId"] = blueprintId,
                ["property"] = property,
                ["value"] = value,
            };
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(1u);
            return new CommandContext(session, 1u, args, output.WriterFor(1u), Services: null!);
        }

        private static uint MakeRoom(EntityService ecs)
        {
            var room = ecs.CreateEntity();
            ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = "room.test" });
            return room.Id;
        }

        [Fact]
        public async Task Tier_dual_writes_live_component_and_template()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Tiered Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "2"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(2, item.Tier);

            world.Registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(2, itemTemplate.Tier);

            Assert.Single(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Tier_out_of_range_above_max_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Tiered Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "7"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(0, item.Tier);
            Assert.Equal(0, world.Writer.WriteCount);
            Assert.Empty(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Tier_negative_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Tiered Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(0, item.Tier);
            Assert.Equal(0, world.Writer.WriteCount);
        }

        [Fact]
        public async Task Band_dual_writes_live_component_and_template()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Banded Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "2"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(2, item.Band);

            world.Registry.TryGet(result.BlueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(2, itemTemplate.Band);

            Assert.Single(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Band_out_of_range_above_max_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Banded Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "4"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(0, item.Band);
            Assert.Equal(0, world.Writer.WriteCount);
            Assert.Empty(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Band_negative_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateItem("Banded Blade", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            var item = world.Ecs.Get<ItemDataComponent>(result.ItemEntityId);
            Assert.Equal(0, item.Band);
            Assert.Equal(0, world.Writer.WriteCount);
        }
    }
}
