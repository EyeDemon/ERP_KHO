import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';

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
  return <div><h2>Quản lý đối tác</h2>{error && <p role="alert" style={{color:'red'}}>{error}</p>}{message && <p role="status" style={{color:'green'}}>{message}</p>}
    {!showForm && <><div style={{display:'flex',gap:8,flexWrap:'wrap'}}><input aria-label="Tìm đối tác" placeholder="Tìm mã hoặc tên" value={search} onChange={e=>{setSearch(e.target.value);setPage(1)}}/><select aria-label="Lọc vai trò" value={role} onChange={e=>{setRole(e.target.value);setPage(1)}}><option value="">Mọi vai trò</option><option value="supplier">Nhà cung cấp</option><option value="customer">Khách hàng</option></select><select aria-label="Lọc trạng thái" value={active} onChange={e=>{setActive(e.target.value);setPage(1)}}><option value="">Mọi trạng thái</option><option value="true">Hoạt động</option><option value="false">Ngừng hoạt động</option></select>{canManage&&<button onClick={openCreate}>Thêm đối tác</button>}</div>
    {loading?<p>Đang tải...</p>:<table style={{width:'100%',marginTop:12}}><thead><tr><th>Mã</th><th>Tên</th><th>Vai trò</th><th>Liên hệ</th><th>Trạng thái</th>{canManage&&<th>Thao tác</th>}</tr></thead><tbody>{items.map(x=><tr key={x.id}><td>{x.code}</td><td>{x.name}</td><td>{[x.isSupplier?'Nhà cung cấp':'',x.isCustomer?'Khách hàng':''].filter(Boolean).join(', ')}</td><td>{x.phone||x.email||'—'}</td><td>{x.isActive?'Hoạt động':'Ngừng hoạt động'}</td>{canManage&&<td><button onClick={()=>openEdit(x)}>Sửa</button> <button onClick={()=>void remove(x)}>Xóa</button></td>}</tr>)}</tbody></table>}<div><button disabled={page<=1} onClick={()=>setPage(p=>p-1)}>Trước</button> <span>Trang {page}/{pages}</span> <button disabled={page>=pages} onClick={()=>setPage(p=>p+1)}>Sau</button></div></>}
    {showForm&&<form onSubmit={submit} style={{maxWidth:600}}><h3>{editing?'Sửa đối tác':'Thêm đối tác'}</h3><label>Mã *<input aria-label="Mã đối tác" value={form.code||''} disabled={!!editing||loading} maxLength={50} onChange={e=>field('code',e.target.value)}/></label><br/><label>Tên *<input aria-label="Tên đối tác" value={form.name||''} disabled={loading} maxLength={200} onChange={e=>field('name',e.target.value)}/></label><br/><label><input type="checkbox" checked={!!form.isSupplier} disabled={loading} onChange={e=>field('isSupplier',e.target.checked)}/> Nhà cung cấp</label> <label><input type="checkbox" checked={!!form.isCustomer} disabled={loading} onChange={e=>field('isCustomer',e.target.checked)}/> Khách hàng</label> <label><input type="checkbox" checked={!!form.isActive} disabled={loading} onChange={e=>field('isActive',e.target.checked)}/> Hoạt động</label><br/><label>Điện thoại<input value={form.phone||''} maxLength={50} onChange={e=>field('phone',e.target.value)}/></label><br/><label>Email<input value={form.email||''} maxLength={254} onChange={e=>field('email',e.target.value)}/></label><br/><label>Địa chỉ<textarea value={form.address||''} maxLength={500} onChange={e=>field('address',e.target.value)}/></label><br/><button type="submit" disabled={loading}>{loading?'Đang lưu...':'Lưu'}</button> <button type="button" disabled={loading} onClick={()=>setShowForm(false)}>Hủy</button></form>}
  </div>;
};
export default BusinessPartners;
