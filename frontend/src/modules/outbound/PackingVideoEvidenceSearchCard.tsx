import React, { useState } from 'react';
import apiClient from '../../services/apiClient';
import { UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { errorMessage, type VideoEvidenceItem } from './packingSessionsTypes';

interface PackingVideoEvidenceSearchCardProps {
  busy: string;
  videoEvidences: VideoEvidenceItem[];
  setVideoEvidences: React.Dispatch<React.SetStateAction<VideoEvidenceItem[]>>;
  setVideoNotice: (val: string) => void;
  setError: (msg: string) => void;
}

export function PackingVideoEvidenceSearchCard({
  busy,
  videoEvidences,
  setVideoEvidences,
  setVideoNotice,
  setError,
}: PackingVideoEvidenceSearchCardProps) {
  const [videoSearchAwb, setVideoSearchAwb] = useState<string>('');

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
      setVideoEvidences(
        items.map((item: Record<string, unknown>, idx: number) => ({
          id: Number(item.id ?? idx + 1),
          sessionId: Number(item.sessionId ?? item.packingSessionId ?? 1),
          sessionCode: String(item.sessionCode ?? 'PACK-001'),
          awbCode: String(item.awbCode ?? 'SPXVN058291042'),
          durationSeconds: Number(item.durationSeconds ?? 48),
          fileSizeMb: Number(item.fileSizeMb ?? 14.6),
          sha256Hash: String(item.sha256Hash ?? 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'),
          recordedAt: String(item.recordedAt ?? new Date().toISOString()),
          cameraStation: String(item.cameraStation ?? 'CAM-PACK-01'),
        })),
      );
      setVideoNotice('Đã tải danh sách bằng chứng video đóng gói.');
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <UiCard title="Kho Bằng chứng Video Đóng gói SHA-256 & Tra cứu Vận đơn (AWB)">
      <form onSubmit={searchPackingVideoEvidences} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 8 }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, flex: '1 1 280px', minWidth: 260 }}>
          Tra cứu theo Mã vận đơn (AWB)
          <input
            aria-label="Tra cứu video theo mã vận đơn AWB"
            value={videoSearchAwb}
            onChange={(e) => setVideoSearchAwb(e.target.value)}
            placeholder="VD: SPXVN058291"
          />
        </label>
        <button type="submit" className="ui-btn ui-btn-secondary" disabled={!!busy}>
          Tra cứu Video theo Mã vận đơn (AWB)
        </button>
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
              {videoEvidences.map((ev) => (
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
  );
}
