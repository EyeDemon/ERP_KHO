import React from 'react';
import { Link } from 'react-router-dom';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import DashboardQuickActions from '../../components/dashboard/DashboardQuickActions';
import {
  healthStatusLabel,
  taskTypeLabel,
  type MultiWarehouseControlTowerDto,
  type OperationalDashboardSummary,
} from './DashboardLabelsAndTypes';

export interface DashboardControlTowerPanelProps {
  demoRuntime: boolean;
  liveSummary: OperationalDashboardSummary | null;
  controlTower: MultiWarehouseControlTowerDto | null;
  showEcommerceDetails: boolean;
  setShowEcommerceDetails: React.Dispatch<React.SetStateAction<boolean>>;
  shopNameInput: string;
  setShopNameInput: (v: string) => void;
  shopPlatformInput: string;
  setShopPlatformInput: (v: string) => void;
  marketplaceSkuInput: string;
  setMarketplaceSkuInput: (v: string) => void;
  internalSkuInput: string;
  setInternalSkuInput: (v: string) => void;
  marketplaceSyncNotice: string | null;
  setMarketplaceSyncNotice: (v: string | null) => void;
  codReconcileNotice: string | null;
  setCodReconcileNotice: (v: string | null) => void;
  ownerFinance: { codWarningCount: number };
  setLiveError: (v: string) => void;
  visibleProductionNavigation: Array<{ path: string; label: string; section: string }>;
}

export const DashboardControlTowerPanel: React.FC<DashboardControlTowerPanelProps> = ({
  demoRuntime,
  liveSummary,
  controlTower,
  showEcommerceDetails,
  setShowEcommerceDetails,
  shopNameInput,
  setShopNameInput,
  shopPlatformInput,
  setShopPlatformInput,
  marketplaceSkuInput,
  setMarketplaceSkuInput,
  internalSkuInput,
  setInternalSkuInput,
  marketplaceSyncNotice,
  setMarketplaceSyncNotice,
  codReconcileNotice,
  setCodReconcileNotice,
  ownerFinance,
  setLiveError,
  visibleProductionNavigation,
}) => {
  return (
    <>
      <UiCard
        title="Báo cáo Lợi nhuận Gộp Ước tính Chủ Shop, Đối soát Tiền Thu Hộ (COD) & Đồng bộ Tồn kho Đa Sàn"
        subtitle="Tổng hợp doanh thu thực xuất, giá vốn bình quân gia quyền, đối soát công nợ COD 3 chiều và tiến trình 5 bước khởi tạo kho nhanh"
      >
        <div className="ui-stack" style={{ gap: 10 }}>
          <div style={{ padding: '8px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff' }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 6 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a' }}>
                Lối tắt Quản trị Tài chính, Đối soát COD &amp; Lộ trình 5 Bước Thiết lập Kho Nhanh:
              </span>
              <button
                type="button"
                className="ui-btn-secondary"
                data-testid="toggle-ecommerce-workbench"
                onClick={() => setShowEcommerceDetails((prev) => !prev)}
                style={{ fontSize: 12, padding: '4px 10px', minHeight: 30 }}
              >
                {showEcommerceDetails ? 'Thu gọn biểu mẫu liên kết & tháp điều hành' : 'Mở rộng liên kết gian hàng & tháp điều hành đa kho'}
              </button>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 14px', fontSize: 12, color: '#475569', marginBottom: 8 }}>
              <span>Bước 1: Khởi tạo sơ đồ kệ mẫu (3 giây)</span>
              <span>Bước 2: Nhập danh mục sản phẩm chuẩn</span>
              <span>Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn</span>
              <span>Bước 4: Liên kết gian hàng Shopee / TikTok Shop</span>
              <span>Bước 5: In gộp tem nhiệt &amp; xuất đơn đầu tiên</span>
            </div>
            <DashboardQuickActions />
          </div>

          <div style={{ padding: '8px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a', marginBottom: 6 }}>
              Quản lý Gian hàng Sàn TMĐT &amp; Ánh xạ Mã Sản phẩm Đa Sàn
            </div>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <input
                aria-label="Tên gian hàng sàn"
                maxLength={120}
                value={shopNameInput}
                onChange={(e) => setShopNameInput(e.target.value)}
                placeholder="VD: Gian hàng Shopee Chính hãng"
                style={{ minHeight: 36, minWidth: 260, width: 280, padding: '4px 10px' }}
              />
              <select
                aria-label="Nền tảng sàn TMĐT"
                value={shopPlatformInput}
                onChange={(e) => setShopPlatformInput(e.target.value)}
                style={{ minHeight: 36, padding: '4px 10px' }}
              >
                <option value="SHOPEE">Shopee Mall / Shopee</option>
                <option value="TIKTOK_SHOP">TikTok Shop</option>
              </select>
              <input
                aria-label="Mã SKU trên sàn"
                value={marketplaceSkuInput}
                onChange={(e) => setMarketplaceSkuInput(e.target.value)}
                placeholder="VD: SP-SKU-001"
                style={{ minHeight: 36, padding: '4px 10px' }}
              />
              <input
                aria-label="Mã sản phẩm nội bộ kho"
                value={internalSkuInput}
                onChange={(e) => setInternalSkuInput(e.target.value)}
                placeholder="VD: SP-NOIBO-001"
                style={{ minHeight: 36, padding: '4px 10px' }}
              />
              <button
                type="button"
                onClick={() => {
                  setLiveError('');
                  setMarketplaceSyncNotice(null);
                  const resolvedShopName = shopNameInput.trim() || 'Gian hàng Shopee Chính hãng';
                  const resolvedMarketSku = marketplaceSkuInput.trim() || 'SP-SKU-001';
                  const resolvedInternalSku = internalSkuInput.trim() || 'SP-NOIBO-001';
                  void Promise.resolve(
                    apiClient.post('/api/v1/sales-orders/marketplace-shops/sync-inventory', {
                      shopName: resolvedShopName,
                      platform: shopPlatformInput,
                      marketplaceSku: resolvedMarketSku,
                      internalProductCode: resolvedInternalSku,
                      channels: [shopPlatformInput],
                      safetyBufferPercent: 5,
                    }),
                  )
                    .then(() => {
                      setMarketplaceSyncNotice(`Đã liên kết gian hàng ${resolvedShopName} (${shopPlatformInput}) & ánh xạ mã ${resolvedMarketSku} → ${resolvedInternalSku}.`);
                    })
                    .catch((err: any) => {
                      setMarketplaceSyncNotice(null);
                      setLiveError(err?.response?.data?.message || 'Không thể liên kết gian hàng và ánh xạ SKU sàn.');
                    });
                }}
              >
                Liên kết Gian hàng &amp; Ánh xạ SKU Sàn
              </button>
            </div>
          </div>

          <div className="ui-inline-wrap" style={{ gap: 8 }}>
            <button
              type="button"
              onClick={() => {
                setLiveError('');
                setMarketplaceSyncNotice(null);
                void Promise.resolve(
                  apiClient.post('/api/v1/sales-orders/marketplace-shops/sync-inventory', {
                    channels: ['SHOPEE', 'TIKTOK_SHOP'],
                    safetyBufferPercent: 5,
                  }),
                )
                  .then(() => {
                    setMarketplaceSyncNotice('Đã đồng bộ tồn kho an toàn đa gian hàng (Shopee, TikTok Shop • Đệm an toàn chống bán lố 100%).');
                  })
                  .catch((err: any) => {
                    setMarketplaceSyncNotice(null);
                    setLiveError(err?.response?.data?.message || 'Không thể đồng bộ tồn kho an toàn đa gian hàng.');
                  });
              }}
            >
              Đồng bộ Tồn kho An toàn Đa Gian hàng
            </button>
            <button
              type="button"
              onClick={() => {
                setLiveError('');
                setCodReconcileNotice(null);
                void Promise.resolve(
                  apiClient.post<Record<string, unknown>>('/api/v1/reports/cod-reconciliation/verify', {
                    reconcileMode: 'THREE_WAY_COD_AND_MARKETPLACE_FEE',
                  }),
                )
                  .then((res) => {
                    const d = (res?.data ?? {}) as Record<string, unknown>;
                    const items = Array.isArray(d.items) ? d.items : (Array.isArray(d.ledgers) ? d.ledgers : []);
                    const discrepancyCount = Number(
                      d.discrepancyCount ??
                      d.feeDiscrepancyWarnings ??
                      d.discrepancyWarningCount ??
                      ownerFinance.codWarningCount ??
                      0,
                    );
                    const totalCount = Number(d.totalShipments ?? d.totalCount ?? items.length);
                    const matchedCount = Number(
                      d.matchedShipments ??
                      d.matchedCount ??
                      Math.max(0, totalCount - discrepancyCount),
                    );
                    const matchRatePct =
                      d.matchRatePct !== undefined
                        ? Number(d.matchRatePct)
                        : totalCount > 0
                          ? Math.round((matchedCount * 1000) / totalCount) / 10
                          : 100;
                    setCodReconcileNotice(
                      `Đã đối soát ${matchedCount}/${totalCount} vận đơn (${matchRatePct}%), phát hiện ${discrepancyCount} cảnh báo lệch phí/chậm hoàn.`,
                    );
                  })
                  .catch((err: any) => {
                    setCodReconcileNotice(null);
                    setLiveError(err?.response?.data?.message || 'Không thể chạy đối soát 3 chiều tiền thu hộ COD.');
                  });
              }}
            >
              Chạy Đối soát 3 Chiều Tiền Thu Hộ &amp; Quyết toán Phí Sàn
            </button>
          </div>

          {marketplaceSyncNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {marketplaceSyncNotice}
            </div>
          )}
          {codReconcileNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {codReconcileNotice}
            </div>
          )}
        </div>
      </UiCard>

      {!demoRuntime && (
        <div className="ui-stack" data-testid="control-tower-collapsible-section" style={{ gap: 10 }}>
          <div
            style={{
              padding: '10px 14px',
              borderRadius: 10,
              border: '1px solid #cbd5e1',
              background: '#f8fafc',
              display: 'flex',
              flexWrap: 'wrap',
              justifyContent: 'space-between',
              alignItems: 'center',
              gap: 10,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>
                Tháp điều khiển đa kho (Multi-Warehouse Control Tower)
              </strong>
              {(controlTower?.warehouses?.length ?? 0) <= 1 && (
                <UiBadge tone="neutral">
                  Tự động thu gọn cho mô hình 1 kho ({controlTower?.warehouses?.length || liveSummary?.warehouseCount || 1} kho hoạt động)
                </UiBadge>
              )}
            </div>
            <button
              type="button"
              className="ui-btn-secondary"
              data-testid="toggle-control-tower-btn"
              aria-expanded={showEcommerceDetails}
              onClick={() => setShowEcommerceDetails((prev) => !prev)}
              style={{ fontSize: 12.5, padding: '5px 12px', minHeight: 32 }}
            >
              {showEcommerceDetails ? 'Thu gọn Tháp điều khiển đa kho' : 'Mở rộng Tháp điều khiển đa kho'}
            </button>
          </div>
          {showEcommerceDetails && (
            <div className="dashboard-grid">
              <UiCard title="Trung tâm Điều hành Kho phân tán & Bộ máy Hàng đợi Nhiệm vụ">
                <div className="ui-inline-wrap" style={{ gap: '6px 14px', fontSize: 12.5, color: '#334155', marginBottom: 8 }}>
                  <span><strong>Kho ổn định:</strong> {`${controlTower?.healthyWarehousesCount ?? 0}/${controlTower?.totalWarehouses ?? (liveSummary?.warehouseCount ?? 0)}`}</span>
                  <span>• <strong>Kho cần chú ý:</strong> {controlTower?.attentionWarehousesCount ?? 0}</span>
                  {(controlTower?.taskEngineQueues ?? [
                    { taskType: 'PUTAWAY', openCount: liveSummary?.openPutawayCount ?? 0, route: '/putaway-tasks' },
                    { taskType: 'PICK', openCount: 0, route: '/picking-tasks' },
                    { taskType: 'PACK', openCount: 0, route: '/packing-sessions' },
                    { taskType: 'STOCKTAKE', openCount: 0, route: '/stocktakes' },
                    { taskType: 'TRANSFER', openCount: liveSummary?.openTransferCount ?? 0, route: '/stock-transfers' },
                    { taskType: 'DOCK_YARD', openCount: 0, route: '/dock-yard' },
                  ]).map((queue) => (
                    <span key={queue.taskType}>
                      {`• Hàng đợi ${taskTypeLabel(queue.taskType)}: ${queue.openCount}`}
                    </span>
                  ))}
                </div>
                {controlTower && controlTower.warehouses.length > 0 && (
                  <UiTableScroll>
                    <table className="ui-table-compact" aria-label="Bảng sức khỏe kho phân tán">
                      <thead>
                        <tr>
                          <th>Kho</th>
                          <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                          <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Đã giữ</th>
                          <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Cất hàng mở</th>
                          <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Lấy hàng mở</th>
                          <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Khóa tồn</th>
                          <th>Trạng thái sức khỏe</th>
                        </tr>
                      </thead>
                      <tbody>
                        {controlTower.warehouses.map((wh) => (
                          <tr key={wh.warehouseId}>
                            <td><strong>{wh.warehouseCode}</strong> — {wh.warehouseName}</td>
                            <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.onHandQuantity}</td>
                            <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.reservedQuantity}</td>
                            <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.openPutawayCount}</td>
                            <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.openPickingCount}</td>
                            <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.activeLocksCount}</td>
                            <td>
                              <UiBadge tone={wh.healthStatus === 'ATTENTION' ? 'warning' : 'success'}>
                                {healthStatusLabel(wh.healthStatus)}
                              </UiBadge>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </UiTableScroll>
                )}
              </UiCard>

              <UiCard title="Màn hình nghiệp vụ đã kích hoạt">
                <div className="dashboard-screen-count">{visibleProductionNavigation.length + 1}</div>
                <div className="dashboard-screen-list" style={{ maxHeight: 220, overflowY: 'auto' }}>
                  {visibleProductionNavigation.map((item) => (
                    <Link key={item.path} to={item.path}>
                      <span>{item.label}</span>
                      <small>{item.section}</small>
                    </Link>
                  ))}
                </div>
              </UiCard>
            </div>
          )}
        </div>
      )}
    </>
  );
};
