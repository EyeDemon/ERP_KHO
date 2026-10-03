import React, { useState, useEffect, useMemo } from 'react';
import apiClient from '../services/apiClient';
import { currentRole, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';
import './Stocktakes.css';

interface StocktakeSummary {
  id: number;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: number;
  note?: string;
  createdBy: number;
  createdByName: string;
  approvedBy?: number;
  approvedByName?: string;
  createdAt: string;
  approvedAt?: string;
  detailCount: number;
}

interface StocktakeDetailRow {
  id: number;
  stocktakeId: number;
  productId: number;
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

interface Warehouse {
  id: number;
  name: string;
  isActive: boolean;
}

const PAGE_SIZE = 10;

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
  const [createNote, setCreateNote] = useState('');
  const [formLoading, setFormLoading] = useState(false);
  const [formError, setFormError] = useState('');

  // Edit Row State
  const [editingRowId, setEditingRowId] = useState<number | null>(null);
  const [editActualQuantity, setEditActualQuantity] = useState<number | string>('');
  const [editNote, setEditNote] = useState('');
  const [rowLoading, setRowLoading] = useState(false);
  const [rowError, setRowError] = useState('');
  const [rowSuccessId, setRowSuccessId] = useState<number | null>(null);

  const fetchStocktakes = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.get('/api/stocktakes');
      setStocktakes(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải danh sách phiếu kiểm kê');
    } finally {
      setLoading(false);
    }
  };

  const fetchWarehouses = async () => {
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
    fetchStocktakes();
    fetchWarehouses();
  }, []);

  const getStatusText = (status: number) => {
    switch (status) {
      case 0: return 'Bản nháp';
      case 1: return 'Đã duyệt';
      case 2: return 'Đã hủy';
      default: return 'Không xác định';
    }
  };

  const filteredStocktakes = useMemo(() => {
    return stocktakes.filter(s => {
      const matchSearch = searchTerm ? 
        (s.code.toLowerCase().includes(searchTerm.toLowerCase()) || 
         s.warehouseName.toLowerCase().includes(searchTerm.toLowerCase())) 
        : true;
      const matchStatus = statusFilter !== '' ? s.status === parseInt(statusFilter) : true;
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
      const payload = {
        warehouseId: createWarehouseId,
        note: createNote
      };
      const action = `stocktake-create:${JSON.stringify(payload)}`;
      const res = await apiClient.post('/api/stocktakes', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(res.data.message || 'Tạo phiếu kiểm kê thành công.');
      await fetchStocktakes();
      if (res.data.id) {
        loadDetail(res.data.id, true);
      } else {
        setViewMode('list');
      }
    } catch (err: any) {
      setFormError(err.response?.data?.message || 'Lỗi khi tạo phiếu kiểm kê.');
    } finally {
      setFormLoading(false);
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
    const val = Number(editActualQuantity);
    if (isNaN(val) || val < 0) {
      setRowError('Số lượng thực tế phải lớn hơn hoặc bằng 0.');
      return;
    }

    setRowLoading(true);
    setRowError('');
    try {
      const payload = {
        actualQuantity: val,
        note: editNote
      };
      const action = `stocktake-detail:${currentDetail?.id}:${row.id}:${JSON.stringify(payload)}`;
      await apiClient.put(`/api/stocktakes/${currentDetail?.id}/details/${row.id}`, payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      // Refresh detail
      const res = await apiClient.get(`/api/stocktakes/${currentDetail?.id}`);
      setCurrentDetail(res.data);
      setEditingRowId(null);
      setRowSuccessId(row.id);
    } catch (err: any) {
      setRowError(err.response?.data?.message || 'Lỗi khi cập nhật chi tiết.');
    } finally {
      setRowLoading(false);
    }
  };

  const handleApprove = async () => {
    if (!currentDetail) return;
    const hasUnentered = currentDetail.details.some(d => d.actualQuantity === null || d.actualQuantity === undefined);
    if (hasUnentered) {
      alert('Tất cả sản phẩm phải được nhập Số lượng thực tế trước khi duyệt.');
      return;
    }
    if (!confirm('Bạn có chắc chắn muốn duyệt phiếu kiểm kê này? Thao tác này sẽ cập nhật trực tiếp tồn kho và không thể hoàn tác.')) return;
    
    setLoading(true);
    setError('');
    try {
      const action = `stocktake-approve:${currentDetail.id}`;
      await apiClient.post(`/api/stocktakes/${currentDetail.id}/approve`, undefined, { headers: idempotencyHeaders(action) });
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

  const statusTone = (status: number): 'neutral' | 'success' | 'warning' | 'danger' => {
    if (status === 0) return 'warning';
    if (status === 1) return 'success';
    if (status === 2) return 'danger';
    return 'neutral';
  };

  const renderVariance = (systemQty: number, actualQty?: number | string | null) => {
    if (actualQty === null || actualQty === undefined || actualQty === '') return <span className="stocktake-variance neutral">Chưa nhập</span>;
    const actual = Number(actualQty);
    if (isNaN(actual)) return <span className="stocktake-variance neutral">Lỗi</span>;

    const diff = actual - systemQty;
    if (diff > 0) return <span className="stocktake-variance success">+{diff} (Thừa)</span>;
    if (diff < 0) return <span className="stocktake-variance danger">{diff} (Thiếu)</span>;
    return <span className="stocktake-variance neutral">0 (Khớp)</span>;
  };

  return (
    <UiPage>
      <div className="stocktakes-page">
        {viewMode === 'list' && (
          <>
            <UiPageHeader
              eyebrow="Inventory control"
              title="Kiểm kê kho"
              description="Tạo đợt kiểm kê, nhập số lượng thực tế, theo dõi chênh lệch và duyệt điều chỉnh tồn kho."
              actions={<button type="button" className="ui-primary-button" onClick={handleAddNew}>Tạo phiếu kiểm kê</button>}
            />

            {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}
            {error && <p role="alert">{error} <button type="button" onClick={() => void fetchStocktakes()}>Thử lại</button></p>}

            <UiToolbar>
              <UiToolbarField label="Tìm kiếm">
                <input
                  aria-label="Tìm phiếu kiểm kê"
                  type="text"
                  placeholder="Tìm mã phiếu hoặc tên kho..."
                  value={searchTerm}
                  onChange={handleSearchChange}
                />
              </UiToolbarField>
              <UiToolbarField label="Trạng thái">
                <select aria-label="Lọc trạng thái kiểm kê" value={statusFilter} onChange={handleStatusFilterChange}>
                  <option value="">Tất cả trạng thái</option>
                  <option value="0">Bản nháp</option>
                  <option value="1">Đã duyệt</option>
                  <option value="2">Đã hủy</option>
                </select>
              </UiToolbarField>
              <div className="ui-muted-text ui-auto-actions">Trang {currentPage}/{totalPages} • {filteredStocktakes.length} phiếu</div>
            </UiToolbar>

            <UiCard title="Danh sách phiếu kiểm kê">
              {loading ? (
                <p role="status">Đang tải...</p>
              ) : (
                <UiTableScroll>
                  <table aria-label="Danh sách phiếu kiểm kê">
                    <thead>
                      <tr>
                        <th>Mã phiếu</th>
                        <th>Kho</th>
                        <th>Ngày tạo</th>
                        <th>Trạng thái</th>
                        <th>SL SP</th>
                        <th>Ghi chú</th>
                        <th>Hành động</th>
                      </tr>
                    </thead>
                    <tbody>
                      {paginatedStocktakes.map(s => (
                        <tr key={s.id}>
                          <td><strong>{s.code}</strong></td>
                          <td>{s.warehouseName}</td>
                          <td>{new Date(s.createdAt).toLocaleString('vi-VN')}</td>
                          <td><UiBadge tone={statusTone(s.status)}>{getStatusText(s.status)}</UiBadge></td>
                          <td>{s.detailCount}</td>
                          <td className="stocktake-note" title={s.note || ''}>{s.note || '—'}</td>
                          <td><button type="button" onClick={() => void loadDetail(s.id)}>Xem chi tiết</button></td>
                        </tr>
                      ))}
                      {filteredStocktakes.length === 0 && (
                        <tr><td colSpan={7} className="ui-empty-cell">Không tìm thấy phiếu kiểm kê nào</td></tr>
                      )}
                    </tbody>
                  </table>
                </UiTableScroll>
              )}

              {totalPages > 1 && (
                <div className="ui-pagination" aria-label="Phân trang kiểm kê">
                  <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage(1)}>Đầu</button>
                  <button type="button" disabled={currentPage === 1} onClick={() => setCurrentPage(p => p - 1)}>Trước</button>
                  <span>Trang {currentPage} / {totalPages}</span>
                  <button type="button" disabled={currentPage === totalPages} onClick={() => setCurrentPage(p => p + 1)}>Sau</button>
                  <button type="button" disabled={currentPage === totalPages} onClick={() => setCurrentPage(totalPages)}>Cuối</button>
                </div>
              )}
            </UiCard>
          </>
        )}

        {viewMode === 'create' && (
          <>
            <UiPageHeader
              eyebrow="Inventory control"
              title="Tạo phiếu kiểm kê mới"
              description="Chọn kho cần kiểm kê; hệ thống sẽ tạo danh sách sản phẩm theo tồn hiện tại của kho."
            />

            <UiCard title="Thông tin kiểm kê">
              <form className="stocktake-form" onSubmit={handleCreateSubmit}>
                {formError && <p role="alert">{formError}</p>}

                <label className="ui-stack">
                  <span>Kho hàng *</span>
                  {warehousesLoading ? (
                    <span role="status" className="ui-muted-text">Đang tải danh sách kho...</span>
                  ) : warehousesError ? (
                    <span role="alert">{warehousesError} <button type="button" onClick={() => void fetchWarehouses()}>Thử lại</button></span>
                  ) : warehouses.length === 0 ? (
                    <span className="stocktake-empty-warning">Không có kho đang hoạt động.</span>
                  ) : (
                    <select
                      aria-label="Kho hàng kiểm kê"
                      value={createWarehouseId}
                      onChange={(e) => setCreateWarehouseId(e.target.value ? parseInt(e.target.value) : '')}
                      disabled={formLoading}
                      required
                    >
                      <option value="">-- Chọn kho hàng --</option>
                      {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                    </select>
                  )}
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
                  <button type="submit" disabled={formLoading || warehousesLoading || !!warehousesError || warehouses.length === 0}>
                    {formLoading ? 'Đang tạo...' : 'Tạo mới'}
                  </button>
                  <button type="button" onClick={handleCancelForm} disabled={formLoading}>Hủy</button>
                </div>
              </form>
            </UiCard>
          </>
        )}

        {viewMode === 'detail' && currentDetail && (
          <>
            <UiPageHeader
              eyebrow="Inventory control"
              title={'Chi tiết kiểm kê: ' + currentDetail.code}
              description="Nhập số lượng thực tế cho từng sản phẩm và duyệt phiếu khi toàn bộ dòng đã được kiểm đếm."
              actions={
                <div className="stocktake-page-actions">
                  <button type="button" onClick={() => setViewMode('list')}>Quay lại danh sách</button>
                  {currentDetail.status === 0 && canApprove && currentDetail.createdBy !== userId && (
                    <button type="button" className="ui-primary-button" onClick={() => void handleApprove()} disabled={loading}>
                      {loading ? 'Đang xử lý...' : 'Duyệt phiếu'}
                    </button>
                  )}
                </div>
              }
            />

            {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}
            {error && <p role="alert">{error}</p>}

            <UiCard title="Thông tin phiếu">
              <div className="stocktake-detail-meta">
                <div><strong>Kho:</strong> {currentDetail.warehouseName}</div>
                <div><strong>Trạng thái:</strong> <UiBadge tone={statusTone(currentDetail.status)}>{getStatusText(currentDetail.status)}</UiBadge></div>
                <div><strong>Ngày tạo:</strong> {new Date(currentDetail.createdAt).toLocaleString('vi-VN')}</div>
                <div><strong>Người tạo:</strong> {currentDetail.createdByName}</div>
                {currentDetail.approvedByName && (
                  <>
                    <div><strong>Người duyệt:</strong> {currentDetail.approvedByName}</div>
                    <div><strong>Ngày duyệt:</strong> {currentDetail.approvedAt ? new Date(currentDetail.approvedAt).toLocaleString('vi-VN') : '—'}</div>
                  </>
                )}
                <div className="wide"><strong>Ghi chú:</strong> {currentDetail.note || '—'}</div>
              </div>
            </UiCard>

            <UiCard title="Chi tiết kiểm đếm">
              <UiTableScroll>
                <table aria-label={'Chi tiết kiểm kê ' + currentDetail.code}>
                  <thead>
                    <tr>
                      <th>Mã SP</th>
                      <th>Tên SP</th>
                      <th>ĐVT</th>
                      <th>Tồn hệ thống</th>
                      <th>Thực tế</th>
                      <th>Chênh lệch</th>
                      <th>Ghi chú</th>
                      {currentDetail.status === 0 && <th>Hành động</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {currentDetail.details.map(row => {
                      const isEditing = editingRowId === row.id;
                      return (
                        <tr key={row.id}>
                          <td><strong>{row.productCode}</strong></td>
                          <td>{row.productName}</td>
                          <td>{row.unitName}</td>
                          <td>{row.systemQuantity}</td>
                          <td>
                            {isEditing ? (
                              <input
                                className="stocktake-editor"
                                aria-label={'Số lượng thực tế ' + row.productCode}
                                type="number"
                                step="0.01"
                                min="0"
                                value={editActualQuantity}
                                onChange={(e) => setEditActualQuantity(e.target.value)}
                                disabled={rowLoading}
                              />
                            ) : (
                              row.actualQuantity !== undefined && row.actualQuantity !== null ? row.actualQuantity : '—'
                            )}
                          </td>
                          <td>{isEditing ? renderVariance(row.systemQuantity, editActualQuantity) : renderVariance(row.systemQuantity, row.actualQuantity)}</td>
                          <td>
                            {isEditing ? (
                              <input
                                className="stocktake-editor-note"
                                aria-label={'Ghi chú kiểm kê ' + row.productCode}
                                type="text"
                                value={editNote}
                                onChange={(e) => setEditNote(e.target.value)}
                                disabled={rowLoading}
                              />
                            ) : (
                              row.note || '—'
                            )}
                          </td>
                          {currentDetail.status === 0 && (
                            <td>
                              {isEditing ? (
                                <div>
                                  <div className="stocktake-row-actions">
                                    <button type="button" onClick={() => void handleRowSave(row)} disabled={rowLoading}>{rowLoading ? '...' : 'Lưu'}</button>
                                    <button type="button" onClick={handleRowCancel} disabled={rowLoading}>Hủy</button>
                                  </div>
                                  {rowError && <div role="alert" className="stocktake-row-error">{rowError}</div>}
                                </div>
                              ) : (
                                <div className="stocktake-row-actions">
                                  <button type="button" onClick={() => handleRowEdit(row)}>Nhập SL</button>
                                  {rowSuccessId === row.id && <span role="status" className="stocktake-row-success">✓ Đã lưu</span>}
                                </div>
                              )}
                            </td>
                          )}
                        </tr>
                      );
                    })}
                    {currentDetail.details.length === 0 && (
                      <tr><td colSpan={currentDetail.status === 0 ? 8 : 7} className="ui-empty-cell">Không có sản phẩm nào trong kho này</td></tr>
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
