using Hedron.Server;
using Hedron.Web.Components;

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
/// </remarks>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddEnvironmentVariables("HEDRON_");

        // Shared engine DI (pure) + this host's trimmed hosted-service set (bootstraps only).
        builder.Services.Register(builder.Configuration);
        builder.Services.AddContentBootstrapHostedServices();

        // Blazor Server stack.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

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

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
