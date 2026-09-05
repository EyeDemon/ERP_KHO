import React, { useState, useEffect, useMemo } from 'react';
import apiClient from '../services/apiClient';
import { canManageCatalogs, currentRole, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';

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
  const canApprove = canManageCatalogs(currentRole());
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

  const renderVariance = (systemQty: number, actualQty?: number | string | null) => {
    if (actualQty === null || actualQty === undefined || actualQty === '') return <span style={{ color: '#7f8c8d' }}>Chưa nhập</span>;
    const actual = Number(actualQty);
    if (isNaN(actual)) return <span style={{ color: '#7f8c8d' }}>Lỗi</span>;
    
    const diff = actual - systemQty;
    if (diff > 0) return <span style={{ color: '#27ae60', fontWeight: 'bold' }}>+{diff} (Thừa)</span>;
    if (diff < 0) return <span style={{ color: '#c0392b', fontWeight: 'bold' }}>{diff} (Thiếu)</span>;
    return <span style={{ color: '#7f8c8d' }}>0 (Khớp)</span>;
  };

  return (
    <div>
      {viewMode === 'list' && (
        <>
          <h2>Kiểm kê kho</h2>
          {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
          {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error} <button onClick={fetchStocktakes}>Thử lại</button></div>}
          
          <div style={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: '10px', marginBottom: '15px' }}>
            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', flex: 1 }}>
              <input 
                type="text" 
                placeholder="Tìm mã phiếu hoặc tên kho..." 
                value={searchTerm}
                onChange={handleSearchChange}
                style={{ padding: '8px', minWidth: '200px', flex: '1', maxWidth: '300px' }}
              />
              <select 
                value={statusFilter} 
                onChange={handleStatusFilterChange}
                style={{ padding: '8px' }}
              >
                <option value="">Tất cả trạng thái</option>
                <option value="0">Bản nháp</option>
                <option value="1">Đã duyệt</option>
                <option value="2">Đã hủy</option>
              </select>
            </div>
            <button onClick={handleAddNew} style={{ padding: '8px 16px', backgroundColor: '#3498db', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer' }}>
              Tạo phiếu kiểm kê
            </button>
          </div>

          {loading ? <div>Đang tải...</div> : (
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '10px' }}>
                <thead>
                  <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã phiếu</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ngày tạo</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>SL SP</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ghi chú</th>
                    <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {paginatedStocktakes.map(s => (
                    <tr key={s.id}>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{s.code}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{s.warehouseName}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{new Date(s.createdAt).toLocaleString()}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{getStatusText(s.status)}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{s.detailCount}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7', maxWidth: '200px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{s.note}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        <button onClick={() => loadDetail(s.id)} style={{ cursor: 'pointer' }}>Xem chi tiết</button>
                      </td>
                    </tr>
                  ))}
                  {filteredStocktakes.length === 0 && (
                    <tr><td colSpan={7} style={{ textAlign: 'center', padding: '20px' }}>Không tìm thấy phiếu kiểm kê nào</td></tr>
                  )}
                </tbody>
              </table>

              {totalPages > 1 && (
                <div style={{ marginTop: '15px', display: 'flex', justifyContent: 'center', gap: '10px', flexWrap: 'wrap' }}>
                  <button disabled={currentPage === 1} onClick={() => setCurrentPage(1)}>&laquo;</button>
                  <button disabled={currentPage === 1} onClick={() => setCurrentPage(p => p - 1)}>&lsaquo;</button>
                  <span>Trang {currentPage} / {totalPages}</span>
                  <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(p => p + 1)}>&rsaquo;</button>
                  <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(totalPages)}>&raquo;</button>
                </div>
              )}
            </div>
          )}
        </>
      )}

      {viewMode === 'create' && (
        <div style={{ border: '1px solid #bdc3c7', padding: '20px', borderRadius: '4px', maxWidth: '600px' }}>
          <h3>Tạo phiếu kiểm kê mới</h3>
          {formError && <div style={{ color: 'red', marginBottom: '15px' }}>{formError}</div>}
          <form onSubmit={handleCreateSubmit}>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Kho hàng <span style={{ color: 'red' }}>*</span></label>
              {warehousesLoading ? (
                <div style={{ color: '#7f8c8d' }}>Đang tải danh sách kho...</div>
              ) : warehousesError ? (
                <div style={{ color: 'red' }}>{warehousesError} <button type="button" onClick={fetchWarehouses} style={{ cursor: 'pointer' }}>Thử lại</button></div>
              ) : warehouses.length === 0 ? (
                <div style={{ color: '#e67e22', padding: '8px 0' }}>Không có kho đang hoạt động.</div>
              ) : (
                <select 
                  value={createWarehouseId} 
                  onChange={(e) => setCreateWarehouseId(e.target.value ? parseInt(e.target.value) : '')}
                  style={{ width: '100%', padding: '8px' }}
                  disabled={formLoading}
                >
                  <option value="">-- Chọn kho hàng --</option>
                  {warehouses.map(w => (
                    <option key={w.id} value={w.id}>{w.name}</option>
                  ))}
                </select>
              )}
            </div>
            <div style={{ marginBottom: '15px' }}>
              <label style={{ display: 'block', marginBottom: '5px' }}>Ghi chú</label>
              <textarea 
                value={createNote} 
                onChange={(e) => setCreateNote(e.target.value)}
                style={{ width: '100%', padding: '8px', minHeight: '60px' }}
                disabled={formLoading}
              />
            </div>
            <div style={{ display: 'flex', gap: '10px' }}>
              <button type="submit" disabled={formLoading || warehousesLoading || !!warehousesError || warehouses.length === 0} style={{ padding: '8px 16px', backgroundColor: '#2ecc71', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer' }}>
                {formLoading ? 'Đang tạo...' : 'Tạo mới'}
              </button>
              <button type="button" onClick={handleCancelForm} disabled={formLoading} style={{ padding: '8px 16px', cursor: 'pointer' }}>Hủy</button>
            </div>
          </form>
        </div>
      )}

      {viewMode === 'detail' && currentDetail && (
        <div>
          <button onClick={() => setViewMode('list')} style={{ marginBottom: '15px', padding: '6px 12px', cursor: 'pointer' }}>&larr; Quay lại danh sách</button>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', marginBottom: '15px' }}>
            <h2 style={{ margin: 0 }}>Chi tiết kiểm kê: {currentDetail.code}</h2>
            {currentDetail.status === 0 && canApprove && currentDetail.createdBy !== userId && (
              <button onClick={handleApprove} disabled={loading} style={{ padding: '8px 16px', backgroundColor: '#e67e22', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer' }}>
                {loading ? 'Đang xử lý...' : 'Duyệt phiếu'}
              </button>
            )}
          </div>

          {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
          {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '20px', backgroundColor: '#fff', padding: '15px', borderRadius: '4px', border: '1px solid #bdc3c7', marginBottom: '20px' }}>
            <div><strong>Kho:</strong> {currentDetail.warehouseName}</div>
            <div><strong>Trạng thái:</strong> {getStatusText(currentDetail.status)}</div>
            <div><strong>Ngày tạo:</strong> {new Date(currentDetail.createdAt).toLocaleString()}</div>
            <div><strong>Người tạo:</strong> {currentDetail.createdByName}</div>
            {currentDetail.approvedByName && (
              <>
                <div><strong>Người duyệt:</strong> {currentDetail.approvedByName}</div>
                <div><strong>Ngày duyệt:</strong> {currentDetail.approvedAt ? new Date(currentDetail.approvedAt).toLocaleString() : ''}</div>
              </>
            )}
            <div style={{ width: '100%' }}><strong>Ghi chú:</strong> {currentDetail.note || '-'}</div>
          </div>

          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã SP</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên SP</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>ĐVT</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tồn hệ thống</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7', minWidth: '150px' }}>Thực tế</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Chênh lệch</th>
                  <th style={{ padding: '10px', border: '1px solid #bdc3c7', minWidth: '200px' }}>Ghi chú</th>
                  {currentDetail.status === 0 && <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>}
                </tr>
              </thead>
              <tbody>
                {currentDetail.details.map(row => {
                  const isEditing = editingRowId === row.id;
                  return (
                    <tr key={row.id}>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{row.productCode}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{row.productName}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{row.unitName}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{row.systemQuantity}</td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        {isEditing ? (
                          <input 
                            type="number" 
                            step="0.01" 
                            min="0"
                            value={editActualQuantity}
                            onChange={(e) => setEditActualQuantity(e.target.value)}
                            style={{ width: '80px', padding: '4px' }}
                            disabled={rowLoading}
                          />
                        ) : (
                          row.actualQuantity !== undefined && row.actualQuantity !== null ? row.actualQuantity : '-'
                        )}
                      </td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        {isEditing ? renderVariance(row.systemQuantity, editActualQuantity) : renderVariance(row.systemQuantity, row.actualQuantity)}
                      </td>
                      <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                        {isEditing ? (
                          <input 
                            type="text" 
                            value={editNote}
                            onChange={(e) => setEditNote(e.target.value)}
                            style={{ width: '100%', padding: '4px', boxSizing: 'border-box' }}
                            disabled={rowLoading}
                          />
                        ) : (
                          row.note || '-'
                        )}
                      </td>
                      {currentDetail.status === 0 && (
                        <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                          {isEditing ? (
                            <>
                              <button onClick={() => handleRowSave(row)} disabled={rowLoading} style={{ color: 'green', marginRight: '5px', cursor: 'pointer' }}>{rowLoading ? '...' : 'Lưu'}</button>
                              <button onClick={handleRowCancel} disabled={rowLoading} style={{ color: 'red', cursor: 'pointer' }}>Hủy</button>
                              {rowError && <div style={{ color: 'red', fontSize: '12px', marginTop: '4px' }}>{rowError}</div>}
                            </>
                          ) : (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '5px' }}>
                              <button onClick={() => handleRowEdit(row)} style={{ cursor: 'pointer' }}>Nhập SL</button>
                              {rowSuccessId === row.id && <span style={{ color: 'green', fontSize: '12px' }}>✓ Đã lưu</span>}
                            </div>
                          )}
                        </td>
                      )}
                    </tr>
                  );
                })}
                {currentDetail.details.length === 0 && (
                  <tr><td colSpan={currentDetail.status === 0 ? 8 : 7} style={{ textAlign: 'center', padding: '20px' }}>Không có sản phẩm nào trong kho này</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
};

export default Stocktakes;
