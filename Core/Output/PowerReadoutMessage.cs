namespace Hedron.Core.Output
{
    /// <summary>
    /// Carries the result of the admin/designer <c>power &lt;target&gt;</c> inspector: a computed
    /// power scalar and its classified tier band, plus (for a content-authored target — item or
    /// mob) the authored tier-band tag for the authored-vs-computed comparison. <c>AuthoredBand</c>
    /// is <see langword="null"/> for a self target (players carry no authored band).
    /// </summary>
    public sealed record PowerReadoutMessage(
        string TargetLabel,
        int Power,
        int Band,
        int? AuthoredBand) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
