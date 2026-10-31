using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Commands;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// The verb names and aliases a help topic can collide with (R10), read from
    /// <see cref="IVerbRegistry"/> command <b>metadata</b> only — never executing a command
    /// (the sanctioned domain read, <c>01-layers.md</c>).
    /// </summary>
    public sealed record HelpVerbTokens(IReadOnlySet<string> All, IReadOnlySet<string> AdminOnly)
    {
        public static HelpVerbTokens From(IVerbRegistry registry)
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            var adminOnly = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in registry.AllCommands)
            {
                var privileged = command.RequiredPrivileges.Count > 0;
                foreach (var token in new[] { command.Name }.Concat(command.Aliases))
                {
                    var normalized = token.Trim().ToLowerInvariant();
                    if (normalized.Length == 0)
                        continue;
                    all.Add(normalized);
                    if (privileged)
                        adminOnly.Add(normalized);
                }
            }
            return new HelpVerbTokens(all, adminOnly);
        }
    }
}
