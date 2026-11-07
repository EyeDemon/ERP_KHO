import axios from 'axios';

const resources: Record<string, string> = {
  approval: 'phiếu nhập kho',
  receipt: 'phiếu nhập',
  quality_inspection: 'kiểm tra chất lượng',
  quality_disposition: 'kết quả kiểm tra chất lượng',
  quality: 'kiểm soát chất lượng',
  receiving_discrepancy: 'sai lệch nhận hàng',
  putaway: 'cất hàng',
  location: 'vị trí kho',
  warehouse: 'kho hàng',
  warehouse_zone: 'cấu trúc khu vực kho',
  warehouse_calendar: 'lịch và ca vận hành kho',
  product: 'sản phẩm',
  product_category: 'danh mục sản phẩm',
  category: 'danh mục sản phẩm',
  product_barcode: 'mã vạch sản phẩm',
  product_uom: 'đơn vị quy đổi sản phẩm',
  uom: 'đơn vị tính',
  partner: 'đối tác',
  reason_code: 'mã lý do',
  quality_policy: 'chính sách chất lượng',
  receiving_tolerance_policy: 'chính sách dung sai nhận hàng',
  permission: 'quyền truy cập',
  role: 'vai trò',
  user: 'người dùng',
  user_warehouse: 'phạm vi kho của người dùng',
  allocation: 'phân bổ tồn kho',
  picking: 'nhiệm vụ lấy hàng',
  packing: 'phiên đóng gói',
  handling_unit: 'đơn vị xử lý (HU)',
  shipment: 'lô giao hàng',
  loading: 'xếp hàng lên phương tiện',
  sales_order: 'đơn bán hàng (SO)',
  backorder: 'đơn thiếu hàng (Backorder)',
  inventory: 'tồn kho',
  inventory_availability: 'tồn kho khả dụng',
  inventory_ledger: 'sổ cái tồn kho',
  inventory_traceability: 'truy vết tồn kho',
  inventory_status_change: 'đổi trạng thái tồn kho',
  inventory_lock: 'khóa tồn kho',
  inventory_movement: 'di chuyển vị trí nội bộ',
  inventory_reversal: 'đảo bút toán tồn kho',
  export_receipt: 'phiếu xuất kho',
  dock: 'cửa kho (Dock)',
  dock_appointment: 'lịch hẹn cửa kho',
  yard: 'điều phối sân bãi (Yard)',
  purchase_order: 'đơn mua hàng (PO)',
  asn: 'thông báo giao hàng dự kiến (ASN)',
  stock_transfer: 'phiếu điều chuyển kho',
  stocktake: 'phiếu kiểm kê',
  stock_reservation: 'giữ hàng tồn kho',
  inventory_opening_balance: 'tồn kho đầu kỳ (Excel/CSV)',
  return: 'hàng hoàn và đổi trả (RMA)',
  replenishment: 'đề xuất bổ sung hàng lên kệ nhặt',
  report: 'báo cáo kho vận',
  cod_reconciliation: 'đối soát thu hộ COD',
  einvoice: 'hóa đơn điện tử GTGT',
};

const actions: Record<string, string> = {
  read: 'Xem',
  create: 'Tạo',
  update: 'Cập nhật',
  deactivate: 'Ngừng sử dụng',
  manage: 'Quản lý',
  cancel: 'Hủy',
  receive: 'Nhận hàng trên',
  complete: 'Hoàn tất',
  post: 'Ghi nhận tồn kho từ',
  execute: 'Thực hiện',
  approve: 'Phê duyệt',
  reject: 'Từ chối',
  submit: 'Gửi xử lý',
  resolve: 'Xử lý',
  assign: 'Phân công',
  release: 'Giải phóng / phát hành',
  reallocate: 'Phân bổ lại',
  short_pick: 'Xác nhận lấy thiếu',
  override: 'Ghi đè ngoại lệ',
  reopen: 'Mở lại',
  modify: 'Điều chỉnh',
  split: 'Tách',
  merge: 'Gộp',
  repack: 'Đóng gói lại',
  stage: 'Đưa ra khu tập kết',
  load: 'Xếp hàng lên phương tiện cho',
  dispatch: 'Xác nhận xuất kho',
  confirm_delivery: 'Xác nhận giao hàng cho',
  hold: 'Tạm giữ',
  close: 'Đóng',
  checkin: 'Xác nhận xe vào cổng cho',
  assign_dock: 'Phân cửa kho cho',
  checkout: 'Xác nhận xe rời bãi cho',
  confirm: 'Xác nhận',
  import: 'Nhập',
  inspect: 'Kiểm định chất lượng',
  disposition: 'Định đoạt xử lý',
  generate: 'Khởi tạo',
  export: 'Kết xuất',
  issue: 'Phát hành',
};


export const PERMISSION_DESCRIPTIONS: Record<string, string> = {
  'approval.approve': 'Cho phép phê duyệt phiếu nhập kho và các yêu cầu phê duyệt chứng từ kho.',
  'approval.reject': 'Cho phép từ chối yêu cầu phê duyệt phiếu nhập kho.',
  'inventory.cost.read': 'Cho phép xem đơn giá mua/bán, giá vốn bình quân (MAC) và giá trị chênh lệch kiểm kê trên các chứng từ và báo cáo kho.',
  'inventory_opening_balance.import': 'Cho phép tải lên và ghi sổ tồn kho đầu kỳ từ tệp Excel/CSV vào kho.',
  'export_receipt.reprint_awb': 'Cho phép in lại tem vận đơn nhiệt AWB 100x150mm trên phiếu xuất kho đã xác nhận.',
  'return.read': 'Cho phép xem danh sách phiếu hàng hoàn & đổi trả (RMA), trạng thái biệt trữ và kết quả định đoạt.',
  'return.create': 'Cho phép tạo phiếu tiếp nhận hàng hoàn trả (RMA) mới từ vận đơn AWB hoặc đơn hàng gốc.',
  'return.receive': 'Cho phép quét tiếp nhận kiện hàng hoàn trả vào khu vực Biệt trữ (Quarantine).',
  'return.inspect': 'Cho phép kiểm định tình trạng vật lý kiện hoàn (Nguyên vẹn / Hỏng vỏ hộp / Lỗi kỹ thuật).',
  'return.disposition': 'Cho phép chọn hướng định đoạt hàng hoàn (Nhập lại kệ, Tân trang thay hộp, Xuất trả NCC, Thanh hủy).',
  'return.post': 'Cho phép thực thi định đoạt hàng hoàn và ghi sổ Sổ cái tồn kho.',
  'platform.dashboard.read': 'Cho phép xem bảng điều khiển toàn nền tảng SaaS (tổng quan doanh nghiệp, gói cước và trạng thái vận hành).',
  'platform.tenant.read': 'Cho phép tra cứu danh sách doanh nghiệp thuê kho (Tenant), hạn mức kho và cấu hình gói dịch vụ.',
  'platform.tenant.provision': 'Cho phép khởi tạo mới doanh nghiệp thuê kho (Tenant) trên nền tảng SaaS.',
  'platform.tenant.suspend': 'Cho phép tạm ngưng hoặc khôi phục trạng thái hoạt động của doanh nghiệp thuê kho.',
  'platform.billing.manage': 'Cho phép quản lý chu kỳ cước SaaS, phát hành hóa đơn và theo dõi công nợ thuê bao.',
  'platform.billing.reconcile': 'Cho phép xác nhận gạch nợ thủ công hóa đơn SaaS kèm mã giao dịch ngân hàng (UNC/FT).',
  'platform.quota.override': 'Cho phép điều chỉnh hạn mức số kho, người dùng và sản phẩm cho từng doanh nghiệp.',
  'platform.support.impersonate': 'Cho phép khởi tạo phiên hỗ trợ kỹ thuật có kiểm soát (Impersonation) kèm nhật ký kiểm toán.',
  'platform.audit.read': 'Cho phép tra cứu nhật ký kiểm toán bảo mật và bằng chứng tuân thủ cấp nền tảng.',
  'platform.integration.manage': 'Cho phép quản trị cấu hình tích hợp cổng thanh toán, vận chuyển và webhook cấp nền tảng.',
};

export const CONFLICT_ERROR_MESSAGES: Record<string, string> = {
  CONCURRENCY_CONFLICT: 'Dữ liệu vừa được cập nhật bởi phiên làm việc khác. Hệ thống đã tải lại số liệu mới nhất.',
  INSUFFICIENT_AVAILABLE_STOCK: 'Tồn kho khả dụng tại vị trí chọn không đủ để thực hiện thao tác. Vui lòng điều chỉnh số lượng hoặc chọn vị trí khác.',
  DOCUMENT_OWNED_RESERVATION: 'Bản ghi giữ hàng này thuộc quyền quản lý của chứng từ gốc. Vui lòng thao tác trực tiếp trên Phiếu xuất hoặc Đơn hàng.',
  INVALID_STATE_TRANSITION: 'Trạng thái chứng từ đã thay đổi, không thể thực hiện thao tác này. Đang đồng bộ lại trạng thái...',
  ORDER_CANCELLED_PUTBACK_REQUIRED: 'Đơn hàng đã bị hủy giữa chừng và đang chờ cất trả hàng về kệ lưu trữ (Putback). Không thể tiếp tục đóng gói hoặc xuất kho.',
};

export const extractConflictCode = (error: unknown): string | undefined => {
  const data = (error as { response?: { status?: number; data?: { code?: string; errorCode?: string; error?: { code?: string } } } })?.response?.data;
  return data?.code || data?.errorCode || data?.error?.code;
};

export const conflictErrorMessage = (
  error: unknown,
  fallback409 = 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.',
): string => {
  const code = extractConflictCode(error);
  if (code && CONFLICT_ERROR_MESSAGES[code]) return CONFLICT_ERROR_MESSAGES[code];
  return fallback409;

};

const PLATFORM_PERMISSION_LABELS: Record<string, string> = {
  'platform.dashboard.read': 'Xem bảng điều khiển nền tảng SaaS',
  'platform.tenant.read': 'Xem danh sách doanh nghiệp (Tenant)',
  'platform.tenant.provision': 'Khởi tạo doanh nghiệp thuê kho mới',
  'platform.tenant.suspend': 'Tạm ngưng / khôi phục doanh nghiệp',
  'platform.billing.manage': 'Quản lý hóa đơn & gói cước SaaS',
  'platform.billing.reconcile': 'Đối soát & gạch nợ hóa đơn SaaS',
  'platform.quota.override': 'Điều chỉnh hạn mức tài nguyên Tenant',
  'platform.support.impersonate': 'Hỗ trợ kỹ thuật qua phiên Impersonation',
  'platform.audit.read': 'Xem nhật ký kiểm toán nền tảng',
  'platform.integration.manage': 'Quản lý tích hợp cấp nền tảng',
};

export const permissionDescription = (code: string, fallbackDescription?: string): string =>
  PERMISSION_DESCRIPTIONS[code] || fallbackDescription || 'Quyền truy cập hệ thống';

export const permissionLabel = (code: string): string => {
  if (PLATFORM_PERMISSION_LABELS[code]) return PLATFORM_PERMISSION_LABELS[code];
  if (code === 'approval.approve') return 'Phê duyệt phiếu nhập kho';
  if (code === 'approval.reject') return 'Từ chối phê duyệt phiếu nhập kho';
  if (code === 'permission.assign') return 'Cấp và thu hồi quyền truy cập';
  if (code === 'inventory.cost.read') return 'Xem đơn giá, giá vốn và giá trị tồn kho';
  if (code === 'export_receipt.reprint_awb') return 'In lại tem vận đơn nhiệt AWB';
  if (code === 'inventory_opening_balance.import') return 'Nhập tồn kho đầu kỳ (Excel/CSV)';
  const [resource, action] = code.split('.');
  return resources[resource] && actions[action] ? `${actions[action]} ${resources[resource]}` : 'Quyền chưa có diễn giải';
};
export const PERMISSION_MODULE_GROUPS = [
  'Nhập kho',
  'Xuất kho',
  'Giao vận & Hàng hoàn',
  'Tồn kho',
  'Sản phẩm & Danh mục',
  'Tài chính & Giá vốn',
  'Cấu trúc kho',
  'Quản trị & Bảo mật',
] as const;

export type PermissionModuleGroup = (typeof PERMISSION_MODULE_GROUPS)[number];

const MODULE_BY_RESOURCE: Record<string, PermissionModuleGroup> = {
  purchase_order: 'Nhập kho',
  asn: 'Nhập kho',
  dock: 'Nhập kho',
  dock_appointment: 'Nhập kho',
  yard: 'Nhập kho',
  receipt: 'Nhập kho',
  approval: 'Nhập kho',
  receiving_discrepancy: 'Nhập kho',
  receiving_tolerance_policy: 'Nhập kho',
  quality_inspection: 'Nhập kho',
  quality_disposition: 'Nhập kho',
  quality: 'Nhập kho',
  quality_policy: 'Nhập kho',
  putaway: 'Nhập kho',
  sales_order: 'Xuất kho',
  backorder: 'Xuất kho',
  allocation: 'Xuất kho',
  picking: 'Xuất kho',
  packing: 'Xuất kho',
  handling_unit: 'Xuất kho',
  export_receipt: 'Xuất kho',
  shipment: 'Giao vận & Hàng hoàn',
  loading: 'Giao vận & Hàng hoàn',
  return: 'Giao vận & Hàng hoàn',
  inventory: 'Tồn kho',
  inventory_availability: 'Tồn kho',
  inventory_ledger: 'Tồn kho',
  inventory_traceability: 'Tồn kho',
  inventory_status_change: 'Tồn kho',
  inventory_lock: 'Tồn kho',
  inventory_movement: 'Tồn kho',
  inventory_reversal: 'Tồn kho',
  inventory_opening_balance: 'Tồn kho',
  stock_transfer: 'Tồn kho',
  stocktake: 'Tồn kho',
  stock_reservation: 'Tồn kho',
  replenishment: 'Tồn kho',
  report: 'Tài chính & Giá vốn',
  cod_reconciliation: 'Tài chính & Giá vốn',
  einvoice: 'Tài chính & Giá vốn',
  product: 'Sản phẩm & Danh mục',
  product_category: 'Sản phẩm & Danh mục',
  category: 'Sản phẩm & Danh mục',
  product_barcode: 'Sản phẩm & Danh mục',
  product_uom: 'Sản phẩm & Danh mục',
  uom: 'Sản phẩm & Danh mục',
  partner: 'Sản phẩm & Danh mục',
  reason_code: 'Sản phẩm & Danh mục',
  warehouse: 'Cấu trúc kho',
  warehouse_zone: 'Cấu trúc kho',
  warehouse_calendar: 'Cấu trúc kho',
  location: 'Cấu trúc kho',
  permission: 'Quản trị & Bảo mật',
  role: 'Quản trị & Bảo mật',
  user: 'Quản trị & Bảo mật',
  user_warehouse: 'Quản trị & Bảo mật',
};

export const permissionModuleGroup = (code: string): PermissionModuleGroup => {
  if (code === 'inventory.cost.read' || code.startsWith('finance.') || code.startsWith('billing.') || code.startsWith('accounting.')) {
    return 'Tài chính & Giá vốn';
  }
  const [resource] = code.split('.');
  return MODULE_BY_RESOURCE[resource] || 'Quản trị & Bảo mật';
};

export const PLATFORM_ROLE_NAMES = [
  'SuperAdmin',
  'BillingAdmin',
  'SupportAdmin',
  'SecurityAuditor',
] as const;

export const CUSTOMER_TENANT_ROLE_NAMES = [
  'Admin',
  'Manager',
  'Supervisor',
  'Accountant',
  'WarehouseStaff',
  'Viewer',
] as const;

export const isPlatformRoleName = (roleName?: string | null): boolean => {
  const trimmed = (roleName ?? '').trim();
  if (!trimmed) return false;
  return (
    (PLATFORM_ROLE_NAMES as readonly string[]).includes(trimmed) ||
    trimmed === 'SupportReadOnly' ||
    trimmed.startsWith('Platform')
  );
};

const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: 'Chủ nền tảng SaaS',
  BillingAdmin: 'Quản trị Thanh toán SaaS',
  SupportAdmin: 'Quản trị Hỗ trợ Kỹ thuật',
  SecurityAuditor: 'Kiểm toán Bảo mật',
  Admin: 'Quản trị công ty',
  Manager: 'Quản lý kho',
  WarehouseStaff: 'Nhân viên kho',
  Accountant: 'Kế toán kho',
  Supervisor: 'Giám sát & QC',
  Receiver: 'Nhân viên nhận hàng',
  PickerPacker: 'Nhân viên lấy/đóng gói',
  Viewer: 'Kiểm toán nội bộ',
};

export const roleLabel = (name: string): string => ROLE_LABELS[name] || `Vai trò ${name}`;

export const roleChipLabel = (roleName: string, count: number): string => {
  const chipNames: Record<string, string> = {
    SuperAdmin: 'Chủ nền tảng SaaS',
    BillingAdmin: 'Quản trị Thanh toán SaaS',
    SupportAdmin: 'Quản trị Hỗ trợ Kỹ thuật',
    SecurityAuditor: 'Kiểm toán Bảo mật',
    Admin: 'Quản trị công ty',
    Manager: 'Quản lý kho',
    Supervisor: 'Giám sát & QC',
    Accountant: 'Kế toán kho',
    WarehouseStaff: 'Nhân viên kho',
    Viewer: 'Kiểm toán nội bộ',
    Receiver: 'Nhân viên nhận hàng',
    PickerPacker: 'Nhân viên lấy/đóng gói',
  };
  const label = chipNames[roleName] || roleLabel(roleName);
  return `${label} (${count})`;
};

const USER_DISPLAY_NAMES_VI: Record<string, string> = {
  qa_admin: 'Chủ dự án (SuperAdmin)',
  'qa system admin': 'Chủ dự án (SuperAdmin)',
  qa_admin_local: 'Quản trị viên Nội bộ',
  qa_billing_admin: 'Quản trị Cước phí SaaS',
  qa_support_admin: 'Chuyên viên Hỗ trợ Kỹ thuật',
  qa_security_auditor: 'Kiểm toán An toàn Thông tin',
  tenant_admin: 'Quản trị viên Đại Phát',
  qa_manager_wh01: 'Quản lý kho WH01',
  'qa manager wh01': 'Quản lý kho WH01',
  qa_supervisor_wh01: 'Giám sát & QC WH01',
  'qa supervisor wh01': 'Giám sát & QC WH01',
  qa_accountant: 'Kế toán kho Đại Phát',
  qa_staff_wh01: 'Nhân viên kho WH01',
  'qa staff wh01': 'Nhân viên kho WH01',
  qa_viewer_wh01: 'Kiểm toán nội bộ WH01',
  'qa viewer wh01': 'Kiểm toán nội bộ WH01',
};

export const formatUserDisplayNameVi = (
  rawUsernameOrLabel?: string | null,
  fallbackRoleName?: string | null,
): string => {
  const trimmed = (rawUsernameOrLabel ?? '').trim();
  if (!trimmed) {
    if (fallbackRoleName === 'SuperAdmin') return 'Chủ dự án (SuperAdmin)';
    if (fallbackRoleName === 'Admin') return 'Quản trị viên Đại Phát';
    if (fallbackRoleName === 'WarehouseStaff') return 'Nhân viên kho WH01';
    return fallbackRoleName ? roleLabel(fallbackRoleName) : 'Người dùng hệ thống';
  }
  const normalized = trimmed.toLowerCase();
  if (USER_DISPLAY_NAMES_VI[normalized]) {
    return USER_DISPLAY_NAMES_VI[normalized];
  }
  const cleaned = trimmed
    .replace(/\s*\((?:qa_[a-z0-9_]+|tenant_admin)\)/gi, '')
    .replace(/^(?:qa_admin|tenant_admin|qa_[a-z0-9_]+)\s*[—-]\s*/i, (_, match) => match && '')
    .trim();
  if (cleaned && cleaned !== trimmed) {
    if (/quản trị viên hệ thống/i.test(cleaned) && fallbackRoleName === 'SuperAdmin') {
      return 'Chủ dự án (SuperAdmin)';
    }
    return cleaned;
  }
  if (/^quản trị viên hệ thống$/i.test(trimmed) && fallbackRoleName === 'SuperAdmin') {
    return 'Chủ dự án (SuperAdmin)';
  }
  return trimmed;
};

export const formatMinLengthCounterVi = (currentLength: number, minRequired = 10): string => {
  const safeLen = Math.max(0, Math.floor(currentLength));
  if (safeLen >= minRequired) {
    return `Đã nhập ${safeLen} ký tự (Đạt yêu cầu ≥ ${minRequired} ký tự)`;
  }
  return `Còn thiếu ${minRequired - safeLen} ký tự (Tối thiểu ${minRequired} ký tự)`;
};
export const permissionError = (error: unknown, fallback = 'Không thể xử lý yêu cầu. Vui lòng thử lại.') => {
  const status = axios.isAxiosError(error) ? error.response?.status : (error as { response?: { status?: number } })?.response?.status;
  if (status === 409) {
    const code = extractConflictCode(error);
    if (code && CONFLICT_ERROR_MESSAGES[code]) return CONFLICT_ERROR_MESSAGES[code];
    return 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.';
  }
  return ({ 401: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.', 403: 'Bạn không có quyền thực hiện thao tác này.',
    404: 'Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.', 409: 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.' } as Record<number, string>)[status || 0] || fallback;
};
