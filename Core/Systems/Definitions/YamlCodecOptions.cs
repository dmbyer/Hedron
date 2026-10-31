namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// Per-family YAML codec settings. CamelCase naming and ignoring unmatched properties are always
    /// on — every family uses them; only what differs between families is a knob here.
    /// </summary>
    /// <param name="OmitNulls">Serialize with <c>DefaultValuesHandling.OmitNull</c> (the world manifest does).</param>
    public sealed record YamlCodecOptions(bool OmitNulls = false)
    {
        public static YamlCodecOptions Default { get; } = new();
    }
}
