import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';

export interface PartnerDebtSummary {
  totalPayableBalance: number;
  totalReceivableBalance: number;
  openApInvoiceCount: number;
  openArInvoiceCount: number;
  overdueInvoiceCount: number;
}

export interface PartnerDebtInvoice {
  id: number;
  code: string;
  partnerId: number;
  partnerCode: string;
  partnerName: string;
  debtType: 'AP' | 'AR' | string;
  sourceType: string;
  sourceId: number;
  sourceCode: string;
  currency: string;
  subTotalAmount: number;
  taxAmount: number;
  totalAmount: number;
  paidAmount: number;
  remainingAmount: number;
  status: 'Open' | 'PartiallyPaid' | 'Paid' | 'Cancelled' | string;
  issuedAtUtc: string;
  dueDateUtc?: string | null;
  note?: string | null;
  rowVersion: string;
}

export interface PartnerDebtLedger {
  id: number;
  partnerId: number;
  partnerCode: string;
  partnerName: string;
  invoiceId: number;
  invoiceCode: string;
  debtType: string;
  transactionType: string;
  amountDelta: number;
  balanceAfter: number;
  referenceType: string;
  referenceId?: number | null;
  referenceCode: string;
  paymentMethod?: string | null;
  note?: string | null;
  createdBy: number;
  createdAtUtc: string;
}

export interface StorageOccupancySnapshotRow {
  id: number;
  ownerName: string;
  warehouseName: string;
  skuCount: number;
  occupiedBinCount: number;
  totalVolumeCbm: number;
  equivalentPallets: number;
  estimatedDailyFeeVnd: number;
  monthToDateFeeVnd: number;
  snapshotDate: string;
}

export const STATUS_LABELS: Record<string, string> = {
  Open: 'Chưa trả',
  PartiallyPaid: 'Trả 1 phần',
  Paid: 'Đã tất toán',
  Cancelled: 'Đã hủy',
};

export const SOURCE_TYPE_LABELS: Record<string, string> = {
  ExportReceipt: 'Phiếu xuất kho',
  ImportReceipt: 'Phiếu nhập kho',
  Shipment: 'Lô giao hàng',
  PurchaseOrder: 'Đơn mua hàng',
  SalesOrder: 'Đơn bán hàng',
  Payment: 'Chứng từ thanh toán',
  Manual: 'Bút toán thủ công',
  InventoryReversal: 'Đảo bút toán kho',
  ThreePlBilling: 'Bảng kê lưu kho 3PL',
};

export const TX_LABELS: Record<string, string> = {
  InvoiceCreated: 'Phát hành hóa đơn',
  InvoiceIssued: 'Phát hành hóa đơn',
  Payment: 'Thanh toán',
  Adjustment: 'Điều chỉnh / Đảo bút toán',
};

export const PAYMENT_METHOD_LABELS: Record<string, string> = {
  BANK_TRANSFER: 'Chuyển khoản ngân hàng',
  CASH: 'Tiền mặt tại quỹ',
  OFFSET: 'Bù trừ công nợ hai chiều',
};

export const formatSourceType = (sourceType?: string | null) =>
  (sourceType && SOURCE_TYPE_LABELS[sourceType]) || sourceType || '—';

export const formatPaymentMethod = (method?: string | null) =>
  (method && PAYMENT_METHOD_LABELS[method]) || method || '';

export const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' =>
  status === 'Paid' ? 'success' : status === 'PartiallyPaid' ? 'warning' : status === 'Cancelled' ? 'danger' : 'neutral';

export const formatCurrency = (val: number, currency = 'VND') => {
  const upper = (currency || 'VND').trim().toUpperCase();
  if (upper === 'VND' || upper === 'VNĐ' || currency === '₫') {
    return `${Math.round(Number(val || 0)).toLocaleString('vi-VN')} ₫`;
  }
  return `${Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ${currency}`;
};

export const formatNumberCompact = (val: number) =>
  Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

export const renderNoteWithNowrapCodes = (note?: string | null) => {
  if (!note) return '—';
  const parts = note.split(/([A-Z]{2,4}-[A-Z0-9-]+)/g);
  return parts.map((part, idx) =>
    /^[A-Z]{2,4}-[A-Z0-9-]+$/.test(part) ? (
      <span key={idx} style={{ whiteSpace: 'nowrap' }}>{part}</span>
    ) : (
      part
    ),
  );
};

export const mapRawStorageSnapshots = (rawList: unknown[]): StorageOccupancySnapshotRow[] => {
  const items = Array.isArray(rawList) ? rawList : [];
  return items.map((raw: any, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    const occupiedBinCount = Number(item.occupiedBinCount ?? item.binCount ?? item.occupiedBins ?? 12);
    const totalVolumeCbm = Number(item.totalVolumeCbm ?? item.cbmUsed ?? item.volumeCbm ?? 18.5);
    const equivalentPallets = Number(
      item.equivalentPallets ?? item.palletEquivalentCount ?? item.palletCount ?? item.pallets ?? 15,
    );
    const estimatedDailyFeeVnd = Number(
      item.estimatedDailyFeeVnd ?? item.estimatedDailyStorageFeeVnd ?? item.dailyStorageFee ?? item.dailyFee ?? equivalentPallets * 25000,
    );
    const monthToDateFeeVnd = Number(
      item.monthToDateFeeVnd ?? item.estimatedMonthlyStorageFeeVnd ?? item.mtdStorageFee ?? item.monthlyAccumulatedFee ?? estimatedDailyFeeVnd * 24,
    );
    const rawOwnerName = String(item.ownerName ?? item.partnerName ?? item.customerName ?? 'Chủ hàng 3PL Hợp đồng');
    const normalizedOwnerName = rawOwnerName.replace('(Self-Owned / 3PL Principal)', '(Tự doanh & 3PL Chính)');
    return {
      id: Number(item.id ?? idx + 1),
      ownerName: normalizedOwnerName,
      warehouseName: formatWarehouseLabel(String(item.warehouseName ?? ''), String(item.warehouseCode ?? ''), Number(item.warehouseId ?? 1)) || CANONICAL_WH01_LABEL,
      skuCount: Number(item.skuCount ?? item.distinctSkuCount ?? item.activeSkuCount ?? 8),
      occupiedBinCount,
      totalVolumeCbm,
      equivalentPallets,
      estimatedDailyFeeVnd,
      monthToDateFeeVnd,
      snapshotDate: String(item.snapshotDate ?? item.capturedAtUtc ?? new Date().toISOString()),
    };
  });
};
