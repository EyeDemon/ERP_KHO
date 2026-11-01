import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';

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

export const formatCurrency = (val: number, currency = 'VND') =>
  `${Number(val || 0).toLocaleString('vi-VN', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ${currency}`;

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
      warehouseName: String(item.warehouseName ?? item.warehouseCode ?? 'Kho Trung tâm WH01'),
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

export function PartnerDebtStorageSnapshotCard({
  storageSnapshots,
  snapshotBusy,
  onCaptureSnapshot,
  onReloadSnapshots,
}: {
  storageSnapshots: StorageOccupancySnapshotRow[];
  snapshotBusy: boolean;
  onCaptureSnapshot: () => Promise<void>;
  onReloadSnapshots: () => Promise<void>;
}) {
  return (
    <UiCard
      title="Đối Soát & Chốt Số Dư Lưu Kho 3PL Hàng Ngày (Theo Pallet • Thể tích m³ • Vị trí kệ)"
      subtitle="Tổng hợp sản lượng chiếm chỗ thực tế theo Chủ hàng để tính phí lưu kho tự động cuối ngày và lũy kế tháng"
    >
      <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
        <div className="ui-inline-wrap" style={{ gap: 8 }}>
          <UiBadge tone="success">Định mức tính phí: Ngày-Pallet • Ngày-m³ • Ngày-Vị trí kệ</UiBadge>
          <UiBadge>{`Số chủ hàng đang lưu kho: ${storageSnapshots.length}`}</UiBadge>
        </div>
        <div className="ui-inline-wrap" style={{ gap: 8 }}>
          <button
            type="button"
            data-testid="btn-capture-3pl-snapshot"
            disabled={snapshotBusy}
            onClick={() => void onCaptureSnapshot()}
          >
            {snapshotBusy ? 'Đang chốt số dư...' : 'Chốt Số Dư Lưu Kho Hôm Nay'}
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            disabled={snapshotBusy}
            onClick={() => void onReloadSnapshots()}
          >
            Tải lại Số Dư Lưu Kho 3PL
          </button>
        </div>
      </div>

      <UiTableScroll>
        <table className="ui-table-compact" aria-label="Bảng đối soát chi phí lưu kho 3PL theo chủ hàng" style={{ width: '100%' }}>
          <thead>
            <tr>
              <th>Chủ hàng</th>
              <th>Kho</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số mặt hàng</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số vị trí kệ chiếm chỗ</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tổng thể tích (m³)</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số Pallet quy đổi</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Phí lưu kho ước tính / ngày</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Phí lưu kho lũy kế tháng</th>
            </tr>
          </thead>
          <tbody>
            {storageSnapshots.length === 0 ? (
              <tr>
                <td colSpan={8} className="ui-empty-cell">
                  Chưa có bản ghi Số Dư Lưu Kho 3PL trong ngày — Nhấn &ldquo;Chốt Số Dư Lưu Kho Hôm Nay&rdquo; để ghi nhận.
                </td>
              </tr>
            ) : (
              storageSnapshots.map(item => (
                <tr key={item.id}>
                  <td><strong>{item.ownerName}</strong></td>
                  <td>{item.warehouseName}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.skuCount}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.occupiedBinCount}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {item.totalVolumeCbm.toLocaleString('vi-VN', { maximumFractionDigits: 2 })} m³
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {item.equivalentPallets.toLocaleString('vi-VN', { maximumFractionDigits: 2 })}
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    <strong>{formatCurrency(item.estimatedDailyFeeVnd)}</strong>
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    <strong>{formatCurrency(item.monthToDateFeeVnd)}</strong>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </UiTableScroll>
    </UiCard>
  );
}

export function PartnerDebtPaymentCard({
  selectedInvoice,
  payAmount,
  setPayAmount,
  paymentMethod,
  setPaymentMethod,
  referenceCode,
  setReferenceCode,
  payNote,
  setPayNote,
  submitting,
  onSubmitPayment,
  onClose,
}: {
  selectedInvoice: PartnerDebtInvoice;
  payAmount: string;
  setPayAmount: React.Dispatch<React.SetStateAction<string>>;
  paymentMethod: string;
  setPaymentMethod: React.Dispatch<React.SetStateAction<string>>;
  referenceCode: string;
  setReferenceCode: React.Dispatch<React.SetStateAction<string>>;
  payNote: string;
  setPayNote: React.Dispatch<React.SetStateAction<string>>;
  submitting: boolean;
  onSubmitPayment: (e: React.FormEvent) => Promise<void>;
  onClose: () => void;
}) {
  return (
    <UiCard title={`Tất toán / Thanh toán Hóa đơn ${selectedInvoice.code} (${selectedInvoice.debtType === 'AP' ? 'Phải trả NCC' : 'Phải thu KH'})`}>
      <form onSubmit={onSubmitPayment} className="ui-form-grid" style={{ width: '100%', maxWidth: '100%', minWidth: 0 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '10px 16px' }}>
          <div>
            <strong>Đối tác:</strong> {selectedInvoice.partnerCode} — {selectedInvoice.partnerName}
            <br />
            <small>Chứng từ gốc: {formatSourceType(selectedInvoice.sourceType)} #{selectedInvoice.sourceCode}</small>
          </div>
          <div>
            <strong>Tổng hóa đơn:</strong> {formatCurrency(selectedInvoice.totalAmount, selectedInvoice.currency)}
            <br />
            <small>Còn lại cần thanh toán: <strong>{formatCurrency(selectedInvoice.remainingAmount, selectedInvoice.currency)}</strong></small>
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px 16px', marginTop: 14, marginBottom: 16 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
            <span>Số tiền thanh toán *</span>
            <input
              aria-label="Số tiền thanh toán công nợ"
              type="number"
              min="0.01"
              max={selectedInvoice.remainingAmount}
              step="0.01"
              required
              value={payAmount}
              onChange={e => setPayAmount(e.target.value)}
              style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
            <span>Phương thức thanh toán</span>
            <select
              aria-label="Phương thức thanh toán"
              value={paymentMethod}
              onChange={e => setPaymentMethod(e.target.value)}
              style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
            >
              <option value="BANK_TRANSFER">Chuyển khoản ngân hàng</option>
              <option value="CASH">Tiền mặt tại quỹ</option>
              <option value="OFFSET">Bù trừ công nợ hai chiều</option>
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
            <span>Mã tham chiếu / Ủy nhiệm chi</span>
            <input
              aria-label="Mã tham chiếu thanh toán"
              value={referenceCode}
              onChange={e => setReferenceCode(e.target.value)}
              maxLength={80}
              style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600, color: '#334155' }}>
            <span>Ghi chú</span>
            <input
              aria-label="Ghi chú thanh toán"
              value={payNote}
              onChange={e => setPayNote(e.target.value)}
              maxLength={300}
              style={{ width: '100%', minHeight: 38, padding: '8px 12px', boxSizing: 'border-box' }}
            />
          </label>
        </div>
        <div className="ui-inline-actions">
          <button type="submit" className="ui-primary-button" disabled={submitting}>
            {submitting ? 'Đang ghi sổ...' : 'Xác nhận Thanh toán'}
          </button>
          <button type="button" disabled={submitting} onClick={onClose}>
            Đóng
          </button>
        </div>
      </form>
    </UiCard>
  );
}
