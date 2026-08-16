using System.Text.Json.Serialization;
using Hedron.Server;
using Hedron.Web.Api;
using Hedron.Web.Components;
using Hedron.Web.Services;
using Microsoft.OpenApi.Models;

namespace Hedron.Web;

/// <summary>
/// Composition root for the offline content-authoring web host.
/// </summary>
/// <remarks>
/// <para>
/// One of the engine's <b>two hosts</b> (the other is the telnet <c>Server</c>). Both boot the
/// shared engine via <see cref="CompositionRoot.Register"/> (pure DI); hosted services are composed
/// per-host — this host uses <see cref="CompositionRoot.AddContentBootstrapHostedServices"/>
/// (content load + registry validation only — no telnet listener, no heartbeat, no persistence
/// flush). Authoring is off the tick and never touches SQLite.
/// </para>
/// <para>
/// <b>Security posture (v1): loopback-only.</b> Kestrel binds <c>http://127.0.0.1:&lt;port&gt;</c>
/// from <c>Web:BindUrl</c>. There is no authn/z. Real authentication/authorization is a hard
/// prerequisite before any non-local bind (a recorded backlog item); do not change the bind to a
/// non-loopback address until it lands.
/// </para>
/// <para>
/// <b>Two surfaces.</b> The Blazor editor (circuit-bound, covered by <c>UseAntiforgery</c>) and the
/// authoring JSON API (<see cref="Api.AuthoringApi"/>). The API is not covered by antiforgery, so it
/// carries its own loopback/origin/content-type filter — see <see cref="Api.LocalOriginFilter"/>,
/// which also documents why <b>no CORS policy may be registered on this host</b>.
/// </para>
/// </remarks>
public class Program
{
    // Not a static class: WebApplicationFactory<Program> needs the entry point as a type argument,
    // and C# forbids a static class there. Never instantiated.
    private Program() { }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddEnvironmentVariables("HEDRON_");

        // Shared engine DI (pure) + this host's trimmed hosted-service set (bootstraps only).
        builder.Services.Register(builder.Configuration);
        builder.Services.AddContentBootstrapHostedServices();

        // Web-only background-job registry (sim-3) — not part of the shared engine composition
        // root since it is a Hedron.Web-specific UI concern (no bus events, no hosted service).
        builder.Services.AddSingleton<SimulationRunService>();
        builder.Services.AddSingleton<ContentIntegritySweepService>();

        // Blazor Server stack.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // ── Authoring JSON surface ───────────────────────────────────────────────
        //
        // NOTE: no CORS policy is registered on this host, and none may be. The API's
        // cross-origin protection depends on a cross-origin JSON fetch failing its preflight,
        // which only holds while no policy exists (see LocalOriginFilter). A guard test pins it.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            // Enum names, not ordinals — the OpenAPI document is hand-transcribed by its consumer,
            // and a renumbered enum must not silently change the meaning of a stored value.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("authoring", new OpenApiInfo
            {
                Title = "Hedron authoring API",
                Version = "v1",
                Description =
                    "Loopback-only, unauthenticated JSON surface over the content-definition " +
                    "catalog. Scoped to the operations the client-tier bakeoff page calls.",
            });
        });

        // Loopback-only bind (v1 auth posture). Any non-local bind gates on real authn/z (backlog).
        var bindUrl = builder.Configuration["Web:BindUrl"] ?? "http://127.0.0.1:5050";
        builder.WebHost.UseUrls(bindUrl);

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapAuthoringApi();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
