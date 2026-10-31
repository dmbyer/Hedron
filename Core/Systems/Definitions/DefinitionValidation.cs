using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Systems.Definitions
{
    /// <summary>
    /// The engine's one "named every violation" validation failure shape for YAML definition
    /// families: <c>"&lt;subject&gt; failed validation:\n  • a\n  • b"</c>.
    /// </summary>
    public static class DefinitionValidation
    {
        public static InvalidOperationException Failed(string subject, IReadOnlyList<string> errors) =>
            new($"{subject} failed validation:\n" + string.Join("\n", errors.Select(e => "  • " + e)));
    }
}
