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
    }
}
