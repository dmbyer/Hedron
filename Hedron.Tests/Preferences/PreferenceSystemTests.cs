using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Components;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Preferences
{
    /// <summary>Tier 1 — system unit tests for <see cref="PreferenceSystem"/> and the registry.</summary>
    public sealed class PreferenceSystemTests
    {
        private static (PreferenceSystem System, EntityService Ecs, uint Entity) Create()
        {
            var ecs = new EntityService();
            var entity = new EntityBuilder(ecs).AsPlayer().Build();
            return (new PreferenceSystem(ecs), ecs, entity);
        }

        [Fact]
        public void Reading_an_unset_preference_does_not_attach_the_component()
        {
            var (system, ecs, entity) = Create();

            system.IsEnabled(entity, PreferenceId.ProgressionXpMessages);

            Assert.False(ecs.HasComponent<PlayerConfigurationComponent>(entity),
                "A read must not create PlayerConfigurationComponent — only Set does.");
        }

        [Fact]
        public void Set_attaches_the_component_and_the_value_reads_back()
        {
            var (system, ecs, entity) = Create();

            system.Set(entity, PreferenceId.ProgressionXpMessages, enabled: false);

            Assert.True(ecs.HasComponent<PlayerConfigurationComponent>(entity));
            Assert.False(system.IsEnabled(entity, PreferenceId.ProgressionXpMessages));

            system.Set(entity, PreferenceId.ProgressionXpMessages, enabled: true);
            Assert.True(system.IsEnabled(entity, PreferenceId.ProgressionXpMessages));
        }

        [Fact]
        public void GetAll_returns_every_registered_preference_in_display_order()
        {
            var (system, _, entity) = Create();

            var all = system.GetAll(entity);

            Assert.Equal(PreferenceRegistry.All.Count, all.Count);
            Assert.Equal(
                PreferenceRegistry.All.Select(d => d.Id).ToList(),
                all.Select(s => s.Definition.Id).ToList());
            Assert.All(all, state => Assert.Equal(PreferenceRegistry.DefaultFor(state.Definition.Id), state.Enabled));
        }

        [Fact]
        public void Preferences_are_per_entity()
        {
            var (system, ecs, first) = Create();
            var second = new EntityBuilder(ecs).AsPlayer().Build();

            system.Set(first, PreferenceId.ProgressionXpMessages, enabled: false);

            Assert.False(system.IsEnabled(first, PreferenceId.ProgressionXpMessages));
            Assert.True(system.IsEnabled(second, PreferenceId.ProgressionXpMessages));
        }

        // ── Name resolution ──────────────────────────────────────────────────────

        [Fact]
        public void TryResolve_matches_the_full_name_case_insensitively()
        {
            Assert.True(PreferenceRegistry.TryResolve("ProgressionXp", out var id));
            Assert.Equal(PreferenceId.ProgressionXpMessages, id);
        }

        [Fact]
        public void TryResolve_matches_an_unambiguous_prefix()
        {
            Assert.True(PreferenceRegistry.TryResolve("progressionxp", out var id));
            Assert.Equal(PreferenceId.ProgressionXpMessages, id);

            Assert.True(PreferenceRegistry.TryResolve("progressionimp", out var improve));
            Assert.Equal(PreferenceId.ProgressionImprovementMessages, improve);
        }

        [Fact]
        public void TryResolve_rejects_an_ambiguous_prefix_and_an_unknown_name()
        {
            // "progression" prefixes both shipped names.
            Assert.False(PreferenceRegistry.TryResolve("progression", out _));
            Assert.False(PreferenceRegistry.TryResolve("nosuchsetting", out _));
            Assert.False(PreferenceRegistry.TryResolve("", out _));
        }

        [Fact]
        public void Every_enum_member_has_a_registry_row()
        {
            foreach (var id in System.Enum.GetValues<PreferenceId>())
                Assert.Contains(PreferenceRegistry.All, d => d.Id == id);
        }
    }
}
