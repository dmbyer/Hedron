using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Stats;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 1 — the track-key vocabulary (D1): construction, fail-fast validation, and the
    /// serialized-key contract that makes the widening backward compatible without a migration.
    /// </summary>
    public sealed class ProgressionTrackTests
    {
        // ── Key round-trip ───────────────────────────────────────────────────────

        [Fact]
        public void Score_track_key_is_the_bare_enum_name()
        {
            Assert.Equal("Body", ProgressionTrack.Of(ScoreId.Body).ToKey());
            Assert.Equal("HpMax", ProgressionTrack.Of(ScoreId.HpMax).ToKey());
        }

        [Fact]
        public void Ability_track_key_carries_the_reserved_prefix()
            => Assert.Equal("ability:kick", ProgressionTrack.Ability("kick").ToKey());

        [Theory]
        [InlineData("Body")]
        [InlineData("HpMax")]
        [InlineData("ability:kick")]
        [InlineData("ability:blood_pact")]
        public void TryParse_round_trips_every_key_shape(string key)
        {
            Assert.True(ProgressionTrack.TryParse(key, out var track));
            Assert.Equal(key, track.ToKey());
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData(null)]
        [InlineData("NotAScore")]
        [InlineData("ability:")]
        [InlineData("ability:a:b")]
        public void TryParse_rejects_an_unknown_key(string? key)
            => Assert.False(ProgressionTrack.TryParse(key, out _));

        [Fact]
        public void The_ability_prefix_cannot_collide_with_a_ScoreId_name()
        {
            // No ScoreId name starts with the reserved prefix, so a score key can never be
            // mistaken for an ability key (or vice versa) — the property that lets pre-slice
            // score-only snapshots load unchanged.
            foreach (var score in Enum.GetValues<ScoreId>())
            {
                Assert.DoesNotContain(':', score.ToString());
                Assert.True(ProgressionTrack.TryParse(score.ToString(), out var parsed));
                Assert.False(parsed.IsAbility);
            }
        }

        // ── Fail-fast validation ─────────────────────────────────────────────────

        [Fact]
        public void Default_track_throws_rather_than_rendering_an_empty_key()
            => Assert.Throws<InvalidOperationException>(() => default(ProgressionTrack).ToKey());

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a:b")]
        public void Ability_rejects_an_invalid_id(string abilityId)
            => Assert.Throws<ArgumentException>(() => ProgressionTrack.Ability(abilityId));

        [Fact]
        public void Ability_rejects_a_null_id()
            => Assert.Throws<ArgumentException>(() => ProgressionTrack.Ability(null!));

        // ── Equality / dictionary behaviour ──────────────────────────────────────

        [Fact]
        public void Tracks_of_the_same_kind_and_value_are_equal_and_hash_alike()
        {
            Assert.Equal(ProgressionTrack.Of(ScoreId.Body), ProgressionTrack.Of(ScoreId.Body));
            Assert.Equal(ProgressionTrack.Ability("kick"), ProgressionTrack.Ability("kick"));
            Assert.NotEqual(ProgressionTrack.Of(ScoreId.Body), ProgressionTrack.Of(ScoreId.Mind));
            Assert.NotEqual(ProgressionTrack.Ability("kick"), ProgressionTrack.Ability("mend"));

            var map = new Dictionary<ProgressionTrack, int>
            {
                [ProgressionTrack.Of(ScoreId.Body)] = 1,
                [ProgressionTrack.Ability("kick")] = 2,
            };
            Assert.Equal(1, map[ProgressionTrack.Of(ScoreId.Body)]);
            Assert.Equal(2, map[ProgressionTrack.Ability("kick")]);
        }

        // ── JSON key serialization (the WriteAsPropertyName/ReadAsPropertyName path) ──

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() },
        };

        [Fact]
        public void Dictionary_keys_serialize_through_the_property_name_overrides()
        {
            var component = new ProgressionComponent
            {
                Xp =
                {
                    [ProgressionTrack.Of(ScoreId.Body)] = 40,
                    [ProgressionTrack.Ability("kick")] = 12,
                },
            };

            var json = JsonSerializer.Serialize(component, SerializerOptions);

            Assert.Contains("\"Body\":40", json);
            Assert.Contains("\"ability:kick\":12", json);
        }

        [Fact]
        public void An_invalid_key_in_a_payload_is_a_JsonException_not_a_silent_default()
        {
            const string payload = "{\"xp\":{\"NotAScore\":5},\"improvements\":{}}";
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<ProgressionComponent>(payload, SerializerOptions));
        }
    }
}
