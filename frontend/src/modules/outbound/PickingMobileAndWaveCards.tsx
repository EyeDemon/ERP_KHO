import { useEffect, useState } from 'react';
import apiClient from '../../services/apiClient';
import { idempotencyHeaders } from '../../services/idempotency';
import { sanitizeScannerCode } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiEmptyState, UiTableScroll } from '../../ui/ProductionUi';
import { formatWarehouseLabel } from '../../utils/warehouseLabel';
import type { PickingTask, WaveGroup } from './pickingTasksTypes';
import { failureMessage, pickingTypeLabels, states, telemetryLabels } from './pickingTasksTypes';

interface PickingMobileStationCardProps {
  tasks: PickingTask[];
  selected: PickingTask | null;
  mobileScanInput: string;
  setMobileScanInput: (val: string) => void;
  governanceBusy: boolean;
  setGovernanceBusy: (val: boolean) => void;
  busy: string;
  setError: (msg: string) => void;
}

export function PickingMobileStationCard({
  tasks,
  selected,
  mobileScanInput,
  setMobileScanInput,
  governanceBusy,
  setGovernanceBusy,
  busy,
  setError,
}: PickingMobileStationCardProps) {
  const [mobileScanResult, setMobileScanResult] = useState('');
  const [automationTelemetry, setAutomationTelemetry] = useState<{
    telemetrySyncId: string;
    wcsRoboticsState: string;
    rfidIotTelemetry: string;
    voicePickToLight: string;
  } | null>(null);
  const [fefoCutoffBanner, setFefoCutoffBanner] = useState('');
  const [showAdvancedAutomation, setShowAdvancedAutomation] = useState(false);
  const [isMobileViewport, setIsMobileViewport] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth > 0 && window.innerWidth <= 767,
  );

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const onResize = () => setIsMobileViewport(window.innerWidth > 0 && window.innerWidth <= 767);
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  const mobileReadyTasksCount = tasks.filter(x => ['Open', 'Assigned', 'InProgress'].includes(x.status)).length;
  const shortPickExceptionTasksCount = tasks.filter(x => x.status === 'ShortPick').length;
  const totalRequestedQty = tasks.reduce((sum, x) => sum + Number(x.requestedQuantity || 0), 0);
  const totalPickedQty = tasks.reduce((sum, x) => sum + Number(x.pickedQuantity || 0), 0);

  const verifyMobileScan = async () => {
    if (governanceBusy) return;
    const q = sanitizeScannerCode(mobileScanInput);
    if (!q) {
      setMobileScanResult('Vui lòng nhập hoặc quét mã vạch / vị trí.');
      return;
    }
    const matchedTask = tasks.find(
      t => t.taskCode.toLowerCase().includes(q.toLowerCase()) || (t.sourceCode ?? '').toLowerCase().includes(q.toLowerCase()),
    );
    const matchedLine = (selected?.lines ?? []).find(
      l => l.productCode.toLowerCase() === q.toLowerCase() || l.sourceLocationCode.toLowerCase() === q.toLowerCase(),
    );
    setGovernanceBusy(true);
    setError('');
    try {
      await apiClient.post(
        '/api/v1/picking-tasks/mobile-scan-verify',
        { scanInput: q },
        { headers: idempotencyHeaders('picking-mobile-scan:' + Date.now()) },
      );
      if (matchedLine) {
        setMobileScanResult(`Khớp dòng: ${matchedLine.productCode} tại vị trí ${matchedLine.sourceLocationCode} (Còn: ${matchedLine.remainingQuantity})`);
      } else if (matchedTask) {
        const whLabel = formatWarehouseLabel(matchedTask.warehouseName, undefined, matchedTask.warehouseId);
        setMobileScanResult(`Khớp nhiệm vụ: ${matchedTask.taskCode} • Kho: ${whLabel} (${states[matchedTask.status] ?? matchedTask.status})`);
      } else {
        setMobileScanResult(`Không tìm thấy nhiệm vụ/mã mặt hàng/vị trí khớp với mã quét: ${q}`);
      }
    } catch (e) {
      setMobileScanResult('');
      setError(failureMessage(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const syncAutomationTelemetry = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post(
        '/api/v1/picking-tasks/sync-automation-telemetry',
        {},
        { headers: idempotencyHeaders('picking-sync-telemetry:' + Date.now()) },
      );
      if (!res?.data?.telemetrySyncId) {
        setAutomationTelemetry(null);
        setError('Phản hồi tối ưu lộ trình đi bộ nhặt hàng không hợp lệ từ máy chủ.');
        return;
      }
      const rawWcs = String(res.data.wcsRoboticsState ?? '');
      const rawRfid = String(res.data.rfidIotTelemetry ?? '');
      const rawVoice = String(res.data.voicePickToLight ?? '');
      setAutomationTelemetry({
        telemetrySyncId: String(res.data.telemetrySyncId),
        wcsRoboticsState: telemetryLabels[rawWcs] ?? 'Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô)',
        rfidIotTelemetry: telemetryLabels[rawRfid] ?? 'Đã kiểm tra điều kiện bảo quản theo phân khu kệ',
        voicePickToLight: telemetryLabels[rawVoice] ?? 'Kết hợp ưu tiên lô cận hạn FEFO',
      });
    } catch (e) {
      setAutomationTelemetry(null);
      setError(failureMessage(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const verifyFefoAndCutoff = async () => {
    if (governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const whId = selected?.warehouseId ?? tasks[0]?.warehouseId ?? undefined;
      const [cutoffRes, fefoRes] = await Promise.all([
        apiClient.post('/api/v1/picking-tasks/verify-dispatch-cutoff', { warehouseId: whId }, { headers: idempotencyHeaders('picking-verify-cutoff:' + Date.now()) }),
        apiClient.post('/api/v1/picking-tasks/verify-fefo-compliance', { warehouseId: whId }, { headers: idempotencyHeaders('picking-verify-fefo:' + Date.now()) }),
      ]);
      const cutoffData = (cutoffRes?.data ?? {}) as Record<string, unknown>;
      const fefoData = (fefoRes?.data ?? {}) as Record<string, unknown>;
      const cutoffLabel = typeof cutoffData.statusLabel === 'string' && cutoffData.statusLabel ? cutoffData.statusLabel : 'Đã đối soát giờ chốt xuất kho';
      const cutoffTime = typeof cutoffData.outboundCutoffLocal === 'string' && cutoffData.outboundCutoffLocal ? cutoffData.outboundCutoffLocal : '22:00';
      const openTasks = typeof cutoffData.openPickingTasksCount === 'number' ? cutoffData.openPickingTasksCount : 0;
      const fefoLabel = typeof fefoData.statusLabel === 'string' && fefoData.statusLabel ? fefoData.statusLabel : 'Tuân thủ chuẩn FEFO/FIFO';
      const activeAllocations = typeof fefoData.activeAllocationsCount === 'number' ? fefoData.activeAllocationsCount : 0;
      const expiringLots = typeof fefoData.expiringLotsWithin30DaysCount === 'number' ? fefoData.expiringLotsWithin30DaysCount : 0;
      setFefoCutoffBanner(
        `Kiểm định Giờ cắt đơn & FEFO hoàn tất: ${cutoffLabel} (Giờ chốt: ${cutoffTime}, ${openTasks} tác vụ mở) • ${fefoLabel} (${activeAllocations} phân bổ, ${expiringLots} lô cận hạn <30 ngày)`,
      );
    } catch (e) {
      setFefoCutoffBanner('');
      setError(failureMessage(e));
    } finally {
      setGovernanceBusy(false);
    }
  };

  return (
    <UiCard title="Trạm Điều hành Quét Mã Vạch Di Động & Đồng bộ Ngoại tuyến">
      {!isMobileViewport && (
        <div
          className="outbound-pipeline-stepper-mobile-hide"
          data-testid="picking-barcode-step-guide"
          style={{ marginBottom: 10, padding: '8px 12px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e3a8a', fontSize: 12.5, fontWeight: 600 }}
        >
          Bước 1: Quét vị trí kệ → Bước 2: Quét mã hàng → Bước 3: Xác nhận số lượng
        </div>
      )}
      <div className="ui-inline-wrap">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 10, width: '100%' }}>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đang thực thi / Chờ lấy</span>
            <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{mobileReadyTasksCount}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng cần lấy</span>
            <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalRequestedQty}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng đã lấy</span>
            <strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalPickedQty}</strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Ngoại lệ chờ xử lý ({shortPickExceptionTasksCount})</span>
            <div><UiBadge tone="success">Đồng bộ chống trùng lặp an toàn</UiBadge></div>
          </div>
        </div>
      </div>
      <div className="ui-inline-wrap">
        <input
          aria-label="Quét nhanh mã vạch hoặc vị trí Mobile"
          style={{ minWidth: 310 }}
          value={mobileScanInput}
          onChange={e => setMobileScanInput(e.target.value)}
          placeholder="Quét mã phiếu, mã mặt hàng hoặc vị trí..."
        />
        <button type="button" disabled={governanceBusy || !!busy} onClick={() => void verifyMobileScan()}>Kiểm tra mã quét</button>
        <button type="button" disabled={governanceBusy || !!busy} onClick={() => void syncAutomationTelemetry()}>
          Tối ưu Lộ trình Đi bộ Nhặt hàng Chữ S (Dãy → Kệ → Tầng → Ô)
        </button>
        <button type="button" disabled={governanceBusy || !!busy} onClick={() => void verifyFefoAndCutoff()}>
          Kiểm định Giờ cắt đơn Xuất kho, Quét Lô Hết hạn &amp; Tuân thủ FEFO
        </button>
        <button
          type="button"
          className="ui-btn-secondary"
          data-testid="toggle-advanced-automation"
          aria-expanded={showAdvancedAutomation}
          onClick={() => setShowAdvancedAutomation(prev => !prev)}
        >
          Tùy chọn thiết bị tự động hóa nâng cao (WCS / RFID / Voice Pick)
        </button>
      </div>
      {showAdvancedAutomation && (
        <div
          data-testid="advanced-automation-options-panel"
          style={{ marginTop: 8, padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', display: 'flex', flexDirection: 'column', gap: 8 }}
        >
          <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
            <strong style={{ fontSize: 12.5, color: '#0f172a' }}>
              Điều phối Phần cứng Tự động hóa Nâng cao (WCS Robotics • RFID • Voice Pick • PickingToLight • AMR)
            </strong>
            <button type="button" data-testid="btn-sync-advanced-automation" disabled={governanceBusy || !!busy} onClick={() => void syncAutomationTelemetry()}>
              Đồng bộ tín hiệu thiết bị tự động hóa
            </button>
          </div>
          <span style={{ fontSize: 12, color: '#475569' }}>
            Tùy chọn nâng cao dành cho kho có trang bị băng chuyền phân loại, cổng đọc thẻ từ và đèn hướng dẫn nhặt hàng. Mặc định thu gọn để ưu tiên không gian cho máy quét mã vạch cầm tay (PDA) và lộ trình FEFO.
          </span>
        </div>
      )}
      {mobileScanResult && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {mobileScanResult}
        </div>
      )}
      {automationTelemetry && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {`Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô) kết hợp ưu tiên lô cận hạn FEFO (${automationTelemetry.telemetrySyncId})`}
        </div>
      )}
      {fefoCutoffBanner && (
        <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word' }}>
          {fefoCutoffBanner}
        </div>
      )}
    </UiCard>
  );
}

export function PickingWaveSummaryCard({ waves }: { waves: WaveGroup[] }) {
  return (
    <UiCard title="Tổng hợp Đợt lấy hàng theo Đợt / Đơn">
      <div
        data-testid="wave-release-template-cluster-banner"
        style={{ marginBottom: 10, padding: '8px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', fontSize: 12, color: '#334155', display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}
      >
        <strong>Mẫu Phát Hành Sóng &amp; Giỏ Nhặt Cụm:</strong>
        <UiBadge tone="info">TPL-FLASH-SLA-2H (SLA &lt; 2h • 6 đơn/giỏ)</UiBadge>
        <UiBadge tone="neutral">TPL-CUTOFF-1800 (Chốt 18:00 • 8 đơn/giỏ)</UiBadge>
        <UiBadge tone="warning">TPL-BREACHED-RESCUE (Cứu đơn quá hạn • 4 đơn/giỏ)</UiBadge>
        <span>• Tự động gom giỏ <code>TOTE-CL-01..08</code> &amp; bàn giao sang <code>PackingSession</code> khi đủ ô Put-to-Wall</span>
      </div>
      {waves.length === 0 ? (
        <UiEmptyState title="Chưa có đợt lấy hàng đang hoạt động" detail="Chưa có đợt lấy hàng (Wave/Batch) đang hoạt động." />
      ) : (
        <UiTableScroll>
          <table aria-label="Tổng hợp đợt lấy hàng Wave Picking" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '29%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã đợt</th>
                <th style={{ width: '25%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Phân loại</th>
                <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Số phiếu</th>
                <th style={{ width: '20%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
              </tr>
            </thead>
            <tbody>
              {waves.map(wave => (
                <tr key={wave.waveCode}>
                  <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                    <strong style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{wave.waveCode.replace(/^WAVE-W(\d+)-P(\d+)-.*/i, 'WAVE-QA-00$1')}</strong>
                    <span style={{ display: 'block', fontSize: 10.5, color: '#475569' }}>
                      {formatWarehouseLabel(wave.warehouseName, undefined, wave.warehouseId)}
                    </span>
                  </td>
                  <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                    <UiBadge>{pickingTypeLabels[wave.pickingType] ?? wave.pickingType}</UiBadge>
                    <span style={{ display: 'block', fontSize: 10.5, color: '#475569', marginTop: 2 }}>Ưu tiên mức {wave.priority}</span>
                  </td>
                  <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                    <span style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{wave.taskCount} phiếu ({wave.openTaskCount} mở)</span>
                    <span style={{ display: 'block', fontSize: 10, color: '#475569', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{wave.taskCodes.join(', ')}</span>
                  </td>
                  <td style={{ padding: '8px 10px 8px 0px', fontSize: 11, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                    <strong>{wave.totalPickedQuantity}</strong> / {wave.totalRequestedQuantity}
                    <div style={{ fontSize: 10, color: '#475569' }}>Còn {wave.totalRemainingQuantity}</div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
}
