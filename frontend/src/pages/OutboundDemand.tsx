import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { OutboundPipelineStepper } from '../components/common/OutboundPipelineStepper';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { UiPage, UiPageHeader, UiTabs } from '../ui/ProductionUi';
import {
  type Backorder,
  type CreateLine,
  emptyLine,
  messageOf,
  type Partner,
  type Product,
  type SalesOrder,
  translateOutboundSignal,
  type Warehouse,
} from '../modules/outbound/outboundDemandTypes';
import {
  OutboundDemandOverviewCard,
  OutboundDomRoutingCard,
} from '../modules/outbound/OutboundDemandSections';
import {
  OutboundCreateOrderCard,
  OutboundMarketplaceAddressCard,
} from '../modules/outbound/OutboundCreateAndExcelCards';
import {
  OutboundBackordersCard,
  OutboundOrdersListCard,
  OutboundSelectedOrderCard,
} from '../modules/outbound/OutboundOrdersAndBackordersCards';

export default function OutboundDemand() {
  const canReadOrders = usePermission('sales_order.read');
  const canCreate = usePermission('sales_order.create');
  const canHold = usePermission('sales_order.hold');
  const canRelease = usePermission('sales_order.release');
  const canCancel = usePermission('sales_order.cancel');
  const canReadBackorders = usePermission('backorder.read');
  const canManageBackorders = usePermission('backorder.manage');
  const canReadPartners = usePermission('partner.read');
  const canReadProducts = usePermission('product.read');
  const canReadWarehouses = usePermission('warehouse.read');

  const [orders, setOrders] = useState<SalesOrder[]>([]);
  const [backorders, setBackorders] = useState<Backorder[]>([]);
  const [selected, setSelected] = useState<SalesOrder | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState('');
  const lock = useRef(new Set<string>());

  const [showCreate, setShowCreate] = useState(false);
  const [partners, setPartners] = useState<Partner[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [externalOrderId, setExternalOrderId] = useState('');
  const [customerId, setCustomerId] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [requestedShipDate, setRequestedShipDate] = useState('');
  const [priority, setPriority] = useState('0');
  const [shippingMethod, setShippingMethod] = useState('');
  const [createLines, setCreateLines] = useState<CreateLine[]>([emptyLine(1)]);

  const [orderCancelReason, setOrderCancelReason] = useState('');
  const [recoverQty, setRecoverQty] = useState<Record<number, string>>({});
  const [backorderCancelQty, setBackorderCancelQty] = useState<Record<number, string>>({});
  const [backorderCancelReason, setBackorderCancelReason] = useState<Record<number, string>>({});

  const load = async (isInitial = false) => {
    setLoading(true);
    setError('');
    try {
      const [orderResponse, backorderResponse] = await Promise.all([
        canReadOrders ? apiClient.get('/api/v1/sales-orders') : Promise.resolve({ data: [] }),
        canReadBackorders ? apiClient.get('/api/v1/backorders') : Promise.resolve({ data: [] }),
      ]);
      setOrders(orderResponse.data);
      setBackorders(backorderResponse.data);
    } catch (e) {
      setOrders([]);
      setBackorders([]);
      setSelected(null);
      if (!isInitial) setError(messageOf(e));
    } finally {
      setLoading(false);
    }
  };

  const loadMasters = async () => {
    if (!(canCreate && canReadPartners && canReadProducts && canReadWarehouses)) return;
    try {
      const [partnerResponse, warehouseResponse, productResponse] = await Promise.all([
        apiClient.get('/api/v1/business-partners', { params: { page: 1, pageSize: 100, role: 'customer', active: true } }),
        apiClient.get('/api/v1/warehouses'),
        apiClient.get('/api/v1/products'),
      ]);
      setPartners(Array.isArray(partnerResponse.data?.items) ? partnerResponse.data.items : Array.isArray(partnerResponse.data) ? partnerResponse.data : []);
      setWarehouses((Array.isArray(warehouseResponse.data) ? warehouseResponse.data : []).filter((item: Warehouse) => item.isActive));
      setProducts((Array.isArray(productResponse.data) ? productResponse.data : []).filter((item: Product) => item.isActive));
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const detail = async (id: number) => {
    if (!canReadOrders) return;
    setError('');
    try {
      setSelected((await apiClient.get(`/api/v1/sales-orders/${id}`)).data);
    } catch (e) {
      setSelected(null);
      setError(messageOf(e));
    }
  };

  useEffect(() => {
    setError('');
    setNotice('');
    void load(true);
  }, [canReadOrders, canReadBackorders]);

  useEffect(() => {
    void loadMasters();
  }, [canCreate, canReadPartners, canReadProducts, canReadWarehouses]);

  const mutate = async (key: string, path: string, body: object, success: string) => {
    if (lock.current.has(key)) return;
    lock.current.add(key);
    setBusy(key);
    setError('');
    setNotice('');
    try {
      const response = await apiClient.post(path, body, { headers: idempotencyHeaders(key) });
      completeIdempotentAction(key);
      setNotice(success);
      if (path.startsWith('/api/v1/sales-orders/')) setSelected(response.data);
      await load();
      return response.data;
    } catch (e) {
      if ((e as { response?: { status?: number } })?.response?.status === 409 && selected?.id) await detail(selected.id);
      setError(messageOf(e));
      return null;
    } finally {
      lock.current.delete(key);
      setBusy('');
    }
  };

  const createOrder = async (event: React.FormEvent) => {
    event.preventDefault();
    const cleanExternalOrderId = sanitizeScannerCode(externalOrderId);
    if (createLines.some(line => line.productId && (!Number.isFinite(Number(line.quantity)) || roundQty4(Number(line.quantity)) <= 0))) {
      setError('Số lượng mỗi dòng hàng trong Đơn bán hàng phải lớn hơn 0.');
      return;
    }
    if (createLines.some(line => line.productId && (!Number.isFinite(Number(line.unitPrice)) || Number(line.unitPrice) <= 0))) {
      setError('Đơn giá mỗi dòng hàng trong Đơn bán hàng phải lớn hơn 0.');
      return;
    }
    const lines = createLines
      .filter(line => line.productId && roundQty4(Number(line.quantity)) > 0)
      .map(line => ({
        externalLineId: sanitizeScannerCode(line.externalLineId),
        productId: Number(line.productId),
        orderedQuantity: roundQty4(Number(line.quantity)),
        ...(Number(line.unitPrice || 0) > 0 ? { unitPrice: Number(line.unitPrice) } : {}),
        ...(Number(line.taxPercent || 0) > 0 ? { taxPercent: Number(line.taxPercent) } : {}),
      }));
    if (!cleanExternalOrderId || !customerId || !warehouseId || lines.length === 0) {
      setError('External Order ID, khách hàng, kho và ít nhất một dòng hàng là bắt buộc.');
      return;
    }
    if (new Set(lines.map(line => line.productId)).size !== lines.length) {
      setError('Mỗi mã sản phẩm (SKU) chỉ được xuất hiện một dòng duy nhất trong cùng Đơn bán hàng.');
      return;
    }
    const key = `sales-order-create-${cleanExternalOrderId}`;
    if (lock.current.has(key)) return;
    lock.current.add(key);
    setBusy(key);
    setError('');
    setNotice('');
    try {
      const response = await apiClient.post(
        '/api/v1/sales-orders',
        {
          externalOrderId: cleanExternalOrderId,
          customerId: Number(customerId),
          warehouseId: Number(warehouseId),
          requestedShipDate: requestedShipDate || undefined,
          priority: Number(priority) || 0,
          shippingMethod: shippingMethod.trim() || undefined,
          lines,
        },
        { headers: idempotencyHeaders(key) },
      );
      completeIdempotentAction(key);
      setNotice('Đã tạo Sales Order.');
      setShowCreate(false);
      setExternalOrderId('');
      setCustomerId('');
      setWarehouseId('');
      setRequestedShipDate('');
      setPriority('0');
      setShippingMethod('');
      setCreateLines([emptyLine(1)]);
      setSelected(response.data);
      await load();
    } catch (e) {
      setError(messageOf(e));
    } finally {
      lock.current.delete(key);
      setBusy('');
    }
  };

  const masterReady = canReadPartners && canReadProducts && canReadWarehouses;
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [expediteResult, setExpediteResult] = useState<{ expediteId: string; orderNumber: string; allocationWaveQueued: boolean } | null>(null);
  const [atpRoutingResult, setAtpRoutingResult] = useState<{
    evaluationId: string;
    atpCtpPromiseStatus: string;
    fulfillmentNodeRouting: string;
    allocationFairnessPolicy: string;
    serviceSegmentation: string;
    status: string;
  } | null>(null);

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

  const totalOrderedQty = roundQty4(orders.reduce((sum, item) => sum + item.orderedQuantity, 0));
  const totalReservedQty = roundQty4(orders.reduce((sum, item) => sum + item.reservedQuantity, 0));
  const totalAllocatedQty = roundQty4(orders.reduce((sum, item) => sum + item.allocatedQuantity, 0));
  const totalShippedQty = roundQty4(orders.reduce((sum, item) => sum + item.shippedQuantity, 0));
  const openBackorderRemaining = roundQty4(backorders.reduce((sum, item) => sum + item.remainingQuantity, 0));
  const fulfillmentRatePct = totalOrderedQty > 0 ? Math.round((totalShippedQty * 10000) / totalOrderedQty) / 100 : 100;
  const [activeSectionTab, setActiveSectionTab] = useState<'ALL' | 'ORDERS' | 'CREATE_EXCEL' | 'DOM_ROUTING' | 'BACKORDERS'>('ALL');
  const [orderPage, setOrderPage] = useState<number>(1);
  const orderPageSize = 10;
  const totalOrderPages = Math.max(1, Math.ceil(orders.length / orderPageSize));
  const pagedOrders = orders.slice((orderPage - 1) * orderPageSize, orderPage * orderPageSize);

  if (loading && orders.length === 0 && backorders.length === 0) return <p role="status">Đang tải Demand & Backorder...</p>;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Xuất kho"
        title="Quản lý Đơn bán hàng & Đơn nợ hàng"
        description="Theo dõi toàn trình từ Đặt hàng → Giữ hàng → Phân bổ → Lấy hàng → Giao hàng → Đơn nợ hàng. Phát hành đơn chỉ tạo cam kết giữ chỗ; tồn kho thực tế chỉ giảm khi Xuất bến giao hàng."
        actions={canCreate && masterReady && !showCreate ? <button type="button" onClick={() => setShowCreate(true)}>Tạo Đơn bán hàng</button> : undefined}
      />
      <OutboundPipelineStepper />
      <UiTabs
        ariaLabel="Chọn phân khu quản lý đơn bán hàng"
        activeTab={activeSectionTab}
        onTabChange={(nextTab) => {
          setActiveSectionTab(nextTab);
          if (nextTab === 'CREATE_EXCEL' && canCreate && masterReady) {
            setShowCreate(true);
          }
        }}
        tabs={[
          { id: 'ALL', label: 'Tất cả phân khu' },
          { id: 'ORDERS', label: `1. Danh sách đơn bán (${orders.length})` },
          { id: 'CREATE_EXCEL', label: '2. Tạo đơn & Nhập Excel' },
          { id: 'DOM_ROUTING', label: '3. Định tuyến đa kho' },
          { id: 'BACKORDERS', label: `4. Đơn nợ hàng (${backorders.length})` },
        ]}
      />
      {error && <div role="alert">{error}</div>}
      {notice && <p role="status" className="ui-success-text">{notice}</p>}
      {canCreate && !masterReady && <p className="ui-muted-text">Tạo Đơn bán hàng trên UI cần thêm partner.read, product.read và warehouse.read để chọn dữ liệu danh mục.</p>}

      <div className="ui-stack">
        <OutboundDemandOverviewCard
          totalOrderedQty={totalOrderedQty}
          totalReservedQty={totalReservedQty}
          totalAllocatedQty={totalAllocatedQty}
          totalShippedQty={totalShippedQty}
          backordersCount={backorders.length}
          openBackorderRemaining={openBackorderRemaining}
          fulfillmentRatePct={fulfillmentRatePct}
          busy={busy}
          governanceBusy={governanceBusy}
          onTriggerExpediteBackorder={triggerExpediteBackorder}
          onEvaluateAtpRouting={evaluateAtpRouting}
          expediteResult={expediteResult}
          atpRoutingResult={atpRoutingResult}
        />

        {(activeSectionTab === 'ALL' || activeSectionTab === 'DOM_ROUTING') && (
          <OutboundDomRoutingCard
            selected={selected}
            orders={orders}
            busy={busy}
            governanceBusy={governanceBusy}
            setError={setError}
          />
        )}

        {(activeSectionTab === 'ALL' || activeSectionTab === 'CREATE_EXCEL') && (
          <OutboundMarketplaceAddressCard
            orders={orders}
            warehouseId={warehouseId}
            busy={busy}
            setError={setError}
            setNotice={setNotice}
            onReload={load}
          />
        )}

        {(showCreate || activeSectionTab === 'CREATE_EXCEL') && canCreate && masterReady && (
          <OutboundCreateOrderCard
            externalOrderId={externalOrderId}
            setExternalOrderId={setExternalOrderId}
            customerId={customerId}
            setCustomerId={setCustomerId}
            warehouseId={warehouseId}
            setWarehouseId={setWarehouseId}
            requestedShipDate={requestedShipDate}
            setRequestedShipDate={setRequestedShipDate}
            priority={priority}
            setPriority={setPriority}
            shippingMethod={shippingMethod}
            setShippingMethod={setShippingMethod}
            partners={partners}
            warehouses={warehouses}
            products={products}
            createLines={createLines}
            setCreateLines={setCreateLines}
            busy={busy}
            onCreateOrder={createOrder}
            onClose={() => setShowCreate(false)}
          />
        )}

        {(activeSectionTab === 'ALL' || activeSectionTab === 'ORDERS') && canReadOrders && (
          <OutboundOrdersListCard
            ordersCount={orders.length}
            pagedOrders={pagedOrders}
            orderPage={orderPage}
            totalOrderPages={totalOrderPages}
            setOrderPage={setOrderPage}
            onSelectDetail={detail}
            onCopyNotice={setNotice}
          />
        )}

        {selected && (
          <OutboundSelectedOrderCard
            selected={selected}
            canHold={canHold}
            canRelease={canRelease}
            canCancel={canCancel}
            busy={busy}
            orderCancelReason={orderCancelReason}
            setOrderCancelReason={setOrderCancelReason}
            onMutate={mutate}
          />
        )}

        {(activeSectionTab === 'ALL' || activeSectionTab === 'BACKORDERS') && canReadBackorders && (
          <OutboundBackordersCard
            backorders={backorders}
            canManageBackorders={canManageBackorders}
            busy={busy}
            recoverQty={recoverQty}
            setRecoverQty={setRecoverQty}
            backorderCancelQty={backorderCancelQty}
            setBackorderCancelQty={setBackorderCancelQty}
            backorderCancelReason={backorderCancelReason}
            setBackorderCancelReason={setBackorderCancelReason}
            onMutate={mutate}
          />
        )}
      </div>
    </UiPage>
  );
}
