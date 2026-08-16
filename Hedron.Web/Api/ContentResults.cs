using Hedron.Core.Modules.Authoring;

namespace Hedron.Web.Api;

/// <summary>Body returned for any refused or failed authoring operation.</summary>
public sealed class ContentErrorResponse
{
    public string BlueprintId { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

/// <summary>Body returned for a successful write; carries the catalog's non-blocking warnings.</summary>
public sealed class ContentWriteResponse
{
    public string BlueprintId { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

/// <summary>One cascade edit a delete applied to a referring definition.</summary>
public sealed class CascadeEditResponse
{
    public string ReferrerKind { get; set; } = string.Empty;
    public string ReferrerBlueprintId { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
}

/// <summary>Body returned for a successful delete.</summary>
public sealed class ContentDeleteResponse
{
    public string BlueprintId { get; set; } = string.Empty;
    public List<CascadeEditResponse> CascadeEdits { get; set; } = new();
}

/// <summary>
/// The one <c>ContentWriteResult</c> → status-code convention for the authoring API, established
/// here so a second endpoint kind inherits it rather than re-hand-rolling it (INV-19).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Refused</b> (validation failed, id collision, malformed id) → <c>400</c> with the
///     catalog's errors. These are author mistakes, not server faults.</item>
///   <item><b>Written</b> → <c>200</c> (<c>201</c> on create) with the catalog's warnings. Warnings
///     never change the status code: the catalog's cross-reference policy is warn-but-allow
///     (INV-19), and collapsing that into a failure status would misreport a written file.</item>
///   <item><b>No such definition</b> → <c>404</c>.</item>
/// </list>
/// </remarks>
public static class ContentResults
{
    public static IResult NotFound(ContentKind kind, string blueprintId) =>
        Results.NotFound(new ContentErrorResponse
        {
            BlueprintId = blueprintId,
            Errors = { $"No {kind} definition with id '{blueprintId}' was found." },
        });

    /// <summary>Maps a write result to <c>200</c>/<c>400</c>.</summary>
    public static IResult FromWrite(ContentWriteResult result) =>
        result.Success
            ? Results.Ok(Written(result))
            : Results.BadRequest(Failed(result));

    /// <summary>
    /// Maps a create result to <c>201</c>/<c>400</c>, with a <c>Location</c> pointing at the new
    /// definition's read route.
    /// </summary>
    public static IResult FromCreate(ContentWriteResult result, string readRoute) =>
        result.Success
            ? Results.Created($"{readRoute}/{Uri.EscapeDataString(result.BlueprintId)}", Written(result))
            : Results.BadRequest(Failed(result));

    public static IResult FromDelete(ContentDeleteResult result) =>
        Results.Ok(new ContentDeleteResponse
        {
            BlueprintId = result.DeletedBlueprintId,
            CascadeEdits = result.CascadeEdits
                .Select(e => new CascadeEditResponse
                {
                    ReferrerKind = e.ReferrerKind.ToString(),
                    ReferrerBlueprintId = e.ReferrerBlueprintId,
                    FieldLabel = e.FieldLabel,
                })
                .ToList(),
        });

    private static ContentWriteResponse Written(ContentWriteResult result) =>
        new() { BlueprintId = result.BlueprintId, Warnings = result.Warnings.ToList() };

    private static ContentErrorResponse Failed(ContentWriteResult result) =>
        new() { BlueprintId = result.BlueprintId, Errors = result.Errors.ToList() };
}
