using System.Linq;
using System.Reflection;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Architecture
{
    /// <summary>
    /// Architecture guards specific to the progression substrate: the display-only contract for
    /// ability rank (D3) and the standing DI-cycle guard on <see cref="ProgressionSystem"/>.
    /// </summary>
    public sealed class ProgressionGuardTests
    {
        private static (ProgressionSystem Progression, EntityService Ecs) CreateSystem()
        {
            var ecs = new EntityService();
            var progression = new ProgressionSystem(
                ecs,
                new FakeRandom(seed: 1),
                new PowerBudgetSystem(PowerBudgetTunables.Default),
                new AdvancementRuleRegistry());
            return (progression, ecs);
        }

        /// <summary>
        /// D3: an ability track is display-only. However much XP and however many ranks an ability
        /// accrues, it must contribute exactly zero to every score. Making rank grant power is a
        /// deliberate later balance slice that must fold into power-model.md and re-pin goldens —
        /// this test is what forces that conversation instead of letting power leak in quietly.
        /// </summary>
        [Fact]
        public void An_ability_track_never_yields_a_non_zero_effect_modifier()
        {
            var (progression, ecs) = CreateSystem();
            var contributor = new ProgressionEffectContributor(progression);
            var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(10, 10, 10, 10).Build();

            // Ranked far past any threshold on several ability tracks, and nothing else.
            foreach (var abilityId in new[] { "kick", "mend", "empower" })
                progression.AwardExperience(entity, ProgressionTrack.Ability(abilityId), 10_000, XpSource.AbilityUse);

            foreach (var score in System.Enum.GetValues<ScoreId>())
                Assert.Equal(0, contributor.GetModifiers(entity, score));

            Assert.Empty(contributor.GetActive(entity));
        }

        /// <summary>
        /// The same, through the full <c>IStatSystem.Get</c> fold — the path every gameplay
        /// consumer actually reads.
        /// </summary>
        [Fact]
        public void Ability_rank_does_not_move_an_effective_score()
        {
            var (progression, ecs) = CreateSystem();
            var effects = new EffectSystem(ecs, new IEffectContributor[] { new ProgressionEffectContributor(progression) });
            var attributes = new Hedron.Core.Modules.Attributes.Systems.AttributeSystem(
                ecs, effects, Microsoft.Extensions.Options.Options.Create(new Hedron.Core.Modules.Death.DeathOptions { HpFloor = -10 }));
            var stats = new StatSystem(attributes, effects);

            var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(10, 10, 10, 10).WithPools().Build();
            var before = stats.Get(entity, ScoreId.Body);

            progression.AwardExperience(entity, ProgressionTrack.Ability("kick"), 10_000, XpSource.AbilityUse);

            Assert.Equal(before, stats.Get(entity, ScoreId.Body));

            // Control: a *score* track's improvements DO move the same score, so the assertion
            // above is proving the ability-track exclusion rather than a broken contributor.
            progression.AwardExperience(entity, ScoreId.Body, ProgressionConstants.ThresholdBase, XpSource.CombatKill);
            Assert.Equal(before + ProgressionConstants.PowerPerImprovement, stats.Get(entity, ScoreId.Body));
        }

        /// <summary>
        /// Standing DI-cycle guard: <see cref="ProgressionSystem"/> must not reach for
        /// <c>IStatSystem</c> / <c>IEffectSystem</c>. Its own contributor is registered on the
        /// <c>IEffectContributor</c> port those systems aggregate, so depending on them would
        /// close the cycle StatSystem → EffectSystem → ProgressionEffectContributor →
        /// IProgressionSystem → StatSystem. The anti-grind proxy reads raw attributes instead.
        /// </summary>
        [Fact]
        public void ProgressionSystem_does_not_depend_on_the_stat_or_effect_systems()
        {
            var forbidden = new[] { typeof(IStatSystem), typeof(IEffectSystem) };
            var type = typeof(ProgressionSystem);

            var offendingParams = type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Where(p => forbidden.Any(f => f.IsAssignableFrom(p.ParameterType)))
                .Select(p => p.Name)
                .ToList();

            var offendingFields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => forbidden.Any(t => t.IsAssignableFrom(f.FieldType)))
                .Select(f => f.Name)
                .ToList();

            Assert.True(
                offendingParams.Count == 0 && offendingFields.Count == 0,
                "ProgressionSystem must not depend on IStatSystem/IEffectSystem (DI cycle). Offenders: " +
                string.Join(", ", offendingParams.Concat(offendingFields)));
        }

        /// <summary>
        /// Every wired <see cref="XpSource"/> has exactly one rule row, and the combat-kill row is
        /// still built from <see cref="ProgressionConstants.CombatTracks"/> — the list the balance
        /// simulator reduces over. Drift between the two would silently break the sim's per-track
        /// reduction.
        /// </summary>
        [Fact]
        public void The_combat_kill_row_stays_aligned_with_CombatTracks()
        {
            var registry = new AdvancementRuleRegistry();

            Assert.Equal(ProgressionConstants.Rules.Count, registry.All.Count);

            var kill = registry.Get(XpSource.CombatKill);
            Assert.Equal(
                ProgressionConstants.CombatTracks.Select(ProgressionTrack.Of).ToList(),
                kill.StaticTracks.ToList());

            // The draw contract depends on this row being a certainty with no decay.
            Assert.True(kill.BaseChance >= 1.0);
            Assert.Equal(0.0, kill.ChanceDecayPerImprovement);

            // RequiresPlayerEarner must stay OFF on this row. Its value is dictated by the balance
            // sandbox, not by a game rule: SimCombatantFactory builds mob-shaped combatants, so
            // turning this on would make every progressionRate run award nothing — and, because
            // the context-eligibility path returns no rows at all, would turn that silent
            // regression into a First() throw inside ProgressionScenarioExecutor. The two
            // use-based rows do set it (see A_non_character_earner_is_rejected_by_the_player_earner_flag).
            Assert.False(kill.Eligibility.RequiresPlayerEarner);
            Assert.True(registry.Get(XpSource.AbilityUse).Eligibility.RequiresPlayerEarner);
            Assert.True(registry.Get(XpSource.DamageTaken).Eligibility.RequiresPlayerEarner);
        }
    }
}
