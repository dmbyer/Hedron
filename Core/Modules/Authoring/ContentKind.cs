using System;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// The four authorable world-content kinds. Each maps to a serializer kind discriminator,
    /// a content subdirectory, and an ad-hoc blueprint-id prefix — the per-kind facts the
    /// <see cref="Systems.IContentDefinitionCatalog"/> dispatches on.
    /// </summary>
    public enum ContentKind
    {
        Area,
        Room,
        Item,
        Mob,
    }

    public static class ContentKindExtensions
    {
        /// <summary>Serializer kind discriminator (matches <c>WorldContentLoader</c> + the deserializers).</summary>
        public static string KindString(this ContentKind kind) => kind switch
        {
            ContentKind.Area => "area",
            ContentKind.Room => "room",
            ContentKind.Item => "item",
            ContentKind.Mob  => "mob",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>Content subdirectory under the configured content root.</summary>
        public static string Subdirectory(this ContentKind kind) => kind switch
        {
            ContentKind.Area => "areas",
            ContentKind.Room => "rooms",
            ContentKind.Item => "items",
            ContentKind.Mob  => "mobs",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>Prefix for ad-hoc blueprint ids created via the editor (mirrors the builders' <c>*.adhoc.*</c>).</summary>
        public static string AdhocPrefix(this ContentKind kind) => kind switch
        {
            ContentKind.Area => "area.adhoc.",
            ContentKind.Room => "room.adhoc.",
            ContentKind.Item => "item.adhoc.",
            ContentKind.Mob  => "mob.adhoc.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
