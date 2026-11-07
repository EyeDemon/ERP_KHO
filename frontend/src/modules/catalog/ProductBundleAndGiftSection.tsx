import React, { useCallback, useEffect, useState } from 'react';
import { Lock } from 'lucide-react';
import apiClient from '../../services/apiClient';
import { usePermission } from '../../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import { UiBadge, UiCard, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';
import { messageOf, type Product, type ProductCatalogTab } from './ProductCatalogTypes';
import { ProductPromotionGiftRulesCard } from './ProductPromotionGiftRulesCard';

export function ProductBundleAndGiftSection({
  activeTab,
  products,
  activeUomProduct,
  mutating,
  mutate,
  setNotice,
  setError,
}: {
  activeTab: ProductCatalogTab;
  products: Product[];
  activeUomProduct: Product | null;
  mutating: boolean;
  mutate: (action: () => Promise<void>) => Promise<void>;
  setNotice: React.Dispatch<React.SetStateAction<string>>;
  setError: React.Dispatch<React.SetStateAction<string>>;
}) {
  const [bundleParentId, setBundleParentId] = useState<number>(0);
  const [bundleChildId, setBundleChildId] = useState<number>(0);
  const [bundleQuantity, setBundleQuantity] = useState<string>('2');
  const [bundleComponents, setBundleComponents] = useState<Array<{ id: number; parentProductId: number; childProductId: number; childProductCode: string; childProductName: string; quantity: number }>>([]);
  const [kitWorkOrderType, setKitWorkOrderType] = useState<'Assemble' | 'Disassemble'>('Assemble');
  const [kitPlannedQty, setKitPlannedQty] = useState<string>('2');
  const canReadCostRbac = usePermission('inventory.cost.read');
  const [kitWorkOrders, setKitWorkOrders] = useState<Array<{
    id: number;
    workOrderCode: string;
    workOrderType: string;
    kitProductId: number;
    kitProductCode: string;
    kitProductName: string;
    baseUomCode: string;
    conversionRateSnapshot: number;
    plannedKitQty: number;
    completedKitQty: number;
    rolledUpUnitCost: number | null;
    canReadCost?: boolean;
    status: string;
    earliestComponentExpiryDate?: string | null;
    completedLotNumber?: string | null;
  }>>([]);

  const loadKitWorkOrders = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/v1/products/kit-work-orders');
      const raw = res?.data?.items ?? res?.data;
      if (Array.isArray(raw)) {
        setKitWorkOrders(
          raw.map((row: Record<string, unknown>, idx: number) => ({
            id: Number(row.id ?? idx + 1),
            workOrderCode: String(row.workOrderCode ?? `KWO-2026-${idx + 1}`),
            workOrderType: String(row.workOrderType ?? 'Assemble'),
            kitProductId: Number(row.kitProductId ?? 1),
            kitProductCode: String(row.kitProductCode ?? 'SKU-KIT'),
            kitProductName: String(row.kitProductName ?? 'Bộ sản phẩm Combo / Kit'),
            baseUomCode: String(row.baseUomCode ?? 'PCS'),
            conversionRateSnapshot: Number(row.conversionRateSnapshot ?? 1),
            plannedKitQty: Number(row.plannedKitQty ?? 1),
            completedKitQty: Number(row.completedKitQty ?? 0),
            rolledUpUnitCost: row.rolledUpUnitCost === null || row.rolledUpUnitCost === undefined ? null : Number(row.rolledUpUnitCost),
            canReadCost: row.canReadCost === undefined ? undefined : Boolean(row.canReadCost),
            status: String(row.status ?? 'Draft'),
            earliestComponentExpiryDate: row.earliestComponentExpiryDate ? String(row.earliestComponentExpiryDate) : null,
            completedLotNumber: row.completedLotNumber ? String(row.completedLotNumber) : null,
          })),
        );
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
    const compProductId = targetChild?.id !== kitProductId ? targetChild?.id || 2 : products.find(p => p.id !== kitProductId)?.id || kitProductId;
    const plannedQty = Math.max(1, Number(kitPlannedQty) || 2);
    const ratioQty = Math.max(1, Number(bundleQuantity) || 2);
    await mutate(async () => {
      try {
        const createKey = `kwo-create-${Date.now()}`;
        const createRes = await apiClient.post(
          '/api/v1/products/kit-work-orders',
          {
            warehouseId: 1,
            kitProductId,
            workOrderType: kitWorkOrderType,
            plannedKitQty: plannedQty,
            note: `Lệnh ${kitWorkOrderType === 'Assemble' ? 'đóng gói lắp ráp' : 'tháo dỡ phân rã'} bộ Kit tại sàn kho`,
            lines: [{ componentProductId: compProductId, componentRatioQty: ratioQty, componentUnitCost: 25000 }],
          },
          { headers: idempotencyHeaders(createKey) },
        );
        completeIdempotentAction(createKey);
        const createdId = Number(createRes?.data?.id ?? 0);
        if (createdId > 0) {
          const completeKey = `kwo-complete-${createdId}-${Date.now()}`;
          await apiClient.post(
            `/api/v1/products/kit-work-orders/${createdId}/complete`,
            { completedKitQty: plannedQty },
            { headers: idempotencyHeaders(completeKey) },
          );
          completeIdempotentAction(completeKey);
        }
        await loadKitWorkOrders();
        setNotice(`Đã thực thi lệnh ${kitWorkOrderType === 'Assemble' ? 'lắp ráp đóng gói' : 'tháo dỡ phân rã'} bộ Kit vật lý tại sàn kho (Bình quân gia quyền + Kế thừa hạn dùng ngắn nhất).`);
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
        const res = await apiClient.post(
          `/api/v1/products/${parentId}/bundle-components`,
          { parentProductId: parentId, childProductId: childId, quantity: qty, components: [{ childProductId: childId, quantity: qty }] },
        );
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

  return (
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
            <select aria-label="Sản phẩm cha Combo / Kit" value={bundleParentId || activeUomProduct?.id || products[0]?.id || 0} onChange={e => setBundleParentId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box' }}>
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm</option> : null}
              {products.map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm thành phần trong Kit">
            <select aria-label="Sản phẩm thành phần trong Kit" value={bundleChildId || products[1]?.id || products[0]?.id || 0} onChange={e => setBundleChildId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box' }}>
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm thành phần</option> : null}
              {products.map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Số lượng thành phần / 1 bộ Kit">
            <input aria-label="Số lượng thành phần trên 1 bộ Kit" type="number" min="1" step="1" value={bundleQuantity} onChange={e => setBundleQuantity(e.target.value)} style={{ height: 38, boxSizing: 'border-box', width: 120 }} />
          </UiToolbarField>
          <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Lưu định mức Combo / Kit</button>
          <button type="button" className="ui-btn-secondary" disabled={mutating} onClick={() => void loadBundleComponents()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tải cấu phần Combo / Kit</button>
        </form>
        {bundleComponents.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách thành phần Combo / Kit" className="ui-table-compact" style={{ marginTop: 10 }}>
              <thead><tr><th style={{ minWidth: 120, whiteSpace: 'nowrap' }}>Mã thành phần</th><th style={{ minWidth: 180 }}>Tên thành phần</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 125, whiteSpace: 'nowrap' }}>Định mức / Bộ Kit</th></tr></thead>
              <tbody>
                {bundleComponents.map(item => (
                  <tr key={item.id}><td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.childProductCode}</strong></td><td>{item.childProductName}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.quantity}</td></tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard
        title="Lệnh Gia Công Lắp Ráp & Tháo Dỡ Bộ Kit Vật Lý Tại Sàn Kho"
        subtitle="Đối ứng nguyên tử sổ cái tồn kho: xuất linh kiện cấu thành, nhập thành phẩm Kit kèm kết chuyển giá vốn bình quân gia quyền và kế thừa hạn dùng ngắn nhất"
      >
        <form onSubmit={createAndCompleteKitWorkOrder} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <UiToolbarField label="Loại lệnh gia công">
            <select aria-label="Loại lệnh gia công Kit" data-testid="select-kit-work-order-type" value={kitWorkOrderType} onChange={e => setKitWorkOrderType(e.target.value as 'Assemble' | 'Disassemble')} style={{ height: 38, boxSizing: 'border-box', minWidth: 210 }}>
              <option value="Assemble">Lắp ráp đóng gói</option>
              <option value="Disassemble">Tháo dỡ phân rã</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="SL bộ Kit thực hiện">
            <input aria-label="Số lượng bộ Kit thực hiện" data-testid="input-kit-planned-qty" type="number" min="1" step="1" value={kitPlannedQty} onChange={e => setKitPlannedQty(e.target.value)} style={{ height: 38, boxSizing: 'border-box', width: 130 }} />
          </UiToolbarField>
          <button type="submit" data-testid="btn-execute-kit-work-order" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>+ Tạo &amp; Hoàn Tất Lệnh Gia Công Kit Tại Sàn</button>
          <button type="button" className="ui-btn-secondary" data-testid="btn-reload-kit-work-orders" disabled={mutating} onClick={() => void loadKitWorkOrders()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tải danh sách Lệnh Gia Công Kit</button>
        </form>

        <UiTableScroll>
          <table aria-label="Danh sách lệnh gia công lắp ráp tháo dỡ bộ Kit" data-testid="kit-work-orders-table" className="ui-table-compact" style={{ marginTop: 10, width: '100%' }}>
            <thead>
              <tr>
                <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Mã lệnh</th>
                <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Loại gia công</th>
                <th style={{ minWidth: 155 }}>Sản phẩm &amp; Đơn vị</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 115, whiteSpace: 'nowrap' }}>SL đạt / KH</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 135, whiteSpace: 'nowrap' }}>Giá vốn cộng dồn</th>
                <th style={{ minWidth: 140 }}>Hạn dùng kế thừa</th>
                <th style={{ minWidth: 115, whiteSpace: 'nowrap' }}>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {kitWorkOrders.length === 0 ? (
                <tr><td colSpan={7} className="ui-empty-cell">Chưa có lệnh gia công lắp ráp / tháo dỡ bộ Kit vật lý nào — Nhấn &ldquo;+ Tạo &amp; Hoàn Tất Lệnh Gia Công Kit Tại Sàn&rdquo; để thực thi.</td></tr>
              ) : (
                kitWorkOrders.map(kwo => {
                  const allowCost = canReadCostRbac && kwo.canReadCost !== false && kwo.rolledUpUnitCost !== null;
                  return (
                    <tr key={kwo.id}>
                      <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{kwo.workOrderCode}</strong></td>
                      <td><UiBadge tone={kwo.workOrderType === 'Assemble' ? 'success' : 'warning'}>{kwo.workOrderType === 'Assemble' ? 'Lắp ráp' : 'Tháo dỡ'}</UiBadge></td>
                      <td><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{kwo.kitProductCode}</strong> – {kwo.kitProductName} <UiBadge tone="neutral">{kwo.baseUomCode || 'PCS'} (x{kwo.conversionRateSnapshot ?? 1})</UiBadge></td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>{kwo.completedKitQty}</strong> / {kwo.plannedKitQty}</td>
                      <td data-testid="kit-wo-rolledup-cost" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                        {allowCost ? (
                          `${Number(kwo.rolledUpUnitCost).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} ₫`
                        ) : (
                          <span className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                            <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
                          </span>
                        )}
                      </td>
                      <td data-testid="kit-wo-earliest-expiry" style={{ whiteSpace: 'nowrap' }}>
                        <span className="ui-code" style={{ whiteSpace: 'nowrap' }}>{kwo.completedLotNumber || 'LOT-KIT-2026'}</span> • HSD: {kwo.earliestComponentExpiryDate ? new Date(kwo.earliestComponentExpiryDate).toLocaleDateString('vi-VN') : 'Chưa xác định'}
                      </td>
                      <td><UiBadge tone={kwo.status === 'Completed' ? 'success' : kwo.status === 'Cancelled' ? 'danger' : 'neutral'}>{kwo.status === 'Completed' ? 'Đã hoàn thành' : kwo.status === 'Cancelled' ? 'Đã hủy (Đảo sổ cái)' : kwo.status}</UiBadge></td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>

      <ProductPromotionGiftRulesCard
        products={products}
        mutating={mutating}
        mutate={mutate}
        setNotice={setNotice}
        setError={setError}
      />
    </div>
  );
}
