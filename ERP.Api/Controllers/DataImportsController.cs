using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/data-imports")]
[Route("api/data-imports")]
public sealed class DataImportsController : ControllerBase
{
    private readonly IExportReceiptService _exportReceiptService;
    private readonly ErpKhoDbContext _db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;

    public DataImportsController(
        IExportReceiptService exportReceiptService,
        ErpKhoDbContext db,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null)
    {
        _exportReceiptService = exportReceiptService;
        _db = db;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    [HttpGet("templates")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public IActionResult GetDataImportTemplates()
    {
        return Ok(ExportReceiptsController.BuildDataImportTemplatesPayload());
    }

    [HttpPost("opening-balance")]
    [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite)]
    [IdempotentCommand("Inventory.ImportOpeningBalance")]
    public async Task<IActionResult> ImportOpeningBalance(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ExportReceiptsController.ImportOpeningBalanceRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? file = null)
    {
        var forwarder = new ExportReceiptsController(_exportReceiptService, _db, _warehouseAccess!, _currentUser)
        {
            ControllerContext = ControllerContext
        };
        return await forwarder.ImportOpeningBalance(request, cancellationToken, file);
    }
}
