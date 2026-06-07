using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Hedron.Core.Modules.Aspects
{
    /// <summary>
    /// Immutable normalized aspect composition: AspectId → weight (positive integers summing
    /// to 100 when non-empty, or empty for untyped damage/entities). Serialized by AspectId
    /// name, never ordinal (INV-23).
    /// </summary>
    public sealed class AspectComposition
    {
        public static readonly AspectComposition Empty = new(new Dictionary<AspectId, int>());

        public IReadOnlyDictionary<AspectId, int> Weights { get; }

        public bool IsEmpty => Weights.Count == 0;

        public AspectComposition(IReadOnlyDictionary<AspectId, int> weights)
        {
            Weights = weights;
        }

        public static AspectComposition Single(AspectId id) =>
            new(new Dictionary<AspectId, int> { [id] = 100 });

        /// <summary>Returns true when the composition is empty or sums to 100 with all positive weights.</summary>
        public bool IsValid([NotNullWhen(false)] out string? error)
        {
            if (IsEmpty) { error = null; return true; }

            int sum = 0;
            foreach (var (_, weight) in Weights)
            {
                if (weight <= 0) { error = $"Composition contains a non-positive weight ({weight})."; return false; }
                sum += weight;
            }

            if (sum != 100) { error = $"Composition weights sum to {sum}, expected 100."; return false; }

            error = null;
            return true;
        }

        public override string ToString()
        {
            if (IsEmpty) return "(untyped)";
            var sb = new StringBuilder();
            foreach (var (id, w) in Weights)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{id} {w}%");
            }
            return sb.ToString();
        }
    }
}
