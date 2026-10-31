using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Thrown by a template deserializer for a fail-fast content <i>shape</i> error (a legacy or
    /// malformed key the operator must convert by hand). Unlike ordinary deserialize failures —
    /// which the loader logs and skips — the loader's staged read collects these into one
    /// <see cref="ContentLoadException"/> that aborts boot or refuses <c>reload</c>.
    /// </summary>
    public sealed class ContentShapeException : Exception
    {
        public ContentShapeException(string message) : base(message) { }
    }
}
