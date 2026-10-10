import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { currentUserId, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type PickingLine = {
  id:number; allocationId:number; allocationCode:string; productId:number; productCode:string; productName:string;
  sourceLocationId:number; sourceLocationCode:string; sourceLocationName:string; requestedQuantity:number;
  pickedQuantity:number; remainingQuantity:number; sequence:number; status:string;
};
type ShortPick = {
  id:number; pickingTaskLineId:number; expectedQuantity:number; pickedQuantity:number; shortageQuantity:number;
  reason:string; resolutionType?:string; status:string; createdAt:string; resolvedAt?:string; resolutionNote?:string;
};
type PickingTask = {
  id:number; taskCode:string; sourceType:string; sourceId?:number; sourceCode?:string; warehouseId:number; warehouseName:string;
  pickingType:string; status:string; priority:number; assignedUserId?:number; assignedUserName?:string; requestedQuantity:number;
  pickedQuantity:number; remainingQuantity:number; createdAt:string; startedAt?:string; completedAt?:string; rowVersion?:string;
  lines?:PickingLine[]; shortPicks?:ShortPick[];
};

const states:Record<string,string>={
  Open:'Chưa phân công',Assigned:'Đã phân công',InProgress:'Đang Picking',ShortPick:'Thiếu hàng',
  Resolved:'Đã xử lý thiếu',Completed:'Hoàn tất',Cancelled:'Đã hủy',
};
const lineStates:Record<string,string>={
  Open:'Chưa bắt đầu',InProgress:'Đang lấy',Picked:'Đã lấy đủ',ShortPick:'Thiếu hàng',Resolved:'Đã xử lý',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Completed'||status==='Picked'?'success':status==='ShortPick'?'danger':status==='InProgress'||status==='Assigned'?'warning':'neutral';

const failureMessage=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='PICK_WRONG_LOCATION')return 'Sai vị trí lấy hàng. Hãy quét đúng location được phân công.';
  if(code==='PICK_WRONG_PRODUCT')return 'Sai sản phẩm. Hãy quét đúng SKU/barcode của dòng Picking.';
  if(code==='PICK_WRONG_LOT'||code==='PICK_WRONG_SERIAL')return response?.data?.message??'Lot/Serial chưa hợp lệ.';
  if(code==='PICK_SHORT')return response?.data?.message??'Short Pick cần được xử lý trước khi tiếp tục.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác này hoặc task đã được giao cho người khác.';
  if(response?.status===404)return 'Không tìm thấy nhiệm vụ Picking hoặc bạn không có quyền truy cập.';
  if(response?.status===409)return response.data?.message??'Dữ liệu Picking đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message??'Không thể xử lý nhiệm vụ Picking.';
};

export default function PickingTasks(){
  const [tasks,setTasks]=useState<PickingTask[]>([]);
  const [selected,setSelected]=useState<PickingTask|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState('');
  const [scan,setScan]=useState<Record<number,{location:string;product:string;quantity:string}>>({});
  const [shortReason,setShortReason]=useState<Record<number,string>>({});
  const [resolution,setResolution]=useState<Record<number,string>>({});
  const [resolutionNote,setResolutionNote]=useState<Record<number,string>>({});
  const lock=useRef(new Set<string>());
  const canAssign=usePermission('picking.assign');
  const canExecute=usePermission('picking.execute');
  const canShortPick=usePermission('picking.short_pick');
  const canOverride=usePermission('picking.override');

  const load=async()=>{
    setLoading(true);setError('');
    try{setTasks((await apiClient.get('/api/picking-tasks')).data)}
    catch(e){setTasks([]);setSelected(null);setError(failureMessage(e))}
    finally{setLoading(false)}
  };
  const detail=async(id:number)=>{
    if(!id)return;
    setError('');
    try{setSelected((await apiClient.get(`/api/picking-tasks/${id}`)).data)}
    catch(e){setSelected(null);setError(failureMessage(e))}
  };
  useEffect(()=>{void load()},[]);

  const mutate=async(key:string,path:string,body:object)=>{
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');
    try{
      const task=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as PickingTask;
      completeIdempotentAction(key);
      setSelected(task);
      await load();
    }catch(e){
      if((e as {response?:{status?:number}})?.response?.status===409&&selected?.id) await detail(selected.id);
      setError(failureMessage(e));
    }finally{lock.current.delete(key);setBusy('')}
  };

  if(loading)return <p role="status">Đang tải nhiệm vụ Picking...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Outbound"
      title="Picking"
      description="Scan-first Picking từ Allocation theo vị trí. Xác nhận Picking không trừ OnHand; tồn chỉ giảm tại dispatch/consume."
    />
    {error&&<div role="alert">{error}</div>}

    <div className="ui-stack">
      <UiCard title="Danh sách nhiệm vụ Picking">
        {tasks.length===0?<p>Chưa có nhiệm vụ Picking phù hợp.</p>:<UiTableScroll>
          <table aria-label="Danh sách nhiệm vụ Picking">
            <thead><tr><th>Nhiệm vụ</th><th>Nguồn</th><th>Kho</th><th>Trạng thái</th><th>Tiến độ</th></tr></thead>
            <tbody>{tasks.map(task=><tr key={task.id}>
              <td><button type="button" onClick={()=>void detail(task.id)}>{task.taskCode}</button></td>
              <td>{task.sourceCode??task.sourceType}</td>
              <td>{task.warehouseName}</td>
              <td><UiBadge tone={tone(task.status)}>{states[task.status]??task.status}</UiBadge></td>
              <td><strong>{task.pickedQuantity}</strong> / {task.requestedQuantity}</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {selected&&<UiCard title={`Chi tiết ${selected.taskCode}`}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{states[selected.status]??selected.status}</UiBadge>
          <span className="ui-muted-text">{selected.sourceCode??selected.sourceType} • {selected.warehouseName}</span>
          <span className="ui-muted-text">Tiến độ {selected.pickedQuantity}/{selected.requestedQuantity}</span>
        </div>
        <p className="ui-muted-text">
          Foundation hiện xác thực location + product + quantity. Lot/Serial, destination Tote/HU và Replenishment chưa được bật.
        </p>

        <div className="ui-inline-wrap">
          {canAssign&&selected.status==='Open'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-assign-${selected.id}`,`/api/picking-tasks/${selected.id}/assign`,
            {assignedUserId:currentUserId(),rowVersion:selected.rowVersion}
          )}>Nhận nhiệm vụ</button>}
          {canExecute&&selected.status==='Assigned'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-start-${selected.id}`,`/api/picking-tasks/${selected.id}/start`,
            {rowVersion:selected.rowVersion}
          )}>Bắt đầu Picking</button>}
          {canExecute&&['InProgress','Resolved'].includes(selected.status)&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-complete-${selected.id}`,`/api/picking-tasks/${selected.id}/complete`,
            {rowVersion:selected.rowVersion}
          )}>Hoàn tất Picking</button>}
        </div>

        <div className="ui-stack">
          {(selected.lines??[]).map(line=><fieldset key={line.id}>
            <legend>{line.productCode} — {line.productName}</legend>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(line.status)}>{lineStates[line.status]??line.status}</UiBadge>
              <UiBadge>Vị trí: {line.sourceLocationCode}</UiBadge>
              <span>Đã lấy <strong>{line.pickedQuantity}</strong> / {line.requestedQuantity} • Còn {line.remainingQuantity}</span>
            </div>

            {canExecute&&selected.status==='InProgress'&&['Open','InProgress'].includes(line.status)&&line.remainingQuantity>0&&
              <div className="ui-inline-wrap">
                <label>Quét location
                  <input aria-label={`Location barcode ${line.productCode}`} value={scan[line.id]?.location??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{product:'',quantity:''}),location:e.target.value}}))}/>
                </label>
                <label>Quét sản phẩm
                  <input aria-label={`Product barcode ${line.productCode}`} value={scan[line.id]?.product??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{location:'',quantity:''}),product:e.target.value}}))}/>
                </label>
                <label>Số lượng
                  <input aria-label={`Pick quantity ${line.productCode}`} type="number" min="0" step="any" value={scan[line.id]?.quantity??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{location:'',product:''}),quantity:e.target.value}}))}/>
                </label>
                <button type="button" disabled={!!busy||!scan[line.id]?.location||!scan[line.id]?.product||!Number(scan[line.id]?.quantity)}
                  onClick={()=>void mutate(
                    `picking-pick-${selected.id}-${line.id}`,`/api/picking-tasks/${selected.id}/pick`,
                    {taskLineId:line.id,locationBarcode:scan[line.id]?.location,productBarcode:scan[line.id]?.product,quantity:Number(scan[line.id]?.quantity),rowVersion:selected.rowVersion}
                  )}>Xác nhận Pick</button>
              </div>}

            {canShortPick&&selected.status==='InProgress'&&['Open','InProgress'].includes(line.status)&&line.remainingQuantity>0&&
              <div className="ui-inline-wrap">
                <label>Lý do Short Pick
                  <input aria-label={`Short pick reason ${line.productCode}`} value={shortReason[line.id]??''}
                    onChange={e=>setShortReason(v=>({...v,[line.id]:e.target.value}))}/>
                </label>
                <button type="button" disabled={!!busy||!(shortReason[line.id]??'').trim()} onClick={()=>void mutate(
                  `picking-short-${selected.id}-${line.id}`,`/api/picking-tasks/${selected.id}/report-short-pick`,
                  {taskLineId:line.id,actualPickedQuantity:line.pickedQuantity,reason:shortReason[line.id],rowVersion:selected.rowVersion}
                )}>Báo Short Pick</button>
              </div>}
          </fieldset>)}
        </div>

        {(selected.shortPicks??[]).filter(item=>item.status==='Open').map(item=><fieldset key={item.id}>
          <legend>Short Pick #{item.id}</legend>
          <p>Dòng #{item.pickingTaskLineId} • Thiếu <strong>{item.shortageQuantity}</strong> • {item.reason}</p>
          {canShortPick&&<div className="ui-inline-wrap">
            <label>Phương án
              <select aria-label={`Short pick resolution ${item.id}`} value={resolution[item.id]??''}
                onChange={e=>setResolution(v=>({...v,[item.id]:e.target.value}))}>
                <option value="">Chọn phương án</option>
                <option value="AlternativeLocation">Vị trí khác</option>
                <option value="Backorder">Backorder</option>
                <option value="CancelRemainder">Hủy phần thiếu</option>
              </select>
            </label>
            <label>Ghi chú
              <input aria-label={`Short pick note ${item.id}`} value={resolutionNote[item.id]??''}
                onChange={e=>setResolutionNote(v=>({...v,[item.id]:e.target.value}))}/>
            </label>
            <button type="button" disabled={!!busy||!resolution[item.id]} onClick={()=>void mutate(
              `picking-resolve-${selected.id}-${item.id}`,`/api/picking-tasks/${selected.id}/short-picks/${item.id}/resolve`,
              {resolution:resolution[item.id],note:resolutionNote[item.id],rowVersion:selected.rowVersion}
            )}>Xử lý Short Pick</button>
          </div>}
          {canOverride&&<button type="button" disabled={!!busy||!(resolutionNote[item.id]??'').trim()} onClick={()=>void mutate(
            `picking-override-${selected.id}-${item.id}`,`/api/picking-tasks/${selected.id}/short-picks/${item.id}/override`,
            {reason:resolutionNote[item.id],rowVersion:selected.rowVersion}
          )}>Supervisor override</button>}
        </fieldset>)}
      </UiCard>}
    </div>
  </UiPage>;
}
