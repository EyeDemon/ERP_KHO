import React from 'react';
import ReceiptInventoryIdentityEditor from '../../components/ReceiptInventoryIdentityEditor';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
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
  selectedReceipt,
  activeWorkspaceTab,
  canUpdate,
  canReadPartners,
  partnerUpdating,
  suppliers,
  changeSupplier,
  canReceive,
  canObserve,
  canQc,
  receiveLines,
  setReceiveLines,
  qcExcludedLines,
  setQcExcludedLines,
  macPreviewLines,
  macPreviewLoading,
  previewMacBeforePost,
  workflowId,
  observeReceipt,
  canReadDiscrepancy,
  discrepancies,
  resolutionForms,
  setResolutionForms,
  reasonCodes,
  canResolve,
  canSubmit,
  canApproveDiscrepancy,
  canRejectDiscrepancy,
  recountDiscrepancy,
  submitResolution,
  reviewResolution,
  runWorkflow,
  setSelectedReceipt,
}) => (
  <UiCard title={'Chi Tiết Phiếu Nhập: ' + selectedReceipt.code}>
    <details open>
      <summary style={{ cursor: 'pointer', fontWeight: 600, fontSize: 13, color: '#0284c7', marginBottom: 10 }}>
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
              ? 'Ẩn theo phân quyền'
              : `${Number(selectedReceipt.totalAmount ?? 0).toLocaleString('vi-VN')} VND`}
          </div>
        )}
      </div>

      <UiTableScroll>
        <table aria-label={'Chi tiết phiếu nhập ' + selectedReceipt.code}>
          <thead>
            <tr>
              <th style={{ textAlign: 'center', width: 48 }}>STT</th>
              <th>Mã SP</th>
              <th>Tên SP</th>
              <th style={{ textAlign: 'center' }}>ĐVT</th>
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
              return (
                <tr key={d.id}>
                  <td style={{ textAlign: 'center', fontVariantNumeric: 'tabular-nums' }}>{idx + 1}</td>
                  <td style={{ whiteSpace: 'nowrap' }}><strong>{d.productCode}</strong></td>
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
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{d.unitPrice === null ? 'Ẩn theo phân quyền' : `${Number(d.unitPrice ?? 0).toLocaleString('vi-VN')} VND`}</td>
                  <td className="import-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{lineTotal === null ? 'Ẩn theo phân quyền' : `${lineTotal.toLocaleString('vi-VN')} VND`}</td>
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
                  {macPreviewLines.map(line => (
                    <tr key={line.productCode}>
                      <td><strong>{line.productCode}</strong> — {line.productName}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.currentOnHandQty}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>+{line.incomingBaseQty}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.currentMacUnitCost === null ? 'Ẩn theo phân quyền' : `${line.currentMacUnitCost.toLocaleString('vi-VN')} VND`}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.incomingUnitPrice === null ? 'Ẩn theo phân quyền' : `${line.incomingUnitPrice.toLocaleString('vi-VN')} VND`}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700, color: '#1e3a8a' }}>{line.projectedMacUnitCost === null ? 'Ẩn theo phân quyền' : `${line.projectedMacUnitCost.toLocaleString('vi-VN')} VND`}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: (line.deltaMacCost ?? 0) > 0 ? '#b45309' : '#065f46' }}>{line.deltaMacCost === null ? 'Ẩn theo phân quyền' : `${line.deltaMacCost >= 0 ? '+' : ''}${line.deltaMacCost.toLocaleString('vi-VN')} VND`}</td>
                    </tr>
                  ))}
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
              id: d.id,
              productId: d.productId,
              productCode: d.productCode,
              productName: d.productName,
              trackingType: d.trackingType || 'None',
              expiryControl: d.expiryControl || false,
              shelfLifeDays: d.shelfLifeDays,
              baseAcceptedQuantity: d.baseAcceptedQuantity || 0,
              baseDamagedQuantity: d.baseDamagedQuantity || 0,
              baseRejectedQuantity: d.baseRejectedQuantity || 0,
              baseUnitCode: normalizeUnitCode(d.baseUnitCode),
            }))}
          />
        </div>
      )}

      {selectedReceipt.status === 'Draft' && canObserve && (
        <div className="import-section-actions no-print">
          <button type="button" style={{ whiteSpace: 'nowrap', background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }} disabled={workflowId === selectedReceipt.id} onClick={() => void observeReceipt(selectedReceipt.id, selectedReceipt)}>
            {workflowId === selectedReceipt.id ? 'Đang ghi nhận...' : 'Ghi nhận số lượng thực tế'}
          </button>
        </div>
      )}
    </details>

    {canReadDiscrepancy && ['DiscrepancyPending', 'DiscrepancyRejected'].includes(selectedReceipt.status) && (
      <section aria-labelledby="discrepancy-heading" className="import-workflow-panel warning no-print">
        <h4 id="discrepancy-heading">Xử lý sai lệch nhận hàng</h4>
        <p>Xử lý sai lệch không ghi tồn kho. Ghi nhận tồn kho là bước duy nhất tạo tồn kho và sổ kho.</p>
        {discrepancies.filter(d => d.status === 'Pending' || d.status === 'Rejected').map(d => {
          const line = selectedReceipt.details.find(x => x.id === d.receiptLineId);
          const form = resolutionForms[d.id] || { action: 'ACCEPT_OBSERVED', doorRejectedQuantity: 0, reasonCode: '', responsibleParty: 'UNKNOWN', supplierClaimRequired: false, note: '', evidenceReference: '', confirmNormalizedObservation: false };
          const set = (next: Partial<ResolutionForm>) => setResolutionForms(x => ({ ...x, [d.id]: { ...form, ...next } }));
          const dOpUnit = normalizeUnitCode(d.operationUnitCode);
          const dObsUnit = normalizeUnitCode(d.observedUnitCode);
          return (
            <div key={d.id} className="ui-subcard import-resolution">
              <div className="ui-subcard-title">{line?.productCode}: dự kiến {d.expectedQuantity} {dOpUnit}, quan sát {d.observedQuantity} {dObsUnit}, chênh lệch chuẩn hóa {d.differenceQuantity} {dOpUnit}</div>
              <label>Hành động
                <select value={form.action} onChange={e => set({ action: e.target.value })}>
                  <option value="ACCEPT_OBSERVED">Chấp nhận số lượng quan sát</option>
                  <option value="ACCEPT_EXPECTED_REJECT_EXCESS">Nhận dự kiến, từ chối phần thừa</option>
                  <option value="REJECT_AT_DOOR">Từ chối tại cửa</option>
                  <option value="REJECT_WRONG_PRODUCT">Từ chối sai sản phẩm</option>
                  <option value="ROUTE_TO_QC">Chuyển sang kiểm tra chất lượng</option>
                </select>
              </label>
              <label>Từ chối tại cửa <input aria-label={'Từ chối tại cửa ' + line?.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={form.doorRejectedQuantity} onChange={e => set({ doorRejectedQuantity: Number(e.target.value) })} /></label>
              <label>Lý do
                <select value={form.reasonCode} onChange={e => set({ reasonCode: e.target.value })}>
                  <option value="">-- Chọn lý do --</option>
                  {reasonCodes.map(x => <option key={x.code} value={x.code}>{x.code === 'CUSTOMER_RETURN' ? 'Khách hoàn trả (RMA)' : x.code} — {x.name} (v{x.version})</option>)}
                </select>
              </label>
              <label>Chịu trách nhiệm
                <select value={form.responsibleParty} onChange={e => set({ responsibleParty: e.target.value })}>
                  <option value="SUPPLIER">Nhà cung cấp</option>
                  <option value="CARRIER">Đơn vị vận chuyển</option>
                  <option value="WAREHOUSE">Kho</option>
                  <option value="CUSTOMER_RETURN">Khách hoàn trả (RMA)</option>
                  <option value="UNKNOWN">Chưa xác định</option>
                </select>
              </label>
              <label>Ghi chú <input value={form.note} onChange={e => set({ note: e.target.value })} /></label>
              <label>Tham chiếu bằng chứng <input value={form.evidenceReference} onChange={e => set({ evidenceReference: e.target.value })} /></label>
              {d.observedUnitId !== d.operationUnitId && (
                <label className="import-resolution-check">
                  <input type="checkbox" checked={form.confirmNormalizedObservation} onChange={e => set({ confirmNormalizedObservation: e.target.checked })} />
                  Xác nhận số lượng quan sát đã quy đổi × {d.conversionFactor} v{d.conversionVersion}
                </label>
              )}
              <div>Số lượng cuối dự kiến: {roundQty4(Math.max(0, d.normalizedObservedQuantity - form.doorRejectedQuantity))} {dOpUnit}; phần từ chối tại cửa không tạo tồn kho.</div>
              <label>Kiểm đếm lại <input aria-label={'Kiểm đếm lại ' + line?.productCode} inputMode="decimal" role="spinbutton" min="0" step="any" value={receiveLines[d.receiptLineId]?.receivedQuantity ?? d.observedQuantity} onChange={e => setReceiveLines(x => ({ ...x, [d.receiptLineId]: { ...(x[d.receiptLineId] || { acceptedQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0 }), receivedQuantity: Number(e.target.value) } }))} /></label>
              <div className="import-row-actions">
                {canResolve && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void recountDiscrepancy(d)}>Lưu phiên bản kiểm đếm mới</button>}
                {canSubmit && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void submitResolution(d)}>{workflowId === selectedReceipt.id ? 'Đang gửi...' : 'Gửi phương án xử lý'}</button>}
              </div>
            </div>
          );
        })}
      </section>
    )}

    {canReadDiscrepancy && selectedReceipt.status === 'DiscrepancyPendingApproval' && (
      <section aria-labelledby="discrepancy-approval-heading" className="import-workflow-panel review no-print">
        <h4 id="discrepancy-approval-heading">Duyệt phương án xử lý sai lệch</h4>
        {discrepancies.filter(d => d.status === 'PendingApproval').map(d => (
          <div key={d.id} className="import-row-actions">
            <span>Dòng {d.receiptLineId}: {d.differenceQuantity} {normalizeUnitCode(d.operationUnitCode)}</span>
            {canApproveDiscrepancy && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void reviewResolution(d, 'approve')}>Duyệt phương án xử lý</button>}
            {canRejectDiscrepancy && <button type="button" disabled={workflowId === selectedReceipt.id} onClick={() => void reviewResolution(d, 'reject')}>Trả lại để kiểm đếm</button>}
          </div>
        ))}
      </section>
    )}

    <div className="ui-print-signatures" data-testid="import-receipt-signatures" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 16, marginTop: 24, paddingTop: 16, borderTop: '1px dashed #cbd5e1', textAlign: 'center', pageBreakInside: 'avoid' }}>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Người lập phiếu</strong><em style={{ fontSize: 12, color: '#64748b' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Người giao / nhận hàng</strong><em style={{ fontSize: 12, color: '#64748b' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Thủ kho</strong><em style={{ fontSize: 12, color: '#64748b' }}>(Ký, ghi rõ họ tên)</em></div>
      <div><strong style={{ display: 'block', fontSize: 13, color: '#0f172a' }}>Quản lý kho / Kế toán</strong><em style={{ fontSize: 12, color: '#64748b' }}>(Ký, ghi rõ họ tên)</em></div>
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
