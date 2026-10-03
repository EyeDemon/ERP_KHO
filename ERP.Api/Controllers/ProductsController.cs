using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            var products = await _productService.GetAllProductsAsync(cancellationToken);
            return Ok(products);
        }

        [HttpGet("paged")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetPaged(
            [FromQuery] int pageIndex = 1, 
            [FromQuery] int pageSize = 20, 
            [FromQuery] string? keyword = null,
            CancellationToken cancellationToken = default)
        {
            var pagedProducts = await _productService.GetPagedProductsAsync(pageIndex, pageSize, keyword, cancellationToken);
            return Ok(pagedProducts);
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productService.GetProductByIdAsync(id, cancellationToken);
            return Ok(product);
        }

        [HttpPost]
        [PermissionAuthorize(AppPermissions.ProductCreate)]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto, CancellationToken cancellationToken = default)
        {
            var username = User?.Identity?.Name ?? "system";
            var product = await _productService.CreateProductAsync(dto, username, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        [HttpPut("{id}")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto, CancellationToken cancellationToken = default)
        {
            var username = User?.Identity?.Name ?? "system";
            await _productService.UpdateProductAsync(id, dto, username, cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [PermissionAuthorize(AppPermissions.ProductDeactivate)]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            await _productService.DeleteProductAsync(id, cancellationToken);
            return NoContent();
        }

        [HttpPut("{id:int}/category")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        public async Task<IActionResult> SetCategory(int id, [FromBody] SetProductCategoryDto dto, [FromServices] IProductCatalogService catalogService, CancellationToken cancellationToken = default)
        {
            await catalogService.SetProductCategoryAsync(id, dto, cancellationToken);
            return NoContent();
        }
    }
}
