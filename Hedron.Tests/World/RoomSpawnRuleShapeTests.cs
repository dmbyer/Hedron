using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — the room <c>spawnRules</c> YAML shape (spawn-authoring T4): write → read preserves
    /// id/blueprint/count/delay, the writer never emits the legacy <c>minCount</c>/<c>maxCount</c>,
    /// and an id-less rule reads its default id.
    /// </summary>
    public sealed class RoomSpawnRuleRoundTripTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hedron-spawnrule-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static RoomTemplateDeserializer Deserializer() =>
            new(NullLogger<RoomTemplateDeserializer>.Instance);

        [Fact]
        public async Task Write_then_read_preserves_every_field_and_emits_no_legacy_keys()
        {
            var writer = new RoomContentWriter(Options.Create(new WorldOptions { ContentDirectory = _dir }));
            var room = new RoomTemplate("room.cellar") { Name = "Cellar" };
            room.SpawnRules.Add(new SpawnRule("rats-north", "mob.rat", 3, 120));
            room.SpawnRules.Add(new SpawnRule("item.herb", "item.herb", 1, 0));

            await writer.WriteAsync(room);
            var yaml = await File.ReadAllTextAsync(Path.Combine(_dir, "rooms", "room.cellar.yaml"));
            var loaded = (RoomTemplate)Deserializer().Deserialize(yaml);

            Assert.Equal(room.SpawnRules, loaded.SpawnRules);
            Assert.DoesNotContain("minCount", yaml);
            Assert.DoesNotContain("maxCount", yaml);
            Assert.Contains("id: item.herb", yaml); // the id is always written
        }

        [Fact]
        public void Omitted_id_count_and_delay_read_their_defaults()
        {
            const string yaml = "id: room.cellar\nname: Cellar\nspawnRules:\n  - blueprintId: mob.rat\n";

            var rule = Assert.Single(((RoomTemplate)Deserializer().Deserialize(yaml)).SpawnRules);

            Assert.Equal(new SpawnRule("mob.rat", "mob.rat", 1, SpawnRuleLimits.DefaultRespawnDelaySeconds), rule);
        }
    }

    /// <summary>
    /// Tier 1 throws-tests — <see cref="RoomTemplateDeserializer"/> fail-fast spawn-rule shape
    /// errors (spawn-authoring T6). Each throws <see cref="ContentShapeException"/> whose message
    /// names the room, the rule's blueprint and the offending key.
    /// </summary>
    public sealed class RoomTemplateDeserializerShapeTests
    {
        private static ContentShapeException Throws(string ruleYaml)
        {
            var yaml = "id: room.cellar\nname: Cellar\nspawnRules:\n" + ruleYaml;
            var deserializer = new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance);
            return Assert.Throws<ContentShapeException>(() => deserializer.Deserialize(yaml));
        }

        private static void AssertNames(ContentShapeException ex, params string[] parts)
        {
            Assert.Contains("room.cellar", ex.Message);
            foreach (var part in parts)
                Assert.Contains(part, ex.Message);
        }

        [Fact]
        public void MinCount_present_throws() =>
            AssertNames(Throws("  - blueprintId: mob.rat\n    minCount: 1\n"), "mob.rat", "minCount", "count: N");

        [Fact]
        public void MaxCount_present_throws() =>
            AssertNames(Throws("  - blueprintId: mob.rat\n    maxCount: 3\n"), "mob.rat", "maxCount", "count: N");

        [Fact]
        public void Blank_blueprint_throws() =>
            AssertNames(Throws("  - id: x\n    blueprintId: ''\n"), "blueprintId");

        [Fact]
        public void Malformed_id_throws() =>
            AssertNames(Throws("  - id: 'bad id'\n    blueprintId: mob.rat\n"), "mob.rat", "'id'");

        [Fact]
        public void Duplicate_explicit_id_throws() =>
            AssertNames(Throws("  - id: rats\n    blueprintId: mob.rat\n  - id: rats\n    blueprintId: mob.bat\n"),
                "mob.bat", "duplicate 'id'");

        [Fact]
        public void Two_id_less_rules_sharing_a_blueprint_throw_the_derived_id_collision() =>
            AssertNames(Throws("  - blueprintId: mob.rat\n  - blueprintId: mob.rat\n"), "mob.rat", "duplicate 'id'");

        [Theory]
        [InlineData(0)]
        [InlineData(21)]
        public void Count_out_of_range_throws(int count) =>
            AssertNames(Throws($"  - blueprintId: mob.rat\n    count: {count}\n"), "mob.rat", "'count'");

        [Fact]
        public void Negative_delay_throws() =>
            AssertNames(Throws("  - blueprintId: mob.rat\n    respawnDelaySeconds: -1\n"), "mob.rat", "respawnDelaySeconds");

        [Fact]
        public void An_id_less_rule_with_a_70_character_blueprint_reads_the_truncated_default()
        {
            var blueprint = "mob." + new string('x', 66);
            var yaml = $"id: room.cellar\nspawnRules:\n  - blueprintId: {blueprint}\n";
            var deserializer = new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance);

            var rule = Assert.Single(((RoomTemplate)deserializer.Deserialize(yaml)).SpawnRules);

            Assert.Equal(blueprint[..60], rule.Id);
        }
    }

    /// <summary>
    /// Tier 1 throws-tests — legacy spawn-room keys on mob/item files (spawn-authoring T7): a
    /// non-blank key is a <see cref="ContentShapeException"/>; a blank key is ignored and dropped
    /// on the next write.
    /// </summary>
    public sealed class SpawnRoomKeyShapeTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"hedron-spawnkey-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static readonly MobTemplateDeserializer Mobs = new(NullLogger<MobTemplateDeserializer>.Instance);
        private static readonly ItemTemplateDeserializer Items = new(NullLogger<ItemTemplateDeserializer>.Instance);

        [Fact]
        public void Mob_with_a_non_blank_spawnRoomBlueprintId_throws_naming_the_room_and_the_fix()
        {
            var ex = Assert.Throws<ContentShapeException>(() =>
                Mobs.Deserialize("blueprintId: mob.rat\nname: rat\nspawnRoomBlueprintId: room.cellar\n"));

            Assert.Contains("mob.rat", ex.Message);
            Assert.Contains("spawnRoomBlueprintId", ex.Message);
            Assert.Contains("room.cellar", ex.Message);
            Assert.Contains("spawnRules", ex.Message);
        }

        [Fact]
        public void Item_with_a_non_blank_spawnRoomId_throws_naming_the_room_and_the_fix()
        {
            var ex = Assert.Throws<ContentShapeException>(() =>
                Items.Deserialize("blueprintId: item.herb\nname: herb\nspawnRoomId: room.garden\n"));

            Assert.Contains("item.herb", ex.Message);
            Assert.Contains("spawnRoomId", ex.Message);
            Assert.Contains("room.garden", ex.Message);
            Assert.Contains("spawnRules", ex.Message);
        }

        [Fact]
        public async Task Blank_mob_key_is_ignored_and_dropped_on_rewrite()
        {
            var mob = (MobTemplate)Mobs.Deserialize("blueprintId: mob.rat\nname: rat\nspawnRoomBlueprintId: ''\n");

            await new MobContentWriter(Options.Create(new WorldOptions { ContentDirectory = _dir })).WriteAsync(mob);

            var yaml = await File.ReadAllTextAsync(Path.Combine(_dir, "mobs", "mob.rat.yaml"));
            Assert.DoesNotContain("spawnRoom", yaml);
        }

        [Fact]
        public async Task Blank_item_key_is_ignored_and_dropped_on_rewrite()
        {
            var item = (ItemTemplate)Items.Deserialize("blueprintId: item.herb\nname: herb\nspawnRoomId: ''\n");

            await new ItemContentWriter(Options.Create(new WorldOptions { ContentDirectory = _dir })).WriteAsync(item);

            var yaml = await File.ReadAllTextAsync(Path.Combine(_dir, "items", "item.herb.yaml"));
            Assert.DoesNotContain("spawnRoom", yaml);
        }
    }
}
