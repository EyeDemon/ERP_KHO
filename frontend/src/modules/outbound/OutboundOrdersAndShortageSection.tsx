import React, { useState } from 'react';
import { Copy, Plus } from 'lucide-react';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiSortTh, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { OutboundBackordersShortageView } from './OutboundBackordersShortageView';
import { type Backorder, type SalesOrder, orderLabels, tone } from './outboundDemandTypes';
import { OutboundSalesOrderMobileCards, useIsMobileViewport } from './OutboundSalesOrderMobileCards';

interface OutboundOrdersAndShortageSectionProps {
  mode: 'sales-orders' | 'backorders';
  orders: SalesOrder[];
  backorders: Backorder[];
  totalOrderedQty: number;
  totalReservedQty: number;
  totalAllocatedQty: number;
  totalShippedQty: number;
  openBackorderRemaining: number;
  fulfillmentRatePct: number;
  canCreate: boolean;
  masterReady: boolean;
  canManageBackorders: boolean;
  busy: string;
  governanceBusy: boolean;
  showCreate: boolean;
  onOpenCreate: () => void;
  onSelectDetail: (id: number) => Promise<void>;
  onCopyNotice: (msg: string) => void;
  onEvaluateAtpRouting: () => Promise<void>;
  onTriggerExpediteBackorder: () => Promise<void>;
  atpRoutingResult: { evaluationId: string; atpCtpPromiseStatus: string; fulfillmentNodeRouting: string; allocationFairnessPolicy: string; serviceSegmentation: string; status: string } | null;
  expediteResult: { expediteId: string; orderNumber: string; allocationWaveQueued: boolean } | null;
  recoverQty: Record<number, string>;
  setRecoverQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelQty: Record<number, string>;
  setBackorderCancelQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelReason: Record<number, string>;
  setBackorderCancelReason: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  onMutate: (key: string, path: string, body: object, success: string) => Promise<unknown>;
}

type SalesOrderSortKey = 'orderCode' | 'customerName' | 'status' | 'orderedQuantity' | 'shippedQuantity';

export const OutboundOrdersAndShortageSection: React.FC<OutboundOrdersAndShortageSectionProps> = ({
  mode, orders, backorders, totalOrderedQty, totalReservedQty, totalAllocatedQty,
  totalShippedQty, openBackorderRemaining, fulfillmentRatePct, canCreate, masterReady,
  canManageBackorders, busy, governanceBusy, showCreate, onOpenCreate, onSelectDetail,
  onCopyNotice, onEvaluateAtpRouting, onTriggerExpediteBackorder, atpRoutingResult,
  expediteResult, recoverQty, setRecoverQty, backorderCancelQty, backorderCancelReason,
  setBackorderCancelReason, onMutate,
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [sortKey, setSortKey] = useState<SalesOrderSortKey>('orderCode');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const isMobileViewport = useIsMobileViewport();

  const toggleSort = (key: SalesOrderSortKey) => {
    if (sortKey === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else {
      setSortKey(key);
      setSortDir('asc');
    }
  };

  if (mode === 'backorders') {
    return (
      <OutboundBackordersShortageView
        backorders={backorders}
        openBackorderRemaining={openBackorderRemaining}
        fulfillmentRatePct={fulfillmentRatePct}
        canManageBackorders={canManageBackorders}
        busy={busy}
        governanceBusy={governanceBusy}
        onTriggerExpediteBackorder={onTriggerExpediteBackorder}
        expediteResult={expediteResult}
        recoverQty={recoverQty}
        setRecoverQty={setRecoverQty}
        backorderCancelQty={backorderCancelQty}
        backorderCancelReason={backorderCancelReason}
        setBackorderCancelReason={setBackorderCancelReason}
        onMutate={onMutate}
      />
    );
  }

  const readyOrdersCount = orders.filter((o) => o.backorderQuantity === 0).length;
  const filteredOrders = orders.filter((o) => {
    const matchesStatus = statusFilter === 'ALL' || o.status === statusFilter;
    const q = searchTerm.trim().toLowerCase();
    const matchesQuery =
      !q ||
      o.orderCode.toLowerCase().includes(q) ||
      o.externalOrderId.toLowerCase().includes(q) ||
      o.customerName.toLowerCase().includes(q);
    return matchesStatus && matchesQuery;
  });

  const sortedOrders = [...filteredOrders].sort((a, b) => {
    const mult = sortDir === 'asc' ? 1 : -1;
    if (sortKey === 'orderCode') return a.orderCode.localeCompare(b.orderCode, 'vi') * mult;
    if (sortKey === 'customerName') return a.customerName.localeCompare(b.customerName, 'vi') * mult;
    if (sortKey === 'status') return (orderLabels[a.status] ?? a.status).localeCompare(orderLabels[b.status] ?? b.status, 'vi') * mult;
    if (sortKey === 'orderedQuantity') return (a.orderedQuantity - b.orderedQuantity) * mult;
    return (a.shippedQuantity - b.shippedQuantity) * mult;
  });

  return (
    <>
      <UiMetricGrid data-testid="sales-orders-kpi-grid">
        <UiMetric value={`${orders.length} đơn (${totalOrderedQty} ĐVT)`} label="Tổng đơn bán hàng" isZero={orders.length === 0} />
        <UiMetric value={`${readyOrdersCount} đơn`} label="Đơn đủ tồn sẵn sàng" isZero={readyOrdersCount === 0} />
        <UiMetric value={`${totalReservedQty} / ${totalAllocatedQty}`} label="Đang giữ / Phân bổ" isZero={totalReservedQty === 0 && totalAllocatedQty === 0} />
        <UiMetric value={`${totalShippedQty} ĐVT`} label="Đã xuất kho" isZero={totalShippedQty === 0} />
      </UiMetricGrid>

      <UiCard
        title="Danh sách đơn bán hàng"
        subtitle="Tra cứu, lọc trạng thái và kiểm tra tồn khả dụng trước khi phát hành đơn bán"
      >
        <div className="ui-inline-wrap" style={{ gap: 8, marginBottom: 12, justifyContent: 'flex-end' }}>
          <button
            type="button"
            className="ui-btn ui-btn-secondary"
            data-testid="btn-evaluate-atp-ctp"
            disabled={!!busy || governanceBusy}
            onClick={() => void onEvaluateAtpRouting()}
          >
            Kiểm tra tồn khả dụng
          </button>
          {canCreate && masterReady && !showCreate ? (
            <button
              type="button"
              className="ui-btn ui-btn-primary"
              data-testid="btn-open-create-so"
              onClick={onOpenCreate}
              style={{ display: 'inline-flex', alignItems: 'center', gap: 6, background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 700 }}
            >
              <Plus size={15} strokeWidth={2} aria-hidden="true" />
              <span>Tạo đơn bán hàng</span>
            </button>
          ) : null}
        </div>
        {atpRoutingResult ? (
          <div
            role="status"
            style={{ marginBottom: 12, padding: '8px 12px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e3a8a', fontSize: 13 }}
          >
            <strong>{atpRoutingResult.evaluationId}</strong> • {atpRoutingResult.atpCtpPromiseStatus} • Định tuyến: {atpRoutingResult.fulfillmentNodeRouting} • {atpRoutingResult.status}
          </div>
        ) : null}

        <div className="ui-inline-wrap" style={{ marginBottom: 12, gap: 10 }}>
          <input
            type="search"
            data-testid="input-search-sales-orders"
            aria-label="Tìm kiếm đơn bán hàng"
            placeholder="Tìm theo mã đơn SO, mã đơn kênh bán hoặc tên khách hàng..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            style={{ flex: 1, minWidth: 240 }}
          />
          <select
            aria-label="Lọc trạng thái đơn bán hàng"
            data-testid="select-filter-sales-orders"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            style={{ minWidth: 180 }}
          >
            <option value="ALL">Tất cả trạng thái ({orders.length})</option>
            <option value="Released">Đã phát hành</option>
            <option value="Allocated">Đã phân bổ</option>
            <option value="PartiallyAllocated">Phân bổ một phần</option>
            <option value="Held">Đang tạm giữ</option>
            <option value="Completed">Đã hoàn tất</option>
          </select>
        </div>

        {sortedOrders.length === 0 ? (
          <UiEmptyState
            title="Không có đơn bán hàng phù hợp"
            detail="Không có đơn bán hàng phù hợp với bộ lọc hiện tại. Hãy thử đổi từ khóa tìm kiếm hoặc trạng thái lọc."
          />
        ) : (
          <>
            {isMobileViewport && (
              <OutboundSalesOrderMobileCards
                orders={sortedOrders}
                onSelectDetail={onSelectDetail}
                onCopyNotice={onCopyNotice}
              />
            )}

            <div className="ui-desktop-table-wrap">
              <UiTableScroll>
                <table className="ui-table-compact" data-testid="sales-orders-table" aria-label="Danh sách đơn bán hàng">
                  <thead>
                    <tr>
                      <UiSortTh label="Mã đơn bán" active={sortKey === 'orderCode'} direction={sortDir} onToggle={() => toggleSort('orderCode')} className="ui-sticky-id" />
                      <UiSortTh label="Trạng thái" active={sortKey === 'status'} direction={sortDir} onToggle={() => toggleSort('status')} />
                      <UiSortTh label="Khách hàng / Kênh bán" active={sortKey === 'customerName'} direction={sortDir} onToggle={() => toggleSort('customerName')} />
                      <th style={{ whiteSpace: 'nowrap' }}>Kho xuất</th>
                      <UiSortTh label="SL Đặt" align="right" active={sortKey === 'orderedQuantity'} direction={sortDir} onToggle={() => toggleSort('orderedQuantity')} />
                      <th style={{ textAlign: 'right', whiteSpace: 'normal', maxWidth: 88, lineHeight: 1.2 }}>Đã giữ / Phân bổ</th>
                      <UiSortTh label="Đã xuất" align="right" active={sortKey === 'shippedQuantity'} direction={sortDir} onToggle={() => toggleSort('shippedQuantity')} />
                      <th className="ui-sticky-action" style={{ width: 108, maxWidth: 136 }}>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {sortedOrders.map((order) => (
                      <tr key={order.id} data-testid={`sales-order-row-${order.id}`}>
                        <td className="cell-nowrap ui-sticky-id" style={{ background: '#ffffff' }}>
                          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                            <strong style={{ whiteSpace: 'nowrap' }}>{order.orderCode}</strong>
                            <button
                              type="button"
                              className="ui-inline-icon-btn"
                              title="Sao chép mã đơn"
                              aria-label={`Sao chép mã đơn ${order.orderCode}`}
                              data-testid={`btn-copy-so-${order.id}`}
                              onClick={() => {
                                void navigator.clipboard?.writeText(order.orderCode);
                                onCopyNotice(`Đã sao chép mã đơn ${order.orderCode}`);
                              }}
                              style={{ padding: '4px 6px', borderRadius: 4, border: '1px solid #cbd5e1', background: '#f8fafc', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
                            >
                              <Copy size={13} strokeWidth={2} aria-hidden="true" />
                            </button>
                          </div>
                          <div className="ui-muted-text" style={{ fontSize: 11.5 }}>Mã gốc: {order.externalOrderId}</div>
                        </td>
                        <td className="cell-nowrap">
                          <UiBadge tone={tone(order.status)}>{orderLabels[order.status] ?? order.status}</UiBadge>
                        </td>
                        <td style={{ minWidth: 130, maxWidth: 170, whiteSpace: 'normal', lineHeight: 1.3 }}>
                          <div style={{ fontWeight: 600, color: '#0f172a' }}>{order.customerName}</div>
                          <div className="ui-muted-text" style={{ fontSize: 11.5 }}>
                            {order.shippingMethod || 'Giao tiêu chuẩn'} • {order.customerCode}
                          </div>
                        </td>
                        <td style={{ whiteSpace: 'normal', maxWidth: 135, lineHeight: 1.3 }}>{formatWarehouseLabel(order.warehouseName, undefined, order.warehouseId)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600, whiteSpace: 'nowrap' }}>{order.orderedQuantity}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', maxWidth: 88, whiteSpace: 'nowrap' }}>{order.reservedQuantity} / {order.allocatedQuantity}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600, whiteSpace: 'nowrap' }}>{order.shippedQuantity}</td>
                        <td className="cell-nowrap ui-sticky-action" style={{ background: '#ffffff', width: 108, maxWidth: 136 }}>
                          <button type="button" className="ui-btn-secondary" style={{ whiteSpace: 'nowrap' }} onClick={() => void onSelectDetail(order.id)}>Chi tiết</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            </div>
          </>
        )}
      </UiCard>
    </>
  );
};
