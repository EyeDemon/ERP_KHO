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

    public static bool HasActionPermission(this ControllerBase controller, ERP.Application.Interfaces.ICurrentUser? currentUser, params string[] permissions)
    {
        if (permissions is not { Length: > 0 })
        {
            return true;
        }

        currentUser ??= controller.HttpContext?.RequestServices?.GetService<ERP.Application.Interfaces.ICurrentUser>();

        if (currentUser is ERP.Api.Authorization.HttpCurrentUser)
        {
            return permissions.Any(currentUser.HasPermission);
        }

        if (currentUser is { IsAuthenticated: true })
        {
            if (permissions.Any(currentUser.HasPermission) || currentUser.IsGlobalAdmin)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(currentUser.Role))
            {
                var rolePerms = ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(currentUser.Role);
                return permissions.Any(req => rolePerms.Contains(req, StringComparer.OrdinalIgnoreCase));
            }

            if (controller.User?.Identity?.IsAuthenticated != true)
            {
                return false;
            }
        }

        var user = controller.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var explicitPermissions = user.Claims
                .Where(c => string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .ToList();
            if (explicitPermissions.Count > 0)
            {
                return permissions.Any(req => explicitPermissions.Any(p => string.Equals(p, req, StringComparison.OrdinalIgnoreCase)));
            }

            var roleClaims = user.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            if (roleClaims.Count > 0)
            {
                return roleClaims.Any(role =>
                {
                    var rolePerms = ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(role);
                    return permissions.Any(req => rolePerms.Contains(req, StringComparer.OrdinalIgnoreCase));
                });
            }
        }

        if (controller.HttpContext is null &&
            (currentUser is null ||
             currentUser.IsGlobalAdmin ||
             (!currentUser.IsAuthenticated && currentUser.UserId == 0 && string.IsNullOrWhiteSpace(currentUser.Role))))
        {
            return true;
        }

        return false;
    }
}
