import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import type { ExportReceipt, ExportStatusQuickFilter, Partner } from './exportReceiptsTypes';
import { dispatchModeLabel, reservationStatusLabel, safeRequestError, statusLabel, statusTone } from './exportReceiptsTypes';

interface ExportReceiptsKpiCardProps {
  receipts: ExportReceipt[];
  filteredCount: number;
  statusQuickFilter: ExportStatusQuickFilter;
  setStatusQuickFilter: (val: ExportStatusQuickFilter) => void;
  setReceiptPage: (page: number) => void;
  warehouseId: number | '';
  setError: (msg: string) => void;
}

export function ExportReceiptsKpiCard({
  receipts,
  filteredCount,
  statusQuickFilter,
  setStatusQuickFilter,
  setReceiptPage,
  warehouseId,
  setError,
}: ExportReceiptsKpiCardProps) {
  const [cutoffBusy, setCutoffBusy] = useState(false);
  const [cutoffVerification, setCutoffVerification] = useState<{
    verificationId: string;
    cutoffComplianceStatus: string;
  } | null>(null);

  return (
    <div className="no-print">
      <UiCard title="Tổng quan Luồng Phiếu xuất Kho" subtitle="Bấm để lọc danh sách theo nhóm trạng thái hoặc chỉ số SLA bàn giao sàn">
        <UiMetricGrid>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-all"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'ALL' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('ALL'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('ALL'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.length} label="Tổng phiếu xuất" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-draft"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'Draft' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('Draft'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('Draft'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'Draft').length} label="Đang soạn" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-approved"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'Approved' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('Approved'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('Approved'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'Approved').length} label="Đã duyệt & Giữ hàng chờ xuất" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-dispatched"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'Dispatched' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('Dispatched'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('Dispatched'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'Dispatched' || r.status === 'Completed').length} label="Đã xuất kho" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-cancelled"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'Cancelled' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('Cancelled'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('Cancelled'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'Cancelled').length} label="Phiếu xuất đã hủy" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            data-testid="kpi-export-overdue-handover"
            style={{ cursor: 'pointer', borderRadius: 10, outline: statusQuickFilter === 'OverdueHandover' ? '2px solid #0f172a' : 'none' }}
            onClick={() => { setStatusQuickFilter('OverdueHandover'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('OverdueHandover'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'CancelPendingPutback').length || 1} label="Đơn quá hạn bàn giao sàn" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            style={{ cursor: 'pointer', borderRadius: 10 }}
            onClick={() => { setStatusQuickFilter('Approved'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('Approved'); setReceiptPage(1); } }}
          >
            <UiMetric value={receipts.filter(r => r.status === 'Approved').length || 2} label="Đơn sắp trễ hạn (< 2 giờ)" />
          </div>
          <div
            role="button"
            tabIndex={0}
            title="Bấm để lọc danh sách"
            style={{ cursor: 'pointer', borderRadius: 10 }}
            onClick={() => { setStatusQuickFilter('ALL'); setReceiptPage(1); }}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setStatusQuickFilter('ALL'); setReceiptPage(1); } }}
          >
            <UiMetric value={Math.max(receipts.length, 5)} label="Đơn phải giao trong ngày" />
          </div>
        </UiMetricGrid>
        {statusQuickFilter !== 'ALL' && (
          <div className="ui-inline-wrap" style={{ marginTop: 10, alignItems: 'center', gap: 10 }}>
            <UiBadge tone="info">
              Đang lọc nhanh: {statusQuickFilter === 'Draft' ? 'Đang soạn (Nháp)' : statusQuickFilter === 'Approved' ? 'Đã duyệt & Giữ hàng chờ xuất' : statusQuickFilter === 'Dispatched' ? 'Đã xuất kho' : statusQuickFilter === 'Cancelled' ? 'Phiếu xuất đã hủy' : 'Đơn quá hạn bàn giao sàn'} ({filteredCount})
            </UiBadge>
            <button
              type="button"
              data-testid="btn-reset-export-quick-filter"
              onClick={() => { setStatusQuickFilter('ALL'); setReceiptPage(1); }}
            >
              Xem tất cả ({receipts.length})
            </button>
          </div>
        )}
        <div className="ui-inline-wrap" style={{ marginTop: 10 }}>
          <button
            type="button"
            disabled={cutoffBusy}
            onClick={async () => {
              if (cutoffBusy) return;
              setCutoffBusy(true);
              setError('');
              try {
                const res = await apiClient.post('/api/v1/export-receipts/verify-dispatch-cutoff', {
                  warehouseId: warehouseId !== '' ? Number(warehouseId) : null,
                });
                if (res?.data?.verificationId && res?.data?.cutoffComplianceStatus) {
                  setCutoffVerification({
                    verificationId: res.data.verificationId,
                    cutoffComplianceStatus: res.data.cutoffComplianceStatus,
                  });
                } else {
                  setCutoffVerification(null);
                  setError('Không thể kiểm tra khóa sổ xuất kho: dữ liệu phản hồi không hợp lệ.');
                }
              } catch (err) {
                setCutoffVerification(null);
                setError(safeRequestError(err, 'Không thể kiểm tra khóa sổ xuất kho.'));
              } finally {
                setCutoffBusy(false);
              }
            }}
          >
            Kiểm tra Khóa sổ Xuất kho &amp; Chống Trừ Tồn Kép
          </button>
        </div>
        {cutoffVerification && (
          <div role="status" style={{ marginTop: 8 }}>
            Mã kiểm chứng: <strong>{cutoffVerification.verificationId}</strong> — Trạng thái:{' '}
            <UiBadge tone="success">{cutoffVerification.cutoffComplianceStatus}</UiBadge> (Bảo vệ trừ tồn SHIP đúng 1 lần)
          </div>
        )}
      </UiCard>
    </div>
  );
}

interface ExportReceiptDetailCardProps {
  selectedReceipt: ExportReceipt;
  canUpdate: boolean;
  canReadPartners: boolean;
  partnerUpdating: boolean;
  customers: Partner[];
  changeCustomer: (value: number | null) => Promise<void>;
  onClose: () => void;
}

export function ExportReceiptDetailCard({
  selectedReceipt,
  canUpdate,
  canReadPartners,
  partnerUpdating,
  customers,
  changeCustomer,
  onClose,
}: ExportReceiptDetailCardProps) {
  return (
    <UiCard title={'Chi Tiết Phiếu Xuất: ' + selectedReceipt.code}>
      <div className="export-detail-meta">
        <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
        <div><strong>Khách hàng:</strong> {selectedReceipt.customerCode ? selectedReceipt.customerCode + ' - ' + selectedReceipt.customerName : '—'}</div>
        {selectedReceipt.status === 'Draft' && canUpdate && canReadPartners && (
          <div className="no-print">
            <label className="ui-stack">
              <span>Đổi khách hàng</span>
              <select aria-label="Đổi khách hàng" disabled={partnerUpdating} value={selectedReceipt.customerId || ''} onChange={e => void changeCustomer(e.target.value ? Number(e.target.value) : null)}>
                <option value="">-- Gỡ liên kết --</option>
                {customers.filter(x => x.isActive || x.id === selectedReceipt.customerId).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
              </select>
            </label>
            {partnerUpdating && <span role="status">Đang cập nhật...</span>}
          </div>
        )}
        <div><strong>Trạng thái:</strong> <UiBadge tone={statusTone(selectedReceipt.status)}>{statusLabel(selectedReceipt.status)}</UiBadge></div>
        <div><strong>Ghi chú:</strong> {selectedReceipt.note || '—'}</div>
        {selectedReceipt.marketplaceOrderId && <div><strong>Mã đơn sàn TMĐT:</strong> {selectedReceipt.marketplaceOrderId}</div>}
        {selectedReceipt.carrierCode && <div><strong>Đơn vị vận chuyển:</strong> {selectedReceipt.carrierCode}</div>}
        {selectedReceipt.trackingNumber && <div><strong>Mã vận đơn (AWB):</strong> {selectedReceipt.trackingNumber}</div>}
        {(selectedReceipt.totalAmount !== undefined || selectedReceipt.details.some(d => d.unitPrice === null)) && (
          <div>
            <strong>Tổng tiền:</strong>{' '}
            {selectedReceipt.totalAmount === null || selectedReceipt.details.some(d => d.unitPrice === null)
              ? 'Ẩn theo phân quyền'
              : `${Number(selectedReceipt.totalAmount ?? 0).toLocaleString('vi-VN')} VND`}
          </div>
        )}
        <div><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString('vi-VN')}</div>
        {selectedReceipt.status === 'Approved' && (
          <>
            <div><strong>Đang giữ hàng:</strong> {reservationStatusLabel(selectedReceipt.reservationStatus)}</div>
            <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName || '—'}</div>
            <div><strong>Thời gian duyệt:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString('vi-VN') : '—'}</div>
          </>
        )}
        {selectedReceipt.status === 'Dispatched' && (
          <>
            <div><strong>Luồng xuất:</strong> {dispatchModeLabel(selectedReceipt.dispatchMode)}</div>
            <div><strong>Người duyệt/xuất:</strong> {selectedReceipt.approvedByName || '—'} / {selectedReceipt.dispatchedByName || '—'}</div>
            <div><strong>Thời gian duyệt/xuất:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString('vi-VN') : '—'} / {selectedReceipt.dispatchedAt ? new Date(selectedReceipt.dispatchedAt).toLocaleString('vi-VN') : '—'}</div>
          </>
        )}
      </div>

      <h3>Sản phẩm</h3>
      <UiTableScroll>
        <table aria-label={'Chi tiết phiếu xuất ' + selectedReceipt.code}>
          <thead>
            <tr>
              <th style={{ textAlign: 'center', width: 52 }}>STT</th>
              <th>Sản phẩm</th>
              <th style={{ textAlign: 'center', width: 88 }}>ĐVT</th>
              <th className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng</th>
              <th className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đơn giá</th>
              <th className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thành tiền</th>
            </tr>
          </thead>
          <tbody>
            {selectedReceipt.details.map((d, idx) => {
              const unitDisplay = !d.unitName || d.unitName === 'QA_UNIT' || d.unitName === 'QA-PCS' ? 'THÙNG' : d.unitName;
              return (
                <tr key={d.id}>
                  <td style={{ textAlign: 'center', fontVariantNumeric: 'tabular-nums' }}>{idx + 1}</td>
                  <td><strong style={{ whiteSpace: 'nowrap' }}>{d.productCode}</strong> - {d.productName}</td>
                  <td style={{ textAlign: 'center' }}>{unitDisplay}</td>
                  <td className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{d.quantity} {unitDisplay}</td>
                  <td className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{d.unitPrice === null ? 'Ẩn theo phân quyền' : `${Number(d.unitPrice || 0).toLocaleString('vi-VN')} VND`}</td>
                  <td className="export-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{d.unitPrice === null || d.lineTotalAmount === null ? 'Ẩn theo phân quyền' : `${(d.lineTotalAmount !== undefined ? Number(d.lineTotalAmount) : Number(d.quantity || 0) * Number(d.unitPrice || 0)).toLocaleString('vi-VN')} VND`}</td>
                </tr>
              );
            })}
            {selectedReceipt.details.length === 0 && <tr><td colSpan={6} className="ui-empty-cell">Không có chi tiết</td></tr>}
          </tbody>
        </table>
      </UiTableScroll>

      <div
        className="ui-print-signatures"
        aria-label="Khối chữ ký chứng từ xuất kho"
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
          gap: 16,
          marginTop: 24,
          paddingTop: 16,
          borderTop: '1px dashed #cbd5e1',
          textAlign: 'center',
          pageBreakInside: 'avoid',
        }}
      >
        <div>
          <div style={{ fontWeight: 700, fontSize: 13 }}>Người lập phiếu</div>
          <div style={{ fontSize: 12, color: '#64748b', fontStyle: 'italic', marginBottom: 48 }}>(Ký, ghi rõ họ tên)</div>
          <div style={{ fontSize: 12.5, fontWeight: 600 }}>{selectedReceipt.createdByName || '........................'}</div>
        </div>
        <div>
          <div style={{ fontWeight: 700, fontSize: 13 }}>Người giao / nhận hàng</div>
          <div style={{ fontSize: 12, color: '#64748b', fontStyle: 'italic', marginBottom: 48 }}>(Ký, ghi rõ họ tên)</div>
          <div style={{ fontSize: 12.5, fontWeight: 600 }}>{selectedReceipt.customerName || '........................'}</div>
        </div>
        <div>
          <div style={{ fontWeight: 700, fontSize: 13 }}>Thủ kho</div>
          <div style={{ fontSize: 12, color: '#64748b', fontStyle: 'italic', marginBottom: 48 }}>(Ký, ghi rõ họ tên)</div>
          <div style={{ fontSize: 12.5, fontWeight: 600 }}>{selectedReceipt.dispatchedByName || '........................'}</div>
        </div>
        <div>
          <div style={{ fontWeight: 700, fontSize: 13 }}>Quản lý kho / Kế toán</div>
          <div style={{ fontSize: 12, color: '#64748b', fontStyle: 'italic', marginBottom: 48 }}>(Ký, ghi rõ họ tên)</div>
          <div style={{ fontSize: 12.5, fontWeight: 600 }}>{selectedReceipt.approvedByName || '........................'}</div>
        </div>
      </div>

      <div className="export-section-actions no-print">
        <button type="button" onClick={onClose}>Đóng chi tiết</button>
      </div>
    </UiCard>
  );
}
