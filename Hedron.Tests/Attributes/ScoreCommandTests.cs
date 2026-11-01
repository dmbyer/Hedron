using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hedron.Core.Commands;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.Attributes.Commands;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Attributes
{
    /// <summary>
    /// Tier 2 — <c>score</c> reports the effective derived scores (combat-effective-scores T27,
    /// Postcondition 22): the message's Attack/Defense fields come from <c>IStatSystem.Get</c>, so a
    /// Body buff shows up. Field assertions, not prose.
    /// </summary>
    public sealed class ScoreCommandTests
    {
        private static ParsedArguments EmptyArgs()
        {
            var ctor = typeof(ParsedArguments).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
                types: new[] { typeof(IReadOnlyDictionary<string, object?>) }, modifiers: null)!;
            return (ParsedArguments)ctor.Invoke(new object[] { new Dictionary<string, object?>() });
        }

        [Fact]
        public async Task Score_reports_effective_attack_and_defense()
        {
            var ecs = new EntityService();
            var effects = new EffectSystem(ecs, Array.Empty<IEffectContributor>());
            var attributes = new AttributeSystem(ecs, effects, Options.Create(new DeathOptions()));
            var stats = new StatSystem(attributes, effects);
            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).WithPools().Build();
            ecs.Get<CharacterComponent>(player).CharacterName = "Tester";
            TestEffects.ApplyStatModifier(effects, player, ScoreId.Body, 10);

            var command = new ScoreCommand(ecs, stats, new EntityStateService(ecs), new WalletSystem(ecs));
            var output = new RecordingOutput();
            await command.ExecuteAsync(new CommandContext(
                new StubSession(player), player, EmptyArgs(), output.WriterFor(player), Services: null!));

            var message = (ScoreDisplayMessage)output.All.First(r => r.MessageType == typeof(ScoreDisplayMessage)).Message;
            Assert.Equal(stats.Get(player, ScoreId.AttackPower), message.AttackPower);
            Assert.Equal(stats.Get(player, ScoreId.Defense), message.Defense);
            Assert.Equal(10, message.AttackPower); // (10 + 10) / 2
            Assert.Equal(5, message.Defense);      // (10 + 10) / 4
        }
    }
}
