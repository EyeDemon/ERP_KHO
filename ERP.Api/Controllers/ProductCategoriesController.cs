using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize(Roles = AppRoles.AllRoles)]
[ApiController]
[Route("api/product-categories")]
public sealed class ProductCategoriesController(IProductCatalogService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await service.GetCategoriesAsync(ct));

    [HttpPost, Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Create(CreateProductCategoryDto dto, CancellationToken ct)
    {
        var result = await service.CreateCategoryAsync(dto, ct);
        return Created($"/api/product-categories/{result.Id}", result);
    }

    [HttpPut("{id:int}"), Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Update(int id, UpdateProductCategoryDto dto, CancellationToken ct)
    { await service.UpdateCategoryAsync(id, dto, ct); return NoContent(); }

    [HttpDelete("{id:int}"), Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    { await service.DeleteCategoryAsync(id, ct); return NoContent(); }
}
