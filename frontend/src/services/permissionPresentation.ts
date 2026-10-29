import axios from 'axios';

const resources: Record<string, string> = {
  approval: 'phê duyệt phiếu nhập',
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
};


export const PERMISSION_DESCRIPTIONS: Record<string, string> = {
  'inventory.cost.read': 'Cho phép xem đơn giá mua/bán, giá vốn bình quân (MAC) và giá trị chênh lệch kiểm kê trên các chứng từ và báo cáo kho.',
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

export const permissionDescription = (code: string, fallbackDescription?: string): string =>
  PERMISSION_DESCRIPTIONS[code] || fallbackDescription || 'Quyền truy cập hệ thống';

export const permissionLabel = (code: string): string => {
  if (code === 'permission.assign') return 'Cấp và thu hồi quyền truy cập';
  if (code === 'inventory.cost.read') return 'Xem đơn giá, giá vốn và giá trị tồn kho';
  const [resource, action] = code.split('.');
  return resources[resource] && actions[action] ? `${actions[action]} ${resources[resource]}` : 'Quyền chưa có diễn giải';
};
export const roleLabel = (name: string) => ({ Admin: 'Quản trị viên', Manager: 'Quản lý', Viewer: 'Người xem', WarehouseStaff: 'Nhân viên kho' }[name] || 'Vai trò khác');
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
