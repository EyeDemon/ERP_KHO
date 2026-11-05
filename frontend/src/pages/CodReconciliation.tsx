import { useCallback, useEffect, useState } from 'react';
import { Video } from 'lucide-react';
import apiClient from '../services/apiClient';
import { CodPackingEvidenceDrawer } from '../modules/finance/CodPackingEvidenceDrawer';
import {
  CodReconciliationLedgerTable,
  extractCodErrorText,
  formatCodCurrencyVnd,
  parseCodReconciliationPayload,
  type CodLedgerItem,
  type CodReconciliationSummary,
} from '../modules/finance/CodReconciliationHelpersAndTable';
import { UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';

const CodReconciliation = () => {
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [selectedCarrierFile, setSelectedCarrierFile] = useState<File | null>(null);
  const [carrierCode, setCarrierCode] = useState('GHN');
  const [taxpayerType, setTaxpayerType] = useState<'HOUSEHOLD_INDIVIDUAL' | 'ENTERPRISE'>('HOUSEHOLD_INDIVIDUAL');
  const [summary, setSummary] = useState<CodReconciliationSummary>({
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
  const [selectedEvidenceItem, setSelectedEvidenceItem] = useState<CodLedgerItem | null>(null);

  const applyReconciliationPayload = (d: Record<string, unknown>) => {
    const parsed = parseCodReconciliationPayload(d);
    setLedgers(parsed.ledgers);
    setSummary(parsed.summary);
    return {
      matchedCount: parsed.summary.matchedCount,
      totalCount: parsed.summary.totalCount,
      discrepancyCount: parsed.summary.discrepancyCount,
    };
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
      setError(extractCodErrorText(err, 'Không thể tải dữ liệu đối soát COD & phí vận chuyển 3 chiều.'));
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
      formData.append('taxpayerType', taxpayerType);
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
      setError(extractCodErrorText(err, 'Không thể tải lên và đối soát bảng kê COD của đơn vị vận chuyển.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Tài chính & Kế toán Kho"
        title="Đối soát Tiền Thu Hộ (COD) & Phí Vận chuyển 3 Chiều"
        description="Đối chiếu tự động sổ cái vận đơn nội bộ với bảng kê quyết toán của Đơn vị Vận chuyển (GHN, GHTK, ViettelPost, SPX) để phát hiện lệch COD, đội cước cân nặng, khấu trừ thuế sàn 1,5% (Nghị định 117/2025/NĐ-CP) và đơn hoàn quá hạn."
      />

      {error && <div role="alert">{error}</div>}
      {notice && (
        <p role="status" className="ui-success-text" data-testid="cod-reconciliation-notice">
          {notice}
        </p>
      )}
      {loading && <p role="status">Đang tải sổ cái đối soát COD 3 chiều...</p>}

      <UiMetricGrid>
        <UiMetric value={formatCodCurrencyVnd(summary.totalExpectedCod)} label="Tổng COD phải thu theo phiếu xuất" />
        <UiMetric value={formatCodCurrencyVnd(summary.totalActualCod)} label="Tổng COD thực nhận từ ĐVVC" />
        <UiMetric value={formatCodCurrencyVnd(summary.totalShippingFeeVariance)} label="Chênh lệch phí vận chuyển (Đội cước)" />
        <UiMetric value={`${summary.discrepancyCount} vận đơn`} label={`Cảnh báo lệch phí / chậm hoàn (Tổng ${summary.totalCount} vận đơn)`} />
      </UiMetricGrid>

      <UiCard
        title="Khấu trừ thuế Sàn TMĐT (Nghị định 117/2025/NĐ-CP) & Phí Hạ tầng Đơn hàng Cố định"
        subtitle="Tự động bóc tách 1% Thuế GTGT + 0,5% Thuế TNCN do sàn TMĐT khấu trừ nộp thay đối với Hộ/Cá nhân kinh doanh (Nghị định 117/2025/NĐ-CP) và phí hạ tầng xử lý đơn cố định 3.000 ₫/đơn"
      >
        <div style={{ marginBottom: 12 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, maxWidth: 680 }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>Loại hình người nộp thuế trên Sàn TMĐT (Nghị định 117/2025/NĐ-CP)</span>
            <select
              aria-label="Loại hình người nộp thuế COD"
              data-testid="select-taxpayer-type-cod"
              value={taxpayerType}
              onChange={e => setTaxpayerType(e.target.value as 'HOUSEHOLD_INDIVIDUAL' | 'ENTERPRISE')}
            >
              <option value="HOUSEHOLD_INDIVIDUAL">Hộ / Cá nhân kinh doanh (Khấu trừ 1% GTGT + 0,5% TNCN theo NĐ 117/2025/NĐ-CP)</option>
              <option value="ENTERPRISE">Doanh nghiệp / Tổ chức (Tự kê khai 0 ₫ khấu trừ tại nguồn)</option>
            </select>
          </label>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 12 }}>
          <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
            <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>
              Thuế sàn khấu trừ tại nguồn (1,5% - NĐ 117/2025/NĐ-CP)
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {taxpayerType === 'ENTERPRISE' ? '0 ₫ (Doanh nghiệp tự kê khai theo NĐ 117/2025/NĐ-CP)' : formatCodCurrencyVnd(summary.totalWithheldTaxAmount)}
            </div>
            <div style={{ fontSize: 12, color: '#334155', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {taxpayerType === 'ENTERPRISE' ? (
                <span>Doanh nghiệp tự kê khai, nộp thuế GTGT &amp; TNDN (Khấu trừ tại nguồn: <strong>0 ₫</strong>)</span>
              ) : (
                <>
                  Tách bạch: <strong>1% GTGT: {formatCodCurrencyVnd(summary.totalVatWithheldAmount)}</strong> •{' '}
                  <strong>0,5% TNCN: {formatCodCurrencyVnd(summary.totalPitWithheldAmount)}</strong>
                </>
              )}
            </div>
          </div>

          <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
            <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>
              Phí hạ tầng xử lý đơn cố định (3.000 ₫/đơn)
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {formatCodCurrencyVnd(summary.totalFixedOrderProcessingFee)}
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
            <select aria-label="Đơn vị vận chuyển đối soát" value={carrierCode} onChange={e => setCarrierCode(e.target.value)}>
              <option value="GHN">Giao Hàng Nhanh (GHN)</option>
              <option value="GHTK">Giao Hàng Tiết Kiệm (GHTK)</option>
              <option value="VTP">ViettelPost (VTP)</option>
              <option value="SPX">SPX Express (Shopee)</option>
              <option value="J&T">J&amp;T Express</option>
            </select>
          </label>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: '#0f172a' }}>Tệp bảng kê quyết toán ĐVVC (.xlsx, .csv)</span>
            <label style={{ position: 'relative', display: 'inline-flex', alignItems: 'center', gap: 10, minHeight: 38, padding: '6px 12px', border: '1px solid #cbd5e1', borderRadius: 6, background: '#ffffff', cursor: 'pointer', fontSize: 13, color: '#0f172a' }}>
              <span style={{ padding: '4px 10px', borderRadius: 4, background: '#f1f5f9', border: '1px solid #cbd5e1', fontWeight: 600, whiteSpace: 'nowrap' }}>
                Chọn tệp bảng kê...
              </span>
              <span style={{ color: selectedCarrierFile ? '#0f172a' : '#475569', fontWeight: selectedCarrierFile ? 600 : 400 }}>
                {selectedCarrierFile ? selectedCarrierFile.name : 'Chưa chọn tệp nào (.xlsx, .csv)'}
              </span>
              <input aria-label="Tệp bảng kê quyết toán COD" data-testid="input-cod-statement-file" type="file" accept=".xlsx,.xls,.csv" onChange={e => setSelectedCarrierFile(e.target.files?.[0] ?? null)} style={{ position: 'absolute', width: 1, height: 1, opacity: 0, overflow: 'hidden' }} />
            </label>
          </div>
          <button type="submit" data-testid="btn-submit-cod-reconciliation" disabled={busy}>
            {busy ? 'Đang đối soát...' : 'Chạy Đối soát 3 Chiều Bảng kê COD'}
          </button>
          <button type="button" className="ui-btn-secondary" disabled={busy} onClick={() => void loadCodReconciliation()}>
            Tải lại Dữ liệu Đối soát
          </button>
          {ledgers.length > 0 && (
            <button
              type="button"
              className="ui-btn-secondary"
              data-testid="btn-open-first-cod-evidence"
              onClick={() =>
                setSelectedEvidenceItem(
                  ledgers.find(x => x.shippingFeeVariance !== 0 || (x.weightVarianceKg ?? 0) !== 0) ?? ledgers[0],
                )
              }
            >
              <Video size={14} strokeWidth={1.75} aria-hidden="true" style={{ marginRight: 6, verticalAlign: 'text-bottom' }} />
              Xem Bằng chứng Cân &amp; Video Đơn lệch cước
            </button>
          )}
        </form>
      </UiCard>

      <CodReconciliationLedgerTable ledgers={ledgers} onSelectEvidenceItem={setSelectedEvidenceItem} />

      <CodPackingEvidenceDrawer item={selectedEvidenceItem} onClose={() => setSelectedEvidenceItem(null)} />
    </UiPage>
  );
};

export default CodReconciliation;
