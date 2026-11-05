import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import {
  formatCurrency,
  formatSourceType,
  type PartnerDebtInvoice,
  type StorageOccupancySnapshotRow,
} from './PartnerDebtTypes';

export * from './PartnerDebtTypes';

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
              <th style={{ minWidth: 125 }}>Chủ hàng</th>
              <th style={{ minWidth: 120 }}>Kho</th>
              <th style={{ minWidth: 96, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số mặt hàng</th>
              <th style={{ minWidth: 115, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Vị trí chiếm chỗ</th>
              <th style={{ minWidth: 110, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thể tích (m³)</th>
              <th style={{ minWidth: 110, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Pallet quy đổi</th>
              <th style={{ minWidth: 130, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Phí ước tính / ngày</th>
              <th style={{ minWidth: 130, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Phí lũy kế tháng</th>
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
                  <td>{formatWarehouseLabel(item.warehouseName)}</td>
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
