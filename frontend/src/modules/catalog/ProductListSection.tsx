import React from 'react';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import {
  type Category,
  formatStorageClass,
  type Product,
  type ProductCatalogTab,
  type ProductFormState,
  sanitizeProductDescription,
  type Unit,
} from './ProductCatalogTypes';
import { ProductFormDrawer } from './ProductDrawerAndCategorySection';

export interface ProductCatalogTabsProps {
  activeTab: ProductCatalogTab;
  setActiveTab: (tab: ProductCatalogTab) => void;
}

export const ProductCatalogTabs: React.FC<ProductCatalogTabsProps> = ({ activeTab, setActiveTab }) => (
  <div
    data-testid="products-catalog-tabs"
    role="tablist"
    aria-label="Phân hệ quản trị sản phẩm"
    style={{ display: 'flex', flexWrap: 'wrap', gap: 6, padding: 4, borderRadius: 10, background: '#f1f5f9', border: '1px solid #e2e8f0' }}
  >
    {[
      { id: 'products' as const, label: 'Danh sách Sản phẩm', testId: 'tab-products-list' },
      { id: 'categories-uom' as const, label: 'Danh mục & Quy đổi ĐVT', testId: 'tab-categories-uom' },
      { id: 'channel-mapping' as const, label: 'Ánh xạ SKU Đa sàn TMĐT', testId: 'tab-channel-mapping' },
      { id: 'bundle-gift' as const, label: 'Combo / Kit & Quà tặng 0đ', testId: 'tab-bundle-gift' },
    ].map(tab => {
      const isActive = activeTab === tab.id;
      return (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={isActive}
          data-testid={tab.testId}
          onClick={() => setActiveTab(tab.id)}
          style={{
            padding: '8px 16px',
            borderRadius: 8,
            fontSize: 13,
            fontWeight: 600,
            background: isActive ? '#ffffff' : 'transparent',
            color: isActive ? '#0f172a' : '#475569',
            border: isActive ? '1px solid #cbd5e1' : '1px solid transparent',
            boxShadow: isActive ? '0 1px 2px rgba(15, 23, 42, 0.06)' : 'none',
            cursor: 'pointer',
          }}
        >
          {tab.label}
        </button>
      );
    })}
  </div>
);

export interface ProductListTabSectionProps {
  activeTab: ProductCatalogTab;
  products: Product[];
  categories: Category[];
  units: Unit[];
  governanceSummary: {
    activeCount: number;
    lotCount: number;
    serialCount: number;
    expiryCount: number;
    dimensionCount: number;
    incomplete: Product[];
  };
  mutating: boolean;
  triggerAutoAuditGovernance: () => Promise<void>;
  verifyCategoryStoragePolicy: () => Promise<void>;
  skuAuditResult: { auditId: string; fefoPolicyStatus: string; packagingHierarchyStatus: string } | null;
  categoryPolicyResult: { verificationId: string; status: string } | null;
  lookup: (e: React.FormEvent) => Promise<void>;
  scan: string;
  setScan: React.Dispatch<React.SetStateAction<string>>;
  search: string;
  setSearch: React.Dispatch<React.SetStateAction<string>>;
  currentPage: number;
  setCurrentPage: React.Dispatch<React.SetStateAction<number>>;
  totalPages: number;
  filteredCount: number;
  visibleProducts: Product[];
  loading: boolean;
  editing: Product | null;
  canManage: boolean;
  canCreate: boolean;
  canManageBarcodes: boolean;
  canReadCategories: boolean;
  canDeactivate: boolean;
  showCreateForm: boolean;
  setShowCreateForm: React.Dispatch<React.SetStateAction<boolean>>;
  createMode: () => void;
  editMode: (p: Product) => void;
  saveProduct: (e: React.FormEvent) => Promise<void>;
  deleteProduct: (id: number) => Promise<void>;
  form: ProductFormState;
  setForm: React.Dispatch<React.SetStateAction<ProductFormState>>;
  barcode: string;
  setBarcode: React.Dispatch<React.SetStateAction<string>>;
  addBarcode: () => Promise<void>;
  deleteBarcode: (id: number) => Promise<void>;
}

export const ProductListTabSection: React.FC<ProductListTabSectionProps> = ({
  activeTab, products, categories, units, governanceSummary, mutating,
  triggerAutoAuditGovernance, verifyCategoryStoragePolicy, skuAuditResult, categoryPolicyResult,
  lookup, scan, setScan, search, setSearch, currentPage, setCurrentPage, totalPages, filteredCount,
  visibleProducts, loading, editing, canManage, canCreate, canManageBarcodes, canReadCategories,
  canDeactivate, showCreateForm, setShowCreateForm, createMode, editMode, saveProduct, deleteProduct,
  form, setForm, barcode, setBarcode, addBarcode, deleteBarcode,
}) => (
  <div
    style={
      activeTab === 'products'
        ? { display: 'contents' }
        : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }
    }
  >
    <UiCard title="Chỉ số Chuẩn hóa Chính sách Hạn dùng, Đa ĐVT, QC & Quy cách Đóng gói Sản phẩm">
      <div className="ui-inline-wrap" style={{ gap: '8px 16px' }}>
        <span style={{ fontSize: 13, color: '#065f46', fontWeight: 600 }}>{`• Sản phẩm hoạt động: ${governanceSummary.activeCount}/${products.length}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Danh mục hoạt động: ${categories.filter(c => c.isActive).length}/${categories.length}`}</span>
        <span style={{ fontSize: 13, color: '#334155' }}>{`• Theo dõi Lô: ${governanceSummary.lotCount} • Sê-ri: ${governanceSummary.serialCount}`}</span>
        <span style={{ fontSize: 13, color: '#92400e', fontWeight: 600 }}>{`• Kiểm soát hạn dùng / FEFO: ${governanceSummary.expiryCount} sản phẩm`}</span>
        <span style={{ fontSize: 13, color: '#065f46', fontWeight: 600 }}>{`• Đã chuẩn hóa Trọng lượng/Thể tích/Pallet: ${governanceSummary.dimensionCount} sản phẩm`}</span>
        <button type="button" disabled={mutating} onClick={() => void triggerAutoAuditGovernance()}>
          Chạy Kiểm định Hồ sơ Kỹ thuật &amp; Chuẩn hóa FEFO/Đóng gói
        </button>
        <button type="button" disabled={mutating} onClick={() => void verifyCategoryStoragePolicy()}>
          Kiểm định Chính sách Bảo quản &amp; Phân nhóm Nhiệt độ/Nguy hại
        </button>
        {skuAuditResult && (
          <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
            {`Đã kiểm định ${skuAuditResult.auditId} • ${({ FEFO_STRICT_ENFORCED: 'Thực thi nghiêm ngặt FEFO', FEFO_VALIDATED: 'Chuẩn hóa FEFO' } as Record<string, string>)[skuAuditResult.fefoPolicyStatus] || skuAuditResult.fefoPolicyStatus} • ${({ UOM_PACKAGING_VALIDATED: 'Đã chuẩn hóa quy cách đóng gói', PACKAGING_VALIDATED: 'Đóng gói hợp lệ' } as Record<string, string>)[skuAuditResult.packagingHierarchyStatus] || skuAuditResult.packagingHierarchyStatus}`}
          </div>
        )}
        {categoryPolicyResult && (
          <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
            {`${categoryPolicyResult.verificationId} • ${({ STORAGE_POLICY_VERIFIED: 'Đã xác minh chính sách bảo quản & phân vùng nhiệt độ/nguy hại', STORAGE_CLASS_VALIDATED: 'Đã chuẩn hóa phân vùng nhiệt độ & bảo quản', HAZMAT_TEMPERATURE_SEGREGATED: 'Đã phân tách hàng nguy hại & dải nhiệt độ' } as Record<string, string>)[categoryPolicyResult.status] || categoryPolicyResult.status}`}
          </div>
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
          <input aria-label="Tra mã vạch" value={scan} onChange={e => setScan(e.target.value)} placeholder="Quét hoặc nhập mã vạch" autoComplete="off" style={{ minWidth: 220 }} />
          <button type="submit" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tra mã vạch</button>
        </form>
      </UiToolbarField>
      <UiToolbarField label="Tìm sản phẩm">
        <input aria-label="Tìm sản phẩm" value={search} onChange={e => { setSearch(e.target.value); setCurrentPage(1); }} placeholder="Mã, tên hoặc mã vạch" style={{ minWidth: 220 }} />
      </UiToolbarField>
      {(editing ? canManage : canCreate) && (
        <button type="button" data-testid="btn-toggle-product-form" onClick={() => setShowCreateForm(prev => !prev)} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
          {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Thêm sản phẩm mới'}
        </button>
      )}
      <div className="ui-muted-text ui-auto-actions">
        {filteredCount} sản phẩm • Trang {currentPage}/{totalPages}
      </div>
    </UiToolbar>

    <ProductFormDrawer
      editing={editing}
      canManage={canManage}
      canCreate={canCreate}
      canManageBarcodes={canManageBarcodes}
      canReadCategories={canReadCategories}
      showCreateForm={showCreateForm}
      setShowCreateForm={setShowCreateForm}
      mutating={mutating}
      createMode={createMode}
      saveProduct={saveProduct}
      form={form}
      setForm={setForm}
      units={units}
      categories={categories}
      barcode={barcode}
      setBarcode={setBarcode}
      addBarcode={addBarcode}
      deleteBarcode={deleteBarcode}
    />

    <UiCard title="Danh sách sản phẩm">
      {loading ? (
        <p role="status">Đang tải sản phẩm...</p>
      ) : (
        <UiTableScroll>
          <table className="ui-table-compact">
            <thead><tr><th>Mã</th><th>Tên</th><th>Danh mục</th><th>Đơn vị</th><th>Thông số lưu trữ</th><th>Theo dõi</th><th>Mã vạch</th><th>Trạng thái</th>{(canManage || canManageBarcodes || canDeactivate) && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
            <tbody>
              {visibleProducts.length === 0 ? (
                <tr><td colSpan={canManage || canManageBarcodes || canDeactivate ? 9 : 8} className="ui-empty-cell">Không có sản phẩm phù hợp với bộ lọc hiện tại.</td></tr>
              ) : (
                visibleProducts.map(product => (
                  <tr key={product.id}>
                    <td style={{ whiteSpace: 'nowrap' }}><strong>{product.code}</strong></td>
                    <td>
                      <div>{product.name}</div>
                      {product.description && <small style={{ color: '#475569' }}>{sanitizeProductDescription(product.description)}</small>}
                    </td>
                    <td>{product.categoryName || '—'}</td>
                    <td><strong>{product.unitName || product.unitId}</strong><br /><small>{product.uoms && product.uoms.length > 0 ? product.uoms.map(u => `${u.unitName || u.unitCode || u.unitId} (×${u.conversionFactor ?? u.conversionFactorToBase ?? 1})`).join(', ') : `ĐVT cơ sở (×1)${product.unitPalletEquivalent ? ` • ${Math.round(1 / product.unitPalletEquivalent)} ĐV/Pallet` : ''}`}</small></td>
                    <td><strong>{formatStorageClass(product.storageClass) || '—'}</strong><br /><small>{product.unitWeightKg != null ? product.unitWeightKg + ' kg' : '—'} • {product.unitVolumeM3 != null ? product.unitVolumeM3 + ' m³' : '—'} • {product.unitPalletEquivalent != null ? product.unitPalletEquivalent + ' quy đổi pallet' : '—'}</small></td>
                    <td><strong>{product.trackingType === 'Lot' ? 'Theo lô' : product.trackingType === 'Serial' ? 'Theo sê-ri' : 'Không theo dõi'}</strong><br /><small>{product.expiryControl ? `Có hạn dùng${product.shelfLifeDays ? ` • ${product.shelfLifeDays} ngày` : ''}` : 'Không kiểm soát hạn dùng'} • {product.qcRequired || product.trackingType === 'Serial' || product.expiryControl ? 'Kiểm định đầu vào' : 'Kiểm định tiêu chuẩn'} • Dung sai: ±{product.receivingTolerancePercent ?? 0}%</small></td>
                    <td>{product.barcodes?.map(item => item.value).join(', ') || '—'}</td>
                    <td><UiBadge tone={product.isActive ? 'success' : 'neutral'}>{product.isActive ? 'Hoạt động' : 'Khóa'}</UiBadge></td>
                    {(canManage || canManageBarcodes || canDeactivate) && (
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <div style={{ display: 'inline-flex', gap: '6px', alignItems: 'center', whiteSpace: 'nowrap' }}>
                          {(canManage || canManageBarcodes) && <button onClick={() => editMode(product)} style={{ whiteSpace: 'nowrap' }}>Sửa</button>}
                          {canDeactivate && <button onClick={() => void deleteProduct(product.id)} style={{ whiteSpace: 'nowrap' }}>Xóa</button>}
                        </div>
                      </td>
                    )}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      )}
      <div className="ui-pagination">
        <button disabled={currentPage === 1} onClick={() => setCurrentPage(value => value - 1)}>Trước</button>
        <span>Trang {currentPage}/{totalPages}</span>
        <button disabled={currentPage === totalPages} onClick={() => setCurrentPage(value => value + 1)}>Sau</button>
      </div>
    </UiCard>
  </div>
);
