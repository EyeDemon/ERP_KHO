import React, { useState } from 'react';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';

export { CostValuationMisaSection } from './CostValuationMisaSection';

export type ValuationRow = {
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

export type FinanceSummaryState = {
  grossRevenue: number;
  movingAverageCogs: number;
  grossProfitBeforeFees: number;
  grossMarginBeforeFeesPct: number;
  shippingAndPlatformFee: number;
  netPocketProfit: number;
  netMarginPct: number;
  withheldTaxTotalAmount: number;
  fixedInfrastructureFeeTotal: number;
  netProfitAfterWithheldTax: number;
};

export const formatCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} ₫`;

interface CostValuationWacSectionProps {
  isPureWacValuationPage: boolean;
  showProfitSection: boolean;
  showValuationTable: boolean;
  costHiddenByRbac: boolean;
  financeSummary: FinanceSummaryState;
  valuationRows: ValuationRow[];
}

export const CostValuationWacSection: React.FC<CostValuationWacSectionProps> = ({
  isPureWacValuationPage,
  showProfitSection,
  showValuationTable,
  costHiddenByRbac,
  financeSummary,
  valuationRows,
}) => {
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const warehouseOptions = Array.from(
    new Set(valuationRows.map(r => formatWarehouseLabel(r.warehouseName) || CANONICAL_WH01_LABEL)),
  );
  const visibleRows = warehouseFilter
    ? valuationRows.filter(r => (formatWarehouseLabel(r.warehouseName) || CANONICAL_WH01_LABEL) === warehouseFilter)
    : valuationRows;
  const totalTaxAndFixedFee = financeSummary.withheldTaxTotalAmount + financeSummary.fixedInfrastructureFeeTotal;
  const totalPortfolioValue = visibleRows.reduce((sum, r) => sum + r.totalInventoryValue, 0);
  const topRow = visibleRows.reduce<ValuationRow | null>(
    (best, r) => (!best || r.totalInventoryValue > best.totalInventoryValue ? r : best),
    null,
  );
  const hasTopItem = !costHiddenByRbac && Boolean(topRow) && visibleRows.length > 0 && totalPortfolioValue > 0;
  const topItemKpiValue = costHiddenByRbac
    ? 'Ẩn theo RBAC'
    : !hasTopItem || !topRow
      ? 'Chưa có dữ liệu'
      : topRow.productCode;
  const topItemSubtitle = costHiddenByRbac
    ? 'Giới hạn theo phân quyền giá vốn'
    : hasTopItem && topRow
      ? ` (${formatCurrencyVnd(topRow.totalInventoryValue)} — chiếm ${((topRow.totalInventoryValue / totalPortfolioValue) * 100).toFixed(1).replace('.', ',')}%)`
      : 'Chưa phát sinh tồn kho có giá trị';

  return (
    <>
      {isPureWacValuationPage ? (
        <UiMetricGrid data-testid="wac-inventory-kpi-grid">
          <UiMetric
            value={costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(totalPortfolioValue)}
            subtitle="Định giá theo bình quân gia quyền di động"
            label="Tổng giá trị tồn kho bình quân"
            isZero={!costHiddenByRbac && totalPortfolioValue === 0}
          />
          <UiMetric
            value={`${visibleRows.length} mặt hàng`}
            subtitle=" (Theo đơn vị tính chuẩn)"
            label="Số mã hàng có tồn"
            isZero={visibleRows.length === 0}
          />
          <UiMetric
            value={topItemKpiValue}
            subtitle={topItemSubtitle}
            label="Mặt hàng giá trị tồn lớn nhất"
            isZero={!costHiddenByRbac && !hasTopItem}
          />
          <UiMetric
            value="02/11/2026 17:45"
            subtitle=" (Phương pháp: Bình quân gia quyền)"
            label="Cập nhật giá vốn gần nhất"
          />
        </UiMetricGrid>
      ) : (
        <UiMetricGrid>
          <UiMetric
            value={formatCurrencyVnd(financeSummary.grossRevenue)}
            subtitle="Tổng doanh thu chứng từ thực xuất"
            label="Doanh thu thuần thực xuất"
            isZero={financeSummary.grossRevenue === 0}
          />
          <UiMetric
            value={costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.movingAverageCogs)}
            subtitle="Tự động kết chuyển theo từng phiếu xuất"
            label="Giá vốn hàng bán bình quân gia quyền"
            isZero={!costHiddenByRbac && financeSummary.movingAverageCogs === 0}
          />
          <UiMetric
            value={costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.grossProfitBeforeFees)}
            subtitle="Chưa trừ phí vận chuyển và phí sàn"
            label={
              <>
                Lợi nhuận gộp ước tính Chủ Shop (Doanh thu − Giá vốn{' '}
                <abbr title="Bình quân gia quyền di động" style={{ textDecoration: 'underline dotted', cursor: 'help' }}>
                  WAC
                </abbr>
                )
              </>
            }
            isZero={!costHiddenByRbac && financeSummary.grossProfitBeforeFees === 0}
          />
          <UiMetric
            value={
              costHiddenByRbac
                ? 'Ẩn theo RBAC'
                : `${String(financeSummary.grossMarginBeforeFeesPct).replace('.', ',')}%`
            }
            subtitle="Tỷ suất lãi gộp trên doanh thu thuần"
            label="Biên lợi nhuận gộp (%)"
            isZero={!costHiddenByRbac && financeSummary.grossMarginBeforeFeesPct === 0}
          />
        </UiMetricGrid>
      )}

      {showProfitSection && (
        <UiCard
          title="Báo cáo đối soát lợi nhuận gộp và lợi nhuận ròng"
          subtitle="Báo cáo Đối soát Lợi nhuận Gộp, Phí Vận chuyển/Sàn & Khấu trừ Thuế Sàn TMĐT — Doanh thu thuần − Giá vốn WAC = Lợi nhuận gộp − Phí vận chuyển/sàn − Khấu trừ thuế Sàn TMĐT 1,5% & Phí cố định = Lợi nhuận ròng thực nhận"
        >
          <div
            data-testid="wac-profit-reconciliation-grid"
            style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 12 }}
          >
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
              <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Phí vận chuyển &amp; Phí sàn đối soát</div>
              <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                {costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.shippingAndPlatformFee)}
              </div>
              {!costHiddenByRbac && (
                <div style={{ fontSize: 12, color: '#475569', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                  <span>Còn lại sau phí sàn: </span>
                  <strong>{formatCurrencyVnd(financeSummary.netPocketProfit)}</strong>
                  <span> (</span>
                  <strong>{`${String(financeSummary.netMarginPct).replace('.', ',')}%`}</strong>
                  <span>)</span>
                </div>
              )}
            </div>

            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
              <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>
                Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)
              </div>
              <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                {formatCurrencyVnd(financeSummary.withheldTaxTotalAmount)}
              </div>
            </div>

            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
              <div style={{ fontSize: 12.5, color: '#475569', fontWeight: 600 }}>Phí Hạ tầng Đơn hàng Cố định (3.000 ₫/đơn)</div>
              <div style={{ fontSize: 18, fontWeight: 700, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                {formatCurrencyVnd(financeSummary.fixedInfrastructureFeeTotal)}
              </div>
              <div style={{ fontSize: 12, color: '#475569', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                Tổng thuế &amp; phí cố định: {formatCurrencyVnd(totalTaxAndFixedFee)}
              </div>
            </div>

            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #a7f3d0', background: '#ecfdf5' }}>
              <div style={{ fontSize: 12.5, color: '#065f46', fontWeight: 600 }}>Lợi nhuận Ròng Sau Thuế Sàn &amp; Phí</div>
              <div style={{ fontSize: 18, fontWeight: 700, color: '#065f46', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                {costHiddenByRbac ? 'Ẩn theo RBAC' : formatCurrencyVnd(financeSummary.netProfitAfterWithheldTax)}
              </div>
              {!costHiddenByRbac && (
                <div style={{ fontSize: 12, color: '#047857', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
                  = {formatCurrencyVnd(financeSummary.grossProfitBeforeFees)} − {formatCurrencyVnd(financeSummary.shippingAndPlatformFee)} − {formatCurrencyVnd(totalTaxAndFixedFee)}
                </div>
              )}
            </div>
          </div>
        </UiCard>
      )}

      {showValuationTable && (
        <UiCard
          title={
            isPureWacValuationPage
              ? `Bảng định giá tồn kho bình quân gia quyền • ${visibleRows.length} mặt hàng`
              : `Bảng Định giá Tồn kho theo Giá vốn Bình quân Gia quyền • ${visibleRows.length} mặt hàng`
          }
          actions={
            isPureWacValuationPage ? (
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 12.5, fontWeight: 600 }}>
                <span style={{ whiteSpace: 'nowrap' }}>Kho:</span>
                <select
                  aria-label="Lọc kho định giá tồn kho"
                  data-testid="select-wac-warehouse-filter"
                  value={warehouseFilter}
                  onChange={e => setWarehouseFilter(e.target.value)}
                >
                  <option value="">Tất cả kho</option>
                  {warehouseOptions.map(wh => (
                    <option key={wh} value={wh}>
                      {wh}
                    </option>
                  ))}
                </select>
              </label>
            ) : undefined
          }
        >
          {visibleRows.length === 0 ? (
            <UiEmptyState
              title="Chưa có dòng chi tiết định giá mặt hàng"
              detail="Chưa có dòng chi tiết định giá mặt hàng hoặc dữ liệu được tổng hợp ở cấp toàn kho."
            />
          ) : (
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Bảng định giá tồn kho bình quân gia quyền" style={{ width: '100%' }}>
                <thead>
                  <tr>
                    <th className="ui-sticky-id" style={{ minWidth: 110, whiteSpace: 'nowrap' }}>Mã mặt hàng</th>
                    <th style={{ minWidth: 180 }}>Tên sản phẩm</th>
                    <th style={{ minWidth: 120, whiteSpace: 'nowrap' }}>Kho</th>
                    <th style={{ minWidth: 84, whiteSpace: 'nowrap' }}>Đơn vị</th>
                    <th style={{ minWidth: 96, whiteSpace: 'nowrap', textAlign: 'right' }}>Tồn thực tế</th>
                    <th style={{ minWidth: 92, whiteSpace: 'nowrap', textAlign: 'right' }}>Khả dụng</th>
                    <th style={{ minWidth: 135, whiteSpace: 'nowrap', textAlign: 'right' }} title="Bình quân gia quyền di động">
                      Đơn giá vốn
                    </th>
                    <th style={{ minWidth: 135, whiteSpace: 'nowrap', textAlign: 'right' }}>Thành tiền tồn</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleRows.map((row, idx) => (
                    <tr key={`${row.productId}-${row.productCode}-${idx}`}>
                      <td className="ui-sticky-id" style={{ background: '#ffffff', whiteSpace: 'nowrap' }}>
                        <strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.productCode}</strong>
                      </td>
                      <td>{row.productName}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>{formatWarehouseLabel(row.warehouseName)}</td>
                      <td>{!row.unitName || row.unitName.trim().toUpperCase() === 'ĐVT' ? 'Cái' : row.unitName}</td>
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
      )}
    </>
  );
};
