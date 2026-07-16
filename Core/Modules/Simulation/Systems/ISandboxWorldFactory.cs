using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Hand-builds one isolated <see cref="SandboxWorld"/> per run — a fresh
    /// <see cref="ECS.EntityService"/> plus the full combat/stats/effects/aspects/abilities/
    /// entity-state/regeneration/progression/ascension system graph, mirroring
    /// <c>Hedron.Tests</c>' harness composition. Never touches the host's live world (INV-12).
    /// </summary>
    public interface ISandboxWorldFactory
    {
        /// <summary>Creates one fresh, isolated sandbox world seeded by <paramref name="random"/>.</summary>
        SandboxWorld Create(IRandom random);
    }
}
