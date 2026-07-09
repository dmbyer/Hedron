using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs.Commands;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Modules.Mobs
{
    /// <summary>
    /// Tier 2 — <c>tier</c>/<c>band</c> property-branch tests for <see cref="SetMobCommand"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md (WP-B, Tier 2) — the
    /// <c>setmob tier</c>/<c>setmob band</c> dual-write assertions and out-of-range rejection.
    /// </summary>
    public sealed class SetMobCommandBandTests
    {
        private sealed class NoOpContentWriter : IMobContentWriter
        {
            public int WriteCount;
            public Task WriteAsync(MobTemplate template, CancellationToken ct = default)
            {
                WriteCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public TemplateRegistry Registry { get; }
            public MobBuilderSystem Builder { get; }
            public NoOpContentWriter Writer { get; }
            public RecordingEventBus Bus { get; }
            public SetMobCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Registry = new TemplateRegistry(Ecs);
                Builder = new MobBuilderSystem(Ecs, Registry, NullLogger<MobBuilderSystem>.Instance);
                Writer = new NoOpContentWriter();
                Bus = new RecordingEventBus(dispatch: false);
                Command = new SetMobCommand(Builder, Writer, Ecs, Registry, Bus);
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
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "2"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(2, mob.Tier);

            world.Registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(2, mobTemplate.Tier);

            Assert.Single(world.Bus.Published.OfType<MobPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Tier_out_of_range_above_max_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "7"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(0, mob.Tier);
            Assert.Equal(0, world.Writer.WriteCount);
            Assert.Empty(world.Bus.Published.OfType<MobPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Tier_negative_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "tier", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(0, mob.Tier);
            Assert.Equal(0, world.Writer.WriteCount);
        }

        [Fact]
        public async Task Band_dual_writes_live_component_and_template()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "2"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(2, mob.Band);

            world.Registry.TryGet(result.BlueprintId, out var template);
            var mobTemplate = Assert.IsType<MobTemplate>(template);
            Assert.Equal(2, mobTemplate.Band);

            Assert.Single(world.Bus.Published.OfType<MobPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Band_out_of_range_above_max_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "4"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(0, mob.Band);
            Assert.Equal(0, world.Writer.WriteCount);
            Assert.Empty(world.Bus.Published.OfType<MobPropertySetByAdminEvent>());
        }

        [Fact]
        public async Task Band_negative_is_rejected()
        {
            var world = new TestWorld();
            var roomId = MakeRoom(world.Ecs);
            var result = world.Builder.CreateMob("Grunt", roomId);

            var output = new RecordingOutput();
            var ctx = MakeContext(MakeArgs(result.BlueprintId, "band", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            var mob = world.Ecs.Get<MobDataComponent>(result.MobEntityId);
            Assert.Equal(0, mob.Band);
            Assert.Equal(0, world.Writer.WriteCount);
        }
    }
}
