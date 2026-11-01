export type Bucket = {
  inventoryStockId: number;
  productId: number;
  productCode: string;
  productName: string;
  warehouseId: number;
  warehouseName: string;
  locationId?: number | null;
  locationCode?: string | null;
  inventoryStatus: string;
  lotId?: number | null;
  lotNumber?: string | null;
  expiryDate?: string | null;
  serialId?: number | null;
  serialNumber?: string | null;
  onHandQuantity: number;
  reservedQuantity: number;
};

export type Event = {
  transactionId: number;
  productId: number;
  productCode: string;
  productName: string;
  warehouseId: number;
  warehouseName: string;
  transactionType: string;
  inventoryStatus: string;
  fromInventoryStatus?: string | null;
  toInventoryStatus?: string | null;
  locationCode?: string | null;
  fromLocationCode?: string | null;
  toLocationCode?: string | null;
  lotNumber?: string | null;
  expiryDate?: string | null;
  serialNumber?: string | null;
  quantity: number;
  referenceType?: string | null;
  referenceId?: number | null;
  transactionDate: string;
  createdBy: number;
  createdByName: string;
  note?: string | null;
  reasonCode?: string | null;
  reversalOfTransactionId?: number | null;
  correctiveTransactionId?: number | null;
  reversalTransactionId?: number | null;
  isReversed: boolean;
};

export type RelatedDocument = {
  warehouseId: number;
  warehouseName: string;
  referenceType: string;
  referenceId: number;
  eventCount: number;
  firstTransactionDate: string;
  lastTransactionDate: string;
  lastTransactionId: number;
};

export type ReceiptExposure = {
  receiptId: number;
  warehouseId: number;
  receiptCode: string;
  postedQuantity: number;
  lastPostedAt: string;
  ledgerEventCount: number;
  lastTransactionId: number;
};

export type ShipmentExposure = {
  shipmentId: number;
  warehouseId: number;
  shipmentCode: string;
  shipmentStatus: string;
  dispatchedAt?: string | null;
  dispatchedQuantity: number;
  ledgerEventCount: number;
  lastTransactionId: number;
};

export type ShipmentPickingEvidence = {
  shipmentId: number;
  shipmentCode: string;
  warehouseId: number;
  packingSessionId: number;
  packingSessionCode: string;
  pickingTaskId: number;
  pickingTaskCode: string;
  pickingTaskLineId: number;
  allocationId: number;
  sourceLocationCode: string;
  pickedQuantity: number;
};

export type ShipmentHuEvidence = {
  shipmentId: number;
  warehouseId: number;
  pickingTaskLineId: number;
  rootHandlingUnitId: number;
  rootHandlingUnitCode: string;
  contentHandlingUnitId: number;
  contentHandlingUnitCode: string;
  contentHandlingUnitBarcode: string;
  parentHandlingUnitId?: number | null;
  hierarchyPath: string;
  packedQuantity: number;
};

export type ShipmentExportSource = {
  shipmentId: number;
  shipmentCode: string;
  warehouseId: number;
  pickingTaskId: number;
  exportReceiptId: number;
  exportReceiptCode: string;
  exportReceiptDispatchedAt?: string | null;
  exportReceiptProductQuantity: number;
};

export type Result = {
  currentBuckets: Bucket[];
  events: Event[];
  relatedDocuments?: RelatedDocument[];
  relatedDocumentsTruncated?: boolean;
  receiptExposures?: ReceiptExposure[];
  receiptExposuresTruncated?: boolean;
  shipmentExposures?: ShipmentExposure[];
  shipmentExposuresTruncated?: boolean;
  shipmentPickingEvidence?: ShipmentPickingEvidence[];
  shipmentPickingEvidenceTruncated?: boolean;
  shipmentHuEvidence?: ShipmentHuEvidence[];
  shipmentHuEvidenceTruncated?: boolean;
  shipmentExportSources?: ShipmentExportSource[];
  shipmentExportSourcesTruncated?: boolean;
  eventAnchorId?: number | null;
  eventsTruncated?: boolean;
  bucketsTruncated?: boolean;
};

export type RecallTreeSimulationResult = {
  simulationId: string;
  query: string;
  upstreamPoLinked: boolean;
  downstreamShipmentLinked: boolean;
  recallContainmentReady: boolean;
  simulatedAtUtc: string;
};

export const errorMessage = (e: unknown) => {
  const r = (e as { response?: { status?: number; data?: { message?: string } } })?.response;
  if (r?.status === 403) return 'Bạn không có quyền truy vết tồn kho. Liên hệ quản trị nếu cần quyền truy cập.';
  if (r?.data?.message) return r.data.message + ' Hãy điều chỉnh điều kiện hoặc thử lại.';
  return 'Không thể tải dữ liệu truy vết tồn kho. Hãy thử lại; nếu lỗi tiếp diễn, liên hệ quản trị.';
};

export const transactionTypeLabel = (value: string) =>
  ({
    Import: 'Nhập kho',
    Export: 'Xuất kho',
    AdjustmentIncrease: 'Điều chỉnh tăng',
    AdjustmentDecrease: 'Điều chỉnh giảm',
    TransferOut: 'Xuất điều chuyển',
    TransferIn: 'Nhập điều chuyển',
    TransferAdjustment: 'Điều chỉnh điều chuyển',
    Ship: 'Xuất giao hàng',
    StatusChange: 'Đổi trạng thái',
    Move: 'Di chuyển vị trí',
    Reversal: 'Đảo giao dịch',
  }[value] ?? value);

export const shipmentStatusLabel = (value: string) =>
  ({
    Draft: 'Nháp',
    Ready: 'Sẵn sàng',
    Staging: 'Chờ bốc hàng',
    Loading: 'Đang bốc hàng',
    Loaded: 'Đã bốc hàng',
    Dispatched: 'Đã xuất giao hàng',
    InTransit: 'Đang vận chuyển',
    Delivered: 'Đã xác nhận giao',
    DeliveryFailed: 'Giao không thành công',
    Cancelled: 'Đã hủy',
    ReturnToWarehouse: 'Đang xử lý hoàn về kho',
    Completed: 'Hoàn tất',
  }[value] ?? value);

export const referenceTypeLabel = (value: string) =>
  ({
    GoodsReceipt: 'Phiếu nhập kho',
    Receipt: 'Phiếu nhập kho',
    ImportReceipt: 'Phiếu nhập kho',
    ExportReceipt: 'Phiếu xuất kho',
    StockTransfer: 'Phiếu điều chuyển',
    Shipment: 'Chuyến giao hàng',
    InventoryReversal: 'Phiếu đảo giao dịch',
    InventoryAdjustment: 'Phiếu điều chỉnh',
    InventoryMove: 'Phiếu di chuyển',
    InternalMove: 'Di chuyển nội bộ',
    PurchaseOrder: 'Đơn mua hàng',
    Asn: 'Thông báo giao hàng',
    Stocktake: 'Phiếu kiểm kê',
    Return: 'Phiếu trả hàng',
    PickTask: 'Phiếu lấy hàng',
    HandlingUnitPalletSeed: 'Khởi tạo kiện LPN',
    QASeedReconcile: 'Đối soát sổ cái',
    VAS_DEKIT: 'Lệnh rã bộ KIT',
  }[value] ?? value);

export const inventoryStatusLabel = (value: string) =>
  ({
    available: 'Khả dụng',
    qchold: 'Chờ kiểm tra chất lượng',
    quarantine: 'Cách ly',
    damaged: 'Hư hỏng',
    rejected: 'Từ chối',
    blocked: 'Bị chặn',
    expired: 'Hết hạn',
    recallblocked: 'Khóa thu hồi',
  }[value.replace(/[_\s-]/g, '').toLowerCase()] ?? value);

export const locationCodeLabel = (value?: string | null) =>
  value
    ? ({
        RECEIVING: 'Bãi tiếp nhận (RCV-01)',
        STAGING: 'TAP-KET (Tập kết xuất)',
        PACKING: 'DONG-GOI (Đóng gói)',
        SHIPPING: 'XUAT-HANG (Xuất hàng)',
        QUARANTINE: 'CACH-LY (Cách ly)',
        QC_HOLD: 'CHO-QC (Chờ QC)',
      }[value.trim().toUpperCase()] ?? value)
    : '—';

export const tone = (x: Event): 'neutral' | 'success' | 'warning' | 'danger' =>
  x.transactionType === 'Reversal' ? 'warning' : x.isReversed ? 'neutral' : 'success';

export const validPositiveId = (value: string) => /^[1-9]\d*$/.test(value) && Number.isSafeInteger(Number(value));
