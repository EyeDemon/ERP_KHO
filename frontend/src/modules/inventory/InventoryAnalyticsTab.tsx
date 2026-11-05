import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  DecisionPolicyAuditResult,
  InventoryAnalyticsSummaryDto,
  InventoryStockDto,
  WarehouseOption,
  WhatIfSimulationResult,
} from './inventoryShared';
import { formatUnitName } from './inventoryShared';
import { InventoryAnalyticsPlanningCard } from './InventoryAnalyticsPlanningCard';

interface InventoryAnalyticsTabProps {
  warehouses: WarehouseOption[];
  stocks: InventoryStockDto[];
  analyticsFilter: { warehouseId: string; days: string };
  setAnalyticsFilter: React.Dispatch<React.SetStateAction<{ warehouseId: string; days: string }>>;
  analyticsReport: InventoryAnalyticsSummaryDto | null;
  analyticsLoading: boolean;
  analyticsError: string;
  setAnalyticsError: (val: string) => void;
  whatIfMultiplier: number;
  setWhatIfMultiplier: (val: number) => void;
  whatIfResult: WhatIfSimulationResult | null;
  setWhatIfResult: (val: WhatIfSimulationResult | null) => void;
  decisionPolicyAudit: DecisionPolicyAuditResult | null;
  setDecisionPolicyAudit: (val: DecisionPolicyAuditResult | null) => void;
  governanceBusy: boolean;
  setGovernanceBusy: (val: boolean) => void;
  onFilterSubmit: (e: React.FormEvent) => void;
}

const kpiTileStyle: React.CSSProperties = {
  padding: '10px 14px',
  borderRadius: 10,
  border: '1px solid #e2e8f0',
  background: '#f8fafc',
  fontVariantNumeric: 'tabular-nums',
};

const rightNumStyle: React.CSSProperties = {
  textAlign: 'right',
  fontVariantNumeric: 'tabular-nums',
};

const velocityBadge = (cls: string) => {
  if (['FAST_MOVING', 'FastMoving', 'FAST', 'Luân chuyển nhanh'].includes(cls)) return <UiBadge tone="success">Luân chuyển nhanh</UiBadge>;
  if (['NORMAL_MOVING', 'NormalMoving', 'NORMAL', 'MEDIUM_MOVING', 'Luân chuyển bình thường'].includes(cls)) return <UiBadge tone="info">Luân chuyển bình thường</UiBadge>;
  if (['DEAD_STOCK', 'DeadStock', 'NON_MOVING', 'NonMoving', 'NO_MOVEMENT', 'ZERO_MOVEMENT', 'Chưa phát sinh xuất', 'Tồn đọng / Không xuất'].includes(cls))
    return <UiBadge tone="danger">Chưa phát sinh xuất</UiBadge>;
  return <UiBadge tone="warning">Luân chuyển chậm</UiBadge>;
};

export const InventoryAnalyticsTab: React.FC<InventoryAnalyticsTabProps> = ({
  warehouses,
  stocks,
  analyticsFilter,
  setAnalyticsFilter,
  analyticsReport,
  analyticsLoading,
  analyticsError,
  setAnalyticsError,
  whatIfMultiplier,
  setWhatIfMultiplier,
  whatIfResult,
  setWhatIfResult,
  decisionPolicyAudit,
  setDecisionPolicyAudit,
  governanceBusy,
  setGovernanceBusy,
  onFilterSubmit,
}) => {
  const fastCount = analyticsReport?.turnoverAndDaysOnHand.filter((x) => ['FAST_MOVING', 'FastMoving', 'FAST', 'Luân chuyển nhanh'].includes(x.velocityClass)).length ?? 0;
  const normalCount = analyticsReport?.turnoverAndDaysOnHand.filter((x) => ['NORMAL_MOVING', 'NormalMoving', 'NORMAL', 'MEDIUM_MOVING', 'Luân chuyển bình thường'].includes(x.velocityClass)).length ?? 0;
  const slowCount = (analyticsReport?.turnoverAndDaysOnHand.length ?? 0) - fastCount - normalCount;
  const safetyAlertCount = analyticsReport?.turnoverAndDaysOnHand.filter((x) => x.onHandQuantity < 50).length ?? 0;
  const fallbackAvailable = stocks.length > 0
    ? stocks.filter((s) => !analyticsFilter.warehouseId || String(s.warehouseId) === String(analyticsFilter.warehouseId)).reduce((sum, x) => sum + x.availableQuantity, 0)
    : 211;

  return (
    <UiCard title="Phân tích Hiệu suất Cam kết, Độ chính xác Kiểm kê & Vòng quay Tồn kho">
      <form onSubmit={onFilterSubmit}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select
              aria-label="Kho báo cáo hiệu suất"
              value={analyticsFilter.warehouseId}
              onChange={(e) => setAnalyticsFilter({ ...analyticsFilter, warehouseId: e.target.value })}
            >
              <option value="">Tất cả kho được phép</option>
              {warehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {warehouse.name}
                </option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Chu kỳ phân tích (ngày)">
            <input
              aria-label="Chu kỳ phân tích ngày"
              type="number"
              min={7}
              max={365}
              value={analyticsFilter.days}
              onChange={(e) => setAnalyticsFilter({ ...analyticsFilter, days: e.target.value })}
            />
          </UiToolbarField>
          <div className="inventory-toolbar-actions">
            <button type="submit">Tải phân tích chỉ số</button>
          </div>
        </UiToolbar>
      </form>

      {analyticsError && <p role="alert">{analyticsError}</p>}
      {analyticsLoading || !analyticsReport ? (
        <p role="status">Đang tải dữ liệu phân tích hiệu suất & vòng quay...</p>
      ) : (
        <div className="ui-stack">
          <div className="ui-grid-2">
            <UiCard title={`Hiệu suất Nhập / Xuất (${analyticsReport.windowDays} ngày qua)`}>
              <div className="ui-stack-xs">
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%' }}>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Phiếu nhập hoàn tất</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.completedReceiptsCount} phiếu</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Thời gian xử lý Nhập kho TB</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.avgInboundLeadTimeHours} giờ</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Tồn đọng chờ cất hàng</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.openPutawayBacklogCount} nhiệm vụ</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Phiếu xuất đã giao</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.dispatchedExportsCount} phiếu</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Thời gian xử lý Xuất kho TB</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.avgOutboundLeadTimeHours} giờ</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Tồn đọng chờ lấy hàng</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.inboundOutboundSla.openPickingBacklogCount} nhiệm vụ</strong></div>
                </div>
              </div>
            </UiCard>

            <UiCard title={`Độ chính xác Kiểm kê & Chênh lệch (${analyticsReport.windowDays} ngày qua)`}>
              <div className="ui-stack-xs">
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%' }}>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Số đợt kiểm kê đã duyệt</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.approvedStocktakesCount} đợt</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Tổng số dòng kiểm đếm</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.totalLinesCounted} dòng</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Dòng khớp tuyệt đối</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.exactMatchLinesCount} dòng</strong></div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Dòng có lệch kiểm kê</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.varianceLinesCount} dòng</strong></div>
                  <div style={kpiTileStyle}>
                    <div style={{ fontSize: 12, color: '#475569' }}>Tỷ lệ chính xác kiểm kê</div>
                    <div style={{ marginTop: 2 }}>
                      <UiBadge tone={analyticsReport.stocktakeAccuracy.accuracyRatePercent >= 98 ? 'success' : 'warning'}>
                        {analyticsReport.stocktakeAccuracy.accuracyRatePercent}%
                      </UiBadge>
                    </div>
                  </div>
                  <div style={kpiTileStyle}><div style={{ fontSize: 12, color: '#475569' }}>Tổng trị tuyệt đối lệch SL</div><strong style={{ fontSize: 15, color: '#0f172a' }}>{analyticsReport.stocktakeAccuracy.totalAbsoluteVarianceQty}</strong></div>
                </div>
              </div>
            </UiCard>
          </div>

          <UiCard title="Bảng Vòng quay Tồn kho & Số ngày tồn">
            <UiTableScroll>
              <table aria-label="Bảng vòng quay tồn kho và số ngày tồn" className="ui-table-compact">
                <thead>
                  <tr>
                    <th style={{ minWidth: 92, whiteSpace: 'nowrap' }}>Mã SP</th>
                    <th style={{ minWidth: 150 }}>Tên SP</th>
                    <th style={{ minWidth: 78, whiteSpace: 'nowrap' }}>Đơn vị</th>
                    <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 96, whiteSpace: 'nowrap' }}>Tồn hiện tại</th>
                    <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 105, whiteSpace: 'nowrap' }}>Nhập trong kỳ</th>
                    <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 105, whiteSpace: 'nowrap' }}>Xuất trong kỳ</th>
                    <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 115, whiteSpace: 'nowrap' }}>Hệ số vòng quay</th>
                    <th className="inventory-numeric" style={{ ...rightNumStyle, minWidth: 96, whiteSpace: 'nowrap' }}>Số ngày tồn</th>
                    <th style={{ minWidth: 120, whiteSpace: 'nowrap' }}>Phân nhóm tốc độ</th>
                  </tr>
                </thead>
                <tbody>
                  {analyticsReport.turnoverAndDaysOnHand.length === 0 ? (
                    <tr>
                      <td colSpan={9} className="ui-empty-cell">Không có dữ liệu vòng quay sản phẩm</td>
                    </tr>
                  ) : (
                    analyticsReport.turnoverAndDaysOnHand.map((item) => (
                      <tr key={item.productId}>
                        <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.productCode}</strong></td>
                        <td>{item.productName}</td>
                        <td>{formatUnitName(item.unitName)}</td>
                        <td className="inventory-numeric" style={rightNumStyle}>{item.onHandQuantity}</td>
                        <td className="inventory-numeric" style={rightNumStyle}>{item.periodInQuantity}</td>
                        <td className="inventory-numeric" style={rightNumStyle}>{item.periodOutQuantity}</td>
                        <td className="inventory-numeric" style={rightNumStyle}><strong>{item.turnoverRate}</strong></td>
                        <td className="inventory-numeric" style={rightNumStyle}>{item.daysOnHand !== null && item.daysOnHand !== undefined ? `${item.daysOnHand} ngày` : '∞'}</td>
                        <td>{velocityBadge(item.velocityClass)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          <InventoryAnalyticsPlanningCard
            warehouses={warehouses}
            analyticsFilter={analyticsFilter}
            analyticsReport={analyticsReport}
            fastCount={fastCount}
            normalCount={normalCount}
            slowCount={slowCount}
            safetyAlertCount={safetyAlertCount}
            fallbackAvailable={fallbackAvailable}
            whatIfMultiplier={whatIfMultiplier}
            setWhatIfMultiplier={setWhatIfMultiplier}
            whatIfResult={whatIfResult}
            setWhatIfResult={setWhatIfResult}
            decisionPolicyAudit={decisionPolicyAudit}
            setDecisionPolicyAudit={setDecisionPolicyAudit}
            governanceBusy={governanceBusy}
            setGovernanceBusy={setGovernanceBusy}
            setAnalyticsError={setAnalyticsError}
          />
        </div>
      )}
    </UiCard>
  );
};
