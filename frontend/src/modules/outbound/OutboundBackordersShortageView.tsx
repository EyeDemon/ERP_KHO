import React from 'react';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { type Backorder, backorderLabels, tone } from './outboundDemandTypes';

export function OutboundBackordersShortageView({
  backorders,
  openBackorderRemaining,
  fulfillmentRatePct,
  canManageBackorders,
  busy,
  governanceBusy,
  onTriggerExpediteBackorder,
  expediteResult,
  recoverQty,
  setRecoverQty,
  backorderCancelQty,
  backorderCancelReason,
  setBackorderCancelReason,
  onMutate,
}: {
  backorders: Backorder[];
  openBackorderRemaining: number;
  fulfillmentRatePct: number;
  canManageBackorders: boolean;
  busy: string;
  governanceBusy: boolean;
  onTriggerExpediteBackorder: () => Promise<void>;
  expediteResult: { expediteId: string; orderNumber: string; allocationWaveQueued: boolean } | null;
  recoverQty: Record<number, string>;
  setRecoverQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelQty: Record<number, string>;
  backorderCancelReason: Record<number, string>;
  setBackorderCancelReason: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  onMutate: (key: string, path: string, body: object, success: string) => Promise<unknown>;
}) {
  const effectiveFulfillmentRate = backorders.length === 0 ? 100 : fulfillmentRatePct;

  return (
    <>
      <UiMetricGrid data-testid="backorders-kpi-grid">
        <UiMetric value={`${backorders.length} dòng`} label="Đơn thiếu hàng đang mở" isZero={backorders.length === 0} />
        <UiMetric value={`${openBackorderRemaining} ĐVT`} label="Tổng SL thiếu chờ bù" isZero={openBackorderRemaining === 0} />
        <UiMetric
          value={`${backorders.filter((b) => b.remainingQuantity > 0).length} đơn`}
          label="Đơn ưu tiên chờ cấp phát"
          isZero={backorders.filter((b) => b.remainingQuantity > 0).length === 0}
        />
        <UiMetric
          value={backorders.length === 0 ? '100%' : `${effectiveFulfillmentRate}%`}
          label={backorders.length === 0 ? 'Tỷ lệ đáp ứng • Không có đơn thiếu' : 'Tỷ lệ đáp ứng đơn hàng'}
        />
      </UiMetricGrid>

      <UiCard
        title="Danh sách đơn thiếu hàng chờ cấp phát bù"
        subtitle="Danh sách các dòng đơn thiếu tồn kho cần cấp phát bù hoặc hủy phần thiếu"
      >
        <div className="ui-inline-wrap" style={{ marginBottom: 12, justifyContent: 'flex-end', alignItems: 'center', gap: 10 }}>
          {backorders.length === 0 && (
            <span data-testid="backorder-empty-disabled-reason" style={{ fontSize: 12.5, color: '#475569' }}>
              Không có đơn thiếu hàng cần xử lý
            </span>
          )}
          <button
            type="button"
            className="ui-btn ui-btn-primary"
            data-testid="btn-trigger-backorder-allocation"
            disabled={backorders.length === 0 || !!busy || governanceBusy}
            title={backorders.length === 0 ? 'Không có đơn thiếu hàng cần xử lý' : undefined}
            onClick={() => void onTriggerExpediteBackorder()}
            style={{ background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 700, whiteSpace: 'nowrap', opacity: backorders.length === 0 ? 0.55 : 1, cursor: backorders.length === 0 ? 'not-allowed' : 'pointer' }}
          >
            Kích hoạt ưu tiên cấp phát đơn thiếu hàng
          </button>
        </div>

        {expediteResult ? (
          <div
            role="status"
            style={{ marginBottom: 12, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 13 }}
          >
            <strong>{expediteResult.expediteId}</strong> • Đã đưa đơn <strong>{expediteResult.orderNumber}</strong> vào hàng đợi ưu tiên cấp phát tồn kho.
          </div>
        ) : null}

        {backorders.length === 0 ? (
          <UiEmptyState
            title="Không có đơn thiếu hàng chờ xử lý"
            detail="Hiện không có đơn thiếu hàng nào đang chờ xử lý. Toàn bộ đơn bán hàng hiện tại đã được đáp ứng đủ tồn kho."
          />
        ) : (
          <UiTableScroll>
            <table className="ui-table-compact" data-testid="backorders-table" aria-label="Danh sách đơn thiếu hàng">
              <thead>
                <tr>
                  <th className="ui-sticky-id">Mã thiếu hàng</th>
                  <th>Trạng thái</th>
                  <th>Đơn bán &amp; Kho</th>
                  <th>Sản phẩm (SKU)</th>
                  <th style={{ textAlign: 'right' }}>SL Thiếu</th>
                  <th style={{ textAlign: 'right' }}>SL Đã bù</th>
                  <th style={{ textAlign: 'right' }}>SL Còn thiếu</th>
                  <th>Xử lý phục hồi / Hủy</th>
                </tr>
              </thead>
              <tbody>
                {backorders.map((bo) => (
                  <tr key={bo.id} data-testid={`backorder-row-${bo.id}`}>
                    <td className="cell-nowrap ui-sticky-id" style={{ background: '#ffffff' }}><strong style={{ whiteSpace: 'nowrap' }}>{bo.backorderCode}</strong></td>
                    <td className="cell-nowrap">
                      <UiBadge tone={tone(bo.status)}>{backorderLabels[bo.status] ?? bo.status}</UiBadge>
                    </td>
                    <td>
                      <div style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{bo.orderCode}</div>
                      <div className="ui-muted-text" style={{ fontSize: 11.5 }}>
                        {bo.externalOrderId} • {formatWarehouseLabel(bo.warehouseName, undefined, bo.warehouseId)}
                      </div>
                    </td>
                    <td>
                      <div style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{bo.productCode}</div>
                      <div className="ui-muted-text" style={{ fontSize: 11.5 }}>{bo.productName}</div>
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{bo.quantity}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{bo.recoveredQuantity}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 700, color: '#b91c1c' }}>{bo.remainingQuantity}</td>
                    <td>
                      {canManageBackorders && bo.remainingQuantity > 0 ? (
                        <div className="ui-inline-wrap" style={{ gap: 6 }}>
                          <input
                            type="number" step="0.0001" placeholder="SL bù" style={{ width: 80 }}
                            aria-label={`Số lượng phục hồi ${bo.backorderCode}`}
                            value={recoverQty[bo.id] ?? ''}
                            onChange={(e) => setRecoverQty((prev) => ({ ...prev, [bo.id]: e.target.value }))}
                          />
                          <button
                            type="button" disabled={!!busy} style={{ whiteSpace: 'nowrap' }}
                            onClick={() => void onMutate(`bo-recover-${bo.id}`, `/api/v1/backorders/${bo.id}/recover`, { quantity: roundQty4(Number(recoverQty[bo.id] || bo.remainingQuantity)) }, `Đã phục hồi tồn cho ${bo.backorderCode}.`)}
                          >
                            Cấp phát bù
                          </button>
                          <input
                            type="text" placeholder="Lý do hủy..." style={{ width: 110 }}
                            aria-label={`Lý do hủy ${bo.backorderCode}`}
                            value={backorderCancelReason[bo.id] ?? ''}
                            onChange={(e) => setBackorderCancelReason((prev) => ({ ...prev, [bo.id]: e.target.value }))}
                          />
                          <button
                            type="button" disabled={!!busy} style={{ whiteSpace: 'nowrap' }}
                            onClick={() => void onMutate(`bo-cancel-${bo.id}`, `/api/v1/backorders/${bo.id}/cancel-remaining`, { quantity: roundQty4(Number(backorderCancelQty[bo.id] || bo.remainingQuantity)), reason: (backorderCancelReason[bo.id] || 'Khách hủy phần thiếu').trim() }, `Đã hủy phần thiếu ${bo.backorderCode}.`)}
                          >
                            Hủy thiếu
                          </button>
                        </div>
                      ) : (
                        <span className="ui-muted-text" style={{ whiteSpace: 'nowrap' }}>Chỉ xem</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </>
  );
}
