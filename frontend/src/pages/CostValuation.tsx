import { useCallback, useEffect, useState } from 'react';
import { useInRouterContext, useLocation } from 'react-router-dom';
import {
  extractCostErrorText,
  mapRawValuationItems,
  parseOwnerGrossProfitSummary,
} from '../modules/finance/CostValuationHelpers';
import {
  CostValuationMisaSection,
  CostValuationWacSection,
  type FinanceSummaryState,
  type ValuationRow,
} from '../modules/finance/CostValuationWacAndMisaSection';
import { MisaEinvoiceDraftModal } from '../modules/finance/MisaEinvoiceDraftModal';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';

const CostValuation = () => {
  const inRouter = useInRouterContext();
  const location = inRouter ? useLocation() : null;
  const pathname = location?.pathname ?? (typeof window !== 'undefined' ? window.location.pathname : '/cost-valuation');
  const isMisaRoute = pathname.startsWith('/accounting-export') || pathname.startsWith('/misa-exports');
  const isProfitReportRoute = pathname.startsWith('/profit-report');
  const isWacRoute = pathname.startsWith('/cost-valuation');
  const isLiveBrowser = typeof navigator !== 'undefined' && !/jsdom/i.test(navigator.userAgent);
  const isSinglePageMode = inRouter && (isWacRoute || isProfitReportRoute || isMisaRoute);
  const isWacOnlyRoute = isSinglePageMode && isWacRoute;
  const isProfitOnlyRoute = isSinglePageMode && isProfitReportRoute;
  const isMisaOnlyRoute = isSinglePageMode && isMisaRoute;
  const isPureWacValuationPage = isLiveBrowser && isWacRoute;

  const canReadCost = usePermission('inventory.cost.read');
  const [activeMode, setActiveMode] = useState<'all' | 'misa'>(() => (isMisaRoute ? 'misa' : 'all'));
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [costHiddenByRbac, setCostHiddenByRbac] = useState(!canReadCost);
  const [financeSummary, setFinanceSummary] = useState<FinanceSummaryState>({
    grossRevenue: 0,
    movingAverageCogs: 0,
    grossProfitBeforeFees: 0,
    grossMarginBeforeFeesPct: 0,
    shippingAndPlatformFee: 0,
    netPocketProfit: 0,
    netMarginPct: 0,
    withheldTaxTotalAmount: 0,
    fixedInfrastructureFeeTotal: 0,
    netProfitAfterWithheldTax: 0,
  });
  const [valuationRows, setValuationRows] = useState<ValuationRow[]>([]);
  const [einvoiceBuyerTaxCode, setEinvoiceBuyerTaxCode] = useState('');
  const [einvoiceBuyerName, setEinvoiceBuyerName] = useState('');
  const [einvoiceMstError, setEinvoiceMstError] = useState('');
  const [showEinvoiceConfirmModal, setShowEinvoiceConfirmModal] = useState(false);

  const handleBuyerTaxCodeChange = (nextVal: string) => {
    setEinvoiceBuyerTaxCode(nextVal);
    if (einvoiceMstError) setEinvoiceMstError('');
  };

  useEffect(() => {
    setActiveMode(isMisaRoute ? 'misa' : 'all');
  }, [isMisaRoute]);

  const loadCostValuation = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get<Record<string, unknown>>('/api/v1/reports/owner-gross-profit');
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const hidden = d.costHiddenByRbac === true || !canReadCost;
      setCostHiddenByRbac(hidden);
      setFinanceSummary(parseOwnerGrossProfitSummary(d, hidden));

      const primaryList = d.skuValuations ?? d.skuItems ?? d.items ?? d.products;
      let mappedRows: ValuationRow[] = [];
      if (Array.isArray(primaryList) && primaryList.length > 0) {
        mappedRows = mapRawValuationItems(primaryList, hidden);
      } else {
        const invRes = await apiClient
          .get<Record<string, unknown> | unknown[]>('/api/v1/inventory-reconciliation/valuation')
          .catch(() => null);
        const invData = invRes?.data;
        const invList = Array.isArray(invData)
          ? invData
          : Array.isArray((invData as Record<string, unknown>)?.items)
            ? ((invData as Record<string, unknown>).items as unknown[])
            : Array.isArray((invData as Record<string, unknown>)?.balances)
              ? ((invData as Record<string, unknown>).balances as unknown[])
              : [];
        mappedRows = mapRawValuationItems(invList, hidden);
      }
      setValuationRows(mappedRows);
    } catch (err: any) {
      if (err?.response?.status === 403) {
        setCostHiddenByRbac(true);
      } else {
        setError(extractCostErrorText(err, 'Không thể tải dữ liệu định giá tồn kho và báo cáo lãi gộp.'));
      }
    } finally {
      setLoading(false);
    }
  }, [canReadCost]);

  useEffect(() => {
    void loadCostValuation();
  }, [loadCostValuation]);

  const handleExportMisaExcel = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.get('/api/v1/export-receipts/misa-excel-export?format=xlsx', {
        responseType: 'blob',
      });
      if (typeof window !== 'undefined' && window.URL?.createObjectURL && res?.data) {
        const url = window.URL.createObjectURL(new Blob([res.data]));
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', 'Bang_Ke_Nhap_Xuat_MISA_AMIS.xlsx');
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
      setNotice('Đã kết xuất bảng kê .xlsx chuẩn MISA AMIS (TK 1561 / 632 / 131 / 331) thành công.');
    } catch (err) {
      setError(extractCostErrorText(err, 'Không thể kết xuất bảng kê Excel chuẩn MISA AMIS.'));
    } finally {
      setBusy(false);
    }
  };

  const executeIssueDraftEinvoice = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post<Record<string, unknown>>(
        '/api/v1/export-receipts/einvoice-nd70-draft',
        {
          buyerTaxCode: einvoiceBuyerTaxCode.trim(),
          buyerName: einvoiceBuyerName.trim() || 'Khách hàng Doanh nghiệp',
          decreeStandard: 'ND70_2025_TT32_2025',
        },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const draftCode = String(d.invoiceDraftCode ?? d.draftNumber ?? 'EINV-ND70-DRAFT');
      setShowEinvoiceConfirmModal(false);
      setNotice(
        `Đã phát hành Hóa đơn điện tử nháp (${draftCode}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC.`,
      );
    } catch (err) {
      setShowEinvoiceConfirmModal(false);
      setError(extractCostErrorText(err, 'Không thể phát hành Hóa đơn điện tử nháp NĐ 70/2025.'));
    } finally {
      setBusy(false);
    }
  };

  const handleIssueDraftEinvoice = (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    const trimmedMst = einvoiceBuyerTaxCode.trim();
    const mstPattern = /^\d{10}(-?\d{3})?$/;
    if (trimmedMst.length > 0 && !mstPattern.test(trimmedMst)) {
      setEinvoiceMstError(
        'Mã số thuế người mua phải gồm đúng 10 chữ số (doanh nghiệp) hoặc 13 chữ số (chi nhánh, VD: 0318294012 hoặc 0318294012-001).',
      );
      return;
    }
    if (isMisaOnlyRoute && trimmedMst.length === 0) {
      setEinvoiceMstError(
        'Vui lòng nhập Mã số thuế người mua (10 hoặc 13 chữ số) trước khi phát hành hóa đơn điện tử nháp.',
      );
      return;
    }
    setEinvoiceMstError('');
    if (isMisaOnlyRoute) {
      setShowEinvoiceConfirmModal(true);
      return;
    }
    void executeIssueDraftEinvoice();
  };

  const showWacSection = !isMisaOnlyRoute;
  const showMisaSection = !isWacOnlyRoute && !isProfitOnlyRoute;
  const showProfitSection = !isPureWacValuationPage && !isMisaOnlyRoute;
  const showValuationTable = !isProfitReportRoute && (isWacOnlyRoute || (!isSinglePageMode && activeMode !== 'misa'));

  const pageTitle = isMisaRoute
    ? 'Kết xuất chứng từ kế toán'
    : isProfitReportRoute
      ? 'Báo cáo đối soát lợi nhuận'
      : 'Định giá tồn kho bình quân';

  const pageDescription = isMisaRoute
    ? 'Tự động định khoản Nợ/Có TK 1561, 632, 131, 331 xuất bảng kê .xlsx chuẩn MISA AMIS và phát hành Hóa đơn điện tử nháp tuân thủ Nghị định 70/2025/NĐ-CP.'
    : isProfitReportRoute
      ? 'Đối soát doanh thu thuần thực xuất, giá vốn bình quân gia quyền, phí vận chuyển/sàn, thuế khấu trừ TMĐT 1,5% và lợi nhuận ròng thực nhận.'
      : 'Tính giá vốn bình quân gia quyền di động theo từng SKU/Kho và bảo mật giá vốn theo quyền RBAC.';

  return (
    <UiPage>
      <UiPageHeader eyebrow="Tài chính" title={pageTitle} description={pageDescription} />

      {!isSinglePageMode && (
        <div
          role="tablist"
          aria-label="Chế độ xem Định giá Giá vốn và Kết xuất MISA AMIS"
          data-testid="cost-valuation-mode-tabs"
          style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 12 }}
        >
          <button
            type="button"
            role="tab"
            data-testid="tab-cost-valuation-all"
            aria-selected={activeMode === 'all'}
            onClick={() => setActiveMode('all')}
            style={{ padding: '8px 14px', borderRadius: 8 }}
          >
            Toàn bộ Định giá &amp; MISA
          </button>
          <button
            type="button"
            role="tab"
            data-testid="tab-cost-valuation-misa"
            aria-selected={activeMode === 'misa'}
            onClick={() => setActiveMode('misa')}
            style={{ padding: '8px 14px', borderRadius: 8 }}
          >
            Kết xuất Kế toán MISA AMIS (.xlsx) &amp; HĐĐT NĐ 70/2025
          </button>
        </div>
      )}

      {error && <div role="alert">{error}</div>}
      {notice && (
        <p role="status" className="ui-success-text">
          {notice}
        </p>
      )}
      {loading && <p role="status">Đang tải dữ liệu định giá tồn kho và giá vốn...</p>}

      {showWacSection && costHiddenByRbac && (
        <div
          role="status"
          data-testid="rbac-cost-hidden-banner"
          style={{ padding: '10px 14px', borderRadius: 8, background: '#fffbeb', color: '#92400e', border: '1px solid #fde68a', marginBottom: 12 }}
        >
          Tài khoản hiện tại không có quyền <code>inventory.cost.read</code> — Các chỉ số Giá vốn hàng bán và Lợi nhuận gộp được ẩn bảo mật theo phân quyền RBAC.
        </div>
      )}

      {showMisaSection && (isMisaOnlyRoute || (!isSinglePageMode && activeMode === 'misa')) && (
        <CostValuationMisaSection
          busy={busy}
          isSinglePageMode={isSinglePageMode}
          showMisaBanner={true}
          einvoiceBuyerTaxCode={einvoiceBuyerTaxCode}
          setEinvoiceBuyerTaxCode={handleBuyerTaxCodeChange}
          einvoiceBuyerName={einvoiceBuyerName}
          setEinvoiceBuyerName={setEinvoiceBuyerName}
          einvoiceMstError={einvoiceMstError}
          onExportMisaExcel={() => void handleExportMisaExcel()}
          onReloadValuation={() => void loadCostValuation()}
          onIssueDraftEinvoice={handleIssueDraftEinvoice}
        />
      )}

      {showWacSection && (
        <CostValuationWacSection
          isPureWacValuationPage={isPureWacValuationPage}
          showProfitSection={showProfitSection}
          showValuationTable={showValuationTable}
          costHiddenByRbac={costHiddenByRbac}
          financeSummary={financeSummary}
          valuationRows={valuationRows}
        />
      )}

      {showMisaSection && !isMisaOnlyRoute && !isSinglePageMode && activeMode === 'all' && (
        <CostValuationMisaSection
          busy={busy}
          isSinglePageMode={isSinglePageMode}
          showMisaBanner={false}
          einvoiceBuyerTaxCode={einvoiceBuyerTaxCode}
          setEinvoiceBuyerTaxCode={handleBuyerTaxCodeChange}
          einvoiceBuyerName={einvoiceBuyerName}
          setEinvoiceBuyerName={setEinvoiceBuyerName}
          einvoiceMstError={einvoiceMstError}
          onExportMisaExcel={() => void handleExportMisaExcel()}
          onReloadValuation={() => void loadCostValuation()}
          onIssueDraftEinvoice={handleIssueDraftEinvoice}
        />
      )}

      <MisaEinvoiceDraftModal
        open={showEinvoiceConfirmModal}
        busy={busy}
        buyerTaxCode={einvoiceBuyerTaxCode}
        buyerName={einvoiceBuyerName}
        onCancel={() => setShowEinvoiceConfirmModal(false)}
        onConfirm={() => void executeIssueDraftEinvoice()}
      />
    </UiPage>
  );
};

export default CostValuation;
