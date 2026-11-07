import React from 'react';
import { X } from 'lucide-react';
import { UiBadge, UiCard, UiDrawer, UiToolbarField } from '../../ui/ProductionUi';
import {
  type Category,
  type Product,
  type ProductCatalogTab,
  type ProductFormState,
  type Unit,
} from './ProductCatalogTypes';

export { ProductChannelMappingSection } from './ProductChannelMappingSection';

export interface ProductFormDrawerProps {
  editing: Product | null;
  canManage: boolean;
  canCreate: boolean;
  canManageBarcodes: boolean;
  canReadCategories: boolean;
  showCreateForm: boolean;
  setShowCreateForm: React.Dispatch<React.SetStateAction<boolean>>;
  mutating: boolean;
  createMode: () => void;
  saveProduct: (e: React.FormEvent) => Promise<void>;
  form: ProductFormState;
  setForm: React.Dispatch<React.SetStateAction<ProductFormState>>;
  units: Unit[];
  categories: Category[];
  barcode: string;
  setBarcode: React.Dispatch<React.SetStateAction<string>>;
  addBarcode: () => Promise<void>;
  deleteBarcode: (id: number) => Promise<void>;
}

export function ProductFormDrawer({
  editing, canManage, canCreate, canManageBarcodes, canReadCategories, showCreateForm, setShowCreateForm,
  mutating, createMode, saveProduct, form, setForm, units, categories, barcode, setBarcode, addBarcode, deleteBarcode,
}: ProductFormDrawerProps) {
  const canOpen = editing ? canManage || canManageBarcodes : canCreate;
  if (!canOpen || !showCreateForm) return null;

  return (
    <UiDrawer
      open={canOpen && showCreateForm}
      title={editing ? `Sửa ${editing.code}` : 'Thêm sản phẩm'}
      subtitle="Thiết lập thông tin SKU, quy cách đóng gói và chính sách theo dõi Lô / Sê-ri / Hạn dùng"
      titleId="product-drawer-title"
      busy={mutating}
      onClose={() => {
        if (editing) createMode();
        setShowCreateForm(false);
      }}
    >
      {(editing ? canManage : canCreate) && (
        <form onSubmit={saveProduct} className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }}>
          <label className="ui-stack"><span>Mã sản phẩm *</span><input aria-label="Mã sản phẩm" value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value }))} placeholder="VD: SKU-001" disabled={!!editing} required style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack" style={{ minWidth: 0 }}><span>Tên sản phẩm *</span><input aria-label="Tên sản phẩm" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} placeholder="Nhập tên sản phẩm" required style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack" style={{ gridColumn: '1 / -1' }}><span>Mô tả sản phẩm</span><textarea aria-label="Mô tả sản phẩm" value={form.description} onChange={e => setForm(x => ({ ...x, description: e.target.value }))} placeholder="VD: Thùng linh kiện điện tử tiêu chuẩn xuất nhập kho" rows={2} style={{ minHeight: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Phân lớp bảo quản</span><input aria-label="Storage Class sản phẩm" value={form.storageClass} onChange={e => setForm(x => ({ ...x, storageClass: e.target.value }))} placeholder="VD: Nhiệt độ thường, Mát" maxLength={32} style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Trọng lượng (kg / đơn vị)</span><input aria-label="Trọng lượng đơn vị kg" type="number" min="0.000001" step="0.000001" value={form.unitWeightKg} onChange={e => setForm(x => ({ ...x, unitWeightKg: e.target.value }))} placeholder="VD: 1.25" style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Thể tích (m³ / đơn vị)</span><input aria-label="Thể tích đơn vị m3" type="number" min="0.00000001" step="0.00000001" value={form.unitVolumeM3} onChange={e => setForm(x => ({ ...x, unitVolumeM3: e.target.value }))} placeholder="VD: 0.015" style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Quy đổi Pallet</span><input aria-label="Pallet-equivalent đơn vị" type="number" min="0.00000001" step="0.00000001" value={form.unitPalletEquivalent} onChange={e => setForm(x => ({ ...x, unitPalletEquivalent: e.target.value }))} placeholder="VD: 0.05" style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Phương thức theo dõi</span>
            <select
              aria-label="Tracking Type sản phẩm"
              value={form.trackingType}
              style={{ height: 38, boxSizing: 'border-box' }}
              onChange={e => {
                const trackingType = e.target.value as 'None' | 'Lot' | 'Serial';
                setForm(x => ({ ...x, trackingType, expiryControl: trackingType === 'None' ? false : x.expiryControl, shelfLifeDays: trackingType === 'None' ? '' : x.shelfLifeDays }));
              }}
            >
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
          <div className="ui-inline-actions" style={{ gridColumn: '1 / -1', justifyContent: 'flex-end', marginTop: 8 }}>
            {editing && <button type="button" className="ui-btn ui-btn-secondary" onClick={createMode}>Hủy sửa</button>}
            <button type="submit" className="ui-btn ui-btn-primary ui-primary-button">Lưu</button>
          </div>
        </form>
      )}
      {editing && canManageBarcodes && (
        <div className="ui-subcard" style={{ marginTop: 8 }}>
          <div className="ui-subcard-title">Mã vạch</div>
          <div className="ui-inline-wrap">
            {editing.barcodes?.map(item => (
              <UiBadge key={item.id}>
                {item.value}{' '}
                <button type="button" aria-label={'Xóa mã vạch ' + item.value} onClick={() => void deleteBarcode(item.id)} className="ui-badge-remove">
                  <X size={14} aria-hidden="true" />
                </button>
              </UiBadge>
            ))}
          </div>
          <div className="ui-inline-wrap">
            <input aria-label="Mã vạch mới" value={barcode} onChange={e => setBarcode(e.target.value)} maxLength={64} placeholder="Mã vạch mới" />
            <button type="button" className="ui-btn ui-btn-secondary" onClick={() => void addBarcode()}>Thêm mã vạch</button>
          </div>
        </div>
      )}
    </UiDrawer>
  );
}

export interface ProductCategoriesAndUomSectionProps {
  activeTab: ProductCatalogTab;
  canManageCategories: boolean;
  canManage: boolean;
  canCreate: boolean;
  categorySearch: string;
  setCategorySearch: React.Dispatch<React.SetStateAction<string>>;
  categoryForm: { code: string; name: string };
  setCategoryForm: React.Dispatch<React.SetStateAction<{ code: string; name: string }>>;
  editingCategory: Category | null;
  setEditingCategory: React.Dispatch<React.SetStateAction<Category | null>>;
  addCategory: (e: React.FormEvent) => Promise<void>;
  editCategory: (c: Category) => void;
  toggleCategory: (c: Category) => Promise<void>;
  deleteCategory: (id: number) => Promise<void>;
  categories: Category[];
  mutating: boolean;
  products: Product[];
  units: Unit[];
  activeUomProduct: Product | null;
  setUomProductId: React.Dispatch<React.SetStateAction<number>>;
  uomUnitId: number;
  setUomUnitId: React.Dispatch<React.SetStateAction<number>>;
  uomFactor: string;
  setUomFactor: React.Dispatch<React.SetStateAction<string>>;
  addProductUom: (e: React.FormEvent) => Promise<void>;
  removeProductUom: (productId: number, uomIdOrUnitId: number) => Promise<void>;
}

export function ProductCategoriesAndUomSection({
  activeTab, canManageCategories, canManage, canCreate, categorySearch, setCategorySearch,
  categoryForm, setCategoryForm, editingCategory, setEditingCategory, addCategory, editCategory,
  toggleCategory, deleteCategory, categories, mutating, products, units, activeUomProduct,
  setUomProductId, uomUnitId, setUomUnitId, uomFactor, setUomFactor, addProductUom, removeProductUom,
}: ProductCategoriesAndUomSectionProps) {
  const hiddenStyle = { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none' as const, margin: 0, padding: 0, border: 0 };
  return (
    <div style={activeTab === 'categories-uom' ? { display: 'contents' } : hiddenStyle}>
      {canManageCategories && (
        <UiCard title="Danh mục sản phẩm">
          <div className="ui-stack">
            <UiToolbarField label="Tìm danh mục">
              <input aria-label="Tìm danh mục" value={categorySearch} onChange={e => setCategorySearch(e.target.value)} placeholder="Tìm mã hoặc tên danh mục" style={{ minWidth: 240 }} />
            </UiToolbarField>
            <form onSubmit={addCategory} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
              <UiToolbarField label="Mã danh mục">
                <input aria-label="Mã danh mục" name="categoryCode" value={categoryForm.code} onChange={e => setCategoryForm(x => ({ ...x, code: e.target.value }))} placeholder="Mã danh mục" disabled={!!editingCategory} required />
              </UiToolbarField>
              <div style={{ flex: '2 1 340px', minWidth: 320 }}>
                <UiToolbarField label="Tên danh mục">
                  <input aria-label="Tên danh mục" name="categoryName" value={categoryForm.name} onChange={e => setCategoryForm(x => ({ ...x, name: e.target.value }))} placeholder="Tên danh mục" required style={{ width: '100%', minWidth: 320, boxSizing: 'border-box' }} />
                </UiToolbarField>
              </div>
              <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>{editingCategory ? 'Lưu danh mục' : 'Thêm danh mục'}</button>
              {editingCategory && <button type="button" onClick={() => { setEditingCategory(null); setCategoryForm({ code: '', name: '' }); }} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>Hủy sửa</button>}
            </form>
            <div className="ui-stack">
              {categories
                .filter(category => {
                  const q = categorySearch.trim().toLowerCase();
                  return !q || category.code.toLowerCase().includes(q) || category.name.toLowerCase().includes(q);
                })
                .map(category => (
                  <div key={category.id} className="ui-divider-row">
                    <strong>{category.code}</strong>
                    <span>{category.name}</span>
                    <UiBadge tone={category.isActive ? 'success' : 'neutral'}>{category.isActive ? 'Hoạt động' : 'Ngừng hoạt động'}</UiBadge>
                    <div className="ui-inline-actions ui-auto-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                      <button type="button" aria-label={`Sửa danh mục ${category.code}`} disabled={mutating} onClick={() => editCategory(category)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>
                      <button type="button" aria-label={`${category.isActive ? 'Ngừng' : 'Kích hoạt'} danh mục ${category.code}`} disabled={mutating} onClick={() => void toggleCategory(category)} style={{ whiteSpace: 'nowrap' }}>{category.isActive ? 'Ngừng' : 'Kích hoạt'}</button>
                      <button type="button" aria-label={`Xóa danh mục ${category.code}`} disabled={mutating} onClick={() => void deleteCategory(category.id)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>
                    </div>
                  </div>
                ))}
            </div>
          </div>
        </UiCard>
      )}

      {(canManage || canCreate) && (
        <UiCard title="Cấu hình Quy đổi Đa Đơn vị tính (Đơn vị phụ)">
          <form onSubmit={addProductUom} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
            <UiToolbarField label="Sản phẩm quy đổi đơn vị">
              <select aria-label="Sản phẩm quy đổi ĐVT" value={activeUomProduct?.id || 0} onChange={e => setUomProductId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box' }}>
                {products.length === 0 ? <option value={0}>Chưa có sản phẩm</option> : null}
                {products.map(item => <option key={item.id} value={item.id}>{item.code} – {item.name} (Đơn vị chuẩn: {!item.unitName || item.unitName.trim().toUpperCase() === 'ĐVT' ? 'Cái' : item.unitName})</option>)}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Đơn vị quy đổi phụ">
              <select aria-label="Đơn vị quy đổi phụ" value={uomUnitId || units[0]?.id || 0} onChange={e => setUomUnitId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box' }}>
                {units.length === 0 ? <option value={0}>Chọn đơn vị phụ</option> : null}
                {units.filter(u => u.isActive).map(u => <option key={u.id} value={u.id}>{u.code} – {u.name}</option>)}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Hệ số quy đổi ra đơn vị chuẩn">
              <input aria-label="Hệ số quy đổi ĐVT" type="number" min="1" step="1" value={uomFactor} onChange={e => setUomFactor(e.target.value)} placeholder="VD: 20" style={{ height: 38, boxSizing: 'border-box', width: 130 }} />
            </UiToolbarField>
            <button type="submit" disabled={mutating || !activeUomProduct} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Thêm quy đổi đơn vị</button>
          </form>
          {activeUomProduct && (activeUomProduct.uoms?.length ?? 0) > 0 && (
            <div className="ui-inline-wrap" style={{ marginTop: 10 }}>
              {activeUomProduct.uoms?.map(u => (
                <UiBadge key={u.id ?? u.unitId}>
                  {`1 ${u.unitName || u.unitCode || u.unitId} = ${u.conversionFactor ?? u.conversionFactorToBase ?? 1} ${!activeUomProduct.unitName || activeUomProduct.unitName.trim().toUpperCase() === 'ĐVT' ? 'đơn vị chuẩn' : activeUomProduct.unitName}`}
                  <button type="button" aria-label={`Xóa quy đổi ${u.unitName || u.unitCode || u.unitId}`} onClick={() => void removeProductUom(activeUomProduct.id, u.id ?? u.unitId)} className="ui-badge-remove">
                    <X size={14} aria-hidden="true" />
                  </button>
                </UiBadge>
              ))}
            </div>
          )}
        </UiCard>
      )}
    </div>
  );
}
