using ERP.Domain.Enums;

namespace ERP.Domain.Policies;

public sealed record InventoryStatusCapabilities(
    InventoryStatus Status,
    string Code,
    bool IsPhysicalOnHand,
    bool IsAvailable,
    bool IsReservable,
    bool IsAllocatable,
    bool IsPickable,
    bool IsShippable);

public static class InventoryStatusPolicy
{
    private static readonly IReadOnlyDictionary<InventoryStatus, InventoryStatusCapabilities> Policies =
        new Dictionary<InventoryStatus, InventoryStatusCapabilities>
        {
            [InventoryStatus.Available] = new(InventoryStatus.Available, "AVAILABLE", true, true, true, true, true, true),
            [InventoryStatus.QcHold] = new(InventoryStatus.QcHold, "QC_HOLD", true, false, false, false, false, false),
            [InventoryStatus.Quarantine] = new(InventoryStatus.Quarantine, "QUARANTINE", true, false, false, false, false, false),
            [InventoryStatus.Damaged] = new(InventoryStatus.Damaged, "DAMAGED", true, false, false, false, false, false),
            [InventoryStatus.Rejected] = new(InventoryStatus.Rejected, "REJECTED", true, false, false, false, false, false),
            [InventoryStatus.Blocked] = new(InventoryStatus.Blocked, "BLOCKED", true, false, false, false, false, false),
            [InventoryStatus.Expired] = new(InventoryStatus.Expired, "EXPIRED", true, false, false, false, false, false),
            [InventoryStatus.RecallBlocked] = new(InventoryStatus.RecallBlocked, "RECALL_BLOCKED", true, false, false, false, false, false),
        };

    public static IReadOnlyList<InventoryStatusCapabilities> Catalog =>
        Policies.Values.OrderBy(x => (int)x.Status).ToArray();

    public static InventoryStatusCapabilities Get(InventoryStatus status) =>
        Policies.TryGetValue(status, out var policy)
            ? policy
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported inventory status.");

    public static InventoryStatus[] ReservableStatuses { get; } =
        Policies.Values.Where(x => x.IsReservable).Select(x => x.Status).ToArray();

    public static InventoryStatus[] AllocatableStatuses { get; } =
        Policies.Values.Where(x => x.IsAllocatable).Select(x => x.Status).ToArray();

    public static InventoryStatus[] PickableStatuses { get; } =
        Policies.Values.Where(x => x.IsPickable).Select(x => x.Status).ToArray();

    public static InventoryStatus[] ShippableStatuses { get; } =
        Policies.Values.Where(x => x.IsShippable).Select(x => x.Status).ToArray();
}
