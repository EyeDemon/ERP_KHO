import { type FormEvent } from 'react';
import {
  UiBadge,
  UiCard,
  UiTableScroll,
} from '../../ui/ProductionUi';
import { productUnitOptions } from '../../pages/inboundPlanningShared';

export type PurchaseOrderList = {
  id: number; externalPoId: string; code: string; supplierCode: string; supplierName: string;
  warehouseId: number; warehouseName: string; status: string; orderDate: string; expectedDate?: string;
  baseOrderedQuantity: number; subTotalAmount?: number; taxAmount?: number; totalAmount?: number;
};
export type PurchaseOrderLine = {
  id: number; externalLineId: string; lineNo: number; productId: number; productCode: string; productName: string;
  orderedQuantity: number; operationUnitCode: string; baseOrderedQuantity: number; baseUnitCode: string;
  unitPrice?: number; taxPercent?: number; lineSubTotal?: number; lineTaxAmount?: number; lineTotal?: number; lineTotalAmount?: number;
  allowedOverReceiptPct: number; allowedUnderReceiptPct: number;
};
export type PurchaseOrderDetail = PurchaseOrderList & {
  sourceSystem: string; currency?: string; externalVersion?: string; rowVersion?: string; lines: PurchaseOrderLine[];
};
export type Warehouse = { id: number; name: string };
export type Partner = { id: number; code: string; name: string; isActive: boolean };
export type ProductUom = { unitId: number; unitCode: string; unitName: string; decimalPlaces: number; conversionFactor: number; version: number };
export type Product = { id: number; code: string; name: string; unitId: number; unitCode: string; unitName: string; uoms: ProductUom[] };
export type LineForm = { externalLineId: string; productId: number | ''; operationUnitId: number | ''; orderedQuantity: number | ''; unitPrice: number | ''; taxPercent: number | ''; allowedOverReceiptPct: number | ''; allowedUnderReceiptPct: number | '' };

export const labels: Record<string, string> = {
  Draft: 'Nháp', Open: 'Đang mở', Approved: 'Đã xác nhận', Confirmed: 'Đã xác nhận', PartiallyReceived: 'Nhận một phần', Received: 'Đã nhận đủ', Closed: 'Đã đóng', Cancelled: 'Đã hủy',
};
export const procurementSignalLabels: Record<string, string> = {
  DISPATCHED_TO_SUPPLIER: 'Đã gửi thông báo đôn đốc nhà cung cấp & giữ cửa ưu tiên',
  PROCUREMENT_INTELLIGENCE_READY: 'Đề xuất mua hàng thông minh sẵn sàng',
  REORDER_THRESHOLD_PROPOSALS_READY: 'Đã lập đề xuất điểm đặt hàng lại',
  P90_LEAD_TIME_CALIBRATED: 'Đã hiệu chuẩn thời gian giao hàng P90',
  EXPEDITE_AND_DEFER_BALANCED: 'Đã cân bằng tín hiệu đôn đốc & giãn tiến độ',
};
export const translateProcurementSignal = (value?: string | null) => value ? (procurementSignalLabels[value] ?? value) : '';
export const tone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' | 'info' =>
  status === 'Approved' || status === 'Confirmed' || status === 'Completed' || status === 'Received' || status === 'Closed'
    ? 'success'
    : status === 'Open' || status === 'PartiallyReceived'
      ? 'info'
      : status === 'Cancelled'
        ? 'danger'
        : 'neutral';
export const isoDay = (value?: string) => {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const yyyy = d.getFullYear();
  return `${dd}/${mm}/${yyyy}`;
};
export const isPoOverdue = (item: PurchaseOrderList) => {
  if (!['Open', 'PartiallyReceived'].includes(item.status) || !item.expectedDate) return false;
  const expectedMs = new Date(item.expectedDate).getTime();
  const todayMs = new Date(new Date().toISOString().slice(0, 10)).getTime();
  return Number.isFinite(expectedMs) && expectedMs < todayMs;
};
export const normalizeUnitCode = (unitCode?: string) => (unitCode === 'QA_UNIT' || unitCode === 'QA-PCS') ? 'THÙNG' : (unitCode || '');
export const emptyLine = (): LineForm => ({ externalLineId: '', productId: '', operationUnitId: '', orderedQuantity: '', unitPrice: 0, taxPercent: 0, allowedOverReceiptPct: 0, allowedUnderReceiptPct: 0 });

export function PurchaseOrderCreateFormCard(props: {
  canPrepareCreate: boolean;
  creating: boolean;
  sourceSystem: string;
  setSourceSystem: (v: string) => void;
  externalPoId: string;
  setExternalPoId: (v: string) => void;
  code: string;
  setCode: (v: string) => void;
  supplierId: number | '';
  setSupplierId: (v: number | '') => void;
  suppliers: Partner[];
  warehouseId: number | '';
  setWarehouseId: (v: number | '') => void;
  warehouses: Warehouse[];
  currency: string;
  setCurrency: (v: string) => void;
  orderDate: string;
  setOrderDate: (v: string) => void;
  expectedDate: string;
  setExpectedDate: (v: string) => void;
  lines: LineForm[];
  setLines: React.Dispatch<React.SetStateAction<LineForm[]>>;
  changeLine: (index: number, patch: Partial<LineForm>) => void;
  products: Product[];
  submitCreate: (e: FormEvent) => Promise<void>;
  resetForm: () => void;
  onClose: () => void;
}) {
  const {
    canPrepareCreate, creating, sourceSystem, setSourceSystem, externalPoId, setExternalPoId,
    code, setCode, supplierId, setSupplierId, suppliers, warehouseId, setWarehouseId, warehouses,
    currency, setCurrency, orderDate, setOrderDate, expectedDate, setExpectedDate, lines, setLines,
    changeLine, products, submitCreate, resetForm, onClose,
  } = props;
  return (
    <UiCard
      title="Tạo đơn mua nháp"
      subtitle="Khai báo thông tin đơn mua nguồn, nhà cung cấp, kho nhận và danh mục dòng hàng đặt mua"
    >
      <div className="ui-inline-actions" style={{ marginBottom: 8 }}>
        <button type="button" onClick={onClose}>Thu gọn biểu mẫu</button>
      </div>
      {!canPrepareCreate && <div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br />Cần quyền đọc kho, sản phẩm và đối tác để tạo đơn mua an toàn.</div>}
      {canPrepareCreate && <form className="inbound-planning-form" onSubmit={submitCreate} style={{ display: 'grid', gap: 16 }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'start', padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Hệ thống nguồn *</span><input required value={sourceSystem} onChange={e => setSourceSystem(e.target.value)} /></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Mã đơn mua ngoài hệ thống *</span><input required value={externalPoId} onChange={e => setExternalPoId(e.target.value)} /></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Mã đơn mua kho *</span><input required value={code} onChange={e => setCode(e.target.value)} /></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Nhà cung cấp *</span><select required value={supplierId} onChange={e => setSupplierId(e.target.value ? Number(e.target.value) : '')}><option value="">Chọn nhà cung cấp</option>{suppliers.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Kho nhận *</span><select required value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')}><option value="">Chọn kho</option>{warehouses.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Tiền tệ tham chiếu</span><input value={currency} onChange={e => setCurrency(e.target.value.toUpperCase())} maxLength={10} /></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Ngày đặt ({isoDay(orderDate)}) *</span><input required type="date" value={orderDate} onChange={e => setOrderDate(e.target.value)} /></label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Ngày dự kiến ({expectedDate ? isoDay(expectedDate) : 'dd/mm/yyyy'})</span><input type="date" value={expectedDate} onChange={e => setExpectedDate(e.target.value)} /></label>
        </div>

        <div>
          <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng đặt mua</strong><button type="button" onClick={() => setLines(current => [...current, emptyLine()])}>+ Thêm dòng</button></div>
          <div className="inbound-planning-lines">
            {lines.map((line, index) => <div className="inbound-planning-line inbound-planning-line-grid" key={index} style={{ alignItems: 'start' }}>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Mã dòng nguồn *</span><input required value={line.externalLineId} onChange={e => changeLine(index, { externalLineId: e.target.value })} /></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Sản phẩm *</span><select required value={line.productId} onChange={e => changeLine(index, { productId: e.target.value ? Number(e.target.value) : '' })}><option value="">Chọn sản phẩm</option>{products.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Đơn vị *</span><select required value={line.operationUnitId} onChange={e => changeLine(index, { operationUnitId: e.target.value ? Number(e.target.value) : '' })}><option value="">Chọn đơn vị</option>{productUnitOptions(products, line.productId).map(x => <option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Số lượng *</span><input required min="0" step="any" type="number" value={line.orderedQuantity} onChange={e => changeLine(index, { orderedQuantity: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Đơn giá</span><input min="0" step="any" type="number" value={line.unitPrice} onChange={e => changeLine(index, { unitPrice: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Thuế %</span><input min="0" max="100" step="any" type="number" value={line.taxPercent} onChange={e => changeLine(index, { taxPercent: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Nhận vượt %</span><input min="0" max="100" step="any" type="number" value={line.allowedOverReceiptPct} onChange={e => changeLine(index, { allowedOverReceiptPct: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}><span>Nhận thiếu %</span><input min="0" max="100" step="any" type="number" value={line.allowedUnderReceiptPct} onChange={e => changeLine(index, { allowedUnderReceiptPct: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
              <button type="button" style={{ height: 38, alignSelf: 'end', border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5' }} disabled={lines.length === 1} onClick={() => setLines(current => current.filter((_, i) => i !== index))}>Xóa</button>
            </div>)}
          </div>
        </div>
        <p className="inbound-planning-helper">Tạo đơn mua chỉ ghi nhận lượng hàng dự kiến về; tồn kho chỉ thay đổi khi Phiếu nhập kho được ghi sổ.</p>
        <div className="inbound-planning-form-actions">
          <button type="submit" disabled={creating} style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }}>{creating ? 'Đang tạo...' : 'Tạo đơn mua'}</button>
          <button type="button" disabled={creating} onClick={resetForm}>Làm lại</button>
        </div>
      </form>}
    </UiCard>
  );
}

export function PurchaseOrderDetailCard(props: {
  selected: PurchaseOrderDetail;
  canRelease: boolean;
  canClose: boolean;
  canCancel: boolean;
  busy: string;
  command: (path: string, action: string, successMessage: string) => Promise<void>;
}) {
  const { selected, canRelease, canClose, canCancel, busy, command } = props;
  return (
    <UiCard title={'Chi tiết ' + selected.code}>
      <div className="inbound-planning-detail-meta">
        <div>
          <strong>Trạng thái</strong><br />
          <div style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
            <UiBadge tone={tone(selected.status)}><span style={{ whiteSpace: 'nowrap' }}>{labels[selected.status] ?? selected.status}</span></UiBadge>
            {isPoOverdue(selected) && <UiBadge tone="danger"><span style={{ whiteSpace: 'nowrap' }}>Quá hạn</span></UiBadge>}
          </div>
        </div>
        <div><strong>Nhà cung cấp</strong><br />{selected.supplierCode} — {selected.supplierName}</div>
        <div><strong>Nguồn ngoài hệ thống</strong><br />{selected.sourceSystem} / {selected.externalPoId}</div>
        <div><strong>Kho nhận</strong><br />{selected.warehouseName}</div>
        <div><strong>Ngày đặt</strong><br />{isoDay(selected.orderDate)}</div>
        <div><strong>Ngày dự kiến</strong><br />{isoDay(selected.expectedDate)}</div>
        <div><strong>Tiền tệ tham chiếu</strong><br />{selected.currency || '—'}</div>
        <div><strong>Tổng SL cơ sở</strong><br />{selected.baseOrderedQuantity.toLocaleString('vi-VN')} THÙNG</div>
        <div><strong>Tổng tiền đơn mua (Đã gồm thuế)</strong><br />{Number(selected.totalAmount || 0).toLocaleString('vi-VN')} đ</div>
      </div>
      <div className="inbound-planning-actions">
        {canRelease && selected.status === 'Draft' && <button className="ui-primary-button" style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }} disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/purchase-orders/' + selected.id + '/open', 'po-open:' + selected.id, 'Đơn mua đã được mở cho quy trình nhận hàng.')}>{busy ? 'Đang lưu...' : 'Mở đơn mua'}</button>}
        {canClose && ['PartiallyReceived', 'Received'].includes(selected.status) && <button disabled={!!busy || !selected.rowVersion} onClick={() => void command('/api/v1/purchase-orders/' + selected.id + '/close', 'po-close:' + selected.id, 'Đơn mua đã được đóng.')}>Đóng đơn mua</button>}
        {canCancel && ['Draft', 'Open'].includes(selected.status) && <button style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }} disabled={!!busy || !selected.rowVersion} onClick={() => window.confirm('Xác nhận hủy đơn mua này?') && void command('/api/v1/purchase-orders/' + selected.id + '/cancel', 'po-cancel:' + selected.id, 'Đơn mua đã được hủy.')}>Hủy đơn mua</button>}
      </div>
      <UiTableScroll>
        <table className="ui-table-compact" aria-label={'Dòng đơn mua ' + selected.code}>
          <thead><tr><th>Dòng</th><th>Sản phẩm</th><th>Đơn vị</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL đặt</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>SL cơ sở</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đơn giá / Thuế</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thành tiền</th><th className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Dung sai</th></tr></thead>
          <tbody>{selected.lines.map(line => {
            const opUnit = normalizeUnitCode(line.operationUnitCode);
            const baseUnit = normalizeUnitCode(line.baseUnitCode);
            return <tr key={line.id}>
              <td>{line.externalLineId}</td>
              <td><strong style={{ whiteSpace: 'nowrap' }}>{line.productCode}</strong><br /><small>{line.productName}</small></td>
              <td>{opUnit || '—'}</td>
              <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.orderedQuantity.toLocaleString('vi-VN')}</td>
              <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{line.baseOrderedQuantity.toLocaleString('vi-VN')}{(line.baseUnitCode === 'QA_UNIT' || line.baseUnitCode === 'QA-PCS') && baseUnit === opUnit ? '' : ' ' + baseUnit}</td>
              <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{Number(line.unitPrice || 0).toLocaleString('vi-VN')} ({Number(line.taxPercent || 0)}%)</td>
              <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{Number(line.lineTotal ?? line.lineTotalAmount ?? 0).toLocaleString('vi-VN')}</td>
              <td className="inbound-planning-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>+{line.allowedOverReceiptPct}% / -{line.allowedUnderReceiptPct}%</td>
            </tr>;
          })}</tbody>
        </table>
      </UiTableScroll>
    </UiCard>
  );
}

export function PurchaseOrderFulfillmentMonitorCard(props: {
  fulfillmentSummary: {
    overdueExpectedCount: number;
    receivedOrClosedCount: number;
    totalBaseOrderedQuantity: number;
    completionRate: number;
    activePriorityItems: PurchaseOrderList[];
  };
  totalItemsCount: number;
  busy: string;
  creating: boolean;
  governanceBusy: boolean;
  expediteOverdue: () => Promise<void>;
  generateProcurementRecommendations: () => Promise<void>;
  expediteResult: { expediteId: string; vendorNotificationStatus: string } | null;
  procurementRecResult: {
    recommendationId: string;
    procurementSuggestionStatus: string;
    supplierLeadTimePercentile: string;
    expediteDeferSignal: string;
    status: string;
  } | null;
}) {
  const {
    fulfillmentSummary, totalItemsCount, busy, creating, governanceBusy,
    expediteOverdue, generateProcurementRecommendations, expediteResult, procurementRecResult,
  } = props;
  return (
    <UiCard title="Giám sát tiến độ tiếp nhận" subtitle="Tổng hợp tiến độ giao hàng theo đơn mua, cảnh báo quá hạn và điều phối đôn đốc nhà cung cấp">
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12, marginBottom: 12 }}>
        <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>Đơn mua quá hạn dự kiến</div>
          <UiBadge tone={fulfillmentSummary.overdueExpectedCount > 0 ? 'danger' : 'success'}>
            {fulfillmentSummary.overdueExpectedCount} đơn mua quá hạn
          </UiBadge>
        </div>
        <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>Tổng SL cơ sở đặt hàng</div>
          <strong style={{ fontSize: 16, fontVariantNumeric: 'tabular-nums' }}>{fulfillmentSummary.totalBaseOrderedQuantity.toLocaleString('vi-VN')} THÙNG</strong>
        </div>
        <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <div style={{ fontSize: 12, color: '#475569', marginBottom: 4 }}>Tỷ lệ hoàn tất (Nhận đủ / Đóng)</div>
          <strong style={{ fontSize: 16, fontVariantNumeric: 'tabular-nums' }}>{fulfillmentSummary.completionRate}% ({fulfillmentSummary.receivedOrClosedCount}/{totalItemsCount})</strong>
        </div>
      </div>
      <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff', fontSize: 13, marginBottom: 10 }}>
        <strong>Đơn mua sắp/quá hạn cần ưu tiên tiếp nhận: </strong>
        {fulfillmentSummary.activePriorityItems.length === 0
          ? 'Không có đơn mua mở cần ưu tiên'
          : fulfillmentSummary.activePriorityItems.map(po => `${po.code} (${po.supplierCode} • Hạn: ${isoDay(po.expectedDate)})`).join(' | ')}
      </div>
      <div className="ui-inline-actions" style={{ marginTop: 8, gap: 8, flexWrap: 'wrap' }}>
        <button
          type="button"
          aria-label="Gửi Yêu cầu Đôn đốc NCC & Giữ Cửa Tiếp nhận Ưu tiên"
          title="Gửi Yêu cầu Đôn đốc NCC & Giữ Cửa Tiếp nhận Ưu tiên"
          style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8' }}
          disabled={!!busy || creating || governanceBusy}
          onClick={() => void expediteOverdue()}
        >
          Đôn đốc NCC &amp; Giữ cửa ưu tiên
        </button>
        <button
          type="button"
          aria-label="Sinh Đề xuất Mua hàng, Phân tích Thời gian giao NCC & Khuyến nghị Đôn đốc/Hoãn"
          title="Sinh Đề xuất Mua hàng, Phân tích Thời gian giao NCC & Khuyến nghị Đôn đốc/Hoãn"
          style={{ background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1', fontWeight: 600 }}
          disabled={!!busy || creating || governanceBusy}
          onClick={() => void generateProcurementRecommendations()}
        >
          Đề xuất mua hàng &amp; Phân tích tiến độ NCC
        </button>
      </div>
      {expediteResult && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {`${expediteResult.expediteId} • ${expediteResult.vendorNotificationStatus}`}
        </div>
      )}
      {procurementRecResult && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {`${procurementRecResult.recommendationId} • ${procurementRecResult.status} (${procurementRecResult.procurementSuggestionStatus} / ${procurementRecResult.supplierLeadTimePercentile} / ${procurementRecResult.expediteDeferSignal})`}
        </div>
      )}
    </UiCard>
  );
}
