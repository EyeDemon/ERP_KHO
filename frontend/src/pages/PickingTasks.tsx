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
  const lock = useRef(new Set<string>());
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

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="XUẤT KHO"
        title="Thực thi Lấy hàng theo Đơn"
        description="Ưu tiên quét mã vạch khi lấy hàng theo phân bổ vị trí và điều phối theo đợt. Xác nhận lấy hàng không trừ tồn kho vật lý; tồn kho chỉ giảm khi xuất bến giao hàng."
      />
      <OutboundPipelineStepper />
      {error && <div role="alert">{error}</div>}

      <MobileBarcodeScannerBar
        warehouseId={selected?.warehouseId ?? tasks[0]?.warehouseId}
        sampleCode={tasks[0]?.taskCode || 'PICK-001'}
        disabled={governanceBusy || !!busy}
        onScan={code => {
          setMobileScanInput(code);
          const matchTask = tasks.find(t => t.taskCode.toUpperCase() === code.toUpperCase());
          if (matchTask) void detail(matchTask.id);
        }}
      />

      <div className="ui-stack">
        <PickingMobileStationCard
          tasks={tasks}
          selected={selected}
          mobileScanInput={mobileScanInput}
          setMobileScanInput={setMobileScanInput}
          governanceBusy={governanceBusy}
          setGovernanceBusy={setGovernanceBusy}
          busy={busy}
          setError={setError}
        />

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
        />

        <PickingPutToWallCard
          tasks={tasks}
          selected={selected}
          busy={busy}
          governanceBusy={governanceBusy}
          setError={setError}
        />

        <PickingWaveSummaryCard waves={waves} />
      </div>
    </UiPage>
  );
}
