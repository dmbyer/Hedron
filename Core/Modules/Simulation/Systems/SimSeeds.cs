namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Deterministic per-run seed derivation (INV-26). Deliberately <b>not</b>
    /// <c>HashCode.Combine</c> (process-randomized per .NET run — would break cross-process
    /// reproducibility) — a SplitMix64-style stable mix so a (scenario, seed) pair reproduces
    /// byte-identically on any machine, any process, forever.
    /// </summary>
    public static class SimSeeds
    {
        public static int DeriveRunSeed(int scenarioSeed, int runIndex)
        {
            unchecked
            {
                var x = (ulong)scenarioSeed * 0x9E3779B97F4A7C15UL + (ulong)runIndex * 0xBF58476D1CE4E5B9UL + 0x2545F4914F6CDD1DUL;
                x ^= x >> 30;
                x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27;
                x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (int)x;
            }
        }
    }
}
