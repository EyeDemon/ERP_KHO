import React from 'react';
import { X } from 'lucide-react';
import apiClient from '../../services/apiClient';
import { hasPermission } from '../../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import { UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import { StockTransferOverviewCard } from './StockTransferSections';
import {
  statusLabel,
  statusNames,
  type InTransitItem,
  type Line,
  type NetworkRebalancingResult,
  type Product,
  type SealVerificationResult,
  type Transfer,
  type Warehouse,
} from './stockTransfersShared';

export interface StockTransferFilters {
  search: string;
  status: string;
  sourceWarehouseId: string;
  destinationWarehouseId: string;
}

export async function fetchInTransitTransfers(filters: Pick<StockTransferFilters, 'sourceWarehouseId' | 'destinationWarehouseId'>): Promise<InTransitItem[]> {
  const params: Record<string, string> = {};
  if (filters.sourceWarehouseId) params.sourceWarehouseId = filters.sourceWarehouseId;
  if (filters.destinationWarehouseId) params.destinationWarehouseId = filters.destinationWarehouseId;
  const res = await apiClient.get('/api/v1/stock-transfers/in-transit', { params });
  return Array.isArray(res.data?.items) ? (res.data.items as InTransitItem[]) : [];
}

export async function submitTransferDraft(params: {
  editId: number | null;
  editingRevision: number | null;
  sourceId: number | '';
  destinationId: number | '';
  note: string;
  lines: Line[];
  internalTransferOrderNo: string;
  transporterName: string;
  vehiclePlateNumber: string;
}) {
  const payload = {
    sourceWarehouseId: params.sourceId,
    destinationWarehouseId: params.destinationId,
    note: params.note,
    details: params.lines,
    ...(params.internalTransferOrderNo.trim() ? { internalTransferOrderNo: params.internalTransferOrderNo.trim() } : {}),
    ...(params.transporterName.trim() ? { transporterName: params.transporterName.trim() } : {}),
    ...(params.vehiclePlateNumber.trim() ? { vehiclePlateNumber: params.vehiclePlateNumber.trim() } : {}),
    ...(params.editId === null ? {} : { expectedDraftRevision: params.editingRevision }),
  };
  const action = `transfer-${params.editId === null ? 'create' : 'update:' + params.editId}:${JSON.stringify(payload)}`;
  try {
    if (params.editId === null) {
      await apiClient.post('/api/v1/stock-transfers', payload, { headers: idempotencyHeaders(action) });
    } else {
      await apiClient.put(`/api/v1/stock-transfers/${params.editId}`, payload, { headers: idempotencyHeaders(action) });
    }
    completeIdempotentAction(action);
  } catch (failure: any) {
    if (params.editId !== null && failure.response?.status === 409) {
      completeIdempotentAction(action);
    }
    throw failure;
  }
}

export async function postTransferCommand(transferId: number, action: string, body?: unknown) {
  const logicalAction = `transfer-${action}:${transferId}${body === undefined ? '' : `:${JSON.stringify(body)}`}`;
  const response = await apiClient.post(`/api/v1/stock-transfers/${transferId}/${action}`, body, {
    headers: idempotencyHeaders(logicalAction),
  });
  completeIdempotentAction(logicalAction);
  return response.data;
}

export async function fetchStockTransferPageData(filters: StockTransferFilters, requestedPage: number) {
  const params = { ...filters, pageIndex: requestedPage, pageSize: 15 };
  const hasExplicitPermissions = typeof localStorage !== 'undefined' && localStorage.getItem('permissions') !== null;
  const canReadWarehouses = !hasExplicitPermissions || hasPermission('warehouse.read');
  const canReadProducts = !hasExplicitPermissions || hasPermission('product.read');
  let refLoadError = '';
  const [transfers, warehouseResult, productResult] = await Promise.all([
    apiClient.get('/api/v1/stock-transfers', { params }),
    canReadWarehouses
      ? apiClient.get('/api/v1/warehouses').catch((err: any) => {
          if (err?.response?.status && err.response.status >= 400) {
            refLoadError = err.response?.data?.message || 'Không thể tải danh sách kho.';
          }
          return { data: [] };
        })
      : Promise.resolve({ data: [] }),
    canReadProducts
      ? apiClient.get('/api/v1/products').catch((err: any) => {
          if (err?.response?.status && err.response.status >= 400) {
            refLoadError = refLoadError || err.response?.data?.message || 'Không thể tải danh sách sản phẩm.';
          }
          return { data: [] };
        })
      : Promise.resolve({ data: [] }),
  ]);
  return {
    items: transfers.data.items as Transfer[],
    totalPages: (transfers.data.totalPages || 1) as number,
    warehouses: (Array.isArray(warehouseResult.data) ? warehouseResult.data : []) as Warehouse[],
    products: (Array.isArray(productResult.data) ? productResult.data : []) as Product[],
    refLoadError,
  };
}

interface StockTransferFilterToolbarProps {
  error: string;
  showCreate: boolean;
  hasSelected: boolean;
  onClearError: () => void;
  items: Transfer[];
  actionInFlight: boolean;
  createInFlight: boolean;
  governanceBusy: boolean;
  sealVerification: SealVerificationResult | null;
  networkRebalancing: NetworkRebalancingResult | null;
  onVerifySeal: () => void;
  onSimulateRebalancing: () => void;
  filters: StockTransferFilters;
  setFilters: React.Dispatch<React.SetStateAction<StockTransferFilters>>;
  warehouses: Warehouse[];
  showInTransit: boolean;
  onApplyFilters: () => void;
  onToggleInTransit: () => void;
}

export const StockTransferFilterToolbar: React.FC<StockTransferFilterToolbarProps> = ({
  error,
  showCreate,
  hasSelected,
  onClearError,
  items,
  actionInFlight,
  createInFlight,
  governanceBusy,
  sealVerification,
  networkRebalancing,
  onVerifySeal,
  onSimulateRebalancing,
  filters,
  setFilters,
  warehouses,
  showInTransit,
  onApplyFilters,
  onToggleInTransit,
}) => (
  <>
    {error && !showCreate && !hasSelected && (
      <div className="transfer-error" role="alert">
        <span>{error}</span>
        <button type="button" aria-label="Đóng thông báo lỗi" onClick={onClearError}>
          <X size={16} aria-hidden="true" />
        </button>
      </div>
    )}

    <StockTransferOverviewCard
      items={items}
      actionInFlight={actionInFlight}
      createInFlight={createInFlight}
      governanceBusy={governanceBusy}
      sealVerification={sealVerification}
      networkRebalancing={networkRebalancing}
      onVerifySeal={onVerifySeal}
      onSimulateRebalancing={onSimulateRebalancing}
    />

    <UiToolbar>
      <UiToolbarField label="Mã phiếu">
        <input
          aria-label="Tìm mã phiếu điều chuyển"
          placeholder="Tìm mã phiếu"
          value={filters.search}
          onChange={(e) => setFilters({ ...filters, search: e.target.value })}
        />
      </UiToolbarField>
      <UiToolbarField label="Trạng thái">
        <select
          aria-label="Lọc trạng thái điều chuyển"
          value={filters.status}
          onChange={(e) => setFilters({ ...filters, status: e.target.value })}
        >
          <option value="">Mọi trạng thái</option>
          {statusNames.map((name, index) => (
            <option key={name} value={index}>
              {statusLabel[name]}
            </option>
          ))}
        </select>
      </UiToolbarField>
      <UiToolbarField label="Kho nguồn">
        <select
          aria-label="Lọc kho nguồn"
          value={filters.sourceWarehouseId}
          onChange={(e) => setFilters({ ...filters, sourceWarehouseId: e.target.value })}
        >
          <option value="">Mọi kho nguồn</option>
          {warehouses.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>
      </UiToolbarField>
      <UiToolbarField label="Kho đích">
        <select
          aria-label="Lọc kho đích"
          value={filters.destinationWarehouseId}
          onChange={(e) => setFilters({ ...filters, destinationWarehouseId: e.target.value })}
        >
          <option value="">Mọi kho đích</option>
          {warehouses.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>
      </UiToolbarField>
      <button type="button" onClick={onApplyFilters}>Lọc</button>
      <button type="button" onClick={onToggleInTransit}>
        {showInTransit ? 'Ẩn tồn đang vận chuyển' : 'Tồn đang vận chuyển'}
      </button>
    </UiToolbar>
  </>
);
