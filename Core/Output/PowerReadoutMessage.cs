using Hedron.Core.Systems;

namespace Hedron.Core.Output
{
    /// <summary>
    /// Carries the result of the admin/designer <c>power &lt;target&gt;</c> inspector: a computed
    /// power scalar and its classified <see cref="PowerBand"/> (Tier×Band), plus (for a
    /// content-authored target — item or mob) the authored Tier/Band tags for the
    /// authored-vs-computed comparison. <c>AuthoredTier</c>/<c>AuthoredBand</c> are
    /// <see langword="null"/> for a self target (players carry no authored tag).
    ///
    /// <para>
    /// <c>RelationRatio</c>/<c>RelationLabel</c> carry the mob target's relation to the invoker
    /// (target ÷ invoker effective power, and the lower-case label <c>consider</c> would show).
    /// Both are <see langword="null"/> for self and item targets (no relation line). A non-null
    /// ratio with a null label is an unreadable relation. Primitives, so <c>Core/Output</c>
    /// imports no module type.
    /// </para>
    /// </summary>
    public sealed record PowerReadoutMessage(
        string TargetLabel,
        int Power,
        PowerBand Computed,
        int? AuthoredTier,
        int? AuthoredBand,
        double? RelationRatio = null,
        string? RelationLabel = null) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
