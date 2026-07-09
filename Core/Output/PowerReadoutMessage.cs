using Hedron.Core.Systems;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Carries the result of the admin/designer <c>power &lt;target&gt;</c> inspector: a computed
    /// power scalar and its classified <see cref="PowerBand"/> (Tier×Band), plus (for a
    /// content-authored target — item or mob) the authored Tier/Band tags for the
    /// authored-vs-computed comparison. <c>AuthoredTier</c>/<c>AuthoredBand</c> are
    /// <see langword="null"/> for a self target (players carry no authored tag).
    /// </summary>
    public sealed record PowerReadoutMessage(
        string TargetLabel,
        int Power,
        PowerBand Computed,
        int? AuthoredTier,
        int? AuthoredBand) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
