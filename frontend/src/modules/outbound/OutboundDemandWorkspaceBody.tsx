import React, { useState } from 'react';
import apiClient from '../../services/apiClient';
import { idempotencyHeaders } from '../../services/idempotency';
import {
  type Backorder,
  type CreateLine,
  messageOf,
  type Partner,
  type Product,
  type SalesOrder,
  translateOutboundSignal,
  type Warehouse,
} from './outboundDemandTypes';
import { OutboundDemandOverviewCard, OutboundDomRoutingCard } from './OutboundDemandSections';
import { OutboundCreateOrderCard, OutboundMarketplaceAddressCard } from './OutboundCreateAndExcelCards';
import { OutboundBackordersCard, OutboundOrdersListCard, OutboundSelectedOrderCard } from './OutboundOrdersAndBackordersCards';

export interface AtpRoutingResultState {
  evaluationId: string;
  atpCtpPromiseStatus: string;
  fulfillmentNodeRouting: string;
  allocationFairnessPolicy: string;
  serviceSegmentation: string;
  status: string;
}

export interface ExpediteBackorderResultState {
  expediteId: string;
  orderNumber: string;
  allocationWaveQueued: boolean;
}

export function useOutboundGovernanceActions({
  orders,
  backorders,
  setError,
}: {
  orders: SalesOrder[];
  backorders: Backorder[];
  setError: (msg: string) => void;
}) {
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [expediteResult, setExpediteResult] = useState<ExpediteBackorderResultState | null>(null);
  const [atpRoutingResult, setAtpRoutingResult] = useState<AtpRoutingResultState | null>(null);

  const triggerExpediteBackorder = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/sales-orders/expedite-backorder',
        {
          warehouseId: orders[0]?.warehouseId ?? undefined,
          orderNumber: backorders[0]?.orderCode ?? orders[0]?.orderCode ?? 'SO-PRIORITY-QUEUE',
        },
        { headers: idempotencyHeaders('so-expedite-backorder:' + Date.now()) },
      );
      if (res?.data?.expediteId) {
        setExpediteResult({
          expediteId: String(res.data.expediteId),
          orderNumber: translateOutboundSignal(String(res.data.orderNumber ?? backorders[0]?.orderCode ?? orders[0]?.orderCode ?? 'SO-PRIORITY-QUEUE')),
          allocationWaveQueued: Boolean(res.data.allocationWaveQueued ?? true),
        });
      } else {
        setExpediteResult(null);
        setError('Phản hồi kích hoạt ưu tiên cấp phát Backorder không hợp lệ từ máy chủ.');
      }
    } catch (e) {
      setExpediteResult(null);
      setError(messageOf(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const evaluateAtpRouting = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/sales-orders/evaluate-atp-routing', {
        warehouseId: orders[0]?.warehouseId ?? undefined,
      });
      if (res?.data?.evaluationId && res?.data?.status) {
        setAtpRoutingResult({
          evaluationId: String(res.data.evaluationId),
          atpCtpPromiseStatus: translateOutboundSignal(String(res.data.atpCtpPromiseStatus || '')),
          fulfillmentNodeRouting: translateOutboundSignal(String(res.data.fulfillmentNodeRouting || '')),
          allocationFairnessPolicy: translateOutboundSignal(String(res.data.allocationFairnessPolicy || '')),
          serviceSegmentation: translateOutboundSignal(String(res.data.serviceSegmentation || '')),
          status: translateOutboundSignal(String(res.data.status)),
        });
      } else {
        setAtpRoutingResult(null);
        setError('Phản hồi tính toán ATP/CTP và định tuyến kho không hợp lệ từ máy chủ.');
      }
    } catch (e) {
      setAtpRoutingResult(null);
      setError(messageOf(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  return {
    governanceBusy,
    expediteResult,
    atpRoutingResult,
    triggerExpediteBackorder,
    evaluateAtpRouting,
  };
}

export function OutboundMultiTabWorkspaceBody({
  activeSectionTab,
  orders,
  pagedOrders,
  orderPage,
  totalOrderPages,
  setOrderPage,
  backorders,
  selected,
  totalOrderedQty,
  totalReservedQty,
  totalAllocatedQty,
  totalShippedQty,
  openBackorderRemaining,
  fulfillmentRatePct,
  canReadOrders,
  canCreate,
  masterReady,
  canHold,
  canRelease,
  canCancel,
  canReadBackorders,
  canManageBackorders,
  showCreate,
  setShowCreate,
  busy,
  governanceBusy,
  triggerExpediteBackorder,
  evaluateAtpRouting,
  expediteResult,
  atpRoutingResult,
  externalOrderId,
  setExternalOrderId,
  customerId,
  setCustomerId,
  warehouseId,
  setWarehouseId,
  requestedShipDate,
  setRequestedShipDate,
  priority,
  setPriority,
  shippingMethod,
  setShippingMethod,
  partners,
  warehouses,
  products,
  createLines,
  setCreateLines,
  createOrder,
  detail,
  setError,
  setNotice,
  load,
  orderCancelReason,
  setOrderCancelReason,
  recoverQty,
  setRecoverQty,
  backorderCancelQty,
  setBackorderCancelQty,
  backorderCancelReason,
  setBackorderCancelReason,
  mutate,
}: {
  activeSectionTab: 'ALL' | 'ORDERS' | 'CREATE_EXCEL' | 'DOM_ROUTING' | 'BACKORDERS';
  orders: SalesOrder[];
  pagedOrders: SalesOrder[];
  orderPage: number;
  totalOrderPages: number;
  setOrderPage: React.Dispatch<React.SetStateAction<number>>;
  backorders: Backorder[];
  selected: SalesOrder | null;
  totalOrderedQty: number;
  totalReservedQty: number;
  totalAllocatedQty: number;
  totalShippedQty: number;
  openBackorderRemaining: number;
  fulfillmentRatePct: number;
  canReadOrders: boolean;
  canCreate: boolean;
  masterReady: boolean;
  canHold: boolean;
  canRelease: boolean;
  canCancel: boolean;
  canReadBackorders: boolean;
  canManageBackorders: boolean;
  showCreate: boolean;
  setShowCreate: (val: boolean) => void;
  busy: string;
  governanceBusy: boolean;
  triggerExpediteBackorder: () => Promise<void>;
  evaluateAtpRouting: () => Promise<void>;
  expediteResult: ExpediteBackorderResultState | null;
  atpRoutingResult: AtpRoutingResultState | null;
  externalOrderId: string;
  setExternalOrderId: (val: string) => void;
  customerId: string;
  setCustomerId: (val: string) => void;
  warehouseId: string;
  setWarehouseId: (val: string) => void;
  requestedShipDate: string;
  setRequestedShipDate: (val: string) => void;
  priority: string;
  setPriority: (val: string) => void;
  shippingMethod: string;
  setShippingMethod: (val: string) => void;
  partners: Partner[];
  warehouses: Warehouse[];
  products: Product[];
  createLines: CreateLine[];
  setCreateLines: React.Dispatch<React.SetStateAction<CreateLine[]>>;
  createOrder: (event: React.FormEvent) => Promise<void>;
  detail: (id: number) => Promise<void>;
  setError: (msg: string) => void;
  setNotice: (msg: string) => void;
  load: () => Promise<void>;
  orderCancelReason: string;
  setOrderCancelReason: (val: string) => void;
  recoverQty: Record<number, string>;
  setRecoverQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelQty: Record<number, string>;
  setBackorderCancelQty: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  backorderCancelReason: Record<number, string>;
  setBackorderCancelReason: React.Dispatch<React.SetStateAction<Record<number, string>>>;
  mutate: (key: string, path: string, body: object, success: string) => Promise<unknown>;
}) {
  return (
    <>
      <OutboundDemandOverviewCard
        totalOrderedQty={totalOrderedQty} totalReservedQty={totalReservedQty} totalAllocatedQty={totalAllocatedQty}
        totalShippedQty={totalShippedQty} backordersCount={backorders.length} openBackorderRemaining={openBackorderRemaining}
        fulfillmentRatePct={fulfillmentRatePct} busy={busy} governanceBusy={governanceBusy}
        onTriggerExpediteBackorder={triggerExpediteBackorder} onEvaluateAtpRouting={evaluateAtpRouting}
        expediteResult={expediteResult} atpRoutingResult={atpRoutingResult}
      />
      {(activeSectionTab === 'ALL' || activeSectionTab === 'DOM_ROUTING') && (
        <OutboundDomRoutingCard selected={selected} orders={orders} busy={busy} governanceBusy={governanceBusy} setError={setError} />
      )}
      {(activeSectionTab === 'ALL' || activeSectionTab === 'CREATE_EXCEL') && (
        <OutboundMarketplaceAddressCard orders={orders} warehouseId={warehouseId} busy={busy} setError={setError} setNotice={setNotice} onReload={load} />
      )}
      {(showCreate || activeSectionTab === 'CREATE_EXCEL') && canCreate && masterReady && (
        <OutboundCreateOrderCard
          externalOrderId={externalOrderId} setExternalOrderId={setExternalOrderId} customerId={customerId} setCustomerId={setCustomerId}
          warehouseId={warehouseId} setWarehouseId={setWarehouseId} requestedShipDate={requestedShipDate} setRequestedShipDate={setRequestedShipDate}
          priority={priority} setPriority={setPriority} shippingMethod={shippingMethod} setShippingMethod={setShippingMethod}
          partners={partners} warehouses={warehouses} products={products} createLines={createLines} setCreateLines={setCreateLines}
          busy={busy} onCreateOrder={createOrder} onClose={() => setShowCreate(false)}
        />
      )}
      {(activeSectionTab === 'ALL' || activeSectionTab === 'ORDERS') && canReadOrders && (
        <OutboundOrdersListCard
          ordersCount={orders.length} pagedOrders={pagedOrders} orderPage={orderPage}
          totalOrderPages={totalOrderPages} setOrderPage={setOrderPage} onSelectDetail={detail} onCopyNotice={setNotice}
        />
      )}
      {selected && (
        <OutboundSelectedOrderCard
          selected={selected} canHold={canHold} canRelease={canRelease} canCancel={canCancel} busy={busy}
          orderCancelReason={orderCancelReason} setOrderCancelReason={setOrderCancelReason} onMutate={mutate}
        />
      )}
      {(activeSectionTab === 'ALL' || activeSectionTab === 'BACKORDERS') && canReadBackorders && (
        <OutboundBackordersCard
          backorders={backorders} canManageBackorders={canManageBackorders} busy={busy}
          recoverQty={recoverQty} setRecoverQty={setRecoverQty} backorderCancelQty={backorderCancelQty}
          setBackorderCancelQty={setBackorderCancelQty} backorderCancelReason={backorderCancelReason}
          setBackorderCancelReason={setBackorderCancelReason} onMutate={mutate}
        />
      )}
    </>
  );
}
