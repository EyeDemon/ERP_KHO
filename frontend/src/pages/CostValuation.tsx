import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type ValuationRow = {
  productId: number;
  productCode: string;
  productName: string;
  warehouseName: string;
  unitName: string;
  onHandQty: number;
  availableQty: number;
  movingAverageUnitCost: number;
  totalInventoryValue: number;
};

const extractErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

const formatCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} đ`;

const mapRawValuationItems = (rawItems: unknown[], hidden: boolean): ValuationRow[] =>
  rawItems.map((raw: any, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    const onHandQty = Number(item.onHandQty ?? item.onHandQuantity ?? item.quantity ?? 0);
    const availableQty = Number(item.availableQty ?? item.availableQuantity ?? onHandQty);
    const unitCost = hidden ? 0 : Number(item.movingAverageUnitCost ?? item.unitCost ?? item.averageCost ?? item.costPrice ?? 0);
    const totalValue = hidden
      ? 0
      : Number(item.totalInventoryValue ?? item.totalCostValue ?? item.inventoryValue ?? Math.round(onHandQty * unitCost));
    return {
      productId: Number(item.productId ?? item.id ?? idx + 1),
      productCode: String(item.productCode ?? item.sku ?? item.code ?? `MH-${idx + 1}`),
      productName: String(item.productName ?? item.name ?? 'Mặt hàng tồn kho'),
      warehouseName: String(item.warehouseName ?? item.warehouseCode ?? 'Kho trung tâm'),
      unitName: String(item.unitName ?? item.uomName ?? item.uomCode ?? 'Cái'),
      onHandQty,
      availableQty,
      movingAverageUnitCost: unitCost,
      totalInventoryValue: totalValue,
    };
  });

const CostValuation = () => {
  const canReadCost = usePermission('inventory.cost.read');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [costHiddenByRbac, setCostHiddenByRbac] = useState(!canReadCost);
  const [financeSummary, setFinanceSummary] = useState({
    grossRevenue: 0,
    movingAverageCogs: 0,
    netPocketProfit: 0,
    netMarginPct: 0,
  });
  const [valuationRows, setValuationRows] = useState<ValuationRow[]>([]);
  const [einvoiceBuyerTaxCode, setEinvoiceBuyerTaxCode] = useState('');
  const [einvoiceBuyerName, setEinvoiceBuyerName] = useState('');

  const loadCostValuation = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get<Record<string, unknown>>('/api/v1/reports/owner-gross-profit');
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const hidden = d.costHiddenByRbac === true || !canReadCost;
      setCostHiddenByRbac(hidden);
      setFinanceSummary({
        grossRevenue: Number(d.grossRevenue ?? d.netRevenue ?? d.totalDispatchedRevenue ?? 0),
        movingAverageCogs: hidden ? 0 : Number(d.movingAverageCogs ?? d.totalCogs ?? d.cogsAmount ?? 0),
        netPocketProfit: hidden ? 0 : Number(d.netPocketProfit ?? d.estimatedGrossProfit ?? d.grossProfit ?? 0),
        netMarginPct: hidden ? 0 : Number(d.netMarginPct ?? d.grossMarginPercent ?? d.grossMarginPct ?? 0),
      });

      const primaryList = d.skuValuations ?? d.skuItems ?? d.items ?? d.products;
      if (Array.isArray(primaryList) && primaryList.length > 0) {
        setValuationRows(mapRawValuationItems(primaryList, hidden));
      } else {
        const invRes = await apiClient
          .get<Record<string, unknown> | unknown[]>('/api/v1/inventory')
          .catch(() => apiClient.get<Record<string, unknown> | unknown[]>('/api/Inventory'))
          .catch(() => null);
        const invData = invRes?.data;
        const invList = Array.isArray(invData)
          ? invData
          : Array.isArray((invData as Record<string, unknown>)?.items)
            ? ((invData as Record<string, unknown>).items as unknown[])
            : Array.isArray((invData as Record<string, unknown>)?.balances)
              ? ((invData as Record<string, unknown>).balances as unknown[])
              : [];
        setValuationRows(mapRawValuationItems(invList, hidden));
      }
    } catch (err: any) {
      if (err?.response?.status === 403) {
        setCostHiddenByRbac(true);
      } else {
        setError(extractErrorText(err, 'Không thể tải dữ liệu định giá tồn kho và báo cáo lãi gộp.'));
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
      setError(extractErrorText(err, 'Không thể kết xuất bảng kê Excel chuẩn MISA AMIS.'));
    } finally {
      setBusy(false);
    }
  };

  const handleIssueDraftEinvoice = async (e: React.FormEvent) => {
    e.preventDefault();
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
      setNotice(`Đã phát hành Hóa đơn điện tử nháp (${draftCode}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC.`);
    } catch (err) {
      setError(extractErrorText(err, 'Không thể phát hành Hóa đơn điện tử nháp NĐ 70/2025.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Tài chính & Kế toán Giá vốn"
        title="Giá Vốn Bình Quân Gia Quyền, Lãi Gộp Chủ Shop & Kết Xuất MISA AMIS"
        description="Tính giá vốn bình quân gia quyền di động, bảo mật giá vốn theo quyền RBAC, kết xuất bảng kê .xlsx chuẩn MISA AMIS và phát hành HĐĐT nháp NĐ 70/2025."
      />

      {error && <div role="alert">{error}</div>}
      {notice && <p role="status" className="ui-success-text">{notice}</p>}
      {loading && <p role="status">Đang tải dữ liệu định giá tồn kho và giá vốn...</p>}

      {costHiddenByRbac && (
        <div
          role="status"
          data-testid="rbac-cost-hidden-banner"
          style={{
            padding: '10px 14px',
            borderRadius: 8,
            background: '#fffbeb',
            color: '#92400e',
            border: '1px solid #fde68a',
            marginBottom: 12,
          }}
        >
          Tài khoản hiện tại không có quyền <code>inventory.cost.read</code> — Các chỉ số Giá vốn hàng bán và Lợi nhuận gộp được ẩn bảo mật theo phân quyền RBAC.
        </div>
      )}

      <UiMetricGrid>
        <UiMetric value={formatCurrencyVnd(financeSummary.grossRevenue)} label="Doanh thu thuần thực xuất" />
        <UiMetric
          value={costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.movingAverageCogs)}
          label="Giá vốn hàng bán bình quân gia quyền"
        />
        <UiMetric
          value={costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.netPocketProfit)}
          label="Lợi nhuận gộp ước tính Chủ Shop"
        />
        <UiMetric
          value={costHiddenByRbac ? 'Ẩn theo RBAC' : `${String(financeSummary.netMarginPct).replace('.', ',')}%`}
          label="Biên lợi nhuận gộp (%)"
        />
      </UiMetricGrid>

      <UiCard
        title="Kết xuất Bảng kê Kế toán MISA AMIS (.xlsx) & Phát hành Hóa đơn Điện tử Nháp NĐ 70/2025"
        subtitle="Tự động định khoản Nợ/Có TK 1561, 632, 131, 331 và chuẩn bị dữ liệu XML hóa đơn điện tử có mã của Cơ quan Thuế"
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div className="ui-inline-wrap" style={{ gap: 10 }}>
            <button
              type="button"
              data-testid="btn-export-misa-xlsx"
              disabled={busy}
              onClick={() => void handleExportMisaExcel()}
            >
              Kết xuất Bảng kê .xlsx Chuẩn MISA AMIS
            </button>
            <button type="button" className="ui-btn-secondary" disabled={busy} onClick={() => void loadCostValuation()}>
              Tính lại Giá vốn Bình quân
            </button>
          </div>

          <form onSubmit={handleIssueDraftEinvoice} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 12 }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Mã số thuế người mua
              <input
                aria-label="Mã số thuế người mua HĐĐT"
                value={einvoiceBuyerTaxCode}
                onChange={(e) => setEinvoiceBuyerTaxCode(e.target.value)}
                placeholder="VD: 0312345678"
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              Tên đơn vị mua hàng
              <input
                aria-label="Tên đơn vị mua hàng HĐĐT"
                value={einvoiceBuyerName}
                onChange={(e) => setEinvoiceBuyerName(e.target.value)}
                placeholder="Nhập tên doanh nghiệp mua..."
                style={{ minWidth: 260 }}
              />
            </label>
            <button type="submit" data-testid="btn-issue-einvoice-nd70" disabled={busy}>
              Phát hành HĐĐT Nháp NĐ 70/2025
            </button>
          </form>
        </div>
      </UiCard>

      <UiCard title={`Bảng Định giá Tồn kho theo Giá vốn Bình quân Gia quyền • ${valuationRows.length} mặt hàng`}>
        {valuationRows.length === 0 ? (
          <p className="ui-muted-text">Chưa có dòng chi tiết định giá mặt hàng hoặc dữ liệu được tổng hợp ở cấp toàn kho.</p>
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng định giá tồn kho bình quân gia quyền" style={{ width: '100%' }}>
              <thead>
                <tr>
                  <th>Mã mặt hàng</th>
                  <th>Tên sản phẩm</th>
                  <th>Kho</th>
                  <th>ĐVT</th>
                  <th style={{ textAlign: 'right' }}>Tồn thực tế</th>
                  <th style={{ textAlign: 'right' }}>Khả dụng</th>
                  <th style={{ textAlign: 'right' }}>Đơn giá vốn bình quân</th>
                  <th style={{ textAlign: 'right' }}>Thành tiền tồn kho</th>
                </tr>
              </thead>
              <tbody>
                {valuationRows.map((row, idx) => (
                  <tr key={`${row.productId}-${row.productCode}-${idx}`}>
                    <td><strong>{row.productCode}</strong></td>
                    <td>{row.productName}</td>
                    <td>{row.warehouseName}</td>
                    <td>{row.unitName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.onHandQty}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.availableQty}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      {costHiddenByRbac ? <UiBadge tone="warning">Ẩn theo RBAC</UiBadge> : formatCurrencyVnd(row.movingAverageUnitCost)}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      {costHiddenByRbac ? <UiBadge tone="warning">Ẩn theo RBAC</UiBadge> : formatCurrencyVnd(row.totalInventoryValue)}
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

export default CostValuation;
