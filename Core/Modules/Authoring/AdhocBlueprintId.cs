using System;
using System.Text;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// Generates ad-hoc blueprint ids of the form <c>{prefix}{8-char-base36}</c> — the same shape
    /// the <c>mk*</c> builder systems mint (<c>area.adhoc.*</c>, <c>room.adhoc.*</c>, …).
    /// </summary>
    /// <remarks>
    /// Shared home for the id-generation logic the builders currently each duplicate; the builders
    /// can migrate onto this in a follow-up. Used by the editor's <c>CreateNew</c>, which mints an
    /// id without creating a live entity (unlike the builders).
    /// </remarks>
    public static class AdhocBlueprintId
    {
        /// <summary>
        /// Returns a fresh id with <paramref name="prefix"/> that <paramref name="exists"/> reports
        /// as not taken. Falls back to a longer guid suffix if the short space collides repeatedly.
        /// </summary>
        public static string Generate(string prefix, Func<string, bool> exists)
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = prefix + ToBase36(Guid.NewGuid())[..8];
                if (!exists(id))
                    return id;
            }
            return prefix + Guid.NewGuid().ToString("N")[..16];
        }

        private static string ToBase36(Guid guid)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var bytes = guid.ToByteArray();
            var value = Math.Abs(BitConverter.ToInt64(bytes, 0));
            if (value == 0) return "0";
            var result = new StringBuilder();
            while (value > 0)
            {
                result.Insert(0, chars[(int)(value % 36)]);
                value /= 36;
            }
            return result.ToString();
        }
    }
}
