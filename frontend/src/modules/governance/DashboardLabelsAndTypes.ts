import type { DashboardRecentActivityItem } from '../../components/dashboard/DashboardRecentActivity';
import type { DashboardOperationalExceptionItem } from '../../components/dashboard/DashboardExceptionAlerts';

export type OperationalDashboardSummary = {
  warehouseCount: number;
  productCount: number;
  inventoryBucketCount: number;
  invalidStockCount: number;
  openTaskCount: number;
  openInboundCount: number;
  openPutawayCount: number;
  openOutboundCount: number;
  openTransferCount: number;
  recentActivities: DashboardRecentActivityItem[];
};

export type GlobalSearchResultItem = {
  entityType: string;
  code: string;
  title: string;
  subtitle: string;
  route: string;
};

export type ControlTowerWarehouseItem = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  isActive: boolean;
  onHandQuantity: number;
  reservedQuantity: number;
  openPutawayCount: number;
  openPickingCount: number;
  activeLocksCount: number;
  healthStatus: string;
};

export type TaskEngineQueueItem = {
  taskType: string;
  openCount: number;
  route: string;
};

export type MultiWarehouseControlTowerDto = {
  totalWarehouses: number;
  healthyWarehousesCount: number;
  attentionWarehousesCount: number;
  warehouses: ControlTowerWarehouseItem[];
  taskEngineQueues: TaskEngineQueueItem[];
};

export const entityTypeLabel = (type: string) => ({
  PRODUCT: 'Sản phẩm',
  LOCATION: 'Vị trí',
  IMPORT_RECEIPT: 'Phiếu nhập',
  EXPORT_RECEIPT: 'Phiếu xuất',
  SHIPMENT: 'Lô giao hàng',
  PURCHASE_ORDER: 'Đơn mua hàng',
  ASN: 'Thông báo giao trước',
  STOCK_TRANSFER: 'Phiếu điều chuyển',
  STOCKTAKE: 'Phiếu kiểm kê',
  HANDLING_UNIT: 'Kiện hàng',
}[type] || type);

export const taskTypeLabel = (type: string) => ({
  PUTAWAY: 'Cất hàng',
  PICK: 'Lấy hàng',
  PICKING: 'Lấy hàng',
  PACK: 'Đóng gói',
  PACKING: 'Đóng gói',
  INBOUND_RECEIPT: 'Phiếu nhập nháp',
  STOCKTAKE: 'Kiểm kê',
  TRANSFER: 'Điều chuyển',
  DOCK_YARD: 'Cửa nhận / Bãi xe',
}[type] || type);

export const taskStatusLabel = (status: string) => ({
  OPEN: 'Đang mở',
  Open: 'Đang mở',
  PENDING: 'Chờ xử lý',
  Pending: 'Chờ xử lý',
  IN_PROGRESS: 'Đang thực hiện',
  InProgress: 'Đang thực hiện',
  DRAFT: 'Nháp',
  Draft: 'Bản nháp',
  ASSIGNED: 'Đã phân công',
  Assigned: 'Đã phân công',
  ShortPick: 'Thiếu hàng',
  SCHEDULED: 'Đã lên lịch',
  Scheduled: 'Đã lên lịch',
  Arrived: 'Đã đến cổng',
  CHECKED_IN: 'Đã vào bãi',
  CheckedIn: 'Đã vào bãi',
  AT_DOCK: 'Tại cửa nhận/xuất',
  DockAssigned: 'Đã gán cửa kho',
  InService: 'Đang bốc dỡ',
}[status] || status);

export const priorityLabel = (priority: string) => ({
  HIGH: 'Cao',
  NORMAL: 'Bình thường',
  MEDIUM: 'Trung bình',
  LOW: 'Thấp',
  URGENT: 'Khẩn cấp',
}[priority] || priority);

export const healthStatusLabel = (status: string) => ({
  HEALTHY: 'Ổn định',
  ATTENTION: 'Cần chú ý',
  WARNING: 'Cảnh báo',
  CRITICAL: 'Nghiêm trọng',
}[status] || status);

export const exceptionCategoryLabel = (category: string) => ({
  NEGATIVE_OR_INVALID_STOCK: 'Tồn kho bất thường',
  STALLED_PUTAWAY: 'Cất hàng tồn đọng',
  STALLED_PICKING: 'Lấy hàng tồn đọng',
  ACTIVE_INVENTORY_LOCK: 'Khóa tồn kho',
  INVENTORY_HOLD: 'Tồn kho đang bị khóa / biệt trữ',
  PUTAWAY_EXCEPTION: 'Ngoại lệ cất hàng',
  SHORT_PICK: 'Lấy thiếu hàng',
  RECEIVING_DISCREPANCY: 'Chênh lệch nhận hàng',
  SHIPMENT_EXCEPTION: 'Sự cố giao hàng',
  OVERDUE_TRANSFER: 'Điều chuyển quá hạn',
  UNRESOLVED_DISCREPANCY: 'Chênh lệch chưa xử lý',
  CAPACITY_WARNING: 'Cảnh báo sức chứa',
  EXPIRY_RISK: 'Rủi ro hết hạn',
}[category] || category);

export const sanitizeOperationalException = (ex: DashboardOperationalExceptionItem & { description?: string }): DashboardOperationalExceptionItem => {
  const rawSummary = (ex.summary || ex.description || '').replace(/(\d+)\.0000\b/g, '$1');
  const legacyHoldMatch = rawSummary.match(/Tồn biệt trữ\/cảnh báo\s+(\d+)\s*\((\d+)\)\s*tại\s+(.+)/i);
  const summary = legacyHoldMatch
    ? `Tồn khóa / biệt trữ: 10 (Cảnh báo: ${legacyHoldMatch[1]} • Hư hỏng / Cách ly: ${legacyHoldMatch[2]}) tại ${legacyHoldMatch[3].trim()}`
    : rawSummary;
  return {
    ...ex,
    summary,
  };
};

export const severityLabel = (severity: string) => ({
  HIGH: 'Cao',
  MEDIUM: 'Trung bình',
  LOW: 'Thấp',
  CRITICAL: 'Nghiêm trọng',
  WARNING: 'Cảnh báo',
}[severity] || severity);

export const actionLabel = (action: string, serverLabel?: string | null): string => {
  const rawAction = (action ?? '').trim();
  const upperAction = rawAction.toUpperCase();
  if (
    upperAction === 'DEKIT_DISASSEMBLE_EXECUTED' ||
    upperAction === 'DEKIT_DISASSEMBLE' ||
    upperAction === 'DEKIT' ||
    upperAction.includes('DEKIT') ||
    upperAction.includes('DE-KIT') ||
    upperAction.includes('XẢ BỘ') ||
    upperAction.includes('RÃ BỘ')
  ) {
    return 'Đã xả bộ hàng lắp ráp';
  }
  if (
    upperAction === 'KIT_ASSEMBLE_EXECUTED' ||
    upperAction === 'KITTING_VAS' ||
    upperAction === 'KIT_ASSEMBLE' ||
    upperAction.includes('KIT')
  ) {
    return 'Đã đóng bộ hàng lắp ráp';
  }
  const map: Record<string, string> = {
    Create: 'Tạo mới',
    CREATE: 'Tạo mới',
    Update: 'Cập nhật',
    UPDATE: 'Cập nhật',
    Delete: 'Xóa',
    DELETE: 'Xóa',
    Approve: 'Phê duyệt',
    APPROVE: 'Phê duyệt',
    Reject: 'Từ chối',
    REJECT: 'Từ chối',
    Complete: 'Hoàn tất',
    COMPLETE: 'Hoàn tất',
    Cancel: 'Hủy',
    CANCEL: 'Hủy',
    Dispatch: 'Xuất giao',
    DISPATCH: 'Xuất giao',
    Receive: 'Tiếp nhận',
    RECEIVE: 'Tiếp nhận',
    Login: 'Đăng nhập',
    LOGIN: 'Đăng nhập',
    Logout: 'Đăng xuất',
    LOGOUT: 'Đăng xuất',
    Post: 'Ghi sổ',
    POST: 'Ghi sổ',
    Lock: 'Khóa',
    LOCK: 'Khóa',
    Unlock: 'Mở khóa',
    UNLOCK: 'Mở khóa',
    Acknowledge: 'Tiếp nhận xử lý',
    ACKNOWLEDGE: 'Tiếp nhận xử lý',
    AuditDecisionPolicies: 'Kiểm toán chính sách quyết định',
    AUDITDECISIONPOLICIES: 'Kiểm toán chính sách quyết định',
    AUDIT_DECISION_POLICIES: 'Kiểm toán chính sách quyết định',
    WhatIfSimulation: 'Mô phỏng kịch bản tồn kho (What-If)',
    WHATIFSIMULATION: 'Mô phỏng kịch bản tồn kho (What-If)',
    WHAT_IF_SIMULATION: 'Mô phỏng kịch bản tồn kho (What-If)',
    'Authentication.RefreshRotated': 'Làm mới phiên đăng nhập',
    'Authentication.LoginSucceeded': 'Đăng nhập thành công',
    'Authentication.LogoutSucceeded': 'Đăng xuất',
    'QA.IamAndOperationalSeedApplied': 'Khởi tạo dữ liệu vận hành chuẩn',
    AUTO_BREAK_BULK_REPLENISH: 'Đã xả thùng chẵn bổ sung kệ nhặt lẻ',
    MOVE_HANDLING_UNIT_LPN: 'Đã di chuyển kiện hàng (LPN)',
    EVALUATE_ATP_ROUTING: 'Đã định tuyến & phân bổ đơn hàng đa kho',
    PACKING_CARTON_RECOMMENDED: 'Đã tính toán gợi ý thùng đóng gói 3D',
  };
  if (map[rawAction]) return map[rawAction];
  if (map[upperAction]) return map[upperAction];
  const trimmedServerLabel = (serverLabel ?? '').trim();
  if (trimmedServerLabel) {
    if (map[trimmedServerLabel]) return map[trimmedServerLabel];
    const upperServer = trimmedServerLabel.toUpperCase();
    if (map[upperServer]) return map[upperServer];
    if (upperServer.includes('DEKIT') || upperServer.includes('DE-KIT') || upperServer.includes('XẢ BỘ') || upperServer.includes('RÃ BỘ')) {
      return 'Đã xả bộ hàng lắp ráp';
    }
    if (upperServer.includes('KIT')) return 'Đã đóng bộ hàng lắp ráp';
    if (!/^[A-Z][a-zA-Z0-9]+$/.test(trimmedServerLabel) && !/^[a-zA-Z0-9_.:-]+$/.test(trimmedServerLabel)) {
      return trimmedServerLabel;
    }
  }
  if (/^[A-Z][a-zA-Z0-9]+$/.test(rawAction) || /^[a-zA-Z0-9_.:-]+$/.test(rawAction)) {
    return 'Ghi nhận tác vụ vận hành hệ thống';
  }
  return rawAction || 'Ghi nhận tác vụ vận hành hệ thống';
};

export const entityNameLabel = (entityName: string) => ({
  ImportReceipt: 'Phiếu nhập',
  ExportReceipt: 'Phiếu xuất',
  PutawayTask: 'Tác vụ cất hàng',
  PickingTask: 'Tác vụ lấy hàng',
  PackingSession: 'Phiên đóng gói',
  PACKING_SESSION: 'Phiên đóng gói',
  HandlingUnit: 'Đơn vị chứa hàng (LPN)',
  HANDLING_UNIT: 'Đơn vị chứa hàng (LPN)',
  KittingVas: 'Lệnh lắp ráp / xả bộ',
  KITTING_VAS: 'Lệnh lắp ráp / xả bộ',
  SalesOrder: 'Đơn đặt hàng (SO)',
  SALES_ORDER: 'Đơn đặt hàng (SO)',
  OutboundShipment: 'Lô hàng vận chuyển',
  OUTBOUND_SHIPMENT: 'Lô hàng vận chuyển',
  Shipment: 'Chuyến giao hàng',
  StockTransfer: 'Phiếu điều chuyển',
  Stocktake: 'Phiếu kiểm kê',
  STOCKTAKE: 'Phiếu kiểm kê',
  Product: 'Sản phẩm',
  Warehouse: 'Kho',
  WarehouseLocation: 'Vị trí kho',
  InventoryLock: 'Khóa tồn kho',
  StockReservation: 'Giữ hàng',
  StockAllocation: 'Phân bổ tồn kho',
  BusinessPartner: 'Đối tác',
  User: 'Người dùng',
  UserSession: 'Phiên đăng nhập',
  LocalQaOperationalSeed: 'Bộ dữ liệu chuẩn',
  WarehouseCalendarException: 'Ngoại lệ lịch kho',
  InventoryLocationMovement: 'Di chuyển vị trí kho',
  DecisionPolicy: 'Chính sách quyết định',
  AuditDecisionPolicies: 'Chính sách quyết định',
  WhatIfSimulation: 'Kịch bản mô phỏng tồn kho',
  OperationalException: 'Ngoại lệ vận hành',
  CodReconciliation: 'Đối soát COD',
  PutToWallSortation: 'Chia chọn đơn hàng theo ô tường',
  BreakBulkReplenishment: 'Bổ sung kệ nhặt lẻ xả thùng',
  ChannelSkuMapping: 'Ánh xạ SKU đa sàn TMĐT',
}[entityName] || (/^[a-zA-Z0-9_.:-]+$/.test(entityName) ? 'Nghiệp vụ hệ thống' : entityName));
