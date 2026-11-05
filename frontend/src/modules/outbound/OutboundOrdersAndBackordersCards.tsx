import React from 'react';
import { roundQty4 } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiEmptyState, UiTableScroll } from '../../ui/ProductionUi';
import {
  type Backorder,
  backorderLabels,
  formatShortOrderCode,
  orderLabels,
  qtyCellStyle,
  type SalesOrder,
  tone,
} from './outboundDemandTypes';

export function OutboundOrdersListCard({
  ordersCount,
  pagedOrders,
  orderPage,
  totalOrderPages,
  setOrderPage,
  onSelectDetail,
  onCopyNotice,
}: {
  ordersCount: number;
  pagedOrders: SalesOrder[];
  orderPage: number;
  totalOrderPages: number;
  setOrderPage: React.Dispatch<React.SetStateAction<number>>;
  onSelectDetail: (id: number) => void;
  onCopyNotice: (msg: string) => void;
}) {
  return (
    <UiCard title="Danh sách Đơn bán hàng">
      {ordersCount === 0 ? (
        <UiEmptyState title="Chưa có đơn bán hàng nào." detail="Tạo đơn bán hàng mới hoặc nhập từ tệp Excel để bắt đầu điều phối xuất kho." />
      ) : (
        <>
          <UiTableScroll>
            <table className="ui-table-compact" style={{ fontSize: 12.5, width: '100%' }} aria-label="Danh sách Sales Order demand">
              <thead>
                <tr>
                  <th>Mã đơn</th>
                  <th>Trạng thái</th>
                  <th>Khách hàng</th>
                  <th>Kho</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đặt hàng</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã giữ</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Phân bổ</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã lấy</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã giao</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Nợ hàng</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã hủy</th>
                  <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Còn mở</th>
                </tr>
              </thead>
              <tbody>
                {pagedOrders.map(order => (
                  <tr key={order.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <button
                          type="button"
                          title={`Mã đơn đầy đủ: ${order.orderCode} (Bấm Sao chép để lấy mã gốc)`}
                          style={{ whiteSpace: 'nowrap', fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace', fontWeight: 600 }}
                          onClick={() => void onSelectDetail(order.id)}
                        >
                          {formatShortOrderCode(order.orderCode)}
                        </button>
                        {order.orderCode && order.orderCode.length > 14 && (
                          <button
                            type="button"
                            title={`Sao chép mã gốc: ${order.orderCode}`}
                            style={{ minHeight: 26, padding: '2px 8px', fontSize: 11 }}
                            onClick={() => {
                              void navigator.clipboard?.writeText(order.orderCode);
                              onCopyNotice(`Đã sao chép mã đơn gốc: ${order.orderCode}`);
                            }}
                          >
                            Sao chép
                          </button>
                        )}
                      </div>
                      <div className="ui-muted-text" style={{ whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: 6, marginTop: 2 }} title={`Mã đơn đầy đủ: ${order.externalOrderId} (Bấm Sao chép để lấy mã gốc)`}>
                        <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace' }}>{formatShortOrderCode(order.externalOrderId)}</span>
                        {order.externalOrderId && order.externalOrderId.length > 14 && (
                          <button
                            type="button"
                            title={`Sao chép mã kênh bán: ${order.externalOrderId}`}
                            style={{ minHeight: 22, padding: '1px 6px', fontSize: 10.5 }}
                            onClick={() => {
                              void navigator.clipboard?.writeText(order.externalOrderId);
                              onCopyNotice(`Đã sao chép mã đơn kênh bán: ${order.externalOrderId}`);
                            }}
                          >
                            Sao chép mã gốc
                          </button>
                        )}
                      </div>
                    </td>
                    <td><UiBadge tone={tone(order.status)}>{orderLabels[order.status] ?? order.status}</UiBadge></td>
                    <td style={{ maxWidth: 185 }} title={`${order.customerCode} — ${order.customerName}`}>
                      <div style={{ fontWeight: 700, color: '#0f172a', whiteSpace: 'nowrap' }}>{order.customerCode}</div>
                      <div title={`${order.customerCode} — ${order.customerName}`} style={{ fontSize: 12.5, color: '#475569', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 185 }}>{order.customerName}</div>
                    </td>
                    <td>{order.warehouseName}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.orderedQuantity)}>{order.orderedQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.reservedQuantity)}>{order.reservedQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.allocatedQuantity)}>{order.allocatedQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.pickedQuantity)}>{order.pickedQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.shippedQuantity)}>{order.shippedQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.backorderQuantity)}>{order.backorderQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.cancelledQuantity)}>{order.cancelledQuantity}</td>
                    <td className="ui-cell-numeric" style={qtyCellStyle(order.openQuantity)}>{order.openQuantity}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
          <div className="ui-pagination" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, marginTop: 10 }}>
            <span style={{ fontSize: 12.5, color: '#475569' }}>Tổng số: <strong>{ordersCount}</strong> đơn bán hàng • Trang {orderPage}/{totalOrderPages}</span>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <button type="button" disabled={orderPage <= 1} onClick={() => setOrderPage(p => Math.max(1, p - 1))}>Trước</button>
              <span style={{ fontSize: 12.5, fontVariantNumeric: 'tabular-nums' }}>Trang {orderPage}/{totalOrderPages}</span>
              <button type="button" disabled={orderPage >= totalOrderPages} onClick={() => setOrderPage(p => Math.min(totalOrderPages, p + 1))}>Sau</button>
            </div>
          </div>
        </>
      )}
    </UiCard>
  );
}

export function OutboundSelectedOrderCard({
  selected,
  canHold,
  canRelease,
  canCancel,
  busy,
  orderCancelReason,
  setOrderCancelReason,
  onMutate,
}: {
  selected: SalesOrder;
  canHold: boolean;
  canRelease: boolean;
  canCancel: boolean;
  busy: string;
  orderCancelReason: string;
  setOrderCancelReason: (v: string) => void;
  onMutate: (key: string, path: string, body: object, success: string) => void;
}) {
  return (
    <UiCard title={`Chi tiết ${selected.orderCode}`}>
      <div className="ui-inline-wrap">
        <UiBadge tone={tone(selected.status)}>{orderLabels[selected.status] ?? selected.status}</UiBadge>
        <span>{selected.externalOrderId} • {selected.customerName} • {selected.warehouseName}</span>
        <span>Đặt hàng: {selected.orderedQuantity} • Đã giao: {selected.shippedQuantity} • Còn mở: {selected.openQuantity} • Đã hủy: {selected.cancelledQuantity}</span>
        {Number(selected.totalAmount || 0) > 0 && <span>Tổng giá trị: <strong>{Number(selected.totalAmount).toLocaleString('vi-VN')} {!selected.currency || selected.currency === 'VND' ? '₫' : selected.currency}</strong></span>}
      </div>
      <p className="ui-muted-text">Phát hành đơn theo tồn khả dụng, tự động tạo giữ hàng, phân bổ cho phần đủ tồn và tạo đơn nợ hàng (Backorder) cho phần thiếu.</p>
      <div className="ui-inline-wrap">
        {canHold && selected.status === 'Draft' && (
          <button
            type="button"
            disabled={!!busy}
            onClick={() => void onMutate(`sales-order-hold-${selected.id}`, `/api/v1/sales-orders/${selected.id}/hold`, { rowVersion: selected.rowVersion }, 'Đã đưa Sales Order vào HOLD.')}
          >
            Hold
          </button>
        )}
        {canRelease && ['Draft', 'Hold'].includes(selected.status) && (
          <button
            type="button"
            disabled={!!busy}
            onClick={() => void onMutate(`sales-order-release-${selected.id}`, `/api/v1/sales-orders/${selected.id}/release`, { rowVersion: selected.rowVersion }, 'Đã release Sales Order.')}
          >
            Release demand
          </button>
        )}
        {canCancel && !['Cancelled', 'Fulfilled'].includes(selected.status) && (
          <>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Lý do hủy
              <input aria-label="Lý do hủy Sales Order" value={orderCancelReason} onChange={e => setOrderCancelReason(e.target.value)} />
            </label>
            <button
              type="button"
              disabled={!!busy || !orderCancelReason.trim()}
              onClick={() => void onMutate(`sales-order-cancel-${selected.id}`, `/api/v1/sales-orders/${selected.id}/cancel`, { rowVersion: selected.rowVersion, reason: orderCancelReason }, 'Đã hủy phần demand chưa shipped.')}
            >
              Hủy phần chưa giao
            </button>
          </>
        )}
      </div>
      <UiTableScroll>
        <table aria-label="Dòng Sales Order">
          <thead>
            <tr>
              <th>Sản phẩm</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đặt hàng</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã giữ</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Phân bổ</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã lấy</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã giao</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Nợ hàng</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã hủy</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Còn mở</th>
              <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đơn giá / Thuế</th>
            </tr>
          </thead>
          <tbody>
            {(selected.lines ?? []).map(line => (
              <tr key={line.id}>
                <td>
                  <strong style={{ whiteSpace: 'nowrap' }}>{line.productCode}</strong> — {line.productName}
                  <div className="ui-muted-text">{(line.uomCode === 'QA_UNIT' || line.uomCode === 'QA-PCS') ? 'THÙNG' : line.uomCode}</div>
                </td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.orderedQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.reservedQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.allocatedQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.pickedQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.shippedQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.backorderQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.cancelledQuantity}</td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}><strong>{line.openQuantity}</strong></td>
                <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{Number(line.unitPrice || 0).toLocaleString('vi-VN')} ({Number(line.taxPercent || 0)}%)</td>
              </tr>
            ))}
          </tbody>
        </table>
      </UiTableScroll>
    </UiCard>
  );
}

export function OutboundBackordersCard({
  backorders,
  canManageBackorders,
  busy,
  recoverQty,
  setRecoverQty,
  backorderCancelQty,
  setBackorderCancelQty,
  backorderCancelReason,
  setBackorderCancelReason,
  onMutate,
}: {
  backorders: Backorder[];
  canManageBackorders: boolean;
  busy: string;
  recoverQty: Record<number, string>;
  setRecoverQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelQty: Record<number, string>;
  setBackorderCancelQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelReason: Record<number, string>;
  setBackorderCancelReason: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  onMutate: (key: string, path: string, body: object, success: string) => void;
}) {
  return (
    <UiCard title="Danh sách Đơn nợ hàng">
      {backorders.length === 0 ? (
        <UiEmptyState title="Không có đơn nợ hàng nào đang mở" detail="Toàn bộ đơn bán hàng hiện tại đã được đáp ứng đủ tồn kho." />
      ) : (
        <div className="ui-stack">
          {backorders.map(item => (
            <div className="ui-subcard" key={item.id}>
              <div className="ui-subcard-title" style={{ whiteSpace: 'nowrap' }}>{item.backorderCode} — {item.productCode}</div>
              <div className="ui-inline-wrap">
                <UiBadge tone={tone(item.status)}>{backorderLabels[item.status] ?? item.status}</UiBadge>
                <span style={{ whiteSpace: 'nowrap' }}>{item.orderCode} / {item.externalOrderId}</span>
                <span>Thiếu ban đầu <strong>{item.quantity}</strong></span>
                <span>Đã khôi phục {item.recoveredQuantity}</span>
                <span>Đã hủy {item.cancelledQuantity}</span>
                <span>Còn <strong>{item.remainingQuantity}</strong></span>
              </div>
              {canManageBackorders && ['Open', 'PartiallyAllocated'].includes(item.status) && item.remainingQuantity > 0 && (
                <div className="ui-inline-wrap">
                  <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Số lượng khôi phục
                    <input
                      aria-label={`Recover quantity ${item.backorderCode}`}
                      type="number"
                      min="0.0001"
                      step="0.0001"
                      value={recoverQty[item.id] ?? ''}
                      onChange={e => setRecoverQty(v => ({ ...v, [item.id]: e.target.value }))}
                      placeholder={String(item.remainingQuantity)}
                    />
                  </label>
                  <button
                    type="button"
                    disabled={!!busy || (Boolean(recoverQty[item.id]) && !(roundQty4(Number(recoverQty[item.id])) > 0))}
                    onClick={() => void onMutate(
                      `backorder-reallocate-${item.id}`,
                      `/api/v1/backorders/${item.id}/reallocate`,
                      { quantity: recoverQty[item.id] ? roundQty4(Number(recoverQty[item.id])) : undefined, rowVersion: item.rowVersion },
                      'Đã thử recover Backorder theo tồn Available hiện tại.',
                    )}
                  >
                    Reallocate
                  </button>
                  <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Số lượng hủy
                    <input
                      aria-label={`Cancel backorder quantity ${item.backorderCode}`}
                      type="number"
                      min="0.0001"
                      step="0.0001"
                      value={backorderCancelQty[item.id] ?? ''}
                      onChange={e => setBackorderCancelQty(v => ({ ...v, [item.id]: e.target.value }))}
                      placeholder={String(item.remainingQuantity)}
                    />
                  </label>
                  <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Lý do
                    <input
                      aria-label={`Cancel backorder reason ${item.backorderCode}`}
                      value={backorderCancelReason[item.id] ?? ''}
                      onChange={e => setBackorderCancelReason(v => ({ ...v, [item.id]: e.target.value }))}
                    />
                  </label>
                  <button
                    type="button"
                    disabled={!!busy || !(backorderCancelReason[item.id] ?? '').trim() || (Boolean(backorderCancelQty[item.id]) && !(roundQty4(Number(backorderCancelQty[item.id])) > 0))}
                    onClick={() => void onMutate(
                      `backorder-cancel-${item.id}`,
                      `/api/v1/backorders/${item.id}/cancel`,
                      { quantity: backorderCancelQty[item.id] ? roundQty4(Number(backorderCancelQty[item.id])) : undefined, reason: backorderCancelReason[item.id], rowVersion: item.rowVersion },
                      'Đã hủy phần Backorder chưa recover.',
                    )}
                  >
                    Hủy Backorder
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </UiCard>
  );
}
