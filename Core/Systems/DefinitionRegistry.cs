using System;
using System.Collections.Generic;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Uniform lookup contract for definition families. Two type parameters let each
    /// family pick the key type that fits its nature: enum for fixed code-owned vocabularies
    /// (Aspect, Score), string for open/persisted/content-authored families (Ability, Effect).
    /// </summary>
    public interface IRegistry<TKey, TDef> where TKey : notnull
    {
        bool TryGet(TKey key, out TDef definition);
        TDef Get(TKey key);
        IReadOnlyCollection<TKey> AllIds { get; }
        IReadOnlyCollection<TDef> All { get; }
    }

    /// <summary>
    /// Instance-based definition store. Rows are loaded once at construction via the subclass
    /// and held in an instance dictionary (reload-shaped: a future Reload(rows) is additive
    /// without touching this contract). The subclass supplies a key selector so each family
    /// points at its own key property without requiring a shared IHasId interface.
    /// </summary>
    public abstract class DefinitionRegistry<TKey, TDef> : IRegistry<TKey, TDef>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TDef> _rows;

        protected DefinitionRegistry(IEnumerable<TDef> rows, Func<TDef, TKey> keySelector)
        {
            _rows = new Dictionary<TKey, TDef>();
            foreach (var row in rows)
                _rows[keySelector(row)] = row;
        }

        public bool TryGet(TKey key, out TDef definition)
            => _rows.TryGetValue(key, out definition!);

        public TDef Get(TKey key)
        {
            if (!_rows.TryGetValue(key, out var def))
                throw new KeyNotFoundException($"No definition for key '{key}' in {GetType().Name}.");
            return def;
        }

        public IReadOnlyCollection<TKey> AllIds => _rows.Keys;
        public IReadOnlyCollection<TDef> All => _rows.Values;
    }
}
