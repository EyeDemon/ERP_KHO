import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

export interface BusinessPartner {
  id: number; code: string; name: string; isSupplier: boolean; isCustomer: boolean;
  isActive: boolean; phone?: string; email?: string; address?: string; rowVersion: string;
}

const empty = { code: '', name: '', isSupplier: true, isCustomer: false, isActive: true, phone: '', email: '', address: '', rowVersion: '' };

const BusinessPartners = () => {
  const canManage = usePermission('partner.update');
  const [items, setItems] = useState<BusinessPartner[]>([]);
  const [page, setPage] = useState(1); const [pages, setPages] = useState(1);
  const [search, setSearch] = useState(''); const [role, setRole] = useState(''); const [active, setActive] = useState('');
  const [form, setForm] = useState<Partial<BusinessPartner>>(empty); const [editing, setEditing] = useState<number | null>(null);
  const [showForm, setShowForm] = useState(false); const [error, setError] = useState(''); const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false); const submitting = useRef(false);
  const load = useCallback(async () => { setLoading(true); setError(''); try { const r = await apiClient.get('/api/business-partners', { params: { page, pageSize: 10, search: search || undefined, role: role || undefined, active: active === '' ? undefined : active === 'true' } }); setItems(r.data.items); setPages(Math.max(1, r.data.totalPages)); } catch (e: any) { setError(e.response?.data?.message || 'Không tải được danh sách đối tác.'); } finally { setLoading(false); } }, [page, search, role, active]);
  useEffect(() => { void load(); }, [load]);
  const openCreate = () => { setEditing(null); setForm(empty); setShowForm(true); setError(''); setMessage(''); };
  const openEdit = (x: BusinessPartner) => { setEditing(x.id); setForm(x); setShowForm(true); setError(''); setMessage(''); };
  const submit = async (e: React.FormEvent) => { e.preventDefault(); if (submitting.current) return; submitting.current = true; setLoading(true); setError(''); try { if (editing) await apiClient.put(`/api/business-partners/${editing}`, form); else await apiClient.post('/api/business-partners', form); setMessage(editing ? 'Đã cập nhật đối tác.' : 'Đã tạo đối tác.'); setShowForm(false); await load(); } catch (x: any) { const message=x.response?.data?.message || 'Không lưu được đối tác.'; setError(x.response?.status===409 ? `${message} Vui lòng tải lại danh sách rồi thử lại.` : message); } finally { submitting.current = false; setLoading(false); } };
  const remove = async (x: BusinessPartner) => { if (!confirm(`Xóa đối tác ${x.code}?`)) return; try { await apiClient.delete(`/api/business-partners/${x.id}`); setMessage('Đã xóa đối tác.'); await load(); } catch (e: any) { setError(e.response?.data?.message || 'Không xóa được đối tác.'); } };
  const field = (name: keyof BusinessPartner, value: unknown) => setForm(v => ({ ...v, [name]: value }));
  return <UiPage>
    <UiPageHeader
      eyebrow="Dữ liệu nền"
      title="Đối tác"
      description="Quản lý nhà cung cấp, khách hàng, vai trò nghiệp vụ và thông tin liên hệ dùng trên các chứng từ kho."
      actions={canManage && !showForm ? <button type="button" className="ui-primary-button" onClick={openCreate}>Thêm đối tác</button> : undefined}
    />

    {error && <p role="alert">{error}</p>}
    {message && <p role="status" style={{ color: '#256b45', margin: 0 }}>{message}</p>}

    {!showForm && <>
      <UiToolbar>
        <UiToolbarField label="Tìm đối tác">
          <input aria-label="Tìm đối tác" placeholder="Mã hoặc tên" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
        </UiToolbarField>
        <UiToolbarField label="Vai trò">
          <select aria-label="Lọc vai trò" value={role} onChange={event => { setRole(event.target.value); setPage(1); }}>
            <option value="">Mọi vai trò</option>
            <option value="supplier">Nhà cung cấp</option>
            <option value="customer">Khách hàng</option>
          </select>
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Lọc trạng thái" value={active} onChange={event => { setActive(event.target.value); setPage(1); }}>
            <option value="">Mọi trạng thái</option>
            <option value="true">Hoạt động</option>
            <option value="false">Ngừng hoạt động</option>
          </select>
        </UiToolbarField>
        <div style={{ marginLeft: 'auto', alignSelf: 'center', color: '#66788d', fontSize: 10 }}>Trang {page}/{pages}</div>
      </UiToolbar>

      <UiCard title="Danh sách đối tác">
        {loading ? <p role="status">Đang tải đối tác...</p> : <table>
          <thead><tr><th>Mã</th><th>Tên</th><th>Vai trò</th><th>Liên hệ</th><th>Trạng thái</th>{canManage && <th>Thao tác</th>}</tr></thead>
          <tbody>
            {items.length === 0
              ? <tr><td colSpan={canManage ? 6 : 5} style={{ textAlign: 'center', padding: 22, color: '#6b7b90' }}>Không có đối tác phù hợp với bộ lọc hiện tại.</td></tr>
              : items.map(item => <tr key={item.id}>
                  <td><strong>{item.code}</strong></td>
                  <td><div>{item.name}</div>{item.address && <small>{item.address}</small>}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
                      {item.isSupplier && <UiBadge>Nhà cung cấp</UiBadge>}
                      {item.isCustomer && <UiBadge>Khách hàng</UiBadge>}
                      {!item.isSupplier && !item.isCustomer && <span>—</span>}
                    </div>
                  </td>
                  <td><div>{item.phone || '—'}</div>{item.email && <small>{item.email}</small>}</td>
                  <td><UiBadge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge></td>
                  {canManage && <td><div style={{ display: 'flex', gap: 6 }}><button onClick={() => openEdit(item)}>Sửa</button><button onClick={() => void remove(item)}>Xóa</button></div></td>}
                </tr>)}
          </tbody>
        </table>}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 9, marginTop: 10 }}>
          <button disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Trước</button>
          <span style={{ color: '#66788d', fontSize: 11 }}>Trang {page}/{pages}</span>
          <button disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Sau</button>
        </div>
      </UiCard>
    </>}

    {showForm && <UiCard title={editing ? 'Sửa đối tác' : 'Thêm đối tác'}>
      <form onSubmit={submit} style={{ display: 'grid', gap: 10, maxWidth: 720 }}>
        <label>Mã đối tác *<input aria-label="Mã đối tác" value={form.code || ''} disabled={!!editing || loading} maxLength={50} onChange={event => field('code', event.target.value)} /></label>
        <label>Tên đối tác *<input aria-label="Tên đối tác" value={form.name || ''} disabled={loading} maxLength={200} onChange={event => field('name', event.target.value)} /></label>
        <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap' }}>
          <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}><input type="checkbox" checked={!!form.isSupplier} disabled={loading} onChange={event => field('isSupplier', event.target.checked)} /> Nhà cung cấp</label>
          <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}><input type="checkbox" checked={!!form.isCustomer} disabled={loading} onChange={event => field('isCustomer', event.target.checked)} /> Khách hàng</label>
          <label style={{ display: 'flex', gap: 6, alignItems: 'center' }}><input type="checkbox" checked={!!form.isActive} disabled={loading} onChange={event => field('isActive', event.target.checked)} /> Hoạt động</label>
        </div>
        <label>Điện thoại<input value={form.phone || ''} maxLength={50} onChange={event => field('phone', event.target.value)} /></label>
        <label>Email<input value={form.email || ''} maxLength={254} onChange={event => field('email', event.target.value)} /></label>
        <label>Địa chỉ<textarea value={form.address || ''} maxLength={500} rows={3} onChange={event => field('address', event.target.value)} /></label>
        <div style={{ display: 'flex', gap: 7 }}>
          <button type="submit" disabled={loading}>{loading ? 'Đang lưu...' : 'Lưu'}</button>
          <button type="button" disabled={loading} onClick={() => setShowForm(false)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </UiPage>;
};
export default BusinessPartners;
