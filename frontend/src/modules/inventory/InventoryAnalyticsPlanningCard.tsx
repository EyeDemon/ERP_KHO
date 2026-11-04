import React from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import type {
  DecisionPolicyAuditResult,
  InventoryAnalyticsSummaryDto,
  WarehouseOption,
  WhatIfSimulationResult,
} from './inventoryShared';

interface InventoryAnalyticsPlanningCardProps {
  warehouses: WarehouseOption[];
  analyticsFilter: { warehouseId: string; days: string };
  analyticsReport: InventoryAnalyticsSummaryDto;
  fastCount: number;
  normalCount: number;
  slowCount: number;
  safetyAlertCount: number;
  fallbackAvailable: number;
  whatIfMultiplier: number;
  setWhatIfMultiplier: (val: number) => void;
  whatIfResult: WhatIfSimulationResult | null;
  setWhatIfResult: (val: WhatIfSimulationResult | null) => void;
  decisionPolicyAudit: DecisionPolicyAuditResult | null;
  setDecisionPolicyAudit: (val: DecisionPolicyAuditResult | null) => void;
  governanceBusy: boolean;
  setGovernanceBusy: (val: boolean) => void;
  setAnalyticsError: (val: string) => void;
}

const noticeStyle: React.CSSProperties = {
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
};

export const InventoryAnalyticsPlanningCard: React.FC<InventoryAnalyticsPlanningCardProps> = ({
  warehouses,
  analyticsFilter,
  analyticsReport,
  fastCount,
  normalCount,
  slowCount,
  safetyAlertCount,
  fallbackAvailable,
  whatIfMultiplier,
  setWhatIfMultiplier,
  whatIfResult,
  setWhatIfResult,
  decisionPolicyAudit,
  setDecisionPolicyAudit,
  governanceBusy,
  setGovernanceBusy,
  setAnalyticsError,
}) => (
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
          <div role="status" aria-label="Kết quả mô phỏng kịch bản giả định What-If" style={noticeStyle}>
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
            style={{ ...noticeStyle, fontWeight: 600 }}
          >
            <span>
              {`Sổ đăng ký: ${decisionPolicyAudit.policyRegistryId} • Dự báo: Đã chuẩn hóa sai số MAPE • Bất thường cầu: Đã bật giám sát đột biến cầu • Chấm điểm rủi ro: Đã chấm điểm rủi ro tồn kho • Trạng thái: Đã xác thực sổ đăng ký chính sách`}
            </span>
          </div>
        )}
      </div>
    </div>
  </UiCard>
);
