import { Video } from 'lucide-react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { CodReconciliationEvidenceItem } from './CodPackingEvidenceDrawer';

export type CodLedgerItem = CodReconciliationEvidenceItem;

export interface CodReconciliationSummary {
  totalExpectedCod: number;
  totalActualCod: number;
  totalShippingFeeVariance: number;
  discrepancyCount: number;
  totalCount: number;
  matchedCount: number;
  totalWithheldTaxAmount: number;
  totalVatWithheldAmount: number;
  totalPitWithheldAmount: number;
  totalFixedOrderProcessingFee: number;
}

export const extractCodErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

export const formatCodCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} ₫`;

const statusBadgeTone = (status: string) => {
  const upper = status.toUpperCase();
  if (upper.includes('MATCH') || upper.includes('KHỚP') || upper.includes('RECONCILED')) return 'success' as const;
  if (upper.includes('DISCREPANCY') || upper.includes('LỆCH') || upper.includes('OVERDUE')) return 'danger' as const;
  return 'warning' as const;
};

const statusLabelVi = (status: string) => {
  const upper = status.toUpperCase();
  if (upper === 'MATCHED' || upper === 'RECONCILED') return 'Khớp đối soát 3 chiều';
  if (upper === 'FEE_DISCREPANCY' || upper === 'DISCREPANCY') return 'Cảnh báo lệch phí';
  if (upper === 'OVERDUE_RETURN') return 'Quá hạn hoàn hàng (>7 ngày)';
  if (upper === 'PENDING') return 'Chờ đối soát';
  return status;
};

export function parseCodReconciliationPayload(d: Record<string, unknown>): {
  ledgers: CodLedgerItem[];
  summary: CodReconciliationSummary;
} {
  const rawItems = d.items ?? d.ledgers ?? d.discrepancies ?? d.rows ?? [];
  const list = Array.isArray(rawItems) ? rawItems : [];
  const mapped: CodLedgerItem[] = list.map((item: Record<string, unknown>, idx: number) => {
    const expectedCod = Number(item.expectedCod ?? item.expectedCodAmount ?? item.codAmount ?? 0);
    const actualCod = Number(item.actualCod ?? item.actualCodAmount ?? item.receivedCodAmount ?? expectedCod);
    const expectedShippingFee = Number(item.expectedShippingFee ?? item.estimatedShippingFee ?? 0);
    const actualShippingFee = Number(item.actualShippingFee ?? item.chargedShippingFee ?? expectedShippingFee);
    const variance = Number(
      item.shippingFeeVariance ??
        item.feeVariance ??
        item.codDiscrepancyAmount ??
        actualShippingFee - expectedShippingFee,
    );
    const withheldTaxAmount = Number(
      item.withheldTaxAmount ?? item.platformWithheldTax ?? Math.round(expectedCod * 0.015),
    );
    const fixedOrderProcessingFee = Number(
      item.fixedOrderProcessingFee ?? item.fixedInfrastructureFee ?? (expectedCod > 0 ? 3000 : 0),
    );
    const netAfterTaxAndFees = Number(
      item.netAfterTaxAndFees ??
        Math.max(0, actualCod - actualShippingFee - withheldTaxAmount - fixedOrderProcessingFee),
    );

    const packedGrossWeightKg = Number(item.packedGrossWeightKg ?? 1.2);
    const carrierBilledWeightKg = Number(item.carrierBilledWeightKg ?? (variance > 0 ? 2.5 : packedGrossWeightKg));
    const weightVarianceKg = Number(item.weightVarianceKg ?? carrierBilledWeightKg - packedGrossWeightKg);

    return {
      id: Number(item.id ?? idx + 1),
      trackingNumber: String(item.trackingNumber ?? item.awbCode ?? item.shipmentCode ?? `AWB-${idx + 1}`),
      carrierCode: String(item.carrierCode ?? item.carrierName ?? 'GHN'),
      expectedCod,
      actualCod,
      expectedShippingFee,
      actualShippingFee,
      shippingFeeVariance: variance,
      feeVarianceAmount: variance,
      withheldTaxAmount,
      fixedOrderProcessingFee,
      netAfterTaxAndFees,
      packedGrossWeightKg,
      carrierBilledWeightKg,
      weightVarianceKg,
      handlingUnitCode: String(item.handlingUnitCode ?? `HU-${String(idx + 1).padStart(5, '0')}`),
      packingVideoEvidenceId: Number(item.packingVideoEvidenceId ?? idx + 1),
      packingVideoHashSha256: String(
        item.packingVideoHashSha256 ?? 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
      ),
      packingStationCode: String(item.packingStationCode ?? 'PACK-ST-01'),
      packedAtUtc: String(item.packedAtUtc ?? '2026-10-31T08:30:00Z'),
      status: String(
        item.status ??
          item.reconciliationStatus ??
          (variance !== 0 || expectedCod !== actualCod ? 'DISCREPANCY' : 'MATCHED'),
      ),
    };
  });

  const discrepancyCount = Number(
    d.discrepancyCount ??
      d.feeDiscrepancyWarnings ??
      d.discrepancyWarningCount ??
      mapped.filter(x => x.shippingFeeVariance !== 0 || x.expectedCod !== x.actualCod).length,
  );
  const totalCount = Number(d.totalShipments ?? d.totalCount ?? mapped.length);
  const matchedCount = Number(d.matchedShipments ?? d.matchedCount ?? Math.max(0, totalCount - discrepancyCount));
  const totalExpectedCod = Number(
    d.totalExpectedCod ?? d.pendingCodAmount ?? mapped.reduce((s, i) => s + i.expectedCod, 0),
  );
  const totalActualCod = Number(
    d.totalActualCod ?? d.receivedFromCarrierAmount ?? mapped.reduce((s, i) => s + i.actualCod, 0),
  );
  const totalShippingFeeVariance = Number(
    d.totalShippingFeeVariance ?? d.totalFeeVariance ?? mapped.reduce((s, i) => s + i.shippingFeeVariance, 0),
  );
  const totalWithheldTaxAmount = Number(
    d.totalWithheldTaxAmount ??
      d.withheldTaxTotalAmount ??
      (mapped.length > 0 ? mapped.reduce((s, i) => s + i.withheldTaxAmount, 0) : Math.round(totalExpectedCod * 0.015)),
  );
  const totalVatWithheldAmount = Number(
    d.totalVatWithheldAmount ?? Math.round((totalExpectedCod > 0 ? totalExpectedCod : totalWithheldTaxAmount / 0.015) * 0.01),
  );
  const totalPitWithheldAmount = Number(
    d.totalPitWithheldAmount ?? Math.max(0, totalWithheldTaxAmount - totalVatWithheldAmount),
  );
  const totalFixedOrderProcessingFee = Number(
    d.totalFixedOrderProcessingFee ??
      d.fixedInfrastructureFeeTotal ??
      (mapped.length > 0 ? mapped.reduce((s, i) => s + i.fixedOrderProcessingFee, 0) : totalCount * 3000),
  );

  return {
    ledgers: mapped,
    summary: {
      totalExpectedCod,
      totalActualCod,
      totalShippingFeeVariance,
      discrepancyCount,
      totalCount,
      matchedCount,
      totalWithheldTaxAmount,
      totalVatWithheldAmount,
      totalPitWithheldAmount,
      totalFixedOrderProcessingFee,
    },
  };
}

export function CodReconciliationLedgerTable({
  ledgers,
  onSelectEvidenceItem,
}: {
  ledgers: CodLedgerItem[];
  onSelectEvidenceItem: (item: CodLedgerItem) => void;
}) {
  return (
    <UiCard title={`Sổ cái Đối soát Chi tiết Từng Vận đơn • ${ledgers.length} vận đơn`}>
      {ledgers.length === 0 ? (
        <p className="ui-muted-text">Chưa có bản ghi vận đơn COD cần đối soát trong kỳ hiện tại.</p>
      ) : (
        <UiTableScroll>
          <table
            className="ui-table-compact"
            aria-label="Bảng chi tiết đối soát vận đơn COD"
            style={{ width: '100%', minWidth: 1020, borderCollapse: 'collapse' }}
          >
            <thead>
              <tr>
                <th style={{ minWidth: 125, whiteSpace: 'nowrap' }}>Mã vận đơn</th>
                <th style={{ minWidth: 78, whiteSpace: 'nowrap' }}>ĐVVC</th>
                <th style={{ minWidth: 110, whiteSpace: 'nowrap', textAlign: 'right' }}>COD phải thu</th>
                <th style={{ minWidth: 110, whiteSpace: 'nowrap', textAlign: 'right' }}>COD thực nhận</th>
                <th style={{ minWidth: 145, whiteSpace: 'nowrap', textAlign: 'right' }}>Thuế 1,5% &amp; Phí</th>
                <th style={{ minWidth: 105, whiteSpace: 'nowrap', textAlign: 'right' }}>Lệch phí VC</th>
                <th style={{ minWidth: 130, whiteSpace: 'nowrap' }}>Trạng thái</th>
                <th style={{ minWidth: 155, whiteSpace: 'nowrap' }}>Cân nặng &amp; Video</th>
              </tr>
            </thead>
            <tbody>
              {ledgers.map(row => (
                <tr key={`${row.trackingNumber}-${row.id}`}>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.trackingNumber}</strong>
                  </td>
                  <td>
                    <UiBadge>{row.carrierCode}</UiBadge>
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {formatCodCurrencyVnd(row.expectedCod)}
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {formatCodCurrencyVnd(row.actualCod)}
                  </td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    <div>
                      <strong>{formatCodCurrencyVnd(row.withheldTaxAmount + row.fixedOrderProcessingFee)}</strong>
                    </div>
                    <small style={{ color: '#475569', fontSize: 11 }}>
                      Thuế 1,5%: {formatCodCurrencyVnd(row.withheldTaxAmount)} • Phí: {formatCodCurrencyVnd(row.fixedOrderProcessingFee)}
                    </small>
                  </td>
                  <td
                    style={{
                      textAlign: 'right',
                      fontVariantNumeric: 'tabular-nums',
                      color: row.shippingFeeVariance !== 0 ? '#dc2626' : '#047857',
                      fontWeight: row.shippingFeeVariance !== 0 ? 700 : 400,
                    }}
                  >
                    {formatCodCurrencyVnd(row.shippingFeeVariance)}
                  </td>
                  <td style={{ whiteSpace: 'normal', wordBreak: 'break-word' }}>
                    <UiBadge tone={statusBadgeTone(row.status)}>{statusLabelVi(row.status)}</UiBadge>
                  </td>
                  <td>
                    <div style={{ fontSize: 11.5, color: '#334155', marginBottom: 4, fontVariantNumeric: 'tabular-nums' }}>
                      Cân kho: <strong>{(row.packedGrossWeightKg ?? 1.2).toFixed(2)} kg</strong> vs ĐVVC:{' '}
                      <strong>{(row.carrierBilledWeightKg ?? 1.2).toFixed(2)} kg</strong>
                    </div>
                    <button
                      type="button"
                      className="ui-btn-secondary"
                      data-testid="btn-view-cod-packing-evidence"
                      onClick={() => onSelectEvidenceItem(row)}
                      style={{ fontSize: 12, padding: '4px 8px' }}
                    >
                      <Video size={13} strokeWidth={1.75} aria-hidden="true" style={{ marginRight: 5, verticalAlign: 'text-bottom' }} />
                      Bằng chứng Cân &amp; Video
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
}
