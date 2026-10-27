using ERP.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Infrastructure;

public static class WmsEndpointOutcomeExtensions
{
    public static IActionResult ToActionResult(this ControllerBase controller, WmsEndpointOutcome outcome)
    {
        return outcome.Kind switch
        {
            WmsEndpointOutcomeKind.Ok => controller.Ok(outcome.Payload),
            WmsEndpointOutcomeKind.OkEmpty => controller.Ok(),
            WmsEndpointOutcomeKind.CreatedAtAction => string.IsNullOrWhiteSpace(outcome.ControllerName)
                ? controller.CreatedAtAction(outcome.ActionName, outcome.RouteValues, outcome.Payload)
                : controller.CreatedAtAction(outcome.ActionName, outcome.ControllerName, outcome.RouteValues, outcome.Payload),
            WmsEndpointOutcomeKind.CreatedObject => controller.StatusCode(201, outcome.Payload),
            WmsEndpointOutcomeKind.NoContent => controller.NoContent(),
            WmsEndpointOutcomeKind.BadRequest => controller.BadRequest(outcome.Payload),
            WmsEndpointOutcomeKind.Unauthorized => outcome.Payload is null
                ? controller.Unauthorized()
                : controller.Unauthorized(outcome.Payload),
            WmsEndpointOutcomeKind.Forbid => controller.Forbid(),
            WmsEndpointOutcomeKind.ForbiddenObject => controller.StatusCode(403, outcome.Payload),
            WmsEndpointOutcomeKind.NotFound => outcome.Payload is null
                ? controller.NotFound()
                : controller.NotFound(outcome.Payload),
            WmsEndpointOutcomeKind.Conflict => controller.Conflict(outcome.Payload),
            WmsEndpointOutcomeKind.UnprocessableEntity => controller.UnprocessableEntity(outcome.Payload),
            WmsEndpointOutcomeKind.StatusCode => outcome.Payload is null
                ? controller.StatusCode(outcome.StatusCode)
                : controller.StatusCode(outcome.StatusCode, outcome.Payload),
            WmsEndpointOutcomeKind.File => string.IsNullOrWhiteSpace(outcome.FileDownloadName)
                ? controller.File(outcome.FileBytes ?? Array.Empty<byte>(), outcome.ContentType ?? "application/octet-stream")
                : controller.File(outcome.FileBytes ?? Array.Empty<byte>(), outcome.ContentType ?? "application/octet-stream", outcome.FileDownloadName),
            _ => controller.StatusCode(outcome.StatusCode, outcome.Payload)
        };
    }

    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, WmsEndpointOutcome outcome)
    {
        return new ActionResult<T>((ActionResult)controller.ToActionResult(outcome));
    }

    public static IActionResult ToActionResult(this WmsEndpointOutcome outcome, ControllerBase controller)
    {
        return controller.ToActionResult(outcome);
    }
}
