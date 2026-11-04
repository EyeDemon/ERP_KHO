import { useCallback, useEffect, useMemo, useState } from 'react';
import { useInRouterContext } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { sanitizeScannerCode } from './productCatalog';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import {
  emptyWarehouseForm,
  WAREHOUSE_PAGE_SIZE,
  WarehouseFormDrawer,
  warehouseMessageOf,
  WarehousesNetworkOverviewCard,
  WarehousesTopologyTabs,
  type NetworkMheEval,
  type Warehouse,
  type WarehouseForm,
} from '../modules/inventory/WarehousesSections';

const Warehouses = () => {
  const inRouter = useInRouterContext();
  const canManage = usePermission('warehouse.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [search, setSearch] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<WarehouseForm>(emptyWarehouseForm);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [networkMheEval, setNetworkMheEval] = useState<NetworkMheEval | null>(null);

  const evaluateNetworkMhe = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/warehouses/evaluate-network-mhe', {
        warehouseId: warehouses[0]?.id ?? null,
      });
      if (res?.data?.evaluationId && res?.data?.networkTopologyMode) {
        setNetworkMheEval({
          evaluationId: String(res.data.evaluationId),
          networkTopologyMode: String(res.data.networkTopologyMode),
          capacityForecastStatus: String(res.data.capacityForecastStatus || ''),
          mheFleetReadiness: String(res.data.mheFleetReadiness || ''),
        });
      } else {
        setNetworkMheEval(null);
        setError('Phản hồi đánh giá quy hoạch mạng lưới kho và thiết bị MHE không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setNetworkMheEval(null);
      setError(warehouseMessageOf(failure, 'Không thể đánh giá quy hoạch mạng lưới kho và thiết bị MHE.'));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await apiClient.get('/api/v1/warehouses');
      setWarehouses(response.data);
    } catch (failure) {
      setError(warehouseMessageOf(failure, 'Không thể tải danh sách kho.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    const query = sanitizeScannerCode(search).toLocaleLowerCase('vi');
    if (!query) return warehouses;
    return warehouses.filter((warehouse) =>
      [warehouse.code, warehouse.name, warehouse.address ?? '']
        .join(' ')
        .toLocaleLowerCase('vi')
        .includes(query)
    );
  }, [search, warehouses]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / WAREHOUSE_PAGE_SIZE));
  const page = Math.min(currentPage, totalPages);
  const paginated = useMemo(() => {
    const start = (page - 1) * WAREHOUSE_PAGE_SIZE;
    return filtered.slice(start, start + WAREHOUSE_PAGE_SIZE);
  }, [filtered, page]);

  useEffect(() => {
    if (currentPage !== page) setCurrentPage(page);
  }, [currentPage, page]);

  const activeCount = warehouses.filter((warehouse) => warehouse.isActive).length;
  const editing = form.id !== undefined;

  const openCreate = () => {
    setForm(emptyWarehouseForm());
    setFormError('');
    setSuccess('');
    setShowForm(true);
  };

  const openEdit = (warehouse: Warehouse) => {
    setForm({
      id: warehouse.id,
      code: warehouse.code,
      name: warehouse.name,
      address: warehouse.address ?? '',
      isActive: warehouse.isActive,
    });
    setFormError('');
    setSuccess('');
    setShowForm(true);
  };

  const closeForm = () => {
    if (saving) return;
    setShowForm(false);
    setFormError('');
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (saving) return;
    setFormError('');
    setSuccess('');

    const code = sanitizeScannerCode(form.code);
    const name = form.name.trim();
    if (!code) {
      setFormError('Mã kho không được để trống.');
      return;
    }
    if (!name) {
      setFormError('Tên kho không được để trống.');
      return;
    }

    setSaving(true);
    try {
      if (editing && form.id !== undefined) {
        await apiClient.put(`/api/v1/warehouses/${form.id}`, {
          name,
          address: form.address.trim(),
          isActive: form.isActive,
        });
        setSuccess('Cập nhật kho thành công.');
      } else {
        await apiClient.post('/api/v1/warehouses', {
          code,
          name,
          address: form.address.trim(),
        });
        setSuccess('Thêm kho thành công.');
      }
      setShowForm(false);
      setCurrentPage(1);
      await load();
    } catch (failure) {
      setFormError(warehouseMessageOf(failure, 'Không thể lưu kho.'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async (warehouse: Warehouse) => {
    if (!canManage || !window.confirm(`Bạn có chắc muốn xóa kho ${warehouse.code}?`)) return;
    setError('');
    setSuccess('');
    try {
      await apiClient.delete(`/api/v1/warehouses/${warehouse.id}`);
      setSuccess('Xóa kho thành công.');
      setCurrentPage(1);
      await load();
    } catch (failure) {
      setError(warehouseMessageOf(failure, 'Không thể xóa kho.'));
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Kho hàng"
        description="Quản lý mã kho, tên kho, địa chỉ và trạng thái hoạt động. Các thao tác ghi chỉ hiển thị khi người dùng có quyền quản lý kho."
        actions={canManage ? <button type="button" className="ui-primary-button" onClick={openCreate}>+ Thêm kho mới</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Thử lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={warehouses.length} label="Tổng số kho" />
        <UiMetric value={activeCount} label="Đang hoạt động" />
        <UiMetric value={filtered.length} label="Kết quả hiện tại" />
      </UiMetricGrid>

      <WarehousesTopologyTabs inRouter={inRouter} />

      <UiToolbar>
        <UiToolbarField label="Tìm kho">
          <input
            aria-label="Tìm kho"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setCurrentPage(1);
            }}
            placeholder="Mã, tên hoặc địa chỉ"
          />
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">Trang {page}/{totalPages} • {filtered.length} kho</div>
      </UiToolbar>

      {loading ? (
        <p role="status">Đang tải danh sách kho...</p>
      ) : (
        <UiCard title="Danh sách kho">
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Danh sách kho">
              <thead>
                <tr>
                  <th>Mã kho</th>
                  <th>Tên kho</th>
                  <th>Địa chỉ</th>
                  <th>Trạng thái</th>
                  {canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {paginated.map((warehouse) => (
                  <tr key={warehouse.id}>
                    <td style={{ whiteSpace: 'nowrap' }}><strong>{warehouse.code}</strong></td>
                    <td>{warehouse.name}</td>
                    <td>{warehouse.address || '—'}</td>
                    <td><UiBadge tone={warehouse.isActive ? 'success' : 'neutral'}>{warehouse.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                    {canManage && <td style={{ whiteSpace: 'nowrap' }}>
                      <div className="ui-inline-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                        <button type="button" aria-label={`Sửa kho ${warehouse.code}`} onClick={() => openEdit(warehouse)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                        <button type="button" aria-label={`Xóa kho ${warehouse.code}`} onClick={() => void remove(warehouse)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>
                      </div>
                    </td>}
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr>
                    <td className="ui-empty-cell" colSpan={canManage ? 5 : 4}>
                      {warehouses.length === 0 ? 'Chưa có kho nào.' : 'Không tìm thấy kho phù hợp.'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </UiTableScroll>

          {totalPages > 1 && (
            <div className="ui-pagination" aria-label="Phân trang kho">
              <button type="button" disabled={page === 1} onClick={() => setCurrentPage(1)}>Đầu</button>
              <button type="button" disabled={page === 1} onClick={() => setCurrentPage((value) => Math.max(1, value - 1))}>Trước</button>
              <span>Trang {page} / {totalPages}</span>
              <button type="button" disabled={page === totalPages} onClick={() => setCurrentPage((value) => Math.min(totalPages, value + 1))}>Sau</button>
              <button type="button" disabled={page === totalPages} onClick={() => setCurrentPage(totalPages)}>Cuối</button>
            </div>
          )}
        </UiCard>
      )}

      <WarehousesNetworkOverviewCard
        warehouses={warehouses}
        activeCount={activeCount}
        loading={loading}
        saving={saving}
        governanceBusy={governanceBusy}
        networkMheEval={networkMheEval}
        onEvaluateNetworkMhe={() => void evaluateNetworkMhe()}
      />

      <WarehouseFormDrawer
        open={showForm && canManage}
        editing={editing}
        saving={saving}
        form={form}
        setForm={setForm}
        formError={formError}
        onClose={closeForm}
        onSave={(event) => void save(event)}
      />
    </UiPage>
  );
};

export default Warehouses;
