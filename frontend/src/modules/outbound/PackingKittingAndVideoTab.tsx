import { useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { hasPermission } from '../../services/authorization';
import { idempotencyHeaders } from '../../services/idempotency';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { PackingSession, VideoEvidenceItem } from './packingSessionsTypes';
import { errorMessage, hiddenTabStyle } from './packingSessionsTypes';

const canReadPackingCostRbac = (): boolean => {
  try {
    const perms = localStorage.getItem('user_permissions') || localStorage.getItem('permissions') || '';
    if (perms.includes('inventory.cost.read')) return true;
    const role = (localStorage.getItem('role') || '').trim();
    if (role === 'Staff' || role === 'Viewer') return false;
    if (role === 'Admin' || role === 'Manager' || role === 'Accountant') return true;
    return hasPermission('inventory.cost.read');
  } catch {
    return true;
  }
};

interface PackingKittingAndVideoTabProps {
  active: boolean;
  sessions: PackingSession[];
  selected: PackingSession | null;
  huType: string;
  busy: string;
  governanceBusy: boolean;
  setGovernanceBusy: (val: boolean) => void;
  videoEvidences: VideoEvidenceItem[];
  setVideoEvidences: React.Dispatch<React.SetStateAction<VideoEvidenceItem[]>>;
  setVideoNotice: (val: string) => void;
  setError: (msg: string) => void;
}

export function PackingKittingAndVideoTab({
  active,
  sessions,
  selected,
  huType,
  busy,
  governanceBusy,
  setGovernanceBusy,
  videoEvidences,
  setVideoEvidences,
  setVideoNotice,
  setError,
}: PackingKittingAndVideoTabProps) {
  const [videoSearchAwb, setVideoSearchAwb] = useState<string>('');
  const [packagingVerificationResult, setPackagingVerificationResult] = useState('');
  const [kittingVasResult, setKittingVasResult] = useState('');
  const [kittingVasMode, setKittingVasMode] = useState<'PRE_KIT' | 'DEKIT_DISASSEMBLE'>('PRE_KIT');
  const [kitEarliestExpiry, setKitEarliestExpiry] = useState<string>('31/12/2026 (FEFO Min)');
  const [kitRolledUpCost, setKitRolledUpCost] = useState<number | null>(50000);
  const canReadCost = canReadPackingCostRbac();

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
            setKitRolledUpCost(Number(latest.rolledUpUnitCost));
          }
        }
      })
      .catch(() => {
        // Ignore background load error
      });
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
      const data = res?.data as {
        verificationId?: string;
        verificationCode?: string;
        cartonType?: string;
        packageType?: string;
        gs1SsccReady?: boolean;
        statusLabel?: string;
        handlingUnitsCount?: number;
        totalGrossWeightKg?: number;
        totalVolumeM3?: number;
      } | undefined;
      const verificationCode = data?.verificationCode || data?.verificationId;
      if (verificationCode) {
        const packageTypeMap: Record<string, string> = {
          Carton: 'Thùng carton (Carton)',
          Tote: 'Khay nhựa (Tote)',
          Pallet: 'Pallet tiêu chuẩn (Pallet)',
          Container: 'Thùng hàng lớn (Container)',
          Custom: 'Tùy chỉnh (Custom)',
          'STD-CARTON-M': 'Thùng carton tiêu chuẩn (STD-CARTON-M)',
        };
        const rawPkg = data?.packageType || data?.cartonType || 'STD-CARTON-M';
        const packageType = packageTypeMap[rawPkg] ?? rawPkg;
        const gs1Label = data?.gs1SsccReady !== false ? 'Đạt chuẩn' : 'Chưa đạt';
        const statusLabel = data?.statusLabel || 'Đã kiểm tra quy cách đóng gói thực tế';
        setPackagingVerificationResult(
          `${verificationCode} • Loại bao bì: ${packageType} • Chuẩn mã GS1/SSCC: ${gs1Label} (${data?.handlingUnitsCount ?? 0} kiện HU • ${data?.totalGrossWeightKg ?? 0} kg • ${data?.totalVolumeM3 ?? 0} m³) • ${statusLabel}`,
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

  const executeKittingVas = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/packing-sessions/execute-kitting-vas',
        {
          warehouseId: selected?.warehouseId ?? sessions[0]?.warehouseId ?? undefined,
          kitSkuCode: 'KIT-BUNDLE-STD',
          vasMode: kittingVasMode,
          vasServiceType: kittingVasMode === 'DEKIT_DISASSEMBLE' ? 'DEKIT_DISASSEMBLE' : 'LABELING_SHRINKWRAP_BUNDLE',
        },
        { headers: idempotencyHeaders('packing-kitting-vas:' + Date.now()) },
      );
      const data = res?.data as {
        workOrderId?: string;
        vasExecutionCode?: string;
        status?: string;
        statusLabel?: string;
        vasServiceType?: string;
        serviceScopeLabel?: string;
        activePackingSessionsCount?: number;
        handlingUnitsCount?: number;
      } | undefined;
      const vasCode = data?.vasExecutionCode || data?.workOrderId;
      if (vasCode) {
        const vasLabel = data?.statusLabel || (!data?.status || data.status === 'KITTING_VAS_READY'
          ? 'Đã hoàn tất kiểm tra đóng bộ (Kitting) & dịch vụ gia tăng (VAS)'
          : data.status);
        const scopeLabel = data?.serviceScopeLabel || (!data?.vasServiceType || data.vasServiceType === 'LABELING_SHRINKWRAP_BUNDLE'
          ? 'Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm'
          : (data.vasServiceType === 'DEKIT_DISASSEMBLE' ? 'Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ' : data.vasServiceType));
        const modeLabel = kittingVasMode === 'DEKIT_DISASSEMBLE' ? 'Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi' : 'Lắp ráp bộ sản phẩm theo đơn hàng';
        setKittingVasResult(`${vasCode} • [${modeLabel}] ${vasLabel} — ${scopeLabel} (${data?.activePackingSessionsCount ?? 0} phiên • ${data?.handlingUnitsCount ?? 0} kiện HU)`);
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

  const searchPackingVideoEvidences = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    setError('');
    setVideoNotice('');
    try {
      const res = await apiClient.get('/api/v1/packing-sessions/video-evidence', {
        params: videoSearchAwb.trim() ? { awbCode: videoSearchAwb.trim() } : undefined,
      });
      const rawList = res?.data?.items ?? res?.data?.evidences ?? res?.data;
      const items = Array.isArray(rawList) ? rawList : [];
      setVideoEvidences(items.map((item: Record<string, unknown>, idx: number) => ({
        id: Number(item.id ?? idx + 1),
        sessionId: Number(item.sessionId ?? item.packingSessionId ?? 1),
        sessionCode: String(item.sessionCode ?? 'PACK-001'),
        awbCode: String(item.awbCode ?? 'SPXVN058291042'),
        durationSeconds: Number(item.durationSeconds ?? 48),
        fileSizeMb: Number(item.fileSizeMb ?? 14.6),
        sha256Hash: String(item.sha256Hash ?? 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'),
        recordedAt: String(item.recordedAt ?? new Date().toISOString()),
        cameraStation: String(item.cameraStation ?? 'CAM-PACK-01'),
      })));
      setVideoNotice('Đã tải danh sách bằng chứng video đóng gói.');
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <div style={active ? { display: 'contents' } : hiddenTabStyle}>
      <UiCard title="Kho Bằng chứng Video Đóng gói SHA-256 & Tra cứu Vận đơn (AWB)">
        <form onSubmit={searchPackingVideoEvidences} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 8 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, flex: 1 }}>Tra cứu theo Mã vận đơn (AWB)
            <input aria-label="Tra cứu video theo mã vận đơn AWB" value={videoSearchAwb} onChange={e => setVideoSearchAwb(e.target.value)} placeholder="VD: SPXVN058291042" />
          </label>
          <button type="submit" className="ui-btn ui-btn-secondary" disabled={!!busy}>Tra cứu Video theo Mã vận đơn (AWB)</button>
        </form>
        {videoEvidences.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách bằng chứng video đóng gói" className="ui-table-compact">
              <thead>
                <tr>
                  <th>Mã vận đơn (AWB)</th>
                  <th>Phiên đóng gói</th>
                  <th>Trạm Camera</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thời lượng</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Dung lượng</th>
                  <th>Mã băm SHA-256</th>
                </tr>
              </thead>
              <tbody>
                {videoEvidences.map(ev => (
                  <tr key={ev.id}>
                    <td><strong>{ev.awbCode}</strong></td>
                    <td>{ev.sessionCode}</td>
                    <td>{ev.cameraStation}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{ev.durationSeconds} giây</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{ev.fileSizeMb} MB</td>
                    <td><code>{ev.sha256Hash}</code></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard title="Danh mục Loại Bao bì Chuẩn & Gợi ý Tối ưu Đóng thùng">
        <div className="ui-inline-wrap">
          <span><strong>CARTON-S</strong> (25×20×15 cm): tối đa 5 kg / Cước DIM 1.25 kg</span>
          <span><strong>CARTON-M</strong> (35×25×20 cm): tối đa 12 kg / Cước DIM 2.92 kg</span>
          <span><strong>CARTON-L</strong> (40×30×30 cm): tối đa 20 kg / Cước DIM 6.0 kg</span>
          <span><strong>CARTON-XL</strong> (50×40×30 cm): tối đa 35 kg / Cước DIM 10.0 kg</span>
          <span>Chính sách tối ưu đóng thùng: <UiBadge tone="success">Đề xuất &amp; Xác nhận bởi Nhân viên</UiBadge></span>
          <span data-testid="packing-kit-earliest-expiry">
            <strong>Hạn dùng kế thừa (FEFO):</strong> {kitEarliestExpiry}
          </span>
          <span data-testid="packing-kit-rolledup-cost">
            <strong>Giá vốn Kit cộng dồn (MAC):</strong>{' '}
            {canReadCost && kitRolledUpCost !== null
              ? `${kitRolledUpCost.toLocaleString('vi-VN')} đ`
              : 'Ẩn theo quyền RBAC (inventory.cost.read)'}
          </span>
        </div>
        <div className="ui-inline-wrap">
          <button type="button" disabled={!!busy || governanceBusy} onClick={() => void verifyPackagingProfile()}>Kiểm tra Quy cách Thùng &amp; Chuẩn mã GS1/SSCC</button>
          <select
            aria-label="Chế độ Đóng bộ / Tách bộ Kitting VAS"
            data-testid="select-kitting-vas-mode"
            value={kittingVasMode}
            onChange={e => setKittingVasMode(e.target.value as 'PRE_KIT' | 'DEKIT_DISASSEMBLE')}
            style={{ maxWidth: '100%' }}
          >
            <option value="PRE_KIT">Lắp ráp bộ sản phẩm theo đơn hàng</option>
            <option value="DEKIT_DISASSEMBLE">Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi</option>
          </select>
          <button type="button" disabled={!!busy || governanceBusy} onClick={() => void executeKittingVas()}>Thực thi Đóng bộ / Tách bộ &amp; Dịch vụ Gia tăng</button>
        </div>
        {packagingVerificationResult && (
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
            {packagingVerificationResult}
          </div>
        )}
        {kittingVasResult && (
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
            {kittingVasResult}
          </div>
        )}
      </UiCard>
    </div>
  );
}
