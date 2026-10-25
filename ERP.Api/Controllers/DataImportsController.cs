using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/data-imports")]
[Route("api/data-imports")]
public sealed class DataImportsController : ControllerBase
{
    private readonly ErpKhoDbContext? _db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;
    private readonly IDataImportService _dataImportService;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public DataImportsController(
        ErpKhoDbContext? db = null,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null,
        IDataImportService? dataImportService = null)
    {
        _db = db;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
        _dataImportService = dataImportService ?? new DataImportService(db);
    }

    public DataImportsController(
        IExportReceiptService? exportReceiptService,
        ErpKhoDbContext? db = null,
        IWarehouseAuthorizationService? warehouseAccess = null,
        ICurrentUser? currentUser = null,
        IDataImportService? dataImportService = null)
        : this(db, warehouseAccess, currentUser, dataImportService)
    {
        _ = exportReceiptService;
    }

    [HttpGet("templates")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public IActionResult GetDataImportTemplates() =>
        Ok(_dataImportService.GetTemplatesPayload());

    [HttpPost("opening-balance")]
    [HttpPost("opening-balances")]
    [HttpPost("opening-balances/excel")]
    [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite)]
    [IdempotentCommand("Inventory.ImportOpeningBalance")]
    public async Task<IActionResult> ImportOpeningBalance(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] DataImportOpeningBalanceRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? file = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (_db == null && request == null && file == null)
        {
            return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        if (request?.WarehouseId is > 0 && _warehouseAccess != null)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        }

        var items = new List<DataImportOpeningBalanceItemRequest>();
        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        if (uploadedFile != null && uploadedFile.Length > 0)
        {
            using var stream = uploadedFile.OpenReadStream();
            using var wb = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var pCode = row.Cell(1).GetString()?.Trim();
                var locCode = row.Cell(2).GetString()?.Trim();
                var qtyVal = row.Cell(3).TryGetValue<decimal>(out var q)
                    ? q
                    : (decimal.TryParse(row.Cell(3).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qParsed) ? qParsed : 0m);
                var costVal = row.Cell(4).TryGetValue<decimal>(out var c)
                    ? c
                    : (decimal.TryParse(row.Cell(4).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cParsed) ? cParsed : 0m);
                var lotNumber = row.Cell(5).GetString()?.Trim();
                DateTime? expiryDate = null;
                if (row.Cell(6).TryGetValue<DateTime>(out var dt))
                {
                    expiryDate = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
                }
                else if (DateTime.TryParse(row.Cell(6).GetString()?.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dtParsed))
                {
                    expiryDate = DateTime.SpecifyKind(dtParsed.Date, DateTimeKind.Utc);
                }

                if (!string.IsNullOrWhiteSpace(pCode) && qtyVal > 0m)
                {
                    items.Add(new DataImportOpeningBalanceItemRequest(
                        ProductCode: pCode,
                        LocationCode: string.IsNullOrWhiteSpace(locCode) ? null : locCode,
                        Quantity: qtyVal,
                        UnitCost: costVal,
                        LotNumber: string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
                        ExpiryDate: expiryDate));
                }
            }
        }

        if (items.Count == 0 && request?.Items is { Count: > 0 })
        {
            items.AddRange(request.Items);
        }

        var normalizedRequest = new DataImportOpeningBalanceRequest(
            WarehouseId: request?.WarehouseId,
            ProductId: request?.ProductId,
            LocationId: request?.LocationId,
            Quantity: request?.Quantity,
            UnitCost: request?.UnitCost,
            CutoverReference: request?.CutoverReference,
            Items: items.Count > 0 ? items : request?.Items);

        var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.FindFirstValue("Id")
            ?? (_currentUser?.UserId > 0 ? _currentUser.UserId.ToString() : null);
        int? actorUserId = int.TryParse(userIdStr, out var parsedUid) && parsedUid > 0 ? parsedUid : null;

        var res = await _dataImportService.ImportOpeningBalanceAsync(normalizedRequest, actorUserId, cancellationToken);
        return res.StatusCode switch
        {
            200 => Ok(res.Payload),
            400 => BadRequest(res.Payload),
            404 => NotFound(res.Payload),
            _ => StatusCode(res.StatusCode, res.Payload)
        };
    }
}
