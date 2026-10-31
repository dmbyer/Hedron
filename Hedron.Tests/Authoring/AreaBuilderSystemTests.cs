using Hedron.Core.Modules.World.Components;
using Hedron.Core.Modules.World;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Admin.Systems;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="AreaBuilderSystem"/>.
    ///
    /// Coverage contract: Postconditions of docs/use-cases/admin-area-authoring.md
    /// and the builder interface <see cref="IAreaBuilderSystem"/>.
    ///
    /// All tests use the real <see cref="EntityService"/> and <see cref="TemplateRegistry"/>
    /// (no mocking framework).
    /// </summary>
    public sealed class AreaBuilderSystemTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        private static (AreaBuilderSystem system, EntityService ecs, TemplateRegistry registry) Build()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var system = new AreaBuilderSystem(ecs, registry, NullLogger<AreaBuilderSystem>.Instance);
            return (system, ecs, registry);
        }

        // ── CreateArea ───────────────────────────────────────────────────────────

        [Fact]
        public void CreateArea_ReturnsEntityWithComponents()
        {
            var (sys, ecs, registry) = Build();
            var result = sys.CreateArea("Test Area");

            // Non-zero entity id
            Assert.NotEqual(0u, result.AreaEntityId);

            // Entity has AreaComponent with correct Name
            var area = ecs.Get<AreaComponent>(result.AreaEntityId);
            Assert.Equal("Test Area", area.Name);

            // Entity has BlueprintComponent with id starting with "area.adhoc."
            var bp = ecs.Get<BlueprintComponent>(result.AreaEntityId);
            Assert.StartsWith("area.adhoc.", bp.BlueprintId);

            // Blueprint is in registry
            Assert.True(registry.TryGet(result.BlueprintId, out _));
        }

        [Fact]
        public void CreateArea_BlueprintIdIsUnique()
        {
            var (sys, _, _) = Build();
            var r1 = sys.CreateArea("Area Alpha");
            var r2 = sys.CreateArea("Area Beta");

            Assert.NotEqual(r1.BlueprintId, r2.BlueprintId);
        }

        // ── INV-5: AreaBuilderSystem does not hold IEventBus ─────────────────────

        [Fact]
        public void AreaBuilderSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(AreaBuilderSystem).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(Hedron.Core.Events.IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: AreaBuilderSystem field '{field.Name}' is IEventBus — " +
                    "domain systems must never hold or publish to the event bus.");
            }
        }

        // ── Property setters (area-designation, P8) ──────────────────────────────

        private static (AreaBuilderSystem sys, EntityService ecs, AreaTemplate template, uint liveId) Spawned()
        {
            var (sys, ecs, registry) = Build();
            var template = new AreaTemplate("area.wilds") { Name = "Wilds" };
            registry.Register(template.BlueprintId, template);
            var live = registry.Spawn(template.BlueprintId);
            return (sys, ecs, template, live.Id);
        }

        [Fact]
        public void SetAreaDesignation_updates_template_and_live_component_and_null_removes_both()
        {
            var (sys, ecs, template, liveId) = Spawned();

            var set = sys.SetAreaDesignation("area.wilds", AreaDesignation.Of(0, 2, 2));
            Assert.True(set.Success);
            Assert.Equal(liveId, set.AreaEntityId);
            Assert.Equal(AreaDesignation.Of(0, 2, 2), template.Designation);
            Assert.Equal(AreaDesignation.Of(0, 2, 2), ecs.Get<AreaDesignationComponent>(liveId).Designation);

            sys.SetAreaDesignation("area.wilds", AreaDesignation.Of(0, 1, 3));
            Assert.Equal(AreaDesignation.Of(0, 1, 3), ecs.Get<AreaDesignationComponent>(liveId).Designation);

            Assert.True(sys.SetAreaDesignation("area.wilds", null).Success);
            Assert.Null(template.Designation);
            Assert.False(ecs.HasComponent<AreaDesignationComponent>(liveId));
        }

        [Fact]
        public void SetAreaDesignation_refuses_malformed_or_unknown_and_changes_nothing()
        {
            var (sys, ecs, template, liveId) = Spawned();

            var malformed = sys.SetAreaDesignation("area.wilds", AreaDesignation.Of(0, 3, 1));
            Assert.False(malformed.Success);
            Assert.Null(template.Designation);
            Assert.False(ecs.HasComponent<AreaDesignationComponent>(liveId));

            Assert.False(sys.SetAreaDesignation("area.missing", AreaDesignation.Of(0, 1, 1)).Success);
        }

        [Fact]
        public void SetAreaName_and_description_update_template_and_AreaComponent()
        {
            var (sys, ecs, template, liveId) = Spawned();

            sys.SetAreaName("area.wilds", "The Wilds");
            sys.SetAreaDescription("area.wilds", "Tall grass.");

            Assert.Equal(("The Wilds", "Tall grass."), (template.Name, template.Description));
            var live = ecs.Get<AreaComponent>(liveId);
            Assert.Equal(("The Wilds", "Tall grass."), (live.Name, live.Description));
        }

        [Fact]
        public void Setters_on_a_template_with_no_live_entity_update_only_the_template()
        {
            var (sys, _, registry) = Build();
            var template = new AreaTemplate("area.unspawned");
            registry.Register(template.BlueprintId, template);

            var result = sys.SetAreaDesignation("area.unspawned", AreaDesignation.Of(0, 1, 1));

            Assert.True(result.Success);
            Assert.Equal(0u, result.AreaEntityId);
            Assert.NotNull(template.Designation);
        }
    }
}
