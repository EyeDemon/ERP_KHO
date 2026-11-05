import React from 'react';
import { Lock } from 'lucide-react';
import ReceiptInventoryIdentityEditor from '../../components/ReceiptInventoryIdentityEditor';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { ImportReceiptDiscrepancyPanels } from './ImportReceiptDiscrepancySection';
import {
  type Discrepancy,
  type ImportReceipt,
  type MacPreviewLine,
  type Partner,
  type ReasonCode,
  type ReceiveLineForm,
  type ResolutionForm,
  formatVnDateTime,
  mapCreatedByDisplayName,
  normalizeUnitCode,
  statusLabel,
  statusTone,
} from './ImportReceiptTypes';

export interface ImportReceiptDetailViewCardProps {
  selectedReceipt: ImportReceipt;
  activeWorkspaceTab: 'receipts' | 'scanner' | 'rma';
  canUpdate: boolean;
  canReadPartners: boolean;
  partnerUpdating: boolean;
  suppliers: Partner[];
  changeSupplier: (value: number | null) => Promise<void>;
  canReceive: boolean;
  canObserve: boolean;
  canQc: boolean;
  receiveLines: Record<number, ReceiveLineForm>;
  setReceiveLines: React.Dispatch<React.SetStateAction<Record<number, ReceiveLineForm>>>;
  qcExcludedLines: number[];
  setQcExcludedLines: React.Dispatch<React.SetStateAction<number[]>>;
  macPreviewLines: MacPreviewLine[];
  macPreviewLoading: boolean;
  previewMacBeforePost: (receipt: ImportReceipt) => Promise<void>;
  workflowId: number | null;
  observeReceipt: (id: number, receipt?: ImportReceipt) => Promise<void>;
  canReadDiscrepancy: boolean;
  discrepancies: Discrepancy[];
  resolutionForms: Record<number, ResolutionForm>;
  setResolutionForms: React.Dispatch<React.SetStateAction<Record<number, ResolutionForm>>>;
  reasonCodes: ReasonCode[];
  canResolve: boolean;
  canSubmit: boolean;
  canApproveDiscrepancy: boolean;
  canRejectDiscrepancy: boolean;
  recountDiscrepancy: (d: Discrepancy) => Promise<void>;
  submitResolution: (d: Discrepancy) => Promise<void>;
  reviewResolution: (d: Discrepancy, decision: 'approve' | 'reject') => Promise<void>;
  runWorkflow: (id: number, command: 'receive' | 'qc-disposition' | 'post', receipt?: ImportReceipt) => Promise<void>;
  setSelectedReceipt: (val: ImportReceipt | null) => void;
}

export const ImportReceiptDetailViewCard: React.FC<ImportReceiptDetailViewCardProps> = ({
  selectedReceipt, activeWorkspaceTab, canUpdate, canReadPartners, partnerUpdating, suppliers, changeSupplier,
  canReceive, canObserve, canQc, receiveLines, setReceiveLines, qcExcludedLines, setQcExcludedLines,
  macPreviewLines, macPreviewLoading, previewMacBeforePost, workflowId, observeReceipt,
  canReadDiscrepancy, discrepancies, resolutionForms, setResolutionForms, reasonCodes,
  canResolve, canSubmit, canApproveDiscrepancy, canRejectDiscrepancy, recountDiscrepancy,
  submitResolution, reviewResolution, runWorkflow, setSelectedReceipt,
}) => (
  <UiCard title={'Chi Tiết Phiếu Nhập: ' + selectedReceipt.code}>
    <details open>
      <summary style={{ cursor: 'pointer', fontWeight: 600, fontSize: 13, color: '#1d4ed8', marginBottom: 10 }}>
        {activeWorkspaceTab === 'scanner'
          ? `Xem / Thu gọn chi tiết dòng hàng phiếu ${selectedReceipt.code} (${selectedReceipt.details.length} dòng)`
          : `Thông tin chung & danh sách dòng hàng (${selectedReceipt.details.length} dòng)`}
      </summary>
      <div className="import-detail-meta">
        <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
        <div><strong>Nhà cung cấp:</strong> {selectedReceipt.supplierCode ? selectedReceipt.supplierCode + ' - ' + selectedReceipt.supplierName : '—'}</div>
        {selectedReceipt.status === 'Draft' && canUpdate && canReadPartners && (
          <div className="no-print">
            <label className="ui-stack">
              <span>Đổi nhà cung cấp</span>
              <select aria-label="Đổi nhà cung cấp" disabled={partnerUpdating} value={selectedReceipt.supplierId || ''} onChange={e => void changeSupplier(e.target.value ? Number(e.target.value) : null)}>
                <option value="">-- Gỡ liên kết --</option>
                {suppliers.filter(x => x.isActive || x.id === selectedReceipt.supplierId).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
              </select>
            </label>
            {partnerUpdating && <span role="status">Đang cập nhật...</span>}
          </div>
        )}
        <div><strong>Trạng thái:</strong> <UiBadge tone={statusTone(selectedReceipt.status)}>{statusLabel(selectedReceipt.status)}</UiBadge></div>
        <div><strong>Người tạo:</strong> {mapCreatedByDisplayName(selectedReceipt.createdByName, true)}</div>
        <div><strong>Ngày tạo:</strong> {formatVnDateTime(selectedReceipt.createdAt)}</div>
        {['Approved', 'ReadyToPost', 'Posted'].includes(selectedReceipt.status) && selectedReceipt.approvedAt && (
          <>
            <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName}</div>
            <div><strong>Ngày duyệt:</strong> {formatVnDateTime(selectedReceipt.approvedAt)}</div>
          </>
        )}
        <div><strong>Ghi chú:</strong> {selectedReceipt.note || '—'}</div>
        {(selectedReceipt.totalAmount !== undefined || selectedReceipt.details.some(d => d.unitPrice === null)) && (
          <div>
            <strong>Tổng giá trị:</strong>{' '}
            {selectedReceipt.totalAmount === null || selectedReceipt.details.some(d => d.unitPrice === null)
              ? <span className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}><Lock size={12} aria-hidden="true" /> Ẩn theo quyền</span>
              : `${Number(selectedReceipt.totalAmount ?? 0).toLocaleString('vi-VN')} ₫`}
          </div>
        )}
      </div>

      <UiTableScroll>
        <table aria-label={'Chi tiết phiếu nhập ' + selectedReceipt.code}>
          <thead>
            <tr>
              <th style={{ textAlign: 'center', width: 76, minWidth: 76 }}>STT</th>
              <th>Mã SP</th>
              <th>Tên SP</th>
              <th style={{ minWidth: 78, textAlign: 'center' }}>Đơn vị</th>
              <th className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Dự kiến</th>
              <th className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Nhận / Chấp nhận</th>
              <th className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đơn vị cơ sở</th>
              <th className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đơn giá</th>
              <th className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thành tiền</th>
              <th>Ghi chú</th>
            </tr>
          </thead>
          <tbody>
            {selectedReceipt.details.map((d, idx) => {
              const opUnit = normalizeUnitCode(d.operationUnitCode);
              const baseUnit = normalizeUnitCode(d.baseUnitCode);
              const effQty = Number(d.postedQuantity || d.acceptedQuantity || d.receivedQuantity || d.expectedQuantity || 0);
              const lineTotal = d.unitPrice === null || d.lineTotalAmount === null
                ? null
                : (d.lineTotalAmount !== undefined ? Number(d.lineTotalAmount) : effQty * Number(d.unitPrice ?? 0));
              const maskedBadge = (
                <span className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}>
                  <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
                </span>
              );
              return (
                <tr key={d.id}>
                  <td style={{ textAlign: 'center', fontVariantNumeric: 'tabular-nums' }}>{idx + 1}</td>
                  <td style={{ whiteSpace: 'nowrap' }}><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 700 }}>{d.productCode}</span></td>
                  <td>{d.productName}<br /><small>Kiểm tra chất lượng: {d.requiresQc ? 'Bắt buộc · ' + statusLabel(d.qcState) + ' · chính sách ' + (d.qcPolicyId ?? '—') + ' phiên bản ' + (d.qcPolicyVersion ?? '—') : 'Không yêu cầu'}</small></td>
                  <td style={{ textAlign: 'center', whiteSpace: 'nowrap' }}>{opUnit || '—'}</td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{d.expectedQuantity} {opUnit}</td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {selectedReceipt.status === 'Draft' && (canReceive || canObserve) ? (
                      <div className="import-quantity-editor">
                        <label>Số lượng nhận <input aria-label={'Số lượng nhận ' + d.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.id]?.receivedQuantity ?? d.expectedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...(x[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }), receivedQuantity: Number(e.target.value) } }))} /></label>
                        {!d.requiresQc && <label>Số lượng chấp nhận <input aria-label={'Số lượng chấp nhận ' + d.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...(x[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }), acceptedQuantity: Number(e.target.value) } }))} /></label>}
                        <span>{d.requiresQc ? 'Kết quả kiểm tra được nhập sau khi nhận; chưa ghi tồn.' : 'Hư hỏng: 0 · Từ chối: 0 (không kiểm tra chất lượng)'}</span>
                      </div>
                    ) : selectedReceipt.status === 'QcPending' && canQc && d.requiresQc && d.qcState === 'QcPending' ? (
                      <div className="import-quantity-editor">
                        <label className="import-resolution-check">
                          <input type="checkbox" aria-label={'Ghi kết quả kiểm tra ' + d.productCode} checked={!qcExcludedLines.includes(d.id)} onChange={e => setQcExcludedLines(ids => e.target.checked ? ids.filter(id => id !== d.id) : [...ids, d.id])} />
                          Ghi kết quả dòng này
                        </label>
                        <label>Chấp nhận <input aria-label={'Kiểm tra chất lượng: chấp nhận ' + d.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.id]?.acceptedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], acceptedQuantity: Number(e.target.value) } }))} /></label>
                        <label>Hư hỏng <input aria-label={'Kiểm tra chất lượng: hư hỏng ' + d.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.id]?.damagedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], damagedQuantity: Number(e.target.value) } }))} /></label>
                        <label>Từ chối giữ tại kho <input aria-label={'Kiểm tra chất lượng: từ chối ' + d.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.id]?.rejectedQuantity ?? 0} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], rejectedQuantity: Number(e.target.value) } }))} /></label>
                        <label>Mã lý do <input aria-label={'Lý do kiểm tra ' + d.productCode} value={receiveLines[d.id]?.reasonCode ?? ''} onChange={e => setReceiveLines(x => ({ ...x, [d.id]: { ...x[d.id], reasonCode: e.target.value } }))} /></label>
                        <small>Tổng phải bằng {d.receivedQuantity} {opUnit}</small>
                      </div>
                    ) : (
                      <>{d.receivedQuantity} / {d.acceptedQuantity} {opUnit}<br /><small>Hư hỏng: {d.damagedQuantity}; Từ chối: {d.rejectedQuantity}</small></>
                    )}
                  </td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    <span style={{ fontSize: 12, color: '#475569' }}>Quy đổi: 1 {(opUnit || 'thùng').toLowerCase()} = {d.conversionFactor} ĐV cơ sở</span><br />
                    <strong>{selectedReceipt.status === 'Draft' ? roundQty4((receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity) * d.conversionFactor) : d.baseAcceptedQuantity || d.baseExpectedQuantity} {baseUnit}</strong>
                  </td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{d.unitPrice === null ? maskedBadge : `${Number(d.unitPrice ?? 0).toLocaleString('vi-VN')} ₫`}</td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{lineTotal === null ? maskedBadge : `${lineTotal.toLocaleString('vi-VN')} ₫`}</td>
                  <td>{d.note}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </UiTableScroll>

      <details className="no-print" style={{ marginTop: 12, marginBottom: 12 }}>
        <summary style={{ cursor: 'pointer', fontSize: 13, fontWeight: 600, color: '#334155' }}>
          Chi tiết quy tắc &amp; kỹ thuật hệ thống — Mô phỏng giá vốn bình quân trước khi ghi sổ
        </summary>
        <div data-testid="import-mac-preview-panel" style={{ marginTop: 8, padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1' }}>
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginBottom: macPreviewLines.length > 0 ? 10 : 0 }}>
            <div>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Mô phỏng &amp; xem trước giá vốn bình quân gia quyền trước khi ghi sổ</strong>
              <div style={{ fontSize: 12, color: '#475569' }}>Kiểm tra biến động giá vốn bình quân dự kiến trên từng SKU trước khi thực hiện lệnh ghi nhận tồn kho.</div>
            </div>
            <button type="button" data-testid="preview-mac-before-post-btn" disabled={macPreviewLoading} onClick={() => void previewMacBeforePost(selectedReceipt)}>
              {macPreviewLoading ? 'Đang tính giá vốn bình quân...' : 'Xem trước giá vốn bình quân trước khi ghi sổ'}
            </button>
          </div>
          {macPreviewLines.length > 0 && (
            <UiTableScroll>
              <table aria-label="Bảng xem trước giá vốn bình quân MAC" className="ui-table-compact">
                <thead>
                  <tr>
                    <th>Mã SP</th>
                    <th style={{ textAlign: 'right' }}>Tồn hiện tại</th>
                    <th style={{ textAlign: 'right' }}>SL nhập</th>
                    <th style={{ textAlign: 'right' }}>Giá vốn MAC hiện tại</th>
                    <th style={{ textAlign: 'right' }}>Đơn giá nhập</th>
                    <th style={{ textAlign: 'right' }}>Giá vốn MAC dự kiến sau Ghi sổ</th>
                    <th style={{ textAlign: 'right' }}>Chênh lệch Δ</th>
                  </tr>
                </thead>
                <tbody>
                  {macPreviewLines.map(line => {
                    const maskedMac = (
                      <span className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4, color: '#475569', fontSize: 12, fontWeight: 600 }}>
                        <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
                      </span>
                    );
                    return (
                      <tr key={line.productCode}>
                        <td><span className="ui-code" style={{ whiteSpace: 'nowrap', fontWeight: 700 }}>{line.productCode}</span> — {line.productName}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.currentOnHandQty}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>+{line.incomingBaseQty}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{line.currentMacUnitCost === null ? maskedMac : `${line.currentMacUnitCost.toLocaleString('vi-VN')} ₫`}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{line.incomingUnitPrice === null ? maskedMac : `${line.incomingUnitPrice.toLocaleString('vi-VN')} ₫`}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700, color: '#1e3a8a', whiteSpace: 'nowrap' }}>{line.projectedMacUnitCost === null ? maskedMac : `${line.projectedMacUnitCost.toLocaleString('vi-VN')} ₫`}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: (line.deltaMacCost ?? 0) > 0 ? '#b45309' : '#065f46', whiteSpace: 'nowrap' }}>{line.deltaMacCost === null ? maskedMac : `${line.deltaMacCost >= 0 ? '+' : ''}${line.deltaMacCost.toLocaleString('vi-VN')} ₫`}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </UiTableScroll>
          )}
        </div>
      </details>

      {selectedReceipt.status !== 'Draft' && (
        <div className="no-print">
          <ReceiptInventoryIdentityEditor
            receiptId={selectedReceipt.id}
            status={selectedReceipt.status}
            canUpdate={canUpdate}
            lines={selectedReceipt.details.map(d => ({
              id: d.id, productId: d.productId, productCode: d.productCode, productName: d.productName,
              trackingType: d.trackingType || 'None', expiryControl: d.expiryControl || false, shelfLifeDays: d.shelfLifeDays,
              baseAcceptedQuantity: d.baseAcceptedQuantity || 0, baseDamagedQuantity: d.baseDamagedQuantity || 0,
              baseRejectedQuantity: d.baseRejectedQuantity || 0, baseUnitCode: normalizeUnitCode(d.baseUnitCode),
            }))}
          />
        </div>
      )}

      {selectedReceipt.status === 'Draft' && canObserve && (
        <div className="import-section-actions no-print">
          <button type="button" style={{ whiteSpace: 'nowrap', background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 600 }} disabled={workflowId === selectedReceipt.id} onClick={() => void observeReceipt(selectedReceipt.id, selectedReceipt)}>
            {workflowId === selectedReceipt.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
          </button>
        </div>
      )}
    </details>

    <ImportReceiptDiscrepancyPanels
      selectedReceipt={selectedReceipt}
      canReadDiscrepancy={canReadDiscrepancy}
      discrepancies={discrepancies}
      resolutionForms={resolutionForms}
      setResolutionForms={setResolutionForms}
      reasonCodes={reasonCodes}
      receiveLines={receiveLines}
      setReceiveLines={setReceiveLines}
      workflowId={workflowId}
      canResolve={canResolve}
      canSubmit={canSubmit}
      canApproveDiscrepancy={canApproveDiscrepancy}
      canRejectDiscrepancy={canRejectDiscrepancy}
      recountDiscrepancy={recountDiscrepancy}
      submitResolution={submitResolution}
      reviewResolution={reviewResolution}
    />

    <div className="ui-print-signatures" data-testid="import-receipt-signatures" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 16, marginTop: 24, paddingTop: 16, borderTop: '1px dashed #cbd5e1', textAlign: 'center', pageBreakInside: 'avoid' }}>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Người lập phiếu</strong><em style={{ fontSize: 12, color: '#475569' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Người giao / nhận hàng</strong><em style={{ fontSize: 12, color: '#475569' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Thủ kho</strong><em style={{ fontSize: 12, color: '#475569' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Quản lý kho / Kế toán</strong><em style={{ fontSize: 12, color: '#475569' }}>(Ký, ghi rõ họ tên)</em></div>
    </div>

    <div className="import-section-actions no-print">
      {selectedReceipt.status === 'QcPending' && canQc && (
        <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void runWorkflow(selectedReceipt.id, 'qc-disposition', selectedReceipt)}>
          {workflowId === selectedReceipt.id ? 'Đang lưu kết quả kiểm tra chất lượng...' : 'Ghi nhận kết quả kiểm tra chất lượng'}
        </button>
      )}
      <button type="button" onClick={() => setSelectedReceipt(null)}>Đóng chi tiết</button>
    </div>
  </UiCard>
);
