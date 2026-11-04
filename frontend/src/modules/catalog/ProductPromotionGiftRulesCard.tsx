import React, { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiCard, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';
import { messageOf, type Product } from './ProductCatalogTypes';

interface GiftRuleItem {
  id: number;
  ruleName: string;
  minOrderValue: number;
  giftProductId: number;
  giftProductCode: string;
  giftProductName: string;
  giftQuantity: number;
  isActive: boolean;
}

export function ProductPromotionGiftRulesCard({
  products,
  mutating,
  mutate,
  setNotice,
  setError,
}: {
  products: Product[];
  mutating: boolean;
  mutate: (action: () => Promise<void>) => Promise<void>;
  setNotice: React.Dispatch<React.SetStateAction<string>>;
  setError: React.Dispatch<React.SetStateAction<string>>;
}) {
  const [giftRuleName, setGiftRuleName] = useState<string>('');
  const [giftMinOrderValue, setGiftMinOrderValue] = useState<string>('500000');
  const [giftProductId, setGiftProductId] = useState<number>(0);
  const [giftQuantity, setGiftQuantity] = useState<string>('1');
  const [giftRules, setGiftRules] = useState<GiftRuleItem[]>([]);

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
  );
}
