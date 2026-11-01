using System;
using System.Collections.Generic;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Combat
{
    /// <summary>
    /// Tier 3 — R13a, the A3/A7 contract (combat-effective-scores T20): progression changes who wins.
    /// The same live-spawned mob template, the same gear (none), the same seed; one arm carries Body and
    /// HpMax progression improvements. Before the slice both arms won the same number of fights,
    /// because combat read raw Body and the progressed HpMax never filled.
    /// </summary>
    public sealed class CombatEffectiveScoresFlowTests
    {
        private const int Seed = 2026;
        private const int Fights = 200;

        private static int PlayerWins(bool progressed)
        {
            var ecs = new EntityService();
            var progression = new ProgressionSystem(ecs, new FakeRandom(1), new AdvancementRuleRegistry());
            var effects = new EffectSystem(ecs, new IEffectContributor[] { new ProgressionEffectContributor(progression) });
            var attributes = new AttributeSystem(ecs, effects, Options.Create(new DeathOptions { HpFloor = -10 }));
            var stats = new StatSystem(attributes, effects);
            var combat = new CombatSystem(ecs, stats, attributes, new AspectSystem(ecs), new FakeRandom(Seed));

            var registry = new TemplateRegistry(ecs);
            registry.Register("mob.test.brute", new MobTemplate("mob.test.brute") { Name = "brute", Body = 12, MaxHp = 110 });

            var player = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).WithPools(hp: 100).Build();
            if (progressed)
            {
                ecs.AddComponent(player, new ProgressionComponent
                {
                    Improvements =
                    {
                        [ProgressionTrack.Of(ScoreId.Body)] = 5,   // +10 Body
                        [ProgressionTrack.Of(ScoreId.HpMax)] = 10, // +20 HpMax
                    },
                });
            }

            var wins = 0;
            for (var fight = 0; fight < Fights; fight++)
            {
                var mob = registry.Spawn("mob.test.brute").Id;
                // Postcondition 15: a live spawn starts at its effective cap by construction.
                Assert.Equal(attributes.GetEffectiveMax(mob, ResourceType.Hp), attributes.GetCurrentHp(mob));
                attributes.RestoreToFraction(player, ResourceType.Hp, 1.0);

                while (true)
                {
                    if (combat.ExecuteRound(player, mob).Outcome == CombatRoundOutcome.MobDied) { wins++; break; }
                    if (combat.ExecuteRound(mob, player).Outcome == CombatRoundOutcome.PlayerIncapacitated) break;
                }
                ecs.DestroyEntity(mob);
            }
            return wins;
        }

        [Fact]
        public void Progressed_character_beats_fresh_character_at_pinned_seed()
        {
            var fresh = PlayerWins(progressed: false);
            var progressed = PlayerWins(progressed: true);

            Assert.True(progressed > fresh, $"progressed {progressed} vs fresh {fresh}");
            // Golden pins at (seed 2026, N 200).
            Assert.Equal(8, fresh);
            Assert.Equal(200, progressed);
        }
    }
}
