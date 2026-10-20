import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { categoryChanged, categoryRequest, normalizeBarcodeInput, sanitizeScannerCode } from './productCatalog';
import { permissionError } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

interface Barcode { id: number; productId: number; value: string }
interface Category { id: number; code: string; name: string; isActive: boolean }
interface Unit { id: number; code: string; name: string; isActive: boolean }
interface Product { id: number; code: string; name: string; description?: string; storageClass?: string | null; unitWeightKg?: number | null; unitVolumeM3?: number | null; unitPalletEquivalent?: number | null; trackingType?: 'None' | 'Lot' | 'Serial'; expiryControl?: boolean; shelfLifeDays?: number | null; qcRequired?: boolean; receivingTolerancePercent?: number | null; uoms?: Array<{ id?: number; unitId: number; unitCode?: string; unitName?: string; conversionFactor?: number; conversionFactorToBase?: number; isBase?: boolean }>; unitId: number; unitName?: string; categoryId?: number | null; categoryName?: string | null; barcodes: Barcode[]; isActive: boolean }
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
  const [form, setForm] = useState({ code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '', unitPalletEquivalent: '', trackingType: 'None' as 'None' | 'Lot' | 'Serial', expiryControl: false, shelfLifeDays: '', unitId: 0, categoryId: '' as number | '', isActive: true });
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
    const cleanSearch = sanitizeScannerCode(search);
    const q = cleanSearch.toLowerCase();
    return q ? products.filter(p => p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q) || p.barcodes?.some(b => b.value.includes(cleanSearch))) : products;
  }, [products, search]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const visibleProducts = useMemo(() => filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE), [filtered, currentPage]);
  const governanceSummary = useMemo(() => {
    const active = products.filter(p => p.isActive);
    const lotCount = products.filter(p => p.trackingType === 'Lot').length;
    const serialCount = products.filter(p => p.trackingType === 'Serial').length;
    const expiryCount = products.filter(p => Boolean(p.expiryControl)).length;
    const dimensionCount = products.filter(p => p.unitWeightKg != null || p.unitVolumeM3 != null || p.unitPalletEquivalent != null).length;
    const incomplete = active.filter(p => (p.unitWeightKg == null && p.unitVolumeM3 == null) || (p.expiryControl && p.shelfLifeDays == null)).slice(0, 10);
    return { activeCount: active.length, lotCount, serialCount, expiryCount, dimensionCount, incomplete };
  }, [products]);
  const [showCreateForm, setShowCreateForm] = useState(true);
  useEffect(() => { if (currentPage > totalPages) setCurrentPage(totalPages); }, [currentPage, totalPages]);
  const createMode = () => { setEditing(null); setForm({ code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '', unitPalletEquivalent: '', trackingType: 'None', expiryControl: false, shelfLifeDays: '', unitId: units[0]?.id || 0, categoryId: '', isActive: true }); };
  const editMode = (p: Product) => { setShowCreateForm(true); setEditing(p); setForm({ code: p.code, name: p.name, description: p.description || '', storageClass: p.storageClass || '', unitWeightKg: p.unitWeightKg?.toString() || '', unitVolumeM3: p.unitVolumeM3?.toString() || '', unitPalletEquivalent: p.unitPalletEquivalent?.toString() || '', trackingType: p.trackingType || 'None', expiryControl: p.expiryControl || false, shelfLifeDays: p.shelfLifeDays?.toString() || '', unitId: p.unitId, categoryId: p.categoryId || '', isActive: p.isActive }); setBarcode(''); };
  const nullableNumber = (value: string) => value.trim() === '' ? null : Number(value);
  const storageProfile = () => ({
    storageClass: form.storageClass.trim() || null,
    unitWeightKg: nullableNumber(form.unitWeightKg),
    unitVolumeM3: nullableNumber(form.unitVolumeM3),
    unitPalletEquivalent: nullableNumber(form.unitPalletEquivalent),
  });
  const trackingPolicy = () => ({
    trackingType: form.trackingType,
    expiryControl: form.expiryControl,
    shelfLifeDays: form.expiryControl ? nullableNumber(form.shelfLifeDays) : null,
  });

  const saveProduct = async (e: React.FormEvent) => {
    e.preventDefault(); if (editing ? !canManage : !canCreate) return; await mutate(async () => {
      try {
      if (editing) {
        await apiClient.put(`/api/products/${editing.id}`, { name: form.name, description: form.description || null, unitId: form.unitId, isActive: form.isActive, updateStorageProfile: true, updateTrackingPolicy: true, ...storageProfile(), ...trackingPolicy() });
        if (categoryChanged(editing.categoryId, form.categoryId)) await apiClient.put(`/api/products/${editing.id}/category`, categoryRequest(form.categoryId));
      } else await apiClient.post('/api/products', { code: sanitizeScannerCode(form.code), name: form.name, description: form.description || null, unitId: form.unitId, ...storageProfile(), ...trackingPolicy(), ...categoryRequest(form.categoryId) });
      setNotice('Đã lưu sản phẩm.'); createMode(); await load();
      } catch (e) { setError(messageOf(e, 'Không thể lưu sản phẩm.')); }
    });
  };
  const addCategory = async (e: React.FormEvent) => {
    e.preventDefault(); if (!canManageCategories) return; await mutate(async () => {
      try {
        if (editingCategory) await apiClient.put(`/api/product-categories/${editingCategory.id}`, { name: categoryForm.name, isActive: editingCategory.isActive });
        else await apiClient.post('/api/product-categories', { code: sanitizeScannerCode(categoryForm.code), name: categoryForm.name });
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
  const lookup = async (e: React.FormEvent) => { e.preventDefault(); setError(''); setNotice(''); try { const p = (await apiClient.get('/api/product-barcodes/lookup', { params: { value: sanitizeScannerCode(scan) } })).data as Product; setSearch(p.code); setNotice(`Mã vạch thuộc sản phẩm ${p.code} – ${p.name}.`); } catch (e) { setSearch(''); setError(messageOf(e, 'Không tìm thấy mã vạch.')); } };
  const [skuAuditResult, setSkuAuditResult] = useState<{ auditId: string; fefoPolicyStatus: string; packagingHierarchyStatus: string } | null>(null);
  const [categoryPolicyResult, setCategoryPolicyResult] = useState<{ verificationId: string; status: string } | null>(null);
  const triggerAutoAuditGovernance = async () => {
    await mutate(async () => {
      try {
        const res = await apiClient.post('/api/Products/auto-audit-governance', {});
        if (res?.data?.auditId) {
          setSkuAuditResult(res.data);
        } else {
          setSkuAuditResult(null);
          setError('Phản hồi kiểm định hồ sơ kỹ thuật SKU không hợp lệ từ máy chủ.');
        }
      } catch (failure) {
        setSkuAuditResult(null);
        setError(messageOf(failure, 'Không thể thực hiện kiểm định.'));
      }
    });
  };
  const verifyCategoryStoragePolicy = async () => {
    await mutate(async () => {
      try {
        const res = await apiClient.post('/api/ProductCategories/verify-storage-policy', {});
        if (res?.data?.verificationId && res?.data?.status) {
          setCategoryPolicyResult({
            verificationId: String(res.data.verificationId),
            status: String(res.data.status),
          });
        } else {
          setCategoryPolicyResult(null);
          setError('Phản hồi kiểm định chính sách bảo quản danh mục không hợp lệ từ máy chủ.');
        }
      } catch (failure) {
        setCategoryPolicyResult(null);
        setError(messageOf(failure, 'Không thể thực hiện kiểm định.'));
      }
    });
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="Dữ liệu nền"
      title="Sản phẩm"
      description="Quản lý sản phẩm, danh mục, đa đơn vị tính (quy đổi), đa mã vạch, chính sách hạn dùng, kiểm định chất lượng và dung sai nhận hàng."
    />

    {notice && <p role="status" className="ui-success-text">{notice}</p>}
    {error && <p role="alert">{error}</p>}
    {mutating && <p role="status">Đang xử lý...</p>}

    <UiCard title="Chỉ số Chuẩn hóa Chính sách Hạn dùng, Đa ĐVT, QC & Quy cách Đóng gói Sản phẩm">
      <div className="ui-inline-wrap">
        <UiBadge tone="success">{`Sản phẩm hoạt động: ${governanceSummary.activeCount}/${products.length}`}</UiBadge>
        <UiBadge>{`Danh mục hoạt động: ${categories.filter(c => c.isActive).length}/${categories.length}`}</UiBadge>
        <UiBadge>{`Theo dõi Lô: ${governanceSummary.lotCount} • Sê-ri: ${governanceSummary.serialCount}`}</UiBadge>
        <UiBadge tone="warning">{`Kiểm soát hạn dùng / FEFO: ${governanceSummary.expiryCount} sản phẩm`}</UiBadge>
        <UiBadge tone="success">{`Đã chuẩn hóa Trọng lượng/Thể tích/Pallet: ${governanceSummary.dimensionCount} sản phẩm`}</UiBadge>
        <button type="button" disabled={mutating} onClick={() => void triggerAutoAuditGovernance()}>
          Chạy Kiểm định Hồ sơ Kỹ thuật & Chuẩn hóa FEFO/Đóng gói
        </button>
        <button type="button" disabled={mutating} onClick={() => void verifyCategoryStoragePolicy()}>
          Kiểm định Chính sách Bảo quản &amp; Phân nhóm Nhiệt độ/Nguy hại
        </button>
        {skuAuditResult && (
          <UiBadge tone="success">
            {`Đã kiểm định ${skuAuditResult.auditId} • ${({ FEFO_STRICT_ENFORCED: 'Thực thi nghiêm ngặt FEFO', FEFO_VALIDATED: 'Chuẩn hóa FEFO' } as Record<string, string>)[skuAuditResult.fefoPolicyStatus] || skuAuditResult.fefoPolicyStatus} • ${({ UOM_PACKAGING_VALIDATED: 'Đã chuẩn hóa quy cách đóng gói', PACKAGING_VALIDATED: 'Đóng gói hợp lệ' } as Record<string, string>)[skuAuditResult.packagingHierarchyStatus] || skuAuditResult.packagingHierarchyStatus}`}
          </UiBadge>
        )}
        {categoryPolicyResult && (
          <UiBadge tone="success">
            {`${categoryPolicyResult.verificationId} • ${({ STORAGE_CLASS_VALIDATED: 'Đã chuẩn hóa phân vùng nhiệt độ & bảo quản', HAZMAT_TEMPERATURE_SEGREGATED: 'Đã phân tách hàng nguy hại & dải nhiệt độ' } as Record<string, string>)[categoryPolicyResult.status] || categoryPolicyResult.status}`}
          </UiBadge>
        )}
      </div>
      {governanceSummary.incomplete.length > 0 && (
        <p className="ui-muted-text">
          {`Sản phẩm cần bổ sung thông số đóng gói hoặc vòng đời hạn dùng: ${governanceSummary.incomplete.map(item => `${item.code} (${item.name})`).join(', ')}`}
        </p>
      )}
    </UiCard>

    <UiToolbar>
      <UiToolbarField label="Tra mã vạch">
        <form onSubmit={lookup} className="ui-inline" style={{ alignItems: 'end' }}>
          <input aria-label="Tra mã vạch" value={scan} onChange={e => setScan(e.target.value)} placeholder="Quét hoặc nhập mã vạch" autoComplete="off" />
          <button type="submit" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tra mã vạch</button>
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
        <UiToolbarField label="Tìm danh mục">
          <input aria-label="Tìm danh mục" value={categorySearch} onChange={e => setCategorySearch(e.target.value)} placeholder="Tìm mã hoặc tên danh mục" />
        </UiToolbarField>
        <form onSubmit={addCategory} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
          <UiToolbarField label="Mã danh mục">
            <input aria-label="Mã danh mục" name="categoryCode" value={categoryForm.code} onChange={e => setCategoryForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã danh mục" disabled={!!editingCategory} required />
          </UiToolbarField>
          <UiToolbarField label="Tên danh mục">
            <input aria-label="Tên danh mục" name="categoryName" value={categoryForm.name} onChange={e => setCategoryForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên danh mục" required />
          </UiToolbarField>
          <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>{editingCategory ? 'Lưu danh mục' : 'Thêm danh mục'}</button>
          {editingCategory && <button type="button" onClick={() => { setEditingCategory(null); setCategoryForm({ code: '', name: '' }); }} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>Hủy sửa</button>}
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
              <div className="ui-inline-actions ui-auto-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                <button type="button" aria-label={`Sửa danh mục ${category.code}`} disabled={mutating} onClick={() => editCategory(category)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                <button type="button" aria-label={`${category.isActive ? 'Ngừng' : 'Kích hoạt'} danh mục ${category.code}`} disabled={mutating} onClick={() => toggleCategory(category)} style={{ whiteSpace: 'nowrap' }}>{category.isActive ? 'Ngừng' : 'Kích hoạt'}</button>
                <button type="button" aria-label={`Xóa danh mục ${category.code}`} disabled={mutating} onClick={() => deleteCategory(category.id)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </UiCard>}

    {(editing ? canManage : canCreate) && <UiCard title={editing ? `Sửa ${editing.code}` : 'Thêm sản phẩm'}>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: showCreateForm ? 12 : 0 }}>
        <button type="button" onClick={() => setShowCreateForm(prev => !prev)}>
          {showCreateForm ? 'Thu gọn biểu mẫu' : 'Mở biểu mẫu tạo mới'}
        </button>
      </div>
      {showCreateForm && <form onSubmit={saveProduct} className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }}>
        <label className="ui-stack"><span>Mã sản phẩm *</span><input aria-label="Mã sản phẩm" value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value }))} placeholder="VD: SKU-001" disabled={!!editing} required style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Tên sản phẩm *</span><input aria-label="Tên sản phẩm" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} placeholder="Nhập tên sản phẩm" required style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Mô tả sản phẩm</span><textarea aria-label="Mô tả sản phẩm" value={form.description} onChange={e => setForm(x => ({ ...x, description: e.target.value }))} placeholder="Mô tả quy cách, đặc tính" rows={1} style={{ height: 38, minHeight: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Phân lớp bảo quản</span><input aria-label="Storage Class sản phẩm" value={form.storageClass} onChange={e => setForm(x => ({ ...x, storageClass: e.target.value.toUpperCase() }))} placeholder="VD: AMBIENT, CHILLED" maxLength={32} style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Trọng lượng (kg / ĐVT)</span><input aria-label="Trọng lượng đơn vị kg" type="number" min="0.000001" step="0.000001" value={form.unitWeightKg} onChange={e => setForm(x => ({ ...x, unitWeightKg: e.target.value }))} placeholder="VD: 1.25" style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Thể tích (m³ / ĐVT)</span><input aria-label="Thể tích đơn vị m3" type="number" min="0.00000001" step="0.00000001" value={form.unitVolumeM3} onChange={e => setForm(x => ({ ...x, unitVolumeM3: e.target.value }))} placeholder="VD: 0.015" style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Quy đổi Pallet</span><input aria-label="Pallet-equivalent đơn vị" type="number" min="0.00000001" step="0.00000001" value={form.unitPalletEquivalent} onChange={e => setForm(x => ({ ...x, unitPalletEquivalent: e.target.value }))} placeholder="VD: 0.05" style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Phương thức theo dõi</span>

          <select aria-label="Tracking Type sản phẩm" value={form.trackingType} style={{ height: 38, boxSizing: 'border-box' }} onChange={e => {
            const trackingType = e.target.value as 'None' | 'Lot' | 'Serial';
            setForm(x => ({ ...x, trackingType, expiryControl: trackingType === 'None' ? false : x.expiryControl, shelfLifeDays: trackingType === 'None' ? '' : x.shelfLifeDays }));
          }}>
            <option value="None">Không theo Lô/Sê-ri</option>
            <option value="Lot">Theo Lô</option>
            <option value="Serial">Theo Sê-ri</option>
          </select>
        </label>
        <label className="ui-checkbox-label" style={{ height: 38, boxSizing: 'border-box', alignSelf: 'end' }}>
          <input aria-label="Kiểm soát hạn dùng" type="checkbox" checked={form.expiryControl} disabled={form.trackingType === 'None'} onChange={e => setForm(x => ({ ...x, expiryControl: e.target.checked, shelfLifeDays: e.target.checked ? x.shelfLifeDays : '' }))} />
          Kiểm soát hạn dùng
        </label>
        <label className="ui-stack"><span>Hạn dùng (ngày)</span><input aria-label="Shelf Life Days" type="number" min="1" step="1" disabled={!form.expiryControl} value={form.shelfLifeDays} onChange={e => setForm(x => ({ ...x, shelfLifeDays: e.target.value }))} placeholder="VD: 365" style={{ height: 38, boxSizing: 'border-box' }} /></label>
        <label className="ui-stack"><span>Đơn vị cơ sở *</span><select aria-label="Đơn vị tính" value={form.unitId} onChange={e => setForm(x => ({ ...x, unitId: Number(e.target.value) }))} required style={{ height: 38, boxSizing: 'border-box' }}>
          <option value={0}>Chọn đơn vị</option>
          {units.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
        </select></label>
        <label className="ui-stack"><span>Danh mục sản phẩm</span><select disabled={!canReadCategories} aria-label="Danh mục" value={form.categoryId} onChange={e => setForm(x => ({ ...x, categoryId: e.target.value ? Number(e.target.value) : '' }))} style={{ height: 38, boxSizing: 'border-box' }}>
          <option value="">Không có danh mục</option>
          {categories.filter(category => category.isActive || category.id === editing?.categoryId).map(category => <option key={category.id} value={category.id}>{category.code} – {category.name}{category.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
        </select></label>
        {editing && <label className="ui-checkbox-label"><input type="checkbox" checked={form.isActive} onChange={e => setForm(x => ({ ...x, isActive: e.target.checked }))} /> Hoạt động</label>}
        <div className="ui-inline-actions">
          <button type="submit">Lưu</button>
          {editing && <button type="button" onClick={createMode}>Hủy sửa</button>}
        </div>
      </form>}
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
      {loading ? <p role="status">Đang tải sản phẩm...</p> : <UiTableScroll><table className="ui-table-compact">
        <thead><tr><th>Mã</th><th>Tên</th><th>Danh mục</th><th>Đơn vị</th><th>Thông số lưu trữ</th><th>Theo dõi</th><th>Mã vạch</th><th>Trạng thái</th>{(canManage || canManageBarcodes || canDeactivate) && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
        <tbody>
          {visibleProducts.length === 0
            ? <tr><td colSpan={(canManage || canManageBarcodes || canDeactivate) ? 9 : 8} className="ui-empty-cell">Không có sản phẩm phù hợp với bộ lọc hiện tại.</td></tr>
            : visibleProducts.map(product => <tr key={product.id}>
                <td style={{ whiteSpace: 'nowrap' }}><strong>{product.code}</strong></td>
                <td>{product.name}</td>
                <td>{product.categoryName || '—'}</td>
                <td><strong>{product.unitName || product.unitId}</strong><br /><small>{product.uoms && product.uoms.length > 0 ? product.uoms.map(u => `${u.unitName || u.unitCode || u.unitId} (×${u.conversionFactor ?? u.conversionFactorToBase ?? 1})`).join(', ') : `ĐVT cơ sở (×1)${product.unitPalletEquivalent ? ` • ${Math.round(1 / product.unitPalletEquivalent)} ĐV/Pallet` : ''}`}</small></td>
                <td><strong>{product.storageClass || '—'}</strong><br /><small>{product.unitWeightKg != null ? product.unitWeightKg + ' kg' : '—'} • {product.unitVolumeM3 != null ? product.unitVolumeM3 + ' m³' : '—'} • {product.unitPalletEquivalent != null ? product.unitPalletEquivalent + ' quy đổi pallet' : '—'}</small></td>
                <td><strong>{product.trackingType === 'Lot' ? 'Theo lô' : product.trackingType === 'Serial' ? 'Theo sê-ri' : 'Không theo dõi'}</strong><br /><small>{product.expiryControl ? `Có hạn dùng${product.shelfLifeDays ? ` • ${product.shelfLifeDays} ngày` : ''}` : 'Không kiểm soát hạn dùng'} • {product.qcRequired || product.trackingType === 'Serial' || product.expiryControl ? 'QC đầu vào' : 'QC tiêu chuẩn'} • Dung sai: ±{product.receivingTolerancePercent ?? 0}%</small></td>
                <td>{product.barcodes?.map(item => item.value).join(', ') || '—'}</td>
                <td><UiBadge tone={product.isActive ? 'success' : 'neutral'}>{product.isActive ? 'Hoạt động' : 'Khóa'}</UiBadge></td>
                {(canManage || canManageBarcodes || canDeactivate) && <td style={{ whiteSpace: 'nowrap' }}>
                  <div style={{ display: 'inline-flex', gap: '6px', alignItems: 'center', whiteSpace: 'nowrap' }}>
                    {(canManage || canManageBarcodes) && <button onClick={() => editMode(product)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>}
                    {canDeactivate && <button onClick={() => deleteProduct(product.id)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>}
                  </div>
                </td>}
              </tr>)}
        </tbody>
      </table></UiTableScroll>}
      <div className="ui-pagination">
        <button disabled={currentPage === 1} onClick={() => setCurrentPage(value => value - 1)}>Trước</button>
        <span>Trang {currentPage}/{totalPages}</span>
        <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(value => value + 1)}>Sau</button>
      </div>
    </UiCard>
  </UiPage>;
};
export default Products;
