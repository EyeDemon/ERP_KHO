import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import OutboundPipelineStepper from '../components/common/OutboundPipelineStepper';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import type { PickingTask, WaveGroup } from '../modules/outbound/pickingTasksTypes';
import { failureMessage } from '../modules/outbound/pickingTasksTypes';
import { PickingMobileStationCard, PickingWaveSummaryCard } from '../modules/outbound/PickingMobileAndWaveCards';
import { PickingPutToWallCard } from '../modules/outbound/PickingPutToWallCard';
import { PickingTasksGrid } from '../modules/outbound/PickingTaskListAndDetailCards';

export default function PickingTasks() {
  const [tasks, setTasks] = useState<PickingTask[]>([]);
  const [waves, setWaves] = useState<WaveGroup[]>([]);
  const [selected, setSelected] = useState<PickingTask | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [mobileScanInput, setMobileScanInput] = useState('');
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [isMobileViewport, setIsMobileViewport] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth > 0 && window.innerWidth <= 767,
  );
  const [isHandheldViewport, setIsHandheldViewport] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false;
    if (typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 480px)').matches) return true;
    return window.innerWidth > 0 && window.innerWidth <= 480;
  });
  const lock = useRef(new Set<string>());

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mql480 = typeof window.matchMedia === 'function' ? window.matchMedia('(max-width: 480px)') : null;
    const onResize = () => {
      setIsMobileViewport(window.innerWidth > 0 && window.innerWidth <= 767);
      setIsHandheldViewport(
        Boolean(mql480?.matches) || (window.innerWidth > 0 && window.innerWidth <= 480),
      );
    };
    window.addEventListener('resize', onResize);
    mql480?.addEventListener?.('change', onResize);
    return () => {
      window.removeEventListener('resize', onResize);
      mql480?.removeEventListener?.('change', onResize);
    };
  }, []);
  const canAssign = usePermission('picking.assign');
  const canExecute = usePermission('picking.execute');
  const canShortPick = usePermission('picking.short_pick');
  const canOverride = usePermission('picking.override');

  const detail = async (id: number) => {
    if (!id) return;
    setError('');
    try {
      setSelected((await apiClient.get(`/api/v1/picking-tasks/${id}`)).data);
    } catch (e) {
      setSelected(null);
      setError(failureMessage(e));
    }
  };

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const [tasksRes, waveRes] = await Promise.all([
        apiClient.get('/api/v1/picking-tasks'),
        apiClient.get('/api/v1/picking-tasks/wave-summary').catch((err) => {
          if (err?.response?.status && err.response.status >= 400) setError(failureMessage(err));
          return { data: [] };
        }),
      ]);
      const loadedTasks = Array.isArray(tasksRes.data) ? (tasksRes.data as PickingTask[]) : [];
      setTasks(loadedTasks);
      if (!selected && loadedTasks.length > 0) {
        if (Array.isArray(loadedTasks[0].lines) && loadedTasks[0].lines.length > 0) {
          setSelected(loadedTasks[0]);
        }
        void detail(loadedTasks[0].id);
      }
      if (Array.isArray(waveRes?.data)) {
        setWaves(waveRes.data as WaveGroup[]);
      } else {
        setWaves([]);
      }
    } catch (e) {
      setTasks([]);
      setWaves([]);
      setSelected(null);
      setError(failureMessage(e));
    } finally {
      setLoading(false);
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
      const task = (await apiClient.post(path, body, { headers: idempotencyHeaders(key) })).data as PickingTask;
      completeIdempotentAction(key);
      setSelected(task);
      await load();
    } catch (e) {
      if ((e as { response?: { status?: number } })?.response?.status === 409 && selected?.id) await detail(selected.id);
      setError(failureMessage(e));
    } finally {
      lock.current.delete(key);
      setBusy('');
    }
  };

  if (loading && tasks.length === 0) return <p role="status">Đang tải nhiệm vụ lấy hàng...</p>;

  const waitingCount = tasks.filter(x => x.status === 'Open' || x.status === 'Assigned').length;
  const activePickingCount = tasks.filter(x => ['InProgress', 'ShortPick', 'Resolved'].includes(x.status)).length;
  const completedPickingCount = tasks.filter(x => x.status === 'Completed').length;

  return (
    <UiPage>
      <div className="picking-tasks-page ui-stack" style={isMobileViewport ? { gap: 6 } : undefined}>
        <style>{`
          @media (max-width: 480px) {
            .picking-tasks-page .picking-page-header-wrap { display: none !important; }
            .picking-tasks-page .picking-handheld-kpi-strip {
              display: grid !important;
              grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr) !important;
              gap: 6px !important;
            }
          }
        `}</style>
        <div className="picking-page-header-wrap" style={isHandheldViewport ? { display: 'none' } : undefined}>
          <UiPageHeader
            eyebrow="Xuất kho"
            title="Lấy hàng tại kệ"
            description="Quét vị trí kệ và mã hàng để xác nhận lấy hàng theo ưu tiên hạn dùng."
          />
        </div>
        {!isMobileViewport && (
          <div className="outbound-pipeline-stepper-mobile-hide">
            <OutboundPipelineStepper />
          </div>
        )}
        {error && <div role="alert">{error}</div>}

        <MobileBarcodeScannerBar
          warehouseId={selected?.warehouseId ?? tasks[0]?.warehouseId}
          sampleCode={tasks[0]?.taskCode || 'PICK-001'}
          disabled={governanceBusy || !!busy}
          stepLabels={['Bước 1: Quét vị trí kệ', 'Bước 2: Quét mã hàng', 'Bước 3: Xác nhận số lượng']}
          onScan={code => {
            setMobileScanInput(code);
            const matchTask = tasks.find(t => t.taskCode.toUpperCase() === code.toUpperCase());
            if (matchTask) void detail(matchTask.id);
          }}
        />

        {isHandheldViewport && (
          <section
            aria-label="Tóm tắt tiến độ lấy hàng"
            data-testid="picking-handheld-kpi-strip"
            className="picking-handheld-kpi-strip"
            style={{
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr)',
              gap: 6,
            }}
          >
            {[
              { label: 'Chờ lấy', value: waitingCount },
              { label: 'Đang lấy', value: activePickingCount },
              { label: 'Hoàn tất', value: completedPickingCount },
            ].map(item => (
              <div
                key={item.label}
                style={{
                  padding: '6px 8px',
                  borderRadius: 8,
                  border: '1px solid #cbd5e1',
                  background: '#ffffff',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 4,
                  minWidth: 0,
                }}
              >
                <span style={{ fontSize: 11.5, color: '#475569', fontWeight: 600, whiteSpace: 'nowrap' }}>{item.label}</span>
                <strong style={{ fontSize: 14, color: '#0f172a', fontWeight: 700, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                  {item.value}
                </strong>
              </div>
            ))}
          </section>
        )}

        <div className="ui-stack" style={isHandheldViewport ? { gap: 8 } : undefined}>
          {!isHandheldViewport && (
            <PickingMobileStationCard
              tasks={tasks}
              selected={selected}
              mobileScanInput={mobileScanInput}
              setMobileScanInput={setMobileScanInput}
              governanceBusy={governanceBusy}
              setGovernanceBusy={setGovernanceBusy}
              busy={busy}
              setError={setError}
              isHandheldViewport={false}
            />
          )}

          <PickingTasksGrid
            tasks={tasks}
            selected={selected}
            busy={busy}
            canAssign={canAssign}
            canExecute={canExecute}
            canShortPick={canShortPick}
            canOverride={canOverride}
            detail={detail}
            mutate={mutate}
            setError={setError}
            isHandheldViewport={isHandheldViewport}
          />

          {isHandheldViewport && (
            <PickingMobileStationCard
              tasks={tasks}
              selected={selected}
              mobileScanInput={mobileScanInput}
              setMobileScanInput={setMobileScanInput}
              governanceBusy={governanceBusy}
              setGovernanceBusy={setGovernanceBusy}
              busy={busy}
              setError={setError}
              isHandheldViewport={true}
            />
          )}

          <PickingPutToWallCard
            tasks={tasks}
            selected={selected}
            busy={busy}
            governanceBusy={governanceBusy}
            setError={setError}
          />

          <PickingWaveSummaryCard waves={waves} />
        </div>
      </div>
    </UiPage>
  );
}
