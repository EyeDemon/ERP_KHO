using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController, Authorize, Route("api/receiving-reason-codes")]
[PermissionAuthorize(AppPermissions.ReasonRead)]
public sealed class ReceivingReasonCodesController(IReceivingReasonCatalogQuery query) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? keyword, [FromQuery] bool? active, [FromQuery] ReasonEffectivity? effectivity,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        if (effectivity.HasValue && !Enum.IsDefined(effectivity.Value)) return BadRequest(new { message = "Bộ lọc thời gian hiệu lực không hợp lệ." });
        return Ok(await query.ListAsync(keyword, active, effectivity, page, pageSize, token));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken token) => Ok(await query.DetailAsync(id, token));

    [HttpGet("versions/{code}")]
    public async Task<IActionResult> Versions(string code, CancellationToken token) => Ok(await query.VersionsAsync(code, token));
}
