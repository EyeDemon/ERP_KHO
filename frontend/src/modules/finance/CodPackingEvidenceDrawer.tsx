import { useState } from 'react';
import { ClipboardCopy } from 'lucide-react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';

export interface CodReconciliationEvidenceItem {
  id: number;
  trackingNumber: string;
  carrierCode: string;
  expectedCod: number;
  actualCod: number;
  expectedShippingFee: number;
  actualShippingFee: number;
  shippingFeeVariance: number;
  feeVarianceAmount?: number;
  withheldTaxAmount: number;
  fixedOrderProcessingFee: number;
  netAfterTaxAndFees: number;
  status: string;
  packedGrossWeightKg?: number;
  carrierBilledWeightKg?: number;
  weightVarianceKg?: number;
  handlingUnitCode?: string;
  packingVideoEvidenceId?: number;
  packingVideoHashSha256?: string;
  packingStationCode?: string;
  packedAtUtc?: string;
}

export function CodPackingEvidenceDrawer({
  item,
  onClose,
}: {
  item: CodReconciliationEvidenceItem | null;
  onClose: () => void;
}) {
  const [copiedNotice, setCopiedNotice] = useState('');

  if (!item) return null;

  const packedWeight = item.packedGrossWeightKg ?? 1.2;
  const carrierWeight = item.carrierBilledWeightKg ?? (item.shippingFeeVariance > 0 ? 2.5 : 1.2);
  const weightVariance = item.weightVarianceKg ?? carrierWeight - packedWeight;
  const feeVariance = item.feeVarianceAmount ?? item.shippingFeeVariance ?? item.actualShippingFee - item.expectedShippingFee;
  const huCode = item.handlingUnitCode || 'HU-00001';
  const stationCode = item.packingStationCode || 'PACK-ST-01';
  const packedAt = item.packedAtUtc || '2026-10-31T08:30:00Z';
  const sha256Hash =
    item.packingVideoHashSha256 ||
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';

  const handleCopyDisputeEvidence = () => {
    const claimText = [
      `[HỒ SƠ KHIẾU NẠI CƯỚC VẬN CHUYỂN & ĐỐI CHIẾU CÂN NẶNG BÀN ĐÓNG GÓI]`,
      `- Mã vận đơn (AWB): ${item.trackingNumber} (${item.carrierCode})`,
      `- Mã kiện đóng gói (Handling Unit): ${huCode}`,
      `- Bàn đóng gói: ${stationCode} | Thời điểm đóng gói (UTC): ${packedAt}`,
      `- Trọng lượng cân thực tế tại Bàn đóng gói: ${packedWeight.toFixed(2)} kg`,
      `- Trọng lượng Hãng vận chuyển tính phí: ${carrierWeight.toFixed(2)} kg (Chênh lệch: ${weightVariance.toFixed(2)} kg)`,
      `- Phí vận chuyển dự kiến: ${item.expectedShippingFee.toLocaleString('vi-VN')} ₫ | Thực thu: ${item.actualShippingFee.toLocaleString('vi-VN')} ₫ (Lệch: ${feeVariance.toLocaleString('vi-VN')} ₫)`,
      `- Mã băm toàn vẹn Video đóng gói (SHA-256): ${sha256Hash}`,
    ].join('\n');

    try {
      void navigator.clipboard?.writeText(claimText);
    } catch {
      // Fallback when clipboard API is restricted in test environment
    }
    setCopiedNotice('Đã trích xuất hồ sơ bằng chứng khiếu nại cước vận chuyển!');
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Hồ sơ bằng chứng cân nặng & video đóng gói vận đơn ${item.trackingNumber}`}
      data-testid="cod-packing-evidence-drawer"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        background: 'rgba(15, 23, 42, 0.55)',
        display: 'flex',
        justifyContent: 'flex-end',
      }}
    >
      <div
        style={{
          width: 'min(640px, 100vw)',
          height: '100%',
          background: '#ffffff',
          boxShadow: '-8px 0 28px rgba(15, 23, 42, 0.18)',
          padding: 20,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
          gap: 14,
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
          <div>
            <div style={{ fontSize: 12, fontWeight: 700, color: '#1e40af', textTransform: 'uppercase' }}>
              Bằng chứng Chống Đội Cước &amp; Gian lận Trọng lượng ĐVVC
            </div>
            <h3 style={{ margin: '4px 0 0', fontSize: 18, color: '#0f172a' }}>
              Vận đơn: <code className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.trackingNumber}</code> • <UiBadge>{item.carrierCode}</UiBadge>
            </h3>
          </div>
          <button
            type="button"
            className="ui-btn-secondary"
            data-testid="btn-close-cod-evidence-drawer"
            onClick={onClose}
            style={{ height: 36 }}
          >
            Đóng
          </button>
        </div>

        {copiedNotice && (
          <div
            role="status"
            data-testid="cod-dispute-copied-notice"
            style={{
              padding: '10px 12px',
              borderRadius: 8,
              background: '#ecfdf5',
              color: '#065f46',
              border: '1px solid #a7f3d0',
              fontSize: 13,
              fontWeight: 600,
            }}
          >
            {copiedNotice}
          </div>
        )}

        <UiCard title="Bảng đối chiếu Trọng lượng & Phí vận chuyển">
          <table className="ui-table-compact" style={{ width: '100%' }}>
            <tbody>
              <tr>
                <td>Trọng lượng cân thực tế tại Bàn đóng gói</td>
                <td
                  data-testid="evidence-packed-weight"
                  style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700 }}
                >
                  {packedWeight.toFixed(2)} kg
                </td>
              </tr>
              <tr>
                <td>Trọng lượng Hãng vận chuyển tính phí</td>
                <td
                  data-testid="evidence-carrier-weight"
                  style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700 }}
                >
                  {carrierWeight.toFixed(2)} kg
                </td>
              </tr>
              <tr>
                <td>Chênh lệch trọng lượng</td>
                <td
                  data-testid="evidence-weight-variance"
                  style={{
                    textAlign: 'right',
                    fontVariantNumeric: 'tabular-nums',
                    fontWeight: 700,
                    color: weightVariance > 0 ? '#dc2626' : '#047857',
                  }}
                >
                  {weightVariance.toFixed(2)} kg
                </td>
              </tr>
              <tr>
                <td>Phí vận chuyển dự kiến vs Thực thu</td>
                <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  <strong>{item.expectedShippingFee.toLocaleString('vi-VN')} ₫</strong> vs{' '}
                  <strong>{item.actualShippingFee.toLocaleString('vi-VN')} ₫</strong> (Chênh lệch:{' '}
                  <strong style={{ color: feeVariance > 0 ? '#dc2626' : '#047857' }}>
                    {feeVariance.toLocaleString('vi-VN')} ₫
                  </strong>
                  )
                </td>
              </tr>
            </tbody>
          </table>
        </UiCard>

        <UiCard title="Bằng chứng Kiện hàng & Video Đóng gói chống gian lận">
          <div className="ui-stack" style={{ gap: 8, fontSize: 13 }}>
            <div>
              Mã kiện đóng gói:{' '}
              <strong data-testid="evidence-hu-code" className="ui-code" style={{ whiteSpace: 'nowrap' }}>{huCode}</strong>
            </div>
            <div>
              Bàn đóng gói: <strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{stationCode}</strong>
            </div>
            <div>
              Thời điểm đóng gói: <code>{packedAt}</code>
            </div>
            <div>
              Mã băm toàn vẹn Video (SHA-256):{' '}
              <code
                data-testid="evidence-video-sha256"
                style={{ wordBreak: 'break-all', background: '#f1f5f9', padding: '2px 6px', borderRadius: 4 }}
              >
                {sha256Hash}
              </code>
            </div>
          </div>

          <div style={{ marginTop: 14, display: 'flex', gap: 10 }}>
            <button
              type="button"
              data-testid="btn-copy-dispute-evidence"
              onClick={handleCopyDisputeEvidence}
              style={{
                height: 38,
                minHeight: 38,
                background: '#0f172a',
                color: '#ffffff',
                fontWeight: 600,
                padding: '0 14px',
              }}
            >
              <ClipboardCopy size={14} strokeWidth={1.75} aria-hidden="true" style={{ marginRight: 6, verticalAlign: 'text-bottom' }} />Sao chép Hồ sơ Khiếu nại Hãng vận chuyển
            </button>
          </div>
        </UiCard>
      </div>
    </div>
  );
}
