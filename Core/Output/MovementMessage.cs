using Hedron.Core;

namespace Hedron.Core.Output
{
    /// <summary>Outcome of a movement attempt — blocked, departed, or arrived.</summary>
    public sealed record MovementMessage(
        MovementDirectionKind Kind,
        Direction? Direction,
        string ActorName) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
