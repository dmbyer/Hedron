using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.Commands.Authorization;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.BalanceInspection.Commands;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 2 — handler/command tests for <see cref="PowerCommand"/>.
    ///
    /// Coverage contract: docs/implementation-plans/power-budget-inspector.md P5 — self/item/mob
    /// target resolution, the golden-number surfacing end-to-end, and the admin-gate declaration.
    /// </summary>
    public sealed class PowerCommandTests
    {
        /// <summary>Stub <see cref="IStatSystem"/> keyed by (entityId, ScoreId). Unregistered scores return 0.</summary>
        private sealed class StubStatSystem : IStatSystem
        {
            private readonly Dictionary<(uint, ScoreId), int> _values = new();

            public void Set(uint entityId, ScoreId score, int value) => _values[(entityId, score)] = value;

            public int Get(uint entityId, ScoreId score) =>
                _values.TryGetValue((entityId, score), out var v) ? v : 0;

            public int GetEffectiveMind(uint entityId) => Get(entityId, ScoreId.Mind);
            public int GetEffectiveBody(uint entityId) => Get(entityId, ScoreId.Body);
            public int GetEffectiveSpirit(uint entityId) => Get(entityId, ScoreId.Spirit);
            public int GetEffectiveAttunement(uint entityId) => Get(entityId, ScoreId.Attunement);
            public int GetEffectiveAttackPower(uint entityId) => Get(entityId, ScoreId.AttackPower);
            public int GetEffectiveDefense(uint entityId) => Get(entityId, ScoreId.Defense);
            public int GetCurrentHp(uint entityId) => Get(entityId, ScoreId.HpCurrent);
            public int GetMaxHp(uint entityId) => Get(entityId, ScoreId.HpMax);
        }

        /// <summary>Minimal stub — only <see cref="TryFindTargetInRoom"/> is exercised by PowerCommand.</summary>
        private sealed class StubCombatSystem : ICombatSystem
        {
            private readonly EntityService _ecs;

            public StubCombatSystem(EntityService ecs) => _ecs = ecs;

            public bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId)
            {
                foreach (var (entityId, mob) in _ecs.GetAllComponents<MobDataComponent>())
                {
                    if (_ecs.TryGet<LocationComponent>(entityId, out var loc) &&
                        loc.RoomEntityId == roomEntityId &&
                        mob.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        mobEntityId = entityId;
                        return true;
                    }
                }
                mobEntityId = default;
                return false;
            }

            public bool CanBeAttacked(uint targetEntityId) => throw new NotImplementedException();
            public void StartCombat(uint attackerEntityId, uint defenderEntityId) => throw new NotImplementedException();
            public void EndCombat(uint attackerEntityId, uint defenderEntityId) => throw new NotImplementedException();
            public CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId) => throw new NotImplementedException();
            public CombatRoundResult ResolveAbilityStrike(uint attackerEntityId, uint defenderEntityId, int basePower, AspectComposition? composition = null) => throw new NotImplementedException();
        }

        private sealed class TestWorld
        {
            public EntityService Ecs { get; } = new();
            public StubStatSystem Stats { get; } = new();
            public PowerCommand Command { get; }

            public TestWorld()
            {
                var itemSystem = new ItemSystem(Ecs);
                var combatSystem = new StubCombatSystem(Ecs);
                Command = new PowerCommand(new PowerBudgetSystem(), Stats, itemSystem, combatSystem, Ecs);
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
            if (target is not null)
                values["target"] = target;

            return (ParsedArguments)ctor.Invoke(new object[] { values });
        }

        private static CommandContext MakeContext(uint invokerEntityId, ParsedArguments args, RecordingOutput output)
        {
            var session = new StubSession(invokerEntityId);
            return new CommandContext(session, invokerEntityId, args, output.WriterFor(invokerEntityId), Services: null!);
        }

        [Fact]
        public void RequiredPrivileges_contains_AdminRequirement()
        {
            var cmd = new PowerCommand(null!, null!, null!, null!, null!);
            Assert.Contains(cmd.RequiredPrivileges, r => r is AdminRequirement);
        }

        [Fact]
        public async Task Self_target_computes_power_from_IStatSystem_and_has_no_authored_band()
        {
            var world = new TestWorld();
            var invokerId = new EntityBuilder(world.Ecs).AsPlayer().Build();

            world.Stats.Set(invokerId, ScoreId.Mind, 10);
            world.Stats.Set(invokerId, ScoreId.Body, 20);
            world.Stats.Set(invokerId, ScoreId.Spirit, 10);
            world.Stats.Set(invokerId, ScoreId.Attunement, 10);
            world.Stats.Set(invokerId, ScoreId.HpMax, 150);
            world.Stats.Set(invokerId, ScoreId.ManaMax, 50);
            world.Stats.Set(invokerId, ScoreId.StaminaMax, 50);
            world.Stats.Set(invokerId, ScoreId.AstraMax, 10);
            world.Stats.Set(invokerId, ScoreId.AttackPower, 10);
            world.Stats.Set(invokerId, ScoreId.Defense, 5);

            var output = new RecordingOutput();
            var ctx = MakeContext(invokerId, MakeArgs(null), output);

            await world.Command.ExecuteAsync(ctx);

            var readout = Assert.Single(GetMessages<PowerReadoutMessage>(output, invokerId));
            Assert.Equal("You", readout.TargetLabel);
            Assert.Equal(355, readout.Power); // golden number: 1*10+5*20+1*10+1*10+1*150+5*10+5*5
            Assert.Equal(2, readout.Band);
            Assert.Null(readout.AuthoredBand);
        }

        [Fact]
        public async Task Self_alias_resolves_the_same_as_no_argument()
        {
            var world = new TestWorld();
            var invokerId = new EntityBuilder(world.Ecs).AsPlayer().Build();
            world.Stats.Set(invokerId, ScoreId.Body, 10);

            var output = new RecordingOutput();
            var ctx = MakeContext(invokerId, MakeArgs("self"), output);

            await world.Command.ExecuteAsync(ctx);

            var readout = Assert.Single(GetMessages<PowerReadoutMessage>(output, invokerId));
            Assert.Equal("You", readout.TargetLabel);
        }

        [Fact]
        public async Task Item_target_computes_power_from_StatBonuses_and_echoes_authored_band()
        {
            var world = new TestWorld();
            var roomId = new EntityBuilder(world.Ecs).Build();
            var invokerId = new EntityBuilder(world.Ecs).AsPlayer().InRoom(roomId)
                .With(new InventoryComponent())
                .Build();

            var item = world.Ecs.CreateEntity();
            world.Ecs.AddComponent(item.Id, new ItemDataComponent
            {
                Name = "Ancient Blade",
                TierBand = 2,
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 15), new EquipmentStatBonus(ScoreId.Defense, 5) },
            });
            world.Ecs.Get<InventoryComponent>(invokerId).ItemEntityIds.Add(item.Id);

            var output = new RecordingOutput();
            var ctx = MakeContext(invokerId, MakeArgs("Ancient Blade"), output);

            await world.Command.ExecuteAsync(ctx);

            var readout = Assert.Single(GetMessages<PowerReadoutMessage>(output, invokerId));
            Assert.Equal("Ancient Blade", readout.TargetLabel);
            Assert.Equal(220, readout.Power); // (5*15 + 5*5) + tier(2)*(weight(Body)+weight(HpMax))*10
            Assert.Equal(0, readout.Band); // 220 is below BandAnchor(1)=245 — authored band 2 does not match
            Assert.Equal(2, readout.AuthoredBand);
        }

        [Fact]
        public async Task Mob_target_computes_power_from_IStatSystem_and_echoes_authored_band()
        {
            var world = new TestWorld();
            var roomId = new EntityBuilder(world.Ecs).Build();
            var invokerId = new EntityBuilder(world.Ecs).AsPlayer().InRoom(roomId).Build();
            var mobId = new EntityBuilder(world.Ecs).AsMob("Goblin", new[] { "goblin" }).InRoom(roomId).Build();
            world.Ecs.Get<MobDataComponent>(mobId).TierBand = 3;

            world.Stats.Set(mobId, ScoreId.Body, 12);
            world.Stats.Set(mobId, ScoreId.HpMax, 80);
            world.Stats.Set(mobId, ScoreId.AttackPower, 6);
            world.Stats.Set(mobId, ScoreId.Defense, 3);

            var output = new RecordingOutput();
            var ctx = MakeContext(invokerId, MakeArgs("Goblin"), output);

            await world.Command.ExecuteAsync(ctx);

            var readout = Assert.Single(GetMessages<PowerReadoutMessage>(output, invokerId));
            Assert.Equal("Goblin", readout.TargetLabel);
            Assert.Equal(365, readout.Power);
            Assert.Equal(3, readout.Band);
            Assert.Equal(3, readout.AuthoredBand);
        }

        [Fact]
        public async Task Unresolved_target_writes_not_found_message()
        {
            var world = new TestWorld();
            var roomId = new EntityBuilder(world.Ecs).Build();
            var invokerId = new EntityBuilder(world.Ecs).AsPlayer().InRoom(roomId).Build();

            var output = new RecordingOutput();
            var ctx = MakeContext(invokerId, MakeArgs("unicorn"), output);

            await world.Command.ExecuteAsync(ctx);

            Assert.Empty(GetMessages<PowerReadoutMessage>(output, invokerId));
            var plain = Assert.Single(GetMessages<PlainMessage>(output, invokerId));
            Assert.Equal(OutputSeverity.System, plain.Severity);
        }

        private static List<TMessage> GetMessages<TMessage>(RecordingOutput output, uint recipientEntityId)
            where TMessage : IOutputMessage
        {
            var result = new List<TMessage>();
            foreach (var (type, recipient, message) in output.All)
            {
                if (type == typeof(TMessage) && recipient == recipientEntityId)
                    result.Add((TMessage)message);
            }
            return result;
        }
    }
}
