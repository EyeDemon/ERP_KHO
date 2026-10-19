import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { sanitizeScannerCode } from './productCatalog';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type ShipmentHu = {
  id:number; handlingUnitId:number; huCode:string; barcode:string; sscc?:string; type:string; status:string;
  sequence:number; contentQuantity:number; assignedAt:string; stagedAt?:string; loadedAt?:string;
};
type ShipmentTrackingEvent = {
  id:number; eventType:string; fromStatus:string; toStatus:string; occurredAt:string; recordedAt:string;
  source:string; sourceEventId?:string; reasonCode?:string; note?:string;
};
type ShipmentProofOfDelivery = {
  deliveredAt:string; receiverName:string; evidenceReference?:string; latitude?:number; longitude?:number;
  carrierReference?:string; deliveryNote?:string; createdAt:string;
};
type Shipment = {
  id:number; shipmentCode:string; packingSessionId:number; packingSessionCode:string; warehouseId:number; warehouseName:string;
  sourceType:string; sourceId?:number; sourceCode?:string; exportReceiptId?:number; exportReceiptCode?:string; status:string; stagingLocationId?:number; stagingLocationCode?:string;
  handlingUnitCount:number; loadedHandlingUnitCount:number; dockAppointmentId?:number; dockAppointmentCode?:string; dockId?:number; dockCode?:string; carrierName?:string; vehiclePlate?:string; trailerPlate?:string;
  sealNumber?:string; createdAt:string; stagedAt?:string; loadingStartedAt?:string; loadedAt?:string;
  dispatchedAt?:string; dispatchedBy?:number; dispatchedByName?:string; inTransitAt?:string; deliveryFailedAt?:string;
  returnInitiatedAt?:string; deliveredAt?:string; completedAt?:string; rowVersion?:string;
  proofOfDelivery?:ShipmentProofOfDelivery; trackingEvents?:ShipmentTrackingEvent[]; handlingUnits?:ShipmentHu[];
};
type DockAppointment = {
  id:number; code:string; warehouseId:number; direction:number|string; status:number|string; dockId?:number; dockCode?:string;
  vehiclePlate?:string; trailerPlate?:string; carrierName?:string;
};

const statusLabels:Record<string,string>={
  Draft:'Nháp',Ready:'Sẵn sàng',Staging:'Đang staging',Loading:'Đang loading',Loaded:'Đã load',
  Dispatched:'Đã dispatch',InTransit:'Đang vận chuyển',Delivered:'Đã giao',DeliveryFailed:'Giao thất bại',
  ReturnToWarehouse:'Đang trả về kho',Completed:'Hoàn tất',Cancelled:'Đã hủy',
};
const huLabels:Record<string,string>={
  InUse:'Đang đóng gói',Closed:'Đã đóng',Staged:'Đã staging',Loaded:'Đã load',Shipped:'Đã ship',
};
const huTypeLabels:Record<string,string>={
  Carton:'Thùng carton',Pallet:'Pallet',Tote:'Khay nhựa (Tote)',Box:'Hộp',
};
const trackingEventLabels:Record<string,string>={
  ShipmentCreated:'Khởi tạo chuyến giao hàng',
  Staged:'Đã đưa vào vị trí tập kết',
  ShipmentStaged:'Đã đưa vào vị trí tập kết',
  LoadingStarted:'Bắt đầu xếp hàng lên xe',
  HuLoaded:'Đã xếp hàng hoàn tất',
  Loaded:'Đã xếp hàng hoàn tất',
  LoadingCompleted:'Đã xếp hàng hoàn tất',
  Dispatched:'Đã xuất bến giao hàng',
  ShipmentDispatched:'Đã xuất bến giao hàng',
  InTransit:'Đang trên đường vận chuyển',
  Delivered:'Đã giao hàng thành công',
  DeliveryFailed:'Giao hàng không thành công',
  ReturnInitiated:'Khởi tạo hoàn trả về kho',
  ReturnToWarehouse:'Khởi tạo hoàn trả về kho',
};
const trackingSourceLabels:Record<string,string>={
  Internal:'Hệ thống nội bộ',
  CarrierWebhook:'Hãng vận chuyển',
  Manual:'Cập nhật thủ công',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  ['Loaded','Dispatched','Delivered','Completed'].includes(status)?'success':
  ['DeliveryFailed','Cancelled'].includes(status)?'danger':
  ['Staging','Loading','InTransit','ReturnToWarehouse'].includes(status)?'warning':'neutral';

const messageFor=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='SHIPMENT_DOCK_APPOINTMENT_INVALID') return response?.data?.message??'Outbound appointment chưa sẵn sàng để loading.';
  if(code==='SHIPMENT_HU_NOT_LOADED') return 'Chưa load đủ Handling Unit của Shipment.';
  if(code==='SHIPMENT_HU_NOT_STAGED') return 'Handling Unit chưa ở staging.';
  if(code==='SHIPMENT_STAGING_LOCATION_INVALID') return response?.data?.message??'Staging location không hợp lệ.';
  if(code==='SHIPMENT_HU_MISMATCH') return 'Handling Unit không thuộc Shipment này hoặc bạn đang quét HU con.';
  if(code==='SHIPMENT_ALLOCATION_MISMATCH') return response?.data?.message??'Allocation/Reservation không còn khớp Shipment.';
  if(code==='SHIPMENT_INVENTORY_MISMATCH') return response?.data?.message??'Inventory bucket không còn khớp Allocation.';
  if(code==='SHIPMENT_PICKING_NOT_READY') return response?.data?.message??'Picking chưa đủ điều kiện dispatch.';
  if(code==='SHIPMENT_SOURCE_STATE_INVALID') return response?.data?.message??'Chứng từ nguồn không còn ở trạng thái cho phép dispatch.';
  if(code==='HU_NOT_FOUND') return 'Không tìm thấy Handling Unit theo mã đã quét.';
  if(code==='HU_ALREADY_LOADED') return 'Handling Unit này đã được load.';
  if(code==='SHIPMENT_TRACKING_TIME_INVALID') return response?.data?.message??'Thời điểm tracking không hợp lệ.';
  if(code==='SHIPMENT_POD_EXISTS') return 'Shipment đã có Proof of Delivery.';
  if(code==='SHIPMENT_POD_REQUIRED') return 'Cần Proof of Delivery trước khi hoàn tất Shipment.';
  if(code==='SHIPMENT_NOT_DISPATCHED') return 'Shipment chưa qua inventory dispatch boundary.';
  if(code==='SHIPMENT_STATE_INVALID') return response?.data?.message??'Trạng thái Shipment không cho phép thao tác này.';
  if(code==='SHIPMENT_VERSION_CONFLICT'||response?.status===409) return response?.data?.message??'Shipment đã thay đổi. Vui lòng tải lại.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác Shipment này.';
  if(response?.status===404)return 'Không tìm thấy Shipment trong phạm vi kho của bạn.';
  return response?.data?.message??'Không thể xử lý Shipment.';
};

export default function Shipments(){
  const [rows,setRows]=useState<Shipment[]>([]);
  const [selected,setSelected]=useState<Shipment|null>(null);
  const [appointments,setAppointments]=useState<DockAppointment[]>([]);
  const [appointmentId,setAppointmentId]=useState('');
  const [stagingCode,setStagingCode]=useState('');
  const [huScan,setHuScan]=useState('');
  const [seal,setSeal]=useState('');
  const [receiverName,setReceiverName]=useState('');
  const [evidenceReference,setEvidenceReference]=useState('');
  const [carrierReference,setCarrierReference]=useState('');
  const [deliveryNote,setDeliveryNote]=useState('');
  const [failureReason,setFailureReason]=useState('');
  const [failureNote,setFailureNote]=useState('');
  const [returnReason,setReturnReason]=useState('');
  const [returnNote,setReturnNote]=useState('');
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState('');
  const [error,setError]=useState('');
  const [tmsManifestResult,setTmsManifestResult]=useState<{
    manifestId:string;
    carrierCode:string;
    syncedShipmentCount:number;
    ediStatus:string;
  }|null>(null);
  const [tmsBusy,setTmsBusy]=useState(false);
  const lock=useRef(new Set<string>());
  const canStage=usePermission('shipment.stage');
  const canShipmentLoad=usePermission('shipment.load');
  const canLoadingExecute=usePermission('loading.execute');
  const canDispatch=usePermission('shipment.dispatch');
  const canUpdate=usePermission('shipment.update');
  const canConfirmDelivery=usePermission('shipment.confirm_delivery');
  const canLoad=canShipmentLoad&&canLoadingExecute;

  const list=async()=>{
    setLoading(true);setError('');
    try{setRows((await apiClient.get('/api/shipments')).data)}
    catch(e){setRows([]);setSelected(null);setError(messageFor(e))}
    finally{setLoading(false)}
  };
  const detail=async(id:number)=>{
    setError('');
    try{
      const value=(await apiClient.get('/api/shipments/'+id)).data as Shipment;
      setSelected(value);
      setStagingCode(value.stagingLocationCode??'');
      setSeal(value.sealNumber??'');
      if(value.status==='Staging'&&canLoad){
        const response=await apiClient.get('/api/dock-yard/appointments',{params:{warehouseId:value.warehouseId,direction:1,status:5}});
        setAppointments(response.data);
      }else setAppointments([]);
    }catch(e){setSelected(null);setAppointments([]);setError(messageFor(e))}
  };
  useEffect(()=>{void list()},[]);

  const mutate=async(key:string,path:string,body:object)=>{
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');
    try{
      const value=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as Shipment;
      completeIdempotentAction(key);
      setSelected(value);
      setStagingCode(value.stagingLocationCode??stagingCode);
      setSeal(value.sealNumber??seal);
      setHuScan('');
      await list();
      if(value.status==='Staging'&&canLoad){
        const response=await apiClient.get('/api/dock-yard/appointments',{params:{warehouseId:value.warehouseId,direction:1,status:5}});
        setAppointments(response.data);
      }else setAppointments([]);
    }catch(e){
      if((e as {response?:{status?:number}})?.response?.status===409&&selected?.id) await detail(selected.id);
      setError(messageFor(e));
    }finally{lock.current.delete(key);setBusy('')}
  };

  if(loading&&rows.length===0)return <p role="status">Đang tải Shipment...</p>;

  const preDispatchCount=rows.filter(x=>['Ready','Staging','Loading','Loaded'].includes(x.status)).length;
  const inTransitCount=rows.filter(x=>['Dispatched','InTransit'].includes(x.status)).length;
  const deliveredOrCompletedCount=rows.filter(x=>['Delivered','Completed'].includes(x.status)).length;
  const exceptionOrReturnCount=rows.filter(x=>['DeliveryFailed','ReturnToWarehouse'].includes(x.status)).length;
  const stagingOrLoadingCount=preDispatchCount;
  const carrierAssignedCount=rows.filter(x=>Boolean(x.carrierName||x.vehiclePlate||x.dockAppointmentId||x.dockAppointmentCode)).length;
  const sealVerifiedCount=rows.filter(x=>Boolean(x.sealNumber)).length;

  return <UiPage>
    <UiPageHeader
      eyebrow="Xuất kho"
      title="Điều phối Vận chuyển & Xuất giao"
      description="Luồng vận chuyển: Sẵn sàng → Tập kết → Xếp hàng → Đã xếp → Xuất bến → Đang giao → Hoàn tất. Tồn kho chỉ trừ một lần duy nhất tại thời điểm Xuất bến."
    />
    {error&&<div role="alert">{error}</div>}
    <div className="ui-stack">
      <UiCard title="Giám sát Chu trình Xuất giao & Cam kết Giao hàng">
        <div className="ui-inline-wrap">
          <span>Chờ xuất bến: <strong>{preDispatchCount}</strong></span>
          <span>Đang vận chuyển: <strong>{inTransitCount}</strong></span>
          <span>Đã giao hoàn tất: <strong>{deliveredOrCompletedCount}</strong></span>
          <span>Ngoại lệ / Hoàn kho: <strong>{exceptionOrReturnCount}</strong></span>
        </div>
      </UiCard>
      <UiCard title="Lập kế hoạch Xếp tải Xe (AX-12) & Tích hợp Đơn vị Vận chuyển / TMS (IG-06)">
        <div className="ui-inline-wrap">
          <span>Chờ tập kết / xếp xe: <strong>{stagingOrLoadingCount}</strong></span>
          <span>Hãng vận chuyển: <strong>{carrierAssignedCount}</strong></span>
          <span>Đã niêm phong chì: <strong>{sealVerifiedCount}</strong></span>
          <span>Đồng bộ sự kiện TMS / POD: <strong>Đồng bộ Webhook &amp; biên bản giao hàng (POD) chống trùng lặp</strong></span>
        </div>
        <div className="ui-inline-actions">
          <button
            type="button"
            disabled={tmsBusy}
            onClick={()=>{
              if(tmsBusy)return;
              setTmsBusy(true);
              setError('');
              void apiClient
                .post<Record<string,unknown>>('/api/shipments/sync-tms-manifest',{
                  warehouseId:rows[0]?.warehouseId??undefined,
                  carrierCode:rows[0]?.carrierName??'VTP-EXPRESS',
                })
                .then(res=>{
                  const data=res?.data??{};
                  if(typeof data.manifestId==='string'&&data.manifestId){
                    setTmsManifestResult({
                      manifestId:data.manifestId,
                      carrierCode:typeof data.carrierCode==='string'&&data.carrierCode?data.carrierCode:'VTP-EXPRESS',
                      syncedShipmentCount:typeof data.syncedShipmentCount==='number'?data.syncedShipmentCount:0,
                      ediStatus:typeof data.ediStatus==='string'&&data.ediStatus?data.ediStatus:'SYNCED',
                    });
                  }else{
                    setTmsManifestResult(null);
                    setError('Không thể đồng bộ Manifest TMS: dữ liệu phản hồi không hợp lệ.');
                  }
                })
                .catch(e=>{
                  setTmsManifestResult(null);
                  setError(messageFor(e));
                })
                .finally(()=>setTmsBusy(false));
            }}
          >
            Đồng bộ Manifest & Ghép tải TMS (IG-06, AX-12)
          </button>
          {tmsManifestResult&&(
            <UiBadge tone="success">
              {`Manifest: ${tmsManifestResult.manifestId} • Carrier: ${tmsManifestResult.carrierCode} • Shipments: ${tmsManifestResult.syncedShipmentCount} • EDI: ${tmsManifestResult.ediStatus}`}
            </UiBadge>
          )}
        </div>
      </UiCard>
      <UiCard title="Danh sách chuyến giao hàng">
        {rows.length===0?<p>Chưa có Shipment. Shipment được tạo khi Packing đạt PACKED.</p>:<UiTableScroll>
          <table aria-label="Danh sách Shipment">
            <thead><tr><th>Mã chuyến</th><th>Đơn nguồn</th><th>Kho</th><th>Trạng thái</th><th>Số kiện</th><th>Cửa kho / Xe</th></tr></thead>
            <tbody>{rows.map(item=><tr key={item.id}>
              <td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void detail(item.id)}>{item.shipmentCode}</button></td>
              <td style={{whiteSpace:'nowrap'}}>{item.sourceCode??item.packingSessionCode}</td>
              <td>{item.warehouseName}</td>
              <td><UiBadge tone={tone(item.status)}>{statusLabels[item.status]??item.status}</UiBadge></td>
              <td>{item.loadedHandlingUnitCount}/{item.handlingUnitCount}</td>
              <td>{item.dockCode??'—'} {item.vehiclePlate?'• '+item.vehiclePlate:''}</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {selected&&<UiCard title={'Chi tiết '+selected.shipmentCode}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{statusLabels[selected.status]??selected.status}</UiBadge>
          <span className="ui-muted-text">{selected.packingSessionCode} • {selected.sourceCode??selected.sourceType} • {selected.warehouseName}</span>
          <span>Đã xếp: <strong>{selected.loadedHandlingUnitCount}</strong> / {selected.handlingUnitCount} kiện</span>
          {selected.stagingLocationCode&&<span>Vị trí tập kết: <strong>{selected.stagingLocationCode}</strong></span>}
          {selected.exportReceiptCode&&<UiBadge tone="success">{`Phiếu xuất kho: ${selected.exportReceiptCode}`}</UiBadge>}
        </div>
        <p className="ui-muted-text">
          Quản lý vận đơn xuyên suốt từ sẵn sàng, tập kết, xếp hàng lên xe, xuất bến đến xác nhận giao hàng. Xuất bến là thời điểm trừ tồn kho duy nhất; mọi trạng thái sau đó không làm thay đổi tồn kho thực tế.
        </p>

        <UiTableScroll>
          <table aria-label="Handling Unit của Shipment">
            <thead><tr><th>#</th><th>Mã kiện</th><th>Loại</th><th>Số lượng</th><th>Trạng thái</th></tr></thead>
            <tbody>{(selected.handlingUnits??[]).map(hu=><tr key={hu.id}>
              <td>{hu.sequence}</td>
              <td><strong style={{whiteSpace:'nowrap'}}>{hu.huCode}</strong><div className="ui-muted-text" style={{whiteSpace:'nowrap'}}>{hu.barcode}</div></td>
              <td>{huTypeLabels[hu.type]??hu.type}</td>
              <td>{hu.contentQuantity}</td>
              <td><UiBadge tone={tone(hu.status)}>{huLabels[hu.status]??hu.status}</UiBadge></td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>

        {selected.status==='Ready'&&canStage&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Đưa vào Khu vực Tập kết</div>
          <div className="ui-inline-wrap">
            <label>Quét hoặc nhập mã vị trí tập kết
              <input
                aria-label="Shipment staging location"
                value={stagingCode}
                onChange={e=>setStagingCode(e.target.value)}
                placeholder="Ví dụ STG-OUT-01"
                autoComplete="off"
              />
            </label>
            <button type="button" disabled={!!busy||!sanitizeScannerCode(stagingCode)} onClick={()=>void mutate(
              'shipment-stage-'+selected.id,'/api/shipments/'+selected.id+'/stage',
              {stagingLocationCode:sanitizeScannerCode(stagingCode),rowVersion:selected.rowVersion}
            )}>Xác nhận Tập kết</button>
          </div>
        </div>}

        {selected.status==='Staging'&&canLoad&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Bắt đầu Loading</div>
          <div className="ui-inline-wrap">
            <label>Outbound appointment IN_SERVICE
              <select aria-label="Outbound appointment loading" value={appointmentId} onChange={e=>setAppointmentId(e.target.value)}>
                <option value="">Chọn appointment</option>
                {appointments.map(item=><option key={item.id} value={item.id}>
                  {item.code} • {item.dockCode??'chưa dock'} • {item.vehiclePlate??'chưa xe'}
                </option>)}
              </select>
            </label>
            <button type="button" disabled={!!busy||!appointmentId} onClick={()=>void mutate(
              'shipment-start-loading-'+selected.id,'/api/shipments/'+selected.id+'/start-loading',
              {dockAppointmentId:Number(appointmentId),rowVersion:selected.rowVersion}
            )}>Bắt đầu Loading</button>
          </div>
        </div>}

        {selected.status==='Loading'&&canLoad&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Scan HU lên xe</div>
          <div className="ui-inline-wrap">
            <label>HU barcode / code / SSCC
              <input aria-label="Shipment HU scan" value={huScan} onChange={e=>setHuScan(e.target.value)} autoComplete="off"/>
            </label>
            <button type="button" disabled={!!busy||!sanitizeScannerCode(huScan)} onClick={()=>void mutate(
              'shipment-load-hu-'+selected.id+'-'+sanitizeScannerCode(huScan).toUpperCase(),'/api/shipments/'+selected.id+'/load-hu',
              {handlingUnitBarcode:sanitizeScannerCode(huScan),rowVersion:selected.rowVersion}
            )}>Xác nhận HU đã load</button>
          </div>
          <div className="ui-inline-wrap">
            <span>Dock: <strong>{selected.dockCode??'—'}</strong></span>
            <span>Xe: <strong>{selected.vehiclePlate??'—'}</strong></span>
            {selected.trailerPlate&&<span>Trailer: <strong>{selected.trailerPlate}</strong></span>}
          </div>
          <div className="ui-inline-wrap">
            <label>Seal number
              <input aria-label="Shipment seal number" value={seal} onChange={e=>setSeal(e.target.value)} placeholder="Không bắt buộc"/>
            </label>
            <button type="button" disabled={!!busy||selected.loadedHandlingUnitCount!==selected.handlingUnitCount} onClick={()=>void mutate(
              'shipment-complete-loading-'+selected.id,'/api/shipments/'+selected.id+'/complete-loading',
              {sealNumber:seal||undefined,rowVersion:selected.rowVersion}
            )}>Xác nhận LOADED</button>
          </div>
        </div>}

        {selected.status==='Loaded'&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Dispatch Shipment</div>
          <p>
            Shipment đã LOADED. Dispatch sẽ consume Reservation/Allocation, chuyển HU sang SHIPPED,
            ghi SHIP ledger theo Location và giảm OnHand. Thao tác này là inventory boundary.
            {selected.sealNumber&&<> Seal: {selected.sealNumber}.</>}
          </p>
          {canDispatch?<button type="button" disabled={!!busy} onClick={()=>void mutate(
            'shipment-dispatch-'+selected.id,'/api/shipments/'+selected.id+'/dispatch',
            {rowVersion:selected.rowVersion}
          )}>Dispatch Shipment</button>:<p className="ui-muted-text">Bạn không có quyền shipment.dispatch.</p>}
        </div>}

        {selected.status==='Dispatched'&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Post-dispatch tracking</div>
          <p>
            <strong>Shipment đã DISPATCHED.</strong> SHIP ledger đã được ghi và OnHand đã giảm tại inventory boundary này.
            {selected.dispatchedAt&&<> Thời điểm: {new Date(selected.dispatchedAt).toLocaleString('vi-VN')}.</>}
            {selected.dispatchedByName&&<> Người dispatch: {selected.dispatchedByName}.</>}
          </p>
          {canUpdate
            ? <button type="button" disabled={!!busy} onClick={()=>void mutate(
                'shipment-in-transit-'+selected.id,'/api/shipments/'+selected.id+'/mark-in-transit',
                {rowVersion:selected.rowVersion}
              )}>Chuyển IN_TRANSIT</button>
            : <p className="ui-muted-text">Bạn cần shipment.update để cập nhật trạng thái vận chuyển.</p>}
        </div>}

        {selected.status==='InTransit'&&<div className="ui-stack">
          {canConfirmDelivery&&<div className="ui-subcard ui-stack">
            <div className="ui-subcard-title">Xác nhận giao hàng / POD</div>
            <div className="ui-inline-wrap">
              <label>Người nhận
                <input aria-label="POD receiver name" value={receiverName} onChange={e=>setReceiverName(e.target.value)}/>
              </label>
              <label>Evidence reference
                <input aria-label="POD evidence reference" value={evidenceReference} onChange={e=>setEvidenceReference(e.target.value)} placeholder="Document/storage reference"/>
              </label>
              <label>Carrier reference
                <input aria-label="POD carrier reference" value={carrierReference} onChange={e=>setCarrierReference(e.target.value)}/>
              </label>
              <label>Ghi chú giao hàng
                <input aria-label="POD delivery note" value={deliveryNote} onChange={e=>setDeliveryNote(e.target.value)}/>
              </label>
              <button type="button" disabled={!!busy||!receiverName.trim()||(!evidenceReference.trim()&&!carrierReference.trim())} onClick={()=>void mutate(
                'shipment-delivery-confirm-'+selected.id,'/api/shipments/'+selected.id+'/delivery-confirm',
                {
                  receiverName:receiverName.trim(),
                  evidenceReference:evidenceReference.trim()||undefined,
                  carrierReference:carrierReference.trim()||undefined,
                  deliveryNote:deliveryNote.trim()||undefined,
                  rowVersion:selected.rowVersion
                }
              )}>Xác nhận DELIVERED</button>
            </div>
          </div>}

          {canUpdate&&<div className="ui-subcard ui-stack">
            <div className="ui-subcard-title">Delivery exception</div>
            <div className="ui-inline-wrap">
              <label>Mã lý do thất bại
                <input aria-label="Delivery failure reason" value={failureReason} onChange={e=>setFailureReason(e.target.value)} placeholder="CUSTOMER_UNAVAILABLE"/>
              </label>
              <label>Ghi chú
                <input aria-label="Delivery failure note" value={failureNote} onChange={e=>setFailureNote(e.target.value)}/>
              </label>
              <button type="button" disabled={!!busy||!failureReason.trim()} onClick={()=>void mutate(
                'shipment-delivery-failed-'+selected.id,'/api/shipments/'+selected.id+'/delivery-failed',
                {reasonCode:failureReason.trim(),note:failureNote.trim()||undefined,rowVersion:selected.rowVersion}
              )}>Ghi nhận DELIVERY_FAILED</button>
            </div>
            <div className="ui-inline-wrap">
              <label>Lý do trả về kho
                <input aria-label="Return to warehouse reason" value={returnReason} onChange={e=>setReturnReason(e.target.value)} placeholder="CUSTOMER_REFUSED"/>
              </label>
              <label>Ghi chú return
                <input aria-label="Return to warehouse note" value={returnNote} onChange={e=>setReturnNote(e.target.value)}/>
              </label>
              <button type="button" disabled={!!busy||!returnReason.trim()} onClick={()=>void mutate(
                'shipment-return-'+selected.id,'/api/shipments/'+selected.id+'/return-initiate',
                {reasonCode:returnReason.trim(),note:returnNote.trim()||undefined,rowVersion:selected.rowVersion}
              )}>Khởi tạo RETURN_TO_WAREHOUSE</button>
            </div>
          </div>}
        </div>}

        {selected.status==='DeliveryFailed'&&canUpdate&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Xử lý giao thất bại</div>
          <p>Retry/return chỉ thay đổi logistics workflow; không tạo thêm SHIP transaction.</p>
          <div className="ui-inline-wrap">
            <button type="button" disabled={!!busy} onClick={()=>void mutate(
              'shipment-retry-delivery-'+selected.id,'/api/shipments/'+selected.id+'/retry-delivery',
              {note:failureNote.trim()||undefined,rowVersion:selected.rowVersion}
            )}>Retry delivery</button>
            <label>Lý do trả về kho
              <input aria-label="Failed return reason" value={returnReason} onChange={e=>setReturnReason(e.target.value)} placeholder="CUSTOMER_REFUSED"/>
            </label>
            <button type="button" disabled={!!busy||!returnReason.trim()} onClick={()=>void mutate(
              'shipment-return-failed-'+selected.id,'/api/shipments/'+selected.id+'/return-initiate',
              {reasonCode:returnReason.trim(),note:returnNote.trim()||undefined,rowVersion:selected.rowVersion}
            )}>RETURN_TO_WAREHOUSE</button>
          </div>
        </div>}

        {['Delivered','Completed'].includes(selected.status)&&<div className="ui-subcard ui-stack">
          <div className="ui-subcard-title">Proof of Delivery</div>
          {selected.proofOfDelivery&&<p>
            Đã giao cho <strong>{selected.proofOfDelivery.receiverName}</strong> lúc {new Date(selected.proofOfDelivery.deliveredAt).toLocaleString('vi-VN')}.
            {selected.proofOfDelivery.evidenceReference&&<> Evidence: {selected.proofOfDelivery.evidenceReference}.</>}
            {selected.proofOfDelivery.carrierReference&&<> Carrier ref: {selected.proofOfDelivery.carrierReference}.</>}
          </p>}
          {selected.status==='Delivered'&&canUpdate&&<button type="button" disabled={!!busy||!selected.proofOfDelivery} onClick={()=>void mutate(
            'shipment-complete-'+selected.id,'/api/shipments/'+selected.id+'/complete',
            {rowVersion:selected.rowVersion}
          )}>Hoàn tất Shipment</button>}
        </div>}

        {selected.status==='ReturnToWarehouse'&&<div role="status">
          <strong>Return-to-warehouse đã được khởi tạo.</strong> Trạng thái này không cộng OnHand. Hàng chỉ quay lại tồn qua Return/Receiving/Post workflow riêng.
        </div>}

        {selected.status==='Completed'&&<div role="status">
          <strong>Shipment đã COMPLETED.</strong> POD đã được ghi nhận và không có inventory movement bổ sung sau Dispatch.
        </div>}

        {(selected.trackingEvents??[]).length>0&&<UiCard title="Lịch sử theo dõi vận chuyển">
          <UiTableScroll>
            <table aria-label="Shipment tracking timeline">
              <thead><tr><th>Thời điểm</th><th>Sự kiện</th><th>Trạng thái</th><th>Nguồn</th><th>Lý do / ghi chú</th></tr></thead>
              <tbody>{[...(selected.trackingEvents??[])].sort((a,b)=>a.occurredAt.localeCompare(b.occurredAt)).map(event=><tr key={event.id}>
                <td>{new Date(event.occurredAt).toLocaleString('vi-VN')}</td>
                <td>{trackingEventLabels[event.eventType]??event.eventType}</td>
                <td>{statusLabels[event.fromStatus]??event.fromStatus} → {statusLabels[event.toStatus]??event.toStatus}</td>
                <td>{trackingSourceLabels[event.source]??event.source}</td>
                <td>{[event.reasonCode,event.note].filter(Boolean).join(' • ')||'—'}</td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>}
      </UiCard>}
    </div>
  </UiPage>;
}
