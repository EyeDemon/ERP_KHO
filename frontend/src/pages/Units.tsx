import { useCallback, useEffect, useMemo, useState } from 'react';
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
    const query = sanitizeScannerCode(search).toLocaleLowerCase('vi');
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

    const code = sanitizeScannerCode(form.code);
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

  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [uomIntegrity, setUomIntegrity] = useState<{ verificationId: string; status: string } | null>(null);
  const [catchWeightColdChainResult, setCatchWeightColdChainResult] = useState('');

  const verifyUomIntegrity = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/Units/verify-uom-integrity', {});
      if (res?.data?.verificationId && res?.data?.status) {
        setUomIntegrity({
          verificationId: String(res.data.verificationId),
          status: String(res.data.status),
        });
      } else {
        setUomIntegrity(null);
        setError('Phản hồi kiểm định hệ số quy đổi ĐVT không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setUomIntegrity(null);
      setError(messageOf(failure, 'Không thể kiểm định hệ số quy đổi và bất biến ĐVT cơ sở.'));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const verifyCatchWeightColdChain = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/Units/verify-catch-weight-coldchain', {});
      const data = res?.data as { verificationId?: string; status?: string } | undefined;
      if (data?.verificationId && data?.status) {
        setCatchWeightColdChainResult(`${data.verificationId} • ${({ CATCH_WEIGHT_COLDCHAIN_VALIDATED: 'Đã chuẩn hóa tồn kho hai ĐVT, chuỗi lạnh & hàng nguy hiểm', DUAL_UOM_COLDCHAIN_READY: 'Sẵn sàng tồn kho hai ĐVT & chuỗi lạnh' } as Record<string, string>)[data.status] || data.status}`);
      } else {
        setCatchWeightColdChainResult('');
        setError('Phản hồi kiểm định trọng lượng biến thiên và chuỗi lạnh không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setCatchWeightColdChainResult('');
      setError(messageOf(failure, 'Không thể kiểm định tồn kho hai đơn vị tính (trọng lượng biến thiên) và chuỗi lạnh.'));
    } finally {
      setGovernanceBusy(false);
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

      <UiCard title="Tổng quan Hệ thống Đơn vị tính & Quy đổi">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, maxWidth: '100%' }}>
          <div className="ui-subcard">
            <strong>Tỷ lệ đơn vị tính kích hoạt</strong><br />
            {activeCount}/{units.length} ĐVT hoạt động ({units.length > 0 ? Math.round((activeCount / units.length) * 100) : 0}%)
          </div>
          <div className="ui-subcard">
            <strong>Đơn vị tính ngừng hoạt động (bảo toàn lịch sử)</strong><br />
            {units.length - activeCount} ĐVT ngừng hoạt động
          </div>
          <div className="ui-subcard">
            <strong>Phạm vi hiển thị theo bộ lọc</strong><br />
            {filtered.length}/{units.length} ĐVT khớp điều kiện tra cứu
          </div>
        </div>
        <div className="ui-inline-wrap">
          <button type="button" disabled={loading || saving || governanceBusy} onClick={() => void verifyUomIntegrity()}>
            Kiểm định Hệ số Quy đổi & Bất biến ĐVT Cơ sở
          </button>
          {uomIntegrity && (
            <UiBadge tone="success">
              {`Đã kiểm định ${uomIntegrity.verificationId} • ${({ UOM_HIERARCHY_VALIDATED: 'Đã chuẩn hóa hệ số quy đổi & bất biến ĐVT cơ sở', BASE_UOM_IMMUTABLE: 'ĐVT cơ sở bất biến hợp lệ' } as Record<string, string>)[uomIntegrity.status] || uomIntegrity.status}`}
            </UiBadge>
          )}
          <button type="button" disabled={loading || saving || governanceBusy} onClick={() => void verifyCatchWeightColdChain()}>
            Kiểm định Tồn kho Hai ĐVT (Trọng lượng biến thiên), Chuỗi lạnh &amp; Hàng nguy hiểm
          </button>
          {catchWeightColdChainResult && (
            <UiBadge tone="success">{catchWeightColdChainResult}</UiBadge>
          )}
        </div>
      </UiCard>

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
            <table className="ui-table-compact" aria-label="Danh sách đơn vị tính">
              <thead>
                <tr>
                  <th>Mã ĐVT</th>
                  <th>Tên đơn vị</th>
                  <th>Trạng thái</th>
                  {canManage && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {paginated.map((unit) => (
                  <tr key={unit.id}>
                    <td style={{ whiteSpace: 'nowrap' }}><strong>{unit.code}</strong></td>
                    <td>{unit.name}</td>
                    <td><UiBadge tone={unit.isActive ? 'success' : 'neutral'}>{unit.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                    {canManage && <td style={{ whiteSpace: 'nowrap' }}>
                      <div className="ui-inline-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                        <button type="button" aria-label={`Sửa đơn vị ${unit.code}`} onClick={() => openEdit(unit)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                        <button type="button" aria-label={`Xóa đơn vị ${unit.code}`} onClick={() => void remove(unit)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>
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
          <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }} onSubmit={save}>
            {formError && <p role="alert">{formError}</p>}

            <label className="ui-stack">
              <span>Mã đơn vị tính *</span>
              <input
                aria-label="Mã đơn vị tính"
                value={form.code}
                onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))}
                disabled={saving || editing}
                maxLength={50} placeholder="VD: THUNG" style={{ height: 38, boxSizing: 'border-box' }}
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
                maxLength={200} placeholder="Nhập tên đơn vị tính" style={{ height: 38, boxSizing: 'border-box' }}
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
