using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize, PermissionAuthorize(AppPermissions.ProductRead)]
[ApiController]
[Route("api/product-barcodes")]
public sealed class ProductBarcodeLookupController(IProductCatalogService service) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string value, CancellationToken ct) => Ok(await service.LookupBarcodeAsync(value, ct));
}
