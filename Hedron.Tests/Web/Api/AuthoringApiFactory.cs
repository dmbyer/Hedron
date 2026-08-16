using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Hedron.Tests.Web.Api
{
    /// <summary>
    /// Boots the real <c>Hedron.Web</c> host in memory for the HTTP-integration tier, against a
    /// <strong>per-test temp content directory</strong>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The temp directory is not a convenience — it is required. <c>WebApplicationFactory</c> boots
    /// the real host, so <c>AddContentBootstrapHostedServices</c> runs and every write endpoint
    /// writes YAML to the configured <c>World:ContentDirectory</c>. Without the override a single
    /// <c>POST</c> test would write into the repository's own content tree.
    /// </para>
    /// <para>
    /// <strong>Parallelism posture.</strong> Each fixture owns its own directory and its own host,
    /// so fixtures are independent and xunit's default per-class parallelism is safe — there is no
    /// shared collection and no <c>[Collection]</c> attribute is needed. What is <em>not</em> safe is
    /// sharing one factory across classes: the catalog is a host singleton with an in-memory index,
    /// and two classes writing through one host would interleave. Keep one factory per fixture.
    /// </para>
    /// </remarks>
    public sealed class AuthoringApiFactory : WebApplicationFactory<global::Hedron.Web.Program>
    {
        /// <summary>Matches the host's own converter set, so tests deserialize what clients see.</summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public string ContentDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "hedron-api-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(ContentDirectory);

            builder.UseSetting("World:ContentDirectory", ContentDirectory);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["World:ContentDirectory"] = ContentDirectory,
                    // Nothing in the authoring host touches SQLite, but pin it away from the repo's
                    // database file so a mistake cannot reach it.
                    ["Persistence:DatabasePath"] = ":memory:",
                }));
        }

        /// <summary>A client whose defaults satisfy the loopback/JSON mitigation.</summary>
        public HttpClient CreateApiClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            try { Directory.Delete(ContentDirectory, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>
    /// Guards the guard: if the factory ever stops overriding the content directory, this fails
    /// loudly rather than letting a write test quietly edit the repository's content tree.
    /// </summary>
    public sealed class AuthoringApiFactoryTests
    {
        [Fact]
        public void Factory_serves_a_temp_content_directory_outside_the_repository()
        {
            using var factory = new AuthoringApiFactory();
            using var client = factory.CreateApiClient(); // forces the host to build

            var configured = factory.Services
                .GetService(typeof(Microsoft.Extensions.Options.IOptions<Hedron.Core.Modules.World.WorldOptions>))
                as Microsoft.Extensions.Options.IOptions<Hedron.Core.Modules.World.WorldOptions>;

            Assert.NotNull(configured);
            Assert.Equal(factory.ContentDirectory, configured!.Value.ContentDirectory);
            Assert.StartsWith(Path.GetTempPath(), factory.ContentDirectory);
        }
    }
}
