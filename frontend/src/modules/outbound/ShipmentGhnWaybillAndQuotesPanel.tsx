import { useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { idempotencyHeaders } from '../../services/idempotency';
import type { Shipment } from './shipmentsTypes';
import { messageFor } from './shipmentsTypes';

export interface GhnOrderDetailState {
  orderCode: string;
  ghnStatus: string;
  leadtime: string;
  codAmount: number;
  totalFee: number;
  logHistoryCount: number;
  shopId: string;
  gatewayMode: string;
}

export interface CarrierRateQuote {
  carrierCode: string;
  carrierName: string;
  totalFeeVnd: number;
  estimatedDeliveryHours: number;
}

export const DEFAULT_CARRIER_QUOTES: CarrierRateQuote[] = [
  { carrierCode: 'GHN', carrierName: 'GHN (Giao Hàng Nhanh - Live Sandbox)', totalFeeVnd: 0, estimatedDeliveryHours: 24 },
  { carrierCode: 'GHTK', carrierName: 'GHTK (Giao Hàng Tiết Kiệm)', totalFeeVnd: 0, estimatedDeliveryHours: 36 },
  { carrierCode: 'VIETTELPOST', carrierName: 'Viettel Post', totalFeeVnd: 0, estimatedDeliveryHours: 36 },
  { carrierCode: 'JT_EXPRESS', carrierName: 'J&T Express', totalFeeVnd: 0, estimatedDeliveryHours: 48 },
];

export const formatGhnError = (e: unknown) => {
  const base = messageFor(e);
  const resp = (e as { response?: { headers?: Record<string, string>; data?: Record<string, unknown> } })?.response;
  const code = String(resp?.data?.errorCode ?? resp?.data?.code ?? '');
  const corr = String(resp?.headers?.['x-correlation-id'] ?? resp?.data?.correlationId ?? '');
  return `${base}${code && !base.includes(code) ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

interface ShipmentGhnWaybillAndQuotesPanelProps {
  rows: Shipment[];
  defaultShipmentId?: number | string;
  estimatedFeeVnd: number;
  shopId: string;
  gatewayMode: string;
  carrierProvider: 'GHN' | 'GHTK' | 'VIETTELPOST' | 'JT_EXPRESS';
  carrierQuotes: CarrierRateQuote[];
  syncBanner: string;
  feeBanner: string;
  setError: (msg: string) => void;
}

export function ShipmentGhnWaybillAndQuotesPanel({
  rows,
  defaultShipmentId,
  estimatedFeeVnd,
  shopId,
  gatewayMode,
  carrierProvider,
  carrierQuotes,
  syncBanner,
  feeBanner,
  setError,
}: ShipmentGhnWaybillAndQuotesPanelProps) {
  const [shipmentIdInput, setShipmentIdInput] = useState<string>(() => (defaultShipmentId ? String(defaultShipmentId) : ''));
  const [ghnOrderCode, setGhnOrderCode] = useState<string>('');
  const [cancelReason, setCancelReason] = useState<string>('');
  const [detailBusy, setDetailBusy] = useState(false);
  const [cancelBusy, setCancelBusy] = useState(false);
  const [orderDetail, setOrderDetail] = useState<GhnOrderDetailState | null>(null);

  useEffect(() => {
    if (rows.length > 0) {
      setShipmentIdInput((prev) => prev || String(rows[0].id));
    }
  }, [rows]);

  const applyDetailPayload = (d: Record<string, unknown>, fallbackStatus: string) => {
    setOrderDetail({
      orderCode: String(d.orderCode ?? d.ghnOrderCode ?? (ghnOrderCode.trim() || 'GHN-ORDER')),
      ghnStatus: String(d.ghnStatus ?? d.status ?? fallbackStatus),
      leadtime: String(d.leadtime ?? d.expectedDeliveryTime ?? '—'),
      codAmount: Number(d.codAmount ?? 0),
      totalFee: Number(d.totalFee ?? d.calculatedFeeVnd ?? estimatedFeeVnd ?? 0),
      logHistoryCount: Number(d.logHistoryCount ?? (Array.isArray(d.logHistory) ? d.logHistory.length : 0)),
      shopId: String(d.shopId ?? shopId ?? '235512'),
      gatewayMode: String(d.gatewayMode ?? gatewayMode ?? 'LIVE_GHN_DEV_SANDBOX'),
    });
  };

  const handleSyncDetail = async () => {
    if (detailBusy) return;
    setDetailBusy(true);
    setError('');
    try {
      const shipmentId = shipmentIdInput.trim() || String(rows[0]?.id ?? 1);
      const res = await apiClient.post(
        `/api/v1/shipments/${shipmentId}/ghn/sync-detail`,
        { orderCode: ghnOrderCode.trim() || undefined },
        { headers: idempotencyHeaders('ghn-sync-detail:' + Date.now()) },
      );
      applyDetailPayload((res?.data ?? {}) as Record<string, unknown>, 'ready_to_pick');
    } catch (e) {
      setError(formatGhnError(e));
    } finally {
      setDetailBusy(false);
    }
  };

  const handleCancelWaybill = async () => {
    if (cancelBusy) return;
    setCancelBusy(true);
    setError('');
    try {
      const shipmentId = shipmentIdInput.trim() || String(rows[0]?.id ?? 1);
      const res = await apiClient.post(
        `/api/v1/shipments/${shipmentId}/ghn/cancel-waybill`,
        { orderCode: ghnOrderCode.trim() || undefined, reason: cancelReason.trim() || 'Hủy vận đơn theo yêu cầu điều phối kho' },
        { headers: idempotencyHeaders('ghn-cancel-waybill:' + Date.now()) },
      );
      applyDetailPayload((res?.data ?? {}) as Record<string, unknown>, 'cancel');
    } catch (e) {
      setError(formatGhnError(e));
    } finally {
      setCancelBusy(false);
    }
  };

  return (
    <>
      <div data-testid="carrier-rate-shopping-grid" style={{ marginBottom: 10, padding: '8px 10px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#f8fafc' }}>
        <div style={{ fontSize: 12.5, fontWeight: 700, color: '#0f172a', marginBottom: 6 }}>
          So sánh cước Đa hãng (Multi-Carrier Rate Shopping)
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 8 }}>
          {carrierQuotes.map((q) => (
            <div key={q.carrierCode} style={{ padding: '6px 10px', borderRadius: 6, border: q.carrierCode === carrierProvider ? '1.5px solid #1d4ed8' : '1px solid #e2e8f0', background: '#ffffff', fontSize: 12, fontVariantNumeric: 'tabular-nums' }}>
              <div style={{ fontWeight: 700, color: '#0f172a' }}>{q.carrierCode} — {q.carrierName}</div>
              <div style={{ color: '#047857', fontWeight: 700 }}>{q.totalFeeVnd.toLocaleString('vi-VN')} ₫ • SLA: {q.estimatedDeliveryHours}h</div>
            </div>
          ))}
        </div>
      </div>

      <div className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10, marginBottom: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0' }}>
        <label className="ui-stack" style={{ minWidth: 130 }}>
          <span>Mã chuyến giao (Shipment ID)</span>
          <input aria-label="Mã chuyến giao GHN" data-testid="input-ghn-shipment-id" value={shipmentIdInput} placeholder="VD: 1" onChange={(e) => setShipmentIdInput(e.target.value)} />
        </label>
        <label className="ui-stack" style={{ minWidth: 170 }}>
          <span>Mã vận đơn GHN (Order Code)</span>
          <input aria-label="Mã vận đơn GHN" data-testid="input-ghn-order-code" value={ghnOrderCode} placeholder="VD: L849201" onChange={(e) => setGhnOrderCode(e.target.value)} />
        </label>
        <label className="ui-stack" style={{ minWidth: 190 }}>
          <span>Lý do hủy vận đơn (nếu hủy)</span>
          <input aria-label="Lý do hủy vận đơn GHN" value={cancelReason} placeholder="Lý do hủy vận đơn GHN" onChange={(e) => setCancelReason(e.target.value)} />
        </label>
        <button type="button" className="ui-btn-secondary" data-testid="btn-ghn-sync-detail" disabled={detailBusy} onClick={() => void handleSyncDetail()}>
          {detailBusy ? 'Đang đồng bộ...' : 'Đồng bộ Chi tiết Vận đơn GHN (/v2/shipping-order/detail)'}
        </button>
        <button type="button" className="ui-btn-secondary" data-testid="btn-ghn-cancel-waybill" disabled={cancelBusy} onClick={() => void handleCancelWaybill()}>
          {cancelBusy ? 'Đang hủy...' : 'Hủy Vận đơn GHN (/v2/switch-status/cancel)'}
        </button>
      </div>

      {syncBanner && (
        <div role="status" data-testid="ghn-sync-status-banner" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
          {syncBanner}
        </div>
      )}
      {feeBanner && (
        <div role="status" data-testid="ghn-fee-result-banner" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#eff6ff', color: '#1e40af', border: '1px solid #bfdbfe', fontSize: 13, fontVariantNumeric: 'tabular-nums' }}>
          {feeBanner}
        </div>
      )}
      {orderDetail && (
        <div role="status" data-testid="ghn-order-detail-banner" style={{ marginTop: 8, padding: '10px 12px', borderRadius: 8, background: '#f0fdf4', color: '#065f46', border: '1px solid #86efac', fontSize: 13, fontVariantNumeric: 'tabular-nums' }}>
          <strong>Chi tiết Vận đơn GHN:</strong> Mã vận đơn: <code>{orderDetail.orderCode}</code> • Trạng thái GHN: <strong>{orderDetail.ghnStatus}</strong> • Dự kiến giao (Leadtime): <strong>{orderDetail.leadtime}</strong> • Thu hộ COD: <strong>{orderDetail.codAmount.toLocaleString('vi-VN')} ₫</strong> • Tổng cước: <strong>{orderDetail.totalFee.toLocaleString('vi-VN')} ₫</strong> • Lịch sử trạng thái: <strong>{orderDetail.logHistoryCount}</strong> mốc • ShopId: <code>{orderDetail.shopId}</code> • Cổng: <code>{orderDetail.gatewayMode}</code>
        </div>
      )}
    </>
  );
}
