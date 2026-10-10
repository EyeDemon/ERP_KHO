using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IReceiptInboundPlanningIntegration
{
    Task AttachSourceAsync(ImportReceipt receipt, CreateImportReceiptDto dto, CancellationToken token = default);
    Task MarkReceivingAsync(ImportReceipt receipt, CancellationToken token = default);
    Task ValidatePostAsync(ImportReceipt receipt, CancellationToken token = default);
    Task ApplyPostedStateAsync(ImportReceipt receipt, int actorUserId, CancellationToken token = default);
}
