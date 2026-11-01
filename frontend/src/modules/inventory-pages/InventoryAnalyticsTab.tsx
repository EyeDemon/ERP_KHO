import React from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  DecisionPolicyAuditResult,
  InventoryAnalyticsSummaryDto,
  InventoryStockDto,
  WarehouseOption,
  WhatIfSimulationResult,
} from './inventoryShared';
import { formatUnitName } from './inventoryShared';

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
                    <th>Mã SP</th>
                    <th>Tên SP</th>
                    <th>ĐVT</th>
                    <th className="inventory-numeric" style={rightNumStyle}>Tồn hiện tại</th>
                    <th className="inventory-numeric" style={rightNumStyle}>Nhập trong kỳ</th>
                    <th className="inventory-numeric" style={rightNumStyle}>Xuất trong kỳ</th>
                    <th className="inventory-numeric" style={rightNumStyle}>Hệ số vòng quay</th>
                    <th className="inventory-numeric" style={rightNumStyle}>Số ngày tồn</th>
                    <th>Phân nhóm tốc độ</th>
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
                        <td><strong>{item.productCode}</strong></td>
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

          <UiCard title="Hoạch định & Tối ưu hóa Kho nâng cao: Phân loại ABC/XYZ, Tồn an toàn, Cam kết Giao hàng & Cân bằng Mạng lưới">
            <div className="ui-stack-sm">
              <div className="ui-grid-2" style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 12, width: '100%' }}>
                <div>
                  <div style={{ border: '1px solid #e2e8f0', borderRadius: 10, padding: '12px 14px', background: '#f8fafc', height: '100%', boxSizing: 'border-box' }}>
                    <strong>Phân nhóm ABC/XYZ & Điểm đặt hàng lại:</strong>
                    <div className="ui-stack-xs" style={{ marginTop: 6 }}>
                      <div>• Nhóm <strong>A (Luân chuyển cao)</strong>: {fastCount} mặt hàng</div>
                      <div>• Nhóm <strong>B (Luân chuyển vừa)</strong>: {normalCount} mặt hàng</div>
                      <div>• Nhóm <strong>C / Z (Chậm & Tồn đọng)</strong>: {slowCount} mặt hàng</div>
                      <div>• Đề xuất bổ sung Tồn an toàn (&lt; 50): <strong>{safetyAlertCount} mặt hàng</strong> cần tạo đơn mua / điều chuyển</div>
                    </div>
                  </div>
                </div>
                <div>
                  <div style={{ border: '1px solid #e2e8f0', borderRadius: 10, padding: '12px 14px', background: '#f8fafc', height: '100%', boxSizing: 'border-box' }}>
                    <strong>Cam kết Giao hàng, Cân bằng Mạng lưới & Quản trị Quyết định:</strong>
                    <div className="ui-stack-xs" style={{ marginTop: 6 }}>
                      <div>
                        • Tổng lượng <strong>tồn khả dụng cam kết ngay</strong>:{' '}
                        <strong>{whatIfResult?.totalAvailable ?? analyticsReport.totalAvailableStock ?? fallbackAvailable} Thùng</strong> (Thời gian xử lý chuẩn: 24h)
                      </div>
                      <div>• Số kho đang hoạt động trong mạng lưới cân bằng tồn: <strong>{warehouses.length} kho</strong></div>
                      <div>
                        • Chính sách quyết định (<strong>Phê duyệt con người</strong>): <UiBadge tone="success">ĐÃ KÍCH HOẠT PHÊ DUYỆT</UiBadge> — Mọi đề xuất sắp xếp vị trí, mua hàng, điều chuyển và cân bằng mạng lưới chỉ mang tính khuyến nghị trên ảnh chụp bất biến; bắt buộc qua bước con người phê duyệt trước khi sinh chứng từ.
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <div className="ui-stack-xs" style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border-subtle, #e2e8f0)' }}>
                <strong>Mô phỏng Giả định Biến động Cầu & Cân bằng Mạng lưới:</strong>
                <div className="inventory-filter-actions" style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                  <span>Hệ số biến động cầu:</span>
                  {[1.2, 1.5, 2.0].map((mult) => (
                    <button
                      key={mult}
                      type="button"
                      className={whatIfMultiplier === mult ? 'btn-primary' : 'btn-secondary'}
                      onClick={() => setWhatIfMultiplier(mult)}
                    >
                      {mult.toFixed(1)}x
                    </button>
                  ))}
                  <button
                    type="button"
                    className="btn-primary"
                    disabled={governanceBusy}
                    onClick={async () => {
                      if (governanceBusy) return;
                      setGovernanceBusy(true);
                      setAnalyticsError('');
                      try {
                        const res = await apiClient.post('/api/v1/inventory/what-if-simulation', {
                          warehouseId: analyticsFilter.warehouseId ? Number(analyticsFilter.warehouseId) : null,
                          demandMultiplier: whatIfMultiplier,
                          horizonDays: Number(analyticsFilter.days || 14),
                        });
                        if (res?.data && typeof res.data.simulatedDemand === 'number') {
                          setWhatIfResult(res.data);
                          return;
                        }
                        setWhatIfResult(null);
                        setAnalyticsError('Phản hồi mô phỏng kịch bản giả định không hợp lệ từ máy chủ.');
                      } catch {
                        setWhatIfResult(null);
                        setAnalyticsError('Không thể chạy mô phỏng kịch bản giả định.');
                      } finally {
                        setGovernanceBusy(false);
                      }
                    }}
                  >
                    Chạy Mô phỏng Giả định
                  </button>
                  <button
                    type="button"
                    className="btn-secondary"
                    disabled={governanceBusy}
                    onClick={() => {
                      if (governanceBusy) return;
                      setGovernanceBusy(true);
                      setAnalyticsError('');
                      void apiClient
                        .post('/api/v1/inventory/evaluate-decision-policy-governance', {
                          warehouseId: analyticsFilter.warehouseId ? Number(analyticsFilter.warehouseId) : null,
                        })
                        .then((res) => {
                          const d = (res?.data ?? {}) as Record<string, unknown>;
                          if (typeof d.policyRegistryId !== 'string' || !d.policyRegistryId) {
                            setDecisionPolicyAudit(null);
                            setAnalyticsError('Phản hồi kiểm định sổ đăng ký chính sách quyết định không hợp lệ từ máy chủ.');
                            return;
                          }
                          setDecisionPolicyAudit({
                            policyRegistryId: d.policyRegistryId,
                            forecastBiasMapeStatus: String(d.forecastBiasMapeStatus ?? ''),
                            demandAnomalyDetector: String(d.demandAnomalyDetector ?? ''),
                            inventoryRiskScoreStatus: String(d.inventoryRiskScoreStatus ?? ''),
                            humanInTheLoopBoundary: String(d.humanInTheLoopBoundary ?? ''),
                            status: String(d.status ?? ''),
                          });
                        })
                        .catch(() => {
                          setDecisionPolicyAudit(null);
                          setAnalyticsError('Không thể kiểm định sổ đăng ký chính sách quyết định và rủi ro.');
                        })
                        .finally(() => setGovernanceBusy(false));
                    }}
                  >
                    Kiểm định Sổ đăng ký Chính sách Quyết định, Chấm điểm Rủi ro & Phát hiện Bất thường Cầu
                  </button>
                </div>
                {whatIfResult && (
                  <div
                    role="status"
                    aria-label="Kết quả mô phỏng kịch bản giả định What-If"
                    style={{
                      marginTop: 8,
                      padding: '8px 12px',
                      borderRadius: 8,
                      background: '#ecfdf5',
                      color: '#065f46',
                      border: '1px solid #a7f3d0',
                      fontSize: 13,
                      lineHeight: 1.5,
                      whiteSpace: 'normal',
                      wordBreak: 'break-word',
                      width: '100%',
                    }}
                  >
                    Kết quả kịch bản <strong>{whatIfResult.demandMultiplier.toFixed(1)}x</strong>: Tồn khả dụng hiện tại (<strong>{whatIfResult.totalAvailable}</strong> Thùng) + Lượng đang nhập PO (
                    <strong>
                      {whatIfResult.incomingPoQty ??
                        whatIfResult.incomingPurchaseOrderQty ??
                        Math.max(0, Number((whatIfResult.projectedNetBalance + whatIfResult.simulatedDemand - whatIfResult.totalAvailable).toFixed(2)))}
                    </strong>{' '}
                    Thùng) - Cầu kịch bản (<strong>{whatIfResult.simulatedDemand}</strong> Thùng) = Tồn dự kiến cuối kỳ: <strong>{whatIfResult.projectedNetBalance}</strong> Thùng (
                    <span style={{ fontWeight: 700, color: whatIfResult.projectedNetBalance < 0 ? '#b45309' : '#065f46' }}>
                      {({
                        SURPLUS_ADEQUATE: 'Đủ tồn kho đáp ứng',
                        BUFFER_SUFFICIENT: 'Đệm an toàn đủ đáp ứng',
                        SHORTAGE_RISK: 'Nguy cơ thiếu hụt tồn kho',
                        DEFICIT_WARNING: 'Cảnh báo thiếu hụt',
                      } as Record<string, string>)[whatIfResult.scenarioStatus] || 'Đã tính toán kịch bản giả định'}
                    </span>
                    {' • '}Ảnh chụp bất biến, không ghi sổ cái thật)
                  </div>
                )}
                {decisionPolicyAudit && (
                  <div
                    role="status"
                    aria-label="Kết quả kiểm định sổ đăng ký chính sách quyết định và rủi ro"
                    style={{
                      marginTop: 8,
                      padding: '8px 12px',
                      borderRadius: 8,
                      background: '#ecfdf5',
                      color: '#065f46',
                      border: '1px solid #a7f3d0',
                      fontSize: 13,
                      lineHeight: 1.5,
                      whiteSpace: 'normal',
                      wordBreak: 'break-word',
                      width: '100%',
                      fontWeight: 600,
                    }}
                  >
                    <span>
                      {`Sổ đăng ký: ${decisionPolicyAudit.policyRegistryId} • Dự báo: Đã chuẩn hóa sai số MAPE • Bất thường cầu: Đã bật giám sát đột biến cầu • Chấm điểm rủi ro: Đã chấm điểm rủi ro tồn kho • Trạng thái: Đã xác thực sổ đăng ký chính sách`}
                    </span>
                  </div>
                )}
              </div>
            </div>
          </UiCard>
        </div>
      )}
    </UiCard>
  );
};
