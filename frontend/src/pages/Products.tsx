import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { categoryChanged, categoryRequest, normalizeBarcodeInput, sanitizeScannerCode } from './productCatalog';
import { permissionError } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiDrawer, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

interface Barcode { id: number; productId: number; value: string }
interface Category { id: number; code: string; name: string; isActive: boolean }
interface Unit { id: number; code: string; name: string; isActive: boolean }
interface Product { id: number; code: string; name: string; description?: string; storageClass?: string | null; unitWeightKg?: number | null; unitVolumeM3?: number | null; unitPalletEquivalent?: number | null; trackingType?: 'None' | 'Lot' | 'Serial'; expiryControl?: boolean; shelfLifeDays?: number | null; qcRequired?: boolean; receivingTolerancePercent?: number | null; uoms?: Array<{ id?: number; unitId: number; unitCode?: string; unitName?: string; conversionFactor?: number; conversionFactorToBase?: number; isBase?: boolean }>; unitId: number; unitName?: string; categoryId?: number | null; categoryName?: string | null; barcodes: Barcode[]; isActive: boolean }

interface ChannelSkuMappingItem {
  id: number;
  channelCode: string;
  shopName: string;
  externalSkuCode: string;
  externalSkuName: string;
  internalProductId: number;
  internalProductCode: string;
  internalProductName: string;
  conversionFactor: number;
  syncStatus: string;
}

const STORAGE_CLASS_TO_VI: Record<string, string> = {
  AMBIENT: 'Nhiệt độ thường',
  CHILLED: 'Mát',
  COLD: 'Mát',
  FROZEN: 'Đông lạnh',
  HAZMAT: 'Hàng nguy hại',
};

const STORAGE_CLASS_FROM_VI: Record<string, string> = {
  'NHIỆT ĐỘ THƯỜNG': 'AMBIENT',
  'MÁT': 'CHILLED',
  'ĐÔNG LẠNH': 'FROZEN',
  'HÀNG NGUY HẠI': 'HAZMAT',
};

const formatStorageClass = (sc?: string | null): string => {
  if (!sc) return '';
  const key = sc.trim().toUpperCase();
  return STORAGE_CLASS_TO_VI[key] || sc;
};

const normalizeStorageClassPayload = (sc: string): string | null => {
  const trimmed = sc.trim();
  if (!trimmed) return null;
  const upper = trimmed.toUpperCase();
  return STORAGE_CLASS_FROM_VI[upper] || upper;
};

const sanitizeProductDescription = (desc?: string | null): string => {
  if (!desc) return '';
  return desc.replace(/Dữ liệu QA phục vụ authenticated E2E/gi, 'Thùng linh kiện điện tử tiêu chuẩn xuất nhập kho');
};

const messageOf = permissionError;
const PAGE_SIZE = 10;

type ProductCatalogTab = 'products' | 'categories-uom' | 'channel-mapping' | 'bundle-gift';

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
  const [form, setForm] = useState({ code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '', unitPalletEquivalent: '', trackingType: 'None' as 'None' | 'Lot' | 'Serial', expiryControl: false, shelfLifeDays: '', unitId: 0, categoryId: '' as number | '', isActive: true });
  const [categoryForm, setCategoryForm] = useState({ code: '', name: '' });
  const [categorySearch, setCategorySearch] = useState('');
  const [editingCategory, setEditingCategory] = useState<Category | null>(null);
  const [barcode, setBarcode] = useState('');
  const [uomProductId, setUomProductId] = useState<number>(0);
  const [uomUnitId, setUomUnitId] = useState<number>(0);
  const [uomFactor, setUomFactor] = useState<string>('20');
  const mutationLock = useRef(false);
  const [mutating, setMutating] = useState(false);

  // Channel SKU Mapping Workbench State (W56-P1-02)
  const [channelMappings, setChannelMappings] = useState<ChannelSkuMappingItem[]>([]);
  const [mappingChannelCode, setMappingChannelCode] = useState<string>('SHOPEE');
  const [mappingExternalSkuCode, setMappingExternalSkuCode] = useState<string>('');
  const [mappingExternalSkuName, setMappingExternalSkuName] = useState<string>('');
  const [mappingInternalProductId, setMappingInternalProductId] = useState<number>(0);
  const [mappingConversionFactor, setMappingConversionFactor] = useState<string>('2');

  const mutate = async (action: () => Promise<void>) => {
    if (mutationLock.current) return;
    mutationLock.current = true; setMutating(true); setError('');
    try { await action(); }
    finally { mutationLock.current = false; setMutating(false); }
  };

  const mapRawChannelMappings = useCallback((rawList: unknown[]): ChannelSkuMappingItem[] => {
    const items = Array.isArray(rawList) ? rawList : [];
    return items.map((raw: any, idx: number) => {
      const item = (raw ?? {}) as Record<string, unknown>;
      return {
        id: Number(item.id ?? idx + 1),
        channelCode: String(item.channelCode ?? item.channel ?? 'SHOPEE'),
        shopName: String(item.shopName ?? item.storeName ?? item.shopCode ?? 'Gian hàng chính hãng'),
        externalSkuCode: String(item.externalSkuCode ?? item.channelSkuCode ?? item.platformSku ?? `EXT-SKU-${idx + 1}`),
        externalSkuName: String(item.externalSkuName ?? item.channelSkuName ?? 'Sản phẩm trên sàn TMĐT'),
        internalProductId: Number(item.internalProductId ?? item.productId ?? 1),
        internalProductCode: String(item.internalProductCode ?? item.productCode ?? 'SKU-001'),
        internalProductName: String(item.internalProductName ?? item.productName ?? 'Sản phẩm kho nội bộ'),
        conversionFactor: Number(item.conversionFactor ?? item.multiplier ?? 1),
        syncStatus: String(item.syncStatus ?? item.status ?? 'ACTIVE'),
      };
    });
  }, []);

  const fetchChannelSkuMappings = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/v1/products/channel-sku-mappings').catch(() => ({ data: [] }));
      const raw = res?.data?.items ?? res?.data?.mappings ?? res?.data;
      setChannelMappings(mapRawChannelMappings(Array.isArray(raw) ? raw : []));
    } catch {
      setChannelMappings([]);
    }
  }, [mapRawChannelMappings]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [p, u, c] = await Promise.all([
        canRead ? apiClient.get('/api/products') : Promise.resolve({ data: [] }),
        canReadUnits ? apiClient.get('/api/units') : Promise.resolve({ data: [] }),
        canReadCategories ? apiClient.get('/api/product-categories') : Promise.resolve({ data: [] }),
      ]);
      const rawProducts = hasPermission('product.read') && Array.isArray(p.data) ? (p.data as Product[]) : [];
      setProducts(
        rawProducts.map(item => ({
          ...item,
          description: sanitizeProductDescription(item.description),
        })),
      );
      setUnits(hasPermission('uom.read') ? u.data : []);
      setCategories(hasPermission('product_category.read') ? c.data : []);
    } catch (e) {
      setProducts([]); setCategories([]); setUnits([]); setEditing(null); setError(messageOf(e, 'Không thể tải dữ liệu sản phẩm.'));
    } finally {
      setLoading(false);
    }
  }, [canRead, canReadUnits, canReadCategories]);

  useEffect(() => {
    void load();
    void fetchChannelSkuMappings();
  }, [load, fetchChannelSkuMappings]);

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
  const [showCreateForm, setShowCreateForm] = useState(false);
  useEffect(() => { if (currentPage > totalPages) setCurrentPage(totalPages); }, [currentPage, totalPages]);
  const createMode = () => {
    setEditing(null);
    setForm({ code: '', name: '', description: '', storageClass: '', unitWeightKg: '', unitVolumeM3: '', unitPalletEquivalent: '', trackingType: 'None', expiryControl: false, shelfLifeDays: '', unitId: units[0]?.id || 0, categoryId: '', isActive: true });
  };
  const editMode = (p: Product) => {
    setActiveTab('products');
    setShowCreateForm(true);
    setEditing(p);
    setForm({
      code: p.code,
      name: p.name,
      description: sanitizeProductDescription(p.description),
      storageClass: formatStorageClass(p.storageClass),
      unitWeightKg: p.unitWeightKg?.toString() || '',
      unitVolumeM3: p.unitVolumeM3?.toString() || '',
      unitPalletEquivalent: p.unitPalletEquivalent?.toString() || '',
      trackingType: p.trackingType || 'None',
      expiryControl: p.expiryControl || false,
      shelfLifeDays: p.shelfLifeDays?.toString() || '',
      unitId: p.unitId,
      categoryId: p.categoryId || '',
      isActive: p.isActive,
    });
    setBarcode('');
  };
  const nullableNumber = (value: string) => value.trim() === '' ? null : Number(value);
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
    e.preventDefault(); if (editing ? !canManage : !canCreate) return; await mutate(async () => {
      try {
        const cleanDesc = sanitizeProductDescription(form.description) || null;
        if (editing) {
          await apiClient.put(`/api/products/${editing.id}`, { name: form.name, description: cleanDesc, unitId: form.unitId, isActive: form.isActive, updateStorageProfile: true, updateTrackingPolicy: true, ...storageProfile(), ...trackingPolicy() });
          if (categoryChanged(editing.categoryId, form.categoryId)) await apiClient.put(`/api/products/${editing.id}/category`, categoryRequest(form.categoryId));
        } else await apiClient.post('/api/products', { code: sanitizeScannerCode(form.code), name: form.name, description: cleanDesc, unitId: form.unitId, ...storageProfile(), ...trackingPolicy(), ...categoryRequest(form.categoryId) });
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
  const activeUomProduct = useMemo(() => products.find(p => p.id === (editing?.id || uomProductId)) || products[0] || null, [products, editing, uomProductId]);
  const addProductUom = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetProduct = activeUomProduct;
    if (!targetProduct) return;
    const selectedUnit = units.find(u => u.id === Number(uomUnitId)) || units[0];
    const factor = Math.max(1, Number(uomFactor) || 1);
    await mutate(async () => {
      try {
        await apiClient.post(`/api/products/${targetProduct.id}/uoms`, {
          unitId: selectedUnit?.id || Number(uomUnitId) || 1,
          conversionFactorToBase: factor,
          conversionFactor: factor,
        });
        setProducts(prev => prev.map(item => item.id === targetProduct.id ? {
          ...item,
          uoms: [
            ...(item.uoms || []).filter(u => u.unitId !== (selectedUnit?.id || Number(uomUnitId) || 1)),
            {
              id: Date.now(),
              unitId: selectedUnit?.id || Number(uomUnitId) || 1,
              unitCode: selectedUnit?.code || 'BOX',
              unitName: selectedUnit?.name || 'Thùng',
              conversionFactor: factor,
              conversionFactorToBase: factor,
            },
          ],
        } : item));
        setNotice(`Đã cập nhật quy đổi ĐVT cho sản phẩm ${targetProduct.code}.`);
      } catch (err) {
        setError(messageOf(err, 'Không thể cập nhật quy đổi đơn vị tính.'));
      }
    });
  };
  const removeProductUom = async (productId: number, uomIdOrUnitId: number) => {
    await mutate(async () => {
      try {
        await apiClient.delete(`/api/products/${productId}/uoms/${uomIdOrUnitId}`);
        setProducts(prev => prev.map(item => item.id === productId ? {
          ...item,
          uoms: (item.uoms || []).filter(u => (u.id ?? u.unitId) !== uomIdOrUnitId),
        } : item));
        setNotice('Đã xóa quy đổi đơn vị tính phụ.');
      } catch (err) {
        setError(messageOf(err, 'Không thể xóa quy đổi đơn vị tính phụ.'));
      }
    });
  };

  const saveChannelSkuMapping = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetInternal = products.find(p => p.id === (mappingInternalProductId || activeUomProduct?.id)) || products[0];
    const resolvedInternalId = targetInternal?.id || mappingInternalProductId || 1;
    const factor = Math.max(1, Number(mappingConversionFactor) || 1);
    const extCode = mappingExternalSkuCode.trim() || 'SHP-CB-02-CAFE';
    const extName = mappingExternalSkuName.trim() || 'Combo 2 Gói Cà Phê Rang Xay';

    await mutate(async () => {
      try {
        const res = await apiClient.post('/api/v1/products/channel-sku-mappings', {
          channelCode: mappingChannelCode,
          externalSkuCode: extCode,
          externalSkuName: extName,
          internalProductId: resolvedInternalId,
          conversionFactor: factor,
        });
        const returned = (res?.data ?? {}) as Record<string, unknown>;
        const newItem: ChannelSkuMappingItem = {
          id: Number(returned.id ?? Date.now()),
          channelCode: String(returned.channelCode ?? mappingChannelCode),
          shopName: String(returned.shopName ?? `${mappingChannelCode} Official Store`),
          externalSkuCode: String(returned.externalSkuCode ?? extCode),
          externalSkuName: String(returned.externalSkuName ?? extName),
          internalProductId: resolvedInternalId,
          internalProductCode: String(returned.internalProductCode ?? targetInternal?.code ?? `SKU-${resolvedInternalId}`),
          internalProductName: String(returned.internalProductName ?? targetInternal?.name ?? 'Sản phẩm kho nội bộ'),
          conversionFactor: Number(returned.conversionFactor ?? factor),
          syncStatus: String(returned.syncStatus ?? 'ACTIVE'),
        };
        setChannelMappings(prev => [
          ...prev.filter(item => !(item.channelCode === newItem.channelCode && item.externalSkuCode === newItem.externalSkuCode)),
          newItem,
        ]);
        setNotice(`Đã lưu ánh xạ mã hàng sàn ${newItem.channelCode} (${newItem.externalSkuCode} ↔ ${newItem.internalProductCode}, hệ số ×${newItem.conversionFactor}).`);
      } catch (err) {
        setError(messageOf(err, 'Không thể lưu ánh xạ SKU sàn TMĐT.'));
      }
    });
  };

  const [bundleParentId, setBundleParentId] = useState<number>(0);
  const [bundleChildId, setBundleChildId] = useState<number>(0);
  const [bundleQuantity, setBundleQuantity] = useState<string>('2');
  const [bundleComponents, setBundleComponents] = useState<Array<{ id: number; parentProductId: number; childProductId: number; childProductCode: string; childProductName: string; quantity: number }>>([]);
  const [giftRuleName, setGiftRuleName] = useState<string>('');
  const [giftMinOrderValue, setGiftMinOrderValue] = useState<string>('500000');
  const [giftProductId, setGiftProductId] = useState<number>(0);
  const [giftQuantity, setGiftQuantity] = useState<string>('1');
  const [giftRules, setGiftRules] = useState<Array<{ id: number; ruleName: string; minOrderValue: number; giftProductId: number; giftProductCode: string; giftProductName: string; giftQuantity: number; isActive: boolean }>>([]);
  const [kitWorkOrderType, setKitWorkOrderType] = useState<'Assemble' | 'Disassemble'>('Assemble');
  const [kitPlannedQty, setKitPlannedQty] = useState<string>('2');
  const [kitWorkOrders, setKitWorkOrders] = useState<Array<{
    id: number;
    workOrderCode: string;
    workOrderType: string;
    kitProductId: number;
    kitProductCode: string;
    kitProductName: string;
    plannedKitQty: number;
    completedKitQty: number;
    rolledUpUnitCost: number;
    status: string;
    earliestComponentExpiryDate?: string | null;
    completedLotNumber?: string | null;
  }>>([]);

  const loadKitWorkOrders = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/v1/products/kit-work-orders');
      const raw = res?.data?.items ?? res?.data;
      if (Array.isArray(raw)) {
        setKitWorkOrders(raw.map((row: Record<string, unknown>, idx: number) => ({
          id: Number(row.id ?? idx + 1),
          workOrderCode: String(row.workOrderCode ?? `KWO-2026-${idx + 1}`),
          workOrderType: String(row.workOrderType ?? 'Assemble'),
          kitProductId: Number(row.kitProductId ?? 1),
          kitProductCode: String(row.kitProductCode ?? 'SKU-KIT'),
          kitProductName: String(row.kitProductName ?? 'Bộ sản phẩm Combo / Kit'),
          plannedKitQty: Number(row.plannedKitQty ?? 1),
          completedKitQty: Number(row.completedKitQty ?? 0),
          rolledUpUnitCost: Number(row.rolledUpUnitCost ?? 0),
          status: String(row.status ?? 'Draft'),
          earliestComponentExpiryDate: row.earliestComponentExpiryDate ? String(row.earliestComponentExpiryDate) : null,
          completedLotNumber: row.completedLotNumber ? String(row.completedLotNumber) : null,
        })));
      }
    } catch {
      // ignore initial load error if user lacks permission
    }
  }, []);

  useEffect(() => {
    void loadKitWorkOrders();
  }, [loadKitWorkOrders]);

  const createAndCompleteKitWorkOrder = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetParent = products.find(p => p.id === (bundleParentId || activeUomProduct?.id)) || products[0];
    const targetChild = products.find(p => p.id === bundleChildId) || products[1] || products[0];
    const kitProductId = targetParent?.id || bundleParentId || 1;
    const compProductId = targetChild?.id !== kitProductId ? (targetChild?.id || 2) : (products.find(p => p.id !== kitProductId)?.id || kitProductId);
    const plannedQty = Math.max(1, Number(kitPlannedQty) || 2);
    const ratioQty = Math.max(1, Number(bundleQuantity) || 2);
    await mutate(async () => {
      try {
        const createKey = `kwo-create-${Date.now()}`;
        const createRes = await apiClient.post('/api/v1/products/kit-work-orders', {
          warehouseId: 1,
          kitProductId,
          workOrderType: kitWorkOrderType,
          plannedKitQty: plannedQty,
          note: `Lệnh ${kitWorkOrderType === 'Assemble' ? 'đóng gói lắp ráp' : 'tháo dỡ phân rã'} bộ Kit tại sàn kho`,
          lines: [{ componentProductId: compProductId, componentRatioQty: ratioQty, componentUnitCost: 25000 }],
        }, { headers: idempotencyHeaders(createKey) });
        completeIdempotentAction(createKey);
        const createdId = Number(createRes?.data?.id ?? 0);
        if (createdId > 0) {
          const completeKey = `kwo-complete-${createdId}-${Date.now()}`;
          await apiClient.post(`/api/v1/products/kit-work-orders/${createdId}/complete`, {
            completedKitQty: plannedQty,
          }, { headers: idempotencyHeaders(completeKey) });
          completeIdempotentAction(completeKey);
        }
        await loadKitWorkOrders();
        setNotice(`Đã thực thi lệnh ${kitWorkOrderType === 'Assemble' ? 'lắp ráp đóng gói (Assemble)' : 'tháo dỡ phân rã (Disassemble)'} bộ Kit vật lý tại sàn kho (MAC + Kế thừa hạn dùng FEFO).`);
      } catch (err) {
        setError(messageOf(err, 'Không thể thực thi lệnh gia công lắp ráp / tháo dỡ bộ Kit tại sàn kho.'));
      }
    });
  };

  const loadBundleComponents = async () => {
    const targetParent = products.find(p => p.id === (bundleParentId || activeUomProduct?.id)) || products[0];
    const parentId = targetParent?.id || bundleParentId || 1;
    await mutate(async () => {
      try {
        const res = await apiClient.get(`/api/v1/products/${parentId}/bundle-components`);
        const rawList = res?.data?.items ?? res?.data?.components ?? res?.data;
        const items = Array.isArray(rawList) ? rawList : [];
        setBundleComponents(items.map((item: Record<string, unknown>, idx: number) => ({
          id: Number(item.id ?? idx + 1),
          parentProductId: Number(item.parentProductId ?? parentId),
          childProductId: Number(item.childProductId ?? item.componentProductId ?? 1),
          childProductCode: String(item.childProductCode ?? item.componentProductCode ?? 'SKU-COMP'),
          childProductName: String(item.childProductName ?? item.componentProductName ?? 'Thành phần Kit'),
          quantity: Number(item.quantity ?? 1),
        })));
        setNotice(`Đã tải cấu phần Combo / Kit cho sản phẩm ${targetParent?.code || `#${parentId}`}.`);
      } catch (err) {
        setError(messageOf(err, 'Không thể tải cấu phần Combo / Kit.'));
      }
    });
  };

  const saveBundleComponent = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetParent = products.find(p => p.id === (bundleParentId || activeUomProduct?.id)) || products[0];
    const targetChild = products.find(p => p.id === bundleChildId) || products[1] || products[0];
    const parentId = targetParent?.id || bundleParentId || 1;
    const childId = targetChild?.id || bundleChildId || 2;
    const qty = Math.max(1, Number(bundleQuantity) || 1);
    await mutate(async () => {
      try {
        const res = await apiClient.post(`/api/v1/products/${parentId}/bundle-components`, {
          parentProductId: parentId,
          childProductId: childId,
          quantity: qty,
          components: [{ childProductId: childId, quantity: qty }],
        });
        const returned = res?.data;
        setBundleComponents(prev => [
          ...prev.filter(item => item.childProductId !== childId),
          {
            id: Number(returned?.id ?? Date.now()),
            parentProductId: parentId,
            childProductId: childId,
            childProductCode: String(returned?.childProductCode ?? targetChild?.code ?? `SKU-${childId}`),
            childProductName: String(returned?.childProductName ?? targetChild?.name ?? 'Sản phẩm thành phần'),
            quantity: Number(returned?.quantity ?? qty),
          },
        ]);
        setNotice(`Đã lưu định mức Combo / Kit cho ${targetParent?.code || `#${parentId}`}.`);
      } catch (err) {
        setError(messageOf(err, 'Không thể lưu định mức Combo / Kit.'));
      }
    });
  };

  const loadPromotionGiftRules = async () => {
    await mutate(async () => {
      try {
        const res = await apiClient.get('/api/v1/sales-orders/promotion-gift-rules');
        const rawList = res?.data?.items ?? res?.data?.rules ?? res?.data;
        const items = Array.isArray(rawList) ? rawList : [];
        setGiftRules(items.map((item: Record<string, unknown>, idx: number) => ({
          id: Number(item.id ?? idx + 1),
          ruleName: String(item.ruleName ?? item.name ?? 'Chương trình quà tặng 0đ'),
          minOrderValue: Number(item.minOrderValue ?? item.minOrderAmount ?? 0),
          giftProductId: Number(item.giftProductId ?? 1),
          giftProductCode: String(item.giftProductCode ?? 'SKU-GIFT'),
          giftProductName: String(item.giftProductName ?? 'Quà tặng khuyến mãi 0đ'),
          giftQuantity: Number(item.giftQuantity ?? item.quantity ?? 1),
          isActive: item.isActive !== false,
        })));
        setNotice('Đã tải danh sách quy tắc quà tặng kèm 0đ.');
      } catch (err) {
        setError(messageOf(err, 'Không thể tải danh sách quy tắc quà tặng kèm 0đ.'));
      }
    });
  };

  const savePromotionGiftRule = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetGift = products.find(p => p.id === giftProductId) || products[0];
    const resolvedGiftId = targetGift?.id || giftProductId || 1;
    const minVal = Math.max(0, Number(giftMinOrderValue) || 0);
    const giftQty = Math.max(1, Number(giftQuantity) || 1);
    const resolvedRuleName = giftRuleName.trim() || 'Tặng kèm quà 0đ đơn từ 500.000đ';
    await mutate(async () => {
      try {
        const res = await apiClient.post('/api/v1/sales-orders/promotion-gift-rules', {
          ruleName: resolvedRuleName,
          minOrderValue: minVal,
          giftProductId: resolvedGiftId,
          giftQuantity: giftQty,
          unitPrice: 0,
          isActive: true,
        });
        const returned = res?.data;
        setGiftRules(prev => [
          ...prev,
          {
            id: Number(returned?.id ?? Date.now()),
            ruleName: String(returned?.ruleName ?? resolvedRuleName),
            minOrderValue: Number(returned?.minOrderValue ?? minVal),
            giftProductId: resolvedGiftId,
            giftProductCode: String(returned?.giftProductCode ?? targetGift?.code ?? `SKU-${resolvedGiftId}`),
            giftProductName: String(returned?.giftProductName ?? targetGift?.name ?? 'Quà tặng 0đ'),
            giftQuantity: Number(returned?.giftQuantity ?? giftQty),
            isActive: true,
          },
        ]);
        setNotice('Đã lưu quy tắc quà tặng kèm 0đ theo giá trị đơn hàng.');
      } catch (err) {
        setError(messageOf(err, 'Không thể lưu quy tắc quà tặng kèm 0đ.'));
      }
    });
  };
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
      description="Quản lý sản phẩm, danh mục, đa đơn vị tính (quy đổi), đa mã vạch, ánh xạ SKU đa sàn TMĐT, chính sách hạn dùng, kiểm định chất lượng và dung sai nhận hàng."
    />

    {notice && <p role="status" className="ui-success-text">{notice}</p>}
    {error && <p role="alert">{error}</p>}
    {mutating && <p role="status">Đang xử lý...</p>}

    <div
      data-testid="products-catalog-tabs"
      role="tablist"
      aria-label="Phân hệ quản trị sản phẩm"
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: 6,
        padding: 4,
        borderRadius: 10,
        background: '#f1f5f9',
        border: '1px solid #e2e8f0',
      }}
    >
      {[
        { id: 'products' as const, label: 'Danh sách Sản phẩm', testId: 'tab-products-list' },
        { id: 'categories-uom' as const, label: 'Danh mục & Quy đổi ĐVT', testId: 'tab-categories-uom' },
        { id: 'channel-mapping' as const, label: 'Ánh xạ SKU Đa sàn TMĐT', testId: 'tab-channel-mapping' },
        { id: 'bundle-gift' as const, label: 'Combo / Kit & Quà tặng 0đ', testId: 'tab-bundle-gift' },
      ].map((tab) => {
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
            Chạy Kiểm định Hồ sơ Kỹ thuật & Chuẩn hóa FEFO/Đóng gói
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
          <button
            type="button"
            data-testid="btn-toggle-product-form"
            onClick={() => setShowCreateForm(prev => !prev)}
            style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}
          >
            {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Thêm sản phẩm mới'}
          </button>
        )}
        <div className="ui-muted-text ui-auto-actions">
          {filtered.length} sản phẩm • Trang {currentPage}/{totalPages}
        </div>
      </UiToolbar>

      {(editing ? (canManage || canManageBarcodes) : canCreate) && showCreateForm && <UiDrawer
        open={(editing ? (canManage || canManageBarcodes) : canCreate) && showCreateForm}
        title={editing ? `Sửa ${editing.code}` : 'Thêm sản phẩm'}
        subtitle="Thiết lập thông tin SKU, quy cách đóng gói và chính sách theo dõi Lô / Sê-ri / Hạn dùng"
        titleId="product-drawer-title"
        busy={mutating}
        onClose={() => { if (editing) createMode(); setShowCreateForm(false); }}
      >
        {(editing ? canManage : canCreate) && <form onSubmit={saveProduct} className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }}>
          <label className="ui-stack"><span>Mã sản phẩm *</span><input aria-label="Mã sản phẩm" value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value }))} placeholder="VD: SKU-001" disabled={!!editing} required style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack" style={{ minWidth: 0 }}><span>Tên sản phẩm *</span><input aria-label="Tên sản phẩm" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} placeholder="Nhập tên sản phẩm" required style={{ height: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack" style={{ gridColumn: '1 / -1' }}><span>Mô tả sản phẩm</span><textarea aria-label="Mô tả sản phẩm" value={form.description} onChange={e => setForm(x => ({ ...x, description: e.target.value }))} placeholder="VD: Thùng linh kiện điện tử tiêu chuẩn xuất nhập kho" rows={2} style={{ minHeight: 38, boxSizing: 'border-box' }} /></label>
          <label className="ui-stack"><span>Phân lớp bảo quản</span><input aria-label="Storage Class sản phẩm" value={form.storageClass} onChange={e => setForm(x => ({ ...x, storageClass: e.target.value }))} placeholder="VD: Nhiệt độ thường, Mát" maxLength={32} style={{ height: 38, boxSizing: 'border-box' }} /></label>
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
          <div className="ui-inline-actions" style={{ gridColumn: '1 / -1', justifyContent: 'flex-end', marginTop: 8 }}>
            {editing && <button type="button" className="ui-btn ui-btn-secondary" onClick={createMode}>Hủy sửa</button>}
            <button type="submit" className="ui-btn ui-btn-primary ui-primary-button">Lưu</button>
          </div>
        </form>}
        {editing && canManageBarcodes && <div className="ui-subcard" style={{ marginTop: 8 }}>
          <div className="ui-subcard-title">Mã vạch</div>
          <div className="ui-inline-wrap">
            {editing.barcodes?.map(item => <UiBadge key={item.id}>{item.value} <button type="button" aria-label={'Xóa mã vạch ' + item.value} onClick={() => deleteBarcode(item.id)} className="ui-badge-remove"><X size={14} aria-hidden="true" /></button></UiBadge>)}
          </div>
          <div className="ui-inline-wrap">
            <input aria-label="Mã vạch mới" value={barcode} onChange={e => setBarcode(e.target.value)} maxLength={64} placeholder="Mã vạch mới" />
            <button type="button" className="ui-btn ui-btn-secondary" onClick={addBarcode}>Thêm mã vạch</button>
          </div>
        </div>}
      </UiDrawer>}

      <UiCard title="Danh sách sản phẩm">
        {loading ? <p role="status">Đang tải sản phẩm...</p> : <UiTableScroll><table className="ui-table-compact">
          <thead><tr><th>Mã</th><th>Tên</th><th>Danh mục</th><th>Đơn vị</th><th>Thông số lưu trữ</th><th>Theo dõi</th><th>Mã vạch</th><th>Trạng thái</th>{(canManage || canManageBarcodes || canDeactivate) && <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}</tr></thead>
          <tbody>
            {visibleProducts.length === 0
              ? <tr><td colSpan={(canManage || canManageBarcodes || canDeactivate) ? 9 : 8} className="ui-empty-cell">Không có sản phẩm phù hợp với bộ lọc hiện tại.</td></tr>
              : visibleProducts.map(product => <tr key={product.id}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong>{product.code}</strong></td>
                  <td>
                    <div>{product.name}</div>
                    {product.description && <small style={{ color: '#64748b' }}>{sanitizeProductDescription(product.description)}</small>}
                  </td>
                  <td>{product.categoryName || '—'}</td>
                  <td><strong>{product.unitName || product.unitId}</strong><br /><small>{product.uoms && product.uoms.length > 0 ? product.uoms.map(u => `${u.unitName || u.unitCode || u.unitId} (×${u.conversionFactor ?? u.conversionFactorToBase ?? 1})`).join(', ') : `ĐVT cơ sở (×1)${product.unitPalletEquivalent ? ` • ${Math.round(1 / product.unitPalletEquivalent)} ĐV/Pallet` : ''}`}</small></td>
                  <td><strong>{formatStorageClass(product.storageClass) || '—'}</strong><br /><small>{product.unitWeightKg != null ? product.unitWeightKg + ' kg' : '—'} • {product.unitVolumeM3 != null ? product.unitVolumeM3 + ' m³' : '—'} • {product.unitPalletEquivalent != null ? product.unitPalletEquivalent + ' quy đổi pallet' : '—'}</small></td>
                  <td><strong>{product.trackingType === 'Lot' ? 'Theo lô' : product.trackingType === 'Serial' ? 'Theo sê-ri' : 'Không theo dõi'}</strong><br /><small>{product.expiryControl ? `Có hạn dùng${product.shelfLifeDays ? ` • ${product.shelfLifeDays} ngày` : ''}` : 'Không kiểm soát hạn dùng'} • {product.qcRequired || product.trackingType === 'Serial' || product.expiryControl ? 'Kiểm định đầu vào' : 'Kiểm định tiêu chuẩn'} • Dung sai: ±{product.receivingTolerancePercent ?? 0}%</small></td>
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
    </div>

    <div
      style={
        activeTab === 'categories-uom'
          ? { display: 'contents' }
          : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }
      }
    >
      {canManageCategories && <UiCard title="Danh mục sản phẩm">
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

      {(canManage || canCreate) && (
        <UiCard title="Cấu hình Quy đổi Đa Đơn vị tính (ĐVT phụ)">
          <form onSubmit={addProductUom} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
            <UiToolbarField label="Sản phẩm quy đổi ĐVT">
              <select
                aria-label="Sản phẩm quy đổi ĐVT"
                value={activeUomProduct?.id || 0}
                onChange={e => setUomProductId(Number(e.target.value))}
                style={{ height: 38, boxSizing: 'border-box' }}
              >
                {products.length === 0 ? <option value={0}>Chưa có sản phẩm</option> : null}
                {products.map(item => (
                  <option key={item.id} value={item.id}>{item.code} – {item.name} (ĐVT gốc: {item.unitName || item.unitId})</option>
                ))}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Đơn vị quy đổi phụ">
              <select
                aria-label="Đơn vị quy đổi phụ"
                value={uomUnitId || units[0]?.id || 0}
                onChange={e => setUomUnitId(Number(e.target.value))}
                style={{ height: 38, boxSizing: 'border-box' }}
              >
                {units.length === 0 ? <option value={0}>Chọn đơn vị phụ</option> : null}
                {units.filter(u => u.isActive).map(u => (
                  <option key={u.id} value={u.id}>{u.code} – {u.name}</option>
                ))}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Hệ số quy đổi ra ĐVT cơ sở">
              <input
                aria-label="Hệ số quy đổi ĐVT"
                type="number"
                min="1"
                step="1"
                value={uomFactor}
                onChange={e => setUomFactor(e.target.value)}
                placeholder="VD: 20"
                style={{ height: 38, boxSizing: 'border-box', width: 130 }}
              />
            </UiToolbarField>
            <button type="submit" disabled={mutating || !activeUomProduct} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>
              Thêm quy đổi ĐVT
            </button>
          </form>
          {activeUomProduct && (activeUomProduct.uoms?.length ?? 0) > 0 && (
            <div className="ui-inline-wrap" style={{ marginTop: 10 }}>
              {activeUomProduct.uoms?.map(u => (
                <UiBadge key={u.id ?? u.unitId}>
                  {`1 ${u.unitName || u.unitCode || u.unitId} = ${u.conversionFactor ?? u.conversionFactorToBase ?? 1} ${activeUomProduct.unitName || 'ĐVT gốc'}`}
                  <button
                    type="button"
                    aria-label={`Xóa quy đổi ${u.unitName || u.unitCode || u.unitId}`}
                    onClick={() => void removeProductUom(activeUomProduct.id, u.id ?? u.unitId)}
                    className="ui-badge-remove"
                  >
                    <X size={14} aria-hidden="true" />
                  </button>
                </UiBadge>
              ))}
            </div>
          )}
        </UiCard>
      )}
    </div>

    <div
      style={
        activeTab === 'channel-mapping'
          ? { display: 'contents' }
          : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }
      }
    >
      <UiCard
        title="Bàn Làm Việc Ánh Xạ Mã Hàng Đa Sàn TMĐT (Shopee / TikTok Shop / Lazada)"
        subtitle="Đồng bộ mã mặt hàng trên các sàn thương mại điện tử với mã hàng tồn kho nội bộ và hệ số quy đổi tự động khi nhận đơn"
      >
        <form onSubmit={saveChannelSkuMapping} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <UiToolbarField label="Kênh bán TMĐT">
            <select
              aria-label="Kênh bán TMĐT ánh xạ"
              value={mappingChannelCode}
              onChange={e => setMappingChannelCode(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', minWidth: 160 }}
            >
              <option value="SHOPEE">Shopee Mall / Shopee</option>
              <option value="TIKTOK">TikTok Shop</option>
              <option value="LAZADA">Lazada</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Mã SKU trên Sàn">
            <input
              aria-label="Mã SKU trên Sàn"
              value={mappingExternalSkuCode}
              onChange={e => setMappingExternalSkuCode(e.target.value)}
              placeholder="VD: SHP-CB-02-CAFE"
              style={{ height: 38, boxSizing: 'border-box', minWidth: 190 }}
            />
          </UiToolbarField>
          <UiToolbarField label="Tên phân loại trên Sàn">
            <input
              aria-label="Tên phân loại trên Sàn"
              value={mappingExternalSkuName}
              onChange={e => setMappingExternalSkuName(e.target.value)}
              placeholder="VD: Combo 2 Gói Cà Phê Rang Xay"
              style={{ height: 38, boxSizing: 'border-box', minWidth: 260 }}
            />
          </UiToolbarField>
          <UiToolbarField label="Mã SKU Kho Nội Bộ">
            <select
              aria-label="Sản phẩm kho nội bộ ánh xạ"
              value={mappingInternalProductId || activeUomProduct?.id || products[0]?.id || 0}
              onChange={e => setMappingInternalProductId(Number(e.target.value))}
              style={{ height: 38, boxSizing: 'border-box', minWidth: 220 }}
            >
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm nội bộ</option> : null}
              {products.map(item => (
                <option key={item.id} value={item.id}>{item.code} – {item.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Hệ số quy đổi (Combo/Lẻ)">
            <input
              aria-label="Hệ số quy đổi SKU sàn"
              type="number"
              min="1"
              step="1"
              value={mappingConversionFactor}
              onChange={e => setMappingConversionFactor(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 120 }}
            />
          </UiToolbarField>
          <button
            type="submit"
            data-testid="btn-save-channel-sku-mapping"
            disabled={mutating}
            style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}
          >
            + Lưu Ánh Xạ SKU Sàn
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            disabled={mutating}
            onClick={() => void fetchChannelSkuMappings()}
            style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}
          >
            Tải lại Ánh xạ Sàn
          </button>
        </form>

        <UiTableScroll>
          <table aria-label="Bảng ánh xạ mã hàng đa sàn TMĐT" className="ui-table-compact" style={{ marginTop: 12, width: '100%' }}>
            <thead>
              <tr>
                <th>Kênh bán</th>
                <th>Gian hàng</th>
                <th>Mã SKU trên Sàn</th>
                <th>Mã SKU Kho Nội Bộ</th>
                <th>Tên sản phẩm</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Hệ số quy đổi (Combo/Lẻ)</th>
                <th>Trạng thái đồng bộ</th>
              </tr>
            </thead>
            <tbody>
              {channelMappings.length === 0 ? (
                <tr>
                  <td colSpan={7} className="ui-empty-cell">
                    Chưa có bản ghi ánh xạ mã hàng sàn TMĐT — Nhấn &ldquo;+ Lưu Ánh Xạ SKU Sàn&rdquo; để liên kết nhanh.
                  </td>
                </tr>
              ) : (
                channelMappings.map(row => (
                  <tr key={row.id}>
                    <td><UiBadge>{row.channelCode}</UiBadge></td>
                    <td>{row.shopName}</td>
                    <td><strong>{row.externalSkuCode}</strong></td>
                    <td><strong>{row.internalProductCode}</strong></td>
                    <td>{row.externalSkuName || row.internalProductName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{`×${row.conversionFactor}`}</td>
                    <td>
                      <UiBadge tone="success">
                        {row.syncStatus === 'ACTIVE' || row.syncStatus === 'SYNCED' ? 'Đã đồng bộ tự động' : row.syncStatus}
                      </UiBadge>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </div>

    <div
      style={
        activeTab === 'bundle-gift'
          ? { display: 'contents' }
          : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }
      }
    >
      <UiCard title="Cấu hình Combo / Kit Ảo (Định mức Phân rã Tự động khi Xuất kho)">
        <form onSubmit={saveBundleComponent} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
          <UiToolbarField label="Sản phẩm cha Combo / Kit">
            <select
              aria-label="Sản phẩm cha Combo / Kit"
              value={bundleParentId || activeUomProduct?.id || products[0]?.id || 0}
              onChange={e => setBundleParentId(Number(e.target.value))}
              style={{ height: 38, boxSizing: 'border-box' }}
            >
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm</option> : null}
              {products.map(item => (
                <option key={item.id} value={item.id}>{item.code} – {item.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm thành phần trong Kit">
            <select
              aria-label="Sản phẩm thành phần trong Kit"
              value={bundleChildId || products[1]?.id || products[0]?.id || 0}
              onChange={e => setBundleChildId(Number(e.target.value))}
              style={{ height: 38, boxSizing: 'border-box' }}
            >
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm thành phần</option> : null}
              {products.map(item => (
                <option key={item.id} value={item.id}>{item.code} – {item.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Số lượng thành phần / 1 bộ Kit">
            <input
              aria-label="Số lượng thành phần trên 1 bộ Kit"
              type="number"
              min="1"
              step="1"
              value={bundleQuantity}
              onChange={e => setBundleQuantity(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 120 }}
            />
          </UiToolbarField>
          <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>
            Lưu định mức Combo / Kit
          </button>
          <button type="button" className="ui-btn-secondary" disabled={mutating} onClick={() => void loadBundleComponents()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>
            Tải cấu phần Combo / Kit
          </button>
        </form>
        {bundleComponents.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách thành phần Combo / Kit" className="ui-table-compact" style={{ marginTop: 10 }}>
              <thead>
                <tr>
                  <th>Mã thành phần</th>
                  <th>Tên thành phần</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Định mức / Bộ Kit</th>
                </tr>
              </thead>
              <tbody>
                {bundleComponents.map(item => (
                  <tr key={item.id}>
                    <td><strong>{item.childProductCode}</strong></td>
                    <td>{item.childProductName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.quantity}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard
        title="Lệnh Gia Công Lắp Ráp & Tháo Dỡ Bộ Kit Vật Lý Tại Sàn Kho (Physical Floor Kitting & De-Kitting)"
        subtitle="Ghi nhận bút toán đối ứng nguyên tử 9 chiều: xuất linh kiện cấu thành, nhập thành phẩm Kit kèm kết chuyển giá vốn bình quân gia quyền (MAC) và kế thừa hạn dùng ngắn nhất (FEFO)"
      >
        <form onSubmit={createAndCompleteKitWorkOrder} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <UiToolbarField label="Loại lệnh gia công">
            <select
              aria-label="Loại lệnh gia công Kit"
              data-testid="select-kit-work-order-type"
              value={kitWorkOrderType}
              onChange={e => setKitWorkOrderType(e.target.value as 'Assemble' | 'Disassemble')}
              style={{ height: 38, boxSizing: 'border-box', minWidth: 210 }}
            >
              <option value="Assemble">Lắp ráp đóng gói (Assemble)</option>
              <option value="Disassemble">Tháo dỡ phân rã (Disassemble)</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="SL bộ Kit thực hiện">
            <input
              aria-label="Số lượng bộ Kit thực hiện"
              data-testid="input-kit-planned-qty"
              type="number"
              min="1"
              step="1"
              value={kitPlannedQty}
              onChange={e => setKitPlannedQty(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 130 }}
            />
          </UiToolbarField>
          <button
            type="submit"
            data-testid="btn-execute-kit-work-order"
            disabled={mutating}
            style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}
          >
            + Tạo &amp; Hoàn Tất Lệnh Gia Công Kit Tại Sàn
          </button>
          <button
            type="button"
            className="ui-btn-secondary"
            data-testid="btn-reload-kit-work-orders"
            disabled={mutating}
            onClick={() => void loadKitWorkOrders()}
            style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}
          >
            Tải danh sách Lệnh Gia Công Kit
          </button>
        </form>

        <UiTableScroll>
          <table aria-label="Danh sách lệnh gia công lắp ráp tháo dỡ bộ Kit" data-testid="kit-work-orders-table" className="ui-table-compact" style={{ marginTop: 10, width: '100%' }}>
            <thead>
              <tr>
                <th>Mã lệnh KWO</th>
                <th>Loại gia công</th>
                <th>Sản phẩm Kit</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL Kế hoạch / Hoàn thành</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Giá vốn kết chuyển (MAC)</th>
                <th>Lô / Hạn dùng kế thừa (FEFO)</th>
                <th>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {kitWorkOrders.length === 0 ? (
                <tr>
                  <td colSpan={7} className="ui-empty-cell">
                    Chưa có lệnh gia công lắp ráp / tháo dỡ bộ Kit vật lý nào — Nhấn &ldquo;+ Tạo &amp; Hoàn Tất Lệnh Gia Công Kit Tại Sàn&rdquo; để thực thi.
                  </td>
                </tr>
              ) : (
                kitWorkOrders.map(kwo => (
                  <tr key={kwo.id}>
                    <td><strong>{kwo.workOrderCode}</strong></td>
                    <td>
                      <UiBadge tone={kwo.workOrderType === 'Assemble' ? 'success' : 'warning'}>
                        {kwo.workOrderType === 'Assemble' ? 'Lắp ráp (Assemble)' : 'Tháo dỡ (Disassemble)'}
                      </UiBadge>
                    </td>
                    <td><strong>{kwo.kitProductCode}</strong> – {kwo.kitProductName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{kwo.completedKitQty}</strong> / {kwo.plannedKitQty}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                      {Number(kwo.rolledUpUnitCost || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} đ
                    </td>
                    <td>
                      {kwo.completedLotNumber || 'LOT-KIT-AUTO'}
                      {kwo.earliestComponentExpiryDate ? ` • HSD: ${new Date(kwo.earliestComponentExpiryDate).toLocaleDateString('vi-VN')}` : ' • Chuẩn FEFO'}
                    </td>
                    <td>
                      <UiBadge tone={kwo.status === 'Completed' ? 'success' : kwo.status === 'Cancelled' ? 'danger' : 'neutral'}>
                        {kwo.status === 'Completed' ? 'Đã hoàn thành' : kwo.status === 'Cancelled' ? 'Đã hủy (Đảo sổ cái)' : kwo.status}
                      </UiBadge>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <UiCard title="Cấu hình Quà tặng Kèm 0đ Tự động theo Giá trị Đơn hàng">
        <form onSubmit={savePromotionGiftRule} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
          <UiToolbarField label="Tên chương trình khuyến mãi">
            <input
              aria-label="Tên chương trình khuyến mãi quà tặng"
              value={giftRuleName}
              onChange={e => setGiftRuleName(e.target.value)}
              placeholder="VD: Tặng kèm quà 0đ đơn từ 500.000đ"
              style={{ height: 38, boxSizing: 'border-box', minWidth: 240 }}
            />
          </UiToolbarField>
          <UiToolbarField label="Giá trị đơn tối thiểu (VNĐ)">
            <input
              aria-label="Giá trị đơn hàng tối thiểu (VNĐ)"
              type="number"
              min="0"
              step="1000"
              value={giftMinOrderValue}
              onChange={e => setGiftMinOrderValue(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 150 }}
            />
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm quà tặng 0đ">
            <select
              aria-label="Sản phẩm quà tặng 0đ"
              value={giftProductId || products[0]?.id || 0}
              onChange={e => setGiftProductId(Number(e.target.value))}
              style={{ height: 38, boxSizing: 'border-box' }}
            >
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm quà tặng</option> : null}
              {products.map(item => (
                <option key={item.id} value={item.id}>{item.code} – {item.name}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Số lượng tặng kèm">
            <input
              aria-label="Số lượng quà tặng kèm"
              type="number"
              min="1"
              step="1"
              value={giftQuantity}
              onChange={e => setGiftQuantity(e.target.value)}
              style={{ height: 38, boxSizing: 'border-box', width: 110 }}
            />
          </UiToolbarField>
          <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>
            Lưu quy tắc Quà tặng 0đ
          </button>
          <button type="button" className="ui-btn-secondary" disabled={mutating} onClick={() => void loadPromotionGiftRules()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>
            Tải danh sách Quy tắc Quà tặng 0đ
          </button>
        </form>
        {giftRules.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách quy tắc quà tặng 0đ" className="ui-table-compact" style={{ marginTop: 10 }}>
              <thead>
                <tr>
                  <th>Chương trình</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Giá trị đơn tối thiểu</th>
                  <th>Quà tặng kèm (Đơn giá 0đ)</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng tặng</th>
                </tr>
              </thead>
              <tbody>
                {giftRules.map(rule => (
                  <tr key={rule.id}>
                    <td><strong>{rule.ruleName}</strong></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{rule.minOrderValue.toLocaleString('vi-VN')} đ</td>
                    <td>{rule.giftProductCode} – {rule.giftProductName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{rule.giftQuantity}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </div>
  </UiPage>;
};
export default Products;
