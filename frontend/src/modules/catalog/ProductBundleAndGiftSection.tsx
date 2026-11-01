import React, { useCallback, useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { hasPermission } from '../../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import { UiBadge, UiCard, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';
import { messageOf, type Product, type ProductCatalogTab } from './ProductCatalogTypes';

const checkCanReadInventoryCost = (): boolean => {
  try {
    const perms = localStorage.getItem('user_permissions') || localStorage.getItem('permissions') || '';
    if (perms.includes('inventory.cost.read')) return true;
    const role = (localStorage.getItem('role') || '').trim();
    if (role === 'Staff' || role === 'Viewer') return false;
    if (role === 'Admin' || role === 'Manager' || role === 'Accountant') return true;
    return hasPermission('inventory.cost.read');
  } catch {
    return true;
  }
};

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
  const [giftRuleName, setGiftRuleName] = useState<string>('');
  const [giftMinOrderValue, setGiftMinOrderValue] = useState<string>('500000');
  const [giftProductId, setGiftProductId] = useState<number>(0);
  const [giftQuantity, setGiftQuantity] = useState<string>('1');
  const [giftRules, setGiftRules] = useState<Array<{ id: number; ruleName: string; minOrderValue: number; giftProductId: number; giftProductCode: string; giftProductName: string; giftQuantity: number; isActive: boolean }>>([]);
  const [kitWorkOrderType, setKitWorkOrderType] = useState<'Assemble' | 'Disassemble'>('Assemble');
  const [kitPlannedQty, setKitPlannedQty] = useState<string>('2');
  const canReadCostRbac = checkCanReadInventoryCost();
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
        const res = await apiClient.post(
          '/api/v1/sales-orders/promotion-gift-rules',
          { ruleName: resolvedRuleName, minOrderValue: minVal, giftProductId: resolvedGiftId, giftQuantity: giftQty, unitPrice: 0, isActive: true },
        );
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
              <thead><tr><th>Mã thành phần</th><th>Tên thành phần</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Định mức / Bộ Kit</th></tr></thead>
              <tbody>
                {bundleComponents.map(item => (
                  <tr key={item.id}><td><strong>{item.childProductCode}</strong></td><td>{item.childProductName}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.quantity}</td></tr>
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
            <select aria-label="Loại lệnh gia công Kit" data-testid="select-kit-work-order-type" value={kitWorkOrderType} onChange={e => setKitWorkOrderType(e.target.value as 'Assemble' | 'Disassemble')} style={{ height: 38, boxSizing: 'border-box', minWidth: 210 }}>
              <option value="Assemble">Lắp ráp đóng gói (Assemble)</option>
              <option value="Disassemble">Tháo dỡ phân rã (Disassemble)</option>
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
                <th>Mã lệnh KWO</th>
                <th>Loại gia công</th>
                <th>Sản phẩm Kit &amp; ĐVT chuẩn (Base UOM)</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL Kế hoạch / Hoàn thành</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Giá vốn cộng dồn (Rolled-Up MAC)</th>
                <th>Hạn dùng kế thừa dự kiến (FEFO Min Expiry)</th>
                <th>Trạng thái</th>
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
                      <td><strong>{kwo.workOrderCode}</strong></td>
                      <td><UiBadge tone={kwo.workOrderType === 'Assemble' ? 'success' : 'warning'}>{kwo.workOrderType === 'Assemble' ? 'Lắp ráp (Assemble)' : 'Tháo dỡ (Disassemble)'}</UiBadge></td>
                      <td><strong>{kwo.kitProductCode}</strong> – {kwo.kitProductName} <UiBadge tone="neutral">{kwo.baseUomCode || 'PCS'} (x{kwo.conversionRateSnapshot ?? 1})</UiBadge></td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}><strong>{kwo.completedKitQty}</strong> / {kwo.plannedKitQty}</td>
                      <td data-testid="kit-wo-rolledup-cost" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {allowCost ? `${Number(kwo.rolledUpUnitCost).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} đ` : 'Ẩn theo quyền RBAC (inventory.cost.read)'}
                      </td>
                      <td data-testid="kit-wo-earliest-expiry">
                        {kwo.completedLotNumber || 'LOT-KIT-AUTO'} • HSD: {kwo.earliestComponentExpiryDate ? new Date(kwo.earliestComponentExpiryDate).toLocaleDateString('vi-VN') : 'Chưa xác định'}
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

      <UiCard title="Cấu hình Quà tặng Kèm 0đ Tự động theo Giá trị Đơn hàng">
        <form onSubmit={savePromotionGiftRule} className="ui-inline-wrap" style={{ alignItems: 'end' }}>
          <UiToolbarField label="Tên chương trình khuyến mãi">
            <input aria-label="Tên chương trình khuyến mãi quà tặng" value={giftRuleName} onChange={e => setGiftRuleName(e.target.value)} placeholder="VD: Tặng kèm quà 0đ đơn từ 500.000đ" style={{ height: 38, boxSizing: 'border-box', minWidth: 240 }} />
          </UiToolbarField>
          <UiToolbarField label="Giá trị đơn tối thiểu (VNĐ)">
            <input aria-label="Giá trị đơn hàng tối thiểu (VNĐ)" type="number" min="0" step="1000" value={giftMinOrderValue} onChange={e => setGiftMinOrderValue(e.target.value)} style={{ height: 38, boxSizing: 'border-box', width: 150 }} />
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm quà tặng 0đ">
            <select aria-label="Sản phẩm quà tặng 0đ" value={giftProductId || products[0]?.id || 0} onChange={e => setGiftProductId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box' }}>
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm quà tặng</option> : null}
              {products.map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Số lượng tặng kèm">
            <input aria-label="Số lượng quà tặng kèm" type="number" min="1" step="1" value={giftQuantity} onChange={e => setGiftQuantity(e.target.value)} style={{ height: 38, boxSizing: 'border-box', width: 110 }} />
          </UiToolbarField>
          <button type="submit" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Lưu quy tắc Quà tặng 0đ</button>
          <button type="button" className="ui-btn-secondary" disabled={mutating} onClick={() => void loadPromotionGiftRules()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tải danh sách Quy tắc Quà tặng 0đ</button>
        </form>
        {giftRules.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách quy tắc quà tặng 0đ" className="ui-table-compact" style={{ marginTop: 10 }}>
              <thead><tr><th>Chương trình</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Giá trị đơn tối thiểu</th><th>Quà tặng kèm (Đơn giá 0đ)</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng tặng</th></tr></thead>
              <tbody>
                {giftRules.map(rule => (
                  <tr key={rule.id}><td><strong>{rule.ruleName}</strong></td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{rule.minOrderValue.toLocaleString('vi-VN')} đ</td><td>{rule.giftProductCode} – {rule.giftProductName}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{rule.giftQuantity}</td></tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </div>
  );
}
