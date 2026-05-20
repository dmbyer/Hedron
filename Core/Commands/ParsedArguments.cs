using System;
using System.Collections.Generic;

namespace Hedron.Core.Commands
{
    /// <summary>Typed argument bag produced by <see cref="ICommandArgumentParser"/>.</summary>
    public sealed class ParsedArguments
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        internal ParsedArguments(IReadOnlyDictionary<string, object?> values)
            => _values = values;

        public static readonly ParsedArguments Empty =
            new(new Dictionary<string, object?>());

        public T Get<T>(string name)
        {
            if (!_values.TryGetValue(name, out var value))
                throw new KeyNotFoundException($"No argument '{name}' in parsed context.");
            return (T)value!;
        }

        public bool TryGet<T>(string name, out T value)
        {
            if (_values.TryGetValue(name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default!;
            return false;
        }

        public bool Has(string name) => _values.ContainsKey(name);
    }
}
