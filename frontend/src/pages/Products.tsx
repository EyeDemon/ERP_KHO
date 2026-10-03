import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { categoryChanged, categoryRequest, normalizeBarcodeInput } from './productCatalog';
import { permissionError } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

interface Barcode { id: number; productId: number; value: string }
interface Category { id: number; code: string; name: string; isActive: boolean }
interface Unit { id: number; code: string; name: string; isActive: boolean }
interface Product { id: number; code: string; name: string; description?: string; unitId: number; unitName?: string; categoryId?: number | null; categoryName?: string | null; barcodes: Barcode[]; isActive: boolean }
const messageOf = permissionError;
const PAGE_SIZE = 10;

const Products = () => {
  const canManage = usePermission('product.update');
  const canRead = usePermission('product.read');
  const canReadUnits = usePermission('uom.read');
  const canCreate = usePermission('product.create');
  const canDeactivate = usePermission('product.deactivate');
  const canReadCategories = usePermission('product_category.read');
  const canManageCategories = usePermission('product_category.manage');
  const canManageBarcodes = usePermission('product_barcode.manage');
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
  const [categorySearch, setCategorySearch] = useState('');
  const [editingCategory, setEditingCategory] = useState<Category | null>(null);
  const [barcode, setBarcode] = useState('');
  const mutationLock = useRef(false);
  const [mutating, setMutating] = useState(false);

  const mutate = async (action: () => Promise<void>) => {
    if (mutationLock.current) return;
    mutationLock.current = true; setMutating(true); setError('');
    try { await action(); }
    finally { mutationLock.current = false; setMutating(false); }
  };

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [p, u, c] = await Promise.all([canRead ? apiClient.get('/api/products') : Promise.resolve({ data: [] }), canReadUnits ? apiClient.get('/api/units') : Promise.resolve({ data: [] }), canReadCategories ? apiClient.get('/api/product-categories') : Promise.resolve({ data: [] })]);
      setProducts(hasPermission('product.read') ? p.data : []); setUnits(hasPermission('uom.read') ? u.data : []); setCategories(hasPermission('product_category.read') ? c.data : []);
    } catch (e) { setProducts([]); setCategories([]); setUnits([]); setEditing(null); setError(messageOf(e, 'Không thể tải dữ liệu sản phẩm.')); }
    finally { setLoading(false); }
  }, [canRead, canReadUnits, canReadCategories]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { setEditing(null); setEditingCategory(null); setNotice(''); }, [canRead, canManage, canManageBarcodes, canManageCategories]);
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
    e.preventDefault(); if (editing ? !canManage : !canCreate) return; await mutate(async () => {
      try {
      if (editing) {
        await apiClient.put(`/api/products/${editing.id}`, { name: form.name, description: form.description || null, unitId: form.unitId, isActive: form.isActive });
        if (categoryChanged(editing.categoryId, form.categoryId)) await apiClient.put(`/api/products/${editing.id}/category`, categoryRequest(form.categoryId));
      } else await apiClient.post('/api/products', { ...form, description: form.description || null, ...categoryRequest(form.categoryId) });
      setNotice('Đã lưu sản phẩm.'); createMode(); await load();
      } catch (e) { setError(messageOf(e, 'Không thể lưu sản phẩm.')); }
    });
  };
  const addCategory = async (e: React.FormEvent) => {
    e.preventDefault(); if (!canManageCategories) return; await mutate(async () => {
      try {
        if (editingCategory) await apiClient.put(`/api/product-categories/${editingCategory.id}`, { name: categoryForm.name, isActive: editingCategory.isActive });
        else await apiClient.post('/api/product-categories', categoryForm);
        setCategoryForm({ code: '', name: '' }); setEditingCategory(null); await load();
      } catch (e) { setError(messageOf(e, editingCategory ? 'Không thể cập nhật danh mục.' : 'Không thể thêm danh mục.')); }
    });
  };
  const editCategory = (c: Category) => { setEditingCategory(c); setCategoryForm({ code: c.code, name: c.name }); };
  const toggleCategory = async (c: Category) => mutate(async () => { try { await apiClient.put(`/api/product-categories/${c.id}`, { name: c.name, isActive: !c.isActive }); await load(); } catch (e) { setError(messageOf(e, 'Không thể cập nhật danh mục.')); } });
  const deleteCategory = async (id: number) => mutate(async () => { try { await apiClient.delete(`/api/product-categories/${id}`); await load(); } catch (e) { setError(messageOf(e, 'Không thể xóa danh mục.')); } });
  const refreshEditing = async () => { if (editing) editMode((await apiClient.get(`/api/products/${editing.id}`)).data); await load(); };
  const addBarcode = async () => { if (!editing || !canManageBarcodes) return; await mutate(async () => { try { await apiClient.post(`/api/products/${editing.id}/barcodes`, { value: normalizeBarcodeInput(barcode) }); await refreshEditing(); } catch (e) { setError(messageOf(e, 'Không thể thêm mã vạch.')); } }); };
  const deleteBarcode = async (id: number) => { if (!editing || !canManageBarcodes) return; await mutate(async () => { try { await apiClient.delete(`/api/products/${editing.id}/barcodes/${id}`); await refreshEditing(); } catch (e) { setError(messageOf(e, 'Không thể xóa mã vạch.')); } }); };
  const deleteProduct = async (id: number) => { if (!canDeactivate || !window.confirm('Bạn có chắc muốn xóa sản phẩm này?')) return; await mutate(async () => { try { await apiClient.delete(`/api/products/${id}`); setNotice('Đã xóa sản phẩm.'); await load(); } catch (e) { setError(messageOf(e, 'Không thể xóa sản phẩm.')); } }); };
  const lookup = async (e: React.FormEvent) => { e.preventDefault(); setError(''); setNotice(''); try { const p = (await apiClient.get('/api/product-barcodes/lookup', { params: { value: scan } })).data as Product; setSearch(p.code); setNotice(`Mã vạch thuộc sản phẩm ${p.code} – ${p.name}.`); } catch (e) { setSearch(''); setError(messageOf(e, 'Không tìm thấy mã vạch.')); } };

  return <UiPage>
    <UiPageHeader
      eyebrow="Dữ liệu nền"
      title="Sản phẩm"
      description="Quản lý SKU, danh mục, đơn vị tính và barcode. Tìm kiếm và tra mã vạch dùng cùng dữ liệu sản phẩm."
    />

    {notice && <p role="status" className="ui-success-text">{notice}</p>}
    {error && <p role="alert">{error}</p>}
    {mutating && <p role="status">Đang xử lý...</p>}

    <UiToolbar>
      <UiToolbarField label="Tra mã vạch">
        <form onSubmit={lookup} className="ui-inline">
          <input aria-label="Tra mã vạch" value={scan} onChange={e => setScan(e.target.value)} placeholder="Quét hoặc nhập mã vạch" autoComplete="off" />
          <button type="submit">Tra mã vạch</button>
        </form>
      </UiToolbarField>
      <UiToolbarField label="Tìm sản phẩm">
        <input aria-label="Tìm sản phẩm" value={search} onChange={e => { setSearch(e.target.value); setCurrentPage(1); }} placeholder="Mã, tên hoặc mã vạch" />
      </UiToolbarField>
      <div className="ui-muted-text ui-auto-actions">
        {filtered.length} sản phẩm • Trang {currentPage}/{totalPages}
      </div>
    </UiToolbar>

    {canManageCategories && <UiCard title="Danh mục sản phẩm">
      <div className="ui-stack">
        <input aria-label="Tìm danh mục" value={categorySearch} onChange={e => setCategorySearch(e.target.value)} placeholder="Tìm mã hoặc tên danh mục" />
        <form onSubmit={addCategory} className="ui-inline-wrap">
          <input value={categoryForm.code} onChange={e => setCategoryForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã danh mục" disabled={!!editingCategory} required />
          <input value={categoryForm.name} onChange={e => setCategoryForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên danh mục" required />
          <button type="submit" disabled={mutating}>{editingCategory ? 'Lưu danh mục' : 'Thêm danh mục'}</button>
          {editingCategory && <button type="button" onClick={() => { setEditingCategory(null); setCategoryForm({ code: '', name: '' }); }}>Hủy sửa</button>}
        </form>
        <div className="ui-stack">
          {categories.filter(category => {
            const q = categorySearch.trim().toLowerCase();
            return !q || category.code.toLowerCase().includes(q) || category.name.toLowerCase().includes(q);
          }).map(category => (
            <div key={category.id} className="ui-divider-row">
              <strong>{category.code}</strong>
              <span>{category.name}</span>
              <UiBadge tone={category.isActive ? 'success' : 'neutral'}>{category.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge>
              <div className="ui-inline-actions ui-auto-actions">
                <button disabled={mutating} onClick={() => editCategory(category)}>Sửa</button>
                <button disabled={mutating} onClick={() => toggleCategory(category)}>{category.isActive ? 'Ngừng' : 'Kích hoạt'}</button>
                <button disabled={mutating} onClick={() => deleteCategory(category.id)}>Xóa</button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </UiCard>}

    {(editing ? canManage : canCreate) && <UiCard title={editing ? `Sửa ${editing.code}` : 'Thêm sản phẩm'}>
      <form onSubmit={saveProduct} className="ui-form-grid">
        <input aria-label="Mã sản phẩm" value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã sản phẩm" disabled={!!editing} required />
        <input aria-label="Tên sản phẩm" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên sản phẩm" required />
        <textarea aria-label="Mô tả sản phẩm" value={form.description} onChange={e => setForm(x => ({ ...x, description: e.target.value }))} placeholder="Mô tả" rows={3} />
        <select aria-label="Đơn vị tính" value={form.unitId} onChange={e => setForm(x => ({ ...x, unitId: Number(e.target.value) }))} required>
          <option value={0}>Chọn đơn vị</option>
          {units.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
        </select>
        <select disabled={!canReadCategories} aria-label="Danh mục" value={form.categoryId} onChange={e => setForm(x => ({ ...x, categoryId: e.target.value ? Number(e.target.value) : '' }))}>
          <option value="">Không có danh mục</option>
          {categories.filter(category => category.isActive || category.id === editing?.categoryId).map(category => <option key={category.id} value={category.id}>{category.code} – {category.name}{category.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
        </select>
        {editing && <label className="ui-checkbox-label"><input type="checkbox" checked={form.isActive} onChange={e => setForm(x => ({ ...x, isActive: e.target.checked }))} /> Hoạt động</label>}
        <div className="ui-inline-actions">
          <button type="submit">Lưu</button>
          {editing && <button type="button" onClick={createMode}>Hủy sửa</button>}
        </div>
      </form>
    </UiCard>}

    {editing && canManageBarcodes && <UiCard title="Mã vạch">
      <div className="ui-inline-wrap">
        {editing.barcodes?.map(item => <UiBadge key={item.id}>{item.value} <button type="button" aria-label={'Xóa mã vạch ' + item.value} onClick={() => deleteBarcode(item.id)} className="ui-badge-remove"><X size={14} aria-hidden="true" /></button></UiBadge>)}
      </div>
      <div className="ui-inline-wrap">
        <input aria-label="Mã vạch mới" value={barcode} onChange={e => setBarcode(e.target.value)} maxLength={64} placeholder="Mã vạch mới" />
        <button type="button" onClick={addBarcode}>Thêm mã vạch</button>
      </div>
    </UiCard>}

    <UiCard title="Danh sách sản phẩm">
      {loading ? <p role="status">Đang tải sản phẩm...</p> : <table>
        <thead><tr><th>Mã</th><th>Tên</th><th>Danh mục</th><th>Đơn vị</th><th>Mã vạch</th><th>Trạng thái</th>{(canManage || canManageBarcodes || canDeactivate) && <th>Thao tác</th>}</tr></thead>
        <tbody>
          {visibleProducts.length === 0
            ? <tr><td colSpan={(canManage || canManageBarcodes || canDeactivate) ? 7 : 6} className="ui-empty-cell">Không có sản phẩm phù hợp với bộ lọc hiện tại.</td></tr>
            : visibleProducts.map(product => <tr key={product.id}>
                <td><strong>{product.code}</strong></td>
                <td>{product.name}</td>
                <td>{product.categoryName || '—'}</td>
                <td>{product.unitName || product.unitId}</td>
                <td>{product.barcodes?.map(item => item.value).join(', ') || '—'}</td>
                <td><UiBadge tone={product.isActive ? 'success' : 'neutral'}>{product.isActive ? 'Hoạt động' : 'Khóa'}</UiBadge></td>
                {(canManage || canManageBarcodes || canDeactivate) && <td>
                  <div className="ui-inline-actions">
                    {(canManage || canManageBarcodes) && <button onClick={() => editMode(product)}>Sửa</button>}
                    {canDeactivate && <button onClick={() => deleteProduct(product.id)}>Xóa</button>}
                  </div>
                </td>}
              </tr>)}
        </tbody>
      </table>}
      <div className="ui-pagination">
        <button disabled={currentPage === 1} onClick={() => setCurrentPage(value => value - 1)}>Trước</button>
        <span>Trang {currentPage}/{totalPages}</span>
        <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(value => value + 1)}>Sau</button>
      </div>
    </UiCard>
  </UiPage>;
};
export default Products;
