import React from 'react';
import apiClient from '../../services/apiClient';
import { hasPermission } from '../../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import { conflictErrorMessage, extractConflictCode, permissionError } from '../../services/permissionPresentation';
import { roundQty4 } from '../../pages/productCatalog';
import {
  type Discrepancy,
  type ImportReceipt,
  type MacPreviewLine,
  type ReasonCode,
  type ReceiveLineForm,
  type ResolutionForm,
  mapMacPreviewResponse,
  normalizeUnitCode,
} from './ImportReceiptTypes';

export function useImportReceiptAuxiliaryActions(params: {
  canObserve: boolean;
  canSubmit: boolean;
  canApproveDiscrepancy: boolean;
  canRejectDiscrepancy: boolean;
  canResolve: boolean;
  canUpdate: boolean;
  canReadPartners: boolean;
  selectedReceipt: ImportReceipt | null;
  setSelectedReceipt: React.Dispatch<React.SetStateAction<ImportReceipt | null>>;
  receiveLines: Record<number, ReceiveLineForm>;
  resolutionForms: Record<number, ResolutionForm>;
  reasonCodes: ReasonCode[];
  setDiscrepancies: React.Dispatch<React.SetStateAction<Discrepancy[]>>;
  workflowInFlight: React.MutableRefObject<number | null>;
  partnerMutationInFlight: React.MutableRefObject<boolean>;
  printRequest: React.MutableRefObject<number>;
  printTriggerRef: React.MutableRefObject<HTMLButtonElement | null>;
  setWorkflowId: React.Dispatch<React.SetStateAction<number | null>>;
  setPartnerUpdating: React.Dispatch<React.SetStateAction<boolean>>;
  setPrintReceipt: React.Dispatch<React.SetStateAction<ImportReceipt | null>>;
  setPrintFetchedAt: React.Dispatch<React.SetStateAction<Date | null>>;
  setPrintLoadingId: React.Dispatch<React.SetStateAction<number | null>>;
  setMacPreviewLines: React.Dispatch<React.SetStateAction<MacPreviewLine[]>>;
  setMacPreviewLoading: React.Dispatch<React.SetStateAction<boolean>>;
  setError: React.Dispatch<React.SetStateAction<string>>;
  setSuccessMsg: React.Dispatch<React.SetStateAction<string>>;
  fetchReceipts: () => Promise<void>;
  handleViewDetails: (id: number) => Promise<void>;
}) {
  const {
    canObserve, canSubmit, canApproveDiscrepancy, canRejectDiscrepancy, canResolve, canUpdate, canReadPartners,
    selectedReceipt, setSelectedReceipt, receiveLines, resolutionForms, reasonCodes, setDiscrepancies,
    workflowInFlight, partnerMutationInFlight, printRequest, printTriggerRef, setWorkflowId, setPartnerUpdating,
    setPrintReceipt, setPrintFetchedAt, setPrintLoadingId, setMacPreviewLines, setMacPreviewLoading,
    setError, setSuccessMsg, fetchReceipts, handleViewDetails,
  } = params;

  const previewMacBeforePost = async (receipt: ImportReceipt) => {
    setMacPreviewLoading(true); setError('');
    try {
      const res = await apiClient.get(`/api/v1/import-receipts/${receipt.id}/mac-preview`);
      setMacPreviewLines(mapMacPreviewResponse(res?.data, receipt));
    } catch (err) {
      setError(permissionError(err, 'Không thể tải xem trước giá vốn bình quân (MAC) từ máy chủ.'));
      setMacPreviewLines([]);
    } finally { setMacPreviewLoading(false); }
  };

  const observeReceipt = async (id: number, receipt?: ImportReceipt) => {
    if (!canObserve || workflowInFlight.current !== null) return;
    workflowInFlight.current = id; setWorkflowId(id); setError('');
    try {
      const source = receipt || (await apiClient.get(`/api/v1/import-receipts/${id}`)).data as ImportReceipt;
      const lines = (source.details || []).map(d => ({ lineId: d.id, observedQuantity: receiveLines[d.id]?.receivedQuantity ?? d.expectedQuantity, observedUnitId: d.operationUnitId, items: [] }));
      if (lines.some(x => x.observedQuantity < 0)) throw new Error('Số lượng quan sát không được âm.');
      const action = `import-discrepancy-observe:${id}`;
      await apiClient.post(`/api/v1/import-receipts/${id}/discrepancies/observe`, { lines }, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); setSuccessMsg('Đã ghi nhận số lượng thực tế. Tồn kho chưa thay đổi.');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) {
      if (err.response && selectedReceipt?.id === id && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') setSelectedReceipt(null);
      setError(err.response?.status === 409 ? conflictErrorMessage(err, 'Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.') : permissionError(err) || err.message || 'Không thể ghi nhận số lượng.');
    } finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const submitResolution = async (d: Discrepancy) => {
    if (!canSubmit || workflowInFlight.current !== null || !selectedReceipt) return;
    workflowInFlight.current = selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const form = resolutionForms[d.id] || { action: 'ACCEPT_OBSERVED', doorRejectedQuantity: 0, reasonCode: '', responsibleParty: 'UNKNOWN', supplierClaimRequired: false, note: '', evidenceReference: '', confirmNormalizedObservation: false };
      if (!form.reasonCode) throw new Error('Chọn mã lý do.');
      if ((form.responsibleParty === 'UNKNOWN' || reasonCodes.find(x => x.code === form.reasonCode)?.requiresNote) && !form.note.trim()) throw new Error('Lý do hoặc bên chịu trách nhiệm này yêu cầu ghi chú.');
      const action = `import-discrepancy-submit:${selectedReceipt.id}:${d.id}`;
      await apiClient.post(`/api/v1/import-receipts/${selectedReceipt.id}/discrepancies/${d.id}/submit`, { ...form, rowVersion: d.rowVersion }, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); setSuccessMsg('Đã gửi phương án xử lý. Tồn kho chưa thay đổi.');
      await fetchReceipts(); await handleViewDetails(selectedReceipt.id);
    } catch (err: any) {
      if (err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status === 409 ? conflictErrorMessage(err, 'Phương án xử lý đã thay đổi. Vui lòng tải lại phiếu.') : permissionError(err) || err.message || 'Không thể gửi phương án xử lý.');
    } finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const reviewResolution = async (d: Discrepancy, decision: 'approve' | 'reject') => {
    if (decision === 'approve' ? !canApproveDiscrepancy : !canRejectDiscrepancy) return;
    if (workflowInFlight.current !== null || !selectedReceipt) return;
    workflowInFlight.current = selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const action = `import-discrepancy-${decision}:${selectedReceipt.id}:${d.id}`;
      await apiClient.post(`/api/v1/import-receipts/${selectedReceipt.id}/discrepancies/${d.id}/${decision}`, d.rowVersion, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); await fetchReceipts(); await handleViewDetails(selectedReceipt.id);
    } catch (err: any) {
      if (err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status === 409 ? conflictErrorMessage(err, 'Phương án xử lý đã thay đổi. Vui lòng tải lại phiếu.') : permissionError(err) || 'Không thể duyệt phương án xử lý.');
    } finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const recountDiscrepancy = async (d: Discrepancy) => {
    if (!canResolve || workflowInFlight.current !== null || !selectedReceipt) return;
    workflowInFlight.current = selectedReceipt.id; setWorkflowId(selectedReceipt.id); setError('');
    try {
      const quantity = receiveLines[d.receiptLineId]?.receivedQuantity ?? d.observedQuantity;
      const action = `import-discrepancy-recount:${selectedReceipt.id}:${d.id}:${quantity}`;
      await apiClient.post(`/api/v1/import-receipts/${selectedReceipt.id}/discrepancies/${d.id}/recount`, { observedQuantity: quantity, observedUnitId: d.operationUnitId, voidedObservationItemIds: [], items: [], rowVersion: d.rowVersion }, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); await handleViewDetails(selectedReceipt.id);
    } catch (err: any) {
      if (err.response) { setSelectedReceipt(null); setDiscrepancies([]); }
      setError(err.response?.status === 409 ? conflictErrorMessage(err, 'Số lượng quan sát đã thay đổi. Vui lòng tải lại phiếu.') : permissionError(err) || 'Không thể kiểm đếm lại.');
    } finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const changeSupplier = async (value: number | null) => {
    if (!canUpdate || !canReadPartners || !selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true; setPartnerUpdating(true); setError('');
    try { await apiClient.put(`/api/v1/import-receipts/${selectedReceipt.id}/supplier`, { partnerId: value }); await fetchReceipts(); await handleViewDetails(selectedReceipt.id); }
    catch (x: any) { setError(permissionError(x) || 'Không đổi được nhà cung cấp.'); }
    finally { partnerMutationInFlight.current = false; setPartnerUpdating(false); }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    const request = ++printRequest.current;
    printTriggerRef.current = trigger; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(id); setError('');
    try { const res = await apiClient.get(`/api/v1/import-receipts/${id}`); if (request === printRequest.current && hasPermission('receipt.read')) { setPrintReceipt(res.data); setPrintFetchedAt(new Date()); } }
    catch (x: any) { if (request === printRequest.current) setError(permissionError(x, 'Không tải được dữ liệu bản in.')); }
    finally { if (request === printRequest.current) setPrintLoadingId(null); }
  };

  const closePrintPreview = () => {
    printRequest.current++; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(null);
    queueMicrotask(() => printTriggerRef.current?.focus());
  };

  return {
    previewMacBeforePost,
    observeReceipt,
    submitResolution,
    reviewResolution,
    recountDiscrepancy,
    changeSupplier,
    openPrintPreview,
    closePrintPreview,
  };
}

export function ImportReceiptDiscrepancyPanels({
  selectedReceipt,
  canReadDiscrepancy,
  discrepancies,
  resolutionForms,
  setResolutionForms,
  reasonCodes,
  receiveLines,
  setReceiveLines,
  workflowId,
  canResolve,
  canSubmit,
  canApproveDiscrepancy,
  canRejectDiscrepancy,
  recountDiscrepancy,
  submitResolution,
  reviewResolution,
}: {
  selectedReceipt: ImportReceipt;
  canReadDiscrepancy: boolean;
  discrepancies: Discrepancy[];
  resolutionForms: Record<number, ResolutionForm>;
  setResolutionForms: React.Dispatch<React.SetStateAction<Record<number, ResolutionForm>>>;
  reasonCodes: ReasonCode[];
  receiveLines: Record<number, ReceiveLineForm>;
  setReceiveLines: React.Dispatch<React.SetStateAction<Record<number, ReceiveLineForm>>>;
  workflowId: number | null;
  canResolve: boolean;
  canSubmit: boolean;
  canApproveDiscrepancy: boolean;
  canRejectDiscrepancy: boolean;
  recountDiscrepancy: (d: Discrepancy) => Promise<void>;
  submitResolution: (d: Discrepancy) => Promise<void>;
  reviewResolution: (d: Discrepancy, decision: 'approve' | 'reject') => Promise<void>;
}) {
  return (
    <>
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
    </>
  );
}
