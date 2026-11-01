export type PickingLine = {
  id: number;
  allocationId: number;
  allocationCode: string;
  productId: number;
  productCode: string;
  productName: string;
  sourceLocationId: number;
  sourceLocationCode: string;
  sourceLocationName: string;
  requestedQuantity: number;
  pickedQuantity: number;
  remainingQuantity: number;
  sequence: number;
  status: string;
};

export type ShortPick = {
  id: number;
  pickingTaskLineId: number;
  expectedQuantity: number;
  pickedQuantity: number;
  shortageQuantity: number;
  reason: string;
  resolutionType?: string;
  status: string;
  createdAt: string;
  resolvedAt?: string;
  resolutionNote?: string;
};

export type PickingTask = {
  id: number;
  taskCode: string;
  sourceType: string;
  sourceId?: number;
  sourceCode?: string;
  warehouseId: number;
  warehouseName: string;
  pickingType: string;
  status: string;
  priority: number;
  assignedUserId?: number;
  assignedUserName?: string;
  requestedQuantity: number;
  pickedQuantity: number;
  remainingQuantity: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  rowVersion?: string;
  lines?: PickingLine[];
  shortPicks?: ShortPick[];
};

export type WaveGroup = {
  waveCode: string;
  warehouseId: number;
  warehouseName: string;
  pickingType: string;
  priority: number;
  taskCount: number;
  openTaskCount: number;
  totalRequestedQuantity: number;
  totalPickedQuantity: number;
  totalRemainingQuantity: number;
  taskCodes: string[];
};

export type PutToWallSlot = {
  slotCode: string;
  toteCode: string;
  orderCode: string;
  productCode: string;
  sortedQty: number;
  requiredQty: number;
  status: string;
  packingSessionCode?: string;
};

export type AlternateBinItem = {
  locationCode: string;
  lotNumber: string;
  expiryDate: string;
  availableQty: number;
};

export const states: Record<string, string> = {
  Open: 'Chưa phân công',
  Assigned: 'Đã phân công',
  InProgress: 'Đang lấy hàng',
  ShortPick: 'Thiếu hàng',
  Resolved: 'Đã xử lý thiếu',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
};

export const lineStates: Record<string, string> = {
  Open: 'Chưa bắt đầu',
  InProgress: 'Đang lấy',
  Picked: 'Đã lấy đủ',
  ShortPick: 'Thiếu hàng',
  Resolved: 'Đã xử lý',
};

export const pickingTypeLabels: Record<string, string> = {
  Standard: 'Tiêu chuẩn',
  STANDARD: 'Tiêu chuẩn',
  Single: 'Đơn lẻ',
  SINGLE: 'Đơn lẻ',
  SingleOrder: 'Đơn lẻ',
  SINGLEORDER: 'Đơn lẻ',
  SINGLE_ORDER: 'Đơn lẻ',
  Wave: 'Theo đợt',
  WAVE: 'Theo đợt',
  Batch: 'Theo lô',
  BATCH: 'Theo lô',
};

export const sourceTypeLabels: Record<string, string> = {
  ExportReceipt: 'Phiếu xuất kho',
  SalesOrder: 'Đơn bán hàng',
  StockTransfer: 'Phiếu điều chuyển',
};

export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  status === 'Completed' || status === 'Picked'
    ? 'success'
    : status === 'ShortPick'
      ? 'danger'
      : status === 'InProgress' || status === 'Assigned'
        ? 'warning'
        : 'neutral';

export const failureMessage = (e: unknown) => {
  const response = (e as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  const code = response?.data?.code;
  if (code === 'PICK_WRONG_LOCATION') return 'Sai vị trí lấy hàng. Hãy quét đúng location được phân công.';
  if (code === 'PICK_WRONG_PRODUCT') return 'Sai sản phẩm. Hãy quét đúng SKU/barcode của dòng Picking.';
  if (code === 'PICK_WRONG_LOT' || code === 'PICK_WRONG_SERIAL') return response?.data?.message ?? 'Lot/Serial chưa hợp lệ.';
  if (code === 'PICK_SHORT') return response?.data?.message ?? 'Short Pick cần được xử lý trước khi tiếp tục.';
  if (response?.status === 403) return 'Bạn không có quyền thực hiện thao tác này hoặc task đã được giao cho người khác.';
  if (response?.status === 404) return 'Không tìm thấy nhiệm vụ Picking hoặc bạn không có quyền truy cập.';
  if (response?.status === 409) return response.data?.message ?? 'Dữ liệu Picking đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message ?? 'Không thể xử lý nhiệm vụ Picking.';
};

export const telemetryLabels: Record<string, string> = {
  AMR_ASRS_CONVEYOR_SYNCHRONIZED: 'Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô)',
  RFID_GATE_AND_COLD_CHAIN_SENSORS_ONLINE: 'Đã kiểm tra điều kiện bảo quản theo phân khu kệ',
  VOICE_PTL_HANDS_FREE_DIRECTED_ACTIVE: 'Kết hợp ưu tiên lô cận hạn FEFO',
};

export const defaultPutToWallSlots: PutToWallSlot[] = Array.from({ length: 8 }, (_, i) => ({
  slotCode: `SLOT-0${i + 1}`,
  toteCode: `TOTE-0${i + 1}`,
  orderCode: `SO-2026-010${i + 1}`,
  productCode: i % 2 === 0 ? 'QA-E2E-001' : 'QA-LOT-002',
  sortedQty: i < 2 ? 4 : i === 2 ? 2 : 0,
  requiredQty: 4,
  status: i < 2 ? 'COMPLETED' : i === 2 ? 'SORTING' : 'WAITING',
  packingSessionCode: i < 2 ? `PACK-PTW-0${i + 1}` : '',
}));
