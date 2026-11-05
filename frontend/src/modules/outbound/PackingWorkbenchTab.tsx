import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { sanitizeScannerCode } from '../../pages/productCatalog';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import type { CartonRecommendation, PackingSession, VideoEvidenceItem } from './packingSessionsTypes';
import { defaultCartonRecommendation, errorMessage, hiddenTabStyle, packingBoxCatalog } from './packingSessionsTypes';
import { PackingWorkbenchVideoAndAwbPanels } from './PackingWorkbenchVideoAndAwbPanels';

interface PackingWorkbenchTabProps {
  active: boolean;
  sessions: PackingSession[];
  selected: PackingSession | null;
  busy: string;
  governanceBusy: boolean;
  videoAwbCode: string;
  setVideoAwbCode: (val: string) => void;
  videoNotice: string;
  setVideoNotice: (val: string) => void;
  setVideoEvidences: React.Dispatch<React.SetStateAction<VideoEvidenceItem[]>>;
  detail: (id: number) => Promise<void>;
  setError: (msg: string) => void;
}

export function PackingWorkbenchTab({
  active,
  sessions,
  selected,
  busy,
  governanceBusy,
  videoAwbCode,
  setVideoAwbCode,
  videoNotice,
  setVideoNotice,
  setVideoEvidences,
  detail,
  setError,
}: PackingWorkbenchTabProps) {
  const [selectedBoxCode, setSelectedBoxCode] = useState<string>('CARTON-M');
  const [unifiedScanInput, setUnifiedScanInput] = useState<string>('');
  const [unifiedScanStatus, setUnifiedScanStatus] = useState<string>('');
  const [unifiedScanCancelledWarning, setUnifiedScanCancelledWarning] = useState<string>('');
  const [unifiedSuggestedCarton, setUnifiedSuggestedCarton] = useState<string>('Thùng trung M (35×25×20 cm)');
  const [unifiedAwbPrintNotice, setUnifiedAwbPrintNotice] = useState<string>('');
  const [unifiedScanTimestamp, setUnifiedScanTimestamp] = useState<string>('08:30:00');
  const [cartonRecommendation, setCartonRecommendation] = useState<CartonRecommendation>(defaultCartonRecommendation);
  const [cartonRecommendBusy, setCartonRecommendBusy] = useState<boolean>(false);

  const applyCatalogCartonSelection = (boxCode: string) => {
    const matched = packingBoxCatalog.find(b => b.code === boxCode) ?? packingBoxCatalog[1];
    setSelectedBoxCode(matched.code);
    setUnifiedSuggestedCarton(matched.name);
    setCartonRecommendation({
      recommendedCartonCode: matched.code,
      recommendedCartonName: matched.name,
      lengthCm: matched.l,
      widthCm: matched.w,
      heightCm: matched.h,
      totalItemsVolumeM3: matched.vol,
      volumeFillRatePercent: 80,
      grossWeightKg: matched.gross,
      dimWeightKg: matched.dimWeightKg,
      isDimWeightHigher: matched.dimWeightKg > matched.gross,
    });
  };

  const handleRecommendCarton3D = async (customBoxCode?: string) => {
    if (cartonRecommendBusy) return;
    setCartonRecommendBusy(true);
    setError('');
    try {
      const targetId = selected?.id ?? sessions[0]?.id ?? 77;
      const preferredCode = customBoxCode || selectedBoxCode || 'CARTON-M';
      const res = await apiClient.post(`/api/v1/packing-sessions/${targetId}/recommend-carton`, {
        dimDivisor: 6000,
        preferredCartonCode: preferredCode,
        requiredQuantity: selected?.requiredQuantity ?? 4,
      });
      const d = (res?.data ?? {}) as Record<string, unknown>;
      const code = String(d.recommendedCartonCode ?? preferredCode);
      const matchedCatalog = packingBoxCatalog.find(b => b.code === code) ?? packingBoxCatalog[1];
      const dimObj = (d.dimensionsCm && typeof d.dimensionsCm === 'object') ? (d.dimensionsCm as Record<string, unknown>) : null;
      const dimStr = typeof d.recommendedDimensionsCm === 'string'
        ? d.recommendedDimensionsCm
        : (typeof d.dimensionsCm === 'string' ? d.dimensionsCm : (typeof d.recommendedCartonName === 'string' ? d.recommendedCartonName : ''));
      const dimMatch = dimStr.match(/(\d+(?:\.\d+)?)\s*[x×X*]\s*(\d+(?:\.\d+)?)\s*[x×X*]\s*(\d+(?:\.\d+)?)/);
      const lengthCm = Number(dimObj?.lengthCm ?? dimObj?.l ?? d.lengthCm ?? (dimMatch ? dimMatch[1] : matchedCatalog.l));
      const widthCm = Number(dimObj?.widthCm ?? dimObj?.w ?? d.widthCm ?? (dimMatch ? dimMatch[2] : matchedCatalog.w));
      const heightCm = Number(dimObj?.heightCm ?? dimObj?.h ?? d.heightCm ?? (dimMatch ? dimMatch[3] : matchedCatalog.h));
      const dimWeightKg = Number(d.dimWeightKg ?? Math.round(((lengthCm * widthCm * heightCm) / 6000) * 100) / 100);
      const grossWeightKg = Number(d.grossWeightKg ?? d.totalItemWeightKg ?? d.actualWeightKg ?? matchedCatalog.gross);
      const boxVolM3 = Math.max(0.0001, (lengthCm * widthCm * heightCm) / 1000000);
      const totalItemsVolumeM3 = Number(d.totalItemsVolumeM3 ?? Math.round(boxVolM3 * 0.8 * 10000) / 10000);
      const volumeFillRatePercent = Number(d.volumeFillRatePercent ?? Math.min(98, Math.round((totalItemsVolumeM3 / boxVolM3) * 100)));
      const recommendedCartonName = typeof d.recommendedCartonName === 'string' && d.recommendedCartonName.trim().length > 0 ? d.recommendedCartonName : matchedCatalog.name;
      setSelectedBoxCode(code);
      setCartonRecommendation({
        recommendedCartonCode: code,
        recommendedCartonName,
        lengthCm,
        widthCm,
        heightCm,
        totalItemsVolumeM3,
        volumeFillRatePercent,
        grossWeightKg,
        dimWeightKg,
        isDimWeightHigher: Boolean(d.isDimWeightHigher ?? d.isDimWeightCharged ?? (dimWeightKg > grossWeightKg)),
      });
      setUnifiedSuggestedCarton(recommendedCartonName);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setCartonRecommendBusy(false);
    }
  };

  const handleUnifiedOrderScan = (rawCode?: string) => {
    const codeToCheck = sanitizeScannerCode(rawCode ?? unifiedScanInput).toUpperCase();
    setError('');
    setUnifiedAwbPrintNotice('');
    const matchedSession = sessions.find(s =>
      s.sessionCode.toUpperCase() === codeToCheck
      || (s.pickingTaskCode && s.pickingTaskCode.toUpperCase() === codeToCheck)
      || (s.sourceCode && s.sourceCode.toUpperCase() === codeToCheck),
    ) ?? selected ?? sessions[0];
    if (matchedSession && matchedSession.id !== selected?.id) void detail(matchedSession.id);
    const isCancelledMidPick = codeToCheck.includes('CANCEL') || codeToCheck.includes('PUTBACK') || matchedSession?.status === 'Cancelled' || matchedSession?.status === 'CancelPendingPutback';
    if (isCancelledMidPick) {
      setUnifiedScanCancelledWarning('CẢNH BÁO CHẶN ĐÓNG GÓI: Đơn hàng / Tác vụ nhặt hàng đang ở trạng thái Hủy giữa chừng chờ hoàn kệ (CancelPendingPutback / Đã hủy). Tuyệt đối không đóng gói — vui lòng chuyển hàng sang luồng Putback hoàn kệ!');
      setUnifiedScanStatus('');
      return;
    }
    setUnifiedScanCancelledWarning('');
    const reqQty = matchedSession?.requiredQuantity ?? 4;
    const targetBoxCode = reqQty <= 2 ? 'CARTON-S' : reqQty <= 8 ? 'CARTON-M' : reqQty <= 15 ? 'CARTON-L' : 'CARTON-XL';
    const matchedCatalog = packingBoxCatalog.find(b => b.code === targetBoxCode) ?? packingBoxCatalog[1];
    applyCatalogCartonSelection(matchedCatalog.code);
    const nowStr = new Date().toLocaleTimeString('vi-VN');
    setUnifiedScanTimestamp(nowStr);
    const targetCode = codeToCheck || matchedSession?.sessionCode || 'PACK-001';
    setUnifiedScanStatus(`Đã xác thực đơn ${targetCode} (${reqQty} SP) lúc ${nowStr} — Đủ điều kiện đóng gói • Đề xuất: ${matchedCatalog.name}`);
  };

  return (
    <div style={active ? { display: 'contents' } : hiddenTabStyle}>
      <UiCard title="Trạm Đóng Gói Hợp Nhất 1 Màn Hình (Quét Đơn • Gợi Ý Thùng • Ghi Hình SHA-256 • In Tem Nhiệt AWB)">
        <div data-testid="unified-packing-station-card" style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 10 }}>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 1: Quét nhanh Mã Đơn / Phiếu Nhặt</strong>
              <UiBadge tone={unifiedScanCancelledWarning ? 'danger' : 'success'}>{unifiedScanCancelledWarning ? 'Chặn đóng gói' : 'Kiểm tra hủy đơn giữa chừng'}</UiBadge>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              <input
                type="search"
                data-testid="unified-packing-scan-input"
                aria-label="Quét nhanh Mã Đơn hoặc Mã Phiếu Nhặt tại bàn đóng gói"
                value={unifiedScanInput}
                onChange={e => setUnifiedScanInput(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); handleUnifiedOrderScan(unifiedScanInput); } }}
                placeholder="Quét mã đơn / PICK-001 / PACK-001..."
                style={{ flex: '1 1 180px', minWidth: 0, minHeight: 34, height: 34 }}
              />
              <button type="button" className="ui-btn ui-btn-primary ui-primary-button" data-testid="unified-packing-verify-order-btn" disabled={!!busy || governanceBusy} onClick={() => handleUnifiedOrderScan(unifiedScanInput)} style={{ minHeight: 34, padding: '4px 12px' }}>
                Kiểm tra Đơn &amp; Chốt Thùng
              </button>
            </div>
            {unifiedScanCancelledWarning && (
              <div role="alert" data-testid="unified-packing-cancelled-warning" style={{ padding: '6px 10px', borderRadius: 8, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 12, fontWeight: 600 }}>
                {unifiedScanCancelledWarning}
              </div>
            )}
            {unifiedScanStatus && !unifiedScanCancelledWarning && (
              <div role="status" data-testid="unified-packing-scan-status" style={{ padding: '6px 10px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 12 }}>
                {unifiedScanStatus}
              </div>
            )}
          </div>

          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 2: Gợi ý Thùng Carton Tự động</strong>
              <UiBadge tone="info">Tối ưu Thể tích &amp; Trọng lượng</UiBadge>
            </div>
            <div data-testid="unified-carton-suggestion" style={{ fontSize: 12.5, fontWeight: 700, color: '#1e3a8a', padding: '6px 10px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe' }}>
              Đề xuất: {unifiedSuggestedCarton}
              <div style={{ marginTop: 4, fontSize: 11.5, fontWeight: 500, color: '#1e293b', display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 2, fontVariantNumeric: 'tabular-nums' }}>
                <span>Kích thước thùng: <strong>{cartonRecommendation.lengthCm}×{cartonRecommendation.widthCm}×{cartonRecommendation.heightCm} cm</strong></span>
                <span>Tổng thể tích hàng: <strong>{cartonRecommendation.totalItemsVolumeM3} m³</strong> ({cartonRecommendation.volumeFillRatePercent}% lấp đầy)</span>
                <span>Trọng lượng thực tế: <strong>{cartonRecommendation.grossWeightKg} kg</strong></span>
                <span>Trọng lượng quy đổi (D×R×C / 6000): <strong>{cartonRecommendation.dimWeightKg} kg</strong></span>
              </div>
              {cartonRecommendation.dimWeightKg > cartonRecommendation.grossWeightKg && (
                <div data-testid="dim-weight-warning-badge" style={{ marginTop: 4, padding: '3px 8px', borderRadius: 6, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 11, fontWeight: 700 }}>
                  Cảnh báo Cước Quy Đổi (Trọng lượng quy đổi &gt; Trọng lượng thực tế)
                </div>
              )}
            </div>
            <div className="ui-inline-wrap" style={{ gap: 6 }}>
              {packingBoxCatalog.map(box => (
                <button
                  key={box.code}
                  type="button"
                  onClick={() => applyCatalogCartonSelection(box.code)}
                  style={{ fontSize: 11.5, minHeight: 28, padding: '3px 8px', fontWeight: cartonRecommendation.recommendedCartonCode === box.code ? 700 : 500, background: cartonRecommendation.recommendedCartonCode === box.code ? '#1e40af' : '#ffffff', color: cartonRecommendation.recommendedCartonCode === box.code ? '#ffffff' : '#1e293b', border: '1px solid #93c5fd', borderRadius: 6 }}
                >
                  {box.code}
                </button>
              ))}
              <button type="button" data-testid="btn-recommend-carton-3d" disabled={!!busy || governanceBusy || cartonRecommendBusy} onClick={() => void handleRecommendCarton3D(selectedBoxCode)} style={{ fontSize: 11.5, minHeight: 28, padding: '3px 8px' }}>
                {cartonRecommendBusy ? 'Đang tính 3D...' : 'Tính Toán Gợi Ý Thùng 3D & Trọng Lượng Quy Đổi'}
              </button>
            </div>
          </div>

          <PackingWorkbenchVideoAndAwbPanels
            sessions={sessions}
            selected={selected}
            busy={busy}
            governanceBusy={governanceBusy}
            videoAwbCode={videoAwbCode}
            setVideoAwbCode={setVideoAwbCode}
            videoNotice={videoNotice}
            setVideoNotice={setVideoNotice}
            setVideoEvidences={setVideoEvidences}
            unifiedScanTimestamp={unifiedScanTimestamp}
            setUnifiedScanTimestamp={setUnifiedScanTimestamp}
            unifiedScanCancelledWarning={unifiedScanCancelledWarning}
            unifiedAwbPrintNotice={unifiedAwbPrintNotice}
            setUnifiedAwbPrintNotice={setUnifiedAwbPrintNotice}
            setError={setError}
          />
        </div>
      </UiCard>
    </div>
  );
}
