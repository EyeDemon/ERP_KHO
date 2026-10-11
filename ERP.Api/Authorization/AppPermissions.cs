namespace ERP.Api.Authorization;

public static class AppPermissions
{
    public const string ReservationRead = "reservation.read", ReservationCreate = "reservation.create", ReservationRelease = "reservation.release";
    public const string ApprovalReject = "approval.reject";
    public const string ExportReceiptRead = "export_receipt.read", ExportReceiptCreate = "export_receipt.create", ExportReceiptUpdate = "export_receipt.update", ExportReceiptApprove = "export_receipt.approve", ExportReceiptDispatch = "export_receipt.dispatch", ExportReceiptCancel = "export_receipt.cancel";
    public const string CategoryRead="product_category.read", CategoryManage="product_category.manage", BarcodeManage="product_barcode.manage", WarehouseRead="warehouse.read", WarehouseManage="warehouse.manage";
    public const string ReceiptRead="receipt.read", ReceiptCreate="receipt.create", ReceiptUpdate="receipt.update", ReceiptCancel="receipt.cancel", ReceiptReceive="receipt.receive", ReceiptComplete="receipt.complete", ReceiptPost="receipt.post";
    public const string QualityExecute="quality_inspection.execute", QualityComplete="quality_inspection.complete", QualityApprove="quality_disposition.approve";
    public const string DiscrepancyRead="receiving_discrepancy.read", DiscrepancyCreate="receiving_discrepancy.create", DiscrepancySubmit="receiving_discrepancy.submit", DiscrepancyApprove="receiving_discrepancy.approve", DiscrepancyReject="receiving_discrepancy.reject", DiscrepancyResolve="receiving_discrepancy.resolve";
    public const string PutawayRead="putaway.read", PutawayAssign="putaway.assign", PutawayExecute="putaway.execute", PutawayCancel="putaway.cancel";
    public const string LocationRead="location.read", LocationManage="location.manage", ProductRead="product.read", ProductCreate="product.create", ProductUpdate="product.update", ProductDeactivate="product.deactivate", ProductUomManage="product_uom.manage", UomRead="uom.read", UomManage="uom.manage", PartnerRead="partner.read", PartnerCreate="partner.create", PartnerUpdate="partner.update", PartnerDeactivate="partner.deactivate", ReasonRead="reason_code.read", ReasonManage="reason_code.manage", QualityPolicyRead="quality_policy.read", QualityPolicyManage="quality_policy.manage", ToleranceRead="receiving_tolerance_policy.read", ToleranceManage="receiving_tolerance_policy.manage";
    public const string PermissionRead="permission.read", PermissionAssign="permission.assign", RoleRead="role.read", RoleManage="role.manage", UserRead="user.read", UserManage="user.manage", UserWarehouseRead="user_warehouse.read", UserWarehouseManage="user_warehouse.manage";

    public static readonly string[] Catalog = typeof(AppPermissions).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Where(x=>x.IsLiteral&&x.FieldType==typeof(string)).Select(x=>(string)x.GetRawConstantValue()!).Distinct(StringComparer.Ordinal).Order().ToArray();
}
