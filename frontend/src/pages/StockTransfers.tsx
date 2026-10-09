import { useEffect, useRef, useState } from 'react';
import { ArrowRight, Plus, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { currentUserId, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import './StockTransfers.css';
import StockTransferDetailsDialog from './StockTransferDetailsDialog';

type Warehouse = { id: number; name: string };
type Product = { id: number; code: string; name: string };
type Line = { productId: number | ''; quantity: number | ''; note: string };
type TransferLine = {
  productId: number;
  productCode: string;
  productName: string;
  requestedQuantity: number;
  dispatchedQuantity: number;
  receivedQuantity: number;
  missingQuantity: number;
  damagedQuantity: number;
  inTransitQuantity: number;
  note?: string;
};
type Transfer = {
  id: number;
  code: string;
  sourceWarehouseId: number;
  sourceWarehouseName: string;
  destinationWarehouseId: number;
  destinationWarehouseName: string;
  status: string | number;
  note?: string;
  createdBy: number;
  createdAt: string;
  approvedAt?: string;
  dispatchedAt?: string;
  receivedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  details: TransferLine[];
};

const statusNames = ['Draft', 'Approved', 'InTransit', 'Received', 'Completed', 'Cancelled', 'Returned'];
const statusLabel: Record<string, string> = {
  Draft: 'Nháp',
  Approved: 'Đã duyệt',
  InTransit: 'Đang vận chuyển',
  Received: 'Đã nhận',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
  Returned: 'Đã hoàn trả',
};
const normalizeStatus = (status: string | number) =>
  typeof status === 'number' ? statusNames[status] : status;
const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
  if (status === 'Completed' || status === 'Received') return 'success';
  if (status === 'InTransit' || status === 'Approved') return 'warning';
  if (status === 'Cancelled') return 'danger';
  if (status === 'Returned') return 'warning';
  return 'neutral';
};

export default function StockTransfers() {
  const role = localStorage.getItem('role') || 'Viewer';
  const canWrite = ['Admin', 'Manager', 'WarehouseStaff'].includes(role);
  const canApprove = ['Admin', 'Manager'].includes(role);
  const canReturn = usePermission('inventory_reversal.create');
  const userId = currentUserId();

  const [items, setItems] = useState<Transfer[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Transfer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [sourceId, setSourceId] = useState<number | ''>('');
  const [destinationId, setDestinationId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<Line[]>([{ productId: '', quantity: '', note: '' }]);
  const [filters, setFilters] = useState({
    search: '',
    status: '',
    sourceWarehouseId: '',
    destinationWarehouseId: '',
  });
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [receive, setReceive] = useState<
    Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>
  >({});
  const [actionInFlight, setActionInFlight] = useState(false);
  const [createInFlight, setCreateInFlight] = useState(false);
  const actionInFlightRef = useRef(false);
  const createInFlightRef = useRef(false);
  const listRequestSequence = useRef(0);

  const load = async (requestedPage = page) => {
    const requestSequence = ++listRequestSequence.current;
    setLoading(true);
    setError('');
    try {
      const params = { ...filters, pageIndex: requestedPage, pageSize: 15 };
      const [transfers, warehouseResult, productResult] = await Promise.all([
        apiClient.get('/api/stock-transfers', { params }),
        apiClient.get('/api/warehouses'),
        apiClient.get('/api/products'),
      ]);
      if (requestSequence !== listRequestSequence.current) return;
      setItems(transfers.data.items);
      setTotalPages(transfers.data.totalPages || 1);
      setWarehouses(warehouseResult.data);
      setProducts(productResult.data);
    } catch (failure: any) {
      if (requestSequence === listRequestSequence.current) {
        setError(failure.response?.data?.message || 'Không thể tải dữ liệu điều chuyển.');
      }
    } finally {
      if (requestSequence === listRequestSequence.current) setLoading(false);
    }
  };

  // Filters are applied explicitly; changing page re-runs the current query.
  // oxlint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void load(page);
  }, [page]);

  const applyFilters = () => {
    if (page === 1) void load(1);
    else setPage(1);
  };

  const openDetail = async (id: number) => {
    try {
      const response = await apiClient.get(`/api/stock-transfers/${id}`);
      setSelected(response.data);
      setReceive(
        Object.fromEntries(
          response.data.details.map((line: TransferLine) => [
            line.productId,
            {
              receivedQuantity: line.dispatchedQuantity,
              missingQuantity: 0,
              damagedQuantity: 0,
            },
          ])
        )
      );
    } catch (failure: any) {
      setError(failure.response?.data?.message || 'Không thể tải chi tiết phiếu.');
    }
  };

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    if (createInFlightRef.current) return;
    if (sourceId === destinationId) return setError('Kho nguồn và kho đích phải khác nhau.');
    if (lines.some(line => !line.productId || Number(line.quantity) <= 0)) {
      return setError('Mỗi dòng phải có sản phẩm và số lượng lớn hơn 0.');
    }
    if (new Set(lines.map(line => line.productId)).size !== lines.length) {
      return setError('Sản phẩm không được trùng dòng.');
    }

    createInFlightRef.current = true;
    setCreateInFlight(true);
    try {
      const payload = {
        sourceWarehouseId: sourceId,
        destinationWarehouseId: destinationId,
        note,
        details: lines,
      };
      const action = `transfer-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/stock-transfers', payload, {
        headers: idempotencyHeaders(action),
      });
      completeIdempotentAction(action);
      setShowCreate(false);
      setLines([{ productId: '', quantity: '', note: '' }]);
      setSourceId('');
      setDestinationId('');
      setNote('');
      await load();
    } catch (failure: any) {
      setError(failure.response?.data?.message || 'Không thể tạo phiếu.');
    } finally {
      createInFlightRef.current = false;
      setCreateInFlight(false);
    }
  };

  const act = async (action: string, body?: unknown) => {
    if (!selected || actionInFlightRef.current) return;
    if (!window.confirm(`Xác nhận thao tác ${action === 'return' ? 'hoàn trả về kho nguồn' : action === 'reverse-draft' ? 'lập phiếu điều chuyển ngược' : statusLabel[action] || action}?`)) return;

    actionInFlightRef.current = true;
    setActionInFlight(true);
    // Khóa idempotency gắn với toàn bộ payload, đặc biệt là số lượng thực nhận.
    const logicalAction = `transfer-${action}:${selected.id}${body === undefined ? '' : `:${JSON.stringify(body)}`}`;
    try {
      const response = await apiClient.post(`/api/stock-transfers/${selected.id}/${action}`, body, {
        headers: idempotencyHeaders(logicalAction),
      });
      completeIdempotentAction(logicalAction);
      await openDetail(action === 'reverse-draft' ? response.data.id : selected.id);
      await load();
    } catch (failure: any) {
      setError(failure.response?.data?.message || 'Thao tác không thành công.');
    } finally {
      actionInFlightRef.current = false;
      setActionInFlight(false);
    }
  };

  const submitReceive = () =>
    act('receive', {
      details: selected?.details.map(line => ({
        productId: line.productId,
        ...receive[line.productId],
      })),
    });

  const status = selected ? normalizeStatus(selected.status) : '';

  return (
    <UiPage>
      <div className="transfer-page">
        <UiPageHeader
          eyebrow="Inventory Control"
          title="Điều chuyển kho"
          description="Theo dõi hàng từ kho nguồn, duyệt xuất, trạng thái đang vận chuyển đến khi kho đích xác nhận và hoàn tất."
          actions={
            canWrite ? (
              <button type="button" className="ui-primary-button" onClick={() => setShowCreate(true)}>
                <Plus size={17} aria-hidden="true" /> Tạo phiếu
              </button>
            ) : undefined
          }
        />

        {error && (
          <div className="transfer-error" role="alert">
            <span>{error}</span>
            <button type="button" aria-label="Đóng thông báo lỗi" onClick={() => setError('')}>
              <X size={16} aria-hidden="true" />
            </button>
          </div>
        )}

        <UiToolbar>
          <UiToolbarField label="Mã phiếu">
            <input
              aria-label="Tìm mã phiếu điều chuyển"
              placeholder="Tìm mã phiếu"
              value={filters.search}
              onChange={event => setFilters({ ...filters, search: event.target.value })}
            />
          </UiToolbarField>
          <UiToolbarField label="Trạng thái">
            <select
              aria-label="Lọc trạng thái điều chuyển"
              value={filters.status}
              onChange={event => setFilters({ ...filters, status: event.target.value })}
            >
              <option value="">Mọi trạng thái</option>
              {statusNames.map((name, index) => (
                <option key={name} value={index}>{statusLabel[name]}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Kho nguồn">
            <select
              aria-label="Lọc kho nguồn"
              value={filters.sourceWarehouseId}
              onChange={event => setFilters({ ...filters, sourceWarehouseId: event.target.value })}
            >
              <option value="">Mọi kho nguồn</option>
              {warehouses.map(warehouse => (
                <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Kho đích">
            <select
              aria-label="Lọc kho đích"
              value={filters.destinationWarehouseId}
              onChange={event => setFilters({ ...filters, destinationWarehouseId: event.target.value })}
            >
              <option value="">Mọi kho đích</option>
              {warehouses.map(warehouse => (
                <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <button type="button" onClick={applyFilters}>Lọc</button>
        </UiToolbar>

        <UiCard title="Danh sách phiếu điều chuyển">
          {loading ? (
            <p role="status">Đang tải...</p>
          ) : items.length === 0 ? (
            <UiEmptyState title="Chưa có phiếu điều chuyển phù hợp." />
          ) : (
            <UiTableScroll>
              <table aria-label="Danh sách phiếu điều chuyển">
                <thead>
                  <tr>
                    <th>Mã phiếu</th>
                    <th>Luồng kho</th>
                    <th>Trạng thái</th>
                    <th>Ngày tạo</th>
                    <th>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map(item => {
                    const normalized = normalizeStatus(item.status);
                    return (
                      <tr key={item.id}>
                        <td><strong>{item.code}</strong></td>
                        <td>
                          <span className="transfer-flow">
                            {item.sourceWarehouseName}
                            <ArrowRight size={14} aria-hidden="true" />
                            {item.destinationWarehouseName}
                          </span>
                        </td>
                        <td><UiBadge tone={statusTone(normalized)}>{statusLabel[normalized]}</UiBadge></td>
                        <td>{new Date(item.createdAt).toLocaleString('vi-VN')}</td>
                        <td>
                          <button
                            type="button"
                            aria-label={`Xem chi tiết ${item.code}`}
                            onClick={() => void openDetail(item.id)}
                          >
                            Xem chi tiết
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </UiTableScroll>
          )}

          <div className="ui-pagination" aria-label="Phân trang điều chuyển">
            <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Trước</button>
            <span>{page} / {totalPages}</span>
            <button type="button" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Sau</button>
          </div>
        </UiCard>

        {showCreate && (
          <div className="transfer-modal" role="presentation">
            <form
              className="transfer-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="transfer-create-title"
              onSubmit={create}
            >
              <div className="dialog-title">
                <h2 id="transfer-create-title">Tạo phiếu điều chuyển</h2>
                <button type="button" aria-label="Đóng tạo phiếu" disabled={createInFlight} onClick={() => setShowCreate(false)}>
                  <X aria-hidden="true" />
                </button>
              </div>

              <div className="warehouse-pair">
                <label>
                  Kho nguồn
                  <select
                    aria-label="Kho nguồn"
                    required
                    value={sourceId}
                    onChange={event => setSourceId(Number(event.target.value) || '')}
                  >
                    <option value="">Chọn kho</option>
                    {warehouses.map(warehouse => (
                      <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
                    ))}
                  </select>
                </label>
                <ArrowRight aria-hidden="true" />
                <label>
                  Kho đích
                  <select
                    aria-label="Kho đích"
                    required
                    value={destinationId}
                    onChange={event => setDestinationId(Number(event.target.value) || '')}
                  >
                    <option value="">Chọn kho</option>
                    {warehouses
                      .filter(warehouse => warehouse.id !== sourceId)
                      .map(warehouse => (
                        <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
                      ))}
                  </select>
                </label>
              </div>

              <label>
                Ghi chú
                <textarea aria-label="Ghi chú điều chuyển" value={note} onChange={event => setNote(event.target.value)} />
              </label>

              <div className="line-header">
                <h3>Sản phẩm</h3>
                <button
                  type="button"
                  onClick={() => setLines([...lines, { productId: '', quantity: '', note: '' }])}
                >
                  <Plus size={16} aria-hidden="true" /> Thêm dòng
                </button>
              </div>

              {lines.map((line, index) => (
                <div className="transfer-line" key={index}>
                  <select
                    aria-label={`Sản phẩm dòng ${index + 1}`}
                    required
                    value={line.productId}
                    onChange={event => {
                      const next = [...lines];
                      next[index].productId = Number(event.target.value) || '';
                      setLines(next);
                    }}
                  >
                    <option value="">Chọn sản phẩm</option>
                    {products.map(product => (
                      <option key={product.id} value={product.id}>{product.code} - {product.name}</option>
                    ))}
                  </select>
                  <input
                    aria-label={`Số lượng dòng ${index + 1}`}
                    required
                    type="number"
                    min="0.0001"
                    step="0.0001"
                    placeholder="Số lượng"
                    value={line.quantity}
                    onChange={event => {
                      const next = [...lines];
                      next[index].quantity = Number(event.target.value) || '';
                      setLines(next);
                    }}
                  />
                  <button
                    type="button"
                    aria-label={`Xóa dòng ${index + 1}`}
                    onClick={() => setLines(lines.filter((_, rowIndex) => rowIndex !== index))}
                  >
                    <X size={17} aria-hidden="true" />
                  </button>
                </div>
              ))}

              <div className="dialog-actions">
                <button type="button" disabled={createInFlight} onClick={() => setShowCreate(false)}>Đóng</button>
                <button className="ui-primary-button" type="submit" disabled={createInFlight}>
                  {createInFlight ? 'Đang tạo phiếu...' : 'Tạo phiếu'}
                </button>
              </div>
            </form>
          </div>
        )}

        {selected && (
          <StockTransferDetailsDialog
            key={selected.id}
            selected={selected}
            status={status}
            statusNames={statusNames}
            statusLabel={statusLabel}
            statusTone={statusTone}
            canWrite={canWrite}
            canReturn={canReturn}
            canApprove={canApprove}
            userId={userId}
            actionInFlight={actionInFlight}
            receive={receive}
            setReceive={setReceive}
            onClose={() => setSelected(null)}
            onAction={act}
            onReceive={submitReceive} onOpenTransfer={openDetail}
          />
        )}
      </div>
    </UiPage>
  );
}
