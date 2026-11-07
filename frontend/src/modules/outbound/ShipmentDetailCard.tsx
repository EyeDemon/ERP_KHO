import { useState } from 'react';
import { sanitizeScannerCode } from '../../pages/productCatalog';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { DockAppointment, Shipment } from './shipmentsTypes';
import { huLabels, huTypeLabels, statusLabels, tone, trackingEventLabels, trackingSourceLabels } from './shipmentsTypes';

interface ShipmentDetailCardProps {
  selected: Shipment;
  appointments: DockAppointment[];
  stagingCode: string;
  setStagingCode: (val: string) => void;
  huScan: string;
  setHuScan: (val: string) => void;
  seal: string;
  setSeal: (val: string) => void;
  busy: string;
  canStage: boolean;
  canLoad: boolean;
  canDispatch: boolean;
  canUpdate: boolean;
  canConfirmDelivery: boolean;
  mutate: (key: string, path: string, body: object) => Promise<void>;
}

export function ShipmentDetailCard({
  selected,
  appointments,
  stagingCode,
  setStagingCode,
  huScan,
  setHuScan,
  seal,
  setSeal,
  busy,
  canStage,
  canLoad,
  canDispatch,
  canUpdate,
  canConfirmDelivery,
  mutate,
}: ShipmentDetailCardProps) {
  const [appointmentId, setAppointmentId] = useState('');
  const [receiverName, setReceiverName] = useState('');
  const [evidenceReference, setEvidenceReference] = useState('');
  const [carrierReference, setCarrierReference] = useState('');
  const [deliveryNote, setDeliveryNote] = useState('');
  const [failureReason, setFailureReason] = useState('');
  const [failureNote, setFailureNote] = useState('');
  const [returnReason, setReturnReason] = useState('');
  const [returnNote, setReturnNote] = useState('');

  return (
    <UiCard title={'Chi tiết ' + selected.shipmentCode}>
      <div className="ui-inline-wrap">
        <UiBadge tone={tone(selected.status)}>{statusLabels[selected.status] ?? selected.status}</UiBadge>
        <span className="ui-muted-text">{selected.packingSessionCode} • {selected.sourceCode ?? selected.sourceType} • {selected.warehouseName}</span>
        <span>Đã xếp: <strong>{selected.loadedHandlingUnitCount}</strong> / {selected.handlingUnitCount} kiện</span>
        {selected.stagingLocationCode && <span>Vị trí tập kết: <strong>{selected.stagingLocationCode}</strong></span>}
        {selected.exportReceiptCode && <UiBadge tone="success">{`Phiếu xuất kho: ${selected.exportReceiptCode}`}</UiBadge>}
      </div>
      <p className="ui-muted-text">
        Quản lý vận đơn xuyên suốt từ sẵn sàng, tập kết, xếp hàng lên xe, xuất bến đến xác nhận giao hàng. Xuất bến là thời điểm trừ tồn kho duy nhất; mọi trạng thái sau đó không làm thay đổi tồn kho thực tế.
      </p>

      <UiTableScroll>
        <table aria-label="Handling Unit của Shipment">
          <thead>
            <tr>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>#</th>
              <th>Mã kiện</th>
              <th>Loại</th>
              <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng</th>
              <th>Trạng thái</th>
            </tr>
          </thead>
          <tbody>
            {(selected.handlingUnits ?? []).map(hu => (
              <tr key={hu.id}>
                <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{hu.sequence}</td>
                <td>
                  <strong style={{ whiteSpace: 'nowrap' }}>{hu.huCode}</strong>
                  <div className="ui-muted-text" style={{ whiteSpace: 'nowrap' }}>{hu.barcode}</div>
                </td>
                <td>{huTypeLabels[hu.type] ?? hu.type}</td>
                <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{hu.contentQuantity}</td>
                <td><UiBadge tone={tone(hu.status)}>{huLabels[hu.status] ?? hu.status}</UiBadge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </UiTableScroll>

      {selected.status === 'Ready' && canStage && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Đưa vào Khu vực Tập kết</div>
          <div className="ui-inline-wrap" style={{ alignItems: 'flex-end', gap: 10 }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Quét hoặc nhập mã vị trí tập kết
              <input aria-label="Shipment staging location" value={stagingCode} onChange={e => setStagingCode(e.target.value)} placeholder="Ví dụ: STG-OUT-01" autoComplete="off" />
            </label>
            <div className="ui-inline-wrap" style={{ gap: 6, alignItems: 'center' }} aria-label="Gợi ý vị trí tập kết nhanh">
              {['STG-OUT-01', 'STG-OUT-02', 'STAGING'].map(code => (
                <button key={code} type="button" className="ui-btn ui-btn-secondary" style={{ padding: '4px 10px', minHeight: 34, fontSize: 12 }} onClick={() => setStagingCode(code)}>{code}</button>
              ))}
            </div>
            <button type="button" disabled={!!busy || !sanitizeScannerCode(stagingCode)} onClick={() => void mutate('shipment-stage-' + selected.id, '/api/v1/shipments/' + selected.id + '/stage', { stagingLocationCode: sanitizeScannerCode(stagingCode), rowVersion: selected.rowVersion })}>
              Xác nhận Tập kết
            </button>
          </div>
        </div>
      )}

      {selected.status === 'Staging' && canLoad && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Bắt đầu xếp hàng lên xe</div>
          <div className="ui-inline-wrap">
            <label>Outbound appointment IN_SERVICE
              <select aria-label="Outbound appointment loading" value={appointmentId} onChange={e => setAppointmentId(e.target.value)}>
                <option value="">Chọn appointment</option>
                {appointments.map(item => (
                  <option key={item.id} value={item.id}>{item.code} • {item.dockCode ?? 'chưa dock'} • {item.vehiclePlate ?? 'chưa xe'}</option>
                ))}
              </select>
            </label>
            <button type="button" disabled={!!busy || !appointmentId} onClick={() => void mutate('shipment-start-loading-' + selected.id, '/api/v1/shipments/' + selected.id + '/start-loading', { dockAppointmentId: Number(appointmentId), rowVersion: selected.rowVersion })}>
              Bắt đầu xếp hàng lên xe
            </button>
          </div>
        </div>
      )}

      {selected.status === 'Loading' && canLoad && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Scan HU lên xe</div>
          <div className="ui-inline-wrap">
            <label>HU barcode / code / SSCC
              <input aria-label="Shipment HU scan" value={huScan} onChange={e => setHuScan(e.target.value)} autoComplete="off" />
            </label>
            <button type="button" disabled={!!busy || !sanitizeScannerCode(huScan)} onClick={() => void mutate('shipment-load-hu-' + selected.id + '-' + sanitizeScannerCode(huScan).toUpperCase(), '/api/v1/shipments/' + selected.id + '/load-hu', { handlingUnitBarcode: sanitizeScannerCode(huScan), rowVersion: selected.rowVersion })}>
              Xác nhận HU đã load
            </button>
          </div>
          <div className="ui-inline-wrap">
            <span>Dock: <strong>{selected.dockCode ?? '—'}</strong></span>
            <span>Xe: <strong>{selected.vehiclePlate ?? '—'}</strong></span>
            {selected.trailerPlate && <span>Trailer: <strong>{selected.trailerPlate}</strong></span>}
          </div>
          <div className="ui-inline-wrap">
            <label>Seal number
              <input aria-label="Shipment seal number" value={seal} onChange={e => setSeal(e.target.value)} placeholder="Không bắt buộc" />
            </label>
            <button type="button" disabled={!!busy || selected.loadedHandlingUnitCount !== selected.handlingUnitCount} onClick={() => void mutate('shipment-complete-loading-' + selected.id, '/api/v1/shipments/' + selected.id + '/complete-loading', { sealNumber: seal || undefined, rowVersion: selected.rowVersion })}>
              Xác nhận đã xếp lên xe
            </button>
          </div>
        </div>
      )}

      {selected.status === 'Loaded' && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Dispatch Shipment</div>
          <p>
            Shipment đã LOADED. Dispatch sẽ consume Reservation/Allocation, chuyển HU sang SHIPPED,
            ghi SHIP ledger theo Location và giảm OnHand. Thao tác này là inventory boundary.
            {selected.sealNumber && <> Seal: {selected.sealNumber}.</>}
          </p>
          {canDispatch ? (
            <button type="button" disabled={!!busy} onClick={() => void mutate('shipment-dispatch-' + selected.id, '/api/v1/shipments/' + selected.id + '/dispatch', { rowVersion: selected.rowVersion })}>Dispatch Shipment</button>
          ) : (
            <p className="ui-muted-text">Bạn không có quyền shipment.dispatch.</p>
          )}
        </div>
      )}

      {selected.status === 'Dispatched' && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Post-dispatch tracking</div>
          <p>
            <strong>Shipment đã DISPATCHED.</strong> SHIP ledger đã được ghi và OnHand đã giảm tại inventory boundary này.
            {selected.dispatchedAt && <> Thời điểm: {new Date(selected.dispatchedAt).toLocaleString('vi-VN')}.</>}
            {selected.dispatchedByName && <> Người dispatch: {selected.dispatchedByName}.</>}
          </p>
          {canUpdate ? (
            <button type="button" disabled={!!busy} onClick={() => void mutate('shipment-in-transit-' + selected.id, '/api/v1/shipments/' + selected.id + '/mark-in-transit', { rowVersion: selected.rowVersion })}>Chuyển IN_TRANSIT</button>
          ) : (
            <p className="ui-muted-text">Bạn cần shipment.update để cập nhật trạng thái vận chuyển.</p>
          )}
        </div>
      )}

      {selected.status === 'InTransit' && (
        <div className="ui-stack">
          {canConfirmDelivery && (
            <div className="ui-subcard ui-stack">
              <div className="ui-subcard-title">Xác nhận giao hàng / POD</div>
              <div className="ui-inline-wrap">
                <label>Người nhận <input aria-label="POD receiver name" value={receiverName} onChange={e => setReceiverName(e.target.value)} /></label>
                <label>Evidence reference <input aria-label="POD evidence reference" value={evidenceReference} onChange={e => setEvidenceReference(e.target.value)} placeholder="Document/storage reference" /></label>
                <label>Carrier reference <input aria-label="POD carrier reference" value={carrierReference} onChange={e => setCarrierReference(e.target.value)} /></label>
                <label>Ghi chú giao hàng <input aria-label="POD delivery note" value={deliveryNote} onChange={e => setDeliveryNote(e.target.value)} /></label>
                <button
                  type="button"
                  disabled={!!busy || !receiverName.trim() || (!evidenceReference.trim() && !carrierReference.trim())}
                  onClick={() => void mutate('shipment-delivery-confirm-' + selected.id, '/api/v1/shipments/' + selected.id + '/delivery-confirm', {
                    receiverName: receiverName.trim(),
                    evidenceReference: evidenceReference.trim() || undefined,
                    carrierReference: carrierReference.trim() || undefined,
                    deliveryNote: deliveryNote.trim() || undefined,
                    rowVersion: selected.rowVersion,
                  })}
                >
                  Xác nhận DELIVERED
                </button>
              </div>
            </div>
          )}

          {canUpdate && (
            <div className="ui-subcard ui-stack">
              <div className="ui-subcard-title">Delivery exception</div>
              <div className="ui-inline-wrap">
                <label>Mã lý do thất bại <input aria-label="Delivery failure reason" value={failureReason} onChange={e => setFailureReason(e.target.value)} placeholder="CUSTOMER_UNAVAILABLE" /></label>
                <label>Ghi chú <input aria-label="Delivery failure note" value={failureNote} onChange={e => setFailureNote(e.target.value)} /></label>
                <button type="button" disabled={!!busy || !failureReason.trim()} onClick={() => void mutate('shipment-delivery-failed-' + selected.id, '/api/v1/shipments/' + selected.id + '/delivery-failed', { reasonCode: failureReason.trim(), note: failureNote.trim() || undefined, rowVersion: selected.rowVersion })}>
                  Ghi nhận DELIVERY_FAILED
                </button>
              </div>
              <div className="ui-inline-wrap">
                <label>Lý do trả về kho <input aria-label="Return to warehouse reason" value={returnReason} onChange={e => setReturnReason(e.target.value)} placeholder="CUSTOMER_REFUSED" /></label>
                <label>Ghi chú return <input aria-label="Return to warehouse note" value={returnNote} onChange={e => setReturnNote(e.target.value)} /></label>
                <button type="button" disabled={!!busy || !returnReason.trim()} onClick={() => void mutate('shipment-return-' + selected.id, '/api/v1/shipments/' + selected.id + '/return-initiate', { reasonCode: returnReason.trim(), note: returnNote.trim() || undefined, rowVersion: selected.rowVersion })}>
                  Khởi tạo RETURN_TO_WAREHOUSE
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {selected.status === 'DeliveryFailed' && canUpdate && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Xử lý giao thất bại</div>
          <p>Retry/return chỉ thay đổi logistics workflow; không tạo thêm SHIP transaction.</p>
          <div className="ui-inline-wrap">
            <button type="button" disabled={!!busy} onClick={() => void mutate('shipment-retry-delivery-' + selected.id, '/api/v1/shipments/' + selected.id + '/retry-delivery', { note: failureNote.trim() || undefined, rowVersion: selected.rowVersion })}>Retry delivery</button>
            <label>Lý do trả về kho <input aria-label="Failed return reason" value={returnReason} onChange={e => setReturnReason(e.target.value)} placeholder="CUSTOMER_REFUSED" /></label>
            <button type="button" disabled={!!busy || !returnReason.trim()} onClick={() => void mutate('shipment-return-failed-' + selected.id, '/api/v1/shipments/' + selected.id + '/return-initiate', { reasonCode: returnReason.trim(), note: returnNote.trim() || undefined, rowVersion: selected.rowVersion })}>RETURN_TO_WAREHOUSE</button>
          </div>
        </div>
      )}

      {['Delivered', 'Completed'].includes(selected.status) && (
        <div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Proof of Delivery</div>
          {selected.proofOfDelivery && (
            <p>
              Đã giao cho <strong>{selected.proofOfDelivery.receiverName}</strong> lúc {new Date(selected.proofOfDelivery.deliveredAt).toLocaleString('vi-VN')}.
              {selected.proofOfDelivery.evidenceReference && <> Evidence: {selected.proofOfDelivery.evidenceReference}.</>}
              {selected.proofOfDelivery.carrierReference && <> Carrier ref: {selected.proofOfDelivery.carrierReference}.</>}
            </p>
          )}
          {selected.status === 'Delivered' && canUpdate && (
            <button type="button" disabled={!!busy || !selected.proofOfDelivery} onClick={() => void mutate('shipment-complete-' + selected.id, '/api/v1/shipments/' + selected.id + '/complete', { rowVersion: selected.rowVersion })}>Hoàn tất Shipment</button>
          )}
        </div>
      )}

      {selected.status === 'ReturnToWarehouse' && (
        <div role="status">
          <strong>Return-to-warehouse đã được khởi tạo.</strong> Trạng thái này không cộng OnHand. Hàng chỉ quay lại tồn qua Return/Receiving/Post workflow riêng.
        </div>
      )}

      {selected.status === 'Completed' && (
        <div role="status">
          <strong>Shipment đã COMPLETED.</strong> POD đã được ghi nhận và không có inventory movement bổ sung sau Dispatch.
        </div>
      )}

      {(selected.trackingEvents ?? []).length > 0 && (
        <UiCard title="Lịch sử theo dõi vận chuyển">
          <UiTableScroll>
            <table aria-label="Shipment tracking timeline">
              <thead>
                <tr><th>Thời điểm</th><th>Sự kiện</th><th>Trạng thái</th><th>Nguồn</th><th>Lý do / ghi chú</th></tr>
              </thead>
              <tbody>
                {[...(selected.trackingEvents ?? [])].sort((a, b) => a.occurredAt.localeCompare(b.occurredAt)).map(event => (
                  <tr key={event.id}>
                    <td>{new Date(event.occurredAt).toLocaleString('vi-VN')}</td>
                    <td>{trackingEventLabels[event.eventType] ?? event.eventType}</td>
                    <td>{statusLabels[event.fromStatus] ?? event.fromStatus} → {statusLabels[event.toStatus] ?? event.toStatus}</td>
                    <td>{trackingSourceLabels[event.source] ?? event.source}</td>
                    <td>{[event.reasonCode, event.note].filter(Boolean).join(' • ') || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>
      )}
    </UiCard>
  );
}
