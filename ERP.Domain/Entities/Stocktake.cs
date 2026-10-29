using ERP.Domain.Common;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;

namespace ERP.Domain.Entities;

public class Stocktake : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public void TransitionTo(StocktakeStatus targetStatus, int? actorUserId = null, DateTime? timestampUtc = null)
    {
        var isValid = (Status, targetStatus) switch
        {
            (StocktakeStatus.Draft, StocktakeStatus.InProgress) => true,
            (StocktakeStatus.Draft, StocktakeStatus.CountCompleted) => true,
            (StocktakeStatus.Draft, StocktakeStatus.PendingApproval) => true,
            (StocktakeStatus.Draft, StocktakeStatus.Approved) => true,
            (StocktakeStatus.Draft, StocktakeStatus.Completed) => true,
            (StocktakeStatus.Draft, StocktakeStatus.Cancelled) => true,
            (StocktakeStatus.Draft, StocktakeStatus.Rejected) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.CountCompleted) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.PendingApproval) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.Approved) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.Completed) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.Cancelled) => true,
            (StocktakeStatus.InProgress, StocktakeStatus.Draft) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.PendingApproval) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.Approved) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.Completed) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.InProgress) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.Cancelled) => true,
            (StocktakeStatus.CountCompleted, StocktakeStatus.Rejected) => true,
            (StocktakeStatus.PendingApproval, StocktakeStatus.Approved) => true,
            (StocktakeStatus.PendingApproval, StocktakeStatus.Completed) => true,
            (StocktakeStatus.PendingApproval, StocktakeStatus.Rejected) => true,
            (StocktakeStatus.PendingApproval, StocktakeStatus.InProgress) => true,
            (StocktakeStatus.PendingApproval, StocktakeStatus.Cancelled) => true,
            (StocktakeStatus.Approved, StocktakeStatus.Completed) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new ConflictException(
                $"Chuyển trạng thái phiếu kiểm kê từ '{Status}' sang '{targetStatus}' không hợp lệ.",
                "DOC_INVALID_STATE_TRANSITION");
        }

        Status = targetStatus;
        if (targetStatus is StocktakeStatus.Approved or StocktakeStatus.Completed)
        {
            if (actorUserId.HasValue)
            {
                ApprovedBy = actorUserId.Value;
            }
            ApprovedAt = timestampUtc ?? DateTime.UtcNow;
        }
    }

    // Navigation
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public ICollection<StocktakeDetail> Details { get; set; } = new List<StocktakeDetail>();
}
