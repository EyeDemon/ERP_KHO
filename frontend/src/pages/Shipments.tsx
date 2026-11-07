import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import OutboundPipelineStepper from '../components/common/OutboundPipelineStepper';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import type { DockAppointment, Shipment } from '../modules/outbound/shipmentsTypes';
import { messageFor } from '../modules/outbound/shipmentsTypes';
import { ShipmentsOverviewAndTmsCards } from '../modules/outbound/ShipmentsOverviewAndTmsCards';
import { ShipmentDetailCard } from '../modules/outbound/ShipmentDetailCard';

export default function Shipments() {
  const [rows, setRows] = useState<Shipment[]>([]);
  const [selected, setSelected] = useState<Shipment | null>(null);
  const [appointments, setAppointments] = useState<DockAppointment[]>([]);
  const [stagingCode, setStagingCode] = useState('');
  const [huScan, setHuScan] = useState('');
  const [seal, setSeal] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const lock = useRef(new Set<string>());
  const canStage = usePermission('shipment.stage');
  const canShipmentLoad = usePermission('shipment.load');
  const canLoadingExecute = usePermission('loading.execute');
  const canDispatch = usePermission('shipment.dispatch');
  const canUpdate = usePermission('shipment.update');
  const canConfirmDelivery = usePermission('shipment.confirm_delivery');
  const canLoad = canShipmentLoad && canLoadingExecute;

  const list = async () => {
    setLoading(true);
    setError('');
    try {
      setRows((await apiClient.get('/api/v1/shipments')).data);
    } catch (e) {
      setRows([]);
      setSelected(null);
      setError(messageFor(e));
    } finally {
      setLoading(false);
    }
  };

  const detail = async (id: number) => {
    setError('');
    try {
      const value = (await apiClient.get('/api/v1/shipments/' + id)).data as Shipment;
      setSelected(value);
      setStagingCode(
        value.status === 'Ready' && (!value.stagingLocationCode || value.stagingLocationCode === 'STAGING')
          ? ''
          : (value.stagingLocationCode ?? ''),
      );
      setSeal(value.sealNumber ?? '');
      if (value.status === 'Staging' && canLoad) {
        const response = await apiClient.get('/api/v1/dock-yard/appointments', {
          params: { warehouseId: value.warehouseId, direction: 1, status: 5 },
        });
        setAppointments(response.data);
      } else {
        setAppointments([]);
      }
    } catch (e) {
      setSelected(null);
      setAppointments([]);
      setError(messageFor(e));
    }
  };

  useEffect(() => {
    void list();
  }, []);

  const mutate = async (key: string, path: string, body: object) => {
    if (lock.current.has(key)) return;
    lock.current.add(key);
    setBusy(key);
    setError('');
    try {
      const value = (await apiClient.post(path, body, { headers: idempotencyHeaders(key) })).data as Shipment;
      completeIdempotentAction(key);
      setSelected(value);
      setStagingCode(value.stagingLocationCode ?? stagingCode);
      setSeal(value.sealNumber ?? seal);
      setHuScan('');
      await list();
      if (value.status === 'Staging' && canLoad) {
        const response = await apiClient.get('/api/v1/dock-yard/appointments', {
          params: { warehouseId: value.warehouseId, direction: 1, status: 5 },
        });
        setAppointments(response.data);
      } else {
        setAppointments([]);
      }
    } catch (e) {
      if ((e as { response?: { status?: number } })?.response?.status === 409 && selected?.id) await detail(selected.id);
      setError(messageFor(e));
    } finally {
      lock.current.delete(key);
      setBusy('');
    }
  };

  if (loading && rows.length === 0) return <p role="status">Đang tải Shipment...</p>;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Xuất kho"
        title="Điều phối Vận chuyển & Xuất giao"
        description="Luồng vận chuyển: Sẵn sàng → Tập kết → Xếp hàng → Đã xếp → Xuất bến → Đang giao → Hoàn tất. Tồn kho chỉ trừ một lần duy nhất tại thời điểm Xuất bến."
      />
      <OutboundPipelineStepper />
      {error && <div role="alert">{error}</div>}
      <div className="ui-stack">
        <ShipmentsOverviewAndTmsCards
          rows={rows}
          detail={detail}
          setError={setError}
        />

        {selected && (
          <ShipmentDetailCard
            selected={selected}
            appointments={appointments}
            stagingCode={stagingCode}
            setStagingCode={setStagingCode}
            huScan={huScan}
            setHuScan={setHuScan}
            seal={seal}
            setSeal={setSeal}
            busy={busy}
            canStage={canStage}
            canLoad={canLoad}
            canDispatch={canDispatch}
            canUpdate={canUpdate}
            canConfirmDelivery={canConfirmDelivery}
            mutate={mutate}
          />
        )}
      </div>
    </UiPage>
  );
}
