using System.Collections.Generic;

namespace Hedron.Core.Modules.Ascension.Systems
{
    /// <summary>Reasons <see cref="IAscensionSystem.CanAscend"/> can return besides eligible.</summary>
    public enum AscendIneligibleReason
    {
        AtMaxTier,
    }

    /// <summary>Result of <see cref="IAscensionSystem.CanAscend"/>.</summary>
    public readonly record struct AscendEligibility(bool Eligible, AscendIneligibleReason? Reason)
    {
        public static AscendEligibility Ok() => new(true, null);
        public static AscendEligibility Blocked(AscendIneligibleReason reason) => new(false, reason);
    }

    /// <summary>Result of a resolved <see cref="IAscensionSystem.TryAscend"/> call.</summary>
    public readonly record struct AscendResult(
        bool Success,
        int PreviousTier,
        int NewTier,
        IReadOnlyList<string> UnlocksRecorded,
        AscendIneligibleReason? FailureReason);

    /// <summary>
    /// Character-wide tier state and the ascend gate for the ascension substrate (gameplay-model
    /// R1). Reads/writes raw <see cref="Components.AscensionComponent"/> fields directly via
    /// <c>EntityService</c> — never <c>IStatSystem</c>/<c>IEffectSystem</c> — because the additive
    /// power baseline this system's own contributor computes is a pure function of tier; going
    /// through the stat pipeline here would recreate the DI cycle
    /// <c>IStatSystem</c> → <c>IEffectSystem</c> → contributors → backing system → <c>IStatSystem</c>
    /// that the progression substrate's DI-cycle rule guards against. Never touches the event bus
    /// (INV-5) — callers publish the result.
    /// </summary>
    public interface IAscensionSystem
    {
        /// <summary>Current tier for <paramref name="entityId"/>. 0 if no component exists (safe default, creates nothing).</summary>
        int GetTier(uint entityId);

        /// <summary>Whether <paramref name="entityId"/> may ascend right now. The real Ascension-Objective gate is deferred; the admin path always reaches this check.</summary>
        AscendEligibility CanAscend(uint entityId);

        /// <summary>
        /// Ascends <paramref name="entityId"/> one tier: creates <see cref="Components.AscensionComponent"/>
        /// lazily if absent, increments <c>Tier</c> (clamped to <c>[0, AscensionConstants.MaxTier]</c>),
        /// and records the new tier's configured unlock ids onto <c>GrantedUnlocks</c> idempotently.
        /// A non-eligible call (e.g. already at max tier) is a no-op returning <see cref="AscendResult.Success"/> false.
        /// </summary>
        AscendResult TryAscend(uint entityId);

        /// <summary>Unlock ids recorded for <paramref name="entityId"/> across every ascend so far. Empty if never ascended.</summary>
        IReadOnlyList<string> GetGrantedUnlocks(uint entityId);
    }
}
