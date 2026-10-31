using System;
using System.Linq;
using Hedron.Core.ECS.Components;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>Tier 1 — <see cref="SpawnRuleIds"/> (spawn-authoring T3).</summary>
    public sealed class SpawnRuleIdsTests
    {
        private static readonly string Blueprint40 = "mob." + new string('a', 36);
        private static readonly string Blueprint70 = "mob." + new string('b', 66);

        [Fact]
        public void DefaultFor_returns_a_short_blueprint_unchanged()
        {
            Assert.Equal(40, Blueprint40.Length);
            Assert.Equal(Blueprint40, SpawnRuleIds.DefaultFor(Blueprint40));
        }

        [Fact]
        public void DefaultFor_truncates_a_long_blueprint_to_its_first_60_characters_deterministically()
        {
            var id = SpawnRuleIds.DefaultFor(Blueprint70);

            Assert.Equal(Blueprint70[..60], id);
            Assert.Equal(id, SpawnRuleIds.DefaultFor(Blueprint70));
            Assert.True(SpawnRuleIds.IsWellFormed(id));
        }

        [Fact]
        public void Mint_returns_the_default_then_numbered_suffixes_case_insensitively()
        {
            Assert.Equal("mob.rat", SpawnRuleIds.Mint(Array.Empty<string>(), "mob.rat"));
            Assert.Equal("mob.rat-2", SpawnRuleIds.Mint(new[] { "MOB.RAT" }, "mob.rat"));
            Assert.Equal("mob.rat-3", SpawnRuleIds.Mint(new[] { "mob.rat", "Mob.Rat-2" }, "mob.rat"));
        }

        [Fact]
        public void Mint_suffix_on_a_70_character_blueprint_stays_well_formed()
        {
            var id = SpawnRuleIds.Mint(new[] { SpawnRuleIds.DefaultFor(Blueprint70) }, Blueprint70);

            Assert.Equal(62, id.Length); // 60-character root + "-2"
            Assert.EndsWith("-2", id);
            Assert.True(SpawnRuleIds.IsWellFormed(id));
        }

        [Fact]
        public void Mint_throws_when_every_candidate_is_taken()
        {
            var taken = new[] { "mob.rat" }.Concat(Enumerable.Range(2, 998).Select(n => $"mob.rat-{n}"));

            Assert.Throws<InvalidOperationException>(() => SpawnRuleIds.Mint(taken, "mob.rat"));
        }

        [Theory]
        [InlineData("mob.rat", true)]
        [InlineData("Rats_north-2", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("has space", false)]
        [InlineData("slash/id", false)]
        public void IsWellFormed_checks_charset(string? id, bool expected) =>
            Assert.Equal(expected, SpawnRuleIds.IsWellFormed(id));

        [Fact]
        public void IsWellFormed_bounds_length_at_64()
        {
            Assert.True(SpawnRuleIds.IsWellFormed(new string('a', 64)));
            Assert.False(SpawnRuleIds.IsWellFormed(new string('a', 65)));
        }
    }

    /// <summary>Tier 1 — <see cref="SpawnRuleValidation"/> (spawn-authoring T3).</summary>
    public sealed class SpawnRuleValidationTests
    {
        private static string Single(params SpawnRule[] rules) =>
            Assert.Single(SpawnRuleValidation.Errors("room.test", rules));

        [Fact]
        public void A_valid_rule_set_has_no_errors()
        {
            Assert.Empty(SpawnRuleValidation.Errors("room.test", new[]
            {
                new SpawnRule("rats", "mob.rat", 1, 0),
                new SpawnRule("rats-2", "mob.rat", SpawnRuleLimits.MaxCountPerRule, 300),
            }));
        }

        [Fact]
        public void Blank_blueprint_is_an_error() =>
            Assert.Contains("blank 'blueprintId'", Single(new SpawnRule("x", " ", 1, 10)));

        [Fact]
        public void Malformed_id_is_an_error() =>
            Assert.Contains("malformed 'id'", Single(new SpawnRule("bad id", "mob.rat", 1, 10)));

        [Fact]
        public void Duplicate_id_is_an_error_case_insensitively() =>
            Assert.Contains("duplicate 'id'", Single(
                new SpawnRule("rats", "mob.rat", 1, 10),
                new SpawnRule("RATS", "mob.rat", 1, 10)));

        [Theory]
        [InlineData(0)]
        [InlineData(21)]
        public void Count_outside_1_to_20_is_an_error(int count) =>
            Assert.Contains("'count'", Single(new SpawnRule("rats", "mob.rat", count, 10)));

        [Fact]
        public void Negative_delay_is_an_error() =>
            Assert.Contains("negative", Single(new SpawnRule("rats", "mob.rat", 1, -1)));
    }
}
