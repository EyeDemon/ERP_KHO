import { useEffect, useRef, useState } from 'react';
import { Plus } from 'lucide-react';
import apiClient from '../services/apiClient';
import { currentRole, currentUserId, usePermission } from '../services/authorization';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import './StockTransfers.css';
import StockTransferDetailsDialog from './StockTransferDetailsDialog';
import {
  StockTransferDraftModal,
  StockTransferInTransitCard,
  StockTransferListCard,
} from '../modules/inventory/StockTransferSections';
import {
  fetchInTransitTransfers,
  fetchStockTransferPageData,
  postTransferCommand,
  StockTransferFilterToolbar,
  submitTransferDraft,
  type StockTransferFilters,
} from '../modules/inventory/StockTransferFilterToolbar';
import {
  executeSimulateNetworkRebalancing,
  executeVerifyInTransitSeal,
  normalizeStatus,
  statusLabel,
  statusNames,
  statusTone,
  useDraftModalFocusTrap,
  validateTransferDraft,
  validateTransferReceive,
  type InTransitItem,
  type Line,
  type NetworkRebalancingResult,
  type Product,
  type SealVerificationResult,
  type Transfer,
  type Warehouse,
} from '../modules/inventory/stockTransfersShared';

export default function StockTransfers() {
  const role = currentRole();
  const hasCreatePermission = usePermission('stock_transfer.create');
  const hasApprovePermission = usePermission('stock_transfer.approve');
  const canWrite = hasCreatePermission || ['Admin', 'Manager', 'WarehouseStaff'].includes(role);
  const canApprove = hasApprovePermission || ['Admin', 'Manager'].includes(role);
  const canReturn = usePermission('inventory_reversal.create');
  const userId = currentUserId();

  const [items, setItems] = useState<Transfer[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Transfer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editingRevision, setEditingRevision] = useState<number | null>(null);
  const [draftConflict, setDraftConflict] = useState(false);
  const createButtonRef = useRef<HTMLButtonElement>(null);
  const draftDialogRef = useRef<HTMLFormElement>(null);
  const [sourceId, setSourceId] = useState<number | ''>('');
  const [destinationId, setDestinationId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [internalTransferOrderNo, setInternalTransferOrderNo] = useState('');
  const [transporterName, setTransporterName] = useState('');
  const [vehiclePlateNumber, setVehiclePlateNumber] = useState('');
  const [lines, setLines] = useState<Line[]>([{ productId: '', quantity: '', note: '' }]);
  const [filters, setFilters] = useState<StockTransferFilters>({ search: '', status: '', sourceWarehouseId: '', destinationWarehouseId: '' });
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [receive, setReceive] = useState<Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>>({});
  const [actionInFlight, setActionInFlight] = useState(false);
  const [createInFlight, setCreateInFlight] = useState(false);
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [showInTransit, setShowInTransit] = useState(false);
  const [inTransitItems, setInTransitItems] = useState<InTransitItem[]>([]);
  const [inTransitLoading, setInTransitLoading] = useState(false);
  const [sealVerification, setSealVerification] = useState<SealVerificationResult | null>(null);
  const [networkRebalancing, setNetworkRebalancing] = useState<NetworkRebalancingResult | null>(null);
  const actionInFlightRef = useRef(false);
  const createInFlightRef = useRef(false);
  const listRequestSequence = useRef(0);
  const detailRequestSequence = useRef(0);

  const loadInTransit = async () => {
    setInTransitLoading(true);
    try {
      setInTransitItems(await fetchInTransitTransfers(filters));
    } catch (failure: any) {
      setInTransitItems([]);
      setError(failure?.response?.data?.message || 'Không thể tải dữ liệu tồn kho đang vận chuyển.');
    } finally {
      setInTransitLoading(false);
    }
  };

  const toggleInTransit = () => {
    const next = !showInTransit;
    setShowInTransit(next);
    if (next) void loadInTransit();
  };

  const load = async (requestedPage = page) => {
    const requestSequence = ++listRequestSequence.current;
    setLoading(true);
    setError('');
    try {
      const data = await fetchStockTransferPageData(filters, requestedPage);
      if (requestSequence !== listRequestSequence.current) return;
      setItems(data.items);
      setTotalPages(data.totalPages);
      setWarehouses(data.warehouses);
      setProducts(data.products);
      if (data.refLoadError) setError(data.refLoadError);
    } catch (failure: any) {
      if (requestSequence === listRequestSequence.current) {
        setError(failure.response?.data?.message || 'Không thể tải dữ liệu điều chuyển.');
      }
    } finally {
      if (requestSequence === listRequestSequence.current) setLoading(false);
    }
  };

  // oxlint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void load(page);
  }, [page]);

  const applyFilters = () => {
    if (showInTransit) void loadInTransit();
    if (page === 1) void load(1);
    else setPage(1);
  };

  const showTransferDetail = (transfer: Transfer) => {
    setSelected(transfer);
    setReceive(
      Object.fromEntries(
        transfer.details.map((line) => [
          line.productId,
          { receivedQuantity: line.dispatchedQuantity, missingQuantity: 0, damagedQuantity: 0 },
        ]),
      ),
    );
  };

  const openDetail = async (id: number) => {
    const requestSequence = ++detailRequestSequence.current;
    try {
      const response = await apiClient.get(`/api/v1/stock-transfers/${id}`);
      if (requestSequence !== detailRequestSequence.current) return;
      showTransferDetail(response.data as Transfer);
    } catch (failure: any) {
      if (requestSequence === detailRequestSequence.current) {
        setError(failure.response?.data?.message || 'Không thể tải chi tiết phiếu.');
      }
    }
  };

  const openNewDraft = () => {
    setEditingId(null);
    setEditingRevision(null);
    setDraftConflict(false);
    setSourceId('');
    setDestinationId('');
    setNote('');
    setLines([{ productId: '', quantity: '', note: '' }]);
    setError('');
    setShowCreate(true);
  };

  const openDraftEditor = (transfer: Omit<Transfer, 'draftRevision'> & { draftRevision?: number }) => {
    if (!canWrite || normalizeStatus(transfer.status) !== 'Draft' || transfer.reverseOfTransferId) return;
    if (!Number.isSafeInteger(transfer.draftRevision) || !transfer.draftRevision || transfer.draftRevision < 1) {
      setError('Phiếu nháp thiếu phiên bản hợp lệ. Không thể lưu; hãy tải lại chi tiết phiếu.');
      return;
    }
    detailRequestSequence.current += 1;
    setEditingId(transfer.id);
    setEditingRevision(transfer.draftRevision);
    setDraftConflict(false);
    setSourceId(transfer.sourceWarehouseId);
    setDestinationId(transfer.destinationWarehouseId);
    setNote(transfer.note ?? '');
    setLines(transfer.details.map((line) => ({ productId: line.productId, quantity: line.requestedQuantity, note: line.note ?? '' })));
    setError('');
    setSelected(null);
    setShowCreate(true);
  };

  const closeDraftEditor = () => {
    if (createInFlightRef.current) return;
    setShowCreate(false);
    setEditingId(null);
    setEditingRevision(null);
    setDraftConflict(false);
    setError('');
    createButtonRef.current?.focus();
  };

  useDraftModalFocusTrap(showCreate, draftDialogRef, createInFlightRef, closeDraftEditor);

  const reloadConflictedDraft = async () => {
    if (editingId === null || createInFlightRef.current) return;
    const id = editingId;
    createInFlightRef.current = true;
    setCreateInFlight(true);
    try {
      const response = await apiClient.get<Transfer>(`/api/v1/stock-transfers/${id}`);
      const latest = response.data;
      if (normalizeStatus(latest.status) === 'Draft' && !latest.reverseOfTransferId) {
        openDraftEditor(latest);
      } else {
        detailRequestSequence.current += 1;
        setShowCreate(false);
        setEditingId(null);
        setEditingRevision(null);
        setDraftConflict(false);
        setError('');
        showTransferDetail(latest);
        void load();
      }
    } catch (failure: any) {
      setError(failure.response?.data?.message || 'Không thể tải lại phiếu. Hãy kiểm tra kết nối và thử lại.');
    } finally {
      createInFlightRef.current = false;
      setCreateInFlight(false);
    }
  };

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    if (createInFlightRef.current || (editingId !== null && draftConflict)) return;
    const validationErr = validateTransferDraft({ editingId, editingRevision, sourceId, destinationId, warehouses, products, lines, note });
    if (validationErr) return setError(validationErr);

    const editId = editingId;
    createInFlightRef.current = true;
    setCreateInFlight(true);
    try {
      await submitTransferDraft({
        editId,
        editingRevision,
        sourceId,
        destinationId,
        note,
        lines,
        internalTransferOrderNo,
        transporterName,
        vehiclePlateNumber,
      });
      setShowCreate(false);
      setEditingId(null);
      setEditingRevision(null);
      setDraftConflict(false);
      createButtonRef.current?.focus();
      setLines([{ productId: '', quantity: '', note: '' }]);
      setSourceId('');
      setDestinationId('');
      setNote('');
      await load();
      if (editId !== null) await openDetail(editId);
    } catch (failure: any) {
      if (editId !== null && failure.response?.status === 409) {
        setDraftConflict(true);
      }
      setError(
        failure.response?.data?.message ||
          (editingId === null ? 'Không thể tạo phiếu.' : 'Không thể cập nhật phiếu; hãy kiểm tra trạng thái mới nhất.'),
      );
    } finally {
      createInFlightRef.current = false;
      setCreateInFlight(false);
    }
  };

  const act = async (action: string, body?: unknown) => {
    if (!selected || actionInFlightRef.current) return;
    const label = action === 'return' ? 'hoàn trả về kho nguồn' : action === 'reverse-draft' ? 'lập phiếu điều chuyển ngược' : statusLabel[action] || action;
    if (!window.confirm(`Xác nhận thao tác ${label}?`)) return;

    actionInFlightRef.current = true;
    setActionInFlight(true);
    try {
      const data = await postTransferCommand(selected.id, action, body);
      await openDetail(action === 'reverse-draft' ? data.id : selected.id);
      await load();
    } catch (failure: any) {
      setError(failure.response?.data?.message || 'Thao tác không thành công.');
    } finally {
      actionInFlightRef.current = false;
      setActionInFlight(false);
    }
  };

  const submitReceive = async (): Promise<void> => {
    if (!selected || actionInFlightRef.current) return;
    const validationErr = validateTransferReceive(selected, receive);
    if (validationErr) return setError(validationErr);
    setError('');
    await act('receive', { details: selected.details.map((line) => ({ productId: line.productId, ...receive[line.productId] })) });
  };

  const handleVerifySeal = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true); setError('');
    try { setSealVerification(await executeVerifyInTransitSeal(items)); }
    catch (err: any) { setSealVerification(null); setError(err?.response?.data?.message || err?.message || 'Không thể xác minh niêm phong HU và đối chiếu hàng đang đi đường.'); }
    finally { setGovernanceBusy(false); }
  };

  const handleSimulateRebalancing = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true); setError('');
    try { setNetworkRebalancing(await executeSimulateNetworkRebalancing(filters)); }
    catch (err: any) { setNetworkRebalancing(null); setError(err?.response?.data?.message || err?.message || 'Không thể mô phỏng tái cân bằng tồn kho mạng lưới.'); }
    finally { setGovernanceBusy(false); }
  };

  const status = selected ? normalizeStatus(selected.status) : '';

  return (
    <UiPage>
      <div className="transfer-page">
        <UiPageHeader
          eyebrow="Kiểm soát tồn kho"
          title="Điều chuyển kho"
          description="Theo dõi hàng từ kho nguồn, duyệt xuất, trạng thái đang vận chuyển đến khi kho đích xác nhận và hoàn tất."
          actions={canWrite ? <button type="button" ref={createButtonRef} className="ui-primary-button" onClick={openNewDraft}><Plus size={17} aria-hidden="true" /> Tạo phiếu</button> : undefined}
        />

        <StockTransferFilterToolbar
          error={error} showCreate={showCreate} hasSelected={Boolean(selected)} onClearError={() => setError('')}
          items={items} actionInFlight={actionInFlight} createInFlight={createInFlight} governanceBusy={governanceBusy}
          sealVerification={sealVerification} networkRebalancing={networkRebalancing}
          onVerifySeal={() => void handleVerifySeal()} onSimulateRebalancing={() => void handleSimulateRebalancing()}
          filters={filters} setFilters={setFilters} warehouses={warehouses} showInTransit={showInTransit}
          onApplyFilters={applyFilters} onToggleInTransit={toggleInTransit}
        />

        {showInTransit && (
          <StockTransferInTransitCard inTransitLoading={inTransitLoading} inTransitItems={inTransitItems} onOpenDetail={(id) => void openDetail(id)} />
        )}

        <StockTransferListCard loading={loading} items={items} page={page} totalPages={totalPages} onSetPage={setPage} onOpenDetail={(id) => void openDetail(id)} />

        {showCreate && (
          <StockTransferDraftModal
            draftDialogRef={draftDialogRef} editingId={editingId} editingRevision={editingRevision}
            draftConflict={draftConflict} createInFlight={createInFlight} error={error}
            sourceId={sourceId} setSourceId={setSourceId} destinationId={destinationId} setDestinationId={setDestinationId}
            note={note} setNote={setNote}
            internalTransferOrderNo={internalTransferOrderNo} setInternalTransferOrderNo={setInternalTransferOrderNo}
            transporterName={transporterName} setTransporterName={setTransporterName}
            vehiclePlateNumber={vehiclePlateNumber} setVehiclePlateNumber={setVehiclePlateNumber}
            lines={lines} setLines={setLines} warehouses={warehouses} products={products}
            onCloseDraftEditor={closeDraftEditor} onReloadConflictedDraft={() => void reloadConflictedDraft()}
            onSubmitCreate={(event) => void create(event)}
          />
        )}

        {selected && (
          <StockTransferDetailsDialog
            key={selected.id} selected={selected} error={error} status={status}
            statusNames={statusNames} statusLabel={statusLabel} statusTone={statusTone}
            canWrite={canWrite} canReturn={canReturn} canApprove={canApprove}
            userId={userId} actionInFlight={actionInFlight} receive={receive} setReceive={setReceive}
            onClose={() => { detailRequestSequence.current += 1; setSelected(null); setError(''); }}
            onEdit={openDraftEditor} onAction={act} onReceive={submitReceive} onOpenTransfer={openDetail}
          />
        )}
      </div>
    </UiPage>
  );
}
