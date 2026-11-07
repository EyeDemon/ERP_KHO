import { BarcodeSvg } from '../../components/BarcodeSvg';
import { UiBadge } from '../../ui/ProductionUi';
import type { AccountingEinvoiceData, AwbLabelItem } from './exportReceiptsTypes';

interface ExportReceiptsAwbPreviewAndEinvoicePanelProps {
  awbLabels: AwbLabelItem[];
  handoverTrackingInput: string;
  batchAwbNotice: string | null;
  handoverManifestNotice: string | null;
  misaEinvoiceNotice: string | null;
  accountingEinvoiceData: AccountingEinvoiceData | null;
}

export function ExportReceiptsAwbPreviewAndEinvoicePanel({
  awbLabels,
  handoverTrackingInput,
  batchAwbNotice,
  handoverManifestNotice,
  misaEinvoiceNotice,
  accountingEinvoiceData,
}: ExportReceiptsAwbPreviewAndEinvoicePanelProps) {
  return (
    <>
      {awbLabels.length > 0 && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 10 }}>
          {awbLabels.map((lbl, idx) => (
            <div
              key={`${lbl.awbCode}-${idx}`}
              style={{
                padding: '10px 12px',
                borderRadius: 8,
                border: '1px solid #cbd5e1',
                background: '#f8fafc',
                display: 'flex',
                flexDirection: 'column',
                gap: 4,
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                <strong>{lbl.awbCode} — {lbl.carrierName}</strong>
                <UiBadge tone={lbl.isReprint ? 'warning' : 'success'}>{lbl.printCountLabel}</UiBadge>
              </div>
              {lbl.watermarkText && (
                <div style={{ fontSize: 12, fontWeight: 700, color: '#b45309' }}>{lbl.watermarkText}</div>
              )}
              <div style={{ fontSize: 12.5, color: '#475569' }}>Đơn sàn: <code>{lbl.marketplaceOrderId || '—'}</code> • Khổ nhiệt: 100x150mm</div>
              <div style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a' }}>{lbl.pickSequenceFooter}</div>
              <div style={{ marginTop: 4 }}>
                <BarcodeSvg value={lbl.awbCode} qrCaption="Mã QR vận đơn" />
              </div>
            </div>
          ))}
        </div>
      )}

      <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff', display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
        <div style={{ fontSize: 13 }}>
          <strong>Biên bản Bàn giao Bưu tá Điện tử:</strong>{' '}
          {awbLabels.length > 0
            ? `Vận đơn bàn giao ${awbLabels.map((l) => l.awbCode).join(', ')}`
            : handoverTrackingInput.trim()
              ? `Vận đơn bàn giao ${handoverTrackingInput.trim()}`
              : 'Chưa có vận đơn kết xuất'}{' '}
          • Chặn cứng đơn hủy trên sàn trước khi xuất kho
        </div>
        <UiBadge tone="success">Viettel SInvoice • Mẫu 1/001 • C26TAA</UiBadge>
      </div>

      {batchAwbNotice && (
        <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
          {batchAwbNotice}
        </div>
      )}
      {handoverManifestNotice && (
        <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
          {handoverManifestNotice}
        </div>
      )}
      {misaEinvoiceNotice && (
        <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
          {misaEinvoiceNotice}
        </div>
      )}
      {accountingEinvoiceData && (
        <div
          role="region"
          aria-label="Đồng bộ Kế toán Thông tư 200 và Hóa đơn Điện tử Nghị định 70/2025"
          data-testid="epic90-3-accounting-einvoice-panel"
          style={{
            padding: '10px 14px',
            borderRadius: 8,
            background: '#f0fdf4',
            color: '#065f46',
            border: '1px solid #86efac',
            fontSize: 13,
            lineHeight: 1.5,
          }}
        >
          <div style={{ fontWeight: 700, marginBottom: 4 }}>
            Đồng bộ Kế toán Thông tư 200 ({accountingEinvoiceData.accountingProvider}) &amp; HĐĐT NĐ 70/2025 ({accountingEinvoiceData.einvoiceProvider} • {accountingEinvoiceData.invoiceTemplateCode} • {accountingEinvoiceData.invoiceSeries} #{accountingEinvoiceData.invoiceNumber})
          </div>
          <div style={{ marginBottom: 4 }}>{accountingEinvoiceData.statusLabel}</div>
          {accountingEinvoiceData.circular200JournalEntries && accountingEinvoiceData.circular200JournalEntries.length > 0 && (
            <div style={{ fontSize: 12.5 }}>
              <strong>Bút toán Kép Thông tư 200/2014/TT-BTC:</strong>{' '}
              {accountingEinvoiceData.circular200JournalEntries.map((j, idx) => (
                <span key={idx} style={{ display: 'inline-block', marginRight: 10 }}>
                  [Nợ TK {j.debitAccount} / Có TK {j.creditAccount}: <strong>{(j.debitAmount ?? 0).toLocaleString('vi-VN')} ₫</strong>]
                </span>
              ))}
              <span>• Cân đối Nợ/Có Δ = <strong>{accountingEinvoiceData.journalImbalanceDelta ?? 0}</strong></span>
            </div>
          )}
        </div>
      )}
    </>
  );
}
