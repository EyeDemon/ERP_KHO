using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/users/{userId:int}/security")]
[Authorize(Roles = AppRoles.Admin)]
public sealed class AccountSecurityController(IAccountAdminService service, IUserSessionService sessionService) : ControllerBase
{
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock(int userId, CancellationToken cancellationToken)
    {
        await service.UnlockAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPost("revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(int userId, CancellationToken cancellationToken)
    {
        await sessionService.RevokeUserSessionsAsAdminAsync(userId, cancellationToken);
        return NoContent();
    }
}
