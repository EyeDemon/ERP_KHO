import { useEffect, useRef, useState } from 'react';
import { useInRouterContext, useLocation } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import OutboundPipelineStepper from '../components/common/OutboundPipelineStepper';
import { UiPage, UiPageHeader, UiTabs } from '../ui/ProductionUi';
import type { HandlingUnit, PackingSession, VideoEvidenceItem } from '../modules/outbound/packingSessionsTypes';
import { errorMessage } from '../modules/outbound/packingSessionsTypes';
import { PackingWorkbenchTab } from '../modules/outbound/PackingWorkbenchTab';
import { PackingKittingAndVideoTab } from '../modules/outbound/PackingKittingAndVideoTab';
import { PackingSessionsAndHuTab } from '../modules/outbound/PackingSessionsAndHuTab';

export default function PackingSessions() {
  const inRouter = useInRouterContext();
  const location = inRouter ? useLocation() : null;
  const pathname = location?.pathname ?? (typeof window !== 'undefined' ? window.location.pathname : '/packing-sessions');
  const isKitRoute = pathname.startsWith('/kitting') || pathname.startsWith('/kit-work-orders');
  const [activeTab, setActiveTab] = useState<'workbench' | 'sessions-hu' | 'kitting-video'>(() =>
    isKitRoute ? 'kitting-video' : 'workbench',
  );
  useEffect(() => {
    setActiveTab(isKitRoute ? 'kitting-video' : 'workbench');
  }, [isKitRoute]);
  const [sessions, setSessions] = useState<PackingSession[]>([]);
  const [selected, setSelected] = useState<PackingSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [huType, setHuType] = useState('Carton');
  const [huRegistry, setHuRegistry] = useState<HandlingUnit[]>([]);
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [videoAwbCode, setVideoAwbCode] = useState<string>('');
  const [videoNotice, setVideoNotice] = useState<string>('');
  const [videoEvidences, setVideoEvidences] = useState<VideoEvidenceItem[]>([]);
  const lock = useRef(new Set<string>());
  const canExecute = usePermission('packing.execute');
  const canCreateHu = usePermission('handling_unit.create');
  const canModifyHu = usePermission('handling_unit.modify');

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const [sessionsRes, husRes] = await Promise.all([
        apiClient.get('/api/v1/packing-sessions'),
        apiClient.get('/api/v1/handling-units').catch((err) => {
          if ((err as { response?: { status?: number } })?.response?.status) setError(errorMessage(err));
          return { data: [] };
        }),
      ]);
      setSessions(Array.isArray(sessionsRes.data) ? sessionsRes.data : []);
      setHuRegistry(Array.isArray(husRes.data) ? husRes.data : []);
    } catch (e) {
      setSessions([]);
      setSelected(null);
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  };

  const detail = async (id: number) => {
    if (!id) return;
    setError('');
    try {
      setSelected((await apiClient.get(`/api/v1/packing-sessions/${id}`)).data);
    } catch (e) {
      setSelected(null);
      setError(errorMessage(e));
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const mutate = async (key: string, path: string, body: object) => {
    if (lock.current.has(key)) return;
    lock.current.add(key);
    setBusy(key);
    setError('');
    try {
      const result = (await apiClient.post(path, body, { headers: idempotencyHeaders(key) })).data as PackingSession;
      completeIdempotentAction(key);
      setSelected(result);
      await load();
    } catch (e) {
      if ((e as { response?: { status?: number } })?.response?.status === 409 && selected?.id) await detail(selected.id);
      setError(errorMessage(e));
    } finally {
      lock.current.delete(key);
      setBusy('');
    }
  };

  if (loading && sessions.length === 0) return <p role="status">Đang tải phiên đóng gói...</p>;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow={isKitRoute ? 'Sản xuất & Lắp ráp VAS' : 'XUẤT KHO'}
        title={isKitRoute ? 'Lệnh lắp ráp bộ Kit' : 'Trạm đóng gói đơn hàng'}
        description={
          isKitRoute
            ? 'Quản lý định mức cấu thành bộ Kit (BOM), lập lệnh lắp ráp / tháo dỡ bộ Kit, kế thừa hạn dùng FEFO sớm nhất và cộng dồn giá vốn bình quân gia quyền (MAC).'
            : 'Đóng gói hàng đã lấy vào thùng carton, khay hoặc pallet (HU/SSCC). Bước đóng gói bảo toàn số lượng và không trừ tồn kho.'
        }
      />
      {!isKitRoute && <OutboundPipelineStepper />}
      {error && <div role="alert">{error}</div>}

      {isKitRoute ? (
        <PackingKittingAndVideoTab
          active={true}
          isKitRoute={true}
          sessions={sessions}
          selected={selected}
          huType={huType}
          busy={busy}
          governanceBusy={governanceBusy}
          setGovernanceBusy={setGovernanceBusy}
          videoEvidences={videoEvidences}
          setVideoEvidences={setVideoEvidences}
          setVideoNotice={setVideoNotice}
          setError={setError}
        />
      ) : (
        <>
          <UiTabs
            ariaLabel="Chuyển đổi phân mục bàn đóng gói"
            activeTab={activeTab}
            onChange={setActiveTab}
            tabs={[
              { id: 'workbench', label: 'Bàn Đóng Gói Trực Tiếp (1 Màn Hình)', testId: 'tab-packing-workbench' },
              { id: 'sessions-hu', label: 'Danh sách Phiên & Đơn vị xử lý (HU / SSCC)', badge: sessions.length, testId: 'tab-packing-sessions-hu' },
              { id: 'kitting-video', label: 'Lắp ráp Kit / VAS & Kho Video SHA-256', testId: 'tab-packing-kitting-video' },
            ]}
          />

          <PackingWorkbenchTab
            active={activeTab === 'workbench'}
            sessions={sessions}
            selected={selected}
            busy={busy}
            governanceBusy={governanceBusy}
            videoAwbCode={videoAwbCode}
            setVideoAwbCode={setVideoAwbCode}
            videoNotice={videoNotice}
            setVideoNotice={setVideoNotice}
            setVideoEvidences={setVideoEvidences}
            detail={detail}
            setError={setError}
          />

          <PackingKittingAndVideoTab
            active={activeTab === 'kitting-video'}
            isKitRoute={false}
            sessions={sessions}
            selected={selected}
            huType={huType}
            busy={busy}
            governanceBusy={governanceBusy}
            setGovernanceBusy={setGovernanceBusy}
            videoEvidences={videoEvidences}
            setVideoEvidences={setVideoEvidences}
            setVideoNotice={setVideoNotice}
            setError={setError}
          />

          <PackingSessionsAndHuTab
            active={activeTab === 'sessions-hu'}
            sessions={sessions}
            selected={selected}
            huRegistry={huRegistry}
            huType={huType}
            setHuType={setHuType}
            busy={busy}
            governanceBusy={governanceBusy}
            canExecute={canExecute}
            canCreateHu={canCreateHu}
            canModifyHu={canModifyHu}
            detail={detail}
            mutate={mutate}
          />
        </>
      )}
    </UiPage>
  );
}
