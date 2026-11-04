import { useEffect, type RefObject } from 'react';
import apiClient from '../../services/apiClient';

export type Warehouse = { id: number; name: string };
export type Product = { id: number; code: string; name: string };
export type Line = { productId: number | ''; quantity: number | ''; note: string };
export type TransferLine = {
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
export type Transfer = {
  id: number;
  code: string;
  sourceWarehouseId: number;
  sourceWarehouseName: string;
  destinationWarehouseId: number;
  destinationWarehouseName: string;
  status: string | number;
  draftRevision: number;
  note?: string;
  reverseOfTransferId?: number;
  createdBy: number;
  createdAt: string;
  approvedAt?: string;
  dispatchedAt?: string;
  receivedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  details: TransferLine[];
};
export type InTransitItem = {
  transferId: number;
  transferCode: string;
  sourceWarehouseId: number;
  sourceWarehouseName: string;
  destinationWarehouseId: number;
  destinationWarehouseName: string;
  productId: number;
  productCode: string;
  productName: string;
  unitName: string;
  dispatchedQuantity: number;
  inTransitQuantity: number;
  dispatchedAt?: string | null;
  hoursInTransit: number;
};

export type SealVerificationResult = {
  sealVerificationId: string;
  sealStatus: string;
  transferCode: string;
};

export type NetworkRebalancingResult = {
  simulationId: string;
  networkSurplusShortageStatus: string;
  interWarehouseReplenishment: string;
  replenishmentExceptionTriage: string;
  status: string;
};

export const statusNames = ['Draft', 'Approved', 'InTransit', 'Received', 'Completed', 'Cancelled', 'Returned'];
export const statusLabel: Record<string, string> = {
  Draft: 'Nháp',
  Approved: 'Đã duyệt',
  InTransit: 'Đang vận chuyển',
  Received: 'Đã nhận',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
  Returned: 'Đã hoàn trả',
};
export const transferSignalLabels: Record<string, string> = {
  SEAL_INTACT_VERIFIED: 'Đã xác minh niêm phong nguyên vẹn & khớp số dư đi đường',
  UNSEALED_HU_DETECTED: 'Phát hiện kiện hàng đang vận chuyển chưa niêm phong',
  'TRF-IN-TRANSIT': 'Lô điều chuyển đang vận chuyển',
  NETWORK_REBALANCING_SIMULATED: 'Đã mô phỏng tái cân bằng mạng lưới kho',
  SURPLUS_TO_SHORTAGE_LANES_MAPPED: 'Đã ánh xạ luồng điều chuyển từ kho dư sang kho thiếu',
  BALANCED_ACROSS_NODES: 'Đã cân bằng tồn kho giữa các điểm kho',
  RESPECTS_RESERVATION_AND_TRANSFER_SEMANTICS: 'Tuân thủ cam kết giữ hàng & nguyên tắc điều chuyển',
  AUTO_SUGGESTED_TRANSFERS_READY: 'Đề xuất bổ sung liên kho tự động sẵn sàng',
  ZERO_SILENT_STOCKOUT_CONFLICTS: 'Không phát sinh xung đột thiếu hụt tồn kho',
  ZERO_CRITICAL_SHORTAGES: 'Không có điểm kho thiếu hụt nghiêm trọng',
};
export const translateTransferSignal = (value?: string | null) => (value ? (transferSignalLabels[value] ?? value) : '');
export const normalizeStatus = (status: string | number) => (typeof status === 'number' ? statusNames[status] : status);
export const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
  if (status === 'Completed' || status === 'Received') return 'success';
  if (status === 'InTransit' || status === 'Approved') return 'warning';
  if (status === 'Cancelled') return 'danger';
  if (status === 'Returned') return 'warning';
  return 'neutral';
};
export const formatDateTimeVn = (value?: string | null) => {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

export const validateTransferDraft = (params: {
  editingId: number | null;
  editingRevision: number | null;
  sourceId: number | '';
  destinationId: number | '';
  warehouses: Warehouse[];
  products: Product[];
  lines: Line[];
  note: string;
}): string | null => {
  const { editingId, editingRevision, sourceId, destinationId, warehouses, products, lines, note } = params;
  if (editingId !== null && (!Number.isSafeInteger(editingRevision) || !editingRevision || editingRevision < 1)) {
    return 'Không có phiên bản nháp hợp lệ để lưu. Vui lòng tải lại phiếu.';
  }
  if (
    !sourceId ||
    !destinationId ||
    !warehouses.some((warehouse) => warehouse.id === sourceId) ||
    !warehouses.some((warehouse) => warehouse.id === destinationId)
  ) {
    return 'Phải chọn kho nguồn và kho đích hợp lệ.';
  }
  if (sourceId === destinationId) return 'Kho nguồn và kho đích phải khác nhau.';
  if (
    lines.length === 0 ||
    lines.some(
      (line) =>
        !line.productId ||
        !products.some((product) => product.id === line.productId) ||
        !Number.isFinite(Number(line.quantity)) ||
        Number(line.quantity) <= 0,
    )
  ) {
    return 'Mỗi dòng phải có sản phẩm và số lượng lớn hơn 0.';
  }
  if (
    lines.some(
      (line) =>
        Number(line.quantity) >= 100_000_000_000_000 ||
        Math.abs(Number(line.quantity) * 10_000 - Math.round(Number(line.quantity) * 10_000)) > 0.000001 ||
        line.note.length > 500,
    ) ||
    note.length > 500
  ) {
    return 'Số lượng tối đa 4 chữ số thập phân; ghi chú không quá 500 ký tự.';
  }
  if (new Set(lines.map((line) => line.productId)).size !== lines.length) {
    return 'Sản phẩm không được trùng dòng.';
  }
  return null;
};

export const validateTransferReceive = (
  selected: Transfer,
  receive: Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>,
): string | null => {
  const units = (quantity: number) => Math.round(quantity * 10_000);
  for (const line of selected.details) {
    const quantities = receive[line.productId];
    const parts = quantities && [quantities.receivedQuantity, quantities.missingQuantity, quantities.damagedQuantity];
    if (
      !parts ||
      parts.some(
        (quantity) => !Number.isFinite(quantity) || quantity < 0 || Math.abs(quantity * 10_000 - units(quantity)) > 0.000001,
      )
    ) {
      return `Sản phẩm ${line.productCode}: số lượng thực nhận, thiếu và hỏng phải không âm, tối đa 4 chữ số thập phân.`;
    }
    if (parts.reduce((total, quantity) => total + units(quantity), 0) !== units(line.dispatchedQuantity)) {
      return `Sản phẩm ${line.productCode}: tổng thực nhận, thiếu và hỏng phải bằng số lượng đã xuất (${line.dispatchedQuantity}).`;
    }
  }
  return null;
};

export const executeVerifyInTransitSeal = async (items: Transfer[]): Promise<SealVerificationResult> => {
  const defaultCode = items.find((x) => normalizeStatus(x.status) === 'InTransit')?.code || items[0]?.code || 'TRF-IN-TRANSIT';
  const res = await apiClient.post('/api/v1/handling-units/verify-intransit-seal', { transferCode: defaultCode });
  if (res?.data?.sealVerificationId && res?.data?.sealStatus) {
    return {
      sealVerificationId: String(res.data.sealVerificationId),
      sealStatus: translateTransferSignal(String(res.data.sealStatus)),
      transferCode: translateTransferSignal(String(res.data.transferCode || defaultCode)),
    };
  }
  throw new Error('Phản hồi xác minh niêm phong HU không hợp lệ từ máy chủ.');
};

export const executeSimulateNetworkRebalancing = async (filters: {
  sourceWarehouseId: string;
  destinationWarehouseId: string;
}): Promise<NetworkRebalancingResult> => {
  const res = await apiClient.post('/api/v1/stock-transfers/simulate-network-rebalancing', {
    sourceWarehouseId: filters.sourceWarehouseId ? Number(filters.sourceWarehouseId) : undefined,
    destinationWarehouseId: filters.destinationWarehouseId ? Number(filters.destinationWarehouseId) : undefined,
  });
  if (res?.data?.simulationId && res?.data?.status) {
    return {
      simulationId: String(res.data.simulationId),
      networkSurplusShortageStatus: translateTransferSignal(String(res.data.networkSurplusShortageStatus || '')),
      interWarehouseReplenishment: translateTransferSignal(String(res.data.interWarehouseReplenishment || '')),
      replenishmentExceptionTriage: translateTransferSignal(String(res.data.replenishmentExceptionTriage || '')),
      status: translateTransferSignal(String(res.data.status)),
    };
  }
  throw new Error('Phản hồi mô phỏng tái cân bằng tồn kho mạng lưới không hợp lệ từ máy chủ.');
};

export function useDraftModalFocusTrap(
  showCreate: boolean,
  draftDialogRef: RefObject<HTMLFormElement | null>,
  createInFlightRef: RefObject<boolean>,
  closeDraftEditor: () => void,
) {
  useEffect(() => {
    if (!showCreate) return;
    const root = draftDialogRef.current;
    const focusable = () =>
      Array.from(
        root?.querySelectorAll<HTMLElement>(
          'button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );
    (root?.querySelector<HTMLElement>('select') ?? focusable()[0])?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !createInFlightRef.current) {
        if (event.defaultPrevented) return;
        event.preventDefault();
        closeDraftEditor();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusItems = focusable();
      if (!focusItems.length) return;
      const first = focusItems[0];
      const last = focusItems[focusItems.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [showCreate, draftDialogRef, createInFlightRef, closeDraftEditor]);
}

