import React, { useEffect } from 'react';

interface MisaEinvoiceDraftModalProps {
  open: boolean;
  busy: boolean;
  buyerTaxCode: string;
  buyerName: string;
  onCancel: () => void;
  onConfirm: () => void;
}

export const MisaEinvoiceDraftModal: React.FC<MisaEinvoiceDraftModalProps> = ({
  open,
  busy,
  buyerTaxCode,
  buyerName,
  onCancel,
  onConfirm,
}) => {
  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [open, busy, onCancel]);

  if (!open) return null;

  const resolvedBuyerName = buyerName.trim() || 'Khách hàng Doanh nghiệp';

  return (
    <div
      role="presentation"
      onClick={() => {
        if (!busy) onCancel();
      }}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1200,
        background: 'rgba(15, 23, 42, 0.55)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="einvoice-draft-modal-title"
        data-testid="modal-confirm-einvoice-draft"
        onClick={(e) => e.stopPropagation()}
        style={{
          width: 'min(520px, 100%)',
          borderRadius: 12,
          background: '#ffffff',
          border: '1px solid #cbd5e1',
          boxShadow: '0 20px 40px rgba(15, 23, 42, 0.22)',
          padding: 20,
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
        }}
      >
        <div>
          <div style={{ fontSize: 12, fontWeight: 700, color: '#1d4ed8', textTransform: 'uppercase' }}>
            Kiểm tra trước khi khởi tạo XML hóa đơn điện tử
          </div>
          <h3 id="einvoice-draft-modal-title" style={{ margin: '4px 0 0', fontSize: 18, color: '#0f172a' }}>
            Xác nhận phát hành HĐĐT Nháp NĐ 70/2025
          </h3>
        </div>

        <p style={{ margin: 0, fontSize: 13.5, color: '#334155', lineHeight: 1.55 }}>
          Vui lòng rà soát thông tin pháp lý người mua trước khi sinh bản nháp XML chuẩn Cơ quan Thuế:
        </p>

        <div
          style={{
            padding: '12px 14px',
            borderRadius: 8,
            background: '#f8fafc',
            border: '1px solid #e2e8f0',
            display: 'grid',
            gap: 8,
            fontSize: 13,
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
            <span style={{ color: '#475569' }}>Mã số thuế người mua:</span>
            <strong style={{ color: '#0f172a', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace' }}>
              {buyerTaxCode.trim()}
            </strong>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
            <span style={{ color: '#475569' }}>Tên đơn vị mua hàng:</span>
            <strong style={{ color: '#0f172a', textAlign: 'right' }}>{resolvedBuyerName}</strong>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
            <span style={{ color: '#475569' }}>Chuẩn định dạng XML:</span>
            <strong style={{ color: '#1e3a8a', textAlign: 'right' }}>
              Nghị định 70/2025/NĐ-CP &amp; Thông tư 32/2025/TT-BTC
            </strong>
          </div>
        </div>

        <div style={{ fontSize: 12.5, color: '#475569', background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: 8, padding: '8px 12px' }}>
          Bản nháp chưa ký số Token HSM và chưa cấp mã chính thức từ Tổng cục Thuế; kế toán có thể kiểm tra lại trước khi ký phát hành.
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 4 }}>
          <button
            type="button"
            className="ui-btn-secondary"
            disabled={busy}
            onClick={onCancel}
            style={{ minHeight: 40, padding: '8px 16px' }}
          >
            Hủy bỏ
          </button>
          <button
            type="button"
            className="ui-primary-button"
            data-testid="btn-confirm-einvoice-draft"
            disabled={busy}
            onClick={onConfirm}
            style={{ minHeight: 40, padding: '8px 16px', background: '#1d4ed8', borderColor: '#1d4ed8', color: '#ffffff', fontWeight: 600 }}
          >
            {busy ? 'Đang phát hành nháp...' : 'Xác nhận phát hành nháp'}
          </button>
        </div>
      </div>
    </div>
  );
};
