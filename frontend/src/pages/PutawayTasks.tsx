import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import './PutawayTasks.css';
import { UiBadge, UiCard, UiPage, UiPageHeader } from '../ui/ProductionUi';

type Item={id:number;productCode:string;productName:string;inventoryStatus:string;sourceLocationCode:string;operationUnitCode:string;baseUnitCode:string;requiredOperationQuantity:number;requiredBaseQuantity:number;movedBaseQuantity:number;remainingBaseQuantity:number};
type Task={id:number;receiptCode:string;warehouseName:string;status:string;assignedUserId:number|null;requiredBaseQuantity:number;movedBaseQuantity:number;rowVersion:string;exceptionReason?:string;items:Item[]};
type Location={id:number;code:string;name:string;locationType:string;isPickable:boolean};
const states:Record<string,string>={Open:'Chưa phân công',Assigned:'Đã phân công',InProgress:'Đang thực hiện',Completed:'Đã hoàn thành',Cancelled:'Đã hủy',Exception:'Cần xử lý'};
const inventory:Record<string,string>={Available:'Có thể sử dụng',Damaged:'Hư hỏng',Rejected:'Hàng bị từ chối'};
const message=(e:unknown)=>{const status=(e as {response?:{status?:number}})?.response?.status;return status===403?'Bạn không có quyền thực hiện thao tác này.':status===404?'Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.':status===409?'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.':'Không thể lưu dữ liệu. Vui lòng thử lại.'};

export default function PutawayTasks(){
 const [tasks,setTasks]=useState<Task[]>([]),[selected,setSelected]=useState<Task|null>(null),[loading,setLoading]=useState(true),[error,setError]=useState(''),[busy,setBusy]=useState('');
 const [locations,setLocations]=useState<Record<number,Location[]>>({}),[destination,setDestination]=useState<Record<number,number>>({}),[quantity,setQuantity]=useState<Record<number,number>>({}); const lock=useRef(new Set<string>());
 const canAssign=usePermission('putaway.assign'),canExecute=usePermission('putaway.execute'),canCancel=usePermission('putaway.cancel');
 const [exceptionReason,setExceptionReason]=useState('');
 const load=async()=>{setLoading(true);setError('');try{setTasks((await apiClient.get('/api/putaway-tasks')).data)}catch{setTasks([]);setSelected(null);setError('Không thể tải dữ liệu. Vui lòng thử lại.')}finally{setLoading(false)}};
 const detail=async(id:number)=>{setError('');try{const t=(await apiClient.get(`/api/putaway-tasks/${id}`)).data as Task;setSelected(t);const entries=await Promise.all(t.items.filter(i=>i.remainingBaseQuantity>0).map(async i=>[i.id,(await apiClient.get(`/api/putaway-tasks/${id}/items/${i.id}/destinations`)).data] as const));setLocations(Object.fromEntries(entries))}catch{setSelected(null);setError('Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.')}};
 useEffect(()=>{void load()},[]);
 const mutate=async(key:string,path:string,body:object)=>{if(lock.current.has(key))return;lock.current.add(key);setBusy(key);setError('');try{const t=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as Task;completeIdempotentAction(key);setSelected(t);await load();await detail(t.id)}catch(e){if((e as {response?:{status?:number}})?.response?.status===409)setSelected(null);setError(message(e))}finally{lock.current.delete(key);setBusy('')}};
 if(loading)return <p role="status">Đang tải nhiệm vụ cất hàng...</p>;
 return <UiPage>
   <UiPageHeader
     eyebrow="Nhập kho"
     title="Cất hàng"
     description="Theo dõi nhiệm vụ cất hàng từ vị trí nhận hàng tới vị trí đích, gồm trạng thái, tiến độ và xử lý ngoại lệ."
   />

   {error&&<div role="alert" className="putaway-error">{error}</div>}

   <div className="putaway putaway-grid">
     <UiCard title="Danh sách nhiệm vụ">
       {tasks.length===0?<p>Chưa có nhiệm vụ cất hàng phù hợp.</p>:<table>
         <thead><tr><th>Phiếu nhập</th><th>Kho</th><th>Trạng thái</th><th>Tiến độ</th></tr></thead>
         <tbody>{tasks.map(task=><tr key={task.id}>
           <td><button onClick={()=>void detail(task.id)}>{task.receiptCode}</button></td>
           <td>{task.warehouseName}</td>
           <td><UiBadge tone={task.status==='Completed'?'success':task.status==='Exception'?'danger':task.status==='InProgress'?'warning':'neutral'}>{states[task.status]??'Không xác định'}</UiBadge></td>
           <td><strong>{task.movedBaseQuantity}</strong> / {task.requiredBaseQuantity}</td>
         </tr>)}</tbody>
       </table>}
     </UiCard>

     {selected&&<UiCard title={'Chi tiết nhiệm vụ ' + selected.receiptCode}>
       <div className="ui-inline-wrap">
         <UiBadge tone={selected.status==='Completed'?'success':selected.status==='Exception'?'danger':selected.status==='InProgress'?'warning':'neutral'}>{states[selected.status]??'Không xác định'}</UiBadge>
         <span className="ui-muted-text">Tiến độ {selected.movedBaseQuantity}/{selected.requiredBaseQuantity}</span>
       </div>
       {selected.exceptionReason&&<p><strong>Lý do cần xử lý:</strong> {selected.exceptionReason}</p>}

       <div className="putaway-actions">
         {canAssign&&selected.status==='Open'&&<button disabled={!!busy} onClick={()=>void mutate(`assign-${selected.id}`,`/api/putaway-tasks/${selected.id}/assign`,{assignedUserId:currentUserId(),rowVersion:selected.rowVersion})}>{busy?'Đang lưu...':'Nhận nhiệm vụ'}</button>}
         {canExecute&&selected.status==='Assigned'&&<button disabled={!!busy} onClick={()=>void mutate(`start-${selected.id}`,`/api/putaway-tasks/${selected.id}/start`,{rowVersion:selected.rowVersion})}>{busy?'Đang lưu...':'Bắt đầu cất hàng'}</button>}
         {canExecute&&selected.status==='InProgress'&&<>
           <label>Lý do cần xử lý<input value={exceptionReason} onChange={e=>setExceptionReason(e.target.value)} /></label>
           <button disabled={!!busy||!exceptionReason.trim()} onClick={()=>void mutate(`exception-${selected.id}`,`/api/putaway-tasks/${selected.id}/exception`,{reason:exceptionReason,rowVersion:selected.rowVersion})}>Báo cần xử lý</button>
         </>}
         {canExecute&&selected.status==='Exception'&&<button disabled={!!busy} onClick={()=>void mutate(`resume-${selected.id}`,`/api/putaway-tasks/${selected.id}/resume`,{rowVersion:selected.rowVersion})}>Tiếp tục cất hàng</button>}
         {canCancel&&['Open','Assigned'].includes(selected.status)&&<button disabled={!!busy} onClick={()=>void mutate(`cancel-${selected.id}`,`/api/putaway-tasks/${selected.id}/cancel`,{rowVersion:selected.rowVersion})}>Hủy nhiệm vụ</button>}
       </div>

       <div className="ui-stack">
         {selected.items.map(item=><fieldset key={item.id}>
           <legend>{item.productCode} — {item.productName}</legend>
           <div className="ui-inline-wrap">
             <UiBadge>{inventory[item.inventoryStatus]??'Không xác định'}</UiBadge>
             <UiBadge>Vị trí nguồn: {item.sourceLocationCode}</UiBadge>
           </div>
           <p>Cần cất: <strong>{item.requiredOperationQuantity} {item.operationUnitCode}</strong> ({item.requiredBaseQuantity} {item.baseUnitCode})</p>
           <p>Đã cất: <strong>{item.movedBaseQuantity}</strong> • Còn lại: <strong>{item.remainingBaseQuantity}</strong></p>
           {canExecute&&item.remainingBaseQuantity>0&&['Assigned','InProgress'].includes(selected.status)&&<div className="move-form">
             <label>Vị trí đích<select aria-label={`Vị trí đích ${item.productCode}`} value={destination[item.id]??''} onChange={e=>setDestination(value=>({...value,[item.id]:Number(e.target.value)}))}><option value="">Chọn vị trí đích</option>{(locations[item.id]??[]).map(location=><option key={location.id} value={location.id}>{location.code} — {location.name}</option>)}</select></label>
             <label>Số lượng<input aria-label={`Số lượng cất ${item.productCode}`} type="number" min="0" step="any" value={quantity[item.id]??''} onChange={e=>setQuantity(value=>({...value,[item.id]:Number(e.target.value)}))}/></label>
             <button disabled={!!busy||!destination[item.id]||!quantity[item.id]} onClick={()=>void mutate(`move-${selected.id}-${item.id}`,`/api/putaway-tasks/${selected.id}/move`,{itemId:item.id,destinationLocationId:destination[item.id],quantity:quantity[item.id],unitCode:item.operationUnitCode,rowVersion:selected.rowVersion})}>{busy===`move-${selected.id}-${item.id}`?'Đang hoàn thành...':'Xác nhận số lượng'}</button>
           </div>}
         </fieldset>)}
       </div>
     </UiCard>}
   </div>
 </UiPage>;
}
