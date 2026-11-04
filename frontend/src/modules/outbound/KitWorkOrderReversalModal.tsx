import React, { useRef } from 'react';
import { useDialogFocusTrap } from '../../ui/useDialogFocusTrap';
import type { KitWorkOrderRow } from './packingSessionsTypes';

interface KitWorkOrderReversalModalProps {
  order: KitWorkOrderRow | null;
  canReadCost: boolean;
  reversalReason: string;
  setReversalReason: (val: string) => void;
  busy: boolean;
  onClose: () => void;
  onConfirm: (e: React.FormEvent) => Promise<void>;
}

export const KitWorkOrderReversalModal: React.FC<KitWorkOrderReversalModalProps> = ({
  order,
  canReadCost,
  reversalReason,
  setReversalReason,
  busy,
  onClose,
  onConfirm,
}) => {
  const dialogRef = useRef<HTMLDivElement>(null);
  useDialogFocusTrap(dialogRef, {
    open: Boolean(order),
    onEscape: onClose,
    escapeDisabled: busy,
  });

  if (!order) return null;

  const woQty = order.completedQty > 0 ? order.completedQty : order.plannedQty;
  const unitCost = order.rolledUpCost || 500000;
  const totalMac = order.totalCost ?? woQty * unitCost;
  const trimmedLen = reversalReason.trim().length;
  const isReasonValid = trimmedLen >= 10;

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        background: 'rgba(15, 23, 42, 0.55)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div
        ref={dialogRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-labelledby="kit-reversal-modal-title"
        data-testid="modal-confirm-kit-reversal"
        style={{
          width: 'min(540px, 100%)',
          borderRadius: 12,
          background: '#ffffff',
          border: '1px solid #fecaca',
          boxShadow: '0 20px 40px rgba(15, 23, 42, 0.22)',
          padding: 20,
          color: '#0f172a',
        }}
      >
        <form onSubmit={(e) => void onConfirm(e)} className="ui-stack" style={{ gap: 14 }}>
          <div>
            <div style={{ fontSize: 12, fontWeight: 700, color: '#b91c1c', textTransform: 'uppercase' }}>
              Cảnh báo tác vụ đảo bút toán tồn kho &amp; giá vốn
            </div>
            <h2 id="kit-reversal-modal-title" style={{ margin: '4px 0 0', fontSize: 17, fontWeight: 700, color: '#0f172a' }}>
              Xác nhận đảo lệnh lắp ráp {order.workOrderCode} ({order.kitSku})
            </h2>
          </div>

          <div
            data-testid="kit-reversal-impact-summary"
            style={{
              padding: '12px 14px',
              borderRadius: 8,
              background: '#fff1f2',
              border: '1px solid #fecdd3',
              color: '#881337',
              fontSize: 13.5,
              lineHeight: 1.5,
              fontWeight: 600,
              fontVariantNumeric: 'tabular-nums',
            }}
          >
            {`Đảo lệnh ${order.workOrderCode} sẽ trừ ${woQty} Bộ Kit khỏi tồn khả dụng, hoàn trả linh kiện về kệ và ghi bút toán đảo giá vốn ${
              canReadCost ? `${totalMac.toLocaleString('vi-VN')} ₫` : 'theo sổ cái MAC'
            }`}
          </div>

          <div
            data-testid="kit-reversal-mac-breakdown"
            style={{
              padding: '8px 12px',
              borderRadius: 8,
              background: '#f8fafc',
              border: '1px solid #e2e8f0',
              color: '#334155',
              fontSize: 12.5,
              fontVariantNumeric: 'tabular-nums',
            }}
          >
            {canReadCost
              ? `Chi tiết giá vốn bình quân (MAC): ${unitCost.toLocaleString('vi-VN')} ₫ / Bộ × ${woQty} Bộ = ${totalMac.toLocaleString('vi-VN')} ₫`
              : 'Chi tiết giá vốn bình quân (MAC): Đã ẩn theo phân quyền'}
          </div>

          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 13, fontWeight: 600 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
              <span>Lý do đảo lệnh lắp ráp (bắt buộc tối thiểu 10 ký tự) *</span>
              <span
                data-testid="kit-reversal-char-counter"
                style={{
                  fontSize: 12,
                  fontWeight: 600,
                  color: isReasonValid ? '#047857' : '#b45309',
                  fontVariantNumeric: 'tabular-nums',
                }}
              >
                ({trimmedLen}/10 ký tự)
              </span>
            </div>
            <textarea
              rows={3}
              data-testid="input-kit-reversal-reason"
              aria-label="Lý do đảo lệnh lắp ráp"
              value={reversalReason}
              onChange={(e) => setReversalReason(e.target.value)}
              placeholder="Nhập lý do đảo lệnh / xả bộ linh kiện hoàn kệ (tối thiểu 10 ký tự)..."
              style={{ width: '100%', boxSizing: 'border-box', resize: 'vertical' }}
            />
          </label>

          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              gap: 12,
              paddingTop: 8,
              borderTop: '1px solid #e2e8f0',
            }}
          >
            <button
              type="button"
              className="ui-btn ui-btn-secondary"
              disabled={busy}
              onClick={onClose}
            >
              Hủy bỏ
            </button>
            <button
              type="submit"
              data-testid="btn-confirm-kit-reversal"
              disabled={!isReasonValid || busy}
              style={{
                background: isReasonValid ? '#dc2626' : '#fca5a5',
                color: '#ffffff',
                borderColor: isReasonValid ? '#b91c1c' : '#fca5a5',
                fontWeight: 700,
                cursor: !isReasonValid || busy ? 'not-allowed' : 'pointer',
              }}
            >
              {busy ? 'Đang đảo lệnh...' : 'Xác nhận đảo lệnh'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
