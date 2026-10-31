using System;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// The outcome of one YAML read. <see cref="YamlDocumentFile{TDto}.Read"/> and
    /// <see cref="YamlDefinitionDirectory{TDto}.Read"/> never throw: a missing file is
    /// <c>Exists = false</c>, and an unreadable or unparseable one carries <see cref="Error"/> plus the
    /// original exception in <see cref="Fault"/>, so a caller whose public contract throws can rethrow
    /// it unchanged.
    /// </summary>
    /// <param name="Dto">The deserialized document; <c>null</c> when absent, empty, or faulted.</param>
    public sealed record YamlReadResult<TDto>(TDto? Dto, bool Exists, string? Error, Exception? Fault = null)
        where TDto : class;
}
