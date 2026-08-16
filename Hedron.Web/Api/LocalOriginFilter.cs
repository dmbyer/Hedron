using System.Net;

namespace Hedron.Web.Api;

/// <summary>
/// The authoring API's in-slice security mitigation, applied to every endpoint in the <c>/api</c>
/// group. It is <b>not</b> authentication — that is deferred (see
/// <c>docs/roadmap/backlog.md</c>) — but it closes the gap between "a loopback-bound Blazor page"
/// and "a loopback-bound unauthenticated write endpoint", which are not the same risk.
/// </summary>
/// <remarks>
/// <para>
/// The Blazor editor is covered by <c>UseAntiforgery()</c> on a circuit-bound surface. A minimal-API
/// write endpoint is not: antiforgery does not apply, it is reachable by any local process, and it
/// is reachable cross-origin from any page in the author's browser (localhost CSRF, DNS rebinding).
/// Loopback is a <em>weaker</em> control here than it is for a Blazor page, so three cheap checks
/// stand in:
/// </para>
/// <list type="number">
///   <item><b>Loopback <c>Host</c></b> — rejects a request that arrived under a non-loopback name,
///     which is what a DNS-rebinding attempt looks like from inside the process.</item>
///   <item><b>Same-origin <c>Origin</c></b> — rejects a browser request initiated by another
///     origin.</item>
///   <item><b>JSON <c>Content-Type</c> on bodied methods</b> — an HTML form can only send
///     <c>application/x-www-form-urlencoded</c>, <c>multipart/form-data</c>, or
///     <c>text/plain</c>, so requiring JSON blocks the form-post CSRF shape outright. A
///     cross-origin <c>fetch</c> that <em>does</em> send JSON becomes a preflighted request.</item>
/// </list>
/// <para>
/// <b>Check 3 depends on no CORS policy being registered on this host</b> — that is what makes the
/// preflight fail. Registering one (an <c>AllowAnyOrigin</c> in particular) would silently undo the
/// mitigation, so it is forbidden here and held by an architecture-guard test.
/// </para>
/// <para>
/// Every branch is fail-fast validation, so each has a test row (INV-25) rather than riding along
/// untested.
/// </para>
/// <para>
/// <b>Ordering note.</b> Minimal-API parameter binding runs <em>before</em> endpoint filters, so a
/// request with a missing or malformed body answers <c>400</c> without this filter executing at all.
/// That is not a bypass — binding failed, so no handler ran and nothing was written — but it does
/// mean a probe sent with an empty body exercises none of the checks below.
/// </para>
/// </remarks>
public sealed class LocalOriginFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!IsLoopbackHost(request.Host.Host))
        {
            return Results.Problem(
                title: "Non-loopback host",
                detail: $"The authoring API serves loopback requests only; '{request.Host.Host}' is not a loopback address.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) &&
            !string.Equals(origin, $"{request.Scheme}://{request.Host.Value}", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Cross-origin request",
                detail: $"Origin '{origin}' does not match this host.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (HttpMethods.IsPost(request.Method) ||
            HttpMethods.IsPut(request.Method) ||
            HttpMethods.IsPatch(request.Method))
        {
            if (!IsJson(request.ContentType))
            {
                return Results.Problem(
                    title: "Unsupported content type",
                    detail: "The authoring API accepts 'application/json' request bodies only.",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }
        }

        return await next(context);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // Kestrel strips the brackets from an IPv6 authority, but a proxy may not.
        var candidate = host.Trim('[', ']');
        return IPAddress.TryParse(candidate, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool IsJson(string? contentType) =>
        !string.IsNullOrEmpty(contentType) &&
        contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);
}
