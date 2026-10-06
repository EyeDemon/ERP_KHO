import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type ShipmentHu = {
  id:number; handlingUnitId:number; huCode:string; barcode:string; sscc?:string; type:string; status:string;
  sequence:number; contentQuantity:number; assignedAt:string; stagedAt?:string; loadedAt?:string;
};
type Shipment = {
  id:number; shipmentCode:string; packingSessionId:number; packingSessionCode:string; warehouseId:number; warehouseName:string;
  sourceType:string; sourceId?:number; sourceCode?:string; status:string; stagingLocationId?:number; stagingLocationCode?:string;
  handlingUnitCount:number; loadedHandlingUnitCount:number; dockAppointmentId?:number; dockAppointmentCode?:string; dockId?:number; dockCode?:string; vehiclePlate?:string; trailerPlate?:string;
  sealNumber?:string; createdAt:string; stagedAt?:string; loadingStartedAt?:string; loadedAt?:string; dispatchedAt?:string; dispatchedBy?:number; rowVersion?:string;
  handlingUnits?:ShipmentHu[];
};
type DockAppointment = {
  id:number; code:string; warehouseId:number; direction:number|string; status:number|string; dockId?:number; dockCode?:string;
  vehiclePlate?:string; trailerPlate?:string; carrierName?:string;
};

const statusLabels:Record<string,string>={
  Draft:'Nháp',Ready:'Sẵn sàng',Staging:'Đang staging',Loading:'Đang loading',Loaded:'Đã load',
  Dispatched:'Đã dispatch',InTransit:'Đang vận chuyển',Delivered:'Đã giao',DeliveryFailed:'Giao thất bại',Cancelled:'Đã hủy',
};
const huLabels:Record<string,string>={
  Closed:'Đã đóng',Staged:'Đã staging',Loaded:'Đã load',Shipped:'Đã ship',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  ['Loaded','Dispatched','Delivered'].includes(status)?'success':
  ['DeliveryFailed','Cancelled'].includes(status)?'danger':
  ['Staging','Loading'].includes(status)?'warning':'neutral';

const messageFor=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='SHIPMENT_DOCK_APPOINTMENT_INVALID') return response?.data?.message??'Outbound appointment chưa sẵn sàng để loading.';
  if(code==='SHIPMENT_HU_NOT_LOADED') return 'Chưa load đủ Handling Unit của Shipment.';
  if(code==='SHIPMENT_HU_NOT_STAGED') return 'Handling Unit chưa ở staging.';
  if(code==='SHIPMENT_STAGING_LOCATION_INVALID') return response?.data?.message??'Staging location không hợp lệ.';
  if(code==='SHIPMENT_HU_MISMATCH') return 'Handling Unit không thuộc Shipment này hoặc bạn đang quét HU con.';
  if(code==='HU_NOT_FOUND') return 'Không tìm thấy Handling Unit theo mã đã quét.';
  if(code==='HU_ALREADY_LOADED') return 'Handling Unit này đã được load.';
  if(code==='SHIPMENT_ALLOCATION_INVALID') return response?.data?.message??'Allocation/Picked quantity không còn khớp để dispatch.';
  if(code==='SHIPMENT_PACKING_MISMATCH') return response?.data?.message??'Packing content không còn khớp số lượng đã Picking.';
  if(code==='SHIPMENT_RESERVATION_INVALID') return response?.data?.message??'Reservation không thuộc Shipment hoặc đã thay đổi.';
  if(code==='SHIPMENT_SOURCE_INVALID'||code==='SHIPMENT_SOURCE_STATE_INVALID') return response?.data?.message??'Chứng từ nguồn không còn hợp lệ để dispatch.';
  if(code==='SHIPMENT_BACKORDER_NOT_SUPPORTED') return response?.data?.message??'Shipment còn Short Pick cần xử lý ở Backorder.';
  if(code==='SHIPMENT_LEDGER_EXISTS') return 'Shipment đã có SHIP ledger. Hãy đối soát trước khi retry.';
  if(code==='SHIPMENT_VERSION_CONFLICT'||response?.status===409) return response?.data?.message??'Shipment đã thay đổi. Vui lòng tải lại.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác Shipment này.';
  if(response?.status===404)return 'Không tìm thấy Shipment trong phạm vi kho của bạn.';
  return response?.data?.message??'Không thể xử lý Staging & Loading.';
};

export default function Shipments(){
  const [rows,setRows]=useState<Shipment[]>([]);
  const [selected,setSelected]=useState<Shipment|null>(null);
  const [appointments,setAppointments]=useState<DockAppointment[]>([]);
  const [appointmentId,setAppointmentId]=useState('');
  const [stagingCode,setStagingCode]=useState('');
  const [huScan,setHuScan]=useState('');
  const [seal,setSeal]=useState('');
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState('');
  const [error,setError]=useState('');
  const lock=useRef(new Set<string>());
  const canStage=usePermission('shipment.stage');
  const canShipmentLoad=usePermission('shipment.load');
  const canLoadingExecute=usePermission('loading.execute');
  const canDispatch=usePermission('shipment.dispatch');
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

  if(loading)return <p role="status">Đang tải Shipment...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Outbound"
      title="Staging, Loading & Dispatch"
      description="Shipment READY → STAGING → LOADING → LOADED → DISPATCHED. Chỉ Dispatch là posting boundary trừ OnHand, consume Reservation/Allocation và ghi SHIP ledger."
    />
    {error&&<div role="alert">{error}</div>}
    <div className="ui-stack">
      <UiCard title="Shipment">
        {rows.length===0?<p>Chưa có Shipment. Shipment được tạo khi Packing đạt PACKED.</p>:<UiTableScroll>
          <table aria-label="Danh sách Shipment">
            <thead><tr><th>Shipment</th><th>Nguồn</th><th>Kho</th><th>Trạng thái</th><th>HU</th><th>Dock / xe</th></tr></thead>
            <tbody>{rows.map(item=><tr key={item.id}>
              <td><button type="button" onClick={()=>void detail(item.id)}>{item.shipmentCode}</button></td>
              <td>{item.sourceCode??item.packingSessionCode}</td>
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
          <span>HU loaded <strong>{selected.loadedHandlingUnitCount}</strong> / {selected.handlingUnitCount}</span>
          {selected.stagingLocationCode&&<span>Staging: <strong>{selected.stagingLocationCode}</strong></span>}
        </div>
        <p className="ui-muted-text">
          Foundation hiện quản lý Shipment READY/STAGING/LOADING/LOADED/DISPATCHED, staging lane, root-HU ownership, dock/vehicle/seal context và canonical Dispatch.
          Dispatch consume Reservation/Allocation, trừ OnHand đúng một lần, ghi SHIP ledger + outbox. Backorder, load optimization/capacity và carrier/POD vẫn chưa hoàn tất.
        </p>

        <UiTableScroll>
          <table aria-label="Handling Unit của Shipment">
            <thead><tr><th>#</th><th>HU</th><th>Loại</th><th>Quantity</th><th>Trạng thái</th></tr></thead>
            <tbody>{(selected.handlingUnits??[]).map(hu=><tr key={hu.id}>
              <td>{hu.sequence}</td>
              <td><strong>{hu.huCode}</strong><div className="ui-muted-text">{hu.barcode}</div></td>
              <td>{hu.type}</td>
              <td>{hu.contentQuantity}</td>
              <td><UiBadge tone={tone(hu.status)}>{huLabels[hu.status]??hu.status}</UiBadge></td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>

        {selected.status==='Ready'&&canStage&&<fieldset>
          <legend>Đưa vào Staging</legend>
          <div className="ui-inline-wrap">
            <label>Quét / nhập staging location
              <input
                aria-label="Shipment staging location"
                value={stagingCode}
                onChange={e=>setStagingCode(e.target.value)}
                placeholder="Ví dụ STG-OUT-01"
                autoComplete="off"
              />
            </label>
            <button type="button" disabled={!!busy||!stagingCode.trim()} onClick={()=>void mutate(
              'shipment-stage-'+selected.id,'/api/shipments/'+selected.id+'/stage',
              {stagingLocationCode:stagingCode.trim(),rowVersion:selected.rowVersion}
            )}>Xác nhận Staging</button>
          </div>
        </fieldset>}

        {selected.status==='Staging'&&canLoad&&<fieldset>
          <legend>Bắt đầu Loading</legend>
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
        </fieldset>}

        {selected.status==='Loading'&&canLoad&&<fieldset>
          <legend>Scan HU lên xe</legend>
          <div className="ui-inline-wrap">
            <label>HU barcode / code / SSCC
              <input aria-label="Shipment HU scan" value={huScan} onChange={e=>setHuScan(e.target.value)} autoComplete="off"/>
            </label>
            <button type="button" disabled={!!busy||!huScan.trim()} onClick={()=>void mutate(
              'shipment-load-hu-'+selected.id+'-'+huScan.trim().toUpperCase(),'/api/shipments/'+selected.id+'/load-hu',
              {handlingUnitBarcode:huScan.trim(),rowVersion:selected.rowVersion}
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
        </fieldset>}

        {selected.status==='Loaded'&&<fieldset>
          <legend>Dispatch Shipment</legend>
          <div className="ui-inline-wrap">
            <span><strong>Shipment đã LOADED.</strong> OnHand vẫn chưa bị trừ cho tới khi Dispatch.</span>
            {selected.sealNumber&&<span>Seal: <strong>{selected.sealNumber}</strong></span>}
          </div>
          {canDispatch?<button type="button" disabled={!!busy} onClick={()=>void mutate(
            'shipment-dispatch-'+selected.id,'/api/shipments/'+selected.id+'/dispatch',
            {rowVersion:selected.rowVersion}
          )}>Xác nhận Dispatch</button>:<span className="ui-muted-text">Cần quyền shipment.dispatch để xác nhận xuất kho.</span>}
        </fieldset>}

        {selected.status==='Dispatched'&&<div role="status">
          <strong>Shipment đã DISPATCHED.</strong> SHIP ledger đã được ghi và inventory đã được consume.
          {selected.dispatchedAt&&<> Thời điểm: {new Date(selected.dispatchedAt).toLocaleString('vi-VN')}.</>}
        </div>}
      </UiCard>}
    </div>
  </UiPage>;
}
