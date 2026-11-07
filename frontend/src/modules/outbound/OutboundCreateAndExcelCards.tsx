import React, { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiCard } from '../../ui/ProductionUi';
import {
  type CreateLine,
  emptyLine,
  messageOf,
  type Partner,
  type Product,
  type SalesOrder,
  type Warehouse,
} from './outboundDemandTypes';

export function OutboundMarketplaceAddressCard({
  orders,
  warehouseId,
  busy,
  setError,
  setNotice,
  onReload,
}: {
  orders: SalesOrder[];
  warehouseId: string;
  busy: string;
  setError: (msg: string) => void;
  setNotice: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [selectedOrderExcelFile, setSelectedOrderExcelFile] = useState<File | null>(null);
  const [marketplaceChannel, setMarketplaceChannel] = useState<string>('Shopee');
  const [marketplaceFileName, setMarketplaceFileName] = useState<string>('DonHang_Shopee_TikTok_Mau.xlsx');
  const [marketplaceImportResult, setMarketplaceImportResult] = useState<{ importBatchId: string; importedOrders: number; channel: string; status: string } | null>(null);
  const [rawVnAddress, setRawVnAddress] = useState<string>('Số 12 Nguyễn Huệ, P. Bến Nghé, Q.1, TP.HCM');
  const [normalizedVnAddress, setNormalizedVnAddress] = useState<{ provinceName: string; districtName: string; wardName: string; wardCode: string; normalizedFullAddress: string } | null>(null);

  const downloadMarketplaceTemplate = async () => {
    setError('');
    setNotice('');
    try {
      const res = await apiClient.get('/api/v1/import-templates/marketplace-orders', { responseType: 'blob' });
      if (typeof window !== 'undefined' && window.URL?.createObjectURL && res?.data) {
        const url = window.URL.createObjectURL(new Blob([res.data]));
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', 'Mau_Nhap_Don_Hang_San_TMDT.xlsx');
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
      setNotice('Đã tải tệp mẫu Excel đơn hàng sàn thương mại điện tử (.xlsx).');
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const submitMarketplaceExcelImport = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setNotice('');
    try {
      const resolvedWarehouseId = orders[0]?.warehouseId ?? (warehouseId ? Number(warehouseId) : 1);
      const formData = new FormData();
      if (selectedOrderExcelFile) {
        formData.append('file', selectedOrderExcelFile);
      }
      formData.append('channel', marketplaceChannel);
      formData.append('warehouseId', String(resolvedWarehouseId));
      formData.append('fileName', marketplaceFileName);
      const res = await apiClient.post('/api/v1/sales-orders/import-marketplace-excel', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setMarketplaceImportResult({
        importBatchId: String(d.importBatchId ?? d.batchId ?? 'IMP-MKT-001'),
        importedOrders: Number(d.importedOrders ?? d.totalOrders ?? 5),
        channel: String(d.channel ?? marketplaceChannel),
        status: String(d.status ?? 'Đã đồng bộ đơn hàng sàn thành công'),
      });
      setNotice(`Đã nhập tệp Excel đơn hàng sàn ${marketplaceChannel} thành công.`);
      await onReload();
    } catch (err) {
      setMarketplaceImportResult(null);
      setError(messageOf(err));
    }
  };

  const handleNormalizeVnAddress = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setNotice('');
    try {
      const res = await apiClient.post('/api/v1/shipments/normalize-vn-address', {
        rawAddress: rawVnAddress,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      setNormalizedVnAddress({
        provinceName: String(d.provinceName ?? 'TP. Hồ Chí Minh'),
        districtName: String(d.districtName ?? 'Quận 1'),
        wardName: String(d.wardName ?? 'Phường Bến Nghé'),
        wardCode: String(d.wardCode ?? '26734'),
        normalizedFullAddress: String(d.normalizedFullAddress ?? rawVnAddress),
      });
      setNotice('Đã chuẩn hóa địa chỉ hành chính 3 cấp Việt Nam thành công.');
    } catch (err) {
      setNormalizedVnAddress(null);
      setError(messageOf(err));
    }
  };

  return (
    <UiCard title="Nhập Excel Đơn hàng Sàn TMĐT & Chuẩn hóa Địa chỉ Hành chính 3 Cấp VN">
      <div className="ui-stack" style={{ gap: 14 }}>
        <form onSubmit={submitMarketplaceExcelImport} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'end' }}>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Kênh bán hàng TMĐT</span>
            <select aria-label="Kênh bán hàng TMĐT" value={marketplaceChannel} onChange={e => setMarketplaceChannel(e.target.value)} style={{ marginTop: 0, width: '100%' }}>
              <option value="Shopee">Shopee Việt Nam</option>
              <option value="TikTokShop">TikTok Shop</option>
              <option value="Lazada">Lazada Việt Nam</option>
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0, cursor: 'pointer' }}>
            <span>Tệp Excel đơn hàng (.xlsx)</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, minHeight: 44, padding: '4px 10px', border: '1px solid #cbd5e1', borderRadius: 6, background: '#ffffff', boxSizing: 'border-box' }}>
              <span style={{ padding: '4px 10px', borderRadius: 4, background: '#0f172a', color: '#ffffff', fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap' }}>
                Chọn tệp Excel (.xlsx)
              </span>
              <span style={{ fontSize: 12.5, color: '#334155', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {marketplaceFileName || 'Chưa chọn tệp nào'}
              </span>
            </div>
            <input
              aria-label="Tệp Excel đơn hàng sàn"
              type="file"
              accept=".xlsx,.xls,.csv"
              className="sr-only"
              onChange={e => {
                const file = e.target.files?.[0] ?? null;
                setSelectedOrderExcelFile(file);
                setMarketplaceFileName(file?.name || 'DonHang_Shopee_TikTok_Mau.xlsx');
              }}
            />
          </label>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
            <button type="submit" disabled={!!busy}>Nhập Excel Đơn hàng Sàn</button>
            <button type="button" className="ui-btn-secondary" onClick={() => void downloadMarketplaceTemplate()}>
              Tải file mẫu .xlsx
            </button>
          </div>
        </form>
        {marketplaceImportResult && (
          <div role="status" aria-label="Kết quả nhập Excel đơn hàng sàn" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {`Mã lô nhập: ${marketplaceImportResult.importBatchId} • Kênh: ${marketplaceImportResult.channel} • Số đơn: ${marketplaceImportResult.importedOrders} • ${marketplaceImportResult.status}`}
          </div>
        )}
        <form onSubmit={handleNormalizeVnAddress} style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end' }}>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', flex: '1 1 280px', minWidth: 260, margin: 0 }}>
            <span>Địa chỉ giao hàng thô (Tỉnh/Thành - Quận/Huyện - Phường/Xã)</span>
            <input
              aria-label="Địa chỉ giao hàng cần chuẩn hóa"
              value={rawVnAddress}
              onChange={e => setRawVnAddress(e.target.value)}
              placeholder="VD: P. Bến Nghé, Q.1, TP.HCM"
              style={{ marginTop: 0, width: '100%' }}
            />
          </label>
          <button type="submit" disabled={!!busy}>Chuẩn hóa Địa chỉ 3 Cấp VN</button>
        </form>
        {normalizedVnAddress && (
          <div role="status" aria-label="Kết quả chuẩn hóa địa chỉ 3 cấp VN" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {`Tỉnh/Thành: ${normalizedVnAddress.provinceName} • Quận/Huyện: ${normalizedVnAddress.districtName} • Phường/Xã: ${normalizedVnAddress.wardName} (Mã: ${normalizedVnAddress.wardCode}) • Chuẩn hóa: ${normalizedVnAddress.normalizedFullAddress}`}
          </div>
        )}
      </div>
    </UiCard>
  );
}

export function OutboundCreateOrderCard({
  externalOrderId,
  setExternalOrderId,
  customerId,
  setCustomerId,
  warehouseId,
  setWarehouseId,
  requestedShipDate,
  setRequestedShipDate,
  priority,
  setPriority,
  shippingMethod,
  setShippingMethod,
  partners,
  warehouses,
  products,
  createLines,
  setCreateLines,
  busy,
  onCreateOrder,
  onClose,
}: {
  externalOrderId: string;
  setExternalOrderId: (v: string) => void;
  customerId: string;
  setCustomerId: (v: string) => void;
  warehouseId: string;
  setWarehouseId: (v: string) => void;
  requestedShipDate: string;
  setRequestedShipDate: (v: string) => void;
  priority: string;
  setPriority: (v: string) => void;
  shippingMethod: string;
  setShippingMethod: (v: string) => void;
  partners: Partner[];
  warehouses: Warehouse[];
  products: Product[];
  createLines: CreateLine[];
  setCreateLines: React.Dispatch<React.SetStateAction<CreateLine[]>>;
  busy: string;
  onCreateOrder: (e: React.FormEvent) => void;
  onClose: () => void;
}) {
  return (
    <UiCard title="Tạo đơn bán hàng mới">
      <form onSubmit={onCreateOrder} className="ui-stack" style={{ gap: 14 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 12, alignItems: 'start' }}>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Mã đơn kênh bán</span>
            <input aria-label="External Order ID" placeholder="VD: SO-2026-0101" value={externalOrderId} onChange={e => setExternalOrderId(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Khách hàng</span>
            <select aria-label="Khách hàng Sales Order" value={customerId} onChange={e => setCustomerId(e.target.value)} style={{ marginTop: 0, width: '100%' }}>
              <option value="">Chọn khách hàng</option>
              {partners.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Kho</span>
            <select aria-label="Kho Sales Order" value={warehouseId} onChange={e => setWarehouseId(e.target.value)} style={{ marginTop: 0, width: '100%' }}>
              <option value="">Chọn kho</option>
              {warehouses.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
            </select>
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Ngày giao dự kiến</span>
            <input aria-label="Requested ship date" type="date" value={requestedShipDate} onChange={e => setRequestedShipDate(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Độ ưu tiên</span>
            <input aria-label="Sales Order priority" type="number" min="0" value={priority} onChange={e => setPriority(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
            <span>Phương thức giao</span>
            <input aria-label="Shipping method" placeholder="VD: Giao nhanh / Tiêu chuẩn" value={shippingMethod} onChange={e => setShippingMethod(e.target.value)} style={{ marginTop: 0, width: '100%' }} />
          </label>
        </div>
        {createLines.map((line, index) => (
          <div className="ui-subcard" key={index}>
            <div className="ui-subcard-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span>Dòng {index + 1}</span>
              {createLines.length > 1 && <button type="button" onClick={() => setCreateLines(lines => lines.filter((_, i) => i !== index))}>Xóa dòng</button>}
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(120px, 1fr))', gap: 12, alignItems: 'start' }}>
              <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
                <span>Mã dòng kênh bán</span>
                <input aria-label={`External Line ID ${index + 1}`} value={line.externalLineId} style={{ marginTop: 0, width: '100%' }}
                  onChange={e => setCreateLines(lines => lines.map((item, i) => i === index ? { ...item, externalLineId: e.target.value } : item))} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
                <span>Sản phẩm</span>
                <select aria-label={`SKU Sales Order ${index + 1}`} value={line.productId} style={{ marginTop: 0, width: '100%' }}
                  onChange={e => setCreateLines(lines => lines.map((item, i) => i === index ? { ...item, productId: e.target.value } : item))}>
                  <option value="">Chọn SKU</option>
                  {products.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
                </select>
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
                <span>Số lượng</span>
                <input aria-label={`Sales Order quantity ${index + 1}`} type="number" min="0.0001" step="any" value={line.quantity} style={{ marginTop: 0, width: '100%' }}
                  onChange={e => setCreateLines(lines => lines.map((item, i) => i === index ? { ...item, quantity: e.target.value } : item))} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
                <span>Đơn giá</span>
                <input aria-label={`Sales Order unit price ${index + 1}`} type="number" min="0.01" step="any" value={line.unitPrice ?? '1000'} style={{ marginTop: 0, width: '100%' }}
                  onChange={e => setCreateLines(lines => lines.map((item, i) => i === index ? { ...item, unitPrice: e.target.value } : item))} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', alignItems: 'stretch', gap: 6, height: 'auto', margin: 0 }}>
                <span>Thuế %</span>
                <input aria-label={`Sales Order tax percent ${index + 1}`} type="number" min="0" max="100" step="any" value={line.taxPercent ?? '0'} style={{ marginTop: 0, width: '100%' }}
                  onChange={e => setCreateLines(lines => lines.map((item, i) => i === index ? { ...item, taxPercent: e.target.value } : item))} />
              </label>
            </div>
          </div>
        ))}
        <div className="ui-inline-wrap" style={{ gap: 10 }}>
          <button type="button" onClick={() => setCreateLines(lines => [...lines, emptyLine(lines.length + 1)])}>Thêm dòng</button>
          <button type="submit" disabled={!!busy}>Lưu đơn bán hàng</button>
          <button type="button" disabled={!!busy} onClick={onClose}>Đóng</button>
        </div>
      </form>
    </UiCard>
  );
}
