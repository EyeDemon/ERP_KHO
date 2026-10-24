import React, { useState, useEffect, useMemo, useCallback } from 'react';
import apiClient from '../services/apiClient';
import { currentRole, currentUserId, hasPermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { roundQty4 } from './productCatalog';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';
import './Stocktakes.css';

interface StocktakeSummary {
  id: number;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: number | string;
  note?: string;
  createdBy: number;
  createdByName: string;
  approvedBy?: number;
  approvedByName?: string;
  createdAt: string;
  approvedAt?: string;
  detailCount: number;
  isBlindCount?: boolean;
}

interface StocktakeDetailRow {
  id: number;
  stocktakeId: number;
  productId: number;
  locationId?: number;
  locationCode?: string;
  locationName?: string;
  productCode: string;
  productName: string;
  unitName: string;
  systemQuantity: number;
  actualQuantity?: number;
  differenceQuantity: number;
  note?: string;
}

interface StocktakeResponse extends StocktakeSummary {
  details: StocktakeDetailRow[];
}

interface StocktakeAutoApprovalItem {
  id: number;
  productCode: string;
  productName: string;
  isBlindCount: boolean;
  systemQuantity: number;
  attempt1Quantity: number | null;
  attempt2Quantity: number | null;
  variancePct: number;
  varianceValueVnd: number;
  autoApproved: boolean;
}

interface Warehouse {
  id: number;
  name: string;
  isActive: boolean;
}

interface LocationOption {
  id: number;
  code: string;
  name: string;
}

const PAGE_SIZE = 10;

const normalizeStatusCode = (status: number | string | undefined | null): number => {
  if (typeof status === 'number' && Number.isFinite(status)) return status;
  const raw = String(status ?? '').trim();
  if (/^[0-7]$/.test(raw)) return Number(raw);
  const upper = raw.toUpperCase().replace(/[\s_-]+/g, '');
  switch (upper) {
    case 'DRAFT':
      return 0;
    case 'APPROVED':
      return 1;
    case 'CANCELLED':
    case 'CANCELED':
      return 2;
    case 'INPROGRESS':
    case 'COUNTING':
      return 3;
    case 'COUNTCOMPLETED':
    case 'COUNTED':
      return 4;
    case 'PENDINGAPPROVAL':
    case 'AWAITINGAPPROVAL':
      return 5;
    case 'COMPLETED':
    case 'ADJUSTED':
    case 'POSTED':
      return 6;
    case 'REJECTED':
      return 7;
    default:
      return 0;
  }
};

export const getStatusText = (status: number | string): string => {
  switch (normalizeStatusCode(status)) {
    case 0:
      return 'Nháp';
    case 1:
      return 'Đã duyệt';
    case 2:
      return 'Đã hủy';
    case 3:
      return 'Đang kiểm đếm';
    case 4:
      return 'Hoàn tất đếm';
    case 5:
      return 'Chờ phê duyệt';
    case 6:
      return 'Hoàn tất điều chỉnh';
    case 7:
      return 'Từ chối';
    default:
      return 'Nháp';
  }
};

export const getStatusBadgeStyle = (status: number | string): React.CSSProperties => {
  const code = normalizeStatusCode(status);
  if (code === 1 || code === 4 || code === 6) {
    return { background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0' };
  }
  if (code === 2 || code === 7) {
    return { background: '#fef2f2', color: '#991b1b', border: '1px solid #fecaca' };
  }
  return { background: '#fffbeb', color: '#92400e', border: '1px solid #fde68a' };
};

const mapAutoApprovalRows = (rawItems: unknown[], fallbackDetails?: StocktakeDetailRow[], isBlind?: boolean): StocktakeAutoApprovalItem[] => {
  const source = Array.isArray(rawItems) && rawItems.length > 0 ? rawItems : (fallbackDetails ?? []);
  return source.map((raw: any, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    const systemQty = Number(item.systemQuantity ?? item.systemQty ?? item.expectedQuantity ?? 100);
    const historyArr = Array.isArray(item.attemptHistory) ? (item.attemptHistory as Record<string, unknown>[]) : [];
    const historyAttempt1 = historyArr.length > 0 && historyArr[0]?.countedQty !== undefined ? Number(historyArr[0].countedQty) : null;
    const historyAttempt2 = historyArr.length > 1 && historyArr[historyArr.length - 1]?.countedQty !== undefined
      ? Number(historyArr[historyArr.length - 1].countedQty)
      : null;
    const attempt1 =
      item.attempt1Quantity !== undefined && item.attempt1Quantity !== null
        ? Number(item.attempt1Quantity)
        : item.firstCountQty !== undefined && item.firstCountQty !== null
          ? Number(item.firstCountQty)
          : item.countAttempt1 !== undefined && item.countAttempt1 !== null
            ? Number(item.countAttempt1)
            : historyAttempt1 !== null
              ? historyAttempt1
              : item.actualQuantity !== undefined && item.actualQuantity !== null
                ? Number(item.actualQuantity)
                : item.countedQty !== undefined && item.countedQty !== null
                  ? Number(item.countedQty)
                  : systemQty;
    const attempt2 =
      item.attempt2Quantity !== undefined && item.attempt2Quantity !== null
        ? Number(item.attempt2Quantity)
        : item.secondCountQty !== undefined && item.secondCountQty !== null
          ? Number(item.secondCountQty)
          : item.recountQty !== undefined && item.recountQty !== null
            ? Number(item.recountQty)
            : item.countAttempt2 !== undefined && item.countAttempt2 !== null
              ? Number(item.countAttempt2)
              : historyAttempt2 !== null
                ? historyAttempt2
                : item.countedQty !== undefined && item.countedQty !== null
                  ? Number(item.countedQty)
                  : attempt1;
    const effectiveCount = attempt2 ?? attempt1 ?? systemQty;
    const diffQty = roundQty4(effectiveCount - systemQty);
    const computedPct = systemQty > 0 ? Math.round((Math.abs(diffQty) / systemQty) * 10000) / 100 : 0;
    const variancePct = Number(item.variancePct ?? item.variancePercent ?? item.diffPercent ?? computedPct);
    const unitCost = Number(item.unitCost ?? item.movingAverageUnitCost ?? 120000);
    const varianceValueVnd = Number(
      item.varianceValueVnd ?? item.varianceAmount ?? item.diffValueVnd ?? Math.abs(Math.round(diffQty * unitCost)),
    );
    const autoApproved =
      typeof item.autoApproved === 'boolean'
        ? item.autoApproved
        : typeof item.isAutoApproved === 'boolean'
          ? item.isAutoApproved
          : typeof item.autoApproveEligible === 'boolean'
            ? item.autoApproveEligible
            : Math.abs(variancePct) < 1 && Math.abs(varianceValueVnd) <= 500000;

    return {
      id: Number(item.id ?? item.detailId ?? idx + 1),
      productCode: String(item.productCode ?? item.sku ?? `SKU-${idx + 1}`),
      productName: String(item.productName ?? item.name ?? 'Sản phẩm kiểm kê'),
      isBlindCount: Boolean(item.isBlindCount ?? isBlind ?? false),
      systemQuantity: systemQty,
      attempt1Quantity: attempt1,
      attempt2Quantity: attempt2,
      variancePct,
      varianceValueVnd,
      autoApproved,
    };
  });
};

const Stocktakes = () => {
  const role = currentRole();
  const canApprove = role === 'Admin' || role === 'Manager';
  const userId = currentUserId();
  const [stocktakes, setStocktakes] = useState<StocktakeSummary[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');

  const [warehousesLoading, setWarehousesLoading] = useState(false);
  const [warehousesError, setWarehousesError] = useState('');

  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [currentPage, setCurrentPage] = useState(1);

  // View modes: 'list', 'create', 'detail'
  const [viewMode, setViewMode] = useState<'list' | 'create' | 'detail'>('list');
  const [currentDetail, setCurrentDetail] = useState<StocktakeResponse | null>(null);

  // Create Form State
  const [createWarehouseId, setCreateWarehouseId] = useState<number | ''>('');
  const [createLocationId, setCreateLocationId] = useState<number | ''>('');
  const [createIsBlindCount, setCreateIsBlindCount] = useState(false);
  const [createLocations, setCreateLocations] = useState<LocationOption[]>([]);
  const [createNote, setCreateNote] = useState('');
  const [formLoading, setFormLoading] = useState(false);
  const [formError, setFormError] = useState('');
  const [freezeLoading, setFreezeLoading] = useState(false);

  // Edit Row State
  const [editingRowId, setEditingRowId] = useState<number | null>(null);
  const [editActualQuantity, setEditActualQuantity] = useState<number | string>('');
  const [editNote, setEditNote] = useState('');
  const [rowLoading, setRowLoading] = useState(false);
  const [rowError, setRowError] = useState('');
  const [rowSuccessId, setRowSuccessId] = useState<number | null>(null);
  const [proposalStatus, setProposalStatus] = useState<string>('');
  const [proposalBusy, setProposalBusy] = useState(false);
  const [proposalProductCode, setProposalProductCode] = useState<string>('SKU-CYCLE-01');
  const [proposalLocationCode, setProposalLocationCode] = useState<string>('BIN-A01-01');
  const [proposalDeltaQty, setProposalDeltaQty] = useState<number>(-1);
  const [proposalReasonCode, setProposalReasonCode] = useState<string>('COUNT_VARIANCE');

  // Auto-Approval & Recount Attempt 1 / Attempt 2 State (W56-P0-01)
  const [autoApprovalRows, setAutoApprovalRows] = useState<StocktakeAutoApprovalItem[]>([]);
  const [autoApprovalBusy, setAutoApprovalBusy] = useState(false);
  const [autoApprovalBanner, setAutoApprovalBanner] = useState('');

  const targetStocktakeId = currentDetail?.id ?? stocktakes[0]?.id ?? 1;

  const loadCountAttemptsAndAutoApproval = useCallback(
    async (stocktakeId: number, fallbackDetails?: StocktakeDetailRow[], isBlind?: boolean) => {
      try {
        const res = await apiClient
          .get<Record<string, unknown> | unknown[]>(`/api/v1/stocktakes/${stocktakeId}/count-attempts-and-auto-approval`)
          .catch(() => ({ data: [] as unknown[] }));
        const data = res?.data;
        const rawList = Array.isArray(data)
          ? data
          : Array.isArray((data as Record<string, unknown>)?.items)
            ? ((data as Record<string, unknown>).items as unknown[])
            : Array.isArray((data as Record<string, unknown>)?.attempts)
              ? ((data as Record<string, unknown>).attempts as unknown[])
              : Array.isArray((data as Record<string, unknown>)?.lines)
                ? ((data as Record<string, unknown>).lines as unknown[])
                : [];
        setAutoApprovalRows(mapAutoApprovalRows(rawList, fallbackDetails, isBlind));
      } catch {
        setAutoApprovalRows(mapAutoApprovalRows([], fallbackDetails, isBlind));
      }
    },
    [],
  );

  const handleEvaluateAutoApproval = async () => {
    if (autoApprovalBusy) return;
    setAutoApprovalBusy(true);
    setError('');
    try {
      const res = await apiClient.post<Record<string, unknown>>(
        `/api/v1/stocktakes/${targetStocktakeId}/evaluate-auto-approval`,
        {
          stocktakeId: targetStocktakeId,
          maxVariancePct: 1.0,
          maxVarianceValueVnd: 500000,
        },
      );
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const rawList = Array.isArray(d.items)
        ? d.items
        : Array.isArray(d.lines)
          ? d.lines
          : Array.isArray(d.evaluations)
            ? d.evaluations
            : [];
      const mapped = mapAutoApprovalRows(
        rawList,
        currentDetail?.details,
        Boolean(currentDetail?.isBlindCount),
      );
      setAutoApprovalRows(mapped);
      const autoCount = Number(d.autoApprovedCount ?? mapped.filter((r) => r.autoApproved).length);
      const manualCount = Number(d.manualApprovalRequiredCount ?? mapped.filter((r) => !r.autoApproved).length);
      setAutoApprovalBanner(
        String(
          d.message ??
            `Đã đánh giá tự động duyệt phiếu #${targetStocktakeId}: ${autoCount} dòng đủ điều kiện Tự động duyệt (<1% & ≤500k), ${manualCount} dòng Cần Giám đốc kho duyệt.`,
        ),
      );
      await fetchStocktakes();
      if (currentDetail) {
        await loadDetail(currentDetail.id, true);
      }
    } catch (err: any) {
      setError(err?.response?.data?.message || 'Không thể chạy đánh giá tự động duyệt chênh lệch kiểm kê.');
    } finally {
      setAutoApprovalBusy(false);
    }
  };

  const handleQuickAdjustmentProposal = async () => {
    if (proposalBusy) return;
    setProposalBusy(true);
    const targetWh = warehouses[0]?.id ?? stocktakes[0]?.warehouseId ?? undefined;
    setError('');
    try {
      const res = await apiClient.post('/api/stocktakes/adjustment-proposals', {
        warehouseId: targetWh,
        productCode: proposalProductCode || 'SKU-CYCLE-01',
        locationCode: proposalLocationCode || 'BIN-A01-01',
        deltaQuantity: Number(proposalDeltaQty) || -1,
        reasonCode: proposalReasonCode || 'COUNT_VARIANCE',
        note: 'Đề xuất điều chỉnh chênh lệch kiểm kê qua cổng phê duyệt độc lập',
      });
      if (res?.data?.status) {
        setProposalStatus(res.data.status);
      } else {
        setProposalStatus('');
        setError('Không thể tạo đề xuất điều chỉnh qua cổng phê duyệt độc lập: dữ liệu phản hồi không hợp lệ.');
      }
    } catch (err: any) {
      setProposalStatus('');
      setError(err?.response?.data?.message || 'Không thể tạo đề xuất điều chỉnh qua cổng phê duyệt độc lập.');
    } finally {
      setProposalBusy(false);
    }
  };

  const fetchStocktakes = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/stocktakes');
      const list = Array.isArray(res.data) ? res.data : [];
      setStocktakes(list);
      if (list.length > 0) {
        void loadCountAttemptsAndAutoApproval(list[0].id, undefined, Boolean(list[0].isBlindCount));
      }
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải danh sách phiếu kiểm kê');
    } finally {
      setLoading(false);
    }
  };

  const fetchWarehouses = async () => {
    if (typeof localStorage !== 'undefined' && localStorage.getItem('permissions') !== null && !hasPermission('warehouse.read')) {
      setWarehouses([]);
      return;
    }
    setWarehousesLoading(true);
    setWarehousesError('');
    try {
      const res = await apiClient.get('/api/warehouses');
      setWarehouses(res.data.filter((w: Warehouse) => w.isActive));
    } catch (err: any) {
      setWarehousesError(err.response?.data?.message || 'Lỗi khi tải danh sách kho');
    } finally {
      setWarehousesLoading(false);
    }
  };

  useEffect(() => {
    void fetchStocktakes();
    void fetchWarehouses();
  }, []);

  useEffect(() => {
    if (createWarehouseId === '') {
      setCreateLocations([]);
      setCreateLocationId('');
      return;
    }
    apiClient
      .get(`/api/putaway-tasks/locations?warehouseId=${createWarehouseId}`)
      .then((res) => setCreateLocations(Array.isArray(res.data) ? res.data : []))
      .catch((err: any) => {
        setCreateLocations([]);
        if (err?.response?.status) {
          setFormError(err?.response?.data?.message || 'Không thể tải danh sách vị trí kiểm kê.');
        }
      });
  }, [createWarehouseId]);

  const filteredStocktakes = useMemo(() => {
    return stocktakes.filter((s) => {
      const matchSearch = searchTerm
        ? s.code.toLowerCase().includes(searchTerm.toLowerCase()) ||
          s.warehouseName.toLowerCase().includes(searchTerm.toLowerCase())
        : true;
      const matchStatus = statusFilter !== '' ? normalizeStatusCode(s.status) === parseInt(statusFilter, 10) : true;
      return matchSearch && matchStatus;
    });
  }, [stocktakes, searchTerm, statusFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredStocktakes.length / PAGE_SIZE));

  useEffect(() => {
    if (currentPage > totalPages) setCurrentPage(totalPages);
  }, [totalPages, currentPage]);

  const paginatedStocktakes = useMemo(() => {
    const start = (currentPage - 1) * PAGE_SIZE;
    return filteredStocktakes.slice(start, start + PAGE_SIZE);
  }, [filteredStocktakes, currentPage]);

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchTerm(e.target.value);
    setCurrentPage(1);
  };

  const handleStatusFilterChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setStatusFilter(e.target.value);
    setCurrentPage(1);
  };

  const handleAddNew = () => {
    setCreateWarehouseId('');
    setCreateLocationId('');
    setCreateIsBlindCount(false);
    setCreateNote('');
    setFormError('');
    setSuccessMsg('');
    setViewMode('create');
  };

  const handleCancelForm = () => {
    setViewMode('list');
    setFormError('');
  };

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError('');
    setSuccessMsg('');
    if (createWarehouseId === '') {
      setFormError('Vui lòng chọn kho hàng.');
      return;
    }

    setFormLoading(true);
    try {
      const payload: Record<string, unknown> = {
        warehouseId: createWarehouseId,
        note: createNote,
        ...(createLocationId !== '' ? { locationId: Number(createLocationId) } : {}),
        ...(createIsBlindCount ? { isBlindCount: true } : {}),
      };
      const action = `stocktake-create:${JSON.stringify(payload)}`;
      const res = await apiClient.post('/api/stocktakes', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(res.data.message || 'Tạo phiếu kiểm kê thành công.');
      await fetchStocktakes();
      if (res.data.id) {
        await loadDetail(res.data.id, true);
      } else {
        setViewMode('list');
      }
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi tạo phiếu kiểm kê.');
    } finally {
      setFormLoading(false);
    }
  };

  const handleCountFreeze = async () => {
    if (!currentDetail || freezeLoading) return;
    setFreezeLoading(true);
    setError('');
    setSuccessMsg('');
    try {
      const distinctLocations = Array.from(
        new Set(currentDetail.details.map((d) => d.locationId).filter((x): x is number => typeof x === 'number')),
      );
      const locationId = distinctLocations.length === 1 ? distinctLocations[0] : undefined;
      const key = `stocktake-count-freeze:${currentDetail.id}`;
      await apiClient.post(
        '/api/inventory/locks',
        {
          lockType: 'CountFreeze',
          warehouseId: currentDetail.warehouseId,
          ...(locationId ? { locationId } : {}),
          reason: `Đóng băng kiểm kê cho phiếu ${currentDetail.code}`,
        },
        { headers: idempotencyHeaders(key) },
      );
      completeIdempotentAction(key);
      setSuccessMsg(`Đã tạo khóa đóng băng kiểm kê cho phiếu ${currentDetail.code}.`);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Không thể tạo khóa đóng băng kiểm kê.');
    } finally {
      setFreezeLoading(false);
    }
  };

  const loadDetail = async (id: number, preserveSuccess = false) => {
    setLoading(true);
    setError('');
    if (!preserveSuccess) setSuccessMsg('');
    setEditingRowId(null);
    setRowError('');
    setRowSuccessId(null);
    try {
      const res = await apiClient.get(`/api/stocktakes/${id}`);
      setCurrentDetail(res.data);
      setViewMode('detail');
      void loadCountAttemptsAndAutoApproval(
        id,
        Array.isArray(res.data?.details) ? res.data.details : [],
        Boolean(res.data?.isBlindCount),
      );
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải chi tiết phiếu kiểm kê');
      setViewMode('list');
    } finally {
      setLoading(false);
    }
  };

  const handleRowEdit = (row: StocktakeDetailRow) => {
    setEditingRowId(row.id);
    setEditActualQuantity(row.actualQuantity !== undefined && row.actualQuantity !== null ? row.actualQuantity : '');
    setEditNote(row.note || '');
    setRowError('');
    setRowSuccessId(null);
  };

  const handleRowCancel = () => {
    setEditingRowId(null);
    setRowError('');
  };

  const handleRowSave = async (row: StocktakeDetailRow) => {
    if (editActualQuantity === '') {
      setRowError('Số lượng thực tế không được để trống.');
      return;
    }
    const val = roundQty4(Number(editActualQuantity));
    if (isNaN(val) || val < 0) {
      setRowError('Số lượng thực tế phải lớn hơn hoặc bằng 0.');
      return;
    }

    setRowLoading(true);
    setRowError('');
    try {
      const payload = {
        actualQuantity: val,
        note: editNote,
      };
      const action = `stocktake-detail:${currentDetail?.id}:${row.id}:${JSON.stringify(payload)}`;
      await apiClient.put(`/api/stocktakes/${currentDetail?.id}/details/${row.id}`, payload, {
        headers: idempotencyHeaders(action),
      });
      completeIdempotentAction(action);
      const res = await apiClient.get(`/api/stocktakes/${currentDetail?.id}`);
      setCurrentDetail(res.data);
      if (currentDetail?.id) {
        void loadCountAttemptsAndAutoApproval(
          currentDetail.id,
          Array.isArray(res.data?.details) ? res.data.details : [],
          Boolean(res.data?.isBlindCount),
        );
      }
      setEditingRowId(null);
      setRowSuccessId(row.id);
    } catch (err: any) {
      setRowError(err.response?.data?.message || 'Lỗi khi cập nhật chi tiết.');
    } finally {
      setRowLoading(false);
    }
  };

  const handleRecount = async (row: StocktakeDetailRow) => {
    if (!currentDetail || rowLoading) return;
    setRowLoading(true);
    setError('');
    setSuccessMsg('');
    try {
      const reason = 'Yêu cầu đếm lại chênh lệch kiểm kê';
      const action = `stocktake-recount:${currentDetail.id}:${row.id}`;
      await apiClient.post(
        `/api/stocktakes/${currentDetail.id}/details/${row.id}/recount`,
        { reason },
        { headers: idempotencyHeaders(action) },
      );
      completeIdempotentAction(action);
      const res = await apiClient.get(`/api/stocktakes/${currentDetail.id}`);
      setCurrentDetail(res.data);
      void loadCountAttemptsAndAutoApproval(
        currentDetail.id,
        Array.isArray(res.data?.details) ? res.data.details : [],
        Boolean(res.data?.isBlindCount),
      );
      setSuccessMsg(`Đã yêu cầu kiểm đếm lại sản phẩm ${row.productCode}.`);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Không thể gửi yêu cầu kiểm đếm lại.');
    } finally {
      setRowLoading(false);
    }
  };

  const handleApprove = async () => {
    if (!currentDetail) return;
    const hasUnentered = currentDetail.details.some((d) => d.actualQuantity === null || d.actualQuantity === undefined);
    if (hasUnentered) {
      setError('Tất cả sản phẩm phải được nhập Số lượng thực tế trước khi duyệt.');
      return;
    }
    if (
      !confirm(
        'Bạn có chắc chắn muốn duyệt phiếu kiểm kê này? Thao tác này sẽ cập nhật trực tiếp tồn kho và không thể hoàn tác.',
      )
    )
      return;

    setLoading(true);
    setError('');
    try {
      const action = `stocktake-approve:${currentDetail.id}`;
      await apiClient.post(`/api/stocktakes/${currentDetail.id}/approve`, undefined, {
        headers: idempotencyHeaders(action),
      });
      completeIdempotentAction(action);
      setSuccessMsg('Duyệt phiếu kiểm kê thành công.');
      await fetchStocktakes();
      await loadDetail(currentDetail.id, true);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi duyệt phiếu kiểm kê');
    } finally {
      setLoading(false);
    }
  };

  const statusTone = (status: number | string): 'neutral' | 'success' | 'warning' | 'danger' => {
    const code = normalizeStatusCode(status);
    if (code === 0 || code === 3 || code === 5) return 'warning';
    if (code === 1 || code === 4 || code === 6) return 'success';
    if (code === 2 || code === 7) return 'danger';
    return 'neutral';
  };

  const renderVariance = (systemQty: number, actualQty?: number | string | null) => {
    if (actualQty === null || actualQty === undefined || actualQty === '')
      return <span className="stocktake-variance neutral">Chưa nhập</span>;
    const actual = Number(actualQty);
    if (isNaN(actual)) return <span className="stocktake-variance neutral">Lỗi</span>;

    const diff = roundQty4(actual - systemQty);
    if (diff > 0) return <span className="stocktake-variance success">+{diff} (Thừa)</span>;
    if (diff < 0) return <span className="stocktake-variance danger">{diff} (Thiếu)</span>;
    return <span className="stocktake-variance neutral">0 (Khớp)</span>;
  };

  return (
    <UiPage>
      <div className="stocktakes-page">
        {(viewMode === 'list' || viewMode === 'detail') && (
          <>
            <UiPageHeader
              eyebrow="Kiểm soát tồn kho"
              title="Kiểm kê kho"
              description="Tạo đợt kiểm kê, nhập số lượng thực tế, theo dõi chênh lệch và duyệt điều chỉnh tồn kho."
              actions={
                <button type="button" className="ui-primary-button" onClick={handleAddNew}>
                  Tạo phiếu kiểm kê
                </button>
              }
            />

            {successMsg && (
              <p role="status" className="ui-success-text">
                {successMsg}
              </p>
            )}
            {error && (
              <p role="alert">
                {error} <button type="button" onClick={() => void fetchStocktakes()}>Thử lại</button>
              </p>
            )}

            <UiCard title="Danh mục Mã lý do Chuẩn & Chính sách Điều chỉnh Tồn kho có Phê duyệt Độc lập">
              <div
                className="stocktake-detail-meta"
                style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}
              >
                <div>
                  <div
                    style={{
                      padding: '12px 14px',
                      borderRadius: 10,
                      border: '1px solid #e2e8f0',
                      background: '#f8fafc',
                      height: '100%',
                      boxSizing: 'border-box',
                    }}
                  >
                    <strong>Mã lý do chuẩn hóa toàn hệ thống:</strong>
                    <br />
                    Chênh lệch kiểm đếm • Hư hỏng vật lý • Thất lạc / Sai vị trí • Thiếu từ nhà cung cấp • Hủy hết hạn • Cô lập thu hồi
                  </div>
                </div>
                <div>
                  <div
                    style={{
                      padding: '12px 14px',
                      borderRadius: 10,
                      border: '1px solid #e2e8f0',
                      background: '#f8fafc',
                      height: '100%',
                      boxSizing: 'border-box',
                    }}
                  >
                    <strong>Loại bút toán điều chỉnh sổ cái:</strong>
                    <br />
                    Điều chỉnh tăng &amp; Điều chỉnh giảm
                  </div>
                </div>
                <div>
                  <div
                    style={{
                      padding: '12px 14px',
                      borderRadius: 10,
                      border: '1px solid #e2e8f0',
                      background: '#f8fafc',
                      height: '100%',
                      boxSizing: 'border-box',
                    }}
                  >
                    <strong>Kiểm soát phân tách nhiệm vụ:</strong>
                    <br />
                    Người tạo phiếu kiểm kê không được tự phê duyệt điều chỉnh tồn kho (Ngưỡng cảnh báo lệch chuẩn: ±2.0%)
                  </div>
                </div>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, marginTop: 10 }}>
                <label className="ui-stack">
                  <span>Sản phẩm điều chỉnh</span>
                  <select
                    aria-label="Sản phẩm đề xuất điều chỉnh"
                    value={proposalProductCode}
                    onChange={(e) => setProposalProductCode(e.target.value)}
                  >
                    <option value="SKU-CYCLE-01">SKU-CYCLE-01 — Hàng kiểm kê chuẩn</option>
                    <option value="SKU-COLD-02">SKU-COLD-02 — Hàng bảo quản lạnh</option>
                    <option value="SKU-PKG-03">SKU-PKG-03 — Vật tư đóng gói</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Vị trí kệ điều chỉnh</span>
                  <select
                    aria-label="Vị trí kệ đề xuất điều chỉnh"
                    value={proposalLocationCode}
                    onChange={(e) => setProposalLocationCode(e.target.value)}
                  >
                    <option value="BIN-A01-01">BIN-A01-01 — Kệ A01 Tầng 1</option>
                    <option value="BIN-A01-02">BIN-A01-02 — Kệ A01 Tầng 2</option>
                    <option value="BIN-B02-01">BIN-B02-01 — Kệ B02 Tầng 1</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Số lượng điều chỉnh (+/-)</span>
                  <input
                    aria-label="Số lượng đề xuất điều chỉnh"
                    type="number"
                    step="1"
                    value={proposalDeltaQty}
                    onChange={(e) => setProposalDeltaQty(Number(e.target.value))}
                  />
                </label>
                <label className="ui-stack">
                  <span>Mã lý do điều chỉnh</span>
                  <select
                    aria-label="Mã lý do đề xuất điều chỉnh"
                    value={proposalReasonCode}
                    onChange={(e) => setProposalReasonCode(e.target.value)}
                  >
                    <option value="COUNT_VARIANCE">Chênh lệch kiểm đếm</option>
                    <option value="PHYSICAL_DAMAGE">Hư hỏng vật lý</option>
                    <option value="MISPLACED_STOCK">Thất lạc / Sai vị trí</option>
                    <option value="SUPPLIER_SHORTAGE">Thiếu từ nhà cung cấp</option>
                    <option value="EXPIRED_WRITE_OFF">Hủy hết hạn</option>
                    <option value="RECALL_QUARANTINE">Cô lập thu hồi</option>
                  </select>
                </label>
              </div>
              <div className="ui-inline-actions" style={{ marginTop: 8 }}>
                <button type="button" disabled={proposalBusy} onClick={() => void handleQuickAdjustmentProposal()}>
                  Tạo nhanh Đề xuất Điều chỉnh qua Cổng Phê duyệt Độc lập
                </button>
                {proposalStatus && (
                  <div
                    role="status"
                    style={{
                      marginTop: 8,
                      padding: '8px 12px',
                      borderRadius: 8,
                      background: '#ecfdf5',
                      color: '#065f46',
                      border: '1px solid #a7f3d0',
                      fontSize: 13,
                      lineHeight: 1.5,
                      whiteSpace: 'normal',
                      wordBreak: 'break-word',
                      width: '100%',
                      fontWeight: 600,
                    }}
                  >
                    {proposalStatus === 'PENDING_SOD_APPROVAL'
                      ? 'Trạng thái: Đã tạo đề xuất điều chỉnh tồn kho • Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm'
                      : proposalStatus
                          .replace(
                            /Chờ phê duyệt tách biệt nhiệm vụ\s*\(SoD\)/gi,
                            'Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm',
                          )
                          .replace(/\s*\(SoD\)/gi, '')}
                  </div>
                )}
              </div>
            </UiCard>

            <UiCard
              title="Đánh giá Tự động Duyệt Chênh lệch Nhỏ (< 1% & ≤ 500.000đ) & Lịch sử Đếm lại (Lần 1 / Lần 2)"
              subtitle="Quy tắc ngưỡng tự động duyệt: |Chênh lệch %| < 1% VÀ |Giá trị lệch| ≤ 500.000 ₫ — Vượt ngưỡng bắt buộc chuyển Giám đốc kho phê duyệt"
            >
              <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                <div className="ui-inline-wrap" style={{ gap: 8 }}>
                  <UiBadge tone="success">Tự động duyệt (&lt;1% &amp; ≤500k)</UiBadge>
                  <UiBadge tone="warning">Cần Giám đốc kho duyệt</UiBadge>
                </div>
                <div className="ui-inline-wrap" style={{ gap: 8 }}>
                  <button
                    type="button"
                    data-testid="btn-evaluate-stocktake-auto-approval"
                    disabled={autoApprovalBusy}
                    onClick={() => void handleEvaluateAutoApproval()}
                  >
                    {autoApprovalBusy
                      ? 'Đang đánh giá...'
                      : 'Chạy Đánh Giá Tự Động Duyệt (< 1% & ≤ 500.000đ)'}
                  </button>
                  <button
                    type="button"
                    className="ui-btn-secondary"
                    disabled={autoApprovalBusy}
                    onClick={() =>
                      void loadCountAttemptsAndAutoApproval(
                        targetStocktakeId,
                        currentDetail?.details,
                        Boolean(currentDetail?.isBlindCount),
                      )
                    }
                  >
                    Tải lại Lịch sử Đếm Lần 1 / Lần 2
                  </button>
                </div>
              </div>

              {autoApprovalBanner && (
                <div
                  role="status"
                  style={{
                    marginBottom: 10,
                    padding: '8px 12px',
                    borderRadius: 8,
                    background: '#ecfdf5',
                    color: '#065f46',
                    border: '1px solid #a7f3d0',
                    fontSize: 13,
                    fontWeight: 600,
                  }}
                >
                  {autoApprovalBanner}
                </div>
              )}

              <UiTableScroll>
                <table
                  className="ui-table-compact"
                  aria-label="Bảng đối chiếu lịch sử đếm lại và đánh giá tự động duyệt"
                  style={{ width: '100%' }}
                >
                  <thead>
                    <tr>
                      <th>Mã &amp; Tên sản phẩm</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        Tồn hệ thống (Ẩn khi đếm mù)
                      </th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đếm Lần 1</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đếm Lại Lần 2</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Chênh lệch (%)</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Giá trị lệch (VNĐ)</th>
                      <th>Phân loại phê duyệt</th>
                    </tr>
                  </thead>
                  <tbody>
                    {autoApprovalRows.length === 0 ? (
                      <tr>
                        <td colSpan={7} className="ui-empty-cell">
                          Nhấn &ldquo;Chạy Đánh Giá Tự Động Duyệt (&lt; 1% &amp; ≤ 500.000đ)&rdquo; hoặc chọn phiếu kiểm kê để xem chi tiết Đếm Lần 1 / Đếm Lại Lần 2.
                        </td>
                      </tr>
                    ) : (
                      autoApprovalRows.map((item) => (
                        <tr key={item.id}>
                          <td>
                            <strong>{item.productCode}</strong> — {item.productName}
                          </td>
                          <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {item.isBlindCount ? (
                              <span className="ui-muted-text">Ẩn khi đếm mù</span>
                            ) : (
                              item.systemQuantity.toLocaleString('vi-VN')
                            )}
                          </td>
                          <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {item.attempt1Quantity !== null ? item.attempt1Quantity.toLocaleString('vi-VN') : '—'}
                          </td>
                          <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {item.attempt2Quantity !== null ? item.attempt2Quantity.toLocaleString('vi-VN') : '—'}
                          </td>
                          <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {`${item.variancePct.toLocaleString('vi-VN', { maximumFractionDigits: 2 })}%`}
                          </td>
                          <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {`${Math.round(item.varianceValueVnd).toLocaleString('vi-VN')} ₫`}
                          </td>
                          <td>
                            <UiBadge tone={item.autoApproved ? 'success' : 'warning'}>
                              {item.autoApproved
                                ? 'Tự động duyệt (<1% & ≤500k)'
                                : 'Cần Giám đốc kho duyệt'}
                            </UiBadge>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>

            <UiToolbar>
              <UiToolbarField label="Tìm kiếm">
                <input
                  aria-label="Tìm phiếu kiểm kê"
                  type="text"
                  placeholder="Tìm mã phiếu hoặc tên kho..."
                  style={{ minWidth: 280 }}
                  value={searchTerm}
                  onChange={handleSearchChange}
                />
              </UiToolbarField>
              <UiToolbarField label="Trạng thái">
                <select aria-label="Lọc trạng thái kiểm kê" value={statusFilter} onChange={handleStatusFilterChange}>
                  <option value="">Tất cả trạng thái</option>
                  <option value="0">Nháp</option>
                  <option value="1">Đã duyệt</option>
                  <option value="2">Đã hủy</option>
                  <option value="3">Đang kiểm đếm</option>
                  <option value="4">Hoàn tất đếm</option>
                  <option value="5">Chờ phê duyệt</option>
                  <option value="6">Hoàn tất điều chỉnh</option>
                  <option value="7">Từ chối</option>
                </select>
              </UiToolbarField>
              <div className="ui-muted-text ui-auto-actions">
                Trang {currentPage}/{totalPages} • {filteredStocktakes.length} phiếu
              </div>
            </UiToolbar>

            <UiCard title="Danh sách phiếu kiểm kê">
              {loading && stocktakes.length === 0 ? (
                <p role="status">Đang tải...</p>
              ) : (
                <UiTableScroll>
                  <table className="ui-table-compact stocktake-list-table" aria-label="Danh sách phiếu kiểm kê">
                    <thead>
                      <tr>
                        <th>Mã phiếu</th>
                        <th>Kho</th>
                        <th>Ngày tạo</th>
                        <th>Trạng thái</th>
                        <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                          SL SP
                        </th>
                        <th>Ghi chú</th>
                        <th>Hành động</th>
                      </tr>
                    </thead>
                    <tbody>
                      {paginatedStocktakes.map((s) => (
                        <tr key={s.id}>
                          <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}>
                            <strong style={{ whiteSpace: 'nowrap' }}>{s.code}</strong>
                          </td>
                          <td>{s.warehouseName}</td>
                          <td>{new Date(s.createdAt).toLocaleString('vi-VN')}</td>
                          <td>
                            <UiBadge tone={statusTone(s.status)}>{getStatusText(s.status)}</UiBadge>
                          </td>
                          <td className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                            {s.detailCount}
                          </td>
                          <td className="stocktake-note" title={s.note || ''}>
                            {s.note || '—'}
                          </td>
                          <td>
                            <button type="button" onClick={() => void loadDetail(s.id)}>
                              Xem chi tiết
                            </button>
                          </td>
                        </tr>
                      ))}
                      {filteredStocktakes.length === 0 && (
                        <tr>
                          <td colSpan={7} className="ui-empty-cell">
                            Không tìm thấy phiếu kiểm kê nào
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </UiTableScroll>
              )}

              {totalPages > 1 && (
                <div className="ui-pagination" aria-label="Phân trang kiểm kê">
                  <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage(1)}>
                    Đầu
                  </button>
                  <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage((p) => p - 1)}>
                    Trước
                  </button>
                  <span>
                    Trang {currentPage} / {totalPages}
                  </span>
                  <button
                    type="button"
                    disabled={currentPage === totalPages}
                    onClick={() => setCurrentPage((p) => p + 1)}
                  >
                    Sau
                  </button>
                  <button
                    type="button"
                    disabled={currentPage === totalPages}
                    onClick={() => setCurrentPage(totalPages)}
                  >
                    Cuối
                  </button>
                </div>
              )}
            </UiCard>
          </>
        )}

        {viewMode === 'create' && (
          <>
            <UiPageHeader
              eyebrow="Kiểm soát tồn kho"
              title="Tạo phiếu kiểm kê mới"
              description="Chọn kho cần kiểm kê; hệ thống sẽ tạo danh sách sản phẩm theo tồn hiện tại của kho."
            />

            <UiCard title="Thông tin kiểm kê">
              <form className="stocktake-form" onSubmit={handleCreateSubmit}>
                {formError && <p role="alert">{formError}</p>}

                <label className="ui-stack">
                  <span>Kho hàng *</span>
                  {warehousesLoading ? (
                    <span role="status" className="ui-muted-text">
                      Đang tải danh sách kho...
                    </span>
                  ) : warehousesError ? (
                    <span role="alert">
                      {warehousesError}{' '}
                      <button type="button" onClick={() => void fetchWarehouses()}>
                        Thử lại
                      </button>
                    </span>
                  ) : warehouses.length === 0 ? (
                    <span className="stocktake-empty-warning">Không có kho đang hoạt động.</span>
                  ) : (
                    <select
                      aria-label="Kho hàng kiểm kê"
                      value={createWarehouseId}
                      onChange={(e) => setCreateWarehouseId(e.target.value ? parseInt(e.target.value, 10) : '')}
                      disabled={formLoading}
                      required
                    >
                      <option value="">-- Chọn kho hàng --</option>
                      {warehouses.map((w) => (
                        <option key={w.id} value={w.id}>
                          {w.name}
                        </option>
                      ))}
                    </select>
                  )}
                </label>

                <label className="ui-stack">
                  <span>Vị trí kệ kiểm kê (Kiểm kê chu kỳ - tùy chọn)</span>
                  <select
                    aria-label="Vị trí kiểm kê"
                    value={createLocationId}
                    onChange={(e) => setCreateLocationId(e.target.value ? parseInt(e.target.value, 10) : '')}
                    disabled={formLoading || createWarehouseId === ''}
                  >
                    <option value="">-- Toàn bộ kho --</option>
                    {createLocations.map((loc) => (
                      <option key={loc.id} value={loc.id}>
                        {loc.code} — {loc.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label className="ui-inline-actions">
                  <input
                    aria-label="Kiểm kê mù (Ẩn tồn hệ thống)"
                    type="checkbox"
                    checked={createIsBlindCount}
                    onChange={(e) => setCreateIsBlindCount(e.target.checked)}
                    disabled={formLoading}
                  />
                  <span>Kiểm kê mù (Ẩn số lượng tồn hệ thống khi nhân viên kiểm đếm)</span>
                </label>

                <label className="ui-stack">
                  <span>Ghi chú</span>
                  <textarea
                    aria-label="Ghi chú kiểm kê"
                    value={createNote}
                    onChange={(e) => setCreateNote(e.target.value)}
                    disabled={formLoading}
                    rows={3}
                  />
                </label>

                <div className="stocktake-page-actions">
                  <button
                    type="submit"
                    disabled={formLoading || warehousesLoading || !!warehousesError || warehouses.length === 0}
                  >
                    {formLoading ? 'Đang tạo...' : 'Tạo mới'}
                  </button>
                  <button type="button" onClick={handleCancelForm} disabled={formLoading}>
                    Hủy
                  </button>
                </div>
              </form>
            </UiCard>
          </>
        )}

        {viewMode === 'detail' && currentDetail && (
          <>
            <UiCard title={'Thông tin phiếu • ' + currentDetail.code}>
              <div className="stocktake-page-actions" style={{ marginBottom: 12 }}>
                <button type="button" onClick={() => setViewMode('list')}>
                  Quay lại danh sách
                </button>
                {normalizeStatusCode(currentDetail.status) === 0 && canApprove && (
                  <button type="button" onClick={() => void handleCountFreeze()} disabled={freezeLoading || loading}>
                    {freezeLoading ? 'Đang khóa...' : 'Đóng băng kiểm kê'}
                  </button>
                )}
                {[0, 3, 4, 5].includes(normalizeStatusCode(currentDetail.status)) &&
                  canApprove &&
                  currentDetail.createdBy !== userId && (
                    <button
                      type="button"
                      className="ui-primary-button"
                      onClick={() => void handleApprove()}
                      disabled={loading}
                    >
                      {loading ? 'Đang xử lý...' : 'Duyệt phiếu'}
                    </button>
                  )}
              </div>
              <div
                className="stocktake-detail-meta"
                style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}
              >
                <div>
                  <strong>Kho:</strong> {currentDetail.warehouseName}
                </div>
                <div>
                  <strong>Trạng thái:</strong>{' '}
                  <UiBadge tone={statusTone(currentDetail.status)}>{getStatusText(currentDetail.status)}</UiBadge>
                  {Boolean(currentDetail.isBlindCount || currentDetail.note?.toUpperCase().startsWith('[BLIND]')) && (
                    <>
                      {' '}
                      <UiBadge tone="warning">Kiểm kê mù (Ẩn tồn hệ thống)</UiBadge>
                    </>
                  )}
                </div>
                <div>
                  <strong>Ngày tạo:</strong> {new Date(currentDetail.createdAt).toLocaleString('vi-VN')}
                </div>
                <div>
                  <strong>Người tạo:</strong> {currentDetail.createdByName}
                </div>
                {currentDetail.approvedByName && (
                  <>
                    <div>
                      <strong>Người duyệt:</strong> {currentDetail.approvedByName}
                    </div>
                    <div>
                      <strong>Ngày duyệt:</strong>{' '}
                      {currentDetail.approvedAt ? new Date(currentDetail.approvedAt).toLocaleString('vi-VN') : '—'}
                    </div>
                  </>
                )}
                <div className="wide">
                  <strong>Ghi chú:</strong> {currentDetail.note || '—'}
                </div>
              </div>
            </UiCard>

            <UiCard title="Chi tiết kiểm đếm">
              <UiTableScroll>
                <table
                  className="ui-table-compact stocktake-detail-table"
                  aria-label={'Chi tiết kiểm kê ' + currentDetail.code}
                >
                  <thead>
                    <tr>
                      <th>Mã SP</th>
                      <th>Tên sản phẩm</th>
                      <th>Đơn vị</th>
                      <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        Tồn hệ thống
                      </th>
                      <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        Thực đếm
                      </th>
                      <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        Chênh lệch
                      </th>
                      <th>Ghi chú</th>
                      {normalizeStatusCode(currentDetail.status) === 0 && <th>Hành động</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {currentDetail.details.map((row) => {
                      const isEditing = editingRowId === row.id;
                      const isBlindDraft =
                        normalizeStatusCode(currentDetail.status) === 0 &&
                        Boolean(currentDetail.isBlindCount || currentDetail.note?.toUpperCase().startsWith('[BLIND]'));
                      return (
                        <tr key={row.id}>
                          <td className="ui-cell-nowrap" style={{ whiteSpace: 'nowrap' }}>
                            <strong style={{ whiteSpace: 'nowrap' }}>{row.productCode}</strong>
                            {row.locationCode && (
                              <>
                                <br />
                                <small style={{ whiteSpace: 'nowrap' }}>{row.locationCode}</small>
                              </>
                            )}
                          </td>
                          <td>{row.productName}</td>
                          <td>{row.unitName === 'QA_UNIT' || row.unitName === 'QA-PCS' ? 'THÙNG' : row.unitName}</td>
                          <td
                            className="ui-cell-numeric"
                            style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}
                          >
                            {isBlindDraft ? (
                              <span className="ui-muted-text">Ẩn (Kiểm kê mù)</span>
                            ) : (
                              row.systemQuantity
                            )}
                          </td>
                          <td
                            className="ui-cell-numeric"
                            style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}
                          >
                            {isEditing ? (
                              <input
                                className="stocktake-editor"
                                aria-label={'Số lượng thực tế ' + row.productCode}
                                type="number"
                                step="0.0001"
                                min="0"
                                value={editActualQuantity}
                                onChange={(e) => setEditActualQuantity(e.target.value)}
                                disabled={rowLoading}
                              />
                            ) : row.actualQuantity !== undefined && row.actualQuantity !== null ? (
                              row.actualQuantity
                            ) : (
                              '—'
                            )}
                          </td>
                          <td
                            className="ui-cell-numeric"
                            style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}
                          >
                            {isBlindDraft ? (
                              <span className="stocktake-variance neutral">Ẩn khi đếm mù</span>
                            ) : isEditing ? (
                              renderVariance(row.systemQuantity, editActualQuantity)
                            ) : (
                              renderVariance(row.systemQuantity, row.actualQuantity)
                            )}
                          </td>
                          <td>
                            {isEditing ? (
                              <div className="ui-stack-xs">
                                <select
                                  aria-label={'Mã lý do chênh lệch ' + row.productCode}
                                  value=""
                                  onChange={(e) => {
                                    if (!e.target.value) return;
                                    const prefix = `[REASON: ${e.target.value}] `;
                                    setEditNote((prev) =>
                                      prev.startsWith('[REASON:')
                                        ? prev.replace(/^\[REASON:\s*[^\]]+\]\s*/, prefix)
                                        : prefix + prev,
                                    );
                                  }}
                                  disabled={rowLoading}
                                >
                                  <option value="">-- Chọn nhanh mã lý do chênh lệch --</option>
                                  <option value="COUNT_VARIANCE">Chênh lệch kiểm đếm định kỳ</option>
                                  <option value="DAMAGED_FOUND">Phát hiện hàng hư hỏng vật lý</option>
                                  <option value="EXPIRY_WRITE_OFF">Hủy hàng hết hạn sử dụng</option>
                                  <option value="SUPPLIER_SHORTAGE">Thiếu hụt nguồn giao từ nhà cung cấp</option>
                                  <option value="MISPLACED_STOCK">Sai lệch vị trí lưu kho nội bộ</option>
                                  <option value="RECALL_QUARANTINE">Cô lập thu hồi lô hàng khẩn cấp</option>
                                  <option value="DAMAGED">Hư hỏng</option>
                                  <option value="MISPLACED">Thất lạc / Sai vị trí</option>
                                  <option value="SUPPLIER_SHORT">Thiếu từ nhà cung cấp</option>
                                  <option value="COUNT_ERROR">Sai sót kiểm đếm</option>
                                </select>
                                <input
                                  className="stocktake-editor-note"
                                  aria-label={'Ghi chú kiểm kê ' + row.productCode}
                                  type="text"
                                  value={editNote}
                                  onChange={(e) => setEditNote(e.target.value)}
                                  disabled={rowLoading}
                                />
                              </div>
                            ) : (
                              row.note || '—'
                            )}
                          </td>
                          {normalizeStatusCode(currentDetail.status) === 0 && (
                            <td>
                              {isEditing ? (
                                <div>
                                  <div className="stocktake-row-actions">
                                    <button type="button" onClick={() => void handleRowSave(row)} disabled={rowLoading}>
                                      {rowLoading ? '...' : 'Lưu'}
                                    </button>
                                    <button type="button" onClick={handleRowCancel} disabled={rowLoading}>
                                      Hủy
                                    </button>
                                  </div>
                                  {rowError && (
                                    <div role="alert" className="stocktake-row-error">
                                      {rowError}
                                    </div>
                                  )}
                                </div>
                              ) : (
                                <div className="stocktake-row-actions">
                                  <button type="button" onClick={() => handleRowEdit(row)}>
                                    Nhập SL
                                  </button>
                                  {canApprove && row.actualQuantity !== undefined && row.actualQuantity !== null && (
                                    <button
                                      type="button"
                                      className="ui-btn-secondary"
                                      onClick={() => void handleRecount(row)}
                                      disabled={rowLoading}
                                    >
                                      Yêu cầu đếm lại
                                    </button>
                                  )}
                                  {rowSuccessId === row.id && (
                                    <span role="status" className="stocktake-row-success">
                                      ✓ Đã lưu
                                    </span>
                                  )}
                                </div>
                              )}
                            </td>
                          )}
                        </tr>
                      );
                    })}
                    {currentDetail.details.length === 0 && (
                      <tr>
                        <td
                          colSpan={normalizeStatusCode(currentDetail.status) === 0 ? 8 : 7}
                          className="ui-empty-cell"
                        >
                          Không có sản phẩm nào trong kho này
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </UiTableScroll>
            </UiCard>
          </>
        )}
      </div>
    </UiPage>
  );
};

export default Stocktakes;
