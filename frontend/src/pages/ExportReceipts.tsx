import { useState, useEffect, useRef } from 'react';
import apiClient from '../services/apiClient';
import { currentUserId, hasPermission, usePermissionSet } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { extractConflictCode } from '../services/permissionPresentation';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import OutboundPipelineStepper from '../components/common/OutboundPipelineStepper';
import { UiPage, UiPageHeader, UiTabs } from '../ui/ProductionUi';
import type { ExportReceipt, ExportSectionTab, ExportStatusQuickFilter, Partner, Product, Warehouse } from '../modules/outbound/exportReceiptsTypes';
import { safeRequestError } from '../modules/outbound/exportReceiptsTypes';
import { ExportReceiptsBatchAwbCard } from '../modules/outbound/ExportReceiptsBatchAwbCard';
import { ExportReceiptDetailCard, ExportReceiptsKpiCard } from '../modules/outbound/ExportReceiptsKpiAndDetailCards';
import { ExportReceiptsCreateCard, ExportReceiptsListCard } from '../modules/outbound/ExportReceiptsCreateAndListCards';
import './ExportReceipts.css';

const ExportReceipts = () => {
  const permissionSnapshot = usePermissionSet();
  const userId = currentUserId();
  const canCreate = hasPermission('export_receipt.create');
  const canUpdate = hasPermission('export_receipt.update');
  const canApproveAndReserve = hasPermission('export_receipt.approve');
  const canDispatch = hasPermission('export_receipt.dispatch');
  const canCancel = hasPermission('export_receipt.cancel');
  const canReadPartners = hasPermission('partner.read');
  const canReadSalesOrders = hasPermission('sales_order.read');
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [receipts, setReceipts] = useState<ExportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [customers, setCustomers] = useState<Partner[]>([]);
  const [salesOrders, setSalesOrders] = useState<Array<{ id: number; orderCode?: string; code?: string; customerName?: string }>>([]);
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ExportReceipt | null>(null);
  const [actionInFlight, setActionInFlight] = useState<string | null>(null);
  const partnerMutationInFlight = useRef(false);
  const [partnerUpdating, setPartnerUpdating] = useState(false);
  const [printReceipt, setPrintReceipt] = useState<ExportReceipt | null>(null);
  const [printFetchedAt, setPrintFetchedAt] = useState<Date | null>(null);
  const [printLoadingId, setPrintLoadingId] = useState<number | null>(null);
  const printTriggerRef = useRef<HTMLButtonElement | null>(null);
  const workflowInFlight = useRef(false);
  const auxiliaryGeneration = useRef(0);
  const listGeneration = useRef(0);
  const detailGeneration = useRef(0);
  const printGeneration = useRef(0);
  const [handoverTrackingInput, setHandoverTrackingInput] = useState('');

  const fetchData = async () => {
    const generation = ++auxiliaryGeneration.current;
    try {
      const nextWarehouses: Warehouse[] = [];
      const nextProducts: Product[] = [];
      let nextCustomers: Partner[] = [];

      if (canCreate) {
        const [whRes, prRes] = await Promise.all([
          apiClient.get('/api/v1/warehouses'),
          apiClient.get('/api/v1/products'),
        ]);
        nextWarehouses.push(...whRes.data);
        nextProducts.push(...prRes.data);
      }
      if (canReadPartners && (canCreate || canUpdate)) {
        const bpRes = await apiClient.get('/api/v1/business-partners', { params: { role: 'customer', pageSize: 100 } });
        nextCustomers = bpRes.data.items;
      }
      let nextSalesOrders: Array<{ id: number; orderCode?: string; code?: string; customerName?: string }> = [];
      if (canCreate && canReadSalesOrders) {
        const soRes = await Promise.resolve(apiClient.get('/api/v1/sales-orders')).catch(() => ({ data: [] }));
        nextSalesOrders = Array.isArray(soRes?.data)
          ? soRes.data
          : Array.isArray(soRes?.data?.items)
            ? soRes.data.items
            : [];
      }

      if (generation !== auxiliaryGeneration.current) return;
      setWarehouses(nextWarehouses);
      setProducts(nextProducts);
      setCustomers(nextCustomers);
      setSalesOrders(nextSalesOrders);
    } catch (err: any) {
      if (generation !== auxiliaryGeneration.current) return;
      setWarehouses([]);
      setProducts([]);
      setCustomers([]);
      setSalesOrders([]);
      setError(safeRequestError(err, 'Không thể tải dữ liệu hỗ trợ phiếu xuất.'));
    }
  };

  const fetchReceipts = async () => {
    const generation = ++listGeneration.current;
    try {
      const res = await apiClient.get('/api/v1/export-receipts');
      if (generation !== listGeneration.current) return;
      const list = Array.isArray(res.data) ? (res.data as ExportReceipt[]) : [];
      setReceipts(list);
      const realTrackings = list.map(r => r.trackingNumber?.trim()).filter(Boolean).join(', ');
      if (realTrackings) {
        setHandoverTrackingInput(prev => prev || realTrackings);
      }
    } catch (err: any) {
      if (generation !== listGeneration.current) return;
      setReceipts([]);
      setSelectedReceipt(null);
      setPrintReceipt(null);
      setPrintFetchedAt(null);
      setError(safeRequestError(err, 'Không thể tải danh sách phiếu xuất.'));
    }
  };

  useEffect(() => {
    detailGeneration.current++;
    printGeneration.current++;
    setSelectedReceipt(null);
    setPrintReceipt(null);
    setPrintFetchedAt(null);
    void fetchData();
    void fetchReceipts();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [permissionSnapshot, canCreate, canUpdate, canReadPartners]);

  const handleViewDetails = async (id: number) => {
    const generation = ++detailGeneration.current;
    try {
      const res = await apiClient.get(`/api/v1/export-receipts/${id}`);
      if (generation !== detailGeneration.current) return;
      setSelectedReceipt(res.data);
    } catch (err: any) {
      if (generation !== detailGeneration.current) return;
      setSelectedReceipt(null);
      setError(safeRequestError(err, 'Không thể tải chi tiết phiếu xuất.'));
    }
  };

  const refreshAfterAction = async (id: number) => {
    await fetchReceipts();
    if (selectedReceipt?.id === id) await handleViewDetails(id);
  };

  const handleCancel = async (id: number) => {
    if (workflowInFlight.current || !confirm('Bạn có chắc muốn hủy phiếu xuất này?')) return;
    workflowInFlight.current = true;
    setActionInFlight(`${id}-cancel`);
    try {
      const action = `export-cancel:${id}`;
      await apiClient.post(`/api/v1/export-receipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Đã hủy phiếu xuất.');
      await refreshAfterAction(id);
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể hủy phiếu xuất.'));
      if (err?.response?.status === 409 && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') await refreshAfterAction(id);
    } finally {
      workflowInFlight.current = false;
      setActionInFlight(null);
    }
  };

  const handleWorkflowAction = async (id: number, action: 'approve-and-reserve' | 'approve-and-dispatch' | 'dispatch') => {
    const messages = {
      'approve-and-reserve': 'Phiếu sẽ được duyệt và giữ hàng. Tồn thực tế chưa giảm cho đến khi người có quyền xác nhận xuất kho xác nhận hàng đã rời kho.',
      'approve-and-dispatch': 'Thao tác này sẽ duyệt phiếu và giảm tồn kho ngay lập tức. Bạn có chắc hàng đã được giao khỏi kho?',
      dispatch: 'Xác nhận hàng đã rời kho. Lượng hàng đã giữ sẽ được tiêu thụ và tồn thực tế sẽ giảm ngay.',
    };
    if (workflowInFlight.current || !confirm(messages[action])) return;
    workflowInFlight.current = true;
    setActionInFlight(`${id}-${action}`);
    try {
      const logicalAction = `export-${action}:${id}`;
      await apiClient.post(`/api/v1/export-receipts/${id}/${action}`, undefined, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      await refreshAfterAction(id);
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể xử lý phiếu xuất.'));
      if (err?.response?.status === 409 && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') await refreshAfterAction(id);
    } finally {
      workflowInFlight.current = false;
      setActionInFlight(null);
    }
  };

  const changeCustomer = async (value: number | null) => {
    if (!selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true;
    setPartnerUpdating(true);
    setError('');
    try {
      await apiClient.put(`/api/v1/export-receipts/${selectedReceipt.id}/customer`, { partnerId: value });
      await fetchReceipts();
      await handleViewDetails(selectedReceipt.id);
    } catch (x: any) {
      setError(safeRequestError(x, 'Không đổi được khách hàng.'));
    } finally {
      partnerMutationInFlight.current = false;
      setPartnerUpdating(false);
    }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    const generation = ++printGeneration.current;
    printTriggerRef.current = trigger;
    setPrintReceipt(null);
    setPrintFetchedAt(null);
    setPrintLoadingId(id);
    setError('');
    try {
      const res = await apiClient.get(`/api/v1/export-receipts/${id}`);
      if (generation !== printGeneration.current) return;
      setPrintReceipt(res.data);
      setPrintFetchedAt(new Date());
    } catch (x: any) {
      if (generation !== printGeneration.current) return;
      setPrintReceipt(null);
      setPrintFetchedAt(null);
      setError(safeRequestError(x, 'Không tải được dữ liệu bản in.'));
    } finally {
      if (generation === printGeneration.current) setPrintLoadingId(null);
    }
  };

  const closePrintPreview = () => {
    printGeneration.current++;
    setPrintReceipt(null);
    setPrintFetchedAt(null);
    setPrintLoadingId(null);
    queueMicrotask(() => printTriggerRef.current?.focus());
  };

  const [activeSectionTab, setActiveSectionTab] = useState<ExportSectionTab>('ALL');
  const [statusQuickFilter, setStatusQuickFilter] = useState<ExportStatusQuickFilter>('ALL');
  const [receiptPage, setReceiptPage] = useState<number>(1);
  const receiptPageSize = 10;
  const filteredReceipts = receipts.filter((r) => {
    if (statusQuickFilter === 'ALL') return true;
    if (statusQuickFilter === 'Draft') return r.status === 'Draft';
    if (statusQuickFilter === 'Approved') return r.status === 'Approved';
    if (statusQuickFilter === 'Dispatched') return r.status === 'Dispatched' || r.status === 'Completed';
    if (statusQuickFilter === 'Cancelled') return r.status === 'Cancelled';
    if (statusQuickFilter === 'OverdueHandover') {
      const hasCancelPending = receipts.some((item) => item.status === 'CancelPendingPutback');
      if (hasCancelPending) return r.status === 'CancelPendingPutback';
      return r.status === 'Approved' || r.status === 'Draft' || Boolean(r.marketplaceOrderId);
    }
    return true;
  });
  const totalReceiptPages = Math.max(1, Math.ceil(filteredReceipts.length / receiptPageSize));
  const pagedReceipts = filteredReceipts.slice((receiptPage - 1) * receiptPageSize, receiptPage * receiptPageSize);

  return (
    <UiPage>
      <div className="export-receipts">
        <UiPageHeader
          eyebrow="Xuất kho"
          title="Quản Lý Phiếu Xuất Kho"
          description="Tạo và theo dõi phiếu xuất từ kiểm tra tồn khả dụng, duyệt và giữ hàng đến xác nhận hàng rời kho."
          actions={
            canCreate ? (
              <button
                type="button"
                className="no-print ui-primary-button"
                onClick={() => setShowCreateForm(prev => !prev)}
                style={{ whiteSpace: 'nowrap' }}
              >
                {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo phiếu xuất kho mới'}
              </button>
            ) : undefined
          }
        />

        <div className="no-print">
          <OutboundPipelineStepper />
        </div>

        <div className="no-print">
          <UiTabs
            ariaLabel="Chọn phân khu quản lý phiếu xuất kho"
            activeTab={activeSectionTab}
            onTabChange={(nextTab) => {
              setActiveSectionTab(nextTab);
              if (nextTab === 'CREATE') {
                setShowCreateForm(true);
              }
            }}
            tabs={[
              { id: 'ALL', label: 'Tất cả phân khu' },
              { id: 'LIST', label: '1. Danh sách phiếu xuất' },
              { id: 'BATCH_AWB', label: '2. In gộp AWB & Bàn giao bưu tá' },
              { id: 'CREATE', label: '3. Tạo phiếu xuất mới' },
            ]}
          />
        </div>

        {error && <p role="alert">{error}</p>}
        {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}
        {receipts.some(receipt => !receipt.writeEnabled) && (
          <div role="status" className="export-maintenance no-print">
            Luồng xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.
          </div>
        )}

        <ExportReceiptsKpiCard
          receipts={receipts}
          filteredCount={filteredReceipts.length}
          statusQuickFilter={statusQuickFilter}
          setStatusQuickFilter={setStatusQuickFilter}
          setReceiptPage={setReceiptPage}
          warehouseId={warehouseId}
          setError={setError}
        />

        {(activeSectionTab === 'ALL' || activeSectionTab === 'BATCH_AWB') && (
          <ExportReceiptsBatchAwbCard
            warehouseId={warehouseId}
            receipts={receipts}
            handoverTrackingInput={handoverTrackingInput}
            setHandoverTrackingInput={setHandoverTrackingInput}
            setError={setError}
          />
        )}

        {(activeSectionTab === 'ALL' || activeSectionTab === 'CREATE') && canCreate && (
          <ExportReceiptsCreateCard
            showCreateForm={showCreateForm}
            setShowCreateForm={setShowCreateForm}
            warehouseId={warehouseId}
            setWarehouseId={setWarehouseId}
            warehouses={warehouses}
            canReadPartners={canReadPartners}
            customers={customers}
            products={products}
            salesOrders={salesOrders}
            setError={setError}
            setSuccessMsg={setSuccessMsg}
            fetchReceipts={fetchReceipts}
          />
        )}

        {(activeSectionTab === 'ALL' || activeSectionTab === 'LIST') && (
          <ExportReceiptsListCard
            receipts={receipts}
            filteredReceipts={filteredReceipts}
            pagedReceipts={pagedReceipts}
            receiptPage={receiptPage}
            totalReceiptPages={totalReceiptPages}
            setReceiptPage={setReceiptPage}
            setStatusQuickFilter={setStatusQuickFilter}
            userId={userId}
            canApproveAndReserve={canApproveAndReserve}
            canDispatch={canDispatch}
            canCancel={canCancel}
            actionInFlight={actionInFlight}
            printLoadingId={printLoadingId}
            handleViewDetails={handleViewDetails}
            openPrintPreview={openPrintPreview}
            handleWorkflowAction={handleWorkflowAction}
            handleCancel={handleCancel}
          />
        )}

        {selectedReceipt && (
          <ExportReceiptDetailCard
            selectedReceipt={selectedReceipt}
            canUpdate={canUpdate}
            canReadPartners={canReadPartners}
            partnerUpdating={partnerUpdating}
            customers={customers}
            changeCustomer={changeCustomer}
            onClose={() => setSelectedReceipt(null)}
          />
        )}

        {printReceipt && printFetchedAt && (
          <ReceiptPrintPreview kind="export" receipt={printReceipt} fetchedAt={printFetchedAt} onClose={closePrintPreview} />
        )}
      </div>
    </UiPage>
  );
};

export default ExportReceipts;
