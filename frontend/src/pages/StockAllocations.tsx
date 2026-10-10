import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Layers3, RefreshCw, Repeat2, Unlock } from 'lucide-react';
import AccessibleDialog from '../components/AccessibleDialog';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import {
  UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader,
  UiTableScroll, UiToolbar, UiToolbarField,
} from '../ui/ProductionUi';

type Allocation = {
  id:number; allocationCode:string; reservationId:number; reservationCode:string; sourceType:string; sourceCode?:string;
  warehouseId:number; warehouseName:string; productId:number; productCode:string; productName:string;
  locationId:number; locationCode:string; locationName:string; inventoryStatus:string; quantity:number;
  status:string; strategy:string; selectionReason:string; allocatedAt:string; releasedAt?:string; releaseReason?:string;
};
type AllocationPage={items:Allocation[];totalRecords:number;pageIndex:number;pageSize:number};
type AllocatableReservation={
  reservationId:number;reservationCode:string;sourceType:string;sourceCode?:string;warehouseId:number;warehouseName:string;
  productId:number;productCode:string;productName:string;reservedQuantity:number;allocatedQuantity:number;
  allocatableQuantity:number;expiresAt:string;
};
type Candidate={
  locationId:number;locationCode:string;locationName:string;reservedQuantity:number;allocatedQuantity:number;
  allocatableQuantity:number;rank:number;reason:string;
};
type ActionState={kind:'release'|'reallocate';allocation:Allocation;trigger:HTMLElement|null};

const emptyPage:AllocationPage={items:[],totalRecords:0,pageIndex:1,pageSize:20};
const messageOf=(failure:unknown,fallback:string)=>{
  const response=failure as {response?:{data?:{message?:string}}};
  return response.response?.data?.message||fallback;
};
const toneOf=(status:string):'neutral'|'success'|'warning'|'danger'=> {
  if(status==='Consumed'||status==='Picked') return 'success';
  if(status==='Active'||status==='Picking') return 'warning';
  return 'neutral';
};

export default function StockAllocations(){
  const canCreate=usePermission('allocation.create');
  const canRelease=usePermission('allocation.release');
  const canReallocate=usePermission('allocation.reallocate');
  const submitting=useRef(false);
  const [data,setData]=useState<AllocationPage>(emptyPage);
  const [reservations,setReservations]=useState<AllocatableReservation[]>([]);
  const [candidates,setCandidates]=useState<Candidate[]>([]);
  const [status,setStatus]=useState('');
  const [selectedReservationId,setSelectedReservationId]=useState<number|''>('');
  const [quantity,setQuantity]=useState('');
  const [locationId,setLocationId]=useState<number|''>('');
  const [loading,setLoading]=useState(true);
  const [referenceLoading,setReferenceLoading]=useState(false);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [action,setAction]=useState<ActionState|null>(null);
  const [reason,setReason]=useState('');
  const [actionLocationId,setActionLocationId]=useState<number|''>('');
  const [actionCandidates,setActionCandidates]=useState<Candidate[]>([]);

  const selectedReservation=useMemo(
    ()=>reservations.find(item=>item.reservationId===selectedReservationId),
    [reservations,selectedReservationId],
  );

  const loadPage=useCallback(async(page=1)=>{
    setLoading(true);setError('');
    try{
      const response=await apiClient.get('/api/inventory/allocations',{params:{page,pageSize:20,status:status||undefined}});
      setData(response.data);
    }catch(failure){setError(messageOf(failure,'Không thể tải danh sách Allocation.'))}
    finally{setLoading(false)}
  },[status]);

  const loadReservations=useCallback(async()=>{
    setReferenceLoading(true);
    try{
      const response=await apiClient.get('/api/inventory/allocations/reservations');
      const items=response.data as AllocatableReservation[];
      setReservations(items);
      setSelectedReservationId(current=>current&&items.some(item=>item.reservationId===current)?current:(items[0]?.reservationId??''));
    }catch(failure){setError(messageOf(failure,'Không thể tải reservation có thể Allocation.'))}
    finally{setReferenceLoading(false)}
  },[]);

  useEffect(()=>{void loadPage(1)},[loadPage]);
  useEffect(()=>{void loadReservations()},[loadReservations]);
  useEffect(()=>{
    if(!selectedReservationId){setCandidates([]);setLocationId('');setQuantity('');return}
    let active=true;setReferenceLoading(true);
    apiClient.get('/api/inventory/allocations/candidates',{params:{reservationId:selectedReservationId}})
      .then(response=>{if(active){setCandidates(response.data);setLocationId('')}})
      .catch(failure=>{if(active)setError(messageOf(failure,'Không thể tải vị trí có thể Allocation.'))})
      .finally(()=>{if(active)setReferenceLoading(false)});
    return()=>{active=false};
  },[selectedReservationId]);
  useEffect(()=>{if(selectedReservation)setQuantity(String(selectedReservation.allocatableQuantity))},[selectedReservation]);

  const refresh=async()=>{await Promise.all([loadPage(data.pageIndex),loadReservations()])};

  const allocate=async(manual:boolean)=>{
    if(submitting.current||!selectedReservation)return;
    const parsed=Number(quantity);
    if(!Number.isFinite(parsed)||parsed<=0||parsed>selectedReservation.allocatableQuantity){
      setError('Số lượng Allocation phải lớn hơn 0 và không vượt phần reservation còn có thể phân bổ.');return;
    }
    if(manual&&!locationId){setError('Hãy chọn vị trí khi Allocation thủ công.');return}
    submitting.current=true;setBusy(true);setError('');
    const logical=`allocation:${manual?'manual':'auto'}:${selectedReservation.reservationId}:${parsed}:${manual?locationId:'auto'}`;
    try{
      await apiClient.post(manual?'/api/inventory/allocations':'/api/inventory/allocations/auto',{
        reservationId:selectedReservation.reservationId,quantity:parsed,locationId:manual?locationId:null,
      },{headers:idempotencyHeaders(logical)});
      completeIdempotentAction(logical);await refresh();
    }catch(failure){setError(messageOf(failure,'Không thể tạo Allocation.'))}
    finally{submitting.current=false;setBusy(false)}
  };

  const openAction=async(kind:ActionState['kind'],allocation:Allocation,trigger:HTMLElement)=>{
    setError('');setReason('');setActionLocationId('');setActionCandidates([]);setAction({kind,allocation,trigger});
    if(kind==='reallocate'){
      try{
        const response=await apiClient.get('/api/inventory/allocations/candidates',{params:{reservationId:allocation.reservationId}});
        setActionCandidates((response.data as Candidate[]).filter(item=>item.locationId!==allocation.locationId));
      }catch(failure){setError(messageOf(failure,'Không thể tải vị trí để phân bổ lại.'))}
    }
  };

  const submitAction=async()=>{
    if(!action||submitting.current)return;
    const cleaned=reason.trim();if(!cleaned){setError('Lý do là bắt buộc.');return}
    submitting.current=true;setBusy(true);setError('');
    const logical=`allocation:${action.kind}:${action.allocation.id}:${action.kind==='reallocate'?(actionLocationId||'auto'):'release'}`;
    try{
      if(action.kind==='release'){
        await apiClient.post(`/api/inventory/allocations/${action.allocation.id}/release`,{reason:cleaned},{headers:idempotencyHeaders(logical)});
      }else{
        await apiClient.post(`/api/inventory/allocations/${action.allocation.id}/reallocate`,
          {locationId:actionLocationId||null,reason:cleaned},{headers:idempotencyHeaders(logical)});
      }
      completeIdempotentAction(logical);setAction(null);setReason('');setActionLocationId('');await refresh();
    }catch(failure){setError(messageOf(failure,action.kind==='release'?'Không thể giải phóng Allocation.':'Không thể phân bổ lại Allocation.'))}
    finally{submitting.current=false;setBusy(false)}
  };

  const pages=Math.max(1,Math.ceil(data.totalRecords/data.pageSize));
  const activeQuantity=data.items.filter(item=>['Active','Picking','Picked'].includes(item.status)).reduce((sum,item)=>sum+item.quantity,0);

  return <UiPage>
    <UiPageHeader eyebrow="Outbound" title="Allocation theo vị trí"
      description="Gắn phần reservation đã giữ vào vị trí pickable cụ thể. Allocation không làm giảm Available lần thứ hai; OnHand chỉ thay đổi khi dispatch/consume."
      actions={<button type="button" disabled={loading||referenceLoading||busy} onClick={()=>void refresh()}><RefreshCw size={16} aria-hidden="true"/> Làm mới</button>}
    />
    {error&&<p role="alert">{error}</p>}
    <UiMetricGrid>
      <UiMetric value={data.totalRecords} label="Allocation phù hợp"/>
      <UiMetric value={activeQuantity} label="Qty đang cam kết trên trang"/>
      <UiMetric value={reservations.length} label="Reservation còn có thể Allocation"/>
    </UiMetricGrid>

    {canCreate&&<UiCard title="Tạo Allocation">
      <p className="ui-muted-text">Tự động hiện dùng thứ tự Location ổn định. FEFO/FIFO, Lot, Serial và Owner chưa được bật trong foundation này.</p>
      <UiToolbar>
        <UiToolbarField label="Reservation">
          <select aria-label="Chọn reservation để Allocation" value={selectedReservationId} disabled={referenceLoading||busy}
            onChange={event=>setSelectedReservationId(event.target.value?Number(event.target.value):'')}>
            <option value="">Chọn reservation</option>
            {reservations.map(item=><option key={item.reservationId} value={item.reservationId}>{item.reservationCode} • {item.productCode} • còn {item.allocatableQuantity}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Số lượng">
          <input aria-label="Số lượng Allocation" type="number" min="0.0001" step="0.0001" value={quantity}
            disabled={!selectedReservation||busy} onChange={event=>setQuantity(event.target.value)}/>
        </UiToolbarField>
        <UiToolbarField label="Vị trí thủ công">
          <select aria-label="Chọn vị trí Allocation thủ công" value={locationId} disabled={!selectedReservation||referenceLoading||busy}
            onChange={event=>setLocationId(event.target.value?Number(event.target.value):'')}>
            <option value="">Chọn vị trí</option>
            {candidates.map(item=><option key={item.locationId} value={item.locationId}>#{item.rank} {item.locationCode} • khả dụng {item.allocatableQuantity}</option>)}
          </select>
        </UiToolbarField>
        <button type="button" disabled={!selectedReservation||busy||referenceLoading} onClick={()=>void allocate(false)}><Layers3 size={16} aria-hidden="true"/> Tự động</button>
        <button type="button" disabled={!selectedReservation||!locationId||busy||referenceLoading} onClick={()=>void allocate(true)}><Layers3 size={16} aria-hidden="true"/> Theo vị trí</button>
      </UiToolbar>
      {selectedReservation&&<p className="ui-muted-text" role="status">
        {selectedReservation.reservationCode} • {selectedReservation.warehouseName} • {selectedReservation.productCode} – {selectedReservation.productName}
        {' '}• Reserved {selectedReservation.reservedQuantity} • Đã Allocation {selectedReservation.allocatedQuantity}
        {' '}• Còn {selectedReservation.allocatableQuantity} • Hết hạn {new Date(selectedReservation.expiresAt).toLocaleString('vi-VN')}
      </p>}
    </UiCard>}

    <UiToolbar>
      <UiToolbarField label="Trạng thái">
        <select aria-label="Lọc trạng thái Allocation" value={status} onChange={event=>setStatus(event.target.value)}>
          <option value="">Tất cả</option>
          {['Active','Picking','Picked','Consumed','Released','Reallocated'].map(value=><option key={value} value={value}>{value}</option>)}
        </select>
      </UiToolbarField>
      <div className="ui-muted-text ui-auto-actions">Trang {data.pageIndex}/{pages} • {data.totalRecords} bản ghi</div>
    </UiToolbar>

    <UiCard title="Danh sách Allocation">
      <UiTableScroll><table aria-label="Danh sách Allocation">
        <thead><tr><th>Mã Allocation</th><th>Reservation / nguồn</th><th>Sản phẩm</th><th>Kho / vị trí</th><th>Qty</th><th>Chiến lược</th><th>Trạng thái</th><th>Lý do chọn</th>{(canRelease||canReallocate)&&<th>Thao tác</th>}</tr></thead>
        <tbody>
          {loading?<tr><td className="ui-empty-cell" colSpan={(canRelease||canReallocate)?9:8}>Đang tải...</td></tr>:
          data.items.length===0?<tr><td className="ui-empty-cell" colSpan={(canRelease||canReallocate)?9:8}>Không có Allocation phù hợp.</td></tr>:
          data.items.map(item=><tr key={item.id}>
            <td><strong>{item.allocationCode}</strong><div className="ui-muted-text">{new Date(item.allocatedAt).toLocaleString('vi-VN')}</div></td>
            <td>{item.reservationCode}<div className="ui-muted-text">{item.sourceType}{item.sourceCode?` / ${item.sourceCode}`:''}</div></td>
            <td>{item.productCode} – {item.productName}</td>
            <td>{item.warehouseName}<div className="ui-muted-text">{item.locationCode} – {item.locationName}</div></td>
            <td><strong>{item.quantity}</strong></td><td>{item.strategy}</td><td><UiBadge tone={toneOf(item.status)}>{item.status}</UiBadge></td><td>{item.selectionReason||'—'}</td>
            {(canRelease||canReallocate)&&<td>{item.status==='Active'?<div className="ui-auto-actions">
              {canRelease&&<button type="button" disabled={busy} onClick={event=>void openAction('release',item,event.currentTarget)}><Unlock size={16} aria-hidden="true"/> Giải phóng</button>}
              {canReallocate&&<button type="button" disabled={busy} onClick={event=>void openAction('reallocate',item,event.currentTarget)}><Repeat2 size={16} aria-hidden="true"/> Phân bổ lại</button>}
            </div>:'—'}</td>}
          </tr>)}
        </tbody>
      </table></UiTableScroll>
      {pages>1&&<div className="ui-pagination" aria-label="Phân trang Allocation">
        <button type="button" disabled={loading||data.pageIndex<=1} onClick={()=>void loadPage(data.pageIndex-1)}>Trước</button>
        <span>Trang {data.pageIndex} / {pages}</span>
        <button type="button" disabled={loading||data.pageIndex>=pages} onClick={()=>void loadPage(data.pageIndex+1)}>Sau</button>
      </div>}
    </UiCard>

    {action&&<AccessibleDialog titleId="allocation-action-title" busy={busy} returnFocusTo={action.trigger} onClose={()=>setAction(null)}>
      <h2 id="allocation-action-title">{action.kind==='release'?'Giải phóng Allocation':'Phân bổ lại Allocation'}</h2>
      <p><strong>{action.allocation.allocationCode}</strong> • {action.allocation.productCode} • {action.allocation.quantity} tại {action.allocation.locationCode}</p>
      {action.kind==='reallocate'&&<label>Vị trí mới
        <select aria-label="Vị trí phân bổ lại" value={actionLocationId} disabled={busy} onChange={event=>setActionLocationId(event.target.value?Number(event.target.value):'')}>
          <option value="">Tự động chọn vị trí khác</option>
          {actionCandidates.map(item=><option key={item.locationId} value={item.locationId}>{item.locationCode} • còn {item.allocatableQuantity}</option>)}
        </select>
      </label>}
      <label>Lý do<textarea data-initial-focus aria-label="Lý do thao tác Allocation" value={reason} disabled={busy} onChange={event=>setReason(event.target.value)}/></label>
      <div className="approval-dialog-actions">
        <button type="button" disabled={busy} onClick={()=>setAction(null)}>Hủy</button>
        <button type="button" disabled={busy||!reason.trim()} onClick={()=>void submitAction()}>{busy?'Đang xử lý...':'Xác nhận'}</button>
      </div>
    </AccessibleDialog>}
  </UiPage>;
}
