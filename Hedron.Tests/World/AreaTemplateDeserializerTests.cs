using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.World.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="AreaTemplateDeserializer"/> aspect-affinity parsing.
    ///
    /// Validates that YAML aspect affinities round-trip through deserialisation and Apply.
    /// </summary>
    public sealed class AreaTemplateDeserializerTests
    {
        private static AreaTemplateDeserializer BuildDeserializer()
            => new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance);

        private static string BuildAreaYaml(string? aspectAffinitiesBlock = null)
        {
            var affinities = aspectAffinitiesBlock is null
                ? string.Empty
                : $"\naspectAffinities:\n{aspectAffinitiesBlock}";
            return $@"id: test.area
name: Test Area
description: A test area.{affinities}
";
        }

        // ── Parse valid aspect affinities ────────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_ParsesAspectAffinities()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 60\n  Lightning: 40");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            Assert.NotNull(template.AspectAffinities);
            Assert.Equal(2, template.AspectAffinities!.Count);
            Assert.Equal(60, template.AspectAffinities[AspectId.Fire]);
            Assert.Equal(40, template.AspectAffinities[AspectId.Lightning]);
        }

        [Fact]
        public void AreaTemplateDeserializer_Apply_AttachesAspectAffinitiesComponent()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 60\n  Lightning: 40");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            Assert.True(ecs.TryGet<AspectAffinitiesComponent>(entity.Id, out var affinities));
            Assert.Equal(2, affinities.AffinityWeights.Count);
            Assert.Equal(60, affinities.AffinityWeights[AspectId.Fire]);
            Assert.Equal(40, affinities.AffinityWeights[AspectId.Lightning]);
        }

        // ── Absent aspect affinities block ───────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_AbsentAspectAffinities_TemplatePropertyIsNull()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml();  // no aspectAffinities block

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            Assert.Null(template.AspectAffinities);
        }

        [Fact]
        public void AreaTemplateDeserializer_AbsentAspectAffinities_NoComponent()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml();  // no aspectAffinities block

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            Assert.False(ecs.HasComponent<AspectAffinitiesComponent>(entity.Id));
        }

        // ── Unknown aspect key is skipped ────────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_UnknownAspectKey_Skipped()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Bogus: 100");

            // Must not throw; the unknown key is skipped.
            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            // AspectAffinities should be null or empty — "Bogus" is not a valid AspectId.
            var affinityCount = template.AspectAffinities?.Count ?? 0;
            Assert.Equal(0, affinityCount);
        }

        [Fact]
        public void AreaTemplateDeserializer_MixedKnownAndUnknownKeys_OnlyKnownParsed()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 100\n  Bogus: 50");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            // "Bogus" skipped; "Fire" should be present.
            Assert.NotNull(template.AspectAffinities);
            Assert.Single(template.AspectAffinities);
            Assert.Equal(100, template.AspectAffinities![AspectId.Fire]);
        }
    }
}
