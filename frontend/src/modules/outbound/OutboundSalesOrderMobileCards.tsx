import React, { useEffect, useState } from 'react';
import { MoreHorizontal } from 'lucide-react';
import { UiBadge } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { type SalesOrder, formatShippingMethodVi, formatShortOrderDateVi, orderLabels, tone } from './outboundDemandTypes';

const checkMobileOrTablet = (): boolean => {
  if (typeof window === 'undefined') return false;
  if (typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 768px)').matches) {
    return true;
  }
  return window.innerWidth > 0 && window.innerWidth <= 768;
};

export function useIsMobileViewport(): boolean {
  const [isMobile, setIsMobile] = useState<boolean>(checkMobileOrTablet);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => setIsMobile(checkMobileOrTablet());
    const mq = typeof window.matchMedia === 'function' ? window.matchMedia('(max-width: 768px)') : null;
    window.addEventListener('resize', syncViewport);
    mq?.addEventListener?.('change', syncViewport);
    return () => {
      window.removeEventListener('resize', syncViewport);
      mq?.removeEventListener?.('change', syncViewport);
    };
  }, []);

  return isMobile;
}

interface OutboundSalesOrderMobileCardsProps {
  orders: SalesOrder[];
  onSelectDetail: (id: number) => Promise<void>;
  onCopyNotice: (msg: string) => void;
}

export const OutboundSalesOrderMobileCards: React.FC<OutboundSalesOrderMobileCardsProps> = ({
  orders,
  onSelectDetail,
  onCopyNotice,
}) => (
  <div className="ui-mobile-card-list" data-testid="sales-orders-mobile-cards" style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 10 }}>
    {orders.map((order) => {
      const orderDateVi = formatShortOrderDateVi(order.createdAt);
      return (
        <article
          key={order.id}
          className="ui-mobile-card"
          data-testid={`sales-order-mobile-card-${order.id}`}
          style={{
            padding: 12,
            borderRadius: 10,
            border: '1px solid #cbd5e1',
            background: '#ffffff',
            display: 'flex',
            flexDirection: 'column',
            gap: 8,
            minWidth: 0,
            maxWidth: '100%',
            boxSizing: 'border-box',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
            <div style={{ minWidth: 0 }}>
              <span className="ui-code" style={{ fontSize: 14, fontWeight: 700, color: '#0f172a', whiteSpace: 'nowrap' }}>
                {order.orderCode}
              </span>
              <div style={{ fontSize: 11.5, color: '#475569', fontVariantNumeric: 'tabular-nums', display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '4px 10px' }}>
                {orderDateVi ? <span>{orderDateVi}</span> : null}
                <span>{order.externalOrderId}</span>
              </div>
            </div>
            <UiBadge tone={tone(order.status)}>{orderLabels[order.status] ?? order.status}</UiBadge>
          </div>

          <div style={{ fontSize: 12.5, color: '#475569', display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '6px 12px', fontVariantNumeric: 'tabular-nums' }}>
            <strong style={{ color: '#0f172a' }}>{order.customerName}</strong>
            <span>{formatShippingMethodVi(order.shippingMethod)}</span>
            <span>{formatWarehouseLabel(order.warehouseName, undefined, order.warehouseId)}</span>
          </div>

          <div style={{ fontSize: 12.5, color: '#475569', display: 'flex', justifyContent: 'space-between', gap: 8, fontVariantNumeric: 'tabular-nums' }}>
            <span>SL đặt: <strong style={{ color: '#0f172a' }}>{order.orderedQuantity}</strong></span>
            <span>Giữ / PB: <strong>{order.reservedQuantity}/{order.allocatedQuantity}</strong></span>
            <span>Đã xuất: <strong style={{ color: '#0f172a' }}>{order.shippedQuantity}</strong></span>
          </div>

          <div style={{ display: 'flex', flexDirection: 'row', flexWrap: 'nowrap', alignItems: 'center', gap: 8, width: '100%' }}>
            <button
              type="button"
              className="ui-btn-secondary"
              style={{ flex: '1 1 auto', minWidth: 0, minHeight: 44, whiteSpace: 'nowrap', fontWeight: 600 }}
              onClick={() => void onSelectDetail(order.id)}
            >
              Chi tiết đơn bán
            </button>
            <details className="ui-row-more-menu" style={{ position: 'relative', flex: '0 0 44px', width: 44 }}>
              <summary
                className="ui-more-summary"
                aria-label="Thao tác khác"
                style={{ width: 44, height: 44, minWidth: 44, minHeight: 44, padding: 0, display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}
              >
                <MoreHorizontal size={18} aria-hidden="true" />
              </summary>
              <div className="ui-row-more-popover">
                <button
                  type="button"
                  style={{ width: '100%', textAlign: 'left', justifyContent: 'flex-start', padding: '8px 10px', fontSize: 12.5, fontWeight: 600, borderRadius: 6, border: '1px solid transparent', background: '#ffffff', color: '#0f172a' }}
                  onClick={() => {
                    void navigator.clipboard?.writeText(order.orderCode);
                    onCopyNotice(`Đã sao chép mã đơn ${order.orderCode}`);
                  }}
                >
                  Sao chép mã đơn
                </button>
              </div>
            </details>
          </div>
        </article>
      );
    })}
  </div>
);
