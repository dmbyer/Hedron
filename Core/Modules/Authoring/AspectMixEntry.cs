using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// One entry in a <see cref="GenerationProfile.AspectMix"/>: an <see cref="AspectId"/> and its
    /// relative weight. The generator picks each area's elemental affinity by weighted choice over
    /// the mix, so a profile can bias a generated world toward (say) more Fire areas than Ice.
    /// </summary>
    /// <remarks>
    /// Pure-data record (no logic). <see cref="Weight"/> is a relative weight, not a percentage —
    /// it need not sum to 100 across the mix; the generator normalizes by the running total.
    /// </remarks>
    public sealed record AspectMixEntry(AspectId Aspect, int Weight);
}
