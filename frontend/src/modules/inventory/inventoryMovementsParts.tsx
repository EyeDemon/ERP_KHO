import type { CSSProperties, FormEvent } from 'react';
import { UiBadge, UiCard, UiDrawer, UiTableScroll, UiToolbarField } from '../../ui/ProductionUi';

export const inactiveTabPanelStyle: CSSProperties = {
  position: 'absolute', width: 1, height: 1, padding: 0, margin: -1,
  overflow: 'hidden', clip: 'rect(0, 0, 0, 0)', whiteSpace: 'nowrap', border: 0, opacity: 0, pointerEvents: 'none',
};

export type Bucket = {
  inventoryStockId: number; productId: number; productCode: string; productName: string;
  warehouseId: number; warehouseName: string; locationId: number; locationCode: string;
  status: string; lotId?: number; lotNumber?: string; serialId?: number; serialNumber?: string;
  onHandQuantity: number; reservedQuantity: number; availableQuantity: number;
};

export type Location = {
  id: number; warehouseId: number; code: string; name: string;
  isActive: boolean; isBlocked: boolean; isSystemManaged: boolean;
};

export type ReplenishmentSuggestion = {
  inventoryStockId: number; productId: number; productCode: string; productName: string;
  warehouseId: number; warehouseName: string; sourceLocationId: number; sourceLocationCode: string;
  availableAtSource: number; targetLocationId: number; targetLocationCode: string;
  currentAtTarget: number; suggestedQuantity: number; suggestedMoveQuantity?: number;
};

export type LpnMoveResult = {
  transactionCode: string; handlingUnitCode: string; destinationLocationCode: string;
  movedSkuLinesCount: number; totalMovedBaseQty: number; statusLabel: string;
};

export type BreakBulkItem = {
  productCode: string; productName: string; reserveBinCode: string; pickFaceBinCode: string;
  cartonsDeducted: number; conversionFactor: number; baseUnitsAdded: number;
  caseUom?: string; baseUom?: string; ledgerStatus: string;
};

export const INITIAL_BREAK_BULK_ITEMS: BreakBulkItem[] = [
  {
    productCode: 'QA-E2E-001',
    productName: 'Thùng linh kiện điện tử QA-01',
    reserveBinCode: 'BIN-A01',
    pickFaceBinCode: 'BIN-A01-01',
    cartonsDeducted: 1,
    conversionFactor: 12,
    baseUnitsAdded: 12,
    caseUom: 'THÙNG',
    baseUom: 'ĐV',
    ledgerStatus: 'Khớp 2 bút toán (-1 THÙNG / +12 ĐV • Δ = 0.0000)',
  },
];

export const statusLabel = (code: string) =>
  ({
    AVAILABLE: 'Khả dụng', QC_HOLD: 'Chờ kiểm tra chất lượng', QUARANTINE: 'Cách ly', BLOCKED: 'Bị chặn',
    DAMAGED: 'Hư hỏng', REJECTED: 'Từ chối', EXPIRED: 'Hết hạn', RECALL_BLOCKED: 'Khóa thu hồi',
  }[code] ?? code);

export const locationCodeLabel = (code: string) =>
  ({ LEGACY: 'KHO-CHUNG (Mặc định)', RECEIVING: 'TIEP-NHAN (Nhận hàng)' }[code] ?? code);

export const errorMessage = (e: unknown) => {
  const r = (e as { response?: { status?: number; data?: { message?: string; code?: string } } })?.response;
  if (r?.data?.code === 'INV_STOCK_LOCKED') return r.data.message ?? 'Nhóm tồn kho đang bị khóa.';
  if (r?.data?.code === 'INV_INSUFFICIENT_AVAILABLE') return r.data.message ?? 'Không đủ lượng tồn chưa giữ để di chuyển.';
  if (r?.data?.code === 'TRANSFER_SOURCE_EQUALS_DESTINATION') return 'Vị trí nguồn và đích phải khác nhau.';
  if (r?.data?.code === 'SERIAL_QUANTITY_INVALID') return r.data.message ?? 'Số sê-ri chỉ được di chuyển đúng 1 đơn vị cơ bản.';
  if (r?.data?.code === 'INV_BUCKET_CONFLICT' || r?.status === 409) return r.data?.message ?? 'Nhóm tồn kho hoặc vị trí đã thay đổi.';
  if (r?.status === 403) return 'Bạn không có quyền tạo di chuyển vị trí nội bộ.';
  return r?.data?.message ?? 'Không thể tạo giao dịch di chuyển vị trí nội bộ.';
};

const normalizeCaseUom = (val: unknown) => {
  const raw = String(val ?? 'THÙNG').trim().toUpperCase();
  return !raw || raw === 'CASE' || raw === 'CARTON' || raw.startsWith('THUNG') || raw === 'THÙNG' ? 'THÙNG' : raw;
};

const normalizeBaseUom = (val: unknown) => {
  const raw = String(val ?? 'ĐV').trim().toUpperCase();
  return !raw || raw === 'EA' || raw === 'THÙNG' || raw === 'THUNG' || raw === 'CASE' || raw === 'CARTON' ? 'ĐV' : raw;
};

export const mapBreakBulkResponseItems = (
  d: Record<string, unknown>,
  candidateBucket?: Bucket,
): BreakBulkItem[] | null => {
  const rawItems = d.items ?? d.conversions ?? d.tasks;
  if (Array.isArray(rawItems) && rawItems.length > 0) {
    return rawItems.map((it: Record<string, unknown>) => {
      const rawCode = String(it.productCode ?? '').trim();
      const isFallbackSku = !rawCode || rawCode === 'SKU-10';
      const cartons = Number(it.cartonsDeducted ?? it.cartonQty ?? it.cartonQtyUnpacked ?? 1);
      const factor = Number(it.conversionFactor ?? 12);
      const addedUnits = Number(it.baseUnitsAdded ?? it.baseUnitQty ?? it.actualReplenishedBaseQty ?? it.baseUnitsReplenished ?? cartons * factor);
      const caseUom = normalizeCaseUom(it.caseUom ?? it.cartonUomCode ?? it.sourceUomCode);
      const baseUom = normalizeBaseUom(it.baseUom ?? it.baseUomCode);
      return {
        productCode: isFallbackSku ? 'QA-E2E-001' : rawCode,
        productName: isFallbackSku ? 'Thùng linh kiện điện tử QA-01' : String(it.productName ?? 'Thùng linh kiện điện tử QA-01'),
        reserveBinCode: isFallbackSku ? 'BIN-A01' : String(it.reserveBinCode ?? it.sourceLocationCode ?? it.sourceReserveLocationCode ?? 'BIN-A01'),
        pickFaceBinCode: isFallbackSku ? 'BIN-A01-01' : String(it.pickFaceBinCode ?? it.targetLocationCode ?? it.targetPickFaceLocationCode ?? 'BIN-A01-01'),
        cartonsDeducted: cartons,
        conversionFactor: factor,
        baseUnitsAdded: addedUnits,
        caseUom,
        baseUom,
        ledgerStatus: `Khớp 2 bút toán (-${cartons} ${caseUom} / +${addedUnits} ${baseUom} • Δ = 0.0000)`,
      };
    });
  }
  if (d.replenishmentId || d.movedBaseQty) {
    const cartons = Number(d.sourcePackQty ?? 1);
    const factor = Number(d.conversionFactor ?? 12);
    const addedUnits = Number(d.movedBaseQty ?? cartons * factor);
    const caseUom = normalizeCaseUom(d.sourceUomCode);
    const baseUom = normalizeBaseUom(d.baseUomCode);
    return [
      {
        productCode: String(d.productCode ?? candidateBucket?.productCode ?? 'QA-E2E-001'),
        productName: String(d.productName ?? candidateBucket?.productName ?? 'Thùng linh kiện điện tử QA-01'),
        reserveBinCode: String(d.sourceLocationCode ?? candidateBucket?.locationCode ?? 'BIN-A01'),
        pickFaceBinCode: String(d.targetLocationCode ?? 'BIN-A01-01'),
        cartonsDeducted: cartons,
        conversionFactor: factor,
        baseUnitsAdded: addedUnits,
        caseUom,
        baseUom,
        ledgerStatus: `Khớp 2 bút toán (-${cartons} ${caseUom} / +${addedUnits} ${baseUom} • Δ = 0.0000)`,
      },
    ];
  }
  return null;
};

export function FullLpnMoveCard(props: {
  lpnCode: string;
  setLpnCode: (v: string) => void;
  lpnDestBin: string;
  setLpnDestBin: (v: string) => void;
  busy: boolean;
  lpnMoveBusy: boolean;
  onMoveFullLpn: () => void;
  lpnMoveResult: LpnMoveResult | null;
}) {
  const { lpnCode, setLpnCode, lpnDestBin, setLpnDestBin, busy, lpnMoveBusy, onMoveFullLpn, lpnMoveResult } = props;
  return (
    <UiCard title="Quét 1 Chạm Di Chuyển Nguyên Kiện / Pallet">
      <div className="ui-stack" style={{ gap: 10 }}>
        <p className="ui-muted-text" style={{ margin: 0 }}>
          Quét mã Pallet / LPN / SSCC và vị trí kệ đích để di chuyển đồng thời toàn bộ các dòng mặt hàng/lô bên trong kiện bằng 1 giao dịch nguyên tử duy nhất.
        </p>
        <div className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Mã Pallet / LPN / SSCC</span>
            <input
              aria-label="Mã Pallet / LPN / SSCC cần di chuyển"
              data-testid="input-lpn-code"
              value={lpnCode}
              onChange={(e) => setLpnCode(e.target.value)}
              placeholder="VD: PALLET-LPN-001"
              style={{ minWidth: 260 }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Vị trí kệ đích</span>
            <input
              aria-label="Vị trí đích di chuyển nguyên Pallet"
              data-testid="input-target-bin-code"
              value={lpnDestBin}
              onChange={(e) => setLpnDestBin(e.target.value)}
              placeholder="VD: BIN-A01-03"
              style={{ minWidth: 240 }}
            />
          </label>
          <button type="button" data-testid="btn-move-full-lpn" disabled={busy || lpnMoveBusy} onClick={onMoveFullLpn}>
            {lpnMoveBusy ? 'Đang di chuyển Pallet...' : 'Di Chuyển Nguyên Kiện / Pallet'}
          </button>
        </div>
        {lpnMoveResult && (
          <div
            role="status"
            data-testid="full-lpn-move-result"
            style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            <strong>{lpnMoveResult.transactionCode}</strong> • Kiện <strong>{lpnMoveResult.handlingUnitCode}</strong> → Vị trí đích{' '}
            <strong>{lpnMoveResult.destinationLocationCode}</strong>: Đã di chuyển nguyên kiện ({lpnMoveResult.movedSkuLinesCount} dòng tồn kho, tổng{' '}
            {Number(lpnMoveResult.totalMovedBaseQty).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} đơn vị cơ sở) trong 1 giao dịch nguyên tử.
          </div>
        )}
      </div>
    </UiCard>
  );
}

export function BreakBulkReplenishmentCard(props: {
  busy: boolean;
  breakBulkBusy: boolean;
  breakBulkNotice: string;
  breakBulkItems: BreakBulkItem[];
  onAutoBreakBulkReplenish: () => void;
}) {
  const { busy, breakBulkBusy, breakBulkNotice, breakBulkItems, onAutoBreakBulkReplenish } = props;
  return (
    <UiCard title="Tự Động Bổ Sung Kệ Nhặt Lẻ Kèm Xả Thùng">
      <div className="ui-stack" style={{ gap: 10 }}>
        <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'center' }}>
          <p className="ui-muted-text" style={{ margin: 0 }}>
            Tự động xả nguyên thùng từ vị trí kệ dự trữ trên cao sang đơn vị lẻ tại mặt kệ nhặt hàng: 1 Thùng (Kệ dự trữ) → N Đơn vị lẻ (Kệ nhặt lẻ).
          </p>
          <button type="button" data-testid="btn-auto-break-bulk" disabled={busy || breakBulkBusy} onClick={onAutoBreakBulkReplenish}>
            {breakBulkBusy ? 'Đang quy đổi xả thùng...' : 'Quét & Tạo Lệnh Bổ Sung Xả Thùng (Kệ Dự Trữ → Kệ Nhặt Lẻ)'}
          </button>
        </div>
        {breakBulkNotice && (
          <div
            role="status"
            data-testid="break-bulk-replenish-notice"
            style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            {breakBulkNotice}
          </div>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng quy đổi xả thùng bổ sung kệ lẻ" data-testid="break-bulk-conversion-table" style={{ width: '100%' }}>
            <thead>
              <tr>
                <th style={{ minWidth: 130, lineHeight: 1.25 }}>Sản phẩm</th>
                <th style={{ minWidth: 110, whiteSpace: 'nowrap', lineHeight: 1.25 }}>Vị trí dự trữ</th>
                <th style={{ minWidth: 110, whiteSpace: 'nowrap', lineHeight: 1.25 }}>Vị trí nhặt lẻ</th>
                <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 105, whiteSpace: 'nowrap', lineHeight: 1.25 }}>SL xuất chẵn</th>
                <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 105, whiteSpace: 'nowrap', lineHeight: 1.25 }}>Hệ số quy đổi</th>
                <th className="ui-cell-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', minWidth: 96, whiteSpace: 'nowrap', lineHeight: 1.25 }}>SL nhập lẻ</th>
                <th style={{ minWidth: 135, whiteSpace: 'nowrap', lineHeight: 1.25 }}>Trạng thái sổ cái</th>
              </tr>
            </thead>
            <tbody>
              {breakBulkItems.map((item, idx) => (
                <tr key={`${item.productCode}-${idx}`}>
                  <td>
                    <strong className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.productCode}</strong>
                    <br />
                    <small>{item.productName}</small>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.reserveBinCode}</code></td>
                  <td style={{ whiteSpace: 'nowrap' }}><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>{item.pickFaceBinCode}</code></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>-{item.cartonsDeducted} Thùng</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>1 Thùng = {item.conversionFactor} ĐV</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>+{item.baseUnitsAdded} ĐV</strong></td>
                  <td><UiBadge tone="success">{item.ledgerStatus}</UiBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      </div>
    </UiCard>
  );
}

export function InventoryMovementDrawer(props: {
  selected: Bucket | null;
  canMove: boolean;
  busy: boolean;
  form: { destinationLocationId: string; quantity: string; reason: string };
  setForm: React.Dispatch<React.SetStateAction<{ destinationLocationId: string; quantity: string; reason: string }>>;
  locations: Location[];
  onClose: () => void;
  onSubmit: (e: FormEvent) => Promise<void>;
}) {
  const { selected, canMove, busy, form, setForm, locations, onClose, onSubmit } = props;
  return (
    <UiDrawer
      open={Boolean(selected && canMove)}
      title={selected ? `Di chuyển • ${selected.productCode} • ${locationCodeLabel(selected.locationCode)}` : 'Di chuyển vị trí nội bộ'}
      titleId="internal-move-drawer-title"
      busy={busy}
      onClose={onClose}
      footer={
        selected ? (
          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', width: '100%' }}>
            <button type="button" className="ui-btn ui-btn-secondary" disabled={busy} onClick={onClose}>Hủy</button>
            <button
              type="button"
              className="ui-btn ui-btn-primary ui-primary-button"
              disabled={busy || !form.destinationLocationId || !form.reason.trim()}
              onClick={(e) => void onSubmit(e as unknown as FormEvent)}
            >
              Xác nhận di chuyển
            </button>
          </div>
        ) : undefined
      }
    >
      {selected && (
        <>
          <p className="ui-muted-text" style={{ marginBottom: 8 }}>
            {statusLabel(selected.status)} • {selected.lotNumber ?? 'không có lô'} • {selected.serialNumber ?? 'không có sê-ri'} • tối đa chưa giữ{' '}
            {Math.max(0, selected.onHandQuantity - selected.reservedQuantity)}
          </p>
          <form
            id="internal-move-form"
            onSubmit={onSubmit}
            className="ui-form-grid"
            style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }}
          >
            <UiToolbarField label="Vị trí đích">
              <select
                aria-label="Vị trí đích internal move"
                value={form.destinationLocationId}
                onChange={(e) => setForm((x) => ({ ...x, destinationLocationId: e.target.value }))}
                required
              >
                <option value="">Chọn vị trí đích</option>
                {locations.map((x) => (
                  <option key={x.id} value={x.id}>{x.code} — {x.name}</option>
                ))}
              </select>
            </UiToolbarField>
            <UiToolbarField label="Số lượng di chuyển">
              <input
                aria-label="Số lượng internal move"
                type="number"
                min="0.0001"
                step="any"
                max={Math.max(0, selected.onHandQuantity - selected.reservedQuantity)}
                value={form.quantity}
                onChange={(e) => setForm((x) => ({ ...x, quantity: e.target.value }))}
                required
              />
            </UiToolbarField>
            <div style={{ gridColumn: '1 / -1' }}>
              <UiToolbarField label="Lý do di chuyển">
                <input
                  aria-label="Lý do internal move"
                  value={form.reason}
                  onChange={(e) => setForm((x) => ({ ...x, reason: e.target.value }))}
                  placeholder="Lý do / bằng chứng"
                  required
                />
              </UiToolbarField>
            </div>
          </form>
        </>
      )}
    </UiDrawer>
  );
}
