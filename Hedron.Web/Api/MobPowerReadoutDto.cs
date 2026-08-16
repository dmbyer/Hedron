using Hedron.Core.Modules.BalanceInspection.Systems;

namespace Hedron.Web.Api;

/// <summary>
/// Transport shape for <see cref="MobPowerReadout"/>. Flattened: the core record nests
/// <c>PowerBand</c> and a nullable <c>PowerRange</c>, and a flat readout is markedly cheaper to
/// bind to a live-updating display than a nested optional one.
/// </summary>
public sealed class MobPowerReadoutDto
{
    public int Power { get; set; }
    public int ComputedTier { get; set; }
    public int ComputedBand { get; set; }
    public int AuthoredTier { get; set; }
    public int AuthoredBand { get; set; }

    /// <summary>Target window of the authored cell; both null when the mob is unbanded.</summary>
    public int? TargetMinPower { get; set; }
    public int? TargetMaxPower { get; set; }

    public bool DriftsFromAuthoredCell { get; set; }

    public static MobPowerReadoutDto From(MobPowerReadout readout) => new()
    {
        Power = readout.Power,
        ComputedTier = readout.Computed.Tier,
        ComputedBand = readout.Computed.Band,
        AuthoredTier = readout.AuthoredTier,
        AuthoredBand = readout.AuthoredBand,
        TargetMinPower = readout.AuthoredTargetRange?.MinPower,
        TargetMaxPower = readout.AuthoredTargetRange?.MaxPower,
        DriftsFromAuthoredCell = readout.DriftsFromAuthoredCell,
    };
}
