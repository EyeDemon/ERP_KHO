import React from 'react';
import { UiCard } from '../../ui/ProductionUi';

export interface CostValuationMisaSectionProps {
  busy: boolean;
  isSinglePageMode: boolean;
  showMisaBanner: boolean;
  einvoiceBuyerTaxCode: string;
  setEinvoiceBuyerTaxCode: (val: string) => void;
  einvoiceBuyerName: string;
  setEinvoiceBuyerName: (val: string) => void;
  einvoiceMstError?: string;
  onExportMisaExcel: () => void;
  onReloadValuation: () => void;
  onIssueDraftEinvoice: (e: React.FormEvent) => void;
}

export const CostValuationMisaSection: React.FC<CostValuationMisaSectionProps> = ({
  busy,
  showMisaBanner,
  einvoiceBuyerTaxCode,
  setEinvoiceBuyerTaxCode,
  einvoiceBuyerName,
  setEinvoiceBuyerName,
  einvoiceMstError,
  onExportMisaExcel,
  onReloadValuation,
  onIssueDraftEinvoice,
}) => (
  <>
    {showMisaBanner && (
      <div
        role="status"
        data-testid="misa-export-active-banner"
        style={{
          padding: '10px 14px',
          borderRadius: 8,
          background: '#eff6ff',
          border: '1px solid #93c5fd',
          color: '#1e3a8a',
          marginBottom: 12,
          fontSize: 13.5,
          fontWeight: 600,
        }}
      >
        Bàn làm việc chuyên trách Kết xuất Bảng kê Kế toán MISA AMIS (.xlsx) và Phát hành Hóa đơn Điện tử Nháp có mã của Cơ quan Thuế theo Nghị định 70/2025/NĐ-CP.
      </div>
    )}

    <UiCard
      title="Kết xuất Bảng kê Kế toán MISA AMIS (.xlsx) & Phát hành Hóa đơn Điện tử Nháp NĐ 70/2025"
      subtitle="Tự động định khoản Nợ/Có TK 1561, 632, 131, 331 và chuẩn bị dữ liệu XML hóa đơn điện tử có mã của Cơ quan Thuế theo Nghị định 70/2025/NĐ-CP"
    >
      <div className="ui-stack" style={{ gap: 12 }}>
        <div className="ui-inline-wrap" style={{ gap: 10 }}>
          <button
            type="button"
            className="ui-primary-button"
            data-testid="btn-export-misa-xlsx"
            disabled={busy}
            onClick={onExportMisaExcel}
            style={{ background: '#1d4ed8', borderColor: '#1d4ed8', color: '#ffffff', fontWeight: 600 }}
          >
            Kết xuất Bảng kê .xlsx Chuẩn MISA AMIS
          </button>
          <button type="button" className="ui-btn-secondary" disabled={busy} onClick={onReloadValuation}>
            Đồng bộ chứng từ hóa đơn
          </button>
        </div>

        <form onSubmit={onIssueDraftEinvoice} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }} noValidate>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Mã số thuế người mua (10 hoặc 13 số)
            <input
              aria-label="Mã số thuế người mua HĐĐT"
              aria-invalid={Boolean(einvoiceMstError)}
              value={einvoiceBuyerTaxCode}
              onChange={e => setEinvoiceBuyerTaxCode(e.target.value)}
              placeholder="VD: 0318294012"
              style={{ minWidth: 240, flex: '1 1 240px', borderColor: einvoiceMstError ? '#dc2626' : undefined }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Tên đơn vị mua hàng
            <input
              aria-label="Tên đơn vị mua hàng HĐĐT"
              value={einvoiceBuyerName}
              onChange={e => setEinvoiceBuyerName(e.target.value)}
              placeholder="Nhập tên doanh nghiệp mua..."
              style={{ minWidth: 260 }}
            />
          </label>
          <button type="submit" className="ui-btn-secondary" data-testid="btn-issue-einvoice-nd70" disabled={busy}>
            Phát hành HĐĐT Nháp NĐ 70/2025
          </button>
        </form>

        {einvoiceMstError && (
          <div
            role="alert"
            data-testid="einvoice-mst-error"
            style={{
              padding: '8px 12px',
              borderRadius: 8,
              background: '#fef2f2',
              border: '1px solid #fecaca',
              color: '#991b1b',
              fontSize: 13,
              fontWeight: 500,
            }}
          >
            {einvoiceMstError}
          </div>
        )}
      </div>
    </UiCard>
  </>
);
