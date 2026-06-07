using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Stats
{
    public enum ScoreRole { Primary, Pool, Derived }

    public sealed record ScoreRegistration(ScoreId ScoreId, ScoreRole Role, ScoreId? GoverningAttribute = null);

    public interface IStatRegistry : IRegistry<ScoreId, ScoreRegistration>
    {
        // Inherits TryGet, Get, AllIds, and All from IRegistry<ScoreId, ScoreRegistration>.
        // All returns IReadOnlyCollection<ScoreRegistration> — consumers that iterated the
        // former IReadOnlyList<ScoreRegistration> via foreach still compile because
        // IReadOnlyCollection<T> also exposes GetEnumerator and Count; random-access
        // indexers were not in use.
    }
}
