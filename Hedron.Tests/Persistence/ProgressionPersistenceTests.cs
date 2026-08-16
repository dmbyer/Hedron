using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Components;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Persistence
{
    /// <summary>
    /// Persistence tier — the shapes this slice changed: <see cref="ProgressionComponent"/>'s
    /// widened dictionary key and the new <see cref="PlayerConfigurationComponent"/>.
    ///
    /// The load-bearing test is <see cref="A_pre_slice_score_only_payload_re_serializes_byte_identically"/>:
    /// it is the reason this widening needs no migration.
    /// </summary>
    public sealed class ProgressionPersistenceTests
    {
        private static IComponentSerializer CreateSerializer()
            => new ComponentSerializer(new ComponentTypeRegistry());

        // ── Back-compat: the no-migration proof ──────────────────────────────────

        [Fact]
        public void A_pre_slice_score_only_payload_re_serializes_byte_identically()
        {
            // Exactly what ComponentSerializer emitted for Dictionary<ScoreId,int> before the
            // widening: bare enum names as keys (PropertyNamingPolicy is camelCase but
            // DictionaryKeyPolicy is not set, so keys are untouched).
            const string preSlicePayload =
                "{\"xp\":{\"Body\":130,\"HpMax\":40},\"improvements\":{\"Body\":1,\"HpMax\":0}}";

            var serializer = CreateSerializer();

            var component = serializer.Deserialize(typeof(ProgressionComponent).FullName!, preSlicePayload);
            var progression = Assert.IsType<ProgressionComponent>(component);

            Assert.Equal(130, progression.Xp[ProgressionTrack.Of(ScoreId.Body)]);
            Assert.Equal(40, progression.Xp[ProgressionTrack.Of(ScoreId.HpMax)]);
            Assert.Equal(1, progression.Improvements[ProgressionTrack.Of(ScoreId.Body)]);
            Assert.Equal(0, progression.Improvements[ProgressionTrack.Of(ScoreId.HpMax)]);

            Assert.Equal(preSlicePayload, serializer.Serialize(progression));
        }

        // ── Mixed score + ability keys ───────────────────────────────────────────

        [Fact]
        public async Task Mixed_score_and_ability_tracks_survive_a_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs).AsPlayer().Build();
            ecs.AddComponent(id, new ProgressionComponent
            {
                Xp =
                {
                    [ProgressionTrack.Of(ScoreId.Body)] = 130,
                    [ProgressionTrack.Ability("kick")] = 55,
                    [ProgressionTrack.Ability("blood_pact")] = 7,
                },
                Improvements =
                {
                    [ProgressionTrack.Of(ScoreId.Body)] = 1,
                    [ProgressionTrack.Ability("kick")] = 0,
                    [ProgressionTrack.Ability("blood_pact")] = 0,
                },
            });
            ecs.AddComponent(id, new PersistentEntity());

            await harness.SaveAsync(id);
            var fresh = await harness.ReloadIntoFreshWorld();

            var progression = fresh.Get<ProgressionComponent>(id);
            Assert.Equal(130, progression.Xp[ProgressionTrack.Of(ScoreId.Body)]);
            Assert.Equal(55, progression.Xp[ProgressionTrack.Ability("kick")]);
            Assert.Equal(7, progression.Xp[ProgressionTrack.Ability("blood_pact")]);
            Assert.Equal(1, progression.Improvements[ProgressionTrack.Of(ScoreId.Body)]);
        }

        [Fact]
        public void An_ability_track_key_serializes_with_the_reserved_prefix()
        {
            var serializer = CreateSerializer();
            var component = new ProgressionComponent
            {
                Xp = { [ProgressionTrack.Ability("kick")] = 55 },
            };

            Assert.Contains("\"ability:kick\":55", serializer.Serialize(component));
        }

        // ── PlayerConfigurationComponent ─────────────────────────────────────────

        [Fact]
        public async Task PlayerConfigurationComponent_survives_a_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var id = new EntityBuilder(ecs).AsPlayer().Build();
            new PreferenceSystem(ecs).Set(id, PreferenceId.ProgressionXpMessages, enabled: false);
            ecs.AddComponent(id, new PersistentEntity());

            await harness.SaveAsync(id);
            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<PlayerConfigurationComponent>(id),
                "PlayerConfigurationComponent must survive round-trip (INV-14).");
            Assert.False(new PreferenceSystem(fresh).IsEnabled(id, PreferenceId.ProgressionXpMessages));
        }

        [Fact]
        public void An_absent_preference_key_falls_back_to_the_registry_default()
        {
            var ecs = new EntityService();
            var system = new PreferenceSystem(ecs);
            var id = new EntityBuilder(ecs).AsPlayer().Build();

            // No component at all.
            Assert.Equal(
                PreferenceRegistry.DefaultFor(PreferenceId.ProgressionXpMessages),
                system.IsEnabled(id, PreferenceId.ProgressionXpMessages));

            // Component present, but this key never set.
            system.Set(id, PreferenceId.ProgressionImprovementMessages, enabled: false);
            Assert.Equal(
                PreferenceRegistry.DefaultFor(PreferenceId.ProgressionXpMessages),
                system.IsEnabled(id, PreferenceId.ProgressionXpMessages));
        }
    }
}
