namespace ERP.Domain.Security;

public static class AppPermissions
{
    public const string ApprovalApprove = "approval.approve";
    public const string ApprovalReject = "approval.reject";
    public const string AllocationRead = "allocation.read";
    public const string AllocationCreate = "allocation.create";
    public const string AllocationRelease = "allocation.release";
    public const string AllocationReallocate = "allocation.reallocate";
    public const string PickingRead = "picking.read";
    public const string PickingAssign = "picking.assign";
    public const string PickingExecute = "picking.execute";
    public const string PickingShortPick = "picking.short_pick";
    public const string PickingOverride = "picking.override";
    public const string PackingRead = "packing.read";
    public const string PackingExecute = "packing.execute";
    public const string PackingReopen = "packing.reopen";
    public const string HandlingUnitRead = "handling_unit.read";
    public const string HandlingUnitCreate = "handling_unit.create";
    public const string HandlingUnitModify = "handling_unit.modify";
    public const string HandlingUnitSplit = "handling_unit.split";
    public const string HandlingUnitMerge = "handling_unit.merge";
    public const string HandlingUnitRepack = "handling_unit.repack";
    public const string ShipmentRead = "shipment.read";
    public const string ShipmentUpdate = "shipment.update";
    public const string ShipmentStage = "shipment.stage";
    public const string ShipmentLoad = "shipment.load";
    public const string ShipmentDispatch = "shipment.dispatch";
    public const string ShipmentConfirmDelivery = "shipment.confirm_delivery";
    public const string LoadingExecute = "loading.execute";
    public const string SalesOrderRead = "sales_order.read";
    public const string SalesOrderCreate = "sales_order.create";
    public const string SalesOrderUpdate = "sales_order.update";
    public const string SalesOrderHold = "sales_order.hold";
    public const string SalesOrderRelease = "sales_order.release";
    public const string SalesOrderCancel = "sales_order.cancel";
    public const string SalesOrderClose = "sales_order.close";
    public const string BackorderRead = "backorder.read";
    public const string BackorderManage = "backorder.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryCostRead = "inventory.cost.read";
    public const string InventoryAvailabilityRead = "inventory_availability.read";
    public const string InventoryLedgerRead = "inventory_ledger.read";
    public const string InventoryTraceabilityRead = "inventory_traceability.read";
    public const string InventoryStatusChangeCreate = "inventory_status_change.create";
    public const string InventoryLockRead = "inventory_lock.read";
    public const string InventoryLockManage = "inventory_lock.manage";
    public const string InventoryMovementCreate = "inventory_movement.create";
    public const string InventoryWrite = "inventory_movement.create";
    public const string InventoryOpeningBalanceImport = "inventory_opening_balance.import";
    public const string InventoryReversalCreate = "inventory_reversal.create";
    public const string StocktakeRead = "stocktake.read";
    public const string StocktakeManage = "stocktake.manage";
    public const string ReplenishmentRead = "replenishment.read";
    public const string ReplenishmentGenerate = "replenishment.generate";
    public const string ReplenishmentExecute = "replenishment.execute";
    public const string ReportExport = "report.export";
    public const string CodReconciliationManage = "cod_reconciliation.manage";
    public const string EinvoiceIssue = "einvoice.issue";
    public const string ExportReceiptRead = "export_receipt.read";
    public const string ExportReceiptCreate = "export_receipt.create";
    public const string ExportReceiptUpdate = "export_receipt.update";
    public const string ExportReceiptApprove = "export_receipt.approve";
    public const string ExportReceiptDispatch = "export_receipt.dispatch";
    public const string ExportReceiptCancel = "export_receipt.cancel";
    public const string ExportReceiptReprintAwb = "export_receipt.reprint_awb";
    public const string DockRead = "dock.read";
    public const string DockManage = "dock.manage";
    public const string DockAppointmentRead = "dock_appointment.read";
    public const string DockAppointmentManage = "dock_appointment.manage";
    public const string YardRead = "yard.read";
    public const string YardCheckIn = "yard.checkin";
    public const string YardAssignDock = "yard.assign_dock";
    public const string YardCheckout = "yard.checkout";
    public const string CategoryRead = "product_category.read";
    public const string CategoryManage = "product_category.manage";
    public const string BarcodeManage = "product_barcode.manage";
    public const string WarehouseRead = "warehouse.read";
    public const string WarehouseManage = "warehouse.manage";
    public const string WarehouseCalendarManage = "warehouse_calendar.manage";
    public const string PurchaseOrderRead = "purchase_order.read";
    public const string PurchaseOrderCreate = "purchase_order.create";
    public const string PurchaseOrderUpdate = "purchase_order.update";
    public const string PurchaseOrderRelease = "purchase_order.release";
    public const string PurchaseOrderClose = "purchase_order.close";
    public const string PurchaseOrderCancel = "purchase_order.cancel";
    public const string AsnRead = "asn.read";
    public const string AsnCreate = "asn.create";
    public const string AsnUpdate = "asn.update";
    public const string AsnConfirm = "asn.confirm";
    public const string AsnReceive = "asn.receive";
    public const string AsnCancel = "asn.cancel";
    public const string ReceiptRead = "receipt.read";
    public const string ReceiptCreate = "receipt.create";
    public const string ReceiptUpdate = "receipt.update";
    public const string ReceiptCancel = "receipt.cancel";
    public const string ReceiptReceive = "receipt.receive";
    public const string ReceiptComplete = "receipt.complete";
    public const string ReceiptPost = "receipt.post";
    public const string ReturnRead = "return.read";
    public const string ReturnCreate = "return.create";
    public const string ReturnReceive = "return.receive";
    public const string ReturnInspect = "return.inspect";
    public const string ReturnDisposition = "return.disposition";
    public const string ReturnPost = "return.post";
    public const string QualityExecute = "quality_inspection.execute";
    public const string QualityComplete = "quality_inspection.complete";
    public const string QualityApprove = "quality_disposition.approve";
    public const string DiscrepancyRead = "receiving_discrepancy.read";
    public const string DiscrepancyCreate = "receiving_discrepancy.create";
    public const string DiscrepancySubmit = "receiving_discrepancy.submit";
    public const string DiscrepancyApprove = "receiving_discrepancy.approve";
    public const string DiscrepancyReject = "receiving_discrepancy.reject";
    public const string DiscrepancyResolve = "receiving_discrepancy.resolve";
    public const string PutawayRead = "putaway.read";
    public const string PutawayAssign = "putaway.assign";
    public const string PutawayExecute = "putaway.execute";
    public const string PutawayCancel = "putaway.cancel";
    public const string LocationRead = "location.read";
    public const string LocationManage = "location.manage";
    public const string ProductRead = "product.read";
    public const string ProductCreate = "product.create";
    public const string ProductUpdate = "product.update";
    public const string ProductDeactivate = "product.deactivate";
    public const string ProductUomManage = "product_uom.manage";
    public const string UomRead = "uom.read";
    public const string UomManage = "uom.manage";
    public const string PartnerRead = "partner.read";
    public const string PartnerCreate = "partner.create";
    public const string PartnerUpdate = "partner.update";
    public const string PartnerDeactivate = "partner.deactivate";
    public const string ReasonRead = "reason_code.read";
    public const string ReasonManage = "reason_code.manage";
    public const string QualityPolicyRead = "quality_policy.read";
    public const string QualityPolicyManage = "quality_policy.manage";
    public const string ToleranceRead = "receiving_tolerance_policy.read";
    public const string ToleranceManage = "receiving_tolerance_policy.manage";
    public const string PermissionRead = "permission.read";
    public const string PermissionAssign = "permission.assign";
    public const string RoleRead = "role.read";
    public const string RoleManage = "role.manage";
    public const string UserRead = "user.read";
    public const string UserManage = "user.manage";
    public const string UserWarehouseRead = "user_warehouse.read";
    public const string UserWarehouseManage = "user_warehouse.manage";
    public const string UsersManage = UserManage;
    public const string RolesManage = RoleManage;

    public const string PlatformDashboardRead = "platform.dashboard.read";
    public const string PlatformTenantRead = "platform.tenant.read";
    public const string PlatformTenantProvision = "platform.tenant.provision";
    public const string PlatformTenantSuspend = "platform.tenant.suspend";
    public const string PlatformTenantWrite = PlatformTenantSuspend;
    public const string PlatformBillingManage = "platform.billing.manage";
    public const string PlatformBillingWrite = PlatformBillingManage;
    public const string PlatformBillingReconcile = "platform.billing.reconcile";
    public const string PlatformQuotaOverride = "platform.quota.override";
    public const string PlatformQuotaWrite = PlatformQuotaOverride;
    public const string PlatformIntegrationManage = "platform.integration.manage";
    public const string PlatformAuditRead = "platform.audit.read";
    public const string PlatformSupportImpersonate = "platform.support.impersonate";

    private static string[] NormalizeBundle(params string[] codes) =>
        codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static readonly string[] PlatformOnlyPermissions = NormalizeBundle(
        PlatformDashboardRead, PlatformTenantRead, PlatformTenantProvision, PlatformTenantSuspend,
        PlatformBillingManage, PlatformBillingReconcile, PlatformQuotaOverride, PlatformIntegrationManage,
        PlatformAuditRead, PlatformSupportImpersonate);

    public static readonly string[] All = typeof(AppPermissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(x => x.IsLiteral && x.FieldType == typeof(string))
        .Select(x => (string)x.GetRawConstantValue()!)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static string[] Catalog => All;

    public static readonly string[] CustomerAllPermissions = All
        .Where(code => !code.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public static readonly string[] CanonicalCustomerRoleNames =
        ["Admin", "Manager", "Supervisor", "Accountant", "WarehouseStaff", "Viewer"];

    public static readonly string[] PlatformRoleNames =
        [
            "SuperAdmin", "BillingAdmin", "SupportAdmin", "SecurityAuditor",
            "PlatformOpsAdmin", "PlatformSuperAdmin", "PlatformOwner", "PlatformAdmin",
            "PlatformBillingAdmin", "PlatformSupportAgent", "PlatformSecurityAuditor",
            "SupportReadOnly", "PlatformReadOnly"
        ];

    public static bool IsPlatformRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var trimmed = roleName.Trim();
        return trimmed.StartsWith("Platform", StringComparison.OrdinalIgnoreCase)
            || PlatformRoleNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }

    public static string ResolvePermissionDescriptionVi(string code) => code switch
    {
        ApprovalApprove => "Phê duyệt phiếu nhập kho",
        ApprovalReject => "Từ chối phê duyệt phiếu nhập kho",
        _ => $"Quyền hệ thống {code}"
    };

    public static readonly IReadOnlyList<PermissionMetadata> Definitions = All
        .Select(code => new PermissionMetadata(code, ResolvePermissionDescriptionVi(code)))
        .ToArray();

    public static readonly IReadOnlyList<PermissionMetadata> CustomerDefinitions = CustomerAllPermissions
        .Select(code => new PermissionMetadata(code, ResolvePermissionDescriptionVi(code)))
        .ToArray();

    private static readonly string[] BillingAdminPermissions = NormalizeBundle(
        PlatformDashboardRead, PlatformTenantRead, PlatformBillingManage,
        PlatformBillingReconcile, PlatformQuotaOverride, PlatformAuditRead, PermissionRead);

    private static readonly string[] SupportAdminPermissions = NormalizeBundle(
        PlatformDashboardRead, PlatformTenantRead, PlatformAuditRead,
        PlatformSupportImpersonate, PermissionRead);

    private static readonly string[] SecurityAuditorPermissions = NormalizeBundle(
        PlatformDashboardRead, PlatformTenantRead, PlatformAuditRead, PermissionRead);

    private static readonly string[] PlatformOpsAdminPermissions = NormalizeBundle(
        PlatformDashboardRead, PlatformTenantRead, PlatformTenantProvision, PlatformTenantSuspend,
        PlatformIntegrationManage, PlatformAuditRead, PlatformSupportImpersonate,
        PermissionRead, RoleRead, UserRead);

    // 18 distinct Customer WMS read-only codes (0 mutations, 0 cost, 0 platform.*)
    private static readonly string[] ViewerPermissions = NormalizeBundle(
        InventoryRead, InventoryAvailabilityRead, InventoryLedgerRead, InventoryTraceabilityRead, StocktakeRead,
        ProductRead, CategoryRead, UomRead, WarehouseRead, LocationRead, PartnerRead,
        PurchaseOrderRead, AsnRead, ReceiptRead, SalesOrderRead, BackorderRead, ExportReceiptRead, ShipmentRead);

    // 36 distinct Customer WMS floor codes (14 read + 22 floor execution; 0 cost, 0 opening_balance, 0 platform.*)
    private static readonly string[] WarehouseStaffPermissions = NormalizeBundle(
        InventoryRead, LocationRead, ProductRead, UomRead, WarehouseRead, ReceiptRead, AsnRead,
        PutawayRead, PickingRead, PackingRead, HandlingUnitRead, ShipmentRead, ReturnRead, DockAppointmentRead,
        AsnReceive, ReceiptReceive, ReceiptComplete, PutawayExecute, PickingExecute, PickingShortPick,
        PackingExecute, HandlingUnitCreate, HandlingUnitModify, HandlingUnitSplit, HandlingUnitMerge, HandlingUnitRepack,
        InventoryMovementCreate, ReplenishmentRead, ReplenishmentExecute, ShipmentStage, ShipmentLoad, LoadingExecute,
        YardCheckIn, YardCheckout, ReturnReceive, DiscrepancyCreate);

    // 54 distinct Customer WMS floor supervision & QC codes (36 staff + 18 supervisory; 0 cost, 0 opening_balance, 0 platform.*)
    private static readonly string[] SupervisorPermissions = NormalizeBundle(
        [
            ..WarehouseStaffPermissions,
            QualityExecute, QualityComplete,
            DiscrepancyRead, DiscrepancySubmit, DiscrepancyResolve,
            StocktakeRead, StocktakeManage, ReplenishmentGenerate,
            InventoryLockRead, InventoryLockManage, InventoryStatusChangeCreate,
            PickingAssign, PickingOverride, PackingReopen, PutawayAssign,
            ReturnInspect, ReturnDisposition, YardAssignDock
        ]);

    // 42 distinct Customer WMS finance & audit codes (36 non-platform read + cost/export/COD/eInvoice/partner; 0 floor exec, 0 platform.*)
    private static readonly string[] AccountantPermissions = NormalizeBundle(
        AllocationRead, AsnRead, BackorderRead, CategoryRead, DiscrepancyRead, DockAppointmentRead, DockRead,
        ExportReceiptRead, HandlingUnitRead, InventoryAvailabilityRead, InventoryLedgerRead, InventoryLockRead,
        InventoryRead, InventoryTraceabilityRead, LocationRead, PackingRead, PartnerRead, PickingRead,
        ProductRead, PurchaseOrderRead, PutawayRead, QualityPolicyRead, ReasonRead, ReceiptRead,
        ReplenishmentRead, ReturnRead, SalesOrderRead, ShipmentRead, StocktakeRead, ToleranceRead,
        UomRead, WarehouseRead, YardRead, PermissionRead, RoleRead, UserRead,
        InventoryCostRead, ReportExport, CodReconciliationManage, EinvoiceIssue, PartnerCreate, PartnerUpdate);

    // 98 distinct Customer WMS operational & approval codes (excludes opening_balance.import, IAM writes, and platform.*)
    private static readonly string[] ManagerPermissions = NormalizeBundle(
        ApprovalApprove, ApprovalReject,
        InventoryRead, InventoryCostRead, InventoryAvailabilityRead, InventoryLedgerRead, InventoryTraceabilityRead,
        InventoryStatusChangeCreate, InventoryLockRead, InventoryLockManage, InventoryMovementCreate, InventoryReversalCreate,
        StocktakeRead, StocktakeManage, ReplenishmentRead, ReplenishmentGenerate, ReplenishmentExecute, ReportExport,
        AllocationRead, AllocationCreate, AllocationRelease, AllocationReallocate, BackorderRead, BackorderManage,
        PurchaseOrderRead, PurchaseOrderRelease,
        AsnRead, AsnCreate, AsnUpdate, AsnConfirm, AsnReceive,
        ReceiptRead, ReceiptCreate, ReceiptUpdate, ReceiptCancel, ReceiptReceive, ReceiptComplete, ReceiptPost,
        PutawayRead, PutawayAssign, PutawayExecute, PutawayCancel,
        QualityExecute, QualityComplete, QualityApprove,
        DiscrepancyRead, DiscrepancyApprove, DiscrepancyReject, DiscrepancyResolve,
        SalesOrderRead, SalesOrderHold, SalesOrderRelease,
        PickingRead, PickingAssign, PickingShortPick, PickingOverride,
        PackingRead, PackingExecute, PackingReopen,
        HandlingUnitRead, HandlingUnitCreate,
        ExportReceiptRead, ExportReceiptCreate, ExportReceiptUpdate, ExportReceiptApprove,
        ExportReceiptDispatch, ExportReceiptCancel, ExportReceiptReprintAwb,
        ShipmentRead, ShipmentUpdate, ShipmentStage, ShipmentLoad, ShipmentDispatch, ShipmentConfirmDelivery,
        DockRead, DockAppointmentRead, DockAppointmentManage, YardRead,
        ReturnRead, ReturnCreate, ReturnReceive, ReturnInspect, ReturnDisposition, ReturnPost,
        ProductRead, ProductCreate, ProductUpdate, ProductDeactivate, CategoryRead, UomRead,
        PartnerRead, PartnerCreate, PartnerUpdate, LocationRead, LocationManage, WarehouseRead, WarehouseManage,
        PermissionRead);

    public static IReadOnlyList<string> GetDefaultPermissionsForRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return [];
        }

        return roleName.Trim() switch
        {
            "SuperAdmin" or "PlatformSuperAdmin" or "PlatformOwner" or "PlatformAdmin" => All,
            "BillingAdmin" or "PlatformBillingAdmin" => BillingAdminPermissions,
            "SupportAdmin" or "PlatformSupportAgent" => SupportAdminPermissions,
            "SecurityAuditor" or "PlatformSecurityAuditor" or "SupportReadOnly" or "PlatformReadOnly" => SecurityAuditorPermissions,
            "PlatformOpsAdmin" => PlatformOpsAdminPermissions,
            "Admin" => CustomerAllPermissions,
            "Manager" or "WarehouseManager" => ManagerPermissions,
            "Supervisor" or "WarehouseSupervisor" => SupervisorPermissions,
            "Accountant" => AccountantPermissions,
            "WarehouseStaff" => WarehouseStaffPermissions,
            "Viewer" or "SalesStaff" or "ProcurementStaff" => ViewerPermissions,
            _ => []
        };
    }
}

public sealed record PermissionMetadata(string Code, string Description);
