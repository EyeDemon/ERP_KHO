import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type CodLedgerItem = {
  id: number;
  trackingNumber: string;
  carrierCode: string;
  expectedCod: number;
  actualCod: number;
  expectedShippingFee: number;
  actualShippingFee: number;
  shippingFeeVariance: number;
  status: string;
};

const extractErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

const formatCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} đ`;

const statusBadgeTone = (status: string) => {
  const upper = status.toUpperCase();
  if (upper.includes('MATCH') || upper.includes('KHỚP') || upper.includes('RECONCILED')) return 'success' as const;
  if (upper.includes('DISCREPANCY') || upper.includes('LỆCH') || upper.includes('OVERDUE')) return 'danger' as const;
  return 'warning' as const;
};

const statusLabelVi = (status: string) => {
  const upper = status.toUpperCase();
  if (upper === 'MATCHED' || upper === 'RECONCILED') return 'Khớp đối soát 3 chiều';
  if (upper === 'FEE_DISCREPANCY' || upper === 'DISCREPANCY') return 'Cảnh báo lệch phí / COD';
  if (upper === 'OVERDUE_RETURN') return 'Quá hạn hoàn hàng (>7 ngày)';
  if (upper === 'PENDING') return 'Chờ đối soát';
  return status;
};

const CodReconciliation = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [selectedCarrierFile, setSelectedCarrierFile] = useState<File | null>(null);
  const [carrierCode, setCarrierCode] = useState('GHN');
  const [summary, setSummary] = useState({
    totalExpectedCod: 0,
    totalActualCod: 0,
    totalShippingFeeVariance: 0,
    discrepancyCount: 0,
    totalCount: 0,
    matchedCount: 0,
  });
  const [ledgers, setLedgers] = useState<CodLedgerItem[]>([]);

  const applyReconciliationPayload = (d: Record<string, unknown>) => {
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
        (actualShippingFee - expectedShippingFee),
      );
      return {
        id: Number(item.id ?? idx + 1),
        trackingNumber: String(item.trackingNumber ?? item.awbCode ?? item.shipmentCode ?? `AWB-${idx + 1}`),
        carrierCode: String(item.carrierCode ?? item.carrierName ?? 'GHN'),
        expectedCod,
        actualCod,
        expectedShippingFee,
        actualShippingFee,
        shippingFeeVariance: variance,
        status: String(item.status ?? item.reconciliationStatus ?? (variance !== 0 || expectedCod !== actualCod ? 'DISCREPANCY' : 'MATCHED')),
      };
    });

    const discrepancyCount = Number(
      d.discrepancyCount ??
      d.feeDiscrepancyWarnings ??
      d.discrepancyWarningCount ??
      mapped.filter((x) => x.shippingFeeVariance !== 0 || x.expectedCod !== x.actualCod).length,
    );
    const totalCount = Number(d.totalShipments ?? d.totalCount ?? mapped.length);
    const matchedCount = Number(d.matchedShipments ?? d.matchedCount ?? Math.max(0, totalCount - discrepancyCount));

    setLedgers(mapped);
    setSummary({
      totalExpectedCod: Number(d.totalExpectedCod ?? d.pendingCodAmount ?? mapped.reduce((s, i) => s + i.expectedCod, 0)),
      totalActualCod: Number(d.totalActualCod ?? d.receivedFromCarrierAmount ?? mapped.reduce((s, i) => s + i.actualCod, 0)),
      totalShippingFeeVariance: Number(d.totalShippingFeeVariance ?? d.totalFeeVariance ?? mapped.reduce((s, i) => s + i.shippingFeeVariance, 0)),
      discrepancyCount,
      totalCount,
      matchedCount,
    });

    return { matchedCount, totalCount, discrepancyCount };
  };

  const loadCodReconciliation = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient
        .get<Record<string, unknown>>('/api/v1/reports/cod-shipping-reconciliation')
        .catch(() => apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-reconciliation'));
      if (res?.data) {
        applyReconciliationPayload(res.data);
      }
    } catch (err) {
      setError(extractErrorText(err, 'Không thể tải dữ liệu đối soát COD & phí vận chuyển 3 chiều.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadCodReconciliation();
  }, [loadCodReconciliation]);

  const handleUploadReconciliationStatement = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const formData = new FormData();
      if (selectedCarrierFile) {
        formData.append('file', selectedCarrierFile);
      }
      formData.append('carrierCode', carrierCode);
      formData.append('reconcileMode', 'THREE_WAY_COD_AND_MARKETPLACE_FEE');

      const res = await apiClient.post<Record<string, unknown>>(
        '/api/v1/reports/cod-shipping-reconciliation',
        formData,
        { headers: { 'Content-Type': 'multipart/form-data' } },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const stats = applyReconciliationPayload(d);
      const matchRatePct =
        d.matchRatePct !== undefined
          ? Number(d.matchRatePct)
          : stats.totalCount > 0
            ? Math.round((stats.matchedCount * 1000) / stats.totalCount) / 10
            : 100;
      setNotice(
        `Đã đối soát ${stats.matchedCount}/${stats.totalCount} vận đơn (${matchRatePct}%), phát hiện ${stats.discrepancyCount} cảnh báo lệch phí/chậm hoàn.`,
      );
    } catch (err) {
      setError(extractErrorText(err, 'Không thể tải lên và đối soát bảng kê COD của đơn vị vận chuyển.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Tài chính & Kế toán Kho"
        title="Đối soát Tiền Thu Hộ (COD) & Phí Vận chuyển 3 Chiều"
        description="Đối chiếu tự động sổ cái vận đơn nội bộ (ShipmentFinancialLedgers) với bảng kê quyết toán của Đơn vị Vận chuyển (GHN, GHTK, ViettelPost, SPX) để phát hiện lệch COD, đội cước cân nặng và đơn hoàn quá hạn."
      />

      {error && <div role="alert">{error}</div>}
      {notice && <p role="status" className="ui-success-text" data-testid="cod-reconciliation-notice">{notice}</p>}
      {loading && <p role="status">Đang tải sổ cái đối soát COD 3 chiều...</p>}

      <UiMetricGrid>
        <UiMetric value={formatCurrencyVnd(summary.totalExpectedCod)} label="Tổng COD phải thu theo phiếu xuất" />
        <UiMetric value={formatCurrencyVnd(summary.totalActualCod)} label="Tổng COD thực nhận từ ĐVVC" />
        <UiMetric value={formatCurrencyVnd(summary.totalShippingFeeVariance)} label="Chênh lệch phí vận chuyển (Đội cước)" />
        <UiMetric value={`${summary.discrepancyCount} vận đơn`} label={`Cảnh báo lệch phí / chậm hoàn (Tổng ${summary.totalCount} vận đơn)`} />
      </UiMetricGrid>

      <UiCard
        title="Tải lên Bảng kê Quyết toán ĐVVC (.xlsx / .csv) & Chạy Đối soát Tự động 3 Chiều"
        subtitle="Gửi tệp nhị phân bảng kê đối soát qua FormData lên máy chủ để khớp từng mã vận đơn AWB"
      >
        <form onSubmit={handleUploadReconciliationStatement} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Đơn vị vận chuyển (ĐVVC)
            <select aria-label="Đơn vị vận chuyển đối soát" value={carrierCode} onChange={(e) => setCarrierCode(e.target.value)}>
              <option value="GHN">Giao Hàng Nhanh (GHN)</option>
              <option value="GHTK">Giao Hàng Tiết Kiệm (GHTK)</option>
              <option value="VTP">ViettelPost (VTP)</option>
              <option value="SPX">SPX Express (Shopee)</option>
              <option value="J&T">J&amp;T Express</option>
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Tệp bảng kê quyết toán ĐVVC (.xlsx, .csv)
            <input
              aria-label="Tệp bảng kê quyết toán COD"
              data-testid="input-cod-statement-file"
              type="file"
              accept=".xlsx,.xls,.csv"
              onChange={(e) => setSelectedCarrierFile(e.target.files?.[0] ?? null)}
            />
          </label>
          <button type="submit" data-testid="btn-submit-cod-reconciliation" disabled={busy}>
            {busy ? 'Đang đối soát...' : 'Chạy Đối soát 3 Chiều Bảng kê COD'}
          </button>
          <button type="button" className="ui-btn-secondary" disabled={busy} onClick={() => void loadCodReconciliation()}>
            Tải lại Dữ liệu Đối soát
          </button>
        </form>
      </UiCard>

      <UiCard title={`Sổ cái Đối soát Chi tiết Từng Vận đơn (ShipmentFinancialLedgers) • ${ledgers.length} vận đơn`}>
        {ledgers.length === 0 ? (
          <p className="ui-muted-text">Chưa có bản ghi vận đơn COD cần đối soát trong kỳ hiện tại.</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng chi tiết đối soát vận đơn COD">
              <thead>
                <tr>
                  <th>Mã vận đơn (TrackingNumber)</th>
                  <th>ĐVVC</th>
                  <th style={{ textAlign: 'right' }}>COD kỳ vọng (ExpectedCod)</th>
                  <th style={{ textAlign: 'right' }}>COD thực nhận (ActualCod)</th>
                  <th style={{ textAlign: 'right' }}>Chênh lệch phí VC (ShippingFeeVariance)</th>
                  <th>Trạng thái (Status)</th>
                </tr>
              </thead>
              <tbody>
                {ledgers.map((row) => (
                  <tr key={`${row.trackingNumber}-${row.id}`}>
                    <td><strong>{row.trackingNumber}</strong></td>
                    <td><UiBadge>{row.carrierCode}</UiBadge></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{formatCurrencyVnd(row.expectedCod)}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{formatCurrencyVnd(row.actualCod)}</td>
                    <td
                      style={{
                        textAlign: 'right',
                        fontVariantNumeric: 'tabular-nums',
                        color: row.shippingFeeVariance !== 0 ? '#dc2626' : '#059669',
                        fontWeight: row.shippingFeeVariance !== 0 ? 700 : 400,
                      }}
                    >
                      {formatCurrencyVnd(row.shippingFeeVariance)}
                    </td>
                    <td>
                      <UiBadge tone={statusBadgeTone(row.status)}>
                        {statusLabelVi(row.status)}
                      </UiBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </UiPage>
  );
};

export default CodReconciliation;
