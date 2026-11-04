import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
} from '../ui/ProductionUi';
import { InboundPlanningFeedback, InboundPlanningToolbar, runInboundStateCommand } from './inboundPlanningShared';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import './InboundPlanning.css';
import {
  AsnCreateFormCard,
  dateTime,
  defaultVnArrival,
  emptyLine,
  labels,
  parseArrivalToIso,
  tone,
  type AsnDetail,
  type AsnList,
  type LineForm,
  type Partner,
  type Product,
  type PurchaseOrderDetail,
  type PurchaseOrderList,
  type Warehouse,
} from '../modules/inbound/AsnComponents';
import { AsnDetailAndStagingCards } from '../modules/inbound/AsnDetailAndStagingCards';

export default function Asns() {
  const canRead = usePermission('asn.read');
  const canCreate = usePermission('asn.create');
  const canUpdate = usePermission('asn.update');
  const canConfirm = usePermission('asn.confirm');
  const canReceive = usePermission('asn.receive');
  const canCancel = usePermission('asn.cancel');
  const canReadPo = usePermission('purchase_order.read');
  const canReadWarehouses = usePermission('warehouse.read');
  const canReadProducts = usePermission('product.read');
  const canReadPartners = usePermission('partner.read');

  const [items, setItems] = useState<AsnList[]>([]);
  const [selected, setSelected] = useState<AsnDetail | null>(null);
  const [purchaseOrders, setPurchaseOrders] = useState<PurchaseOrderList[]>([]);
  const [poDetail, setPoDetail] = useState<PurchaseOrderDetail | null>(null);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [suppliers, setSuppliers] = useState<Partner[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [statusFilter, setStatusFilter] = useState('');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [creating, setCreating] = useState(false);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [busy, setBusy] = useState('');
  const requestSeq = useRef(0);
  const mutationLock = useRef(new Set<string>());

  const [code, setCode] = useState('');
  const [purchaseOrderId, setPurchaseOrderId] = useState<number | ''>('');
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [expectedArrival, setExpectedArrival] = useState(defaultVnArrival);
  const [carrierName, setCarrierName] = useState('');
  const [vehiclePlate, setVehiclePlate] = useState('');
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<LineForm[]>([emptyLine()]);

  const canPrepareCreate = canCreate && canReadProducts && canReadWarehouses && canReadPartners;

  const loadList = useCallback(async () => {
    if (!canRead) { setItems([]); setSelected(null); setLoading(false); return; }
    const request = ++requestSeq.current; setLoading(true); setError('');
    try {
      const response = await apiClient.get('/api/v1/asns', { params: { status: statusFilter || undefined } });
      if (request !== requestSeq.current || !hasPermission('asn.read')) return;
      setItems(response.data);
    } catch (err: unknown) {
      if (request !== requestSeq.current) return;
      setItems([]); setSelected(null); setError(permissionError(err, 'Không thể tải danh sách ASN. Vui lòng thử lại.'));
    } finally { if (request === requestSeq.current) setLoading(false); }
  }, [canRead, statusFilter]);

  const loadReferences = useCallback(async () => {
    if (!canCreate) return;
    try {
      const [poRes, warehouseRes, productRes, partnerRes] = await Promise.all([
        canReadPo ? apiClient.get('/api/v1/purchase-orders') : Promise.resolve({ data: [] }),
        canReadWarehouses ? apiClient.get('/api/v1/warehouses') : Promise.resolve({ data: [] }),
        canReadProducts ? apiClient.get('/api/v1/products') : Promise.resolve({ data: [] }),
        canReadPartners ? apiClient.get('/api/v1/business-partners', { params: { role: 'supplier', active: true, pageSize: 100 } }) : Promise.resolve({ data: { items: [] } }),
      ]);
      setPurchaseOrders(hasPermission('purchase_order.read') ? poRes.data : []);
      setWarehouses(hasPermission('warehouse.read') ? warehouseRes.data : []);
      setProducts(hasPermission('product.read') ? productRes.data : []);
      setSuppliers(hasPermission('partner.read') ? partnerRes.data.items : []);
    } catch (err: unknown) {
      setPurchaseOrders([]); setWarehouses([]); setProducts([]); setSuppliers([]);
      setError(permissionError(err, 'Không thể tải dữ liệu tham khảo để tạo ASN.'));
    }
  }, [canCreate, canReadPo, canReadWarehouses, canReadProducts, canReadPartners]);

  useEffect(() => { void loadList(); return () => { requestSeq.current++; }; }, [loadList]);
  useEffect(() => { void loadReferences(); }, [loadReferences]);

  const filtered = useMemo(() => {
    const q = sanitizeScannerCode(search).toLowerCase();
    if (!q) return items;
    return items.filter(item => [item.code, item.purchaseOrderCode ?? '', item.supplierCode, item.supplierName, item.warehouseName].join(' ').toLowerCase().includes(q));
  }, [items, search]);

  const arrivalSummary = useMemo(() => {
    const nowMs = Date.now();
    const overdueArrivalCount = items.filter(x => (x.status === 'Confirmed' || x.status === 'InTransit') && x.expectedArrivalAtUtc && new Date(x.expectedArrivalAtUtc).getTime() < nowMs).length;
    const linkedPoAsnCount = items.filter(x => x.purchaseOrderId != null).length;
    const unlinkedPoAsnCount = items.filter(x => x.purchaseOrderId == null).length;
    const inboundExpectedVolume = roundQty4(items
      .filter(x => ['Confirmed', 'InTransit', 'Arrived', 'Receiving'].includes(x.status))
      .reduce((sum, x) => sum + (x.baseExpectedQuantity || 0), 0));
    return { overdueArrivalCount, linkedPoAsnCount, unlinkedPoAsnCount, inboundExpectedVolume };
  }, [items]);

  const viewDetail = async (id: number) => {
    setError(''); setSuccess('');
    try { setSelected((await apiClient.get('/api/v1/asns/' + id)).data); }
    catch (err: unknown) { setSelected(null); setError(permissionError(err, 'Không tìm thấy ASN hoặc bạn không có quyền truy cập.')); }
  };

  const choosePurchaseOrder = useCallback(async (value: string) => {
    const id = value ? Number(value) : '';
    setPurchaseOrderId(id); setPoDetail(null); setLines([emptyLine()]);
    if (!id) return;
    try {
      const detail = (await apiClient.get('/api/v1/purchase-orders/' + id)).data as PurchaseOrderDetail;
      setPoDetail(detail);
      const matchedSupplier = suppliers.find(x => x.code === detail.supplierCode);
      if (matchedSupplier) setSupplierId(matchedSupplier.id);
      if (detail.warehouseId) setWarehouseId(detail.warehouseId);
      const first = detail.lines[0];
      if (first) {
        const product = products.find(x => x.id === first.productId);
        setLines([{ purchaseOrderLineId: first.id, productId: first.productId, operationUnitId: product?.unitId ?? '', expectedQuantity: first.orderedQuantity }]);
      }
    } catch (err: unknown) { setPurchaseOrderId(''); setError(permissionError(err, 'Không thể tải chi tiết PO đã chọn.')); }
  }, [products, suppliers]);

  useEffect(() => {
    if (!canPrepareCreate || purchaseOrders.length === 0) return;
    let preselect = '';
    try {
      const params = new URLSearchParams(window.location.search);
      preselect = params.get('poId') || sessionStorage.getItem('erp_preselect_po_id') || '';
      if (preselect) sessionStorage.removeItem('erp_preselect_po_id');
    } catch {/* ignore */ }
    if (preselect) {
      setShowCreateForm(true);
      if (!code) setCode(`ASN-2026-${String(items.length + 1).padStart(3, '0')}`);
      void choosePurchaseOrder(preselect);
    }
  }, [canPrepareCreate, choosePurchaseOrder, code, items.length, purchaseOrders.length]);

  const changeLine = (index: number, patch: Partial<LineForm>) => {
    setLines(current => current.map((line, i) => {
      if (i !== index) return line;
      const next = { ...line, ...patch };
      if ('purchaseOrderLineId' in patch && patch.purchaseOrderLineId) {
        const poLine = poDetail?.lines.find(x => x.id === patch.purchaseOrderLineId);
        const product = products.find(x => x.id === poLine?.productId);
        next.productId = poLine?.productId ?? '';
        next.operationUnitId = product?.unitId ?? '';
        next.expectedQuantity = poLine?.orderedQuantity ?? '';
      } else if ('productId' in patch) {
        const product = products.find(x => x.id === patch.productId);
        next.operationUnitId = product?.unitId ?? '';
      }
      return next;
    }));
  };

  const resetForm = () => {
    setCode(`ASN-2026-${String(items.length + 1).padStart(3, '0')}`);
    setPurchaseOrderId(''); setPoDetail(null); setSupplierId(''); setWarehouseId('');
    setExpectedArrival(defaultVnArrival()); setCarrierName(''); setVehiclePlate(''); setNote(''); setLines([emptyLine()]);
  };

  const toggleCreateForm = () => {
    setShowCreateForm(prev => {
      const next = !prev;
      if (next && !code) setCode(`ASN-2026-${String(items.length + 1).padStart(3, '0')}`);
      if (next && !expectedArrival) setExpectedArrival(defaultVnArrival());
      return next;
    });
  };

  const submitCreate = async (event: FormEvent) => {
    event.preventDefault();
    if (!canPrepareCreate || creating) return;
    setError(''); setSuccess('');
    if (!purchaseOrderId && (!supplierId || !warehouseId)) { setError('ASN không gắn PO cần nhà cung cấp và kho nhận.'); return; }
    if (lines.length === 0 || lines.some(line => !line.productId || !line.operationUnitId || roundQty4(Number(line.expectedQuantity)) <= 0 || (purchaseOrderId && !line.purchaseOrderLineId))) {
      setError('Mỗi dòng ASN cần nguồn PO (nếu có), sản phẩm, đơn vị và số lượng lớn hơn 0.'); return;
    }
    const parsedArrivalIso = parseArrivalToIso(expectedArrival);
    if (expectedArrival.trim() && !parsedArrivalIso) {
      setError('Định dạng ngày dự kiến đến không hợp lệ. Vui lòng nhập theo chuẩn dd/mm/yyyy HH:mm (VD: 31/10/2026 08:00).');
      return;
    }
    setCreating(true);
    const cleanCode = sanitizeScannerCode(code);
    const action = 'asn-create:' + cleanCode;
    try {
      const resolvedSupplierId = purchaseOrderId
        ? (supplierId ? Number(supplierId) : (suppliers.find(x => x.code === poDetail?.supplierCode)?.id ?? 0))
        : Number(supplierId);
      const resolvedWarehouseId = purchaseOrderId
        ? (warehouseId ? Number(warehouseId) : (poDetail?.warehouseId ?? 0))
        : Number(warehouseId);
      const payload = {
        code: cleanCode, purchaseOrderId: purchaseOrderId || null,
        supplierId: resolvedSupplierId, warehouseId: resolvedWarehouseId,
        expectedArrivalAtUtc: parsedArrivalIso,
        carrierName: carrierName || null, vehiclePlate: vehiclePlate || null, note: note || null,
        lines: lines.map(line => ({
          purchaseOrderLineId: line.purchaseOrderLineId || null, productId: Number(line.productId),
          operationUnitId: Number(line.operationUnitId), expectedQuantity: roundQty4(Number(line.expectedQuantity)),
        })),
      };
      const response = await apiClient.post('/api/v1/asns', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccess('Đã tạo ASN nháp. ASN chỉ ghi nhận hàng dự kiến, chưa tăng tồn kho.');
      resetForm(); await loadList(); await viewDetail(response.data.id);
    } catch (err: unknown) { setError(permissionError(err, 'Không thể tạo ASN. Kiểm tra số lượng PO và dữ liệu tham khảo.')); }
    finally { setCreating(false); }
  };

  const command = (path: string, action: string, successMessage: string) =>
    runInboundStateCommand({
      selected, path, action, successMessage, errorMessage: 'Không thể chuyển trạng thái ASN. Vui lòng tải lại và thử lại.',
      mutationLock, setBusy, setError, setSuccess, setSelected, reload: loadList,
    });

  const eligiblePurchaseOrders = purchaseOrders.filter(x => ['Open', 'PartiallyReceived'].includes(x.status));

  if (loading && items.length === 0) return <p role="status">Đang tải ASN...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Nhập kho"
      title="Thông báo giao hàng trước (ASN)"
      description="Theo dõi lô hàng dự kiến từ nhà cung cấp tới kho trước khi tiếp nhận. Hoàn tất ASN vẫn chưa tạo tồn kho."
      actions={canCreate ? (
        <button
          type="button"
          className="ui-primary-button"
          style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8', padding: '8px 16px', borderRadius: 8 }}
          onClick={toggleCreateForm}
        >
          {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo thông báo ASN'}
        </button>
      ) : undefined}
    />

    <InboundPlanningFeedback error={error} success={success} />

    {canCreate && showCreateForm && (
      <AsnCreateFormCard
        canPrepareCreate={canPrepareCreate} canReadPo={canReadPo} creating={creating} code={code} setCode={setCode}
        purchaseOrderId={purchaseOrderId} choosePurchaseOrder={choosePurchaseOrder} eligiblePurchaseOrders={eligiblePurchaseOrders}
        poDetail={poDetail} supplierId={supplierId} setSupplierId={setSupplierId} suppliers={suppliers}
        warehouseId={warehouseId} setWarehouseId={setWarehouseId} warehouses={warehouses}
        expectedArrival={expectedArrival} setExpectedArrival={setExpectedArrival} carrierName={carrierName} setCarrierName={setCarrierName}
        vehiclePlate={vehiclePlate} setVehiclePlate={setVehiclePlate} note={note} setNote={setNote}
        lines={lines} setLines={setLines} changeLine={changeLine} products={products}
        submitCreate={submitCreate} resetForm={resetForm} onClose={() => setShowCreateForm(false)}
      />
    )}

    <UiMetricGrid>
      <UiMetric value={items.length} label="ASN trong phạm vi hiện tại" />
      <UiMetric value={items.filter(x => x.status === 'InTransit').length} label="Đang vận chuyển" />
      <UiMetric value={items.filter(x => x.status === 'Arrived').length} label="Đã đến cổng" />
      <UiMetric value={items.filter(x => x.status === 'Receiving').length} label="Đang tiếp nhận" />
      <UiMetric value={`${arrivalSummary.inboundExpectedVolume.toLocaleString('vi-VN')} THÙNG`} label="Tổng SL cơ sở đang đến kho" />
    </UiMetricGrid>

    <InboundPlanningToolbar labels={labels} statusFilter={statusFilter} setStatusFilter={setStatusFilter} search={search} setSearch={setSearch} placeholder="Mã ASN, đơn mua, nhà cung cấp, kho" />

    <div className="inbound-planning-grid">
      <UiCard title="Danh sách ASN" subtitle="Theo dõi lịch xe giao hàng dự kiến (chuẩn ngày giờ Việt Nam dd/mm/yyyy) và liên kết đơn mua">
        {filtered.length === 0 ? <UiEmptyState title="Chưa có ASN phù hợp" detail="Thay đổi bộ lọc hoặc tạo ASN mới nếu bạn có quyền." /> :
          <UiTableScroll>
            <table className="inbound-planning-table ui-table-compact" aria-label="Danh sách ASN">
              <thead><tr><th>Mã ASN</th><th>Đơn mua (PO)</th><th>Nhà cung cấp</th><th>Kho nhận</th><th>Dự kiến đến</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL dự kiến</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
              <tbody>{filtered.map(item => <tr key={item.id}>
                <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}><button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void viewDetail(item.id)}><strong style={{ whiteSpace: 'nowrap' }}>{item.code}</strong></button></td>
                <td style={{ whiteSpace: 'nowrap' }}>{item.purchaseOrderCode || 'Không gắn đơn mua'}</td>
                <td><span style={{ whiteSpace: 'nowrap' }}>{item.supplierCode}</span><br /><small>{item.supplierName}</small></td>
                <td>{item.warehouseName}</td>
                <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{dateTime(item.expectedArrivalAtUtc)}</td>
                <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.baseExpectedQuantity.toLocaleString('vi-VN')} THÙNG</td>
                <td><UiBadge tone={tone(item.status)}><span style={{ whiteSpace: 'nowrap' }}>{labels[item.status] ?? item.status}</span></UiBadge></td>
                <td>
                  <button
                    type="button"
                    style={{ whiteSpace: 'nowrap', background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }}
                    onClick={() => void viewDetail(item.id)}
                  >
                    Xem chi tiết
                  </button>
                </td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
          <span>Hiển thị {filtered.length === 0 ? 0 : 1}–{filtered.length} / Tổng {filtered.length} thông báo ASN</span>
          <span>Trang 1 / 1 (Cỡ trang: 20)</span>
        </div>
      </UiCard>

      <AsnDetailAndStagingCards
        items={items}
        selected={selected}
        warehouseId={warehouseId}
        canConfirm={canConfirm}
        canUpdate={canUpdate}
        canReceive={canReceive}
        canCancel={canCancel}
        busy={busy}
        creating={creating}
        arrivalSummary={arrivalSummary}
        command={command}
        setError={setError}
      />
    </div>
  </UiPage>;
}
