import React, { useEffect, useState } from 'react';
import { Copy } from 'lucide-react';
import { UiBadge } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { type SalesOrder, orderLabels, tone } from './outboundDemandTypes';

export function useIsMobileViewport(): boolean {
  const [isMobile, setIsMobile] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth > 0 && window.innerWidth <= 640,
  );

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const handleResize = () => setIsMobile(window.innerWidth > 0 && window.innerWidth <= 640);
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
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
  <div className="ui-mobile-card-list" data-testid="sales-orders-mobile-cards" style={{ display: 'grid', gap: 10 }}>
    {orders.map((order) => (
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
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
            <strong
              style={{
                fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
                fontSize: 14,
                color: '#0f172a',
                whiteSpace: 'nowrap',
              }}
            >
              {order.orderCode}
            </strong>
            <button
              type="button"
              title="Sao chép mã đơn"
              aria-label={`Sao chép mã đơn ${order.orderCode}`}
              onClick={() => {
                void navigator.clipboard?.writeText(order.orderCode);
                onCopyNotice(`Đã sao chép mã đơn ${order.orderCode}`);
              }}
              style={{
                padding: '6px 10px',
                minHeight: 44,
                minWidth: 44,
                borderRadius: 6,
                border: '1px solid #cbd5e1',
                background: '#f8fafc',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <Copy size={14} strokeWidth={2} aria-hidden="true" />
            </button>
          </div>
          <UiBadge tone={tone(order.status)}>{orderLabels[order.status] ?? order.status}</UiBadge>
        </div>

        <div style={{ fontSize: 12.5, color: '#475569', display: 'flex', flexWrap: 'wrap', gap: 6, fontVariantNumeric: 'tabular-nums' }}>
          <strong style={{ color: '#0f172a' }}>{order.customerName}</strong>
          <span>•</span>
          <span>{order.shippingMethod || 'Giao tiêu chuẩn'}</span>
          <span>•</span>
          <span>{formatWarehouseLabel(order.warehouseName, undefined, order.warehouseId)}</span>
        </div>

        <div style={{ fontSize: 12.5, color: '#475569', display: 'flex', justifyContent: 'space-between', gap: 8, fontVariantNumeric: 'tabular-nums' }}>
          <span>SL đặt: <strong style={{ color: '#0f172a' }}>{order.orderedQuantity}</strong></span>
          <span>Giữ / Phân bổ: <strong>{order.reservedQuantity}/{order.allocatedQuantity}</strong></span>
          <span>Đã xuất: <strong style={{ color: '#0f172a' }}>{order.shippedQuantity}</strong></span>
        </div>

        <button
          type="button"
          className="ui-btn-secondary"
          style={{ minHeight: 44, width: '100%', fontWeight: 600 }}
          onClick={() => void onSelectDetail(order.id)}
        >
          Chi tiết đơn bán
        </button>
      </article>
    ))}
  </div>
);
