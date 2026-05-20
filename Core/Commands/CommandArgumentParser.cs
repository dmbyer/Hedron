using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Commands
{
    /// <summary>
    /// Default argument parser: single-pass, whitespace + double-quoted tokenization,
    /// enum-prefix matching. <see cref="IArgumentResolver"/> seam is null this slice.
    /// </summary>
    public sealed class CommandArgumentParser : ICommandArgumentParser
    {
        public ParseResult Parse(CommandArgumentSchema schema, string rawTail)
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
                        var coerced = Coerce(token, arg.ClrType);
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

        private static object? Coerce(string token, Type clrType)
        {
            if (clrType == typeof(string)) return token;
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
