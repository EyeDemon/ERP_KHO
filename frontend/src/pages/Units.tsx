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

interface Unit {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

type UnitForm = {
  id?: number;
  code: string;
  name: string;
  isActive: boolean;
};

const PAGE_SIZE = 10;

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { data?: { message?: string } } };
  return response.response?.data?.message || fallback;
};

const emptyForm = (): UnitForm => ({
  code: '',
  name: '',
  isActive: true,
});

const Units = () => {
  const canManage = usePermission('uom.manage');
  const [units, setUnits] = useState<Unit[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [search, setSearch] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<UnitForm>(emptyForm);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await apiClient.get('/api/units');
      setUnits(response.data);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể tải danh sách đơn vị tính.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('vi');
    if (!query) return units;
    return units.filter((unit) =>
      [unit.code, unit.name].join(' ').toLocaleLowerCase('vi').includes(query)
    );
  }, [search, units]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const page = Math.min(currentPage, totalPages);
  const paginated = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return filtered.slice(start, start + PAGE_SIZE);
  }, [filtered, page]);

  useEffect(() => {
    if (currentPage !== page) setCurrentPage(page);
  }, [currentPage, page]);

  const activeCount = units.filter((unit) => unit.isActive).length;
  const editing = form.id !== undefined;

  const openCreate = () => {
    setForm(emptyForm());
    setFormError('');
    setSuccess('');
    setShowForm(true);
  };

  const openEdit = (unit: Unit) => {
    setForm({
      id: unit.id,
      code: unit.code,
      name: unit.name,
      isActive: unit.isActive,
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
      setFormError('Mã đơn vị tính không được để trống.');
      return;
    }
    if (!name) {
      setFormError('Tên đơn vị tính không được để trống.');
      return;
    }

    setSaving(true);
    try {
      if (editing && form.id !== undefined) {
        await apiClient.put(`/api/units/${form.id}`, {
          name,
          isActive: form.isActive,
        });
        setSuccess('Cập nhật đơn vị tính thành công.');
      } else {
        await apiClient.post('/api/units', {
          code,
          name,
        });
        setSuccess('Thêm đơn vị tính thành công.');
      }
      setShowForm(false);
      setCurrentPage(1);
      await load();
    } catch (failure) {
      setFormError(messageOf(failure, 'Không thể lưu đơn vị tính.'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async (unit: Unit) => {
    if (!canManage || !window.confirm(`Bạn có chắc muốn xóa đơn vị tính ${unit.code}?`)) return;
    setError('');
    setSuccess('');
    try {
      await apiClient.delete(`/api/units/${unit.id}`);
      setSuccess('Xóa đơn vị tính thành công.');
      setCurrentPage(1);
      await load();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể xóa đơn vị tính.'));
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Đơn vị tính"
        description="Quản lý mã và tên đơn vị tính dùng trong chứng từ, tồn kho và quy đổi số lượng."
        actions={canManage ? <button type="button" className="ui-primary-button" onClick={openCreate}>Thêm đơn vị</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Thử lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={units.length} label="Tổng đơn vị tính" />
        <UiMetric value={activeCount} label="Đang hoạt động" />
        <UiMetric value={filtered.length} label="Kết quả hiện tại" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Tìm đơn vị tính">
          <input
            aria-label="Tìm đơn vị tính"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setCurrentPage(1);
            }}
            placeholder="Mã hoặc tên đơn vị"
          />
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">Trang {page}/{totalPages} • {filtered.length} đơn vị</div>
      </UiToolbar>

      {loading ? (
        <p role="status">Đang tải danh sách đơn vị tính...</p>
      ) : (
        <UiCard title="Danh sách đơn vị tính">
          <UiTableScroll>
            <table aria-label="Danh sách đơn vị tính">
              <thead>
                <tr>
                  <th>Mã ĐVT</th>
                  <th>Tên đơn vị</th>
                  <th>Trạng thái</th>
                  {canManage && <th>Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {paginated.map((unit) => (
                  <tr key={unit.id}>
                    <td><strong>{unit.code}</strong></td>
                    <td>{unit.name}</td>
                    <td><UiBadge tone={unit.isActive ? 'success' : 'neutral'}>{unit.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                    {canManage && <td>
                      <div className="ui-inline-actions">
                        <button type="button" aria-label={`Sửa đơn vị ${unit.code}`} onClick={() => openEdit(unit)}>Sửa</button>
                        <button type="button" aria-label={`Xóa đơn vị ${unit.code}`} onClick={() => void remove(unit)}>Xóa</button>
                      </div>
                    </td>}
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr>
                    <td className="ui-empty-cell" colSpan={canManage ? 4 : 3}>
                      {units.length === 0 ? 'Chưa có đơn vị tính nào.' : 'Không tìm thấy đơn vị tính phù hợp.'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </UiTableScroll>

          {totalPages > 1 && (
            <div className="ui-pagination" aria-label="Phân trang đơn vị tính">
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
        <UiCard title={editing ? `Sửa đơn vị ${form.code}` : 'Thêm đơn vị tính'}>
          <form className="ui-form-grid" onSubmit={save}>
            {formError && <p role="alert">{formError}</p>}

            <label className="ui-stack">
              <span>Mã đơn vị tính *</span>
              <input
                aria-label="Mã đơn vị tính"
                value={form.code}
                onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))}
                disabled={saving || editing}
                maxLength={50}
                required
              />
            </label>

            <label className="ui-stack">
              <span>Tên đơn vị tính *</span>
              <input
                aria-label="Tên đơn vị tính"
                value={form.name}
                onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                disabled={saving}
                maxLength={200}
                required
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
                Đơn vị đang hoạt động
              </label>
            )}

            <div className="ui-inline-actions">
              <button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu đơn vị'}</button>
              <button type="button" disabled={saving} onClick={closeForm}>Hủy</button>
            </div>
          </form>
        </UiCard>
      )}
    </UiPage>
  );
};

export default Units;
