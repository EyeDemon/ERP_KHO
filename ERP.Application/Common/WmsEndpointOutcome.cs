namespace ERP.Application.Common;

public enum WmsEndpointOutcomeKind
{
    Ok,
    OkEmpty,
    CreatedAtAction,
    CreatedObject,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbid,
    ForbiddenObject,
    NotFound,
    Conflict,
    UnprocessableEntity,
    StatusCode,
    File
}

public sealed record WmsEndpointOutcome(
    WmsEndpointOutcomeKind Kind,
    int StatusCode = 200,
    object? Payload = null,
    string? ActionName = null,
    string? ControllerName = null,
    object? RouteValues = null,
    byte[]? FileBytes = null,
    string? ContentType = null,
    string? FileDownloadName = null)
{
    public static WmsEndpointOutcome Ok(object? payload) =>
        new(WmsEndpointOutcomeKind.Ok, 200, Payload: payload);

    public static WmsEndpointOutcome OkEmpty() =>
        new(WmsEndpointOutcomeKind.OkEmpty, 200);

    public static WmsEndpointOutcome CreatedAtAction(
        string actionName,
        object? routeValues,
        object? payload,
        string? controllerName = null) =>
        new(WmsEndpointOutcomeKind.CreatedAtAction, 201, Payload: payload, ActionName: actionName, ControllerName: controllerName, RouteValues: routeValues);

    public static WmsEndpointOutcome Created(object? payload) =>
        new(WmsEndpointOutcomeKind.CreatedObject, 201, Payload: payload);

    public static WmsEndpointOutcome NoContent() =>
        new(WmsEndpointOutcomeKind.NoContent, 204);

    public static WmsEndpointOutcome BadRequest(object? payload) =>
        new(WmsEndpointOutcomeKind.BadRequest, 400, Payload: payload);

    public static WmsEndpointOutcome Unauthorized(object? payload = null) =>
        new(WmsEndpointOutcomeKind.Unauthorized, 401, Payload: payload);

    public static WmsEndpointOutcome Forbid() =>
        new(WmsEndpointOutcomeKind.Forbid, 403);

    public static WmsEndpointOutcome ForbiddenObject(object? payload) =>
        new(WmsEndpointOutcomeKind.ForbiddenObject, 403, Payload: payload);

    public static WmsEndpointOutcome NotFound(object? payload = null) =>
        new(WmsEndpointOutcomeKind.NotFound, 404, Payload: payload);

    public static WmsEndpointOutcome Conflict(object? payload) =>
        new(WmsEndpointOutcomeKind.Conflict, 409, Payload: payload);

    public static WmsEndpointOutcome UnprocessableEntity(object? payload) =>
        new(WmsEndpointOutcomeKind.UnprocessableEntity, 422, Payload: payload);

    public static WmsEndpointOutcome Status(int statusCode, object? payload = null) =>
        new(WmsEndpointOutcomeKind.StatusCode, statusCode, Payload: payload);

    public static WmsEndpointOutcome Json(int statusCode, object? payload = null) =>
        Status(statusCode, payload);

    public static WmsEndpointOutcome File(byte[] fileBytes, string contentType, string? fileDownloadName = null) =>
        new(WmsEndpointOutcomeKind.File, 200, FileBytes: fileBytes, ContentType: contentType, FileDownloadName: fileDownloadName);
}
