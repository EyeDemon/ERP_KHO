export type SourceLine = {
  pickingTaskLineId: number;
  productId: number;
  productCode: string;
  productName: string;
  pickedQuantity: number;
  packedQuantity: number;
  remainingQuantity: number;
};

export type HuContent = {
  id: number;
  pickingTaskLineId: number;
  productId: number;
  productCode: string;
  productName: string;
  quantity: number;
  packedAt: string;
};

export type HandlingUnit = {
  id: number;
  huCode: string;
  barcode: string;
  sscc?: string;
  warehouseId: number;
  packingSessionId: number;
  parentHandlingUnitId?: number;
  parentHandlingUnitCode?: string;
  type: string;
  status: string;
  grossWeightKg?: number;
  netWeightKg?: number;
  volumeM3?: number;
  sealedAt?: string;
  createdAt: string;
  closedAt?: string;
  rowVersion: string;
  contents: HuContent[];
};

export type PackingSession = {
  id: number;
  sessionCode: string;
  pickingTaskId: number;
  pickingTaskCode: string;
  sourceType: string;
  sourceId?: number;
  sourceCode?: string;
  warehouseId: number;
  warehouseName: string;
  status: string;
  requiredQuantity: number;
  packedQuantity: number;
  remainingQuantity: number;
  handlingUnitCount: number;
  createdAt: string;
  startedAt?: string;
  packedAt?: string;
  closedAt?: string;
  rowVersion?: string;
  lines?: SourceLine[];
  handlingUnits?: HandlingUnit[];
};

export type CartonRecommendation = {
  recommendedCartonCode: string;
  recommendedCartonName: string;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
  totalItemsVolumeM3: number;
  volumeFillRatePercent: number;
  grossWeightKg: number;
  dimWeightKg: number;
  isDimWeightHigher: boolean;
};

export type VideoEvidenceItem = {
  id: number;
  sessionId: number;
  sessionCode: string;
  awbCode: string;
  durationSeconds: number;
  fileSizeMb: number;
  sha256Hash: string;
  recordedAt: string;
  cameraStation: string;
};

export const stateLabels: Record<string, string> = {
  Open: 'Mở',
  InProgress: 'Đang đóng gói',
  Packed: 'Đã đóng gói',
  Closed: 'Đã đóng',
  Exception: 'Ngoại lệ',
  Cancelled: 'Đã hủy',
};

export const huStateLabels: Record<string, string> = {
  Open: 'Mở',
  InUse: 'Đang dùng',
  Closed: 'Đã đóng',
  Staged: 'Đã tập kết',
  Loaded: 'Đã xếp xe',
  Shipped: 'Đã xuất giao',
  Cancelled: 'Đã hủy',
};

export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  ['Packed', 'Closed', 'Shipped'].includes(status)
    ? 'success'
    : status === 'Cancelled' || status === 'Exception'
      ? 'danger'
      : status === 'InProgress' || status === 'InUse'
        ? 'warning'
        : 'neutral';

export const errorMessage = (e: unknown) => {
  const response = (e as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  const code = response?.data?.code;
  if (code === 'PACK_QUANTITY_EXCEEDS_PICKED') return 'Số lượng đóng gói vượt số lượng đã Picking.';
  if (code === 'HU_CIRCULAR_NESTING') return 'Không thể tạo vòng lặp trong cấu trúc Handling Unit.';
  if (code === 'HU_CLOSED') return response?.data?.message ?? 'Handling Unit đã đóng hoặc đã đi vào downstream.';
  if (code === 'HU_CONTENT_MISMATCH') return response?.data?.message ?? 'Content/HU không khớp Packing session.';
  if (code === 'HU_NOT_FOUND' || response?.status === 404) return 'Không tìm thấy Packing session/Handling Unit trong phạm vi kho của bạn.';
  if (response?.status === 403) return 'Bạn không có quyền thực hiện thao tác Packing này.';
  if (response?.status === 409) return response.data?.message ?? 'Dữ liệu Packing đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message ?? 'Không thể xử lý Packing.';
};

export const packingBoxCatalog = [
  { code: 'CARTON-S', name: 'Thùng nhỏ S (25×20×15 cm)', l: 25, w: 20, h: 15, maxWeightKg: 5, volumeCm3: 7500, dimWeightKg: 1.25, gross: 0.85, vol: 0.006 },
  { code: 'CARTON-M', name: 'Thùng trung M (35×25×20 cm)', l: 35, w: 25, h: 20, maxWeightKg: 12, volumeCm3: 17500, dimWeightKg: 2.92, gross: 1.15, vol: 0.014 },
  { code: 'CARTON-L', name: 'Thùng lớn L (40×30×30 cm)', l: 40, w: 30, h: 30, maxWeightKg: 20, volumeCm3: 36000, dimWeightKg: 6.0, gross: 4.5, vol: 0.0288 },
  { code: 'CARTON-XL', name: 'Thùng đại XL (50×40×30 cm)', l: 50, w: 40, h: 30, maxWeightKg: 35, volumeCm3: 60000, dimWeightKg: 10.0, gross: 6.2, vol: 0.048 },
];

export const defaultCartonRecommendation: CartonRecommendation = {
  recommendedCartonCode: 'CARTON-M',
  recommendedCartonName: 'Thùng trung M (35×25×20 cm)',
  lengthCm: 35,
  widthCm: 25,
  heightCm: 20,
  totalItemsVolumeM3: 0.014,
  volumeFillRatePercent: 80,
  grossWeightKg: 1.15,
  dimWeightKg: 2.92,
  isDimWeightHigher: true,
};

export const hiddenTabStyle = {
  position: 'fixed' as const,
  top: 0,
  left: 0,
  width: 0,
  height: 0,
  maxHeight: 0,
  overflow: 'hidden' as const,
  opacity: 0,
  pointerEvents: 'none' as const,
  margin: 0,
  padding: 0,
  border: 0,
};
