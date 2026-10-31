using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring.Contracts;
using Hedron.Core.ECS.Components;
using Xunit;

namespace Hedron.Tests.Web.Api
{
    /// <summary>
    /// HTTP-integration tier — the strict mob JSON shape (spawn-authoring T19). Placement lives only
    /// in room spawn rules, so the mob DTO no longer carries <c>spawnRoomBlueprintId</c>, and a body
    /// that still sends it (or any unmapped member) is rejected with 400 and writes nothing.
    /// </summary>
    public sealed class AuthoringApiMobShapeTests : IDisposable
    {
        private readonly AuthoringApiFactory _factory = new();

        public void Dispose() => _factory.Dispose();

        private static MobDefinitionDto Mob() => new() { Name = "Shape Goblin", MobType = MobType.Creature, Level = 2, MaxHp = 40 };

        /// <summary>The DTO's JSON with a stale <c>spawnRoomBlueprintId</c> member added.</summary>
        private static StringContent WithLegacyField(MobDefinitionDto dto)
        {
            var node = JsonSerializer.SerializeToNode(dto, AuthoringApiFactory.Json)!.AsObject();
            node["spawnRoomBlueprintId"] = "room.legacy";
            return new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        }

        private string MobsDir => Path.Combine(_factory.ContentDirectory, "mobs");

        private int MobFileCount() => Directory.Exists(MobsDir) ? Directory.GetFiles(MobsDir).Length : 0;

        [Fact]
        public async Task Post_with_spawnRoomBlueprintId_is_a_400_and_writes_no_file()
        {
            var client = _factory.CreateApiClient();

            var response = await client.PostAsync("/api/mobs?blueprintId=mob.stale", WithLegacyField(Mob()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, MobFileCount());
        }

        [Fact]
        public async Task Put_with_spawnRoomBlueprintId_is_a_400_and_leaves_the_file_unchanged()
        {
            var client = _factory.CreateApiClient();
            var created = await client.PostAsJsonAsync("/api/mobs?blueprintId=mob.kept", Mob(), AuthoringApiFactory.Json);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var path = Path.Combine(MobsDir, "mob.kept.yaml");
            var before = await File.ReadAllTextAsync(path);

            var response = await client.PutAsync("/api/mobs/mob.kept", WithLegacyField(Mob()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task Get_returns_no_spawnRoomBlueprintId_property()
        {
            var client = _factory.CreateApiClient();
            await client.PostAsJsonAsync("/api/mobs?blueprintId=mob.read", Mob(), AuthoringApiFactory.Json);

            var json = JsonNode.Parse(await client.GetStringAsync("/api/mobs/mob.read"))!.AsObject();

            Assert.DoesNotContain(json, p => p.Key.Equals("spawnRoomBlueprintId", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Shape Goblin", json["name"]!.GetValue<string>());
        }

        [Fact]
        public async Task Power_readout_with_spawnRoomBlueprintId_is_a_400()
        {
            var client = _factory.CreateApiClient();

            var response = await client.PostAsync("/api/power/mob", WithLegacyField(Mob()));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
