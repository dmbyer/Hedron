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
    /// Tier 2 — handler/orchestration tests for the <c>value</c> property case on
    /// <see cref="SetitemCommand"/> (item-value WP2).
    ///
    /// Coverage contract (docs/implementation-plans/item-value.md, WP2 exit criterion):
    ///   - <c>setitem &lt;bp&gt; value 250</c> (valid) mutates entity + template and invokes
    ///     the content writer + publishes <see cref="ItemPropertySetByAdminEvent"/>.
    ///   - <c>setitem &lt;bp&gt; value -1</c> (negative) produces an error echo with NO mutation
    ///     and NO content write.
    ///   - <c>setitem &lt;bp&gt; value abc</c> (non-integer) produces an error echo with NO
    ///     mutation and NO content write.
    ///
    /// No mocking framework — all collaborators are real or hand-rolled stubs.
    /// </summary>
    public sealed class SetitemValueCommandTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Records every <see cref="WriteAsync"/> call so tests can assert
        /// "writer was called" vs. "writer was NOT called".
        /// </summary>
        private sealed class RecordingContentWriter : IItemContentWriter
        {
            public List<ItemTemplate> Written { get; } = new();

            public Task WriteAsync(ItemTemplate template, CancellationToken ct = default)
            {
                Written.Add(template);
                return Task.CompletedTask;
            }
        }

        // ── World ─────────────────────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public TemplateRegistry Registry { get; }
            public ItemBuilderSystem Builder { get; }
            public RecordingContentWriter ContentWriter { get; }
            public RecordingEventBus Bus { get; }
            public SetitemCommand Command { get; }

            public TestWorld()
            {
                Ecs = new EntityService();
                Registry = new TemplateRegistry(Ecs);
                Builder = new ItemBuilderSystem(Ecs, Registry, NullLogger<ItemBuilderSystem>.Instance);
                ContentWriter = new RecordingContentWriter();
                Bus = new RecordingEventBus(dispatch: false);
                Command = new SetitemCommand(Builder, ContentWriter, Ecs, Registry, Bus);
            }

            /// <summary>
            /// Creates a room, spawns an item blueprint via <see cref="ItemBuilderSystem.CreateItem"/>,
            /// and returns the blueprint id + live entity id.
            /// </summary>
            public (string blueprintId, uint itemEntityId) CreateItem(string name = "Test Item")
            {
                var room = Ecs.CreateEntity();
                Ecs.AddComponent(room.Id, new BlueprintComponent { BlueprintId = $"room.test.{Guid.NewGuid():N}" });
                var result = Builder.CreateItem(name, room.Id);
                return (result.BlueprintId, result.ItemEntityId);
            }
        }

        // ── ParsedArguments factory (internal ctor via reflection) ─────────────────

        private static ParsedArguments MakeArgs(string blueprintId, string property, string? value = null)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;

            var dict = new Dictionary<string, object?>
            {
                ["blueprintId"] = blueprintId,
                ["property"] = property,
            };
            if (value is not null)
                dict["value"] = value;

            return (ParsedArguments)ctor.Invoke(new object[] { dict });
        }

        private static CommandContext MakeContext(
            uint invokerEntityId,
            ParsedArguments args,
            RecordingOutput output)
        {
            var session = new StubSession(invokerEntityId);
            return new CommandContext(
                session,
                invokerEntityId,
                args,
                output.WriterFor(invokerEntityId),
                Services: null!);
        }

        // ── Valid value: mutates entity + template + write + event ────────────────

        [Fact]
        public async Task ExecuteAsync_valid_value_sets_ItemDataComponent_Value_on_live_entity()
        {
            var world = new TestWorld();
            var (blueprintId, itemEntityId) = world.CreateItem("Gold Ring");
            const uint adminId = 42u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "250"), output);

            await world.Command.ExecuteAsync(ctx);

            var comp = world.Ecs.Get<ItemDataComponent>(itemEntityId);
            Assert.Equal(250L, comp.Value);
        }

        [Fact]
        public async Task ExecuteAsync_valid_value_sets_template_Value_in_registry()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Silver Amulet");
            const uint adminId = 42u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "500"), output);

            await world.Command.ExecuteAsync(ctx);

            world.Registry.TryGet(blueprintId, out var template);
            var itemTemplate = Assert.IsType<ItemTemplate>(template);
            Assert.Equal(500L, itemTemplate.Value);
        }

        [Fact]
        public async Task ExecuteAsync_valid_value_invokes_content_writer()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Bronze Shield");
            const uint adminId = 42u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "250"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Single(world.ContentWriter.Written);
        }

        [Fact]
        public async Task ExecuteAsync_valid_value_publishes_ItemPropertySetByAdminEvent()
        {
            var world = new TestWorld();
            var (blueprintId, itemEntityId) = world.CreateItem("Diamond Gem");
            const uint adminId = 42u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "250"), output);

            await world.Command.ExecuteAsync(ctx);

            var events = world.Bus.Published.OfType<ItemPropertySetByAdminEvent>().ToList();
            Assert.Single(events);
            Assert.Equal(adminId, events[0].AdminEntityId);
            Assert.Equal(itemEntityId, events[0].ItemEntityId);
            Assert.Equal("value", events[0].PropertyName);
            Assert.Equal("250", events[0].NewValue);
        }

        [Fact]
        public async Task ExecuteAsync_zero_value_is_valid_and_mutates()
        {
            // Value == 0 is the "valueless/non-saleable" sentinel; it is still a valid authored value.
            var world = new TestWorld();
            var (blueprintId, itemEntityId) = world.CreateItem("Pebble");
            world.Builder.SetItemValue(itemEntityId, 999L); // pre-set non-zero

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "0"), output);

            await world.Command.ExecuteAsync(ctx);

            var comp = world.Ecs.Get<ItemDataComponent>(itemEntityId);
            Assert.Equal(0L, comp.Value);
            Assert.Single(world.ContentWriter.Written);
        }

        // ── Invalid: negative value ───────────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_negative_value_writes_error_echo()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Sword");
            const uint adminId = 1u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            // At least one PlainMessage with Error severity must have been sent to the invoker.
            var errorMessages = output.All
                .Where(r => r.RecipientEntityId == adminId &&
                            r.Message is PlainMessage pm &&
                            ((PlainMessage)r.Message).Severity == OutputSeverity.Error)
                .ToList();
            Assert.NotEmpty(errorMessages);
        }

        [Fact]
        public async Task ExecuteAsync_negative_value_does_not_mutate_entity()
        {
            var world = new TestWorld();
            var (blueprintId, itemEntityId) = world.CreateItem("Sword");
            world.Builder.SetItemValue(itemEntityId, 100L); // pre-set to confirm no overwrite

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            var comp = world.Ecs.Get<ItemDataComponent>(itemEntityId);
            Assert.Equal(100L, comp.Value); // unchanged
        }

        [Fact]
        public async Task ExecuteAsync_negative_value_does_not_invoke_content_writer()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Sword");

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.ContentWriter.Written);
        }

        [Fact]
        public async Task ExecuteAsync_negative_value_does_not_publish_event()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Sword");

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "-1"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }

        // ── Invalid: non-integer value ────────────────────────────────────────────

        [Fact]
        public async Task ExecuteAsync_non_integer_value_writes_error_echo()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Dagger");
            const uint adminId = 1u;

            var output = new RecordingOutput();
            var ctx = MakeContext(adminId, MakeArgs(blueprintId, "value", "abc"), output);

            await world.Command.ExecuteAsync(ctx);

            var errorMessages = output.All
                .Where(r => r.RecipientEntityId == adminId &&
                            r.Message is PlainMessage pm &&
                            ((PlainMessage)r.Message).Severity == OutputSeverity.Error)
                .ToList();
            Assert.NotEmpty(errorMessages);
        }

        [Fact]
        public async Task ExecuteAsync_non_integer_value_does_not_mutate_entity()
        {
            var world = new TestWorld();
            var (blueprintId, itemEntityId) = world.CreateItem("Dagger");
            world.Builder.SetItemValue(itemEntityId, 200L); // pre-set

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "abc"), output);

            await world.Command.ExecuteAsync(ctx);

            var comp = world.Ecs.Get<ItemDataComponent>(itemEntityId);
            Assert.Equal(200L, comp.Value); // unchanged
        }

        [Fact]
        public async Task ExecuteAsync_non_integer_value_does_not_invoke_content_writer()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Dagger");

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "abc"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.ContentWriter.Written);
        }

        [Fact]
        public async Task ExecuteAsync_non_integer_value_does_not_publish_event()
        {
            var world = new TestWorld();
            var (blueprintId, _) = world.CreateItem("Dagger");

            var output = new RecordingOutput();
            var ctx = MakeContext(1u, MakeArgs(blueprintId, "value", "abc"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(world.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
        }
    }
}
