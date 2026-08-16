using Hedron.Core.Modules.Authoring;
using Hedron.Core.Modules.Authoring.Contracts;
using Hedron.Core.Modules.Authoring.Systems;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Microsoft.AspNetCore.Mvc;

namespace Hedron.Web.Api;

/// <summary>
/// The authoring host's JSON surface — a transport adapter over the same
/// <see cref="IContentDefinitionCatalog"/> the Blazor editor calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> Deliberately narrow: exactly what the client-tier decision gate's bakeoff page
/// calls (see <c>docs/design/client-tier.md</c>) — the mob editor's list/load/create/save/delete,
/// the spawn-room picker's read-only area and room lookups, and the live power readout. Not "all
/// authoring operations". Area and Room are <b>read-only</b> here.
/// </para>
/// <para>
/// <b>These endpoints are adapters, not Initiators (INV-5).</b> They add no authoring rule of their
/// own — validation, id minting and YAML shape all stay in the catalog — and they publish nothing.
/// An apply/reload endpoint is deliberately excluded: it <em>would</em> be an Initiator
/// (<c>IWorldContentLoader</c> publishes <c>ContentReloadedEvent</c>) and carries a different
/// posture. What this tier does own is DTO mapping, which is unavoidable, and which is delegated to
/// the <see cref="IContentDefinitionMapper{TDto}"/> seam rather than done here.
/// </para>
/// <para>
/// <b>Kind dispatch lives in the seam, not here.</b> <see cref="MapWritableKind{TDto}"/> is generic
/// over the DTO and resolves the mapper from DI, so adding a writable kind is a DTO + mapper +
/// registration + one call below — never a <c>switch (kind)</c> in this file, which
/// <c>docs/architecture/08-blazor.md</c> forbids in an entry-point surface.
/// </para>
/// <para>
/// <b>The thin-surface rule extends here.</b> An endpoint parses the request, calls a domain system,
/// and maps the result to a status code — the same discipline a Razor component follows.
/// </para>
/// </remarks>
public static class AuthoringApi
{
    /// <summary>Route prefix for the whole surface.</summary>
    public const string Prefix = "/api";

    public static IEndpointRouteBuilder MapAuthoringApi(this IEndpointRouteBuilder endpoints)
    {
        // One group, so the loopback/origin/content-type mitigation cannot be forgotten on a new
        // endpoint. NOTE: no CORS policy is registered on this host, and none may be — the
        // mitigation's cross-origin protection relies on preflight failing (see LocalOriginFilter).
        var api = endpoints.MapGroup(Prefix)
            .AddEndpointFilter<LocalOriginFilter>()
            .WithGroupName("authoring")
            // Declared on the group because LocalOriginFilter can return these from *any* endpoint,
            // and they carry `application/problem+json` rather than ContentErrorResponse. The
            // consumer hand-writes its client from the published document, so leaving the two
            // statuses it is most likely to hit while wiring up undocumented would be a lying
            // contract. Group-level metadata rather than `.ProducesProblem(…)`, which .NET 8 only
            // extends onto a single RouteHandlerBuilder.
            .WithMetadata(
                new ProducesResponseTypeAttribute(
                    typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json"),
                new ProducesResponseTypeAttribute(
                    typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json"));

        MapKindListing(api, ContentKind.Area, "areas");
        MapKindListing(api, ContentKind.Room, "rooms");
        MapKindListing(api, ContentKind.Mob, "mobs");

        MapWritableKind<MobDefinitionDto>(api, ContentKind.Mob, "mobs");
        MapMobPowerReadout(api);

        return endpoints;
    }

    /// <summary>
    /// The read-only listing every kind exposes. Kind-parameterised rather than duplicated, so the
    /// spawn-room picker's area and room lookups are the same code path the mob list uses.
    /// </summary>
    /// <remarks>
    /// The optional <c>?area=</c> filter narrows by <see cref="ContentSummary.AreaBlueprintId"/> —
    /// the resolved area, which the catalog derives one-hop for rooms and two-hop for mobs and
    /// items. Areas themselves have no parent area, so filtering the area listing always yields
    /// nothing; the parameter is uniform rather than special-cased so one route shape serves every
    /// kind. Note there is deliberately **no** `kind == Room ? catalog.RoomsInArea(area) : …`
    /// branch: `RoomsInArea` computes exactly this filter over exactly this list, so the branch
    /// would be a no-op that nonetheless seeds kind dispatch in an entry-point surface. If a kind
    /// ever earns an indexed lookup, promote it to a catalog method every kind calls — do not
    /// branch here.
    /// </remarks>
    private static void MapKindListing(IEndpointRouteBuilder api, ContentKind kind, string route)
    {
        api.MapGet($"/{route}", (IContentDefinitionCatalog catalog, string? area) =>
                Results.Ok(string.IsNullOrEmpty(area)
                    ? catalog.List(kind)
                    : catalog.List(kind).Where(s => s.AreaBlueprintId == area).ToList()))
            .WithName($"List{kind}Definitions")
            .Produces<IReadOnlyList<ContentSummary>>();
    }

    /// <summary>
    /// Load / create / save / delete for one kind, generic over its transport DTO. This method is
    /// the whole reason the surface can grow a kind without branching: it never names a kind's type.
    /// </summary>
    private static void MapWritableKind<TDto>(IEndpointRouteBuilder api, ContentKind kind, string route)
        where TDto : class, new()
    {
        var readRoute = $"{Prefix}/{route}";

        api.MapGet($"/{route}/{{blueprintId}}", (
                string blueprintId,
                IContentDefinitionCatalog catalog,
                IContentDefinitionMapper<TDto> mapper) =>
            {
                var definition = catalog.Load(kind, blueprintId);
                return definition is null
                    ? ContentResults.NotFound(kind, blueprintId)
                    : Results.Ok(mapper.ToDto(definition));
            })
            .WithName($"Get{kind}Definition")
            .Produces<TDto>()
            .Produces<ContentErrorResponse>(StatusCodes.Status404NotFound);

        api.MapPost($"/{route}", async (
                TDto dto,
                IContentDefinitionCatalog catalog,
                IContentDefinitionMapper<TDto> mapper,
                string? blueprintId,
                CancellationToken ct) =>
            {
                // Id minting stays in the catalog: CreateNew resolves a caller-chosen id or mints an
                // ad-hoc one, exactly as the editor's New form does. CreateAsync then applies the
                // create guard (malformed id, or a collision → refuse, never merge).
                var mintedId = catalog.CreateNew(kind, string.Empty, blueprintId).BlueprintId;
                var definition = mapper.ToDefinition(dto, mintedId);

                var result = await catalog.CreateAsync(definition, ct);
                return ContentResults.FromCreate(result, readRoute);
            })
            .WithName($"Create{kind}Definition")
            .Produces<ContentWriteResponse>(StatusCodes.Status201Created)
            .Produces<ContentErrorResponse>(StatusCodes.Status400BadRequest);

        api.MapPut($"/{route}/{{blueprintId}}", async (
                string blueprintId,
                TDto dto,
                IContentDefinitionCatalog catalog,
                IContentDefinitionMapper<TDto> mapper,
                CancellationToken ct) =>
            {
                if (catalog.Load(kind, blueprintId) is null)
                    return ContentResults.NotFound(kind, blueprintId);

                // The route id wins over any id in the body — a PUT addresses one definition, and
                // renaming is a distinct catalog operation this surface does not expose.
                var result = await catalog.SaveAsync(mapper.ToDefinition(dto, blueprintId), ct);
                return ContentResults.FromWrite(result);
            })
            .WithName($"Save{kind}Definition")
            .Produces<ContentWriteResponse>()
            .Produces<ContentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ContentErrorResponse>(StatusCodes.Status404NotFound);

        api.MapDelete($"/{route}/{{blueprintId}}", async (
                string blueprintId,
                IContentDefinitionCatalog catalog,
                CancellationToken ct) =>
            {
                // DeleteAsync treats an absent file as a no-op success; the HTTP contract is more
                // useful with a 404, so the existence check is here rather than in the catalog.
                if (catalog.Load(kind, blueprintId) is null)
                    return ContentResults.NotFound(kind, blueprintId);

                var result = await catalog.DeleteAsync(kind, blueprintId, ct);
                return ContentResults.FromDelete(result);
            })
            .WithName($"Delete{kind}Definition")
            .Produces<ContentDeleteResponse>()
            .Produces<ContentErrorResponse>(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// The mob editor's live power readout, over a <em>posted</em> definition rather than a saved
    /// one. That is deliberate and is the point of the endpoint: the readout the client-tier
    /// analysis names as a tune-and-observe loop updates as the author types, so it must project
    /// unsaved form state. It writes nothing.
    /// </summary>
    private static void MapMobPowerReadout(IEndpointRouteBuilder api)
    {
        // Under /power rather than /mobs so it cannot shadow a mob blueprint id on the
        // /mobs/{blueprintId} route, and because it is a projection, not a mob resource.
        api.MapPost("/power/mob", (
                MobDefinitionDto dto,
                IContentDefinitionMapper<MobDefinitionDto> mapper,
                IMobPowerReadoutSystem readout) =>
            {
                var template = (MobTemplate)mapper.ToDefinition(dto, dto.BlueprintId).Template;
                return Results.Ok(MobPowerReadoutDto.From(readout.Read(template)));
            })
            .WithName("ReadMobPower")
            .Produces<MobPowerReadoutDto>();
    }
}
