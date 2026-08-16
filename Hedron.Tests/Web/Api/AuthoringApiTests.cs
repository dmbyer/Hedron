using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Contracts;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World.Templates;
using Hedron.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hedron.Tests.Web.Api
{
    /// <summary>
    /// HTTP-integration tier for the authoring JSON surface (authoring-api-surface WP2). Boots the
    /// real host via <see cref="AuthoringApiFactory"/> against a per-test temp content directory and
    /// drives the endpoints over <c>HttpClient</c>.
    ///
    /// Coverage contract: docs/implementation-plans/authoring-api-surface.md Postconditions 3 and 5
    /// — the whole bakeoff page is servable, and it is servable without ever writing to the repo's
    /// content tree.
    /// </summary>
    public sealed class AuthoringApiTests : IDisposable
    {
        private readonly AuthoringApiFactory _factory = new();

        public void Dispose() => _factory.Dispose();

        private HttpClient Client() => _factory.CreateApiClient();

        private T Resolve<T>() where T : notnull => _factory.Services.GetRequiredService<T>();

        private static MobDefinitionDto SampleMob(string name = "Test Goblin") => new()
        {
            Name = name,
            Description = "A small, irritable goblin.",
            Keywords = new List<string> { "goblin", "test" },
            MobType = MobType.Creature,
            Level = 3,
            MaxHp = 120,
            Mind = 8,
            Body = 14,
            Spirit = 9,
            Attunement = 7,
            MaxMana = 40,
            MaxStamina = 55,
            MaxAstra = 12,
            Tier = 1,
            Band = 2,
            XpScale = 1.5,
            CurrencyLoot = new List<CurrencyLootRowDto>
            {
                new() { Currency = CurrencyId.Coin, Min = 5, Max = 25 },
            },
            IsShop = true,
            ShopTillSeed = 500,
            ShopBaseStock = new List<ShopStockRowDto>
            {
                new() { BlueprintId = "item.dagger", Quantity = 2 },
            },
        };

        private async Task<string> CreateMobAsync(HttpClient client, MobDefinitionDto dto)
        {
            var response = await client.PostAsJsonAsync("/api/mobs", dto, AuthoringApiFactory.Json);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ContentWriteResponse>(AuthoringApiFactory.Json);
            Assert.NotNull(body);
            return body!.BlueprintId;
        }

        // ── Mob round-trip ────────────────────────────────────────────────────────

        [Fact]
        public async Task Post_then_get_returns_the_written_definition()
        {
            using var client = Client();
            var sent = SampleMob();

            var blueprintId = await CreateMobAsync(client, sent);

            var read = await client.GetFromJsonAsync<MobDefinitionDto>(
                $"/api/mobs/{blueprintId}", AuthoringApiFactory.Json);

            Assert.NotNull(read);
            Assert.Equal(blueprintId, read!.BlueprintId);
            Assert.Equal(sent.Name, read.Name);
            Assert.Equal(sent.Description, read.Description);
            Assert.Equal(sent.Keywords, read.Keywords);
            Assert.Equal(sent.MobType, read.MobType);
            Assert.Equal(sent.Level, read.Level);
            Assert.Equal(sent.MaxHp, read.MaxHp);
            Assert.Equal(sent.Body, read.Body);
            Assert.Equal(sent.Tier, read.Tier);
            Assert.Equal(sent.Band, read.Band);
            Assert.Equal(sent.XpScale, read.XpScale);
            Assert.True(read.IsShop);
            Assert.Equal(sent.ShopTillSeed, read.ShopTillSeed);

            var loot = Assert.Single(read.CurrencyLoot);
            Assert.Equal(CurrencyId.Coin, loot.Currency);
            Assert.Equal(5, loot.Min);
            Assert.Equal(25, loot.Max);

            var stock = Assert.Single(read.ShopBaseStock);
            Assert.Equal("item.dagger", stock.BlueprintId);
            Assert.Equal(2, stock.Quantity);
        }

        [Fact]
        public async Task Post_writes_yaml_that_the_in_process_catalog_reads_back()
        {
            using var client = Client();
            var blueprintId = await CreateMobAsync(client, SampleMob("Shared Corpus"));

            // The endpoint is a second transport over one catalog, not a second store (INV-19).
            var template = Resolve<IContentDefinitionCatalog>().Load(ContentKind.Mob, blueprintId)?.Template;

            Assert.Equal("Shared Corpus", Assert.IsType<MobTemplate>(template).Name);
        }

        [Fact]
        public async Task Post_honours_a_caller_chosen_blueprint_id()
        {
            using var client = Client();

            var response = await client.PostAsJsonAsync(
                "/api/mobs?blueprintId=mob.deliberate", SampleMob(), AuthoringApiFactory.Json);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ContentWriteResponse>(AuthoringApiFactory.Json);
            Assert.Equal("mob.deliberate", body!.BlueprintId);
            Assert.Equal("/api/mobs/mob.deliberate", response.Headers.Location!.ToString());
        }

        [Fact]
        public async Task Post_refuses_a_colliding_blueprint_id_with_the_catalogs_errors()
        {
            using var client = Client();
            await client.PostAsJsonAsync("/api/mobs?blueprintId=mob.taken", SampleMob(), AuthoringApiFactory.Json);

            var response = await client.PostAsJsonAsync(
                "/api/mobs?blueprintId=mob.taken", SampleMob(), AuthoringApiFactory.Json);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ContentErrorResponse>(AuthoringApiFactory.Json);
            Assert.Contains(body!.Errors, e => e.Contains("already exists"));
        }

        [Fact]
        public async Task Put_overwrites_an_existing_definition()
        {
            using var client = Client();
            var blueprintId = await CreateMobAsync(client, SampleMob("Before"));

            var edited = SampleMob("After");
            edited.MaxHp = 999;

            var response = await client.PutAsJsonAsync($"/api/mobs/{blueprintId}", edited, AuthoringApiFactory.Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var read = await client.GetFromJsonAsync<MobDefinitionDto>(
                $"/api/mobs/{blueprintId}", AuthoringApiFactory.Json);
            Assert.Equal("After", read!.Name);
            Assert.Equal(999, read.MaxHp);
            // The route id wins over any id in the body.
            Assert.Equal(blueprintId, read.BlueprintId);
        }

        [Fact]
        public async Task Delete_removes_the_definition()
        {
            using var client = Client();
            var blueprintId = await CreateMobAsync(client, SampleMob());

            var response = await client.DeleteAsync($"/api/mobs/{blueprintId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var read = await client.GetAsync($"/api/mobs/{blueprintId}");
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("DELETE")]
        public async Task Unknown_blueprint_id_is_a_404(string method)
        {
            using var client = Client();

            var response = await client.SendAsync(
                new HttpRequestMessage(new HttpMethod(method), "/api/mobs/mob.nope"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ── Validation → status code ──────────────────────────────────────────────

        [Fact]
        public async Task A_validation_failure_maps_to_400_and_carries_the_catalogs_errors()
        {
            using var client = Client();

            var invalid = SampleMob();
            invalid.CurrencyLoot = new List<CurrencyLootRowDto>
            {
                new() { Currency = CurrencyId.Coin, Min = 90, Max = 10 },
            };

            var response = await client.PostAsJsonAsync("/api/mobs", invalid, AuthoringApiFactory.Json);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ContentErrorResponse>(AuthoringApiFactory.Json);

            // The endpoint adds no rules of its own — this is IContentValidator's message verbatim.
            Assert.Contains(body!.Errors, e => e.Contains("exceed"));
        }

        [Fact]
        public async Task A_refused_write_leaves_no_file_behind()
        {
            using var client = Client();

            var invalid = SampleMob();
            invalid.CurrencyLoot = new List<CurrencyLootRowDto>
            {
                new() { Currency = CurrencyId.Coin, Min = 90, Max = 10 },
            };

            await client.PostAsJsonAsync("/api/mobs?blueprintId=mob.refused", invalid, AuthoringApiFactory.Json);

            Assert.Null(Resolve<IContentDefinitionCatalog>().Load(ContentKind.Mob, "mob.refused"));
        }

        // ── Cross-kind lookups ────────────────────────────────────────────────────

        [Fact]
        public async Task Area_and_room_listings_match_the_in_process_catalog()
        {
            var catalog = Resolve<IContentDefinitionCatalog>();

            var area = catalog.CreateNew(ContentKind.Area, "Test Area");
            await catalog.SaveAsync(area);

            var roomDef = catalog.CreateNew(ContentKind.Room, "Test Room");
            ((RoomTemplate)roomDef.Template).AreaId = area.BlueprintId;
            await catalog.SaveAsync(roomDef);

            using var client = Client();

            var areas = await client.GetFromJsonAsync<List<ContentSummary>>("/api/areas", AuthoringApiFactory.Json);
            var rooms = await client.GetFromJsonAsync<List<ContentSummary>>("/api/rooms", AuthoringApiFactory.Json);

            Assert.Equal(catalog.List(ContentKind.Area), areas);
            Assert.Equal(catalog.List(ContentKind.Room), rooms);
        }

        [Fact]
        public async Task The_room_listing_narrows_by_area()
        {
            var catalog = Resolve<IContentDefinitionCatalog>();

            var wanted = catalog.CreateNew(ContentKind.Area, "Wanted");
            await catalog.SaveAsync(wanted);
            var other = catalog.CreateNew(ContentKind.Area, "Other");
            await catalog.SaveAsync(other);

            foreach (var (areaId, name) in new[] { (wanted.BlueprintId, "In"), (other.BlueprintId, "Out") })
            {
                var room = catalog.CreateNew(ContentKind.Room, name);
                ((RoomTemplate)room.Template).AreaId = areaId;
                await catalog.SaveAsync(room);
            }

            using var client = Client();
            var rooms = await client.GetFromJsonAsync<List<ContentSummary>>(
                $"/api/rooms?area={Uri.EscapeDataString(wanted.BlueprintId)}", AuthoringApiFactory.Json);

            Assert.Equal(catalog.RoomsInArea(wanted.BlueprintId), rooms);
            Assert.Equal("In", Assert.Single(rooms!).Name);
        }

        [Fact]
        public async Task The_mob_listing_is_served_by_the_same_route_shape()
        {
            using var client = Client();
            await CreateMobAsync(client, SampleMob("Listed"));

            var mobs = await client.GetFromJsonAsync<List<ContentSummary>>("/api/mobs", AuthoringApiFactory.Json);

            Assert.Contains(mobs!, m => m.Name == "Listed");
        }

        // ── Power projection read ─────────────────────────────────────────────────

        [Fact]
        public async Task The_power_readout_matches_the_in_process_readout_system()
        {
            using var client = Client();
            var dto = SampleMob();

            var response = await client.PostAsJsonAsync("/api/power/mob", dto, AuthoringApiFactory.Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var served = await response.Content.ReadFromJsonAsync<MobPowerReadoutDto>(AuthoringApiFactory.Json);

            var template = (MobTemplate)Resolve<IContentDefinitionMapper<MobDefinitionDto>>()
                .ToDefinition(dto, "mob.unsaved").Template;
            var expected = Resolve<IMobPowerReadoutSystem>().Read(template);

            Assert.Equal(expected.Power, served!.Power);
            Assert.Equal(expected.Computed.Tier, served.ComputedTier);
            Assert.Equal(expected.Computed.Band, served.ComputedBand);
            Assert.Equal(expected.AuthoredTargetRange?.MinPower, served.TargetMinPower);
            Assert.Equal(expected.AuthoredTargetRange?.MaxPower, served.TargetMaxPower);
            Assert.Equal(expected.DriftsFromAuthoredCell, served.DriftsFromAuthoredCell);
        }

        [Fact]
        public async Task The_power_readout_projects_unsaved_state_and_writes_nothing()
        {
            using var client = Client();
            var dto = SampleMob();

            await client.PostAsJsonAsync("/api/power/mob", dto, AuthoringApiFactory.Json);

            // The tune-and-observe loop must never persist what the author is still typing.
            Assert.Empty(Resolve<IContentDefinitionCatalog>().List(ContentKind.Mob));
        }

        [Fact]
        public async Task The_power_readout_tracks_an_edit_to_the_posted_definition()
        {
            using var client = Client();

            var weak = SampleMob();
            weak.Body = 5;
            var strong = SampleMob();
            strong.Body = 400;

            var weakReadout = await (await client.PostAsJsonAsync("/api/power/mob", weak, AuthoringApiFactory.Json))
                .Content.ReadFromJsonAsync<MobPowerReadoutDto>(AuthoringApiFactory.Json);
            var strongReadout = await (await client.PostAsJsonAsync("/api/power/mob", strong, AuthoringApiFactory.Json))
                .Content.ReadFromJsonAsync<MobPowerReadoutDto>(AuthoringApiFactory.Json);

            Assert.True(strongReadout!.Power > weakReadout!.Power);
        }

        // ── Auth mitigation (fail-fast; see LocalOriginFilter) ────────────────────

        [Fact]
        public async Task A_non_loopback_host_header_is_rejected()
        {
            using var client = Client();
            client.DefaultRequestHeaders.Host = "content.hedron.example";

            var response = await client.GetAsync("/api/mobs");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task A_cross_origin_request_is_rejected()
        {
            using var client = Client();
            client.DefaultRequestHeaders.Add("Origin", "http://attacker.example");

            var response = await client.GetAsync("/api/mobs");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task A_same_origin_request_is_allowed()
        {
            using var client = Client();
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));

            var response = await client.GetAsync("/api/mobs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("text/plain")]
        [InlineData("application/x-www-form-urlencoded")]
        [InlineData("multipart/form-data")]
        public async Task A_non_json_content_type_is_rejected_on_a_write(string contentType)
        {
            using var client = Client();

            var response = await client.PostAsync(
                "/api/mobs", new StringContent("{}", Encoding.UTF8, contentType));

            // The three types an HTML form can send — the CSRF shape this check exists to block.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        [Fact]
        public async Task A_json_content_type_with_a_charset_parameter_is_accepted()
        {
            using var client = Client();

            var response = await client.PostAsync(
                "/api/power/mob",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public void No_CORS_service_resolves_out_of_the_host()
        {
            using var client = Client(); // forces the host to build

            // The runtime half of the no-CORS guard (its source-scan half is in
            // ArchitectureGuardTests). ICorsService only lands in the container via AddCors, so a
            // resolvable one means a policy arrived — directly or transitively — and the API's
            // cross-origin protection, which needs preflight to fail, is gone.
            Assert.Null(_factory.Services.GetService<Microsoft.AspNetCore.Cors.Infrastructure.ICorsService>());
        }

        [Fact]
        public async Task The_mitigation_covers_every_endpoint_under_the_api_prefix()
        {
            using var client = Client();
            client.DefaultRequestHeaders.Host = "content.hedron.example";

            // Routes come from the routing table, not a hand-maintained list. Filter coverage
            // *within* the group is structurally guaranteed by MapGroup, so the failure worth
            // catching is an /api endpoint mapped beside the group rather than into it — which a
            // hardcoded list cannot see, because the new route simply would not be in it.
            //
            // Driven behaviourally rather than by inspecting metadata: an endpoint filter is
            // compiled into the request delegate and leaves nothing to assert on.
            //
            // Bodied methods get a valid JSON payload. Minimal-API parameter binding runs *before*
            // endpoint filters, so a POST with no body answers 400 without the filter executing at
            // all — which is not a bypass (nothing ran, nothing was written), but it does mean an
            // empty request would test nothing here.
            var routes = _factory.Services
                .GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText?.StartsWith(AuthoringApi.Prefix, StringComparison.Ordinal) == true)
                .Select(e => (
                    Path: PlaceholderFor(e.RoutePattern.RawText!),
                    Method: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET"))
                .ToList();

            Assert.True(
                routes.Count >= 8,
                $"pre-check: expected the authoring API's endpoints to be discoverable; found {routes.Count}.");

            var unguarded = new List<string>();
            foreach (var (path, method) in routes)
            {
                var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                    unguarded.Add($"{method} {path} → {(int)response.StatusCode}");
            }

            Assert.True(
                unguarded.Count == 0,
                "Every endpoint under /api must carry the loopback/origin/content-type filter — map it " +
                "into the group, not beside it. These answered a non-loopback Host:\n" +
                string.Join("\n", unguarded));
        }

        /// <summary>Substitutes a literal for each route parameter, e.g. <c>/api/mobs/{id}</c> → <c>/api/mobs/x</c>.</summary>
        private static string PlaceholderFor(string routePattern) =>
            System.Text.RegularExpressions.Regex.Replace(routePattern, @"\{[^}]+\}", "placeholder");

        [Theory]
        [InlineData("GET", "/api/areas")]
        [InlineData("GET", "/api/rooms")]
        [InlineData("GET", "/api/mobs")]
        [InlineData("GET", "/api/mobs/anything")]
        [InlineData("DELETE", "/api/mobs/anything")]
        public async Task A_non_loopback_host_is_rejected_across_the_surface(string method, string route)
        {
            using var client = Client();
            client.DefaultRequestHeaders.Host = "content.hedron.example";

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
