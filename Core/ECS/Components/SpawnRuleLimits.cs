namespace Hedron.Core.ECS.Components
{
    /// <summary>Bounds and defaults for <see cref="SpawnRule"/>.</summary>
    public static class SpawnRuleLimits
    {
        /// <summary>Respawn delay used when a rule omits one, and by <c>mkmob</c>/<c>mkitem</c>.</summary>
        public const int DefaultRespawnDelaySeconds = 300;

        /// <summary>Upper bound on <see cref="SpawnRule.Count"/> (lower bound is 1).</summary>
        public const int MaxCountPerRule = 20;

        /// <summary>Maximum length of a rule id.</summary>
        public const int MaxIdLength = 64;

        /// <summary>Room reserved at the end of a derived id for a <c>-N</c> mint suffix (up to <c>-999</c>).</summary>
        public const int MaxSuffixLength = 4;
    }
}
