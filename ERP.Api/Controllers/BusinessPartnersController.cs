using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController,Route("api/business-partners"),Authorize]
public sealed class BusinessPartnersController(IBusinessPartnerService service):ControllerBase
{
    [HttpGet]
        [PermissionAuthorize(AppPermissions.PartnerRead)] public async Task<IActionResult> Get(int page=1,int pageSize=20,string? search=null,string? role=null,bool? active=null,CancellationToken ct=default)=>Ok(await service.GetAsync(page,pageSize,search,role,active,ct));
    [HttpPost,PermissionAuthorize(AppPermissions.PartnerCreate)] public async Task<IActionResult>Create(SaveBusinessPartnerDto dto,CancellationToken ct){var x=await service.CreateAsync(dto,ct);return Created($"/api/business-partners/{x.Id}",x);}
    [HttpPut("{id:int}"),PermissionAuthorize(AppPermissions.PartnerUpdate)] public async Task<IActionResult>Update(int id,SaveBusinessPartnerDto dto,CancellationToken ct){await service.UpdateAsync(id,dto,ct);return NoContent();}
    [HttpDelete("{id:int}"),PermissionAuthorize(AppPermissions.PartnerDeactivate)] public async Task<IActionResult>Delete(int id,CancellationToken ct){await service.DeleteAsync(id,ct);return NoContent();}
}
