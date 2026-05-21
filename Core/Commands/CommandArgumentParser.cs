using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Default argument parser: single-pass, whitespace + double-quoted tokenization,
    /// enum-prefix matching. For <see cref="CommandArgumentKind.Token"/> string arguments
    /// that declare a non-null <see cref="IArgumentResolver"/>, the resolver is invoked and
    /// prefix matching is applied against the candidate list. No concrete resolver ships until
    /// slice 6; the call-site is present but exercises no live resolver in this slice.
    /// </summary>
    public sealed class CommandArgumentParser : ICommandArgumentParser
    {
        public ParseResult Parse(CommandArgumentSchema schema, string rawTail,
            CommandArgumentResolverContext resolverContext)
        {
            if (!schema.Arguments.Any())
                return new ParseResult.Success(ParsedArguments.Empty);

            var values = new Dictionary<string, object?>();
            var pos = 0;

            SkipWhitespace(rawTail, ref pos);

            foreach (var arg in schema.Arguments)
            {
                switch (arg.Kind)
                {
                    case CommandArgumentKind.Token:
                    {
                        if (pos >= rawTail.Length)
                        {
                            if (arg.Required)
                                return new ParseResult.Failure($"Missing required argument '{arg.Name}'.");
                            break;
                        }

                        var token = ReadToken(rawTail, ref pos);
                        var coerced = Coerce(token, arg.ClrType, arg.Resolver, resolverContext);
                        if (coerced is null)
                            return new ParseResult.Failure(
                                $"'{token}' is not a valid {arg.ClrType.Name} for '{arg.Name}'.");
                        values[arg.Name] = coerced;
                        SkipWhitespace(rawTail, ref pos);
                        break;
                    }

                    case CommandArgumentKind.RestOfLine:
                    {
                        if (pos >= rawTail.Length)
                        {
                            if (arg.Required)
                                return new ParseResult.Failure($"Missing required argument '{arg.Name}'.");
                            break;
                        }
                        values[arg.Name] = rawTail[pos..].TrimEnd();
                        pos = rawTail.Length;
                        break;
                    }

                    case CommandArgumentKind.Quantified:
                        // Deferred — not used in slice 3.
                        break;
                }
            }

            return new ParseResult.Success(new ParsedArguments(values));
        }

        private static string ReadToken(string input, ref int pos)
        {
            if (input[pos] == '"')
            {
                pos++; // skip opening quote
                var start = pos;
                while (pos < input.Length && input[pos] != '"') pos++;
                var token = input[start..pos];
                if (pos < input.Length) pos++; // skip closing quote
                return token;
            }
            else
            {
                var start = pos;
                while (pos < input.Length && !char.IsWhiteSpace(input[pos])) pos++;
                return input[start..pos];
            }
        }

        private static void SkipWhitespace(string input, ref int pos)
        {
            while (pos < input.Length && char.IsWhiteSpace(input[pos])) pos++;
        }

        /// <summary>
        /// Coerces <paramref name="token"/> to <paramref name="clrType"/>.
        /// For <c>string</c> arguments with a non-null <paramref name="resolver"/>, invokes the
        /// resolver to get candidates and applies prefix matching:
        /// <list type="bullet">
        ///   <item>Resolver returns null → pass token through (not applicable).</item>
        ///   <item>Exactly one candidate starts with token → substitute canonical form.</item>
        ///   <item>Zero candidates start with token → pass token through (no match, raw literal).</item>
        ///   <item>Two or more candidates start with token → return null (ambiguous → parse failure).</item>
        /// </list>
        /// </summary>
        private static object? Coerce(string token, Type clrType,
            IArgumentResolver? resolver, CommandArgumentResolverContext resolverContext)
        {
            if (clrType == typeof(string))
            {
                if (resolver is not null)
                {
                    var candidates = resolver.GetCandidates(resolverContext);
                    if (candidates is not null)
                    {
                        var matches = candidates
                            .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        return matches.Count switch
                        {
                            1 => matches[0],            // unique match → canonical form
                            > 1 => null,                // ambiguous → parse failure
                            _ => token,                 // no match → fall through to raw literal
                        };
                    }
                }
                return token;
            }

            if (clrType == typeof(int)) return int.TryParse(token, out var i) ? (object)i : null;
            if (clrType == typeof(uint)) return uint.TryParse(token, out var u) ? (object)u : null;
            if (clrType.IsEnum) return TryParseEnumPrefix(token, clrType);
            return null;
        }

        private static object? TryParseEnumPrefix(string token, Type enumType)
        {
            if (Enum.TryParse(enumType, token, ignoreCase: true, out var exact)) return exact;

            string? matched = null;
            foreach (var name in Enum.GetNames(enumType))
            {
                if (!name.StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;
                if (matched is not null) return null; // ambiguous prefix
                matched = name;
            }
            return matched is null ? null : Enum.Parse(enumType, matched, ignoreCase: true);
        }
    }
}
