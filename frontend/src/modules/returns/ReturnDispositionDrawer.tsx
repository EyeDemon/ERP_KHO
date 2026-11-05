import { useEffect, useRef, useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge } from '../../ui/ProductionUi';
import { useDialogFocusTrap } from '../../ui/useDialogFocusTrap';
import { rmaDispositionLabelVi, warehouseLocationLabelVi } from '../../ui/vietnameseEnumLabels';

export const createRmaIdempotencyKey = (): string =>
  globalThis.crypto?.randomUUID?.() ?? `rma-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

export const normalizeCustomerReturnDto = (row: CustomerReturnDto): CustomerReturnDto => {
  const firstLine = row.lines?.[0];
  const rawCond = String(row.condition || firstLine?.condition || firstLine?.conditionNote || 'GOOD').toUpperCase();
  const normalizedCondition =
    rawCond.includes('DAMAGED') || rawCond.includes('REPACK')
      ? 'DAMAGED_BOX'
      : rawCond.includes('DEFECT')
        ? 'DEFECTIVE'
        : 'GOOD';
  const rawDisp = String(row.disposition || firstLine?.disposition || firstLine?.dispositionAction || 'PENDING').toUpperCase();
  return {
    ...row,
    warehouseName: row.warehouseName || row.warehouseCode || `Kho #${row.warehouseId || 1}`,
    quarantineLocationCode: row.quarantineLocationCode || firstLine?.quarantineLocationCode || 'QUARANTINE-01',
    productId: row.productId ?? firstLine?.productId,
    productCode: row.productCode || firstLine?.productCode || 'SKU-1001',
    productName: row.productName || firstLine?.productName || 'Sản phẩm hoàn trả',
    quantity: Math.max(1, Number(row.quantity ?? firstLine?.quantity ?? 1)),
    unitCost: row.unitCost ?? firstLine?.unitCost ?? firstLine?.unitPrice ?? null,
    condition: normalizedCondition,
    disposition: rawDisp,
  };
};

export interface CustomerReturnLineDto {
  id?: number;
  lineId?: number;
  lineNo?: number;
  productId?: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitPrice?: number | null;
  unitCost?: number | null;
  condition?: string;
  conditionNote?: string | null;
  disposition?: string;
  dispositionAction?: string | null;
  quarantineLocationCode?: string | null;
  restockLocationCode?: string | null;
}

export interface CustomerReturnDto {
  id: number;
  returnCode: string;
  trackingNumber?: string | null;
  exportReceiptCode?: string | null;
  salesOrderCode?: string | null;
  warehouseId: number;
  warehouseCode?: string;
  warehouseName: string;
  quarantineLocationCode?: string;
  productId?: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitCost?: number | null;
  returnReason?: string;
  condition: string;
  disposition: string;
  status: string;
  rowVersion?: string | null;
  createdAt?: string;
  lines?: CustomerReturnLineDto[];
}

export interface ReturnDispositionDrawerProps {
  open: boolean;
  item: CustomerReturnDto | null;
  canExecuteScrap: boolean;
  onClose: () => void;
  onCompleted: (message: string) => void;
  onReloadConflict: () => void;
}

const BUSINESS_ERROR_GUIDANCE: Record<string, string> = {
  INV_INSUFFICIENT_AVAILABLE_STOCK: 'Tồn kho vật tư bao bì hoặc hàng biệt trữ không đủ để thực hiện định đoạt.',
  RMA_RESTOCK_BIN_REQUIRED: 'Vui lòng nhập Vị trí kệ đích hợp lệ trước khi nhập lại kho.',
  RMA_PACKAGING_REQUIRED: 'Luồng Tân trang & Đóng gói lại yêu cầu chọn Mã vật tư bao bì và Số lượng tiêu hao > 0.',
  DOC_INVALID_STATE_TRANSITION: 'Phiếu hoàn trả đã hoàn tất hoặc không ở trạng thái cho phép định đoạt.',
};

export function ReturnDispositionDrawer({
  open,
  item,
  canExecuteScrap,
  onClose,
  onCompleted,
  onReloadConflict,
}: ReturnDispositionDrawerProps) {
  const [condition, setCondition] = useState<'GOOD' | 'DAMAGED_BOX' | 'DEFECTIVE'>('GOOD');
  const [disposition, setDisposition] = useState<
    'RESTOCK_AVAILABLE' | 'REFURBISH_REPACK' | 'RTV_VENDOR' | 'SCRAP_DISPOSAL'
  >('RESTOCK_AVAILABLE');
  const [restockLocationCode, setRestockLocationCode] = useState('A-01-01');
  const [packagingProductId, setPackagingProductId] = useState('PKG-BOX-01');
  const [packagingQuantity, setPackagingQuantity] = useState('1');
  const [note, setNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorState, setErrorState] = useState<{ status?: number; errorCode?: string; message: string } | null>(null);

  const drawerPanelRef = useRef<HTMLDivElement | null>(null);
  const disposeIdempotencyKeyRef = useRef<string>(createRmaIdempotencyKey());
  const inspectIdempotencyKeyRef = useRef<string>(createRmaIdempotencyKey());

  useDialogFocusTrap(drawerPanelRef, {
    open: Boolean(open && item),
    onEscape: onClose,
    escapeDisabled: isSubmitting,
  });

  useEffect(() => {
    if (!item) return;
    const firstLine = item.lines?.[0];
    const initCond = String(item.condition || firstLine?.condition || firstLine?.conditionNote || 'GOOD').toUpperCase();
    setCondition(
      initCond.includes('DAMAGED') || initCond.includes('REPACK')
        ? 'DAMAGED_BOX'
        : initCond.includes('DEFECT')
          ? 'DEFECTIVE'
          : 'GOOD',
    );
    const initDisp = String(item.disposition || firstLine?.disposition || firstLine?.dispositionAction || '').toUpperCase();
    if (
      initDisp === 'RESTOCK_AVAILABLE' ||
      initDisp === 'REFURBISH_REPACK' ||
      initDisp === 'RTV_VENDOR' ||
      (initDisp === 'SCRAP_DISPOSAL' && canExecuteScrap)
    ) {
      setDisposition(initDisp);
    } else {
      setDisposition('RESTOCK_AVAILABLE');
    }
    setRestockLocationCode(firstLine?.restockLocationCode || 'A-01-01');
    setPackagingProductId('PKG-BOX-01');
    setPackagingQuantity('1');
    setNote('');
    setErrorState(null);
    disposeIdempotencyKeyRef.current = createRmaIdempotencyKey();
    inspectIdempotencyKeyRef.current = createRmaIdempotencyKey();
  }, [item?.id, open, canExecuteScrap]);

  if (!open || !item) return null;

  const firstLine = item.lines?.[0];
  const effectiveProductCode = item.productCode || firstLine?.productCode || 'SKU-1001';
  const effectiveProductName = item.productName || firstLine?.productName || 'Sản phẩm hoàn trả';
  const effectiveQty = Math.max(1, Number(item.quantity ?? firstLine?.quantity ?? 1));
  const effectiveQuarantineBin = item.quarantineLocationCode || firstLine?.quarantineLocationCode || 'QUARANTINE-01';

  const extractApiError = (err: unknown, fallbackMsg: string) => {
    const resp = (err as { response?: { status?: number; data?: Record<string, unknown> } })?.response;
    const status = resp?.status;
    const data = resp?.data ?? {};
    const errorCode = String(data.errorCode ?? data.code ?? (data.error as Record<string, unknown>)?.code ?? '');
    const serverMessage = String(data.message ?? data.title ?? '');
    const mappedMessage = (errorCode && BUSINESS_ERROR_GUIDANCE[errorCode]) || serverMessage || fallbackMsg;
    return { status, errorCode, message: mappedMessage };
  };

  const handleInspectOnly = async () => {
    setIsSubmitting(true);
    setErrorState(null);
    try {
      await apiClient.post(
        `/api/v1/customer-returns/${item.id}/inspect`,
        {
          condition,
          proposedDisposition: disposition,
          note: note.trim() || undefined,
          rowVersion: item.rowVersion,
          lines: [{ productCode: effectiveProductCode, quantity: effectiveQty, condition, proposedDisposition: disposition }],
        },
        { headers: { 'Idempotency-Key': inspectIdempotencyKeyRef.current } },
      );
      inspectIdempotencyKeyRef.current = createRmaIdempotencyKey();
      onCompleted(`Đã lưu kết quả kiểm định chất lượng cho phiếu hoàn ${item.returnCode}.`);
    } catch (err) {
      setErrorState(extractApiError(err, 'Không thể ghi nhận kết quả kiểm định.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleExecuteDisposition = async (e: React.FormEvent) => {
    e.preventDefault();
    const needsBin = disposition === 'RESTOCK_AVAILABLE' || disposition === 'REFURBISH_REPACK';
    if (needsBin && !restockLocationCode.trim()) {
      setErrorState({ status: 422, errorCode: 'RMA_RESTOCK_BIN_REQUIRED', message: BUSINESS_ERROR_GUIDANCE.RMA_RESTOCK_BIN_REQUIRED });
      return;
    }
    if (disposition === 'REFURBISH_REPACK' && (!packagingProductId.trim() || Number(packagingQuantity) <= 0)) {
      setErrorState({ status: 422, errorCode: 'RMA_PACKAGING_REQUIRED', message: BUSINESS_ERROR_GUIDANCE.RMA_PACKAGING_REQUIRED });
      return;
    }

    setIsSubmitting(true);
    setErrorState(null);
    try {
      await apiClient.post(
        `/api/v1/customer-returns/${item.id}/dispose`,
        {
          disposition,
          restockLocationCode: needsBin ? restockLocationCode.trim() : undefined,
          packagingProductId: disposition === 'REFURBISH_REPACK' ? Number(packagingProductId) || 1 : undefined,
          packagingCode: disposition === 'REFURBISH_REPACK' ? packagingProductId.trim() : undefined,
          packagingQuantity: disposition === 'REFURBISH_REPACK' ? Number(packagingQuantity) || 1 : undefined,
          condition,
          note: note.trim() || undefined,
          rowVersion: item.rowVersion,
          splits: [{ disposition, quantity: effectiveQty, restockLocationCode: needsBin ? restockLocationCode.trim() : undefined }],
        },
        { headers: { 'Idempotency-Key': disposeIdempotencyKeyRef.current } },
      );
      disposeIdempotencyKeyRef.current = createRmaIdempotencyKey();
      onCompleted(`Đã thực thi định đoạt (${rmaDispositionLabelVi(disposition)}) và ghi sổ Sổ cái cho phiếu ${item.returnCode}.`);
    } catch (err) {
      setErrorState(extractApiError(err, 'Không thể thực thi định đoạt hàng hoàn trả.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="return-disposition-drawer-title"
      aria-label={`Kiểm định và định đoạt phiếu hoàn ${item.returnCode}`}
      data-testid="return-disposition-drawer"
      style={{ position: 'fixed', inset: 0, zIndex: 90, background: 'rgba(15, 23, 42, 0.48)', display: 'flex', justifyContent: 'flex-end' }}
    >
      <div
        ref={drawerPanelRef}
        tabIndex={-1}
        style={{ width: 'min(540px, 100vw)', height: '100%', background: '#ffffff', boxShadow: '-8px 0 28px rgba(15, 23, 42, 0.18)', padding: '20px 22px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 14, boxSizing: 'border-box' }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
          <div>
            <UiBadge tone="warning">Kiểm định &amp; Định đoạt 4 Luồng (RMA)</UiBadge>
            <h2 id="return-disposition-drawer-title" style={{ margin: '6px 0 0', fontSize: 17, color: '#0f172a' }}>
              Phiếu hoàn <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.returnCode}</span> • <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{effectiveProductCode}</span>
            </h2>
          </div>
          <button
            type="button"
            className="ui-btn ui-btn-secondary"
            aria-label="Đóng hộp thoại"
            onClick={onClose}
            disabled={isSubmitting}
            style={{ width: 36, minWidth: 36, minHeight: 36, padding: 0, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', fontSize: 18, lineHeight: 1, flexShrink: 0 }}
          >
            ×
          </button>
        </div>

        <div style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', fontSize: 13 }}>
          <div><strong>Kho / Vị trí Biệt trữ:</strong> {item.warehouseName} • {warehouseLocationLabelVi(effectiveQuarantineBin)}</div>
          <div><strong>Sản phẩm:</strong> <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{effectiveProductCode}</span> — {effectiveProductName} • <strong>SL:</strong> {effectiveQty}</div>
          <div><strong>Vận đơn / Đơn gốc:</strong> <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.trackingNumber || '—'}</span> / <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.exportReceiptCode || item.salesOrderCode || '—'}</span></div>
        </div>

        {errorState && errorState.status === 409 && (
          <div role="alert" data-testid="rma-disposition-conflict-409" style={{ padding: 12, borderRadius: 8, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 13 }}>
            <strong>Xung đột dữ liệu (Mã 409):</strong> {errorState.message}
            <div style={{ marginTop: 8 }}>
              <button type="button" data-testid="btn-reload-rma-conflict" className="ui-btn ui-btn-secondary" onClick={onReloadConflict}>Tải lại dữ liệu mới nhất</button>
            </div>
          </div>
        )}

        {errorState && errorState.status === 403 && (
          <div role="alert" data-testid="rma-disposition-forbidden-403" style={{ padding: 12, borderRadius: 8, background: '#fef2f2', border: '1px solid #f87171', color: '#991b1b', fontSize: 13 }}>
            <strong>Từ chối truy cập (Mã 403):</strong> Bạn không có quyền thực thi định đoạt này.
          </div>
        )}

        {errorState && errorState.status !== 409 && errorState.status !== 403 && (
          <div role="alert" data-testid="rma-disposition-error-422" style={{ padding: 12, borderRadius: 8, background: '#fef2f2', border: '1px solid #fca5a5', color: '#991b1b', fontSize: 13 }}>
            <strong>Lỗi nghiệp vụ:</strong> {errorState.message}
          </div>
        )}

        <form onSubmit={handleExecuteDisposition} className="ui-stack" style={{ gap: 12, flex: 1, display: 'flex', flexDirection: 'column' }}>
          <label className="ui-field">
            <span>Tình trạng vật lý sau kiểm định</span>
            <select aria-label="Tình trạng vật lý sau kiểm định" data-initiator="true" data-initial-focus="true" data-testid="select-rma-condition" value={condition} onChange={e => { setCondition(e.target.value as 'GOOD' | 'DAMAGED_BOX' | 'DEFECTIVE'); disposeIdempotencyKeyRef.current = createRmaIdempotencyKey(); }}>
              <option value="GOOD">Nguyên vẹn</option>
              <option value="DAMAGED_BOX">Móp vỏ hộp</option>
              <option value="DEFECTIVE">Lỗi kỹ thuật / Hư hỏng</option>
            </select>
          </label>

          <label className="ui-field">
            <span>Hướng định đoạt tồn kho (4 luồng chuẩn Sổ cái)</span>
            <select aria-label="Hướng định đoạt tồn kho" data-testid="select-rma-disposition" value={disposition} onChange={e => { setDisposition(e.target.value as typeof disposition); disposeIdempotencyKeyRef.current = createRmaIdempotencyKey(); }}>
              <option value="RESTOCK_AVAILABLE">1. Nhập lại tồn khả dụng</option>
              <option value="REFURBISH_REPACK">2. Tân trang &amp; Đóng gói lại</option>
              <option value="RTV_VENDOR">3. Xuất trả NCC (RTV)</option>
              <option value="SCRAP_DISPOSAL" disabled={!canExecuteScrap}>4. Xuất hủy{!canExecuteScrap ? ' — Cần quyền phê duyệt hủy' : ''}</option>
            </select>
          </label>

          {(disposition === 'RESTOCK_AVAILABLE' || disposition === 'REFURBISH_REPACK') && (
            <label className="ui-field">
              <span>Vị trí kệ đích (VD: A-01-01)</span>
              <input aria-label="Vị trí kệ đích" data-testid="input-rma-restock-bin" value={restockLocationCode} onChange={e => setRestockLocationCode(e.target.value)} placeholder="VD: A-01-01" />
            </label>
          )}

          {disposition === 'REFURBISH_REPACK' && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
              <label className="ui-field">
                <span>Mã vật tư bao bì thay thế</span>
                <input aria-label="Mã vật tư bao bì thay thế" data-testid="input-rma-packaging-code" value={packagingProductId} onChange={e => setPackagingProductId(e.target.value)} placeholder="VD: PKG-BOX-01" />
              </label>
              <label className="ui-field">
                <span>Số lượng bao bì tiêu hao</span>
                <input type="number" min={1} aria-label="Số lượng bao bì tiêu hao" data-testid="input-rma-packaging-qty" value={packagingQuantity} onChange={e => setPackagingQuantity(e.target.value)} />
              </label>
            </div>
          )}

          <label className="ui-field">
            <span>Ghi chú biên bản kiểm định / định đoạt</span>
            <textarea rows={2} aria-label="Ghi chú biên bản kiểm định" value={note} onChange={e => setNote(e.target.value)} placeholder="Ghi rõ tình trạng niêm phong, lý do hoàn hoặc mã biên bản hủy..." />
          </label>

          <div style={{ position: 'sticky', bottom: 0, zIndex: 2, background: '#ffffff', borderTop: '1px solid #e2e8f0', paddingTop: 12, paddingBottom: 2, marginTop: 'auto', display: 'flex', gap: 8, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
            <button type="button" className="ui-btn ui-btn-secondary" disabled={isSubmitting} onClick={onClose}>
              Hủy
            </button>
            <button type="button" data-testid="btn-submit-rma-inspect" className="ui-btn ui-btn-secondary" disabled={isSubmitting} onClick={() => void handleInspectOnly()}>
              {isSubmitting ? 'Đang xử lý...' : 'Lưu kết quả kiểm định'}
            </button>
            <button type="submit" data-testid="btn-submit-rma-disposition" className="ui-btn ui-btn-primary ui-primary-button" disabled={isSubmitting}>
              {isSubmitting ? 'Đang ghi sổ...' : 'Thực thi & Ghi sổ Sổ cái'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default ReturnDispositionDrawer;
