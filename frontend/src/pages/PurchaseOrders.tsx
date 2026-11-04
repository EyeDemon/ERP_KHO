import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import {
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
} from '../ui/ProductionUi';
import { InboundPlanningFeedback, InboundPlanningToolbar, runInboundStateCommand } from './inboundPlanningShared';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import './InboundPlanning.css';
import {
  emptyLine,
  labels,
  PurchaseOrderCreateFormCard,
  PurchaseOrderDetailCard,
  PurchaseOrderFulfillmentMonitorCard,
  translateProcurementSignal,
  type LineForm,
  type Partner,
  type Product,
  type PurchaseOrderDetail,
  type PurchaseOrderList,
  type Warehouse,
} from '../modules/inbound/PurchaseOrderComponents';
import { PurchaseOrderListCard, PurchaseOrderSecondaryFiltersBar } from '../modules/inbound/PurchaseOrderListSection';

export default function PurchaseOrders() {
  const canRead = usePermission('purchase_order.read');
  const canCreate = usePermission('purchase_order.create');
  const canRelease = usePermission('purchase_order.release');
  const canClose = usePermission('purchase_order.close');
  const canCancel = usePermission('purchase_order.cancel');
  const canReadWarehouses = usePermission('warehouse.read');
  const canReadProducts = usePermission('product.read');
  const canReadPartners = usePermission('partner.read');

  const [items, setItems] = useState<PurchaseOrderList[]>([]);
  const [selected, setSelected] = useState<PurchaseOrderDetail | null>(null);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [suppliers, setSuppliers] = useState<Partner[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [statusFilter, setStatusFilter] = useState('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [supplierFilter, setSupplierFilter] = useState('');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [creating, setCreating] = useState(false);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [expediteResult, setExpediteResult] = useState<{ expediteId: string; vendorNotificationStatus: string } | null>(null);
  const [procurementRecResult, setProcurementRecResult] = useState<{
    recommendationId: string;
    procurementSuggestionStatus: string;
    supplierLeadTimePercentile: string;
    expediteDeferSignal: string;
    status: string;
  } | null>(null);
  const requestSeq = useRef(0);
  const mutationLock = useRef(new Set<string>());

  const expediteOverdue = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true); setError('');
    try {
      const res = await apiClient.post('/api/v1/purchase-orders/expedite-overdue', { supplierId: supplierId || undefined }, { headers: idempotencyHeaders('po-expedite-overdue:' + Date.now()) });
      if (res?.data?.expediteId && res?.data?.vendorNotificationStatus) {
        setExpediteResult({
          expediteId: String(res.data.expediteId),
          vendorNotificationStatus: translateProcurementSignal(String(res.data.vendorNotificationStatus)),
        });
      } else {
        setExpediteResult(null);
        setError('Phản hồi đôn đốc nhà cung cấp không hợp lệ từ máy chủ.');
      }
    } catch (err: unknown) {
      setExpediteResult(null);
      setError(permissionError(err, 'Không thể phát tín hiệu đôn đốc nhà cung cấp.'));
    } finally { setGovernanceBusy(false); }
  };

  const generateProcurementRecommendations = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true); setError('');
    try {
      const res = await apiClient.post('/api/v1/purchase-orders/generate-procurement-recommendations', {
        warehouseId: warehouseId || items[0]?.warehouseId || undefined,
      }, { headers: idempotencyHeaders('po-procurement-rec:' + Date.now()) });
      if (res?.data?.recommendationId && res?.data?.status) {
        setProcurementRecResult({
          recommendationId: String(res.data.recommendationId),
          procurementSuggestionStatus: translateProcurementSignal(String(res.data.procurementSuggestionStatus || '')),
          supplierLeadTimePercentile: translateProcurementSignal(String(res.data.supplierLeadTimePercentile || '')),
          expediteDeferSignal: translateProcurementSignal(String(res.data.expediteDeferSignal || '')),
          status: translateProcurementSignal(String(res.data.status)),
        });
      } else {
        setProcurementRecResult(null);
        setError('Phản hồi đề xuất mua hàng thông minh không hợp lệ từ máy chủ.');
      }
    } catch (err: unknown) {
      setProcurementRecResult(null);
      setError(permissionError(err, 'Không thể phân tích đề xuất đặt hàng thông minh.'));
    } finally { setGovernanceBusy(false); }
  };

  const [sourceSystem, setSourceSystem] = useState('ERP');
  const [externalPoId, setExternalPoId] = useState('');
  const [code, setCode] = useState('');
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [orderDate, setOrderDate] = useState(new Date().toISOString().slice(0, 10));
  const [expectedDate, setExpectedDate] = useState('');
  const [currency, setCurrency] = useState('VND');
  const [lines, setLines] = useState<LineForm[]>([emptyLine()]);

  const canPrepareCreate = canCreate && canReadWarehouses && canReadProducts && canReadPartners;

  const loadList = useCallback(async () => {
    if (!canRead) { setItems([]); setSelected(null); setLoading(false); return; }
    const request = ++requestSeq.current;
    setLoading(true); setError('');
    try {
      const response = await apiClient.get('/api/v1/purchase-orders', { params: { status: statusFilter || undefined } });
      if (request !== requestSeq.current || !hasPermission('purchase_order.read')) return;
      setItems(response.data);
    } catch (err: unknown) {
      if (request !== requestSeq.current) return;
      setItems([]); setSelected(null);
      setError(permissionError(err, 'Không thể tải danh sách đơn mua. Vui lòng thử lại.'));
    } finally { if (request === requestSeq.current) setLoading(false); }
  }, [canRead, statusFilter]);

  const loadReferences = useCallback(async () => {
    if (!canCreate) return;
    try {
      const [warehouseRes, productRes, partnerRes] = await Promise.all([
        canReadWarehouses ? apiClient.get('/api/v1/warehouses') : Promise.resolve({ data: [] }),
        canReadProducts ? apiClient.get('/api/v1/products') : Promise.resolve({ data: [] }),
        canReadPartners ? apiClient.get('/api/v1/business-partners', { params: { role: 'supplier', active: true, pageSize: 100 } }) : Promise.resolve({ data: { items: [] } }),
      ]);
      setWarehouses(hasPermission('warehouse.read') ? warehouseRes.data : []);
      setProducts(hasPermission('product.read') ? productRes.data : []);
      setSuppliers(hasPermission('partner.read') ? partnerRes.data.items : []);
    } catch (err: unknown) {
      setWarehouses([]); setProducts([]); setSuppliers([]);
      setError(permissionError(err, 'Không thể tải dữ liệu tham khảo để tạo đơn mua.'));
    }
  }, [canCreate, canReadWarehouses, canReadProducts, canReadPartners]);

  useEffect(() => { void loadList(); return () => { requestSeq.current++; }; }, [loadList]);
  useEffect(() => { void loadReferences(); }, [loadReferences]);

  const warehouseFilterOptions = useMemo(() => {
    const map = new Map<string, string>();
    items.forEach(item => { if (item.warehouseName) map.set(item.warehouseName, item.warehouseName); });
    warehouses.forEach(w => { if (w.name) map.set(w.name, w.name); });
    return Array.from(map.values());
  }, [items, warehouses]);

  const supplierFilterOptions = useMemo(() => {
    const map = new Map<string, string>();
    items.forEach(item => { if (item.supplierCode) map.set(item.supplierCode, `${item.supplierCode} — ${item.supplierName}`); });
    suppliers.forEach(s => { if (s.code) map.set(s.code, `${s.code} — ${s.name}`); });
    return Array.from(map.entries());
  }, [items, suppliers]);

  const filtered = useMemo(() => {
    const q = sanitizeScannerCode(search).toLowerCase();
    return items.filter(item => {
      if (warehouseFilter && item.warehouseName !== warehouseFilter) return false;
      if (supplierFilter && item.supplierCode !== supplierFilter) return false;
      if (!q) return true;
      return [item.code, item.externalPoId, item.supplierCode, item.supplierName, item.warehouseName].join(' ').toLowerCase().includes(q);
    });
  }, [items, search, warehouseFilter, supplierFilter]);

  const fulfillmentSummary = useMemo(() => {
    const todayMs = new Date(new Date().toISOString().slice(0, 10)).getTime();
    const overdueItems = items.filter(x => (x.status === 'Open' || x.status === 'PartiallyReceived') && x.expectedDate && new Date(x.expectedDate).getTime() < todayMs);
    const activePriorityItems = items
      .filter(x => x.status === 'Open' || x.status === 'PartiallyReceived')
      .sort((a, b) => (a.expectedDate || '9999-12-31').localeCompare(b.expectedDate || '9999-12-31'))
      .slice(0, 5);
    const receivedOrClosedCount = items.filter(x => x.status === 'Received' || x.status === 'Closed').length;
    const totalBaseOrderedQuantity = roundQty4(items.reduce((sum, x) => sum + (x.baseOrderedQuantity || 0), 0));
    const completionRate = items.length > 0 ? Math.round((receivedOrClosedCount / items.length) * 100) : 0;
    return { overdueExpectedCount: overdueItems.length, receivedOrClosedCount, totalBaseOrderedQuantity, completionRate, activePriorityItems };
  }, [items]);

  const viewDetail = async (id: number) => {
    setError(''); setSuccess('');
    try { setSelected((await apiClient.get('/api/v1/purchase-orders/' + id)).data); }
    catch (err: unknown) { setSelected(null); setError(permissionError(err, 'Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.')); }
  };

  const resetForm = () => {
    setSourceSystem('ERP'); setExternalPoId(''); setCode(''); setSupplierId(''); setWarehouseId('');
    setOrderDate(new Date().toISOString().slice(0, 10)); setExpectedDate(''); setCurrency('VND'); setLines([emptyLine()]);
  };

  const changeLine = (index: number, patch: Partial<LineForm>) => {
    setLines(current => current.map((line, i) => {
      if (i !== index) return line;
      const next = { ...line, ...patch };
      if ('productId' in patch) {
        const product = products.find(x => x.id === patch.productId);
        next.operationUnitId = product?.unitId ?? '';
      }
      return next;
    }));
  };

  const submitCreate = async (event: FormEvent) => {
    event.preventDefault();
    if (!canPrepareCreate || creating) return;
    setError(''); setSuccess('');
    if (!supplierId || !warehouseId) { setError('Chọn nhà cung cấp và kho nhận.'); return; }
    if (lines.length === 0 || lines.some(line => !sanitizeScannerCode(line.externalLineId) || !line.productId || !line.operationUnitId || roundQty4(Number(line.orderedQuantity)) <= 0)) {
      setError('Mỗi dòng cần mã dòng nguồn, sản phẩm, đơn vị và số lượng lớn hơn 0.'); return;
    }
    setCreating(true);
    const cleanCode = sanitizeScannerCode(code);
    const cleanExternalPoId = sanitizeScannerCode(externalPoId);
    const action = 'purchase-order-create:' + cleanCode + ':' + cleanExternalPoId;
    try {
      const payload = {
        sourceSystem: sanitizeScannerCode(sourceSystem), externalPoId: cleanExternalPoId, code: cleanCode, supplierId, warehouseId,
        orderDate: orderDate ? orderDate + 'T00:00:00' : undefined,
        expectedDate: expectedDate ? expectedDate + 'T00:00:00' : null,
        currency: currency || null,
        lines: lines.map(line => ({
          externalLineId: sanitizeScannerCode(line.externalLineId), productId: Number(line.productId), operationUnitId: Number(line.operationUnitId),
          orderedQuantity: roundQty4(Number(line.orderedQuantity)),
          unitPrice: Number(line.unitPrice || 0), taxPercent: Number(line.taxPercent || 0),
          allowedOverReceiptPct: Number(line.allowedOverReceiptPct || 0),
          allowedUnderReceiptPct: Number(line.allowedUnderReceiptPct || 0),
        })),
      };
      const response = await apiClient.post('/api/v1/purchase-orders', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccess('Đã tạo đơn mua nháp. Đơn mua chưa làm thay đổi tồn kho.');
      resetForm(); await loadList(); await viewDetail(response.data.id);
    } catch (err: unknown) { setError(permissionError(err, 'Không thể tạo đơn mua. Kiểm tra dữ liệu và thử lại.')); }
    finally { setCreating(false); }
  };

  const command = (path: string, action: string, successMessage: string) =>
    runInboundStateCommand({
      selected, path, action, successMessage, errorMessage: 'Không thể chuyển trạng thái đơn mua. Vui lòng tải lại và thử lại.',
      mutationLock, setBusy, setError, setSuccess, setSelected, reload: loadList,
    });

  const toggleCreateForm = () => {
    setShowCreateForm(prev => {
      const next = !prev;
      if (next && !code) {
        const seq = String(items.length + 1).padStart(3, '0');
        setCode(`PO-2026-${seq}`);
        if (!externalPoId) setExternalPoId(`ERP-PO-${seq}`);
      }
      return next;
    });
  };

  if (loading && items.length === 0) return <p role="status">Đang tải đơn mua...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Nhập kho"
      title="Đơn mua (PO)"
      description="Theo dõi đơn đặt hàng dự kiến từ hệ thống mua hàng trước khi tạo thông báo giao hàng (ASN) và tiếp nhận. Đơn mua không làm tăng tồn kho."
      actions={canCreate ? (
        <button
          type="button"
          className="ui-primary-button"
          style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8', padding: '8px 16px', borderRadius: 8 }}
          onClick={toggleCreateForm}
        >
          {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo đơn mua nháp'}
        </button>
      ) : undefined}
    />

    <InboundPlanningFeedback error={error} success={success} />

    {canCreate && showCreateForm && (
      <PurchaseOrderCreateFormCard
        canPrepareCreate={canPrepareCreate} creating={creating} sourceSystem={sourceSystem} setSourceSystem={setSourceSystem}
        externalPoId={externalPoId} setExternalPoId={setExternalPoId} code={code} setCode={setCode}
        supplierId={supplierId} setSupplierId={setSupplierId} suppliers={suppliers}
        warehouseId={warehouseId} setWarehouseId={setWarehouseId} warehouses={warehouses}
        currency={currency} setCurrency={setCurrency} orderDate={orderDate} setOrderDate={setOrderDate}
        expectedDate={expectedDate} setExpectedDate={setExpectedDate} lines={lines} setLines={setLines}
        changeLine={changeLine} products={products} submitCreate={submitCreate} resetForm={resetForm}
        onClose={() => setShowCreateForm(false)}
      />
    )}

    <UiMetricGrid>
      <UiMetric value={items.length} label="Đơn mua trong phạm vi hiện tại" />
      <UiMetric value={items.filter(x => x.status === 'Open').length} label="Đang mở" />
      <UiMetric value={items.filter(x => x.status === 'PartiallyReceived').length} label="Nhận một phần" />
      <UiMetric value={items.filter(x => x.status === 'Received').length} label="Đã nhận đủ" />
    </UiMetricGrid>

    <InboundPlanningToolbar labels={labels} statusFilter={statusFilter} setStatusFilter={setStatusFilter} search={search} setSearch={setSearch} placeholder="Mã đơn mua, mã nguồn, nhà cung cấp, kho" />

    <PurchaseOrderSecondaryFiltersBar
      warehouseFilter={warehouseFilter}
      setWarehouseFilter={setWarehouseFilter}
      warehouseFilterOptions={warehouseFilterOptions}
      supplierFilter={supplierFilter}
      setSupplierFilter={setSupplierFilter}
      supplierFilterOptions={supplierFilterOptions}
    />

    <div className="inbound-planning-grid">
      <PurchaseOrderListCard filtered={filtered} viewDetail={viewDetail} />

      <div className="ui-stack">
        {selected && (
          <PurchaseOrderDetailCard
            selected={selected}
            canRelease={canRelease}
            canClose={canClose}
            canCancel={canCancel}
            busy={busy}
            command={command}
          />
        )}

        <PurchaseOrderFulfillmentMonitorCard
          fulfillmentSummary={fulfillmentSummary}
          totalItemsCount={items.length}
          busy={busy}
          creating={creating}
          governanceBusy={governanceBusy}
          expediteOverdue={expediteOverdue}
          generateProcurementRecommendations={generateProcurementRecommendations}
          expediteResult={expediteResult}
          procurementRecResult={procurementRecResult}
        />
      </div>
    </div>
  </UiPage>;
}
