import { useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { idempotencyHeaders } from '../../services/idempotency';
import { UiCard } from '../../ui/ProductionUi';
import type { Shipment, TmsManifestResult } from './shipmentsTypes';
import { messageFor } from './shipmentsTypes';
import { ShipmentGhnLiveIntegrationCard } from './ShipmentGhnLiveIntegrationCard';
import { ShipmentsListTableCard } from './ShipmentsListTableCard';

interface ShipmentsOverviewAndTmsCardsProps {
  rows: Shipment[];
  detail: (id: number) => Promise<void>;
  setError: (msg: string) => void;
}

export function ShipmentsOverviewAndTmsCards({
  rows,
  detail,
  setError,
}: ShipmentsOverviewAndTmsCardsProps) {
  const [tmsManifestResult, setTmsManifestResult] = useState<TmsManifestResult | null>(null);
  const [tmsBusy, setTmsBusy] = useState(false);
  const [exportReceiptOptions, setExportReceiptOptions] = useState<Array<{ id: number; code: string; warehouseName?: string }>>([]);
  const [carrierPartnerOptions, setCarrierPartnerOptions] = useState<Array<{ id: number; code: string; name: string }>>([]);
  const [handoverManifestOptions, setHandoverManifestOptions] = useState<Array<{ id: number; code: string; carrierCode?: string }>>([]);
  const [dispatchExportReceiptId, setDispatchExportReceiptId] = useState('');
  const [dispatchCarrierCode, setDispatchCarrierCode] = useState('VTP-EXPRESS');
  const [dispatchManifestCode, setDispatchManifestCode] = useState('');

  useEffect(() => {
    void Promise.allSettled([
      apiClient.get('/api/v1/export-receipts'),
      apiClient.get('/api/v1/business-partners'),
      apiClient.get('/api/v1/handover-manifests'),
    ]).then(([expRes, partRes, manRes]) => {
      if (expRes.status === 'fulfilled') {
        const raw = expRes.value?.data;
        const arr = Array.isArray(raw) ? raw : Array.isArray(raw?.items) ? raw.items : [];
        setExportReceiptOptions(
          arr
            .filter((x: Record<string, unknown>) => typeof x?.id === 'number' && typeof x?.code === 'string')
            .map((x: Record<string, unknown>) => ({
              id: Number(x.id),
              code: String(x.code),
              warehouseName: typeof x.warehouseName === 'string' ? x.warehouseName : undefined,
            })),
        );
      }
      if (partRes.status === 'fulfilled') {
        const raw = partRes.value?.data;
        const arr = Array.isArray(raw) ? raw : Array.isArray(raw?.items) ? raw.items : [];
        setCarrierPartnerOptions(
          arr
            .filter((x: Record<string, unknown>) => typeof x?.id === 'number' && typeof x?.code === 'string')
            .map((x: Record<string, unknown>) => ({
              id: Number(x.id),
              code: String(x.code),
              name: typeof x.name === 'string' ? x.name : String(x.code),
            })),
        );
      }
      if (manRes.status === 'fulfilled') {
        const raw = manRes.value?.data;
        const arr = Array.isArray(raw) ? raw : Array.isArray(raw?.items) ? raw.items : [];
        setHandoverManifestOptions(
          arr
            .filter((x: Record<string, unknown>) => typeof x?.id === 'number' && (typeof x?.code === 'string' || typeof x?.manifestCode === 'string'))
            .map((x: Record<string, unknown>) => ({
              id: Number(x.id),
              code: String(x.code ?? x.manifestCode),
              carrierCode: typeof x.carrierCode === 'string' ? x.carrierCode : undefined,
            })),
        );
      }
    });
  }, []);

  const preDispatchCount = rows.filter(x => ['Ready', 'Staging', 'Loading', 'Loaded'].includes(x.status)).length;
  const inTransitCount = rows.filter(x => ['Dispatched', 'InTransit'].includes(x.status)).length;
  const deliveredOrCompletedCount = rows.filter(x => ['Delivered', 'Completed'].includes(x.status)).length;
  const exceptionOrReturnCount = rows.filter(x => ['DeliveryFailed', 'ReturnToWarehouse'].includes(x.status)).length;
  const stagingOrLoadingCount = preDispatchCount;
  const carrierAssignedCount = rows.filter(x => Boolean(x.carrierName || x.vehiclePlate || x.dockAppointmentId || x.dockAppointmentCode)).length;
  const sealVerifiedCount = rows.filter(x => Boolean(x.sealNumber)).length;

  return (
    <>
      <UiCard title="Giám sát Chu trình Xuất giao & Cam kết Giao hàng">
        <div className="ui-inline-wrap">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, width: '100%' }}>
            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Chờ xuất bến</span>
              <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{preDispatchCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đang vận chuyển</span>
              <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{inTransitCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đã giao hoàn tất</span>
              <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{deliveredOrCompletedCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Ngoại lệ / Hoàn kho</span>
              <strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{exceptionOrReturnCount}</strong>
            </div>
          </div>
        </div>
      </UiCard>

      <UiCard title="Lập kế hoạch Xếp tải Xe & Tích hợp Đơn vị Vận chuyển / TMS">
        <div className="ui-inline-wrap">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, width: '100%' }}>
            <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Chờ tập kết / xếp xe</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{stagingOrLoadingCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Hãng vận chuyển</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{carrierAssignedCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đã niêm phong chì</span>
              <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{sealVerifiedCount}</strong>
            </div>
            <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
              <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đồng bộ sự kiện vận tải / biên bản giao</span>
              <strong style={{ fontSize: 12.5, fontWeight: 700, color: '#0f172a', lineHeight: 1.35 }}>Đồng bộ sự kiện &amp; biên bản giao hàng chống trùng lặp</strong>
            </div>
          </div>
        </div>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 12,
            marginTop: 12,
            marginBottom: 12,
          }}
        >
          <label className="ui-stack">
            <span>Phiếu xuất kho điều phối</span>
            <select
              aria-label="Phiếu xuất kho điều phối"
              value={dispatchExportReceiptId}
              onChange={e => setDispatchExportReceiptId(e.target.value)}
            >
              <option value="">-- Chọn phiếu xuất kho --</option>
              {Array.from(
                new Map(
                  (exportReceiptOptions.length > 0
                    ? exportReceiptOptions
                    : rows.map(r => ({
                        id: r.exportReceiptId ?? r.sourceId ?? r.id,
                        code: r.exportReceiptCode ?? r.sourceCode ?? r.shipmentCode,
                        warehouseName: r.warehouseName,
                      }))
                  ).map(opt => [`${opt.id}-${opt.code}`, opt]),
                ).values(),
              ).map((opt, idx) => (
                <option key={`${opt.id}-${opt.code}-${idx}`} value={String(opt.id)}>
                  {opt.code}{opt.warehouseName ? ` — ${opt.warehouseName}` : ''}
                </option>
              ))}
            </select>
          </label>
          <label className="ui-stack">
            <span>Đối tác vận chuyển / Hãng vận chuyển</span>
            <select
              aria-label="Đối tác vận chuyển / Hãng vận chuyển"
              value={dispatchCarrierCode}
              onChange={e => setDispatchCarrierCode(e.target.value)}
            >
              <option value="VTP-EXPRESS">VTP-EXPRESS — Viettel Post</option>
              <option value="GHN">GHN — Giao Hàng Nhanh</option>
              <option value="GHTK">GHTK — Giao Hàng Tiết Kiệm</option>
              <option value="JT-EXPRESS">JT-EXPRESS — J&amp;T Express</option>
              {carrierPartnerOptions.map(partner => (
                <option key={partner.id} value={partner.code}>
                  {partner.code} — {partner.name}
                </option>
              ))}
            </select>
          </label>
          <label className="ui-stack">
            <span>Bảng kê bàn giao (Handover Manifest)</span>
            <select
              aria-label="Bảng kê bàn giao"
              value={dispatchManifestCode}
              onChange={e => setDispatchManifestCode(e.target.value)}
            >
              <option value="">-- Chọn bảng kê bàn giao --</option>
              {Array.from(
                new Map(
                  (handoverManifestOptions.length > 0
                    ? handoverManifestOptions
                    : rows.map(r => ({
                        id: r.id,
                        code: `MAN-${r.packingSessionCode || r.shipmentCode}`,
                        carrierCode: r.carrierName ?? dispatchCarrierCode,
                      }))
                  ).map(man => [`${man.id}-${man.code}`, man]),
                ).values(),
              ).map((man, idx) => (
                <option key={`${man.id}-${man.code}-${idx}`} value={man.code}>
                  {man.code}{man.carrierCode ? ` (${man.carrierCode})` : ''}
                </option>
              ))}
            </select>
          </label>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 12, marginBottom: 14, paddingBottom: 6 }}>
          <button
            type="button"
            disabled={tmsBusy}
            onClick={() => {
              if (tmsBusy) return;
              setTmsBusy(true);
              setError('');
              void apiClient
                .post<Record<string, unknown>>(
                  '/api/v1/shipments/sync-tms-manifest',
                  {
                    warehouseId: rows[0]?.warehouseId ?? undefined,
                    carrierCode: dispatchCarrierCode || (rows[0]?.carrierName ?? 'VTP-EXPRESS'),
                    exportReceiptId: dispatchExportReceiptId ? Number(dispatchExportReceiptId) : undefined,
                    manifestCode: dispatchManifestCode || undefined,
                  },
                  { headers: idempotencyHeaders('shipment-sync-tms:' + Date.now()) },
                )
                .then(res => {
                  const data = res?.data ?? {};
                  const manifestCode = typeof data.manifestCode === 'string' && data.manifestCode ? data.manifestCode : (typeof data.manifestId === 'string' ? data.manifestId : '');
                  if (manifestCode) {
                    const carrier = typeof data.carrierIntegration === 'string' && data.carrierIntegration ? data.carrierIntegration : (typeof data.carrierCode === 'string' && data.carrierCode ? data.carrierCode : 'VTP-EXPRESS');
                    setTmsManifestResult({
                      manifestId: manifestCode,
                      carrierCode: carrier,
                      syncedShipmentCount: typeof data.syncedShipmentCount === 'number' ? data.syncedShipmentCount : 0,
                      totalHandlingUnitsCount: typeof data.totalHandlingUnitsCount === 'number' ? data.totalHandlingUnitsCount : 0,
                      totalGrossWeightKg: typeof data.totalGrossWeightKg === 'number' ? data.totalGrossWeightKg : 0,
                      totalVolumeM3: typeof data.totalVolumeM3 === 'number' ? data.totalVolumeM3 : 0,
                      ediStatus: typeof data.ediStatus === 'string' && data.ediStatus ? data.ediStatus : 'SYNCED',
                      statusLabel: typeof data.statusLabel === 'string' && data.statusLabel ? data.statusLabel : undefined,
                    });
                  } else {
                    setTmsManifestResult(null);
                    setError('Không thể đồng bộ Bảng kê Vận tải TMS: dữ liệu phản hồi không hợp lệ.');
                  }
                })
                .catch(e => {
                  setTmsManifestResult(null);
                  setError(messageFor(e));
                })
                .finally(() => setTmsBusy(false));
            }}
          >
            Đồng bộ Bảng kê Vận tải &amp; Ghép tải TMS
          </button>
        </div>
        {tmsManifestResult && (
          <div
            role="status"
            style={{
              marginTop: 8,
              padding: '8px 12px',
              borderRadius: 8,
              background: '#ecfdf5',
              color: '#065f46',
              border: '1px solid #a7f3d0',
              fontSize: 13,
              lineHeight: 1.5,
              whiteSpace: 'normal',
              wordBreak: 'break-word',
            }}
          >
            {`Bảng kê vận tải: ${tmsManifestResult.manifestId} • Đơn vị vận chuyển: ${tmsManifestResult.carrierCode} • Số chuyến đồng bộ: ${tmsManifestResult.syncedShipmentCount} (${tmsManifestResult.totalHandlingUnitsCount ?? 0} kiện HU • ${tmsManifestResult.totalGrossWeightKg ?? 0} kg • ${tmsManifestResult.totalVolumeM3 ?? 0} m³) • Trạng thái: ${tmsManifestResult.statusLabel || (tmsManifestResult.ediStatus === 'ACK_200_SYNCED' || tmsManifestResult.ediStatus === 'SYNCED' ? 'Đã đồng bộ bảng kê vận tải & ghép tải thành công' : tmsManifestResult.ediStatus)}`}
          </div>
        )}
      </UiCard>

      <ShipmentGhnLiveIntegrationCard rows={rows} setError={setError} />

      <ShipmentsListTableCard rows={rows} detail={detail} />
    </>
  );
}
