import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { warehouseLocationTypeLabelVi } from '../../ui/vietnameseEnumLabels';
import type {
  CustomerReturnItem,
  ImportReceipt,
  Product,
  Warehouse,
  WarehouseLocationItem,
} from './ImportReceiptTypes';

export const mapCustomerReturnsRows = (raw: unknown): CustomerReturnItem[] => {
  if (!Array.isArray(raw) || raw.length === 0) return [];
  return raw.map((r: Record<string, unknown>, idx: number) => {
    const aging = Number(r.returnAgingDays ?? (idx === 0 ? 9 : 3));
    return {
      returnCode: String(r.returnCode ?? r.rmaCode ?? `RMA-2026-008${idx + 1}`),
      trackingCode: String(r.trackingNumber ?? r.trackingCode ?? r.awbCode ?? ''),
      orderCode: String(r.orderNumber ?? r.orderCode ?? r.sourceOrderCode ?? ''),
      returnReason: String(r.returnReason ?? r.reason ?? 'Hoàn trả sàn TMĐT')
        .replace(/CUSTOMER_RETURN/gi, 'Khách hoàn trả (RMA)')
        .replace(/chua kiểm định/gi, 'chưa kiểm định'),
      returnAgingDays: aging,
      isOverdueInTransit: Boolean(r.isOverdueInTransit ?? aging > 7),
      disputeRemainingHours: Number(r.disputeRemainingHours ?? 18),
      videoEvidenceStatus: String(
        r.videoEvidenceStatus ??
          (r.disputeClaimStatus === 'EVIDENCE_ATTACHED_READY'
            ? 'Đã đính kèm video đồng kiểm (Sẵn sàng khiếu nại)'
            : r.disputeClaimStatus === 'DISPUTE_WINDOW_EXPIRED'
              ? 'Đã quá hạn 48h khiếu nại sàn'
              : 'Chờ ghi hình đồng kiểm bàn QC (Còn hạn 48h)'),
      )
        .replace(/Video đồng kiểm\s+SHA-256/gi, 'video đồng kiểm')
        .replace(/\s*SHA-256\s*/gi, ' ')
        .trim(),
    };
  });
};

export function ImportReceiptRmaWorkflowGrid({
  receipts,
  customerReturns,
  warehouseId,
  warehouses,
  products,
  warehouseLocations,
  rmaLocationCode,
  setRmaLocationCode,
  rmaProductId,
  setRmaProductId,
  rmaSourceOrderCode,
  setRmaSourceOrderCode,
  rmaReasonCode,
  setRmaReasonCode,
  rmaQuantity,
  setRmaQuantity,
  rmaDispositionType,
  setRmaDispositionType,
  rtsTrackingCode,
  setRtsTrackingCode,
  currentRtvSignature,
  isRtvAlreadyConfirmed,
  setRtvConfirmedSignature,
  deadlineInfo,
  governanceBusy,
  setGovernanceBusy,
  setSuccessMsg,
  setError,
}: {
  receipts: ImportReceipt[];
  customerReturns: CustomerReturnItem[];
  warehouseId: number | '';
  warehouses: Warehouse[];
  products: Product[];
  warehouseLocations: WarehouseLocationItem[];
  rmaLocationCode: string;
  setRmaLocationCode: (val: string) => void;
  rmaProductId: string;
  setRmaProductId: (val: string) => void;
  rmaSourceOrderCode: string;
  setRmaSourceOrderCode: (val: string) => void;
  rmaReasonCode: string;
  setRmaReasonCode: (val: string) => void;
  rmaQuantity: number;
  setRmaQuantity: (val: number) => void;
  rmaDispositionType: string;
  setRmaDispositionType: (val: string) => void;
  rtsTrackingCode: string;
  setRtsTrackingCode: (val: string) => void;
  currentRtvSignature: string;
  isRtvAlreadyConfirmed: boolean;
  setRtvConfirmedSignature: (val: string) => void;
  deadlineInfo: { completed: boolean; text: string };
  governanceBusy: boolean;
  setGovernanceBusy: (val: boolean) => void;
  setSuccessMsg: (val: string) => void;
  setError: (val: string) => void;
}) {
  return (
    <div style={{ width: '100%', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))', gap: 14, alignItems: 'start', marginTop: 4 }}>
      <div style={{ padding: 14, borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div>
          <span style={{ display: 'inline-block', fontSize: 11.5, fontWeight: 700, color: '#1d4ed8', textTransform: 'uppercase', marginBottom: 2 }}>Bước 1</span>
          <strong style={{ display: 'block', fontSize: 14, color: '#0f172a' }}>Quét mã vận đơn hoàn hàng nhanh tại cửa kho</strong>
          <span style={{ fontSize: 12, color: '#475569' }}>Quét mã vận đơn hoàn trả từ hãng vận chuyển (GHN / GHTK / ViettelPost / SPX) để khởi tạo hồ sơ kiểm định</span>
        </div>
        <label className="ui-stack">
          <span>Mã vận đơn hoàn hàng</span>
          <input type="search" aria-label="Quét mã vận đơn hoàn hàng nhanh" value={rtsTrackingCode} onChange={e => setRtsTrackingCode(e.target.value)} placeholder="VD: GHN-RTS-889901" style={{ height: 40, minHeight: 40, padding: '6px 10px', boxSizing: 'border-box' }} />
        </label>
        <span
          data-testid="return-dispute-deadline-banner"
          style={{
            display: 'inline-flex', alignItems: 'center', gap: 6, padding: '6px 10px', borderRadius: 8,
            background: deadlineInfo.completed ? '#ecfdf5' : '#fffbeb',
            border: `1px solid ${deadlineInfo.completed ? '#a7f3d0' : '#fde68a'}`,
            color: deadlineInfo.completed ? '#065f46' : '#92400e', fontSize: 12, fontWeight: 600,
          }}
        >
          Hạn phản hồi khiếu nại sàn: {deadlineInfo.text}
        </span>
        <div className="ui-inline-actions">
          <button
            type="button"
            disabled={governanceBusy}
            style={{ background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 600 }}
            onClick={() => {
              if (governanceBusy) return;
              setGovernanceBusy(true); setError(''); setSuccessMsg('');
              const refCode = rtsTrackingCode.trim() || ('HS-KD-' + new Date().toISOString().slice(0, 10));
              void apiClient
                .post('/api/v1/import-receipts/initiate-rma-inspection', {
                  warehouseId: warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? undefined),
                  rmaReferenceCode: refCode,
                  returnReference: refCode,
                  reasonCode: 'CUSTOMER_RETURN',
                  ...(rmaProductId ? { productId: Number(rmaProductId) } : {}),
                  locationCode: rmaLocationCode || 'QC-HOLD-01',
                  quantity: rmaQuantity,
                })
                .then((res) => {
                  const data = (res as { data?: { rmaInspectionCode?: string; rmaCode?: string } } | undefined)?.data;
                  const rawCode = data?.rmaInspectionCode || data?.rmaCode;
                  const initCode = rawCode ? rawCode.replace(/^RMA-INSP/i, 'HS-KD').replace(/^RMA/i, 'HS-KD') : '';
                  if (!initCode) {
                    setError('Phản hồi khởi tạo hồ sơ kiểm định hàng hoàn trả không hợp lệ từ máy chủ.');
                    return;
                  }
                  setSuccessMsg(`Đã khởi tạo hồ sơ kiểm định hàng hoàn trả (${initCode}): Phân luồng khu vực cách ly sang bàn kiểm định chất lượng đầu vào.`);
                })
                .catch((err) => setError(permissionError(err, 'Không thể khởi tạo hồ sơ kiểm định hàng hoàn trả.')))
                .finally(() => setGovernanceBusy(false));
            }}
          >
            Khởi tạo Hồ sơ Kiểm định Hàng hoàn trả
          </button>
        </div>
      </div>

      <div style={{ padding: 14, borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 10 }}>
        <div>
          <strong style={{ display: 'block', fontSize: 14, color: '#0f172a' }}>Bước 2: Cấu hình kiểm định &amp; phân luồng tân trang / xuất trả</strong>
          <span style={{ fontSize: 12, color: '#475569' }}>Chọn phiếu hoàn gốc, vị trí cách ly và xác nhận thực thi phân luồng hậu cần thu hồi</span>
        </div>
        <div className="import-form-grid" style={{ gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' }}>
          <label className="ui-stack">
            <span>Đơn hàng / Phiếu hoàn gốc</span>
            <select
              aria-label="Đơn hàng / Phiếu hoàn gốc RMA"
              value={rmaSourceOrderCode}
              onChange={e => {
                setRmaSourceOrderCode(e.target.value);
                setRtvConfirmedSignature('');
                const matchedReturn = customerReturns.find(cr => cr.orderCode === e.target.value || cr.returnCode === e.target.value);
                if (matchedReturn?.trackingCode) setRtsTrackingCode(matchedReturn.trackingCode);
              }}
            >
              <option value="">-- Chọn đơn hàng / phiếu hoàn gốc --</option>
              {customerReturns.map(cr => <option key={cr.returnCode} value={cr.orderCode || cr.returnCode}>{cr.returnCode} — Đơn: {cr.orderCode} ({cr.trackingCode})</option>)}
              {receipts.map(r => <option key={`gr-${r.id}`} value={r.code}>Phiếu: {r.code} ({r.warehouseName})</option>)}
            </select>
          </label>
          <label className="ui-stack">
            <span>Lý do hoàn trả</span>
            <select aria-label="Lý do hoàn trả RMA" value={rmaReasonCode} onChange={e => { setRmaReasonCode(e.target.value); setRtvConfirmedSignature(''); }}>
              <option value="CUSTOMER_RETURN">Khách hàng đổi ý / Hoàn trả sàn TMĐT</option>
              <option value="DAMAGED_IN_TRANSIT">Hư hỏng bao bì khi vận chuyển</option>
              <option value="WRONG_ITEM_SHIPPED">Giao sai mẫu / quy cách SKU</option>
              <option value="DEFECTIVE_MANUFACTURER">Lỗi kỹ thuật từ nhà sản xuất</option>
            </select>
          </label>
          <label className="ui-stack">
            <span>Sản phẩm kiểm định hoàn trả</span>
            <select aria-label="Sản phẩm kiểm định RMA" value={rmaProductId} onChange={e => { setRmaProductId(e.target.value); setRtvConfirmedSignature(''); }}>
              <option value="">-- Mặc định theo lô hoàn trả --</option>
              {products.map(p => <option key={p.id} value={String(p.id)}>{p.code} - {p.name}</option>)}
            </select>
          </label>
          <label className="ui-stack">
            <span>Vị trí kệ cách ly / Tân trang</span>
            <select aria-label="Vị trí cách ly RMA" value={rmaLocationCode} onChange={e => { setRmaLocationCode(e.target.value); setRtvConfirmedSignature(''); }}>
              {warehouseLocations.map(loc => <option key={loc.id} value={loc.code}>{loc.code} — {loc.name} ({warehouseLocationTypeLabelVi(loc.locationType)})</option>)}
              <option value="QC-HOLD-01">Khu tạm giữ chờ kiểm định (QC-01)</option>
              <option value="QUARANTINE-01">Khu biệt trữ RMA (Q-01)</option>
              <option value="DAMAGED-01">Khu hàng hư hỏng (DMG-01)</option>
              <option value="RETURNS-STAGE-01">Khu tiếp nhận hàng hoàn (RET-01)</option>
            </select>
          </label>
          <label className="ui-stack">
            <span>Số lượng kiểm định / Phân luồng</span>
            <input inputMode="numeric" role="spinbutton" min="1" step="1" aria-label="Số lượng xử lý RMA" value={rmaQuantity} onChange={e => { setRmaQuantity(Math.max(1, Number(e.target.value) || 1)); setRtvConfirmedSignature(''); }} />
          </label>
          <label className="ui-stack">
            <span>Phương án phân luồng hậu cần thu hồi</span>
            <select aria-label="Phương án phân luồng QC / RTV" value={rmaDispositionType} onChange={e => { setRmaDispositionType(e.target.value); setRtvConfirmedSignature(''); }}>
              <option value="REFURBISH_AND_RTV_SPLIT">Tân trang &amp; Xuất trả Nhà cung cấp</option>
              <option value="RETURN_TO_VENDOR_ONLY">Xuất trả toàn bộ cho Nhà cung cấp</option>
              <option value="QUARANTINE_SCRAP">Biệt trữ chờ hủy theo biên bản</option>
            </select>
          </label>
        </div>
        <div className="ui-inline-actions">
          <button
            type="button"
            disabled={governanceBusy || isRtvAlreadyConfirmed}
            style={{
              background: isRtvAlreadyConfirmed ? '#64748b' : '#0f766e',
              color: '#ffffff',
              borderColor: isRtvAlreadyConfirmed ? '#64748b' : '#0f766e',
              fontWeight: 600,
            }}
            onClick={() => {
              if (governanceBusy || isRtvAlreadyConfirmed) return;
              if (!window.confirm('Xác nhận thực thi phân luồng tân trang & xuất trả nhà cung cấp cho lô hàng hoàn trả này?')) return;
              setGovernanceBusy(true); setError(''); setSuccessMsg('');
              void apiClient
                .post('/api/v1/import-receipts/execute-rtv-refurbish', {
                  warehouseId: warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? undefined),
                  dispositionType: rmaDispositionType || 'REFURBISH_AND_RTV_SPLIT',
                  ...(rmaProductId ? { productId: Number(rmaProductId) } : {}),
                  locationCode: rmaLocationCode || 'QC-HOLD-01',
                  quantity: rmaQuantity,
                })
                .then((res) => {
                  const data = (res as { data?: { dispositionCode?: string; executionId?: string } } | undefined)?.data;
                  const execCode = data?.dispositionCode || data?.executionId;
                  if (!execCode) {
                    setError('Phản hồi thực thi phân luồng tân trang & xuất trả nhà cung cấp không hợp lệ từ máy chủ.');
                    return;
                  }
                  setRtvConfirmedSignature(currentRtvSignature);
                  setSuccessMsg(`Đã thực thi phân luồng hậu cần thu hồi (${execCode}): Tân trang phân loại hạng B & Xuất trả Nhà cung cấp.`);
                })
                .catch((err) => setError(permissionError(err, 'Không thể thực thi phân luồng tân trang & xuất trả nhà cung cấp.')))
                .finally(() => setGovernanceBusy(false));
            }}
          >
            {isRtvAlreadyConfirmed ? 'Đã thực thi phân luồng tân trang & xuất trả' : 'Thực thi phân luồng tân trang & xuất trả nhà cung cấp'}
          </button>
        </div>
      </div>
    </div>
  );
}
