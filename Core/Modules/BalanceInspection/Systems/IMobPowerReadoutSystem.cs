using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Systems
{
    /// <summary>
    /// The per-mob computed-power readout an editor shows beside the authored stat block: the
    /// estimated power, the cell the oracle classifies it into, the target window of the
    /// <em>authored</em> cell, and whether the two have drifted apart beyond tolerance.
    /// </summary>
    /// <param name="Power">Estimated power of the authored template at its authored tier.</param>
    /// <param name="Computed">The (Tier, Band) cell <see cref="Power"/> classifies into.</param>
    /// <param name="AuthoredTier">The template's authored tier tag.</param>
    /// <param name="AuthoredBand">The template's authored band tag; <c>0</c> means unbanded.</param>
    /// <param name="AuthoredTargetRange">
    /// The power window the authored cell targets, or <c>null</c> when the authored cell has no
    /// window: band <c>0</c> (unbanded, the default) or a tier/band outside the standards table.
    /// </param>
    /// <param name="DriftsFromAuthoredCell">
    /// <c>true</c> when the authored cell and the computed cell are further apart than the
    /// standards document's band-drift tolerance. Always <c>false</c> for an unbanded mob —
    /// authored band <c>0</c> is excluded from drift assertion, the same rule
    /// <see cref="IBalanceAuditSystem"/>'s sweep applies.
    /// </param>
    public sealed record MobPowerReadout(
        int Power,
        PowerBand Computed,
        int AuthoredTier,
        int AuthoredBand,
        PowerRange? AuthoredTargetRange,
        bool DriftsFromAuthoredCell);

    /// <summary>
    /// Domain-tier (BalanceInspection) single-template readout — the per-definition counterpart to
    /// <see cref="IBalanceAuditSystem"/>'s corpus sweep. Composes the shared
    /// <c>IMobPowerProjectionSystem</c> seam, the core-tier <c>IPowerBudgetSystem</c> oracle, and the
    /// standards registry's drift tolerance into the one shape an editor renders.
    /// </summary>
    /// <remarks>
    /// It exists because there are now <b>two</b> surfaces showing that readout — the Blazor
    /// <c>MobEditor</c> and the authoring API's power endpoint — and a second hand-composition of
    /// oracle + projection + tolerance in an entry-point surface is exactly the duplication INV-19
    /// exists to stop. Template-sourced only: no live entity, no write, no bus (INV-5/INV-12).
    /// </remarks>
    public interface IMobPowerReadoutSystem
    {
        MobPowerReadout Read(MobTemplate template);
    }
}
