import { UiMetric, UiMetricGrid } from '../../ui/ProductionUi';

export type OwnerFinanceSummary = {
  netRevenue: number;
  cogsAmount: number;
  grossProfit: number;
  grossMarginPct: number;
  costHiddenByRbac: boolean;
  pendingCodAmount: number;
  receivedCodAmount: number;
  codWarningCount: number;
};

type DashboardMetricsProps = {
  demoRuntime: boolean;
  warehouseCount: number | string;
  productCount: number | string;
  inventoryBucketCount: number | string;
  openTaskOrMismatchCount: number | string;
  mismatchCount: number;
  ownerFinance: OwnerFinanceSummary;
  formatCurrencyVnd: (amount: number) => string;
};

export const DashboardMetrics = ({
  demoRuntime,
  warehouseCount,
  productCount,
  inventoryBucketCount,
  openTaskOrMismatchCount,
  mismatchCount,
  ownerFinance,
  formatCurrencyVnd,
}: DashboardMetricsProps) => {
  return (
    <>
      <UiMetricGrid>
        <UiMetric
          value={warehouseCount}
          label={demoRuntime ? 'Kho trong bộ dữ liệu mẫu' : 'Kho đang hoạt động'}
        />
        <UiMetric
          value={productCount}
          label={demoRuntime ? 'Sản phẩm trong bộ dữ liệu mẫu' : 'Sản phẩm đang quản lý'}
        />
        <UiMetric
          value={inventoryBucketCount}
          label={demoRuntime ? 'Dòng tồn kho mẫu' : 'Nhóm tồn kho dương'}
        />
        <article className={'ui-metric' + (mismatchCount > 0 ? ' dashboard-metric-warning' : '')}>
          <strong>{openTaskOrMismatchCount}</strong>
          <span>{demoRuntime ? 'Chênh lệch đối chiếu mẫu' : 'Phiếu / tác vụ đang mở'}</span>
        </article>
      </UiMetricGrid>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }} data-testid="dashboard-owner-finance-metrics">
        <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
          <div style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Doanh thu đơn đã xuất (Doanh thu thuần)</div>
          <strong style={{ fontSize: 15, color: '#0f172a' }}>{formatCurrencyVnd(ownerFinance.netRevenue)}</strong>
          <div style={{ fontSize: 12, color: '#64748b', marginTop: 2 }}>Đã trừ đơn hoàn &amp; đơn hủy sàn</div>
        </div>
        <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}>
          <div style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Giá vốn hàng bán bình quân</div>
          <strong style={{ fontSize: 15, color: '#0f172a' }}>
            {ownerFinance.costHiddenByRbac ? 'Ẩn giá vốn theo phân quyền RBAC' : formatCurrencyVnd(ownerFinance.cogsAmount)}
          </strong>
          <div style={{ fontSize: 12, color: '#64748b', marginTop: 2 }}>Bình quân gia quyền di động</div>
        </div>
        <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #a7f3d0', background: '#ecfdf5', fontVariantNumeric: 'tabular-nums' }}>
          <div style={{ fontSize: 12, color: '#065f46', fontWeight: 600 }}>Lợi nhuận gộp ước tính</div>
          <strong style={{ fontSize: 15, color: '#065f46' }}>
            {ownerFinance.costHiddenByRbac
              ? 'Ẩn theo phân quyền RBAC'
              : `${formatCurrencyVnd(ownerFinance.grossProfit)} • Biên lãi gộp ${String(ownerFinance.grossMarginPct).replace('.', ',')}%`}
          </strong>
          <div style={{ fontSize: 12, color: '#047857', marginTop: 2 }}>Vốn tồn đọng &gt; 60 ngày: 0 đ • Hàng cận hạn &lt; 30 ngày: 0 lô</div>
        </div>
        <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #fde68a', background: '#fffbeb', fontVariantNumeric: 'tabular-nums' }}>
          <div style={{ fontSize: 12, color: '#92400e', fontWeight: 600 }}>Tiền thu hộ COD chờ đối soát</div>
          <strong style={{ fontSize: 15, color: '#92400e' }}>{`${formatCurrencyVnd(ownerFinance.pendingCodAmount)} • ${ownerFinance.codWarningCount} cảnh báo lệch phí/chậm hoàn`}</strong>
          <div style={{ fontSize: 12, color: '#b45309', marginTop: 2 }}>{`Thực nhận từ ĐVVC: ${formatCurrencyVnd(ownerFinance.receivedCodAmount)} • Đối soát 3 chiều`}</div>
        </div>
      </div>
    </>
  );
};

export default DashboardMetrics;
