using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Commands;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 2 — command tests for <see cref="ConsiderCommand"/>, over a real
    /// <see cref="RelativePowerSystem"/> / <see cref="StatSystem"/> (equipment contributor) and a
    /// real <see cref="CombatSystem"/> (room lookup + protection gate).
    ///
    /// Coverage contract: docs/implementation-plans/relative-power-consider.md WP-3 — one line per
    /// label (Post 1), no digit in any output (Post 2), the label recomputed after gear changes
    /// (Post 3, SR4), the refusal / not-found / untargetable / unreadable lines (Post 6), and the
    /// read-only, event-on-success-only contract (Post 7, resolved decision 8). Output is
    /// <see cref="PlainMessage"/> — no <c>NarrationMessage</c> base exists yet (resolved decision 9),
    /// so the <c>ConsiderNarrationMessage</c> row of the test plan does not apply.
    /// </summary>
    public sealed class ConsiderCommandTests
    {
        private const uint RoomId = 1u;

        private sealed class TestWorld
        {
            public EntityService Ecs { get; } = new();
            public RecordingEventBus Bus { get; } = new(dispatch: false);
            public RecordingOutput Output { get; } = new();
            public RelativePowerSystem RelativePower { get; }
            public EntityStateService EntityState { get; }
            public ConsiderCommand Command { get; }

            public TestWorld()
            {
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { new EquipmentEffectContributor(Ecs) });
                var attributes = new AttributeSystem(Ecs, effects, Options.Create(new DeathOptions { HpFloor = -10 }));
                var stats = new StatSystem(attributes, effects);
                var combat = new CombatSystem(Ecs, stats, attributes, new AspectSystem(Ecs), new FakeRandom(1));
                RelativePower = new RelativePowerSystem(Ecs, stats, new PowerBudgetSystem(PowerBudgetTunables.Default));
                EntityState = new EntityStateService(Ecs);
                Command = new ConsiderCommand(RelativePower, combat, new ItemSystem(Ecs), Ecs, Bus);
            }

            /// <summary>Fresh character: default attributes and pools — 386 effective power.</summary>
            public uint Player() => new EntityBuilder(Ecs).AsPlayer().WithAttributes().WithPools()
                .InRoom(RoomId).With(new InventoryComponent()).Build();

            public uint Mob(string name, int attr = 10, int hp = 100, int tier = 0, string? blueprintId = "mob-wolf")
            {
                var builder = new EntityBuilder(Ecs).AsMob(name, new[] { name })
                    .WithAttributes(attr, attr, attr, attr).WithPools(hp: hp).InRoom(RoomId);
                if (blueprintId != null) builder.With(new BlueprintComponent { BlueprintId = blueprintId });
                var id = builder.Build();
                Ecs.Get<MobDataComponent>(id).Tier = tier;
                return id;
            }

            public async Task<IReadOnlyList<string>> Consider(uint player, string? target)
            {
                var before = Output.All.Count;
                await Command.ExecuteAsync(new CommandContext(
                    new StubSession(player), player, MakeArgs(target), Output.WriterFor(player), Services: null!));
                return Output.All.Skip(before)
                    .Select(r => Assert.IsType<PlainMessage>(r.Message).Text)
                    .ToList();
            }
        }

        private static ParsedArguments MakeArgs(string? target)
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) },
                modifiers: null)!;
            var values = new Dictionary<string, object?>();
            if (target is not null) values["target"] = target;
            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        /// <summary>
        /// Five mobs vs. the 386-power fresh character, one per label band:
        /// trivial ≈ 0.09, easy ≈ 0.75, even = 1.0, dangerous ≈ 1.13, deadly ≈ 1.31 (tier 1).
        /// </summary>
        public static IEnumerable<object[]> LabelFixtures() => new[]
        {
            new object[] { RelativePowerLabel.Trivial, 1, 10, 0 },
            new object[] { RelativePowerLabel.Easy, 5, 100, 0 },
            new object[] { RelativePowerLabel.Even, 10, 100, 0 },
            new object[] { RelativePowerLabel.Dangerous, 10, 125, 0 },
            new object[] { RelativePowerLabel.Deadly, 10, 100, 1 },
        };

        // ── One line per label (Post 1, 2, 7) ───────────────────────────────────

        [Theory]
        [MemberData(nameof(LabelFixtures))]
        public async Task Each_label_prints_one_phrase_line_and_publishes_one_event(
            RelativePowerLabel expected, int attr, int hp, int tier)
        {
            var world = new TestWorld();
            var player = world.Player();
            var mob = world.Mob("wolf", attr, hp, tier);
            Assert.Equal(expected, world.RelativePower.Relate(player, mob).Label); // fixture guard

            var lines = await world.Consider(player, "wolf");

            var line = Assert.Single(lines);
            Assert.Equal(ConsiderCommand.Phrase(expected, "Wolf"), line);
            Assert.DoesNotMatch(new Regex(@"\d"), line);

            var evt = Assert.IsType<MobConsideredEvent>(Assert.Single(world.Bus.Published));
            Assert.Equal(player, evt.PlayerEntityId);
            Assert.Equal("mob-wolf", evt.MobBlueprintId);
            Assert.Equal(expected, evt.DangerLabel);
        }

        [Fact]
        public void Every_label_has_a_distinct_phrase()
        {
            var phrases = Enum.GetValues<RelativePowerLabel>().Select(l => ConsiderCommand.Phrase(l, "Wolf")).ToList();
            Assert.Equal(phrases.Count, phrases.Distinct().Count());
            Assert.All(phrases, p => Assert.StartsWith("Wolf ", p));
        }

        [Fact]
        public async Task A_mob_without_a_blueprint_publishes_an_empty_blueprint_id()
        {
            var world = new TestWorld();
            var player = world.Player();
            world.Mob("wolf", blueprintId: null);

            await world.Consider(player, "wo");

            var evt = Assert.IsType<MobConsideredEvent>(Assert.Single(world.Bus.Published));
            Assert.Equal(string.Empty, evt.MobBlueprintId);
        }

        // ── Recomputed every call (Post 3, SR4) ─────────────────────────────────

        [Fact]
        public async Task Gear_worn_between_two_considers_changes_the_label()
        {
            var world = new TestWorld();
            var player = world.Player();
            world.Mob("wolf", hp: 125); // 436 vs 386 → dangerous

            var before = await world.Consider(player, "wolf");

            var weapon = world.Ecs.CreateEntity().Id;
            world.Ecs.AddComponent(weapon, new ItemDataComponent
            {
                Name = "blade",
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 10) }, // +80 power → 466
            });
            world.Ecs.AddComponent(player, new EquipmentComponent { Slots = { [WornSlot.MainHand] = weapon } });

            var after = await world.Consider(player, "wolf");

            Assert.Equal(ConsiderCommand.Phrase(RelativePowerLabel.Dangerous, "Wolf"), Assert.Single(before));
            Assert.Equal(ConsiderCommand.Phrase(RelativePowerLabel.Even, "Wolf"), Assert.Single(after));
            Assert.Equal(new[] { RelativePowerLabel.Dangerous, RelativePowerLabel.Even },
                world.Bus.Published.OfType<MobConsideredEvent>().Select(e => e.DangerLabel));
        }

        // ── Refusals and not-found publish nothing (Post 6, 7) ──────────────────

        [Fact]
        public async Task Untargetable_mob_prints_the_cannot_fight_line_and_no_label()
        {
            var world = new TestWorld();
            var player = world.Player();
            var mob = world.Mob("shopkeeper");
            world.Ecs.AddComponent(mob, new ProtectionComponent { Flags = ProtectionFlags.Untargetable });

            var lines = await world.Consider(player, "shop");

            Assert.Equal("Shopkeeper is not something you can fight.", Assert.Single(lines));
            Assert.Empty(world.Bus.Published);
        }

        [Theory]
        [InlineData("self")]
        [InlineData("me")]
        [InlineData("ME")]
        public async Task Self_tokens_are_refused_as_not_a_creature(string token)
        {
            var world = new TestWorld();
            var player = world.Player();

            Assert.Equal("You can only size up creatures.", Assert.Single(await world.Consider(player, token)));
            Assert.Empty(world.Bus.Published);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task An_item_token_in_inventory_or_room_is_refused_as_not_a_creature(bool inInventory)
        {
            var world = new TestWorld();
            var player = world.Player();
            var item = world.Ecs.CreateEntity().Id;
            world.Ecs.AddComponent(item, new ItemDataComponent { Name = "lantern" });
            if (inInventory)
                world.Ecs.Get<InventoryComponent>(player).ItemEntityIds.Add(item);
            else
                world.Ecs.AddComponent(item, new LocationComponent { RoomEntityId = RoomId, RoomBlueprintId = "1" });

            Assert.Equal("You can only size up creatures.", Assert.Single(await world.Consider(player, "lantern")));
            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task An_unknown_token_or_another_players_name_is_not_here()
        {
            var world = new TestWorld();
            var player = world.Player();
            var other = world.Player();
            world.Ecs.Get<Hedron.Core.Modules.Account.Components.CharacterComponent>(other).CharacterName = "Bob";

            Assert.Equal("You don't see that here.", Assert.Single(await world.Consider(player, "dragon")));
            Assert.Equal("You don't see that here.", Assert.Single(await world.Consider(player, "Bob")));
            Assert.Empty(world.Bus.Published);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task No_argument_asks_consider_whom(string? target)
        {
            var world = new TestWorld();
            var player = world.Player();

            Assert.Equal("Consider whom?", Assert.Single(await world.Consider(player, target)));
            Assert.Empty(world.Bus.Published);
        }

        [Fact]
        public async Task An_unreadable_reading_prints_cannot_tell_and_publishes_nothing()
        {
            var world = new TestWorld();
            var player = world.Player();
            // A mob with no AttributesComponent is unreadable (IRelativePowerSystem readability gate).
            new EntityBuilder(world.Ecs).AsMob("wisp", new[] { "wisp" }).WithPools().InRoom(RoomId).Build();

            var line = Assert.Single(await world.Consider(player, "wisp"));

            Assert.Equal("You can't tell how dangerous Wisp is.", line);
            Assert.Empty(world.Bus.Published);
        }

        // ── Free and read-only (Post 7, SR6) ────────────────────────────────────

        [Fact]
        public async Task Usable_in_combat_and_spends_nothing()
        {
            var world = new TestWorld();
            var player = world.Player();
            world.Ecs.AddComponent(player, new AbilitiesComponent
            {
                Known = { "kick" },
                CooldownRemaining = { ["kick"] = 2.5f },
            });
            var mob = world.Mob("wolf");
            Assert.True(world.EntityState.TryEnterState(player, EntityStateFlags.InCombat, out _));
            var pools = world.Ecs.Get<PoolsComponent>(player);
            pools.CurrentHp = 40;
            pools.CurrentStamina = 7;
            var componentsBefore = world.Ecs.GetAllComponentsForEntity(player).Select(c => c.ComponentType).ToHashSet();

            var line = Assert.Single(await world.Consider(player, "wolf"));

            Assert.Equal(ConsiderCommand.Phrase(RelativePowerLabel.Even, "Wolf"), line);
            Assert.True(world.EntityState.IsInState(player, EntityStateFlags.InCombat));
            Assert.Equal(40, pools.CurrentHp);
            Assert.Equal(7, pools.CurrentStamina);
            Assert.Equal(2.5f, world.Ecs.Get<AbilitiesComponent>(player).CooldownRemaining["kick"]);
            Assert.Equal(componentsBefore,
                world.Ecs.GetAllComponentsForEntity(player).Select(c => c.ComponentType).ToHashSet());
            Assert.False(world.Ecs.HasComponent<CombatStateComponent>(mob));
            Assert.IsType<MobConsideredEvent>(Assert.Single(world.Bus.Published));
        }

        [Fact]
        public void Declares_the_con_alias_public_partial_and_not_usable_while_incapacitated()
        {
            ICommand cmd = new ConsiderCommand(null!, null!, null!, null!, null!);
            Assert.Equal("consider", cmd.Name);
            Assert.Equal(new[] { "con" }, cmd.Aliases);
            Assert.Equal(CommandMatchingMode.Partial, cmd.MatchingMode);
            Assert.Empty(cmd.RequiredPrivileges);
            Assert.False(cmd.UsableWhileIncapacitated);
        }
    }
}
