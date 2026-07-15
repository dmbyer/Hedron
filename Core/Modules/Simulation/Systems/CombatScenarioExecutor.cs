using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>One completed 1v1 run's outcome — the reduce step's input.</summary>
    public sealed record RunRecord(
        int RunIndex,
        /// <summary>0 = side A won, 1 = side B won, <see langword="null"/> = draw/timeout.</summary>
        int? WinnerSide,
        int Ticks,
        int SideADamageDealt,
        int SideBDamageDealt);

    /// <summary>
    /// Drives one 1v1 combat run to completion: a synthetic heartbeat that performs the same
    /// per-tick sequence the live handlers run (effects → cooldowns → actions → regen) by calling
    /// system methods directly — no bus, no handlers (INV-5). Stateless; safe to instantiate once
    /// per run or reuse across runs on the same thread.
    /// </summary>
    public sealed class CombatScenarioExecutor
    {
        private static readonly System.TimeSpan TickElapsed = System.TimeSpan.FromSeconds(1);

        public RunRecord ExecuteRun(
            SandboxWorld world,
            uint entityA,
            uint entityB,
            ISimCombatantPolicy policyA,
            ISimCombatantPolicy policyB,
            int maxTicksPerRun,
            int runIndex)
        {
            var damageA = 0;
            var damageB = 0;
            int? winnerSide = null;
            var ticksElapsed = 0;

            for (var tick = 0; tick < maxTicksPerRun; tick++)
            {
                ticksElapsed = tick + 1;

                // 1. Advance effects — due periodics + expiry (no EffectExpiredEvent: no bus).
                var tickResult = world.Effects.AdvanceTick(TickElapsed);
                foreach (var application in tickResult.DueApplications)
                    ApplyPeriodicMagnitude(world, application.EntityId, application.Effect.Params.TargetScore, application.Magnitude);

                var aDeadFromPeriodic = world.Stats.GetCurrentHp(entityA) <= 0;
                var bDeadFromPeriodic = world.Stats.GetCurrentHp(entityB) <= 0;
                if (aDeadFromPeriodic || bDeadFromPeriodic)
                {
                    winnerSide = aDeadFromPeriodic && bDeadFromPeriodic ? null : (aDeadFromPeriodic ? 1 : 0);
                    break;
                }

                // 2. Advance ability cooldowns.
                world.Abilities.AdvanceCooldowns(TickElapsed);

                // 3. Draw initiative order from the run's IRandom — a fixed actor order would give
                // side A a structural first-strike advantage, silently biasing the equal-cell 50%
                // expectation the CI invariant exists to catch.
                var aFirst = world.Random.Next(2) == 0;
                var order = aFirst
                    ? new[] { (Self: entityA, Opponent: entityB, Policy: policyA, IsSideA: true),
                               (Self: entityB, Opponent: entityA, Policy: policyB, IsSideA: false) }
                    : new[] { (Self: entityB, Opponent: entityA, Policy: policyB, IsSideA: false),
                               (Self: entityA, Opponent: entityB, Policy: policyA, IsSideA: true) };

                foreach (var (selfId, opponentId, policy, isSideA) in order)
                {
                    if (world.Stats.GetCurrentHp(selfId) <= 0 || world.Stats.GetCurrentHp(opponentId) <= 0)
                        break; // resolved earlier this tick

                    var action = policy.ChooseAction(world, selfId, opponentId, tick);
                    var result = ExecuteAction(world, selfId, opponentId, action);
                    if (result is { } r)
                    {
                        if (isSideA) damageA += r.DamageDealt; else damageB += r.DamageDealt;
                        if (r.Outcome == CombatRoundOutcome.MobDied)
                        {
                            winnerSide = isSideA ? 0 : 1;
                            break;
                        }
                    }
                }

                if (winnerSide.HasValue)
                    break;

                // 4. Regeneration (suppressed while InCombat — near-free call, included for fidelity
                // with the live heartbeat order per the plan's resolved OQ2).
                world.Regeneration.ApplyTickRegen(tick);
            }

            return new RunRecord(runIndex, winnerSide, ticksElapsed, damageA, damageB);
        }

        /// <summary>Activates or melees, mirroring <c>AbilityInvocationPipeline</c>'s steps 3 and 5 minus every bus publish.</summary>
        private static CombatRoundResult? ExecuteAction(SandboxWorld world, uint selfId, uint opponentId, SimAction action)
        {
            if (action is SimAction.MeleeAttack)
                return world.Combat.ExecuteRound(selfId, opponentId);

            if (action is SimAction.UseAbility useAbility)
            {
                var isOffensive = world.Abilities.IsOffensive(useAbility.AbilityId);
                var activation = world.Abilities.Activate(selfId, useAbility.AbilityId, opponentId, resolveOffensiveExternally: isOffensive);

                if (activation.Outcome != AbilityActivationOutcome.Activated)
                    return null; // failed activation (cooldown/cost/etc.) — a passed action, mirrors live UX

                if (isOffensive && activation.OffensivePower.HasValue)
                    return world.Combat.ResolveAbilityStrike(selfId, opponentId, activation.OffensivePower.Value);

                return null; // non-offensive ability (buff/heal) — no combat-round outcome to record
            }

            return null;
        }

        private static void ApplyPeriodicMagnitude(SandboxWorld world, uint entityId, ScoreId targetScore, int magnitude)
        {
            switch (targetScore)
            {
                case ScoreId.HpCurrent:
                    world.Attributes.SetCurrentHp(entityId, world.Attributes.GetCurrentHp(entityId) + magnitude);
                    break;
                case ScoreId.ManaCurrent:
                    world.Attributes.SetCurrentMana(entityId, world.Attributes.GetCurrentMana(entityId) + magnitude);
                    break;
                case ScoreId.StaminaCurrent:
                    world.Attributes.SetCurrentStamina(entityId, world.Attributes.GetCurrentStamina(entityId) + magnitude);
                    break;
                case ScoreId.AstraCurrent:
                    world.Attributes.SetCurrentAstra(entityId, world.Attributes.GetCurrentAstra(entityId) + magnitude);
                    break;
            }
        }
    }
}
