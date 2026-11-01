import { type FormEvent } from 'react';
import { UiCard } from '../../ui/ProductionUi';
import { productUnitOptions } from '../../pages/inboundPlanningShared';

export type AsnList = {
  id: number; code: string; purchaseOrderId?: number; purchaseOrderCode?: string; supplierCode: string; supplierName: string;
  warehouseId: number; warehouseName: string; status: string; expectedArrivalAtUtc?: string; baseExpectedQuantity: number;
};
export type AsnLine = {
  id: number; purchaseOrderLineId?: number; lineNo: number; productId: number; productCode: string; productName: string;
  expectedQuantity: number; operationUnitCode: string; baseExpectedQuantity: number; baseUnitCode: string;
};
export type AsnDetail = AsnList & { carrierName?: string; vehiclePlate?: string; note?: string; rowVersion?: string; lines: AsnLine[] };
export type PurchaseOrderList = {
  id: number; code: string; supplierCode: string; supplierName: string; warehouseId: number; warehouseName: string; status: string; baseOrderedQuantity: number;
};
export type PurchaseOrderLine = { id: number; lineNo: number; externalLineId: string; productId: number; productCode: string; productName: string; orderedQuantity: number; operationUnitCode: string; baseOrderedQuantity: number; baseUnitCode: string };
export type PurchaseOrderDetail = PurchaseOrderList & { lines: PurchaseOrderLine[] };
export type Warehouse = { id: number; name: string };
export type Partner = { id: number; code: string; name: string };
export type ProductUom = { unitId: number; unitCode: string; unitName: string };
export type Product = { id: number; code: string; name: string; unitId: number; unitCode: string; unitName: string; uoms: ProductUom[] };
export type LineForm = { purchaseOrderLineId: number | ''; productId: number | ''; operationUnitId: number | ''; expectedQuantity: number | '' };

export const labels: Record<string, string> = {
  Draft: 'Nháp', Confirmed: 'Đã xác nhận', InTransit: 'Đang vận chuyển', Arrived: 'Đã đến cổng',
  Receiving: 'Đang tiếp nhận', Received: 'Đã hoàn tất', Completed: 'Đã hoàn tất', Cancelled: 'Đã hủy',
};
export const stagingSignalLabels: Record<string, string> = {
  STAGE_PRE_ALLOCATED: 'Đã cấp phát trước bãi đệm tiếp nhận',
  'STAGE-IN-01': 'Làn đệm tiếp nhận 01 (STAGE-IN-01)',
  'ASN-NEXT-ARRIVAL': 'Lô ASN dự kiến tiếp theo',
};
export const translateStagingSignal = (value?: string | null) => value ? (stagingSignalLabels[value] ?? value) : '';
export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' | 'info' =>
  status === 'Confirmed' || status === 'Completed' || status === 'Received'
    ? 'success'
    : status === 'InTransit'
      ? 'info'
      : status === 'Arrived' || status === 'Receiving'
        ? 'warning'
        : status === 'Cancelled'
          ? 'danger'
          : 'neutral';
export const dateTime = (value?: string) => {
  if (!value) return '—';
  const vnMatch = /^(\d{2})\/(\d{2})\/(\d{4})(?:\s+(\d{2}):(\d{2}))?$/.exec(value.trim());
  if (vnMatch) {
    const [, dd, mm, yyyy, hh = '08', min = '00'] = vnMatch;
    return `${dd}/${mm}/${yyyy} ${hh}:${min}`;
  }
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const hh = String(d.getHours()).padStart(2, '0');
  const min = String(d.getMinutes()).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const yyyy = d.getFullYear();
  return `${dd}/${mm}/${yyyy} ${hh}:${min}`;
};
export const defaultVnArrival = () => {
  const d = new Date(Date.now() + 24 * 3600 * 1000);
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const yyyy = d.getFullYear();
  return `${dd}/${mm}/${yyyy} 08:00`;
};
export const parseArrivalToIso = (value: string): string | null => {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const vnMatch = /^(\d{2})\/(\d{2})\/(\d{4})(?:\s+(\d{2}):(\d{2}))?$/.exec(trimmed);
  if (vnMatch) {
    const [, dd, mm, yyyy, hh = '08', min = '00'] = vnMatch;
    const d = new Date(Number(yyyy), Number(mm) - 1, Number(dd), Number(hh), Number(min), 0);
    return Number.isNaN(d.getTime()) ? null : d.toISOString();
  }
  const d = new Date(trimmed);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
};
export const normalizeUnitCode = (unitCode?: string) => (unitCode === 'QA_UNIT' || unitCode === 'QA-PCS') ? 'THÙNG' : (unitCode || '');
export const emptyLine = (): LineForm => ({ purchaseOrderLineId: '', productId: '', operationUnitId: '', expectedQuantity: '' });

export function AsnCreateFormCard(props: {
  canPrepareCreate: boolean;
  canReadPo: boolean;
  creating: boolean;
  code: string;
  setCode: (v: string) => void;
  purchaseOrderId: number | '';
  choosePurchaseOrder: (v: string) => Promise<void>;
  eligiblePurchaseOrders: PurchaseOrderList[];
  poDetail: PurchaseOrderDetail | null;
  supplierId: number | '';
  setSupplierId: (v: number | '') => void;
  suppliers: Partner[];
  warehouseId: number | '';
  setWarehouseId: (v: number | '') => void;
  warehouses: Warehouse[];
  expectedArrival: string;
  setExpectedArrival: (v: string) => void;
  carrierName: string;
  setCarrierName: (v: string) => void;
  vehiclePlate: string;
  setVehiclePlate: (v: string) => void;
  note: string;
  setNote: (v: string) => void;
  lines: LineForm[];
  setLines: React.Dispatch<React.SetStateAction<LineForm[]>>;
  changeLine: (index: number, patch: Partial<LineForm>) => void;
  products: Product[];
  submitCreate: (e: FormEvent) => Promise<void>;
  resetForm: () => void;
  onClose: () => void;
}) {
  const {
    canPrepareCreate, canReadPo, creating, code, setCode, purchaseOrderId, choosePurchaseOrder,
    eligiblePurchaseOrders, poDetail, supplierId, setSupplierId, suppliers, warehouseId, setWarehouseId,
    warehouses, expectedArrival, setExpectedArrival, carrierName, setCarrierName, vehiclePlate,
    setVehiclePlate, note, setNote, lines, setLines, changeLine, products, submitCreate, resetForm, onClose,
  } = props;
  return (
    <UiCard
      title="Tạo thông báo ASN nháp"
      subtitle="Tự động đồng bộ Nhà cung cấp và Kho nhận khi chọn Đơn mua (PO) nguồn. Ngày giờ hiển thị chuẩn Việt Nam dd/mm/yyyy."
    >
      <div className="ui-inline-actions" style={{ marginBottom: 8 }}>
        <button type="button" onClick={onClose}>Thu gọn biểu mẫu</button>
      </div>
      {!canPrepareCreate && <div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br />Cần quyền đọc kho, sản phẩm và đối tác để tạo ASN an toàn.</div>}
      {canPrepareCreate && <form className="inbound-planning-form" onSubmit={submitCreate} style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'start', padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Mã ASN *</span>
            <input aria-label="Mã ASN" required value={code} onChange={e => setCode(e.target.value)} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Đơn mua nguồn (PO)</span>
            <select aria-label="Đơn mua nguồn" value={purchaseOrderId} onChange={e => void choosePurchaseOrder(e.target.value)} disabled={!canReadPo}>
              <option value="">{canReadPo ? 'Không gắn đơn mua' : 'Không có quyền đọc đơn mua'}</option>
              {eligiblePurchaseOrders.map(x => <option key={x.id} value={x.id}>{x.code} — {x.supplierCode} — {x.warehouseName}</option>)}
            </select>
          </label>
          {purchaseOrderId && poDetail ? <>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Nhà cung cấp (theo PO)</span>
              <input aria-label="Nhà cung cấp theo đơn mua" readOnly value={`${poDetail.supplierCode} — ${poDetail.supplierName}`} style={{ background: '#f1f5f9', color: '#334155' }} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Kho nhận (theo PO)</span>
              <input aria-label="Kho theo đơn mua" readOnly value={poDetail.warehouseName} style={{ background: '#f1f5f9', color: '#334155' }} />
            </label>
          </> : <>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Nhà cung cấp *</span>
              <select aria-label="Nhà cung cấp" required={!purchaseOrderId} value={supplierId} onChange={e => setSupplierId(e.target.value ? Number(e.target.value) : '')}>
                <option value="">Chọn nhà cung cấp</option>
                {suppliers.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Kho nhận *</span>
              <select aria-label="Kho nhận" required={!purchaseOrderId} value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')}>
                <option value="">Chọn kho</option>
                {warehouses.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
            </label>
          </>}
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Dự kiến đến kho (dd/mm/yyyy HH:mm)</span>
            <input
              aria-label="Dự kiến đến kho"
              type="text"
              placeholder="dd/mm/yyyy HH:mm (VD: 31/10/2026 08:00)"
              value={expectedArrival}
              onChange={e => setExpectedArrival(e.target.value)}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Đơn vị vận chuyển</span>
            <input aria-label="Đơn vị vận chuyển" value={carrierName} onChange={e => setCarrierName(e.target.value)} placeholder="VD: Viettel Post / GHN" />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Biển số xe</span>
            <input aria-label="Biển số xe" value={vehiclePlate} onChange={e => setVehiclePlate(e.target.value.toUpperCase())} placeholder="VD: 51C-882.14" />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
            <span>Ghi chú</span>
            <textarea aria-label="Ghi chú" rows={2} value={note} onChange={e => setNote(e.target.value)} />
          </label>
        </div>

        <div>
          <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng dự kiến</strong><button type="button" onClick={() => setLines(current => [...current, emptyLine()])}>+ Thêm dòng</button></div>
          <div className="inbound-planning-lines">
            {lines.map((line, index) => <div className="inbound-planning-line inbound-planning-line-grid compact" key={index} style={{ alignItems: 'start' }}>
              {purchaseOrderId ? <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Dòng đơn mua *</span><select aria-label="Dòng đơn mua" required value={line.purchaseOrderLineId} onChange={e => changeLine(index, { purchaseOrderLineId: e.target.value ? Number(e.target.value) : '' })}><option value="">Chọn dòng đơn mua</option>{poDetail?.lines.map(x => <option key={x.id} value={x.id}>{x.externalLineId} — {x.productCode} — {x.productName}</option>)}</select></label> :
                <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Sản phẩm *</span><select aria-label="Sản phẩm" required value={line.productId} onChange={e => changeLine(index, { productId: e.target.value ? Number(e.target.value) : '' })}><option value="">Chọn sản phẩm</option>{products.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>}
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Đơn vị *</span><select aria-label="Đơn vị" required value={line.operationUnitId} onChange={e => changeLine(index, { operationUnitId: e.target.value ? Number(e.target.value) : '' })}><option value="">Chọn đơn vị</option>{productUnitOptions(products, line.productId).map(x => <option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Số lượng dự kiến *</span><input aria-label="Số lượng dự kiến" required min="0" step="any" type="number" value={line.expectedQuantity} onChange={e => changeLine(index, { expectedQuantity: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <div style={{ paddingTop: 6 }}><strong>{products.find(x => x.id === line.productId)?.code || 'Chưa chọn'}</strong><br /><small>{products.find(x => x.id === line.productId)?.name || 'Chọn nguồn dòng để xác định sản phẩm'}</small></div>
              <button type="button" style={{ height: 38, alignSelf: 'end', border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5' }} disabled={lines.length === 1} onClick={() => setLines(current => current.filter((_, i) => i !== index))}>Xóa</button>
            </div>)}
          </div>
        </div>
        <p className="inbound-planning-helper">Tổng ASN theo từng dòng đơn mua không được vượt số lượng đặt cộng dung sai nhận vượt. ASN không tạo tồn kho.</p>
        <div className="inbound-planning-form-actions">
          <button type="submit" disabled={creating} style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7' }}>{creating ? 'Đang tạo...' : 'Tạo ASN'}</button>
          <button type="button" disabled={creating} onClick={resetForm}>Làm lại</button>
        </div>
      </form>}
    </UiCard>
  );
}
