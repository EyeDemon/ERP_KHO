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
    [HttpGet("governance-summary"),PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> GetGovernanceSummary(CancellationToken ct=default)
    {
        var page=await service.GetAsync(1,200,null,null,null,ct);
        return Ok(new
        {
            totalPartners=page.TotalRecords,
            activeSuppliers=page.Items.Count(x=>x.IsActive&&x.IsSupplier),
            activeCustomers=page.Items.Count(x=>x.IsActive&&x.IsCustomer),
            dualRolePartners=page.Items.Count(x=>x.IsSupplier&&x.IsCustomer),
            missingContactInfoCount=page.Items.Count(x=>string.IsNullOrWhiteSpace(x.Phone)&&string.IsNullOrWhiteSpace(x.Email))
        });
    }
    [HttpGet("carriers-and-sla-contracts"),PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> GetCarriersAndSlaContracts(CancellationToken ct=default)
    {
        var page=await service.GetAsync(1,200,null,null,true,ct);
        var items=page.Items;
        return Ok(new
        {
            activeSuppliersCount=items.Count(x=>x.IsActive&&x.IsSupplier),
            activeCustomersCount=items.Count(x=>x.IsActive&&x.IsCustomer),
            dualRoleCount=items.Count(x=>x.IsActive&&x.IsSupplier&&x.IsCustomer),
            standardInboundSlaHours=24,
            standardOutboundDispatchSlaHours=12,
            targetOnTimeRatePct=98.5m,
            slaContracts=items.Where(x=>x.IsActive).Take(15).Select(x=>new
            {
                partnerId=x.Id,
                code=x.Code,
                name=x.Name,
                partnerRole=(x.IsSupplier&&x.IsCustomer)?"SupplierAndCustomer":x.IsSupplier?"Supplier":"Customer",
                slaTier=(x.IsSupplier&&x.IsCustomer)?"ENTERPRISE_3PL_CARRIER":x.IsSupplier?"SUPPLIER_INBOUND_SLA":"CUSTOMER_FULFILLMENT_SLA",
                targetLeadTimeHours=x.IsSupplier?24:12,
                onTimeCommitmentPct=98.5m
            }).ToArray()
        });
    }
    [HttpPost,PermissionAuthorize(AppPermissions.PartnerCreate)] public async Task<IActionResult>Create(SaveBusinessPartnerDto dto,CancellationToken ct){var x=await service.CreateAsync(dto,ct);return Created($"/api/business-partners/{x.Id}",x);}
    [HttpPut("{id:int}"),PermissionAuthorize(AppPermissions.PartnerUpdate)] public async Task<IActionResult>Update(int id,SaveBusinessPartnerDto dto,CancellationToken ct){await service.UpdateAsync(id,dto,ct);return NoContent();}
    [HttpDelete("{id:int}"),PermissionAuthorize(AppPermissions.PartnerDeactivate)] public async Task<IActionResult>Delete(int id,CancellationToken ct){await service.DeleteAsync(id,ct);return NoContent();}
}
