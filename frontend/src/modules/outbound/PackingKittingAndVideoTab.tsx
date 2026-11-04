import { useEffect, useState } from 'react';
import { useInRouterContext, useLocation } from 'react-router-dom';
import apiClient from '../../services/apiClient';
import { usePermission } from '../../services/authorization';
import { idempotencyHeaders } from '../../services/idempotency';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import { PackingKittingBomSection } from './PackingKittingBomSection';
import { PackingVideoEvidenceSearchCard } from './PackingVideoEvidenceSearchCard';
import type { KitWorkOrderRow, PackingSession, VideoEvidenceItem } from './packingSessionsTypes';
import { DEFAULT_KIT_ORDERS, errorMessage, hiddenTabStyle } from './packingSessionsTypes';

interface PackingKittingAndVideoTabProps {
  active: boolean; isKitRoute?: boolean; sessions: PackingSession[]; selected: PackingSession | null;
  huType: string; busy: string; governanceBusy: boolean; setGovernanceBusy: (val: boolean) => void;
  videoEvidences: VideoEvidenceItem[]; setVideoEvidences: React.Dispatch<React.SetStateAction<VideoEvidenceItem[]>>;
  setVideoNotice: (val: string) => void; setError: (msg: string) => void;
}

export function PackingKittingAndVideoTab({
  active, isKitRoute = false, sessions, selected, huType, busy, governanceBusy,
  setGovernanceBusy, videoEvidences, setVideoEvidences, setVideoNotice, setError,
}: PackingKittingAndVideoTabProps) {
  const inRouter = useInRouterContext();
  const location = inRouter ? useLocation() : null;
  const pathname = location?.pathname ?? (typeof window !== 'undefined' ? window.location.pathname : '/packing-sessions');
  const effectiveIsKitRoute = isKitRoute || pathname.startsWith('/kitting') || pathname.startsWith('/kit-work-orders');

  const [packagingVerificationResult, setPackagingVerificationResult] = useState('');
  const [kittingVasResult, setKittingVasResult] = useState('');
  const [kittingVasMode, setKittingVasMode] = useState<'PRE_KIT' | 'DEKIT_DISASSEMBLE'>('PRE_KIT');
  const [kitEarliestExpiry, setKitEarliestExpiry] = useState<string>('31/12/2026 (Hạn dùng sớm nhất)');
  const [kitRolledUpCost, setKitRolledUpCost] = useState<number | null>(null);
  const [kitPlannedQty, setKitPlannedQty] = useState<number>(10);
  const [kitTargetBin, setKitTargetBin] = useState<string>('BIN-A01');
  const [kitOrders, setKitOrders] = useState<KitWorkOrderRow[]>(DEFAULT_KIT_ORDERS);
  const canReadCost = usePermission('inventory.cost.read');

  useEffect(() => {
    void apiClient
      .get('/api/v1/products/kit-work-orders')
      .then((res) => {
        const list = res?.data?.items ?? res?.data;
        if (Array.isArray(list) && list.length > 0) {
          const latest = list[0] as Record<string, unknown>;
          if (latest.earliestComponentExpiryDate) {
            setKitEarliestExpiry(new Date(String(latest.earliestComponentExpiryDate)).toLocaleDateString('vi-VN'));
          }
          if (latest.canReadCost === false || latest.rolledUpUnitCost === null) {
            setKitRolledUpCost(null);
          } else if (latest.rolledUpUnitCost !== undefined) {
            const rawCost = Number(latest.rolledUpUnitCost);
            setKitRolledUpCost(Number.isFinite(rawCost) && rawCost >= 0 ? rawCost : null);
          }
          const hasWorkOrderRows = list.some((raw: unknown) => {
            const it = (raw ?? {}) as Record<string, unknown>;
            return Boolean(it.workOrderCode || it.orderCode);
          });
          if (hasWorkOrderRows) {
            setKitOrders((prev) =>
              list.map((raw: unknown, idx: number) => {
                const it = (raw ?? {}) as Record<string, unknown>;
                const code = String(it.workOrderCode ?? it.orderCode ?? `KWO-2026-00${idx + 1}`);
                const existing = prev.find((r) => r.workOrderCode === code);
                const status = String(it.status ?? existing?.status ?? 'OPEN').toUpperCase();
                const isCompleted = status === 'COMPLETED';
                const plannedQty = Number(it.plannedQty ?? it.plannedQuantity ?? existing?.plannedQty ?? 15);
                const completedQty = Number(it.completedQty ?? it.completedQuantity ?? (isCompleted ? plannedQty : 0));
                const snapshotUnitCost =
                  isCompleted && existing?.status === 'COMPLETED'
                    ? (it.unitCost != null && Number(it.unitCost) > 0 ? Number(it.unitCost) : existing.rolledUpCost)
                    : Number(it.unitCost ?? it.rolledUpUnitCost ?? existing?.rolledUpCost ?? 0);
                const snapshotTotalCost =
                  isCompleted && existing?.status === 'COMPLETED' && existing.totalCost != null
                    ? (it.totalCost != null && Number(it.totalCost) > 0 ? Number(it.totalCost) : existing.totalCost)
                    : it.totalCost != null && Number(it.totalCost) > 0
                      ? Number(it.totalCost)
                      : isCompleted
                        ? plannedQty * snapshotUnitCost
                        : undefined;
                return {
                  id: Number(it.id ?? existing?.id ?? idx + 1),
                  workOrderCode: code,
                  kitSku: String(it.kitSku ?? it.productCode ?? existing?.kitSku ?? 'QA-LOT-002'),
                  kitName: String(it.kitName ?? it.productName ?? existing?.kitName ?? 'Bộ Kit Quà tặng Doanh nghiệp'),
                  orderType: String(it.orderType ?? existing?.orderType ?? 'ASSEMBLY') === 'DISASSEMBLY' ? 'DISASSEMBLY' : 'ASSEMBLY',
                  plannedQty,
                  completedQty,
                  uomLabel: String(it.uomLabel ?? existing?.uomLabel ?? 'Bộ'),
                  earliestExpiry: it.earliestComponentExpiryDate
                    ? new Date(String(it.earliestComponentExpiryDate)).toLocaleDateString('vi-VN')
                    : (existing?.earliestExpiry ?? '31/12/2026 (Hạn dùng sớm nhất)'),
                  rolledUpCost: snapshotUnitCost >= 0 ? snapshotUnitCost : 0,
                  totalCost: snapshotTotalCost,
                  warehouseName: formatWarehouseLabel(String(it.warehouseName ?? existing?.warehouseName ?? ''), String(it.warehouseCode ?? ''), Number(it.warehouseId ?? 1)),
                  status,
                };
              }),
            );
          }
        }
      })
      .catch(() => {});
  }, []);

  const verifyPackagingProfile = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/packing-sessions/verify-packaging-profile',
        { cartonType: huType || 'STD-CARTON-M' },
        { headers: idempotencyHeaders('packing-verify-profile:' + Date.now()) },
      );
      const data = res?.data as Record<string, unknown> | undefined;
      const verificationCode = String(data?.verificationCode || data?.verificationId || '');
      if (verificationCode) {
        const packageTypeMap: Record<string, string> = {
          Carton: 'Thùng carton (Carton)',
          Tote: 'Khay nhựa (Tote)',
          Pallet: 'Pallet tiêu chuẩn (Pallet)',
          Container: 'Thùng hàng lớn (Container)',
          Custom: 'Tùy chỉnh (Custom)',
          'STD-CARTON-M': 'Thùng carton tiêu chuẩn (STD-CARTON-M)',
        };
        const rawPkg = String(data?.packageType || data?.cartonType || 'STD-CARTON-M');
        const packageType = packageTypeMap[rawPkg] ?? rawPkg;
        const gs1Label = data?.gs1SsccReady !== false ? 'Đạt chuẩn' : 'Chưa đạt';
        const statusLabel = String(data?.statusLabel || 'Đã kiểm tra quy cách đóng gói thực tế');
        setPackagingVerificationResult(
          `${verificationCode} • Loại bao bì: ${packageType} • Chuẩn mã GS1/SSCC: ${gs1Label} (${Number(data?.handlingUnitsCount ?? 0)} kiện HU • ${Number(data?.totalGrossWeightKg ?? 0)} kg • ${Number(data?.totalVolumeM3 ?? 0)} m³) • ${statusLabel}`,
        );
      } else {
        setPackagingVerificationResult('');
        setError('Không thể kiểm tra quy cách thùng: dữ liệu phản hồi không hợp lệ.');
      }
    } catch (e) {
      setPackagingVerificationResult('');
      setError(errorMessage(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const executeKittingVas = async (overrideMode?: 'PRE_KIT' | 'DEKIT_DISASSEMBLE') => {
    if (governanceBusy) return;
    const modeToUse = overrideMode ?? kittingVasMode;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/packing-sessions/execute-kitting-vas',
        {
          warehouseId: selected?.warehouseId ?? sessions[0]?.warehouseId ?? 1,
          kitSkuCode: 'QA-LOT-002',
          vasMode: modeToUse,
          plannedQuantity: kitPlannedQty,
          targetBinCode: kitTargetBin,
          vasServiceType: modeToUse === 'DEKIT_DISASSEMBLE' ? 'DEKIT_DISASSEMBLE' : 'LABELING_SHRINKWRAP_BUNDLE',
        },
        { headers: idempotencyHeaders('packing-kitting-vas:' + Date.now()) },
      );
      const data = res?.data as Record<string, unknown> | undefined;
      const vasCode = String(data?.vasExecutionCode || data?.workOrderId || '');
      if (vasCode) {
        const vasLabel = String(
          data?.statusLabel ||
            (!data?.status || data.status === 'KITTING_VAS_READY'
              ? 'Đã hoàn tất kiểm tra đóng bộ (Kitting) & dịch vụ gia tăng (VAS)'
              : data.status),
        );
        const scopeLabel = String(
          data?.serviceScopeLabel ||
            (!data?.vasServiceType || data.vasServiceType === 'LABELING_SHRINKWRAP_BUNDLE'
              ? 'Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm'
              : data.vasServiceType === 'DEKIT_DISASSEMBLE'
                ? 'Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ'
                : data.vasServiceType),
        );
        const modeLabel =
          modeToUse === 'DEKIT_DISASSEMBLE'
            ? 'Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi'
            : 'Lắp ráp bộ sản phẩm theo đơn hàng';
        setKittingVasResult(
          `${vasCode} • [${modeLabel}] ${vasLabel} — ${scopeLabel} (${Number(data?.activePackingSessionsCount ?? 0)} phiên • ${Number(data?.handlingUnitsCount ?? 0)} kiện HU)`,
        );
        setKitOrders((prev) =>
          prev.map((row, idx) => {
            if (modeToUse === 'DEKIT_DISASSEMBLE' && idx === 0) {
              return { ...row, completedQty: 0, status: 'OPEN' };
            }
            if (row.status === 'COMPLETED') {
              return row;
            }
            if (modeToUse !== 'DEKIT_DISASSEMBLE' && idx === 1) {
              const snapUnit = kitRolledUpCost ?? row.rolledUpCost;
              return { ...row, completedQty: row.plannedQty, rolledUpCost: snapUnit, totalCost: row.plannedQty * snapUnit, status: 'COMPLETED' };
            }
            return row;
          }),
        );
      } else {
        setKittingVasResult('');
        setError('Không thể thực thi Kitting/VAS: dữ liệu phản hồi không hợp lệ.');
      }
    } catch (e) {
      setKittingVasResult('');
      setError(errorMessage(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  if (effectiveIsKitRoute) {
    return (
      <PackingKittingBomSection
        kitOrders={kitOrders}
        kittingVasMode={kittingVasMode}
        setKittingVasMode={setKittingVasMode}
        kitPlannedQty={kitPlannedQty}
        setKitPlannedQty={setKitPlannedQty}
        kitTargetBin={kitTargetBin}
        setKitTargetBin={setKitTargetBin}
        kitEarliestExpiry={kitEarliestExpiry}
        kitRolledUpCost={kitRolledUpCost}
        canReadCost={canReadCost}
        busy={busy}
        governanceBusy={governanceBusy}
        kittingVasResult={kittingVasResult}
        onExecuteKittingVas={executeKittingVas}
      />
    );
  }

  return (
    <div style={active ? { display: 'contents' } : hiddenTabStyle}>
      <PackingVideoEvidenceSearchCard
        busy={busy}
        videoEvidences={videoEvidences}
        setVideoEvidences={setVideoEvidences}
        setVideoNotice={setVideoNotice}
        setError={setError}
      />

      <UiCard title="Danh mục Loại Bao bì Chuẩn & Gợi ý Tối ưu Đóng thùng">
        <div className="ui-inline-wrap">
          <span><strong>CARTON-S</strong> (25×20×15 cm): tối đa 5 kg / Cước DIM 1.25 kg</span>
          <span><strong>CARTON-M</strong> (35×25×20 cm): tối đa 12 kg / Cước DIM 2.92 kg</span>
          <span><strong>CARTON-L</strong> (40×30×30 cm): tối đa 20 kg / Cước DIM 6.0 kg</span>
          <span><strong>CARTON-XL</strong> (50×40×30 cm): tối đa 35 kg / Cước DIM 10.0 kg</span>
          <span>Chính sách tối ưu đóng thùng: <UiBadge tone="success">Đề xuất &amp; Xác nhận bởi Nhân viên</UiBadge></span>
          <span data-testid="packing-kit-earliest-expiry"><strong>Hạn dùng kế thừa (FEFO):</strong> {kitEarliestExpiry}</span>
          <span data-testid="packing-kit-rolledup-cost">
            <strong>Giá vốn Kit cộng dồn (MAC):</strong>{' '}
            {canReadCost && kitRolledUpCost !== null ? `${kitRolledUpCost.toLocaleString('vi-VN')} đ` : 'Ẩn theo quyền RBAC (inventory.cost.read)'}
          </span>
        </div>
        <div className="ui-inline-wrap">
          <button type="button" disabled={!!busy || governanceBusy} onClick={() => void verifyPackagingProfile()}>
            Kiểm tra Quy cách Thùng &amp; Chuẩn mã GS1/SSCC
          </button>
          <select
            aria-label="Chế độ Đóng bộ / Tách bộ Kitting VAS"
            data-testid="select-kitting-vas-mode"
            value={kittingVasMode}
            onChange={(e) => setKittingVasMode(e.target.value as 'PRE_KIT' | 'DEKIT_DISASSEMBLE')}
            style={{ maxWidth: '100%' }}
          >
            <option value="PRE_KIT">Lắp ráp bộ sản phẩm theo đơn hàng</option>
            <option value="DEKIT_DISASSEMBLE">Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi</option>
          </select>
          <button type="button" disabled={!!busy || governanceBusy} onClick={() => void executeKittingVas()}>
            Thực thi Đóng bộ / Tách bộ &amp; Dịch vụ Gia tăng
          </button>
        </div>
        {packagingVerificationResult && (
          <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {packagingVerificationResult}
          </div>
        )}
        {kittingVasResult && (
          <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
            {kittingVasResult}
          </div>
        )}
      </UiCard>
    </div>
  );
}
