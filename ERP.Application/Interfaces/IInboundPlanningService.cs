using ERP.Application.DTOs;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces;

public interface IInboundPlanningService
{
    Task<IReadOnlyList<PurchaseOrderListDto>> ListPurchaseOrdersAsync(PurchaseOrderStatus? status = null, CancellationToken token = default);
    Task<PurchaseOrderDto> GetPurchaseOrderAsync(int id, CancellationToken token = default);
    Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderDto dto, CancellationToken token = default);
    Task<PurchaseOrderDto> UpdatePurchaseOrderAsync(int id, UpdatePurchaseOrderDto dto, CancellationToken token = default);
    Task<PurchaseOrderDto> OpenPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<PurchaseOrderDto> CancelPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<PurchaseOrderDto> ClosePurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);

    Task<IReadOnlyList<AsnListDto>> ListAsnsAsync(AsnStatus? status = null, CancellationToken token = default);
    Task<AsnDto> GetAsnAsync(int id, CancellationToken token = default);
    Task<AsnDto> CreateAsnAsync(CreateAsnDto dto, CancellationToken token = default);
    Task<AsnDto> UpdateAsnAsync(int id, UpdateAsnDto dto, CancellationToken token = default);
    Task<AsnDto> ConfirmAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<AsnDto> MarkAsnInTransitAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<AsnDto> ArriveAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<AsnDto> StartReceivingAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<AsnDto> CompleteAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
    Task<AsnDto> CancelAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default);
}
