using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces
{
    public interface IInventoryTransactionQueryService
    {
        Task<PagedResult<InventoryTransactionHistoryDto>> GetHistoryAsync(
            DateTime? fromDate,
            DateTime? toDate,
            TransactionType? transactionType,
            int? warehouseId,
            int? productId,
            int? referenceId,
            string? referenceType,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20);
    }
}
