import { conflictErrorMessage } from '../../services/permissionPresentation';

export type Item = {
  id: number; productCode: string; productName: string; inventoryStatus: string;
  sourceLocationCode: string; operationUnitCode: string; baseUnitCode: string;
  requiredOperationQuantity: number; requiredBaseQuantity: number;
  movedBaseQuantity: number; remainingBaseQuantity: number;
};

export type Task = {
  id: number; warehouseId?: number; receiptCode: string; warehouseName: string;
  status: string; taskType?: string; sourceType?: string; assignedUserId: number | null;
  requiredBaseQuantity: number; movedBaseQuantity: number;
  isCrossDock?: boolean; crossDockTargetSalesOrderId?: number | null;
  crossDockTargetExportReceiptId?: number | null; crossDockTargetBackorderId?: number | null;
  crossDockAllocatedQty?: number; rowVersion: string; exceptionReason?: string; items: Item[];
};

export type Location = {
  id: number; code: string; name: string; locationType: string; isPickable: boolean;
};

export type AbcInterleavingData = {
  evaluationId?: string; evaluatedSkuCount?: number; classACount?: number;
  classBCount?: number; classCCount?: number; reslottingRecommendationCount?: number;
  interleavedTaskPairCount?: number; averageDeadheadSavedPercent?: number; statusLabel?: string;
  reslottingRecommendations?: Array<{
    productCode: string; productName: string; abcClass: string; fromBinCode: string;
    recommendedBinCode: string; recommendedZoneCode: string;
    estimatedTravelSavingMeters: number; reasonVi: string;
  }>;
  interleavedTaskPairs?: Array<{
    cycleCode: string; putawayProductCode: string; putawayTargetBin: string;
    pickingProductCode: string; pickingSourceBin: string;
    interleavedRouteVi: string; estimatedDeadheadSavedPercent: number;
  }>;
};

export const states: Record<string, string> = {
  Open: 'Chưa phân công',
  Assigned: 'Đã phân công',
  InProgress: 'Đang thực hiện',
  Completed: 'Đã hoàn thành',
  Cancelled: 'Đã hủy',
  Exception: 'Cần xử lý',
  CancelPendingPutback: 'Chờ cất trả hàng hủy về kệ',
  PutbackTask: 'Cất trả hàng hủy về kệ',
};

export const taskTypes: Record<string, string> = {
  PutawayTask: 'Cất hàng nhập kho',
  PutbackTask: 'Cất trả hàng hủy về kệ',
  CancelPendingPutback: 'Chờ cất trả hàng hủy về kệ',
};

export const inventory: Record<string, string> = {
  Available: 'Có thể sử dụng',
  Damaged: 'Hư hỏng',
  Rejected: 'Hàng bị từ chối',
};

export const PUTAWAY_RANKING_CODES = [
  '1_EXACT_SKU_AFFINITY',
  '2_STORAGE_GROUP_COMPATIBILITY',
  '3_REMAINING_CAPACITY_SCORE',
  '4_PROXIMITY_PICK_FACE',
  '5_FEFO_LOT_CONSOLIDATION',
] as const;

export const PUTAWAY_HARD_GUARD_CODES = [
  'ACTIVE_BIN_ONLY',
  'UNBLOCKED_BIN_ONLY',
  'NO_STOCKTAKE_FREEZE',
  'CAPACITY_WEIGHT_VOLUME_PALLET_GUARD',
] as const;

export const PUTAWAY_RULE_LABELS: Record<string, string> = {
  '1_EXACT_SKU_AFFINITY': '1. Ưu tiên cùng SKU',
  '2_STORAGE_GROUP_COMPATIBILITY': '2. Tương thích nhóm lưu trữ',
  '3_REMAINING_CAPACITY_SCORE': '3. Đủ sức chứa còn lại',
  '3_REMAINING_CAPACITY_FIT': '3. Đủ sức chứa còn lại',
  '4_PROXIMITY_PICK_FACE': '4. Gần vị trí lấy hàng',
  '4_PICK_FACE_PROXIMITY': '4. Gần vị trí lấy hàng',
  '5_FEFO_LOT_CONSOLIDATION': '5. Gom lô FEFO',
  ACTIVE_BIN_ONLY: 'Kệ đang hoạt động',
  UNBLOCKED_BIN_ONLY: 'Kệ không bị khóa',
  NO_STOCKTAKE_FREEZE: 'Không vướng khóa kiểm kê',
  NO_INVENTORY_LOCK: 'Không vướng khóa kiểm kê',
  CAPACITY_WEIGHT_VOLUME_PALLET_GUARD: 'Đảm bảo tải trọng / thể tích / số Pallet',
};

export const formatPutawayRuleLabel = (code: string) =>
  PUTAWAY_RULE_LABELS[code] ?? code.replace(/_/g, ' ').toLowerCase().replace(/\b\w/g, c => c.toUpperCase());

export const message = (e: unknown) => {
  const response = (e as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  const status = response?.status;
  const code = response?.data?.code;
  if (code === 'PUTAWAY_WRONG_BIN' || code === 'PUTAWAY_WRONG_PRODUCT')
    return response?.data?.message ?? 'Mã vạch vị trí hoặc sản phẩm không khớp.';
  return status === 403
    ? 'Bạn không có quyền thực hiện thao tác này.'
    : status === 404
      ? 'Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.'
      : status === 409
        ? conflictErrorMessage(e, 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.')
        : 'Không thể lưu dữ liệu. Vui lòng thử lại.';
};
