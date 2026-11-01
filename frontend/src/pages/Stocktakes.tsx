import React, { useState, useEffect, useMemo, useCallback } from 'react';
import apiClient from '../services/apiClient';
import { currentRole, currentUserId, hasPermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { roundQty4 } from './productCatalog';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import type {
  LocationOption, StocktakeAutoApprovalItem, StocktakeDetailRow,
  StocktakeResponse, StocktakeSummary, Warehouse,
} from '../modules/inventory-pages/stocktakesShared';
import {
  PAGE_SIZE, getStatusBadgeStyle, getStatusText, mapAutoApprovalRows, normalizeStatusCode,
} from '../modules/inventory-pages/stocktakesShared';
import { StocktakesListAndPolicySection } from '../modules/inventory-pages/StocktakesListAndPolicySection';
import { StocktakesCreateSection, StocktakesDetailSection } from '../modules/inventory-pages/StocktakesCreateAndDetailSection';
import './Stocktakes.css';

export { getStatusText, getStatusBadgeStyle };

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
  const [viewMode, setViewMode] = useState<'list' | 'create' | 'detail'>('list');
  const [currentDetail, setCurrentDetail] = useState<StocktakeResponse | null>(null);
  const [createWarehouseId, setCreateWarehouseId] = useState<number | ''>('');
  const [createLocationId, setCreateLocationId] = useState<number | ''>('');
  const [createIsBlindCount, setCreateIsBlindCount] = useState(false);
  const [createLocations, setCreateLocations] = useState<LocationOption[]>([]);
  const [createNote, setCreateNote] = useState('');
  const [formLoading, setFormLoading] = useState(false);
  const [formError, setFormError] = useState('');
  const [freezeLoading, setFreezeLoading] = useState(false);
  const [editingRowId, setEditingRowId] = useState<number | null>(null);
  const [editActualQuantity, setEditActualQuantity] = useState<number | string>('');
  const [editNote, setEditNote] = useState('');
  const [rowLoading, setRowLoading] = useState(false);
  const [rowError, setRowError] = useState('');
  const [rowSuccessId, setRowSuccessId] = useState<number | null>(null);
  const [proposalStatus, setProposalStatus] = useState<string>('');
  const [proposalBusy, setProposalBusy] = useState(false);
  const [proposalProductCode, setProposalProductCode] = useState<string>('');
  const [proposalLocationCode, setProposalLocationCode] = useState<string>('');
  const [proposalDeltaQty, setProposalDeltaQty] = useState<number>(-1);
  const [proposalReasonCode, setProposalReasonCode] = useState<string>('COUNT_VARIANCE');
  const [stocktakeSectionTab, setStocktakeSectionTab] = useState<'list' | 'policy'>('list');
  const [policyExpanded, setPolicyExpanded] = useState<boolean>(true);
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
        const dRec = data as Record<string, unknown>;
        const rawList = Array.isArray(data)
          ? data
          : Array.isArray(dRec?.items)
            ? (dRec.items as unknown[])
            : Array.isArray(dRec?.attempts)
              ? (dRec.attempts as unknown[])
              : Array.isArray(dRec?.lines)
                ? (dRec.lines as unknown[])
                : [];
        setAutoApprovalRows(mapAutoApprovalRows(rawList, fallbackDetails, isBlind));
      } catch {
        setAutoApprovalRows(mapAutoApprovalRows([], fallbackDetails, isBlind));
      }
    },
    [],
  );

  const fetchStocktakes = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/v1/stocktakes');
      const list = Array.isArray(res.data) ? res.data : [];
      setStocktakes(list);
      if (list.length > 0) void loadCountAttemptsAndAutoApproval(list[0].id, undefined, Boolean(list[0].isBlindCount));
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
      const res = await apiClient.get('/api/v1/warehouses');
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
      .get(`/api/v1/putaway-tasks/locations?warehouseId=${createWarehouseId}`)
      .then((res) => setCreateLocations(Array.isArray(res.data) ? res.data : []))
      .catch((err: any) => {
        setCreateLocations([]);
        if (err?.response?.status) setFormError(err?.response?.data?.message || 'Không thể tải danh sách vị trí kiểm kê.');
      });
  }, [createWarehouseId]);

  const loadDetail = async (id: number, preserveSuccess = false) => {
    setLoading(true);
    setError('');
    if (!preserveSuccess) setSuccessMsg('');
    setEditingRowId(null);
    setRowError('');
    setRowSuccessId(null);
    try {
      const res = await apiClient.get(`/api/v1/stocktakes/${id}`);
      setCurrentDetail(res.data);
      setViewMode('detail');
      void loadCountAttemptsAndAutoApproval(id, Array.isArray(res.data?.details) ? res.data.details : [], Boolean(res.data?.isBlindCount));
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải chi tiết phiếu kiểm kê');
      setViewMode('list');
    } finally {
      setLoading(false);
    }
  };

  const handleEvaluateAutoApproval = async () => {
    if (autoApprovalBusy) return;
    setStocktakeSectionTab('policy');
    setPolicyExpanded(true);
    setAutoApprovalBusy(true);
    setError('');
    try {
      let resolvedId = currentDetail?.id ?? stocktakes[0]?.id;
      if (!resolvedId) {
        const listRes = await apiClient.get('/api/v1/stocktakes').catch(() => ({ data: [] as StocktakeSummary[] }));
        const list = Array.isArray(listRes.data) ? listRes.data : [];
        if (list.length > 0) {
          setStocktakes(list);
          resolvedId = list[0].id;
        }
      }
      const effectiveId = resolvedId ?? targetStocktakeId;
      const res = await apiClient.post<Record<string, unknown>>(`/api/v1/stocktakes/${effectiveId}/evaluate-auto-approval`, {
        stocktakeId: effectiveId, maxVariancePct: 1.0, maxVarianceValueVnd: 500000,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const rawList = Array.isArray(d.items) ? d.items : Array.isArray(d.lines) ? d.lines : Array.isArray(d.evaluations) ? d.evaluations : [];
      const mapped = mapAutoApprovalRows(rawList, currentDetail?.details, Boolean(currentDetail?.isBlindCount));
      setAutoApprovalRows(mapped);
      const autoCount = Number(d.autoApprovedCount ?? mapped.filter((r) => r.autoApproved).length);
      const manualCount = Number(d.manualApprovalRequiredCount ?? mapped.filter((r) => !r.autoApproved).length);
      setAutoApprovalBanner(
        String(d.message ?? `Đã đánh giá tự động duyệt phiếu #${targetStocktakeId}: ${autoCount} dòng đủ điều kiện Tự động duyệt (<1% & ≤500k), ${manualCount} dòng Cần Giám đốc kho duyệt.`),
      );
      await fetchStocktakes();
      if (currentDetail) await loadDetail(currentDetail.id, true);
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
      const res = await apiClient.post('/api/v1/stocktakes/adjustment-proposals', {
        warehouseId: targetWh,
        productCode: proposalProductCode || 'QA-E2E-001',
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

  const filteredStocktakes = useMemo(() => {
    return stocktakes.filter((s) => {
      const matchSearch = searchTerm
        ? s.code.toLowerCase().includes(searchTerm.toLowerCase()) || s.warehouseName.toLowerCase().includes(searchTerm.toLowerCase())
        : true;
      const matchStatus = statusFilter !== '' ? normalizeStatusCode(s.status) === parseInt(statusFilter, 10) : true;
      return matchSearch && matchStatus;
    });
  }, [stocktakes, searchTerm, statusFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredStocktakes.length / PAGE_SIZE));
  useEffect(() => { if (currentPage > totalPages) setCurrentPage(totalPages); }, [totalPages, currentPage]);

  const paginatedStocktakes = useMemo(() => {
    const start = (currentPage - 1) * PAGE_SIZE;
    return filteredStocktakes.slice(start, start + PAGE_SIZE);
  }, [filteredStocktakes, currentPage]);

  const handleAddNew = () => {
    setCreateWarehouseId(''); setCreateLocationId(''); setCreateIsBlindCount(false);
    setCreateNote(''); setFormError(''); setSuccessMsg(''); setViewMode('create');
  };

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(''); setSuccessMsg('');
    if (createWarehouseId === '') { setFormError('Vui lòng chọn kho hàng.'); return; }
    setFormLoading(true);
    try {
      const payload: Record<string, unknown> = {
        warehouseId: createWarehouseId, note: createNote,
        ...(createLocationId !== '' ? { locationId: Number(createLocationId) } : {}),
        ...(createIsBlindCount ? { isBlindCount: true } : {}),
      };
      const action = `stocktake-create:${JSON.stringify(payload)}`;
      const res = await apiClient.post('/api/v1/stocktakes', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(res.data.message || 'Tạo phiếu kiểm kê thành công.');
      await fetchStocktakes();
      if (res.data.id) await loadDetail(res.data.id, true);
      else setViewMode('list');
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi tạo phiếu kiểm kê.');
    } finally {
      setFormLoading(false);
    }
  };

  const handleCountFreeze = async () => {
    if (!currentDetail || freezeLoading) return;
    setFreezeLoading(true); setError(''); setSuccessMsg('');
    try {
      const distinctLocations = Array.from(new Set(currentDetail.details.map((d) => d.locationId).filter((x): x is number => typeof x === 'number')));
      const locationId = distinctLocations.length === 1 ? distinctLocations[0] : undefined;
      const key = `stocktake-count-freeze:${currentDetail.id}`;
      await apiClient.post(
        '/api/v1/inventory/locks',
        { lockType: 'CountFreeze', warehouseId: currentDetail.warehouseId, ...(locationId ? { locationId } : {}), reason: `Đóng băng kiểm kê cho phiếu ${currentDetail.code}` },
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

  const handleRowEdit = (row: StocktakeDetailRow) => {
    setEditingRowId(row.id);
    setEditActualQuantity(row.actualQuantity !== undefined && row.actualQuantity !== null ? row.actualQuantity : '');
    setEditNote(row.note || ''); setRowError(''); setRowSuccessId(null);
  };

  const handleRowSave = async (row: StocktakeDetailRow) => {
    if (editActualQuantity === '') { setRowError('Số lượng thực tế không được để trống.'); return; }
    const val = roundQty4(Number(editActualQuantity));
    if (isNaN(val) || val < 0) { setRowError('Số lượng thực tế phải lớn hơn hoặc bằng 0.'); return; }
    setRowLoading(true); setRowError('');
    try {
      const payload = { actualQuantity: val, note: editNote };
      const action = `stocktake-detail:${currentDetail?.id}:${row.id}:${JSON.stringify(payload)}`;
      await apiClient.put(`/api/v1/stocktakes/${currentDetail?.id}/details/${row.id}`, payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      const res = await apiClient.get(`/api/v1/stocktakes/${currentDetail?.id}`);
      setCurrentDetail(res.data);
      if (currentDetail?.id) {
        void loadCountAttemptsAndAutoApproval(currentDetail.id, Array.isArray(res.data?.details) ? res.data.details : [], Boolean(res.data?.isBlindCount));
      }
      setEditingRowId(null); setRowSuccessId(row.id);
    } catch (err: any) {
      setRowError(err.response?.data?.message || 'Lỗi khi cập nhật chi tiết.');
    } finally {
      setRowLoading(false);
    }
  };

  const handleRecount = async (row: StocktakeDetailRow) => {
    if (!currentDetail || rowLoading) return;
    setRowLoading(true); setError(''); setSuccessMsg('');
    try {
      const action = `stocktake-recount:${currentDetail.id}:${row.id}`;
      await apiClient.post(`/api/v1/stocktakes/${currentDetail.id}/details/${row.id}/recount`, { reason: 'Yêu cầu đếm lại chênh lệch kiểm kê' }, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      const res = await apiClient.get(`/api/v1/stocktakes/${currentDetail.id}`);
      setCurrentDetail(res.data);
      void loadCountAttemptsAndAutoApproval(currentDetail.id, Array.isArray(res.data?.details) ? res.data.details : [], Boolean(res.data?.isBlindCount));
      setSuccessMsg(`Đã yêu cầu kiểm đếm lại sản phẩm ${row.productCode}.`);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Không thể gửi yêu cầu kiểm đếm lại.');
    } finally {
      setRowLoading(false);
    }
  };

  const handleApprove = async () => {
    if (!currentDetail) return;
    if (currentDetail.details.some((d) => d.actualQuantity === null || d.actualQuantity === undefined)) {
      setError('Tất cả sản phẩm phải được nhập Số lượng thực tế trước khi duyệt.');
      return;
    }
    if (!confirm('Bạn có chắc chắn muốn duyệt phiếu kiểm kê này? Thao tác này sẽ cập nhật trực tiếp tồn kho và không thể hoàn tác.')) return;
    setLoading(true); setError('');
    try {
      const action = `stocktake-approve:${currentDetail.id}`;
      await apiClient.post(`/api/v1/stocktakes/${currentDetail.id}/approve`, undefined, { headers: idempotencyHeaders(action) });
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

  return (
    <UiPage>
      <div className="stocktakes-page">
        {(viewMode === 'list' || viewMode === 'detail') && (
          <>
            <UiPageHeader
              eyebrow="Kiểm soát tồn kho"
              title="Kiểm kê kho"
              description="Tạo đợt kiểm kê, nhập số lượng thực tế, theo dõi chênh lệch và duyệt điều chỉnh tồn kho."
              actions={<button type="button" className="ui-primary-button" onClick={handleAddNew}>Tạo phiếu kiểm kê</button>}
            />
            {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}
            {error && <p role="alert">{error} <button type="button" onClick={() => void fetchStocktakes()}>Thử lại</button></p>}

            <StocktakesListAndPolicySection
              stocktakes={stocktakes} filteredStocktakes={filteredStocktakes} paginatedStocktakes={paginatedStocktakes}
              loading={loading} searchTerm={searchTerm} onSearchChange={(e) => { setSearchTerm(e.target.value); setCurrentPage(1); }}
              statusFilter={statusFilter} onStatusFilterChange={(e) => { setStatusFilter(e.target.value); setCurrentPage(1); }}
              currentPage={currentPage} totalPages={totalPages} setCurrentPage={setCurrentPage}
              onLoadDetail={(id) => void loadDetail(id)} stocktakeSectionTab={stocktakeSectionTab}
              setStocktakeSectionTab={setStocktakeSectionTab} policyExpanded={policyExpanded} setPolicyExpanded={setPolicyExpanded}
              autoApprovalRows={autoApprovalRows} autoApprovalBusy={autoApprovalBusy} autoApprovalBanner={autoApprovalBanner}
              onEvaluateAutoApproval={() => void handleEvaluateAutoApproval()}
              onReloadAutoApproval={() => void loadCountAttemptsAndAutoApproval(targetStocktakeId, currentDetail?.details, Boolean(currentDetail?.isBlindCount))}
              currentDetail={currentDetail} proposalProductCode={proposalProductCode} setProposalProductCode={setProposalProductCode}
              proposalLocationCode={proposalLocationCode} setProposalLocationCode={setProposalLocationCode}
              proposalDeltaQty={proposalDeltaQty} setProposalDeltaQty={setProposalDeltaQty}
              proposalReasonCode={proposalReasonCode} setProposalReasonCode={setProposalReasonCode}
              proposalBusy={proposalBusy} proposalStatus={proposalStatus}
              onQuickAdjustmentProposal={() => void handleQuickAdjustmentProposal()}
            />
          </>
        )}

        {viewMode === 'create' && (
          <StocktakesCreateSection
            warehouses={warehouses} warehousesLoading={warehousesLoading} warehousesError={warehousesError}
            onRetryWarehouses={() => void fetchWarehouses()} createWarehouseId={createWarehouseId} setCreateWarehouseId={setCreateWarehouseId}
            createLocationId={createLocationId} setCreateLocationId={setCreateLocationId}
            createLocations={createLocations} createIsBlindCount={createIsBlindCount} setCreateIsBlindCount={setCreateIsBlindCount}
            createNote={createNote} setCreateNote={setCreateNote} formLoading={formLoading} formError={formError}
            onSubmit={(e) => void handleCreateSubmit(e)} onCancel={() => { setViewMode('list'); setFormError(''); }}
          />
        )}

        {viewMode === 'detail' && currentDetail && (
          <StocktakesDetailSection
            currentDetail={currentDetail} canApprove={canApprove} userId={userId} loading={loading} freezeLoading={freezeLoading}
            onBackToList={() => setViewMode('list')} onCountFreeze={() => void handleCountFreeze()} onApprove={() => void handleApprove()}
            editingRowId={editingRowId} editActualQuantity={editActualQuantity} setEditActualQuantity={setEditActualQuantity}
            editNote={editNote} setEditNote={setEditNote} rowLoading={rowLoading} rowError={rowError} rowSuccessId={rowSuccessId}
            onRowEdit={handleRowEdit} onRowSave={(row) => void handleRowSave(row)}
            onRowCancel={() => { setEditingRowId(null); setRowError(''); }} onRecount={(row) => void handleRecount(row)}
          />
        )}
      </div>
    </UiPage>
  );
};

export default Stocktakes;
