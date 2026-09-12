import React, { useEffect, useMemo, useState } from 'react';
import apiClient from '../services/apiClient';
import { canManageCatalogs, currentRole } from '../services/authorization';
import { categoryChanged, categoryRequest, normalizeBarcodeInput } from './productCatalog';

interface Barcode { id: number; productId: number; value: string }
interface Category { id: number; code: string; name: string; isActive: boolean }
interface Unit { id: number; code: string; name: string; isActive: boolean }
interface Product { id: number; code: string; name: string; description?: string; unitId: number; unitName?: string; categoryId?: number | null; categoryName?: string | null; barcodes: Barcode[]; isActive: boolean }
const messageOf = (error: any, fallback: string) => error.response?.data?.message || fallback;
const PAGE_SIZE = 10;

const Products = () => {
  const canManage = canManageCatalogs(currentRole());
  const [products, setProducts] = useState<Product[]>([]);
  const [units, setUnits] = useState<Unit[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [search, setSearch] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [scan, setScan] = useState('');
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const [editing, setEditing] = useState<Product | null>(null);
  const [form, setForm] = useState({ code: '', name: '', description: '', unitId: 0, categoryId: '' as number | '', isActive: true });
  const [categoryForm, setCategoryForm] = useState({ code: '', name: '' });
  const [barcode, setBarcode] = useState('');

  const load = async () => {
    setLoading(true);
    try {
      const [p, u, c] = await Promise.all([apiClient.get('/api/products'), apiClient.get('/api/units'), apiClient.get('/api/product-categories')]);
      setProducts(p.data); setUnits(u.data); setCategories(c.data);
    } catch (e) { setError(messageOf(e, 'Không thể tải dữ liệu sản phẩm.')); }
    finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, []);
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return q ? products.filter(p => p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q) || p.barcodes?.some(b => b.value.includes(search.trim()))) : products;
  }, [products, search]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const visibleProducts = useMemo(() => filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE), [filtered, currentPage]);
  useEffect(() => { if (currentPage > totalPages) setCurrentPage(totalPages); }, [currentPage, totalPages]);
  const createMode = () => { setEditing(null); setForm({ code: '', name: '', description: '', unitId: units[0]?.id || 0, categoryId: '', isActive: true }); };
  const editMode = (p: Product) => { setEditing(p); setForm({ code: p.code, name: p.name, description: p.description || '', unitId: p.unitId, categoryId: p.categoryId || '', isActive: p.isActive }); setBarcode(''); };

  const saveProduct = async (e: React.FormEvent) => {
    e.preventDefault(); setError('');
    try {
      if (editing) {
        await apiClient.put(`/api/products/${editing.id}`, { name: form.name, description: form.description || null, unitId: form.unitId, isActive: form.isActive });
        if (categoryChanged(editing.categoryId, form.categoryId)) await apiClient.put(`/api/products/${editing.id}/category`, categoryRequest(form.categoryId));
      } else await apiClient.post('/api/products', { ...form, description: form.description || null, ...categoryRequest(form.categoryId) });
      setNotice('Đã lưu sản phẩm.'); createMode(); await load();
    } catch (e) { setError(messageOf(e, 'Không thể lưu sản phẩm.')); }
  };
  const addCategory = async (e: React.FormEvent) => {
    e.preventDefault();
    try { await apiClient.post('/api/product-categories', categoryForm); setCategoryForm({ code: '', name: '' }); await load(); }
    catch (e) { setError(messageOf(e, 'Không thể thêm danh mục.')); }
  };
  const toggleCategory = async (c: Category) => { try { await apiClient.put(`/api/product-categories/${c.id}`, { name: c.name, isActive: !c.isActive }); await load(); } catch (e) { setError(messageOf(e, 'Không thể cập nhật danh mục.')); } };
  const deleteCategory = async (id: number) => { try { await apiClient.delete(`/api/product-categories/${id}`); await load(); } catch (e) { setError(messageOf(e, 'Không thể xóa danh mục.')); } };
  const refreshEditing = async () => { if (editing) editMode((await apiClient.get(`/api/products/${editing.id}`)).data); await load(); };
  const addBarcode = async () => { if (!editing) return; try { await apiClient.post(`/api/products/${editing.id}/barcodes`, { value: normalizeBarcodeInput(barcode) }); await refreshEditing(); } catch (e) { setError(messageOf(e, 'Không thể thêm barcode.')); } };
  const deleteBarcode = async (id: number) => { if (!editing) return; try { await apiClient.delete(`/api/products/${editing.id}/barcodes/${id}`); await refreshEditing(); } catch (e) { setError(messageOf(e, 'Không thể xóa barcode.')); } };
  const deleteProduct = async (id: number) => { if (!window.confirm('Bạn có chắc muốn xóa sản phẩm này?')) return; try { await apiClient.delete(`/api/products/${id}`); setNotice('Đã xóa sản phẩm.'); await load(); } catch (e) { setError(messageOf(e, 'Không thể xóa sản phẩm.')); } };
  const lookup = async (e: React.FormEvent) => { e.preventDefault(); try { const p = (await apiClient.get('/api/product-barcodes/lookup', { params: { value: scan } })).data as Product; setSearch(p.code); setNotice(`Barcode thuộc sản phẩm ${p.code} – ${p.name}.`); } catch (e) { setError(messageOf(e, 'Không tìm thấy barcode.')); } };

  return <div><h2>Quản lý sản phẩm</h2>
    {notice && <p style={{ color: 'green' }}>{notice}</p>}{error && <p style={{ color: 'red' }}>{error}</p>}
    <form onSubmit={lookup}><input aria-label="Tra barcode" value={scan} onChange={e => setScan(e.target.value)} placeholder="Quét hoặc nhập barcode" autoComplete="off" /><button>Tra barcode</button></form>
    <input aria-label="Tìm sản phẩm" value={search} onChange={e => { setSearch(e.target.value); setCurrentPage(1); }} placeholder="Tìm mã, tên hoặc barcode" />
    {canManage && <section><h3>Danh mục sản phẩm</h3><form onSubmit={addCategory}><input value={categoryForm.code} onChange={e => setCategoryForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã danh mục" required /><input value={categoryForm.name} onChange={e => setCategoryForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên danh mục" required /><button>Thêm danh mục</button></form><ul>{categories.map(c => <li key={c.id}>{c.code} – {c.name} ({c.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}) <button onClick={() => toggleCategory(c)}>{c.isActive ? 'Ngừng' : 'Kích hoạt'}</button> <button onClick={() => deleteCategory(c.id)}>Xóa</button></li>)}</ul></section>}
    {canManage && <form onSubmit={saveProduct} style={{ display: 'grid', gap: 8, maxWidth: 620 }}><h3>{editing ? `Sửa ${editing.code}` : 'Thêm sản phẩm'}</h3>
      <input value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã sản phẩm" disabled={!!editing} required /><input value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên sản phẩm" required /><textarea value={form.description} onChange={e => setForm(x => ({ ...x, description: e.target.value }))} placeholder="Mô tả" />
      <select value={form.unitId} onChange={e => setForm(x => ({ ...x, unitId: Number(e.target.value) }))} required><option value={0}>Chọn đơn vị</option>{units.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.code} – {x.name}</option>)}</select>
      <select aria-label="Danh mục" value={form.categoryId} onChange={e => setForm(x => ({ ...x, categoryId: e.target.value ? Number(e.target.value) : '' }))}><option value="">Không có danh mục</option>{categories.filter(c => c.isActive || c.id === editing?.categoryId).map(c => <option key={c.id} value={c.id}>{c.code} – {c.name}{c.isActive ? '' : ' (ngừng hoạt động)'}</option>)}</select>
      {editing && <label><input type="checkbox" checked={form.isActive} onChange={e => setForm(x => ({ ...x, isActive: e.target.checked }))} /> Hoạt động</label>}<div><button>Lưu</button> {editing && <button type="button" onClick={createMode}>Hủy sửa</button>}</div>
      {editing && <fieldset><legend>Barcode</legend>{editing.barcodes?.map(b => <span key={b.id}>{b.value} <button type="button" onClick={() => deleteBarcode(b.id)}>×</button> </span>)}<div><input aria-label="Barcode mới" value={barcode} onChange={e => setBarcode(e.target.value)} maxLength={64} placeholder="Barcode mới" /><button type="button" onClick={addBarcode}>Thêm barcode</button></div></fieldset>}
    </form>}
    {loading ? <p>Đang tải...</p> : <table><thead><tr><th>Mã</th><th>Tên</th><th>Danh mục</th><th>Đơn vị</th><th>Barcode</th><th>Trạng thái</th>{canManage && <th />}</tr></thead><tbody>{visibleProducts.map(p => <tr key={p.id}><td>{p.code}</td><td>{p.name}</td><td>{p.categoryName || '—'}</td><td>{p.unitName || p.unitId}</td><td>{p.barcodes?.map(b => b.value).join(', ') || '—'}</td><td>{p.isActive ? 'Hoạt động' : 'Khóa'}</td>{canManage && <td><button onClick={() => editMode(p)}>Sửa</button> <button onClick={() => deleteProduct(p.id)}>Xóa</button></td>}</tr>)}</tbody></table>}
    <div><button disabled={currentPage === 1} onClick={() => setCurrentPage(x => x - 1)}>Trước</button> Trang {currentPage}/{totalPages} <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(x => x + 1)}>Sau</button></div>
  </div>;
};
export default Products;
