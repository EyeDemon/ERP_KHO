import React from 'react';
import { UiCard } from '../../ui/ProductionUi';
import { roundQty4 } from '../../pages/productCatalog';
import {
  type AsnOption,
  type ImportReceipt,
  type Partner,
  type PoOption,
  type Product,
  type ReceiptDetailForm,
  type Warehouse,
  nextShortReceiptCode,
} from './ImportReceiptTypes';

export interface ImportReceiptCreateFormCardProps {
  showCreateForm: boolean;
  setShowCreateForm: React.Dispatch<React.SetStateAction<boolean>>;
  code: string;
  setCode: (val: string) => void;
  warehouseId: number | '';
  setWarehouseId: (val: number | '') => void;
  note: string;
  setNote: (val: string) => void;
  supplierId: number | '';
  setSupplierId: (val: number | '') => void;
  purchaseOrderId: string;
  setPurchaseOrderId: (val: string) => void;
  asnId: string;
  setAsnId: (val: string) => void;
  details: ReceiptDetailForm[];
  setDetails: (val: ReceiptDetailForm[]) => void;
  receipts: ImportReceipt[];
  warehouses: Warehouse[];
  suppliers: Partner[];
  products: Product[];
  purchaseOrders: PoOption[];
  asns: AsnOption[];
  filteredAsns: AsnOption[];
  creating: boolean;
  handleCreate: (e: React.FormEvent) => Promise<void>;
  handleAddDetail: () => void;
  handleRemoveDetail: (index: number) => void;
  handleDetailChange: (index: number, field: keyof ReceiptDetailForm, value: any) => void;
}

export const ImportReceiptCreateFormCard: React.FC<ImportReceiptCreateFormCardProps> = ({
  showCreateForm,
  setShowCreateForm,
  code,
  setCode,
  warehouseId,
  setWarehouseId,
  note,
  setNote,
  supplierId,
  setSupplierId,
  purchaseOrderId,
  setPurchaseOrderId,
  asnId,
  setAsnId,
  details,
  setDetails,
  receipts,
  warehouses,
  suppliers,
  products,
  purchaseOrders,
  asns,
  filteredAsns,
  creating,
  handleCreate,
  handleAddDetail,
  handleRemoveDetail,
  handleDetailChange,
}) => (
  <div style={{ order: 3 }}>
    <UiCard
      title="Tạo Phiếu Nhập (Nháp)"
      subtitle="Khởi tạo phiếu nhập kho nháp liên kết đồng bộ Đơn mua hàng (PO) và Thông báo giao hàng (ASN)"
    >
      <div className="ui-inline-actions" style={{ marginBottom: showCreateForm ? 12 : 0 }}>
        <button
          type="button"
          onClick={() => {
            if (!showCreateForm && !code.trim()) setCode(nextShortReceiptCode(receipts));
            setShowCreateForm(v => !v);
          }}
          style={{ background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}
        >
          {showCreateForm ? 'Thu gọn biểu mẫu' : 'Mở biểu mẫu tạo mới'}
        </button>
      </div>
      <div style={showCreateForm ? undefined : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
        <form onSubmit={handleCreate} className="import-form">
          <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', marginBottom: 8 }}>
            <strong style={{ display: 'block', fontSize: 13, color: '#0f172a', marginBottom: 10 }}>1. Thông tin chung &amp; Nhà cung cấp</strong>
            <div className="import-form-grid">
              <label className="ui-stack">
                <span>Mã phiếu</span>
                <input aria-label="Mã phiếu" placeholder="VD: PN-2026-0004" value={code} onChange={e => setCode(e.target.value)} required style={{ maxWidth: 320 }} />
              </label>
              <label className="ui-stack">
                <span>Kho</span>
                <select aria-label="Kho" value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required style={{ maxWidth: 320 }}>
                  <option value="">-- Chọn kho --</option>
                  {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                </select>
              </label>
              <label className="ui-stack">
                <span>Ghi chú phiếu</span>
                <input aria-label="Ghi chú phiếu" value={note} onChange={e => setNote(e.target.value)} />
              </label>
              <label className="ui-stack">
                <span>Nhà cung cấp</span>
                <select aria-label="Nhà cung cấp" value={supplierId} onChange={e => setSupplierId(e.target.value ? Number(e.target.value) : '')} style={{ maxWidth: 320 }}>
                  <option value="">-- Không chọn --</option>
                  {suppliers.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}
                </select>
              </label>
            </div>
          </div>

          <h3>2. Chi tiết dòng hàng nhập</h3>
          <div className="ui-stack">
            {details.map((d, i) => {
              const p = products.find(x => x.id === d.productId);
              const u = p?.uoms?.find(x => x.unitId === d.operationUnitId);
              return (
                <div key={i} className="import-line">
                  <label className="ui-stack">
                    <span>Sản phẩm</span>
                    <select
                      aria-label={'Sản phẩm dòng ' + (i + 1)}
                      value={d.productId}
                      style={{ maxWidth: 320 }}
                      onChange={e => {
                        const productId = e.target.value ? Number(e.target.value) : '';
                        const product = products.find(x => x.id === productId);
                        const next = [...details];
                        next[i] = { ...next[i], productId, operationUnitId: product?.unitId || '' };
                        setDetails(next);
                      }}
                      required
                    >
                      <option value="">-- Chọn sản phẩm --</option>
                      {products.map(prod => <option key={prod.id} value={prod.id}>{prod.code} - {prod.name}</option>)}
                    </select>
                  </label>
                  <label className="ui-stack">
                    <span>Đơn vị thao tác</span>
                    <select aria-label={'Đơn vị thao tác dòng ' + (i + 1)} value={d.operationUnitId} onChange={e => handleDetailChange(i, 'operationUnitId', e.target.value ? Number(e.target.value) : '')} required style={{ maxWidth: 320 }}>
                      <option value="">-- Đơn vị thao tác --</option>
                      {(p?.uoms || []).map(uom => <option key={uom.unitId + '-' + uom.version} value={uom.unitId}>{uom.unitCode} — Quy đổi: 1 = {uom.conversionFactor} ĐV cơ sở</option>)}
                    </select>
                  </label>
                  <label className="ui-stack">
                    <span>Số lượng dự kiến</span>
                    <input inputMode="decimal" role="spinbutton" aria-label={'Số lượng dự kiến dòng ' + (i + 1)} placeholder="Số lượng dự kiến" value={d.expectedQuantity} onChange={e => handleDetailChange(i, 'expectedQuantity', e.target.value ? Number(e.target.value) : '')} required min="0.01" step="0.01" />
                  </label>
                  <label className="ui-stack">
                    <span>Đơn giá</span>
                    <input inputMode="decimal" role="spinbutton" aria-label={'Đơn giá dòng ' + (i + 1)} placeholder="Đơn giá" value={d.unitPrice} onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')} required min="0" step="0.01" />
                  </label>
                  <label className="ui-stack">
                    <span>Ghi chú</span>
                    <input aria-label={'Ghi chú dòng ' + (i + 1)} placeholder="Ghi chú" value={d.note} onChange={e => handleDetailChange(i, 'note', e.target.value)} />
                  </label>
                  <div className="import-actions">
                    <button type="button" aria-label={'Xóa dòng ' + (i + 1)} onClick={() => handleRemoveDetail(i)}>Xóa</button>
                  </div>
                  {p && u && d.expectedQuantity !== '' && (
                    <div className="import-line-summary">Quy đổi: 1 {u.unitCode.toLowerCase()} = {u.conversionFactor} {p.unitCode} ({d.expectedQuantity} {u.unitCode} = {roundQty4(Number(d.expectedQuantity) * u.conversionFactor)} {p.unitCode} ĐV cơ sở)</div>
                  )}
                </div>
              );
            })}
          </div>

          <div className="import-section-actions">
            <div style={{ width: '100%', padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', marginBottom: 10 }}>
              <strong style={{ display: 'block', fontSize: 13, color: '#0f172a', marginBottom: 8 }}>3. Liên kết chứng từ Đơn mua (PO) &amp; Thông báo giao hàng (ASN)</strong>
              <div className="import-form-grid" style={{ width: '100%' }}>
                <label className="ui-stack">
                  <span>Đơn mua hàng (PO) liên kết</span>
                  <select
                    aria-label="Liên kết Đơn mua hàng (PO)"
                    value={purchaseOrderId}
                    style={{ maxWidth: 320 }}
                    onChange={e => {
                      const nextPoId = e.target.value;
                      setPurchaseOrderId(nextPoId);
                      const poObj = purchaseOrders.find(po => String(po.id) === nextPoId);
                      if (poObj) {
                        if (poObj.warehouseId) setWarehouseId(poObj.warehouseId);
                        if (poObj.supplierId) setSupplierId(poObj.supplierId);
                        const currentAsn = asns.find(a => String(a.id) === asnId);
                        const currentAsnMatchesPo = currentAsn && ((currentAsn.purchaseOrderId != null && String(currentAsn.purchaseOrderId) === nextPoId) || (currentAsn.purchaseOrderCode && currentAsn.purchaseOrderCode === poObj.code));
                        if (!currentAsnMatchesPo) {
                          const matchingAsn = asns.find(a => (a.purchaseOrderId != null && String(a.purchaseOrderId) === nextPoId) || (a.purchaseOrderCode && a.purchaseOrderCode === poObj.code));
                          setAsnId(matchingAsn ? String(matchingAsn.id) : '');
                        }
                      }
                    }}
                  >
                    <option value="">-- Không liên kết PO --</option>
                    {purchaseOrders.map(po => <option key={po.id} value={String(po.id)}>{po.code}{po.supplierName ? ` — ${po.supplierName}` : ''}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Thông báo giao hàng trước (ASN)</span>
                  <select
                    aria-label="Liên kết Thông báo giao hàng (ASN)"
                    value={asnId}
                    style={{ maxWidth: 320 }}
                    onChange={e => {
                      const nextAsnId = e.target.value;
                      setAsnId(nextAsnId);
                      const asnObj = asns.find(a => String(a.id) === nextAsnId);
                      if (asnObj) {
                        if (asnObj.warehouseId) setWarehouseId(asnObj.warehouseId);
                        if (asnObj.supplierId) setSupplierId(asnObj.supplierId);
                        const linkedPo = purchaseOrders.find(po => (asnObj.purchaseOrderId != null && po.id === asnObj.purchaseOrderId) || (asnObj.purchaseOrderCode && po.code === asnObj.purchaseOrderCode));
                        if (linkedPo && String(linkedPo.id) !== purchaseOrderId) setPurchaseOrderId(String(linkedPo.id));
                      }
                    }}
                  >
                    <option value="">-- Không liên kết ASN --</option>
                    {filteredAsns.map(asn => <option key={asn.id} value={String(asn.id)}>{asn.code}{asn.purchaseOrderCode ? ` (PO: ${asn.purchaseOrderCode})` : ''}{asn.supplierName ? ` — ${asn.supplierName}` : ''}</option>)}
                  </select>
                </label>
              </div>
            </div>
            <button type="button" onClick={handleAddDetail}>+ Thêm dòng</button>
            <button type="submit" disabled={creating} style={{ background: '#0284c7', color: '#ffffff', borderColor: '#0284c7', fontWeight: 600 }}>
              {creating ? 'Đang lưu...' : (
                <>
                  <span>Lưu phiếu nháp</span>
                  <span style={{ position: 'absolute', width: 1, height: 1, padding: 0, margin: -1, overflow: 'hidden', clip: 'rect(0, 0, 0, 0)', whiteSpace: 'nowrap', border: 0 }}>Lưu Phiếu Nháp</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </UiCard>
  </div>
);
