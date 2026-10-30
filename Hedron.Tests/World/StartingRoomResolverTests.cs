using Hedron.Core;
using Hedron.Core.Modules.World.Systems;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// System-unit tests for <see cref="StartingRoomResolver"/> — the pure manifest → config →
    /// void → ordinal-least-room chain behind <c>WorldConfiguration.StartingRoom</c>.
    /// </summary>
    public sealed class StartingRoomResolverTests
    {
        [Fact]
        public void Declared_and_present_resolves_from_manifest()
        {
            var d = StartingRoomResolver.Resolve("room.hub", "room.cfg", new[] { "room.hub", "room.cfg" });

            Assert.Equal("room.hub", d.BlueprintId);
            Assert.Equal(StartingRoomSource.Manifest, d.Source);
            Assert.Empty(d.Warnings);
        }

        [Fact]
        public void Declared_but_missing_falls_to_configuration_with_warning()
        {
            var d = StartingRoomResolver.Resolve("room.gone", "room.cfg", new[] { "room.cfg" });

            Assert.Equal("room.cfg", d.BlueprintId);
            Assert.Equal(StartingRoomSource.Configuration, d.Source);
            Assert.Single(d.Warnings);
        }

        [Fact]
        public void No_manifest_declaration_resolves_from_configuration_without_warning()
        {
            var d = StartingRoomResolver.Resolve(null, "room.cfg", new[] { "room.cfg", "room.void" });

            Assert.Equal("room.cfg", d.BlueprintId);
            Assert.Equal(StartingRoomSource.Configuration, d.Source);
            Assert.Empty(d.Warnings);
        }

        [Fact]
        public void Configuration_missing_falls_to_void()
        {
            var d = StartingRoomResolver.Resolve(null, "room.cfg", new[] { "room.a", "room.void" });

            Assert.Equal("room.void", d.BlueprintId);
            Assert.Equal(StartingRoomSource.VoidFallback, d.Source);
            Assert.NotEmpty(d.Warnings);
        }

        [Fact]
        public void No_void_picks_the_ordinal_least_room_regardless_of_input_order()
        {
            var forward = StartingRoomResolver.Resolve(null, "room.cfg", new[] { "room.b", "room.a", "room.c" });
            var reverse = StartingRoomResolver.Resolve(null, "room.cfg", new[] { "room.c", "room.a", "room.b" });

            Assert.Equal("room.a", forward.BlueprintId);
            Assert.Equal("room.a", reverse.BlueprintId);
            Assert.Equal(StartingRoomSource.AnyRoomFallback, forward.Source);
        }

        [Fact]
        public void Empty_room_set_is_unresolved()
        {
            var d = StartingRoomResolver.Resolve("room.hub", "room.cfg", System.Array.Empty<string>());

            Assert.Null(d.BlueprintId);
            Assert.Equal(StartingRoomSource.Unresolved, d.Source);
            Assert.NotEmpty(d.Warnings);
        }

        [Fact]
        public void Matching_is_case_insensitive_and_returns_the_canonical_id()
        {
            var d = StartingRoomResolver.Resolve("ROOM.Hub", null, new[] { "room.hub" });

            Assert.Equal("room.hub", d.BlueprintId);
            Assert.Equal(StartingRoomSource.Manifest, d.Source);
        }
    }
}
