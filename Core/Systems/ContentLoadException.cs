using System;
using System.Collections.Generic;
using System.Text;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Aggregates every <see cref="ContentShapeException"/> found by one staged corpus read.
    /// Each entry of <see cref="Errors"/> is prefixed <c>&lt;kind&gt;/&lt;file&gt;: </c>;
    /// <see cref="Exception.Message"/> is the full multi-line report (the same format as
    /// <c>RegistryValidationBootstrap</c>).
    /// </summary>
    public sealed class ContentLoadException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ContentLoadException(IReadOnlyList<string> errors)
            : base(BuildMessage(errors))
        {
            Errors = errors;
        }

        private static string BuildMessage(IReadOnlyList<string> errors)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Content load failed — {errors.Count} shape error(s); convert these files and retry:");
            foreach (var e in errors)
                builder.AppendLine($"  • {e}");
            return builder.ToString();
        }
    }
}
