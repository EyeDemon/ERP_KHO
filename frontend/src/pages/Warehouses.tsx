import { useCallback, useEffect, useMemo, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
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

interface Warehouse {
  id: number;
  code: string;
  name: string;
  address?: string;
  isActive: boolean;
}

type WarehouseForm = {
  id?: number;
  code: string;
  name: string;
  address: string;
  isActive: boolean;
};

const PAGE_SIZE = 10;

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { data?: { message?: string } } };
  return response.response?.data?.message || fallback;
};

const emptyForm = (): WarehouseForm => ({
  code: '',
  name: '',
  address: '',
  isActive: true,
});

const Warehouses = () => {
  const canManage = usePermission('warehouse.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [search, setSearch] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<WarehouseForm>(emptyForm);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await apiClient.get('/api/warehouses');
      setWarehouses(response.data);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể tải danh sách kho.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('vi');
    if (!query) return warehouses;
    return warehouses.filter((warehouse) =>
      [warehouse.code, warehouse.name, warehouse.address ?? '']
        .join(' ')
        .toLocaleLowerCase('vi')
        .includes(query)
    );
  }, [search, warehouses]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const page = Math.min(currentPage, totalPages);
  const paginated = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return filtered.slice(start, start + PAGE_SIZE);
  }, [filtered, page]);

  useEffect(() => {
    if (currentPage !== page) setCurrentPage(page);
  }, [currentPage, page]);

  const activeCount = warehouses.filter((warehouse) => warehouse.isActive).length;
  const editing = form.id !== undefined;

  const openCreate = () => {
    setForm(emptyForm());
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

    const code = form.code.trim();
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
        await apiClient.put(`/api/warehouses/${form.id}`, {
          name,
          address: form.address.trim(),
          isActive: form.isActive,
        });
        setSuccess('Cập nhật kho thành công.');
      } else {
        await apiClient.post('/api/warehouses', {
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
      setFormError(messageOf(failure, 'Không thể lưu kho.'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async (warehouse: Warehouse) => {
    if (!canManage || !window.confirm(`Bạn có chắc muốn xóa kho ${warehouse.code}?`)) return;
    setError('');
    setSuccess('');
    try {
      await apiClient.delete(`/api/warehouses/${warehouse.id}`);
      setSuccess('Xóa kho thành công.');
      setCurrentPage(1);
      await load();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể xóa kho.'));
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Kho hàng"
        description="Quản lý mã kho, tên kho, địa chỉ và trạng thái hoạt động. Các thao tác ghi chỉ hiển thị khi người dùng có quyền quản lý kho."
        actions={canManage ? <button type="button" className="ui-primary-button" onClick={openCreate}>Thêm kho</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Thử lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={warehouses.length} label="Tổng số kho" />
        <UiMetric value={activeCount} label="Đang hoạt động" />
        <UiMetric value={filtered.length} label="Kết quả hiện tại" />
      </UiMetricGrid>

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
            <table aria-label="Danh sách kho">
              <thead>
                <tr>
                  <th>Mã kho</th>
                  <th>Tên kho</th>
                  <th>Địa chỉ</th>
                  <th>Trạng thái</th>
                  {canManage && <th>Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {paginated.map((warehouse) => (
                  <tr key={warehouse.id}>
                    <td><strong>{warehouse.code}</strong></td>
                    <td>{warehouse.name}</td>
                    <td>{warehouse.address || '—'}</td>
                    <td><UiBadge tone={warehouse.isActive ? 'success' : 'neutral'}>{warehouse.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                    {canManage && <td>
                      <div className="ui-inline-actions">
                        <button type="button" aria-label={`Sửa kho ${warehouse.code}`} onClick={() => openEdit(warehouse)}>Sửa</button>
                        <button type="button" aria-label={`Xóa kho ${warehouse.code}`} onClick={() => void remove(warehouse)}>Xóa</button>
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

      {showForm && canManage && (
        <UiCard title={editing ? `Sửa kho ${form.code}` : 'Thêm kho'}>
          <form className="ui-form-grid" onSubmit={save}>
            {formError && <p role="alert">{formError}</p>}

            <label className="ui-stack">
              <span>Mã kho *</span>
              <input
                aria-label="Mã kho"
                value={form.code}
                onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))}
                disabled={saving || editing}
                maxLength={50}
                required
              />
            </label>

            <label className="ui-stack">
              <span>Tên kho *</span>
              <input
                aria-label="Tên kho"
                value={form.name}
                onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                disabled={saving}
                maxLength={200}
                required
              />
            </label>

            <label className="ui-stack">
              <span>Địa chỉ</span>
              <textarea
                aria-label="Địa chỉ kho"
                value={form.address}
                onChange={(event) => setForm((current) => ({ ...current, address: event.target.value }))}
                disabled={saving}
                rows={3}
              />
            </label>

            {editing && (
              <label className="ui-checkbox-label">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(event) => setForm((current) => ({ ...current, isActive: event.target.checked }))}
                  disabled={saving}
                />
                Kho đang hoạt động
              </label>
            )}

            <div className="ui-inline-actions">
              <button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu kho'}</button>
              <button type="button" disabled={saving} onClick={closeForm}>Hủy</button>
            </div>
          </form>
        </UiCard>
      )}
    </UiPage>
  );
};

export default Warehouses;
