import React, { useCallback, useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiBadge, UiCard, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';
import {
  type ChannelSkuMappingItem,
  mapRawChannelMappings,
  messageOf,
  type Product,
  type ProductCatalogTab,
} from './ProductCatalogTypes';

export function ProductChannelMappingSection({
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
  const [channelMappings, setChannelMappings] = useState<ChannelSkuMappingItem[]>([]);
  const [mappingChannelCode, setMappingChannelCode] = useState<string>('SHOPEE');
  const [mappingExternalSkuCode, setMappingExternalSkuCode] = useState<string>('');
  const [mappingExternalSkuName, setMappingExternalSkuName] = useState<string>('');
  const [mappingInternalProductId, setMappingInternalProductId] = useState<number>(0);
  const [mappingConversionFactor, setMappingConversionFactor] = useState<string>('2');

  const fetchChannelSkuMappings = useCallback(async () => {
    try {
      const res = await apiClient.get('/api/v1/products/channel-sku-mappings').catch(() => ({ data: [] }));
      const raw = res?.data?.items ?? res?.data?.mappings ?? res?.data;
      setChannelMappings(mapRawChannelMappings(Array.isArray(raw) ? raw : []));
    } catch {
      setChannelMappings([]);
    }
  }, []);

  useEffect(() => { void fetchChannelSkuMappings(); }, [fetchChannelSkuMappings]);

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
          channelCode: mappingChannelCode, externalSkuCode: extCode, externalSkuName: extName,
          internalProductId: resolvedInternalId, conversionFactor: factor,
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

  return (
    <div style={activeTab === 'channel-mapping' ? { display: 'contents' } : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
      <UiCard
        title="Bàn Làm Việc Ánh Xạ Mã Hàng Đa Sàn TMĐT (Shopee / TikTok Shop / Lazada)"
        subtitle="Đồng bộ mã mặt hàng trên các sàn thương mại điện tử với mã hàng tồn kho nội bộ và hệ số quy đổi tự động khi nhận đơn"
      >
        <form onSubmit={saveChannelSkuMapping} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <UiToolbarField label="Kênh bán TMĐT">
            <select aria-label="Kênh bán TMĐT ánh xạ" value={mappingChannelCode} onChange={e => setMappingChannelCode(e.target.value)} style={{ height: 38, boxSizing: 'border-box', minWidth: 160 }}>
              <option value="SHOPEE">Shopee Mall / Shopee</option>
              <option value="TIKTOK">TikTok Shop</option>
              <option value="LAZADA">Lazada</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Mã SKU trên Sàn">
            <input aria-label="Mã SKU trên Sàn" value={mappingExternalSkuCode} onChange={e => setMappingExternalSkuCode(e.target.value)} placeholder="VD: SHP-CB-02-CAFE" style={{ height: 38, boxSizing: 'border-box', minWidth: 190 }} />
          </UiToolbarField>
          <UiToolbarField label="Tên phân loại trên Sàn">
            <input aria-label="Tên phân loại trên Sàn" value={mappingExternalSkuName} onChange={e => setMappingExternalSkuName(e.target.value)} placeholder="VD: Combo 2 Gói Cà Phê Rang Xay" style={{ height: 38, boxSizing: 'border-box', minWidth: 260 }} />
          </UiToolbarField>
          <UiToolbarField label="Mã SKU Kho Nội Bộ">
            <select aria-label="Sản phẩm kho nội bộ ánh xạ" value={mappingInternalProductId || activeUomProduct?.id || products[0]?.id || 0} onChange={e => setMappingInternalProductId(Number(e.target.value))} style={{ height: 38, boxSizing: 'border-box', minWidth: 220 }}>
              {products.length === 0 ? <option value={0}>Chưa có sản phẩm nội bộ</option> : null}
              {products.map(item => <option key={item.id} value={item.id}>{item.code} – {item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Hệ số quy đổi (Combo/Lẻ)">
            <input aria-label="Hệ số quy đổi SKU sàn" type="number" min="1" step="1" value={mappingConversionFactor} onChange={e => setMappingConversionFactor(e.target.value)} style={{ height: 38, boxSizing: 'border-box', width: 120 }} />
          </UiToolbarField>
          <button type="submit" data-testid="btn-save-channel-sku-mapping" disabled={mutating} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>+ Lưu Ánh Xạ SKU Sàn</button>
          <button type="button" className="ui-btn-secondary" disabled={mutating} onClick={() => void fetchChannelSkuMappings()} style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }}>Tải lại Ánh xạ Sàn</button>
        </form>

        <UiTableScroll>
          <table aria-label="Bảng ánh xạ mã hàng đa sàn TMĐT" className="ui-table-compact" style={{ marginTop: 12, width: '100%' }}>
            <thead>
              <tr>
                <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Kênh bán</th>
                <th style={{ minWidth: 135 }}>Gian hàng</th>
                <th style={{ minWidth: 130, whiteSpace: 'nowrap' }}>Mã SKU trên Sàn</th>
                <th style={{ minWidth: 135, whiteSpace: 'nowrap' }}>Mã SKU Kho Nội Bộ</th>
                <th style={{ minWidth: 165 }}>Tên sản phẩm</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 110, whiteSpace: 'nowrap' }}>Hệ số quy đổi</th>
                <th style={{ minWidth: 135, whiteSpace: 'nowrap' }}>Trạng thái đồng bộ</th>
              </tr>
            </thead>
            <tbody>
              {channelMappings.length === 0 ? (
                <tr><td colSpan={7} className="ui-empty-cell">Chưa có bản ghi ánh xạ mã hàng sàn TMĐT — Nhấn &ldquo;+ Lưu Ánh Xạ SKU Sàn&rdquo; để liên kết nhanh.</td></tr>
              ) : channelMappings.map(row => (
                <tr key={row.id}>
                  <td><UiBadge>{row.channelCode}</UiBadge></td>
                  <td>{row.shopName}</td>
                  <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.externalSkuCode}</strong></td>
                  <td style={{ whiteSpace: 'nowrap' }}><strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{row.internalProductCode}</strong></td>
                  <td>{row.externalSkuName || row.internalProductName}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{`×${row.conversionFactor}`}</td>
                  <td><UiBadge tone="success">{row.syncStatus === 'ACTIVE' || row.syncStatus === 'SYNCED' ? 'Đã đồng bộ tự động' : row.syncStatus}</UiBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </div>
  );
}
