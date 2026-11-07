import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { categoryChanged, categoryRequest, normalizeBarcodeInput, sanitizeScannerCode } from './productCatalog';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  type Category,
  formatStorageClass,
  messageOf,
  normalizeStorageClassPayload,
  PAGE_SIZE,
  type Product,
  type ProductCatalogTab,
  type ProductFormState,
  sanitizeProductDescription,
  type Unit,
} from '../modules/catalog/ProductCatalogTypes';
import {
  ProductCategoriesAndUomSection,
  ProductChannelMappingSection,
} from '../modules/catalog/ProductDrawerAndCategorySection';
import { ProductCatalogTabs, ProductListTabSection } from '../modules/catalog/ProductListSection';
import { ProductBundleAndGiftSection } from '../modules/catalog/ProductBundleAndGiftSection';

const Products = () => {
  const canManage = usePermission('product.update');
  const canRead = usePermission('product.read');
  const canReadUnits = usePermission('uom.read');
  const canCreate = usePermission('product.create');
  const canDeactivate = usePermission('product.deactivate');
  const canReadCategories = usePermission('product_category.read');
  const canManageCategories = usePermission('product_category.manage');
  const canManageBarcodes = usePermission('product_barcode.manage');
  const [activeTab, setActiveTab] = useState<ProductCatalogTab>('products');
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
  const [form, setForm] = useState<ProductFormState>({
    code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '',
    unitPalletEquivalent: '', trackingType: 'None', expiryControl: false, shelfLifeDays: '',
    unitId: 0, categoryId: '', isActive: true,
  });
  const [categoryForm, setCategoryForm] = useState({ code: '', name: '' });
  const [categorySearch, setCategorySearch] = useState('');
  const [editingCategory, setEditingCategory] = useState<Category | null>(null);
  const [barcode, setBarcode] = useState('');
  const [uomProductId, setUomProductId] = useState<number>(0);
  const [uomUnitId, setUomUnitId] = useState<number>(0);
  const [uomFactor, setUomFactor] = useState<string>('20');
  const mutationLock = useRef(false);
  const [mutating, setMutating] = useState(false);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [skuAuditResult, setSkuAuditResult] = useState<{ auditId: string; fefoPolicyStatus: string; packagingHierarchyStatus: string } | null>(null);
  const [categoryPolicyResult, setCategoryPolicyResult] = useState<{ verificationId: string; status: string } | null>(null);

  const mutate = async (action: () => Promise<void>) => {
    if (mutationLock.current) return;
    mutationLock.current = true; setMutating(true); setError('');
    try { await action(); }
    finally { mutationLock.current = false; setMutating(false); }
  };

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [p, u, c] = await Promise.all([
        canRead ? apiClient.get('/api/v1/products') : Promise.resolve({ data: [] }),
        canReadUnits ? apiClient.get('/api/v1/units') : Promise.resolve({ data: [] }),
        canReadCategories ? apiClient.get('/api/v1/product-categories') : Promise.resolve({ data: [] }),
      ]);
      const rawProducts = hasPermission('product.read') && Array.isArray(p.data) ? (p.data as Product[]) : [];
      setProducts(rawProducts.map(item => ({ ...item, description: sanitizeProductDescription(item.description) })));
      setUnits(hasPermission('uom.read') ? u.data : []);
      setCategories(hasPermission('product_category.read') ? c.data : []);
    } catch (e) {
      setProducts([]); setCategories([]); setUnits([]); setEditing(null);
      setError(messageOf(e, 'Không thể tải dữ liệu sản phẩm.'));
    } finally { setLoading(false); }
  }, [canRead, canReadUnits, canReadCategories]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => { setEditing(null); setEditingCategory(null); setNotice(''); }, [canRead, canManage, canManageBarcodes, canManageCategories]);

  const filtered = useMemo(() => {
    const cleanSearch = sanitizeScannerCode(search);
    const q = cleanSearch.toLowerCase();
    return q
      ? products.filter(p => p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q) || p.barcodes?.some(b => b.value.includes(cleanSearch)))
      : products;
  }, [products, search]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const visibleProducts = useMemo(() => filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE), [filtered, currentPage]);

  const governanceSummary = useMemo(() => {
    const active = products.filter(p => p.isActive);
    const lotCount = products.filter(p => p.trackingType === 'Lot').length;
    const serialCount = products.filter(p => p.trackingType === 'Serial').length;
    const expiryCount = products.filter(p => Boolean(p.expiryControl)).length;
    const dimensionCount = products.filter(p => p.unitWeightKg != null || p.unitVolumeM3 != null || p.unitPalletEquivalent != null).length;
    const incomplete = active
      .filter(p => (p.unitWeightKg == null && p.unitVolumeM3 == null) || (p.expiryControl && p.shelfLifeDays == null))
      .slice(0, 10);
    return { activeCount: active.length, lotCount, serialCount, expiryCount, dimensionCount, incomplete };
  }, [products]);

  useEffect(() => { if (currentPage > totalPages) setCurrentPage(totalPages); }, [currentPage, totalPages]);

  const createMode = () => {
    setEditing(null);
    setForm({
      code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '',
      unitPalletEquivalent: '', trackingType: 'None', expiryControl: false, shelfLifeDays: '',
      unitId: units[0]?.id || 0, categoryId: '', isActive: true,
    });
  };

  const editMode = (p: Product) => {
    setActiveTab('products'); setShowCreateForm(true); setEditing(p);
    setForm({
      code: p.code, name: p.name, description: sanitizeProductDescription(p.description),
      storageClass: formatStorageClass(p.storageClass), unitWeightKg: p.unitWeightKg?.toString() || '',
      unitVolumeM3: p.unitVolumeM3?.toString() || '', unitPalletEquivalent: p.unitPalletEquivalent?.toString() || '',
      trackingType: p.trackingType || 'None', expiryControl: p.expiryControl || false,
      shelfLifeDays: p.shelfLifeDays?.toString() || '', unitId: p.unitId, categoryId: p.categoryId || '', isActive: p.isActive,
    });
    setBarcode('');
  };

  const nullableNumber = (value: string) => (value.trim() === '' ? null : Number(value));
  const storageProfile = () => ({
    storageClass: normalizeStorageClassPayload(form.storageClass),
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
    e.preventDefault();
    if (editing ? !canManage : !canCreate) return;
    await mutate(async () => {
      try {
        const cleanDesc = sanitizeProductDescription(form.description) || null;
        if (editing) {
          await apiClient.put(`/api/v1/products/${editing.id}`, {
            name: form.name, description: cleanDesc, unitId: form.unitId, isActive: form.isActive,
            updateStorageProfile: true, updateTrackingPolicy: true, ...storageProfile(), ...trackingPolicy(),
          });
          if (categoryChanged(editing.categoryId, form.categoryId)) {
            await apiClient.put(`/api/v1/products/${editing.id}/category`, categoryRequest(form.categoryId));
          }
        } else {
          await apiClient.post('/api/v1/products', {
            code: sanitizeScannerCode(form.code), name: form.name, description: cleanDesc, unitId: form.unitId,
            ...storageProfile(), ...trackingPolicy(), ...categoryRequest(form.categoryId),
          });
        }
        setNotice('Đã lưu sản phẩm.'); createMode(); await load();
      } catch (e) { setError(messageOf(e, 'Không thể lưu sản phẩm.')); }
    });
  };

  const addCategory = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canManageCategories) return;
    await mutate(async () => {
      try {
        if (editingCategory) await apiClient.put(`/api/v1/product-categories/${editingCategory.id}`, { name: categoryForm.name, isActive: editingCategory.isActive });
        else await apiClient.post('/api/v1/product-categories', { code: sanitizeScannerCode(categoryForm.code), name: categoryForm.name });
        setCategoryForm({ code: '', name: '' }); setEditingCategory(null); await load();
      } catch (e) { setError(messageOf(e, editingCategory ? 'Không thể cập nhật danh mục.' : 'Không thể thêm danh mục.')); }
    });
  };

  const editCategory = (c: Category) => { setEditingCategory(c); setCategoryForm({ code: c.code, name: c.name }); };
  const toggleCategory = async (c: Category) => mutate(async () => {
    try { await apiClient.put(`/api/v1/product-categories/${c.id}`, { name: c.name, isActive: !c.isActive }); await load(); }
    catch (e) { setError(messageOf(e, 'Không thể cập nhật danh mục.')); }
  });
  const deleteCategory = async (id: number) => mutate(async () => {
    try { await apiClient.delete(`/api/v1/product-categories/${id}`); await load(); }
    catch (e) { setError(messageOf(e, 'Không thể xóa danh mục.')); }
  });

  const refreshEditing = async () => {
    if (editing) editMode((await apiClient.get(`/api/v1/products/${editing.id}`)).data);
    await load();
  };

  const addBarcode = async () => {
    if (!editing || !canManageBarcodes) return;
    await mutate(async () => {
      try { await apiClient.post(`/api/v1/products/${editing.id}/barcodes`, { value: normalizeBarcodeInput(barcode) }); await refreshEditing(); }
      catch (e) { setError(messageOf(e, 'Không thể thêm mã vạch.')); }
    });
  };

  const deleteBarcode = async (id: number) => {
    if (!editing || !canManageBarcodes) return;
    await mutate(async () => {
      try { await apiClient.delete(`/api/v1/products/${editing.id}/barcodes/${id}`); await refreshEditing(); }
      catch (e) { setError(messageOf(e, 'Không thể xóa mã vạch.')); }
    });
  };

  const activeUomProduct = useMemo(() => products.find(p => p.id === (editing?.id || uomProductId)) || products[0] || null, [products, editing, uomProductId]);

  const addProductUom = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetProduct = activeUomProduct;
    if (!targetProduct) return;
    const selectedUnit = units.find(u => u.id === Number(uomUnitId)) || units[0];
    const factor = Math.max(1, Number(uomFactor) || 1);
    await mutate(async () => {
      try {
        await apiClient.post(`/api/v1/products/${targetProduct.id}/uoms`, { unitId: selectedUnit?.id || Number(uomUnitId) || 1, conversionFactorToBase: factor, conversionFactor: factor });
        setProducts(prev => prev.map(item => item.id === targetProduct.id ? {
          ...item,
          uoms: [
            ...(item.uoms || []).filter(u => u.unitId !== (selectedUnit?.id || Number(uomUnitId) || 1)),
            { id: Date.now(), unitId: selectedUnit?.id || Number(uomUnitId) || 1, unitCode: selectedUnit?.code || 'BOX', unitName: selectedUnit?.name || 'Thùng', conversionFactor: factor, conversionFactorToBase: factor },
          ],
        } : item));
        setNotice(`Đã cập nhật quy đổi đơn vị tính cho sản phẩm ${targetProduct.code}.`);
      } catch (err) { setError(messageOf(err, 'Không thể cập nhật quy đổi đơn vị tính.')); }
    });
  };

  const removeProductUom = async (productId: number, uomIdOrUnitId: number) => mutate(async () => {
    try {
      await apiClient.delete(`/api/v1/products/${productId}/uoms/${uomIdOrUnitId}`);
      setProducts(prev => prev.map(item => item.id === productId ? { ...item, uoms: (item.uoms || []).filter(u => (u.id ?? u.unitId) !== uomIdOrUnitId) } : item));
      setNotice('Đã xóa quy đổi đơn vị tính phụ.');
    } catch (err) { setError(messageOf(err, 'Không thể xóa quy đổi đơn vị tính phụ.')); }
  });

  const deleteProduct = async (id: number) => {
    if (!canDeactivate || !window.confirm('Bạn có chắc muốn xóa sản phẩm này?')) return;
    await mutate(async () => {
      try { await apiClient.delete(`/api/v1/products/${id}`); setNotice('Đã xóa sản phẩm.'); await load(); }
      catch (e) { setError(messageOf(e, 'Không thể xóa sản phẩm.')); }
    });
  };

  const lookup = async (e: React.FormEvent) => {
    e.preventDefault(); setError(''); setNotice('');
    try {
      const p = (await apiClient.get('/api/v1/product-barcodes/lookup', { params: { value: sanitizeScannerCode(scan) } })).data as Product;
      setSearch(p.code); setNotice(`Mã vạch thuộc sản phẩm ${p.code} – ${p.name}.`);
    } catch (e) { setSearch(''); setError(messageOf(e, 'Không tìm thấy mã vạch.')); }
  };

  const triggerAutoAuditGovernance = async () => mutate(async () => {
    try {
      const res = await apiClient.post('/api/v1/products/auto-audit-governance', {});
      if (res?.data?.auditId) setSkuAuditResult(res.data);
      else { setSkuAuditResult(null); setError('Phản hồi kiểm định hồ sơ kỹ thuật SKU không hợp lệ từ máy chủ.'); }
    } catch (failure) { setSkuAuditResult(null); setError(messageOf(failure, 'Không thể thực hiện kiểm định.')); }
  });

  const verifyCategoryStoragePolicy = async () => mutate(async () => {
    try {
      const res = await apiClient.post('/api/v1/product-categories/verify-storage-policy', {});
      if (res?.data?.verificationId && res?.data?.status) setCategoryPolicyResult({ verificationId: String(res.data.verificationId), status: String(res.data.status) });
      else { setCategoryPolicyResult(null); setError('Phản hồi kiểm định chính sách bảo quản danh mục không hợp lệ từ máy chủ.'); }
    } catch (failure) { setCategoryPolicyResult(null); setError(messageOf(failure, 'Không thể thực hiện kiểm định.')); }
  });

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Dữ liệu nền"
        title="Sản phẩm"
        description="Quản lý sản phẩm, danh mục, đa đơn vị tính (quy đổi), đa mã vạch, ánh xạ SKU đa sàn TMĐT, chính sách hạn dùng, kiểm định chất lượng và dung sai nhận hàng."
      />

      {notice && <p role="status" className="ui-success-text">{notice}</p>}
      {error && <p role="alert">{error}</p>}
      {mutating && <p role="status">Đang xử lý...</p>}

      <ProductCatalogTabs activeTab={activeTab} setActiveTab={setActiveTab} />

      <ProductListTabSection
        activeTab={activeTab}
        products={products}
        categories={categories}
        units={units}
        governanceSummary={governanceSummary}
        mutating={mutating}
        triggerAutoAuditGovernance={triggerAutoAuditGovernance}
        verifyCategoryStoragePolicy={verifyCategoryStoragePolicy}
        skuAuditResult={skuAuditResult}
        categoryPolicyResult={categoryPolicyResult}
        lookup={lookup}
        scan={scan}
        setScan={setScan}
        search={search}
        setSearch={setSearch}
        currentPage={currentPage}
        setCurrentPage={setCurrentPage}
        totalPages={totalPages}
        filteredCount={filtered.length}
        visibleProducts={visibleProducts}
        loading={loading}
        editing={editing}
        canManage={canManage}
        canCreate={canCreate}
        canManageBarcodes={canManageBarcodes}
        canReadCategories={canReadCategories}
        canDeactivate={canDeactivate}
        showCreateForm={showCreateForm}
        setShowCreateForm={setShowCreateForm}
        createMode={createMode}
        editMode={editMode}
        saveProduct={saveProduct}
        deleteProduct={deleteProduct}
        form={form}
        setForm={setForm}
        barcode={barcode}
        setBarcode={setBarcode}
        addBarcode={addBarcode}
        deleteBarcode={deleteBarcode}
      />

      <ProductCategoriesAndUomSection
        activeTab={activeTab}
        canManageCategories={canManageCategories}
        canManage={canManage}
        canCreate={canCreate}
        categorySearch={categorySearch}
        setCategorySearch={setCategorySearch}
        categoryForm={categoryForm}
        setCategoryForm={setCategoryForm}
        editingCategory={editingCategory}
        setEditingCategory={setEditingCategory}
        addCategory={addCategory}
        editCategory={editCategory}
        toggleCategory={toggleCategory}
        deleteCategory={deleteCategory}
        categories={categories}
        mutating={mutating}
        products={products}
        units={units}
        activeUomProduct={activeUomProduct}
        setUomProductId={setUomProductId}
        uomUnitId={uomUnitId}
        setUomUnitId={setUomUnitId}
        uomFactor={uomFactor}
        setUomFactor={setUomFactor}
        addProductUom={addProductUom}
        removeProductUom={removeProductUom}
      />

      <ProductChannelMappingSection
        activeTab={activeTab}
        products={products}
        activeUomProduct={activeUomProduct}
        mutating={mutating}
        mutate={mutate}
        setNotice={setNotice}
        setError={setError}
      />

      <ProductBundleAndGiftSection
        activeTab={activeTab}
        products={products}
        activeUomProduct={activeUomProduct}
        mutating={mutating}
        mutate={mutate}
        setNotice={setNotice}
        setError={setError}
      />
    </UiPage>
  );
};
export default Products;
