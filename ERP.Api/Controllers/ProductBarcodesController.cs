using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize(Roles = AppRoles.AllRoles)]
[ApiController]
[Route("api/products/{productId:int}/barcodes")]
public sealed class ProductBarcodesController(IProductCatalogService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(int productId, CancellationToken ct) => Ok(await service.GetBarcodesAsync(productId, ct));

    [HttpPost, Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Create(int productId, CreateProductBarcodeDto dto, CancellationToken ct)
    {
        var result = await service.AddBarcodeAsync(productId, dto, ct);
        return Created($"/api/products/{productId}/barcodes/{result.Id}", result);
    }

    [HttpDelete("{barcodeId:int}"), Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Delete(int productId, int barcodeId, CancellationToken ct)
    { await service.DeleteBarcodeAsync(productId, barcodeId, ct); return NoContent(); }
}
