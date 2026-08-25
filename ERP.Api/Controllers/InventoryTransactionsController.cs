using System;
using System.Threading.Tasks;
using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryTransactionsController : ControllerBase
    {
        private readonly IInventoryTransactionQueryService _queryService;

        public InventoryTransactionsController(IInventoryTransactionQueryService queryService)
        {
            _queryService = queryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] TransactionType? transactionType,
            [FromQuery] int? warehouseId,
            [FromQuery] int? productId,
            [FromQuery] int? referenceId,
            [FromQuery] string? referenceType,
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var result = await _queryService.GetHistoryAsync(
                fromDate, toDate, transactionType, warehouseId, productId, referenceId, referenceType, keyword, page, pageSize);

            return Ok(result);
        }
    }
}
