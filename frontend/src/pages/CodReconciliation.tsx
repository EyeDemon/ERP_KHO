import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';

type CodLedgerItem = {
  id: number;
  trackingNumber: string;
  carrierCode: string;
  expectedCod: number;
  actualCod: number;
  expectedShippingFee: number;
  actualShippingFee: number;
  shippingFeeVariance: number;
  withheldTaxAmount: number;
  fixedOrderProcessingFee: number;
  netAfterTaxAndFees: number;
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
  if (upper === 'FEE_DISCREPANCY' || upper === 'DISCREPANCY') return 'Cảnh báo lệch phí';
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
    totalWithheldTaxAmount: 0,
    totalVatWithheldAmount: 0,
    totalPitWithheldAmount: 0,
    totalFixedOrderProcessingFee: 0,
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

      return {
        id: Number(item.id ?? idx + 1),
        trackingNumber: String(item.trackingNumber ?? item.awbCode ?? item.shipmentCode ?? `AWB-${idx + 1}`),
        carrierCode: String(item.carrierCode ?? item.carrierName ?? 'GHN'),
        expectedCod,
        actualCod,
        expectedShippingFee,
        actualShippingFee,
        shippingFeeVariance: variance,
        withheldTaxAmount,
        fixedOrderProcessingFee,
        netAfterTaxAndFees,
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
        mapped.filter((x) => x.shippingFeeVariance !== 0 || x.expectedCod !== x.actualCod).length,
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
        (mapped.length > 0
          ? mapped.reduce((s, i) => s + i.withheldTaxAmount, 0)
          : Math.round(totalExpectedCod * 0.015)),
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

    setLedgers(mapped);
    setSummary({
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
        description="Đối chiếu tự động sổ cái vận đơn nội bộ với bảng kê quyết toán của Đơn vị Vận chuyển (GHN, GHTK, ViettelPost, SPX) để phát hiện lệch COD, đội cước cân nặng, khấu trừ thuế sàn 1,5% (NĐ 70/2025) và đơn hoàn quá hạn."
      />

      {error && <div role="alert">{error}</div>}
      {notice && (
        <p role="status" className="ui-success-text" data-testid="cod-reconciliation-notice">
          {notice}
        </p>
      )}
      {loading && <p role="status">Đang tải sổ cái đối soát COD 3 chiều...</p>}

      <UiMetricGrid>
        <UiMetric value={formatCurrencyVnd(summary.totalExpectedCod)} label="Tổng COD phải thu theo phiếu xuất" />
        <UiMetric value={formatCurrencyVnd(summary.totalActualCod)} label="Tổng COD thực nhận từ ĐVVC" />
        <UiMetric value={formatCurrencyVnd(summary.totalShippingFeeVariance)} label="Chênh lệch phí vận chuyển (Đội cước)" />
        <UiMetric
          value={`${summary.discrepancyCount} vận đơn`}
          label={`Cảnh báo lệch phí / chậm hoàn (Tổng ${summary.totalCount} vận đơn)`}
        />
      </UiMetricGrid>

      <UiCard
        title="Khấu trừ Thuế Sàn TMĐT Tại Nguồn 1,5% (Nghị định 70/2025/NĐ-CP) & Phí Hạ tầng Đơn hàng Cố định"
        subtitle="Tự động bóc tách 1% Thuế GTGT + 0,5% Thuế TNCN do sàn TMĐT khấu trừ nộp thay và phí hạ tầng xử lý đơn cố định 3.000 ₫/đơn"
      >
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
            gap: 12,
          }}
        >
          <div
            style={{
              padding: '12px 14px',
              borderRadius: 10,
              border: '1px solid #cbd5e1',
              background: '#f8fafc',
            }}
          >
            <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>
              Thuế sàn khấu trừ tại nguồn (1,5% - NĐ 70/2025)
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {formatCurrencyVnd(summary.totalWithheldTaxAmount)}
            </div>
            <div style={{ fontSize: 12, color: '#334155', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              Tách bạch: <strong>1% GTGT: {formatCurrencyVnd(summary.totalVatWithheldAmount)}</strong> •{' '}
              <strong>0,5% TNCN: {formatCurrencyVnd(summary.totalPitWithheldAmount)}</strong>
            </div>
          </div>

          <div
            style={{
              padding: '12px 14px',
              borderRadius: 10,
              border: '1px solid #cbd5e1',
              background: '#f8fafc',
            }}
          >
            <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>
              Phí hạ tầng xử lý đơn cố định (3.000đ/đơn)
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {formatCurrencyVnd(summary.totalFixedOrderProcessingFee)}
            </div>
            <div style={{ fontSize: 12, color: '#334155', marginTop: 4 }}>
              Áp dụng định mức cố định 3.000 ₫ trên mỗi đơn hàng giao thành công
            </div>
          </div>
        </div>
      </UiCard>

      <UiCard
        title="Tải lên Bảng kê Quyết toán ĐVVC (.xlsx / .csv) & Chạy Đối soát Tự động 3 Chiều"
        subtitle="Gửi tệp nhị phân bảng kê đối soát lên máy chủ để khớp từng mã vận đơn AWB"
      >
        <form onSubmit={handleUploadReconciliationStatement} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            Đơn vị vận chuyển (ĐVVC)
            <select
              aria-label="Đơn vị vận chuyển đối soát"
              value={carrierCode}
              onChange={(e) => setCarrierCode(e.target.value)}
            >
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

      <UiCard title={`Sổ cái Đối soát Chi tiết Từng Vận đơn • ${ledgers.length} vận đơn`}>
        {ledgers.length === 0 ? (
          <p className="ui-muted-text">Chưa có bản ghi vận đơn COD cần đối soát trong kỳ hiện tại.</p>
        ) : (
          <div style={{ width: '100%', overflowX: 'hidden' }}>
            <table
              className="ui-table-compact"
              aria-label="Bảng chi tiết đối soát vận đơn COD"
              style={{ width: '100%', tableLayout: 'fixed', borderCollapse: 'collapse' }}
            >
              <colgroup>
                <col style={{ width: '14%' }} />
                <col style={{ width: '7%' }} />
                <col style={{ width: '13%' }} />
                <col style={{ width: '13%' }} />
                <col style={{ width: '24%' }} />
                <col style={{ width: '12%' }} />
                <col style={{ width: '17%' }} />
              </colgroup>
              <thead>
                <tr>
                  <th>MÃ VẬN ĐƠN</th>
                  <th>ĐVVC</th>
                  <th style={{ textAlign: 'right' }}>COD PHẢI THU</th>
                  <th style={{ textAlign: 'right' }}>COD THỰC NHẬN</th>
                  <th style={{ textAlign: 'right', whiteSpace: 'normal', lineHeight: 1.25 }}>Thuế khấu trừ 1,5% &amp; Phí cố định</th>
                  <th style={{ textAlign: 'right' }}>LỆCH PHÍ VC</th>
                  <th>TRẠNG THÁI</th>
                </tr>
              </thead>
              <tbody>
                {ledgers.map((row) => (
                  <tr key={`${row.trackingNumber}-${row.id}`}>
                    <td style={{ wordBreak: 'break-word' }}>
                      <strong>{row.trackingNumber}</strong>
                    </td>
                    <td>
                      <UiBadge>{row.carrierCode}</UiBadge>
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      {formatCurrencyVnd(row.expectedCod)}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      {formatCurrencyVnd(row.actualCod)}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <div>
                        <strong>{formatCurrencyVnd(row.withheldTaxAmount + row.fixedOrderProcessingFee)}</strong>
                      </div>
                      <small style={{ color: '#64748b', fontSize: 11 }}>
                        Thuế 1,5%: {formatCurrencyVnd(row.withheldTaxAmount)} • Phí: {formatCurrencyVnd(row.fixedOrderProcessingFee)}
                      </small>
                    </td>
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
                    <td style={{ whiteSpace: 'normal', wordBreak: 'break-word' }}>
                      <UiBadge tone={statusBadgeTone(row.status)}>{statusLabelVi(row.status)}</UiBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </UiCard>
    </UiPage>
  );
};

export default CodReconciliation;
