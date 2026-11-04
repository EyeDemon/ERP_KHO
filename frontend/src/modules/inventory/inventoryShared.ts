export interface InventoryStockDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  quantity: number;
  onHandQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  lastUpdated: string;
}

export interface InventoryTransactionHistoryDto {
  id: number;
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  transactionType: string;
  inventoryStatus?: string;
  fromInventoryStatus?: string | null;
  toInventoryStatus?: string | null;
  lotId?: number | null;
  lotNumber?: string | null;
  expiryDate?: string | null;
  serialId?: number | null;
  serialNumber?: string | null;
  quantity: number;
  referenceId: number | null;
  referenceType: string | null;
  transactionDate: string;
  createdBy: number;
  createdByName: string;
  note: string | null;
}

export interface InventoryInOutDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  warehouseId: number;
  warehouseName: string;
  openingQuantity: number;
  inQuantity: number;
  outQuantity: number;
  closingQuantity: number;
}

export interface InventoryExpiryAgingDto {
  inventoryStockId: number;
  warehouseId: number;
  warehouseName: string;
  locationId?: number | null;
  locationCode: string;
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  status: string;
  lotId?: number | null;
  lotNumber?: string | null;
  manufactureDate?: string | null;
  expiryDate?: string | null;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  daysToExpiry?: number | null;
  ageDays: number;
  expiryStatus: 'EXPIRED' | 'NEAR_EXPIRY' | 'GOOD' | 'NO_EXPIRY';
}

export interface InventoryAnalyticsSkuItemDto {
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  onHandQuantity: number;
  periodOutQuantity: number;
  periodInQuantity: number;
  turnoverRate: number;
  daysOnHand?: number | null;
  velocityClass: string;
}

export interface InventoryAnalyticsSummaryDto {
  windowDays: number;
  inboundOutboundSla: {
    completedReceiptsCount: number;
    avgInboundLeadTimeHours: number;
    openPutawayBacklogCount: number;
    dispatchedExportsCount: number;
    avgOutboundLeadTimeHours: number;
    openPickingBacklogCount: number;
  };
  stocktakeAccuracy: {
    approvedStocktakesCount: number;
    totalLinesCounted: number;
    exactMatchLinesCount: number;
    varianceLinesCount: number;
    accuracyRatePercent: number;
    totalAbsoluteVarianceQty: number;
  };
  turnoverAndDaysOnHand: InventoryAnalyticsSkuItemDto[];
  totalAvailableStock?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
}

export interface WarehouseOption {
  id: number;
  name: string;
  isActive: boolean;
}

export interface WhatIfSimulationResult {
  demandMultiplier: number;
  totalAvailable: number;
  simulatedDemand: number;
  projectedNetBalance: number;
  incomingPoQty?: number;
  incomingPurchaseOrderQty?: number;
  scenarioStatus: string;
  immutableSnapshotGuarantee: boolean;
}

export interface DecisionPolicyAuditResult {
  policyRegistryId: string;
  forecastBiasMapeStatus: string;
  demandAnomalyDetector: string;
  inventoryRiskScoreStatus: string;
  humanInTheLoopBoundary: string;
  status: string;
}

export const formatUnitName = (unit?: string | null) =>
  !unit || unit === 'QA_UNIT' || unit === 'QA-PCS' ? 'THÙNG' : unit;

export const referenceTypeLabel = (value?: string | null) =>
  ({
    ImportReceipt: 'Phiếu nhập kho',
    GoodsReceipt: 'Phiếu nhập kho',
    Receipt: 'Phiếu nhập kho',
    ExportReceipt: 'Phiếu xuất kho',
    StockTransfer: 'Phiếu điều chuyển',
    Stocktake: 'Phiếu kiểm kê',
    Shipment: 'Chuyến giao hàng',
    InventoryReversal: 'Phiếu đảo giao dịch',
    InventoryAdjustment: 'Phiếu điều chỉnh',
    InventoryMove: 'Phiếu di chuyển',
    Return: 'Phiếu trả hàng',
    PickTask: 'Tác vụ lấy hàng',
    HandlingUnitPalletSeed: 'Khởi tạo kiện LPN',
    HandlingUnitLpnMove: 'Di chuyển kiện LPN',
    HandlingUnitMove: 'Di chuyển kiện LPN',
    LpnMove: 'Di chuyển kiện LPN',
    VAS_DEKIT: 'Lệnh rã bộ KIT',
    QASeedReconcile: 'Đối soát sổ cái',
    InternalMove: 'Di chuyển nội bộ',
    StocktakeAdjustment: 'Điều chỉnh kiểm kê',
    QualityDispositionRtv: 'Xuất trả NCC',
    QualityDispositionScrap: 'Thanh hủy hàng lỗi',
    Putaway: 'Cất hàng',
    PutawayTask: 'Tác vụ cất hàng',
    Picking: 'Lấy hàng',
    Packing: 'Đóng gói',
    StatusTransition: 'Chuyển trạng thái',
    ReceivingDiscrepancy: 'Xử lý sai lệch nhận hàng',
    QualityInspection: 'Kiểm định chất lượng',
  }[(value ?? '').replace(/^\[|\]$/g, '').trim()] ?? (value ?? '').replace(/\[?HandlingUnitLpnMove\]?/gi, 'Di chuyển kiện LPN'));

export const transactionTypeLabel = (value: string) =>
  ({
    Adjustment: 'Điều chỉnh tồn kho',
    InternalMove: 'Di chuyển vị trí nội bộ',
    HandlingUnitLpnMove: 'Di chuyển kiện LPN',
    HandlingUnitPalletSeed: 'Khởi tạo kiện LPN',
    Putaway: 'Cất hàng lên kệ',
    Pick: 'Lấy hàng xuất kho',
    Picking: 'Lấy hàng xuất kho',
    Pack: 'Đóng gói',
    Packing: 'Đóng gói',
    StocktakeAdjustment: 'Điều chỉnh kiểm kê',
    Import: 'Nhập kho',
    Export: 'Xuất kho',
    AdjustmentIncrease: 'Điều chỉnh tăng',
    AdjustmentDecrease: 'Điều chỉnh giảm',
    TransferIn: 'Nhập điều chuyển',
    TransferOut: 'Xuất điều chuyển',
    TransferAdjustment: 'Điều chỉnh điều chuyển',
    StatusChange: 'Đổi trạng thái',
    Move: 'Di chuyển vị trí nội bộ',
    Reversal: 'Dấu mốc đảo giao dịch',
    Ship: 'Xuất giao hàng',
  }[value] ?? value);

export const inventoryStatusLabel = (value: string) =>
  ({
    AVAILABLE: 'Khả dụng',
    QC_HOLD: 'Chờ kiểm tra chất lượng',
    QUARANTINE: 'Cách ly',
    BLOCKED: 'Bị chặn',
    DAMAGED: 'Hư hỏng',
    REJECTED: 'Từ chối',
    EXPIRED: 'Hết hạn',
    RECALL_BLOCKED: 'Phong tỏa thu hồi',
    Available: 'Khả dụng',
    'Khả dụng': 'Khả dụng',
    QcHold: 'Tạm giữ chờ kiểm định',
    'Tạm giữ QC': 'Tạm giữ chờ kiểm định',
    'Tạm giữ chờ kiểm định': 'Tạm giữ chờ kiểm định',
    Quarantine: 'Cách ly',
    'Cách ly': 'Cách ly',
    Blocked: 'Bị chặn',
    'Bị chặn': 'Bị chặn',
    Damaged: 'Hư hỏng',
    'Hư hỏng': 'Hư hỏng',
    Rejected: 'Từ chối',
    'Từ chối': 'Từ chối',
    Expired: 'Đã hết hạn',
    'Đã hết hạn': 'Đã hết hạn',
    'Hết hạn': 'Đã hết hạn',
    RecallBlocked: 'Phong tỏa thu hồi',
    'Phong tỏa thu hồi': 'Phong tỏa thu hồi',
    'Khóa thu hồi': 'Phong tỏa thu hồi',
  }[value] ?? value);

export const inventoryStatusTone = (value: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  ['AVAILABLE', 'Available', 'Khả dụng'].includes(value)
    ? 'success'
    : ['QC_HOLD', 'QcHold', 'QUARANTINE', 'Quarantine', 'Cách ly', 'Tạm giữ QC', 'Tạm giữ chờ kiểm định', 'Chờ kiểm tra chất lượng'].includes(value)
      ? 'warning'
      : ['BLOCKED', 'Blocked', 'Bị chặn', 'DAMAGED', 'Damaged', 'Hư hỏng', 'EXPIRED', 'Expired', 'Đã hết hạn', 'Hết hạn', 'RECALL_BLOCKED', 'RecallBlocked', 'Phong tỏa thu hồi', 'Khóa thu hồi', 'REJECTED', 'Rejected', 'Từ chối'].includes(value)
        ? 'danger'
        : 'neutral';

export const parseAnalyticsSummaryResponse = (raw: Record<string, unknown> | null | undefined, fallbackDays: string): InventoryAnalyticsSummaryDto | null => {
  if (!raw) return null;
  const parsedDays = Number(raw.windowDays ?? fallbackDays) || 30;
  const rawItems: Array<Record<string, unknown>> = Array.isArray(raw.turnoverAndDaysOnHand)
    ? (raw.turnoverAndDaysOnHand as Array<Record<string, unknown>>)
    : Array.isArray(raw.turnoverItems)
      ? (raw.turnoverItems as Array<Record<string, unknown>>)
      : [];
  const sla = (raw.inboundOutboundSla ?? {}) as Record<string, unknown>;
  const acc = (raw.stocktakeAccuracy ?? {}) as Record<string, unknown>;
  const plan = (raw.planningSuite ?? {}) as Record<string, unknown>;
  const totalLines = Number(acc.totalLinesCounted ?? raw.stocktakeLineCount ?? 0);
  const exactLines = Number(acc.exactMatchLinesCount ?? raw.stocktakeAccurateLineCount ?? 0);
  return {
    windowDays: parsedDays,
    totalAvailableStock:
      Number(
        plan.currentAvailableStock ??
          plan.totalAvailableQty ??
          plan.totalAtpQuantity ??
          raw.currentAvailableStock ??
          raw.totalAvailableQty ??
          raw.totalAtpQuantity ??
          raw.totalAvailable ??
          0,
      ) || undefined,
    inboundOutboundSla: {
      completedReceiptsCount: Number(sla.completedReceiptsCount ?? raw.inboundPostedCount ?? 0),
      avgInboundLeadTimeHours: Number(sla.avgInboundLeadTimeHours ?? sla.avgInboundTurnaroundHours ?? raw.avgInboundTurnaroundHours ?? 0),
      openPutawayBacklogCount: Number(sla.openPutawayBacklogCount ?? raw.putawayOpenCount ?? 0),
      dispatchedExportsCount: Number(sla.dispatchedExportsCount ?? raw.outboundShippedCount ?? 0),
      avgOutboundLeadTimeHours: Number(sla.avgOutboundLeadTimeHours ?? sla.avgOutboundTurnaroundHours ?? raw.avgOutboundTurnaroundHours ?? 0),
      openPickingBacklogCount: Number(sla.openPickingBacklogCount ?? 0),
    },
    stocktakeAccuracy: {
      approvedStocktakesCount: Number(acc.approvedStocktakesCount ?? raw.stocktakeApprovedCount ?? 0),
      totalLinesCounted: totalLines,
      exactMatchLinesCount: exactLines,
      varianceLinesCount: Number(acc.varianceLinesCount ?? Math.max(0, totalLines - exactLines)),
      accuracyRatePercent: Number(acc.accuracyRatePercent ?? raw.stocktakeAccuracyRatePct ?? 100),
      totalAbsoluteVarianceQty: Number(acc.totalAbsoluteVarianceQty ?? 0),
    },
    turnoverAndDaysOnHand: rawItems.map((item) => ({
      productId: Number(item.productId ?? 0),
      productCode: String(item.productCode ?? ''),
      productName: String(item.productName ?? ''),
      unitName: String(item.unitName ?? 'ĐVT'),
      onHandQuantity: Number(item.onHandQuantity ?? item.currentOnHand ?? 0),
      periodInQuantity: Number(item.periodInQuantity ?? 0),
      periodOutQuantity: Number(item.periodOutQuantity ?? item.issuedLastPeriod ?? 0),
      turnoverRate: Number(item.turnoverRate ?? item.turnoverRatio ?? 0),
      daysOnHand: item.daysOnHand === null || item.daysOnHand === undefined ? null : Number(item.daysOnHand),
      velocityClass: String(item.velocityClass ?? item.movementClass ?? 'SLOW_MOVING'),
    })),
  };
};
