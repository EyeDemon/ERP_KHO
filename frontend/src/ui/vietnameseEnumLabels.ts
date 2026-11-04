/**
 * Canonical 100% Vietnamese operational enum & location label dictionaries (Wave-19).
 * Prevents raw English enum tokens (e.g. QUARANTINED, PENDING_INSPECTION, REPACK_REQUIRED,
 * RECEIVING, QUARANTINE, STORAGE, ENTERPRISE) from leaking onto production UI screens.
 */

export const RMA_STATUS_LABELS_VI: Record<string, string> = {
  QUARANTINED: 'Đang biệt trữ',
  PENDING_INSPECTION: 'Chờ kiểm định QC',
  INSPECTED: 'Đã kiểm định',
  COMPLETED: 'Đã hoàn tất',
  DISPOSED: 'Đã hoàn tất',
  CANCELLED: 'Đã hủy',
};

export const RMA_CONDITION_LABELS_VI: Record<string, string> = {
  GOOD: 'Nguyên vẹn',
  DAMAGED_BOX: 'Móp vỏ hộp (Cần thay vỏ)',
  REPACK_REQUIRED: 'Móp vỏ hộp (Cần thay vỏ)',
  DEFECTIVE: 'Lỗi kỹ thuật / Hư hỏng',
};

export const RMA_DISPOSITION_LABELS_VI: Record<string, string> = {
  PENDING: 'Chờ quyết định',
  RESTOCK_AVAILABLE: 'Nhập lại tồn khả dụng',
  REFURBISH_REPACK: 'Tân trang & Đóng gói lại',
  RTV_VENDOR: 'Xuất trả NCC (RTV)',
  SCRAP_DISPOSAL: 'Xuất hủy',
};

export const LOCATION_TYPE_LABELS_VI: Record<string, string> = {
  RECEIVING: 'Khu tiếp nhận',
  QUARANTINE: 'Khu biệt trữ RMA',
  STORAGE: 'Kệ lưu trữ',
  STAGING: 'Khu tập kết xuất hàng',
  DAMAGED: 'Khu hàng hư hỏng',
  REJECTED: 'Khu hàng bị từ chối',
  QC_HOLD: 'Khu tạm giữ QC',
  PACKING: 'Khu đóng gói',
  SHIPPING: 'Khu giao vận',
  RETURNS: 'Khu tiếp nhận hàng hoàn',
  LEGACY: 'Tương thích hệ thống',
};

export const LOCATION_CODE_LABELS_VI: Record<string, string> = {
  RECEIVING: 'Khu tiếp nhận',
  'RECEIVING-01': 'Khu tiếp nhận (RCV-01)',
  'RCV-01': 'Khu tiếp nhận (RCV-01)',
  QUARANTINE: 'Khu biệt trữ RMA',
  'QUARANTINE-01': 'Khu biệt trữ RMA (Q-01)',
  'QC-HOLD-01': 'Khu tạm giữ QC (QC-01)',
  'DAMAGED-01': 'Khu hàng hư hỏng (DMG-01)',
  'RETURNS-STAGE-01': 'Khu tiếp nhận hàng hoàn (RET-01)',
  STORAGE: 'Kệ lưu trữ',
};

export const TAXPAYER_TYPE_LABELS_VI: Record<string, string> = {
  ENTERPRISE: 'Doanh nghiệp / Tổ chức (MST 10 hoặc 13 số)',
  HOUSEHOLD_INDIVIDUAL: 'Hộ / Cá nhân kinh doanh (CCCD 12 số)',
  INDIVIDUAL_CONSUMER: 'Cá nhân tiêu dùng',
};

const normalizeEnumKey = (raw?: string | null): string =>
  String(raw ?? '')
    .trim()
    .toUpperCase()
    .replace(/[\s-]+/g, '_');

export function rmaStatusLabelVi(raw?: string | null): string {
  const key = normalizeEnumKey(raw);
  if (!key) return 'Chờ kiểm định QC';
  return RMA_STATUS_LABELS_VI[key] ?? 'Đang xử lý';
}

export function rmaConditionLabelVi(raw?: string | null): string {
  const key = normalizeEnumKey(raw);
  if (!key) return 'Nguyên vẹn';
  if (RMA_CONDITION_LABELS_VI[key]) return RMA_CONDITION_LABELS_VI[key];
  if (key.includes('DAMAGED') || key.includes('REPACK')) return RMA_CONDITION_LABELS_VI.DAMAGED_BOX;
  if (key.includes('DEFECT')) return RMA_CONDITION_LABELS_VI.DEFECTIVE;
  return RMA_CONDITION_LABELS_VI.GOOD;
}

export function rmaDispositionLabelVi(raw?: string | null): string {
  const key = normalizeEnumKey(raw);
  if (!key) return 'Chờ quyết định';
  if (RMA_DISPOSITION_LABELS_VI[key]) return RMA_DISPOSITION_LABELS_VI[key];
  if (key.includes('RESTOCK')) return RMA_DISPOSITION_LABELS_VI.RESTOCK_AVAILABLE;
  if (key.includes('REFURBISH') || key.includes('REPACK')) return RMA_DISPOSITION_LABELS_VI.REFURBISH_REPACK;
  if (key.includes('RTV') || key.includes('VENDOR')) return RMA_DISPOSITION_LABELS_VI.RTV_VENDOR;
  if (key.includes('SCRAP')) return RMA_DISPOSITION_LABELS_VI.SCRAP_DISPOSAL;
  return RMA_DISPOSITION_LABELS_VI.PENDING;
}

export function warehouseLocationTypeLabelVi(raw?: string | null): string {
  const key = normalizeEnumKey(raw);
  if (!key) return 'Kệ lưu trữ';
  return LOCATION_TYPE_LABELS_VI[key] ?? 'Kệ lưu trữ';
}

export function warehouseLocationLabelVi(raw?: string | null): string {
  const trimmed = String(raw ?? '').trim();
  if (!trimmed) return 'Khu biệt trữ RMA';
  const upperCode = trimmed.toUpperCase();
  if (LOCATION_CODE_LABELS_VI[upperCode]) return LOCATION_CODE_LABELS_VI[upperCode];
  const normalizedType = normalizeEnumKey(trimmed);
  if (LOCATION_TYPE_LABELS_VI[normalizedType]) return LOCATION_TYPE_LABELS_VI[normalizedType];
  if (upperCode.startsWith('QUARANTINE-')) {
    const suffix = upperCode.replace(/^QUARANTINE-/, '');
    return `Khu biệt trữ RMA (Q-${suffix})`;
  }
  if (upperCode.startsWith('RECEIVING-')) {
    const suffix = upperCode.replace(/^RECEIVING-/, '');
    return `Khu tiếp nhận (RCV-${suffix})`;
  }
  if (upperCode.startsWith('STORAGE-')) {
    const suffix = upperCode.replace(/^STORAGE-/, '');
    return `Kệ lưu trữ (${suffix})`;
  }
  return trimmed;
}

export function taxpayerTypeLabelVi(raw?: string | null): string {
  const key = normalizeEnumKey(raw);
  return TAXPAYER_TYPE_LABELS_VI[key] ?? TAXPAYER_TYPE_LABELS_VI.ENTERPRISE;
}
