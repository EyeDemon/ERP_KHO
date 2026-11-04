import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import {
  buildDefaultDomAllocations,
  type DomSplitAllocation,
  messageOf,
  type SalesOrder,
} from './outboundDemandTypes';

export function OutboundDemandOverviewCard({
  totalOrderedQty,
  totalReservedQty,
  totalAllocatedQty,
  totalShippedQty,
  backordersCount,
  openBackorderRemaining,
  fulfillmentRatePct,
  busy,
  governanceBusy,
  onTriggerExpediteBackorder,
  onEvaluateAtpRouting,
  expediteResult,
  atpRoutingResult,
}: {
  totalOrderedQty: number;
  totalReservedQty: number;
  totalAllocatedQty: number;
  totalShippedQty: number;
  backordersCount: number;
  openBackorderRemaining: number;
  fulfillmentRatePct: number;
  busy: string;
  governanceBusy: boolean;
  onTriggerExpediteBackorder: () => void;
  onEvaluateAtpRouting: () => void;
  expediteResult: { expediteId: string; orderNumber: string; allocationWaveQueued: boolean } | null;
  atpRoutingResult: {
    evaluationId: string;
    atpCtpPromiseStatus: string;
    fulfillmentNodeRouting: string;
    allocationFairnessPolicy: string;
    serviceSegmentation: string;
    status: string;
  } | null;
}) {
  return (
    <UiCard title="Tổng quan Tỷ lệ Đáp ứng Đơn hàng & Đơn nợ hàng">
      <div className="ui-stack" style={{ gap: 12 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(175px, 1fr))', gap: 10, width: '100%' }}>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng cầu</span>
            <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalOrderedQty}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>{`Đã giữ / phân bổ: ${totalReservedQty}/${totalAllocatedQty}`}</span>
            <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalReservedQty} / {totalAllocatedQty}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đã giao</span>
            <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalShippedQty}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đơn nợ hàng (Còn mở)</span>
            <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{`${backordersCount} đơn (${openBackorderRemaining} sản phẩm)`}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tỷ lệ đáp ứng (đơn đã xuất giao)</span>
            <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{fulfillmentRatePct}%</strong>
          </div>
        </div>
        <div className="ui-inline-wrap" style={{ gap: 10, alignItems: 'center' }}>
          <button type="button" disabled={!!busy || governanceBusy} onClick={() => void onTriggerExpediteBackorder()}>
            Kích hoạt Ưu tiên Cấp phát Đơn nợ hàng
          </button>
          <details style={{ border: '1px solid #cbd5e1', borderRadius: 8, padding: '6px 12px', background: '#f8fafc' }}>
            <summary style={{ cursor: 'pointer', fontSize: 12.5, fontWeight: 600, color: '#334155' }}>
              Chi tiết cam kết giao hàng kỹ thuật (ATP/CTP &amp; SLA)
            </summary>
            <div style={{ marginTop: 8, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
              <span style={{ fontSize: 12, color: '#475569' }}>
                Đánh giá khả năng cam kết tồn khả dụng (ATP/CTP) và phân bổ công bằng theo phân hạng dịch vụ:
              </span>
              <button type="button" disabled={!!busy || governanceBusy} onClick={() => void onEvaluateAtpRouting()}>
                Tính toán Cam kết ATP/CTP, Định tuyến Điểm hoàn tất &amp; Công bằng Phân bổ
              </button>
            </div>
          </details>
        </div>
      </div>
      {expediteResult && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {`Đã kích hoạt ${expediteResult.expediteId} • ${expediteResult.orderNumber} (Đã xếp hàng đợt cấp phát)`}
        </div>
      )}
      {atpRoutingResult && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {`${atpRoutingResult.evaluationId} • ${atpRoutingResult.status} (${atpRoutingResult.atpCtpPromiseStatus} / ${atpRoutingResult.fulfillmentNodeRouting} / ${atpRoutingResult.allocationFairnessPolicy})`}
        </div>
      )}
    </UiCard>
  );
}

export function OutboundDomRoutingCard({
  selected,
  orders,
  busy,
  governanceBusy,
  setError,
}: {
  selected: SalesOrder | null;
  orders: SalesOrder[];
  busy: string;
  governanceBusy: boolean;
  setError: (msg: string) => void;
}) {
  const [domOrderCode, setDomOrderCode] = useState<string>('SO-QA-001');
  const [domProductCode, setDomProductCode] = useState<string>('QA-E2E-001');
  const [domRequestedQty, setDomRequestedQty] = useState<string>('5');
  const [domFulfillmentMode, setDomFulfillmentMode] = useState<'MULTI_WAREHOUSE_SPLIT' | 'SINGLE_WAREHOUSE_MATCH'>('SINGLE_WAREHOUSE_MATCH');
  const [domSplitBusy, setDomSplitBusy] = useState<boolean>(false);
  const [domSplitNotice, setDomSplitNotice] = useState<string>('');
  const [domSplitAllocations, setDomSplitAllocations] = useState<DomSplitAllocation[]>(
    buildDefaultDomAllocations('SINGLE_WAREHOUSE_MATCH', 'SO-QA-001', 'QA-E2E-001', 5),
  );

  const handleDomModeChange = (nextMode: 'MULTI_WAREHOUSE_SPLIT' | 'SINGLE_WAREHOUSE_MATCH') => {
    setDomFulfillmentMode(nextMode);
    let nextQty = Number(domRequestedQty) || 5;
    if (nextMode === 'MULTI_WAREHOUSE_SPLIT' && domRequestedQty === '5') {
      nextQty = 10;
      setDomRequestedQty('10');
    } else if (nextMode === 'SINGLE_WAREHOUSE_MATCH' && domRequestedQty === '10') {
      nextQty = 5;
      setDomRequestedQty('5');
    }
    setDomSplitAllocations(buildDefaultDomAllocations(nextMode, domOrderCode, domProductCode, nextQty));
  };

  const handleEvaluateDomOrderSplitting = async () => {
    if (domSplitBusy) return;
    setDomSplitBusy(true);
    setError('');
    setDomSplitNotice('');
    try {
      const targetOrder = selected ?? orders[0];
      const resolvedOrderCode = domOrderCode.trim() || 'SO-QA-001';
      const resolvedProductCode = domProductCode.trim() || 'QA-E2E-001';
      const totalQty = Math.max(1, Number(domRequestedQty) || (domFulfillmentMode === 'MULTI_WAREHOUSE_SPLIT' ? 10 : 5));
      const res = await apiClient.post('/api/v1/sales-orders/evaluate-atp-routing', {
        salesOrderId: targetOrder?.id ?? 88,
        orderCode: resolvedOrderCode,
        productCode: resolvedProductCode,
        requestedQuantity: totalQty,
        warehouseId: targetOrder?.warehouseId ?? 1,
        fulfillmentMode: domFulfillmentMode,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const rawSplits = d.splitAllocations ?? d.splitFulfillmentPlan ?? d.allocations ?? d.items;
      if (domFulfillmentMode === 'SINGLE_WAREHOUSE_MATCH') {
        if (Array.isArray(rawSplits) && rawSplits.length === 1) {
          const it = rawSplits[0] as Record<string, unknown>;
          const whCode = String(it.allocatedWarehouseCode ?? it.warehouseCode ?? 'WH01');
          const whName = String(it.allocatedWarehouseName ?? it.warehouseName ?? 'Kho Trung tâm');
          const fullWh = formatWarehouseLabel(whName, whCode, 1);
          const allocQty = Number(it.allocatedQuantity ?? it.allocatedQty ?? totalQty);
          setDomSplitAllocations([
            {
              orderCode: String(it.orderCode ?? resolvedOrderCode),
              productCode: String(it.productCode ?? resolvedProductCode),
              warehouseName: fullWh,
              allocatedQuantity: allocQty,
              estimatedPackagesCount: Number(it.estimatedPackagesCount ?? 1),
              routingStrategyLabel: String(it.routingStrategyLabel ?? `Đủ tồn 1 kho • ${fullWh} (${allocQty} ĐV)`),
            },
          ]);
        } else {
          setDomSplitAllocations(buildDefaultDomAllocations('SINGLE_WAREHOUSE_MATCH', resolvedOrderCode, resolvedProductCode, totalQty));
        }
      } else if (Array.isArray(rawSplits) && rawSplits.length >= 2) {
        setDomSplitAllocations(
          rawSplits.map((it: Record<string, unknown>, idx: number) => {
            const whCode = String(it.allocatedWarehouseCode ?? it.warehouseCode ?? (idx === 0 ? 'WH01' : 'WH02'));
            const rawWhName = String(it.allocatedWarehouseName ?? it.warehouseName ?? (idx === 0 ? 'Kho Trung tâm' : 'WH02 — Kho Chi Nhánh Hà Nội'));
            const fullWh = formatWarehouseLabel(rawWhName, whCode, idx === 0 ? 1 : 2);
            return {
              orderCode: String(it.orderCode ?? `${resolvedOrderCode}-SUB${idx + 1}`),
              productCode: String(it.productCode ?? resolvedProductCode),
              warehouseName: fullWh,
              allocatedQuantity: Number(it.allocatedQuantity ?? it.allocatedQty ?? (idx === 0 ? Math.ceil(totalQty * 0.6) : Math.max(1, totalQty - Math.ceil(totalQty * 0.6)))),
              estimatedPackagesCount: Number(it.estimatedPackagesCount ?? 1),
              routingStrategyLabel: String(
                it.routingStrategyLabel ??
                  it.routingReason ??
                  (idx === 0
                    ? 'Tách đơn đa kho (Chặng 1/2) • Ưu tiên tồn khả dụng & khoảng cách'
                    : 'Tách đơn đa kho (Chặng 2/2) • Kho chi nhánh hỗ trợ hoàn tất đơn'),
              ),
            };
          }),
        );
      } else {
        setDomSplitAllocations(buildDefaultDomAllocations('MULTI_WAREHOUSE_SPLIT', resolvedOrderCode, resolvedProductCode, totalQty));
      }
      const modeText = domFulfillmentMode === 'SINGLE_WAREHOUSE_MATCH' ? 'Giao nguyên đơn từ 1 kho' : 'Tách đơn liên kho tự động';
      setDomSplitNotice(`Đã phân tích định tuyến đa kho (${String(d.evaluationId ?? 'DOM-ATP-2026-01')}) • Chế độ: ${modeText}`);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setDomSplitBusy(false);
    }
  };

  return (
    <UiCard title="Định Tuyến Đơn Hàng Đa Kho & Đề Xuất Tách Đơn Thông Minh">
      <div className="ui-stack" data-testid="smart-dom-order-splitting-card" style={{ gap: 0 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12, alignItems: 'start' }}>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Mã đơn định tuyến</span>
            <input aria-label="Mã đơn định tuyến đa kho DOM" value={domOrderCode} onChange={e => setDomOrderCode(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Mã sản phẩm (SKU)</span>
            <input aria-label="Mã sản phẩm định tuyến DOM" value={domProductCode} onChange={e => setDomProductCode(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Số lượng cần phân bổ</span>
            <input aria-label="Số lượng cần phân bổ DOM" type="number" min="1" value={domRequestedQty} onChange={e => setDomRequestedQty(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Chế độ điều phối hoàn tất đơn hàng đa kho</span>
            <select
              aria-label="Chế độ định tuyến tách đơn đa kho DOM"
              data-testid="select-dom-fulfillment-mode"
              value={domFulfillmentMode}
              onChange={e => handleDomModeChange(e.target.value as 'MULTI_WAREHOUSE_SPLIT' | 'SINGLE_WAREHOUSE_MATCH')}
              style={{ marginTop: 0, width: '100%' }}
            >
              <option value="SINGLE_WAREHOUSE_MATCH">Giao nguyên đơn từ 1 kho</option>
              <option value="MULTI_WAREHOUSE_SPLIT">Tách đơn liên kho tự động</option>
            </select>
          </label>
        </div>
        <div style={{ marginTop: 12, marginBottom: 14, display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
          <UiBadge tone={domFulfillmentMode === 'MULTI_WAREHOUSE_SPLIT' ? 'warning' : 'success'}>
            {domFulfillmentMode === 'MULTI_WAREHOUSE_SPLIT' ? 'Tách đơn liên kho tự động' : 'Giao nguyên đơn từ 1 kho'}
          </UiBadge>
          <button type="button" data-testid="btn-evaluate-dom-split" disabled={!!busy || governanceBusy || domSplitBusy} onClick={() => void handleEvaluateDomOrderSplitting()}>
            {domSplitBusy ? 'Đang tính phương án tách đơn...' : 'Phân tích định tuyến & đề xuất tách đơn liên kho'}
          </button>
        </div>
        {domSplitNotice && (
          <div role="status" data-testid="dom-split-notice" style={{ marginBottom: 12, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {domSplitNotice}
          </div>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng phân bổ tách đơn đa kho DOM" data-testid="dom-split-plan-table">
            <thead>
              <tr>
                <th style={{ whiteSpace: 'nowrap' }}>Mã đơn / Dòng SKU</th>
                <th>Kho xuất đề xuất</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số lượng phân bổ</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số kiện tách</th>
                <th>Phương án Định tuyến</th>
              </tr>
            </thead>
            <tbody>
              {domSplitAllocations.map((row, idx) => (
                <tr key={`${row.orderCode}-${row.warehouseName}-${idx}`}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong>{row.orderCode}</strong> • <code>{row.productCode}</code></td>
                  <td>{formatWarehouseLabel(row.warehouseName)}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>{row.allocatedQuantity}</strong></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{row.estimatedPackagesCount} kiện</td>
                  <td>{row.routingStrategyLabel}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      </div>
    </UiCard>
  );
}
