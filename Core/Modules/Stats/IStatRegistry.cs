using System.Collections.Generic;

namespace Hedron.Core.Modules.Stats
{
    public enum ScoreRole { Primary, Pool, Derived }

    public sealed record ScoreRegistration(ScoreId ScoreId, ScoreRole Role, ScoreId? GoverningAttribute = null);

    public interface IStatRegistry
    {
        IReadOnlyList<ScoreRegistration> All { get; }
    }
}
