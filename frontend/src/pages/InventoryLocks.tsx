import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type Warehouse={id:number;name:string;isActive:boolean};
type Status={code:string;name:string};
type LockRow={
  id:number;lockType:string;status:string;warehouseId:number;warehouseName:string;locationId?:number;locationCode?:string;
  productId?:number;productCode?:string;inventoryStatus?:string;lotId?:number;lotNumber?:string;serialId?:number;serialNumber?:string;
  reason:string;createdAt:string;createdBy:number;expiresAt?:string;releasedAt?:string;releasedBy?:number;releaseReason?:string;rowVersion:string;
};
const types=['CountFreeze','QualityHold','InvestigationHold','RecallHold','MaintenanceFreeze','ManualOperationalLock'];
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Active'?'danger':status==='Released'?'success':status==='Expired'?'warning':'neutral';
const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Inventory Lock không còn ở trạng thái có thể thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền quản lý Inventory Lock.';
  if(r?.status===409)return r.data?.message??'Inventory Lock đã thay đổi. Vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý Inventory Lock.';
};

export default function InventoryLocks(){
  const canManage=usePermission('inventory_lock.manage');
  const [locks,setLocks]=useState<LockRow[]>([]);
  const [warehouses,setWarehouses]=useState<Warehouse[]>([]);
  const [statuses,setStatuses]=useState<Status[]>([]);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState(false);
  const [release,setRelease]=useState<LockRow|null>(null);
  const [releaseReason,setReleaseReason]=useState('');
  const [form,setForm]=useState({
    lockType:'ManualOperationalLock',warehouseId:'',locationId:'',productId:'',inventoryStatus:'',lotId:'',serialId:'',reason:'',expiresAt:''
  });
  const guard=useRef(false);

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [lockResponse,warehouseResponse,statusResponse]=await Promise.all([
        apiClient.get('/api/inventory/locks'),
        apiClient.get('/api/Warehouses'),
        apiClient.get('/api/inventory/statuses'),
      ]);
      setLocks(lockResponse.data);
      setWarehouses(warehouseResponse.data);
      setStatuses(statusResponse.data);
    }catch(e){setLocks([]);setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  useEffect(()=>{void load()},[]);

  const create=async(e:FormEvent)=>{
    e.preventDefault();
    if(!canManage||guard.current||!form.warehouseId||!form.reason.trim())return;
    const key='inventory-lock-create';
    guard.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/locks',{
        lockType:form.lockType,
        warehouseId:Number(form.warehouseId),
        locationId:form.locationId?Number(form.locationId):undefined,
        productId:form.productId?Number(form.productId):undefined,
        inventoryStatus:form.inventoryStatus||undefined,
        lotId:form.lotId?Number(form.lotId):undefined,
        serialId:form.serialId?Number(form.serialId):undefined,
        reason:form.reason.trim(),
        expiresAt:form.expiresAt?new Date(form.expiresAt).toISOString():undefined,
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setForm(x=>({...x,locationId:'',productId:'',inventoryStatus:'',lotId:'',serialId:'',reason:'',expiresAt:''}));
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  const releaseLock=async(e:FormEvent)=>{
    e.preventDefault();
    if(!release||!canManage||guard.current||!releaseReason.trim())return;
    const key=`inventory-lock-release-${release.id}`;
    guard.current=true;setBusy(true);setError('');
    try{
      await apiClient.post(`/api/inventory/locks/${release.id}/release`,{
        reason:releaseReason.trim(),rowVersion:release.rowVersion
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setRelease(null);setReleaseReason('');
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Inventory Control" title="Inventory Locks / Freeze"
      description="Lock thay đổi eligibility, không thay đổi quantity. Active lock được re-check trong inventory mutation transaction."/>
    {error&&<p role="alert">{error}</p>}
    <div className="ui-stack">
      {canManage&&<UiCard title="Tạo Inventory Lock">
        <form onSubmit={create} className="ui-form-grid">
          <select aria-label="Loại Inventory Lock" value={form.lockType} onChange={e=>setForm(x=>({...x,lockType:e.target.value}))}>
            {types.map(x=><option key={x} value={x}>{x}</option>)}
          </select>
          <select aria-label="Warehouse lock" value={form.warehouseId} onChange={e=>setForm(x=>({...x,warehouseId:e.target.value}))} required>
            <option value="">Chọn Warehouse</option>
            {warehouses.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
          </select>
          <input aria-label="Location ID lock" type="number" min="1" placeholder="Location ID (tùy chọn)" value={form.locationId} onChange={e=>setForm(x=>({...x,locationId:e.target.value}))}/>
          <input aria-label="Product ID lock" type="number" min="1" placeholder="Product ID (tùy chọn)" value={form.productId} onChange={e=>setForm(x=>({...x,productId:e.target.value}))}/>
          <select aria-label="Inventory status lock" value={form.inventoryStatus} onChange={e=>setForm(x=>({...x,inventoryStatus:e.target.value}))}>
            <option value="">Mọi Inventory Status</option>
            {statuses.map(x=><option key={x.code} value={x.code}>{x.code}</option>)}
          </select>
          <input aria-label="Lot ID lock" type="number" min="1" placeholder="Lot ID (tùy chọn)" value={form.lotId} onChange={e=>setForm(x=>({...x,lotId:e.target.value}))}/>
          <input aria-label="Serial ID lock" type="number" min="1" placeholder="Serial ID (tùy chọn)" value={form.serialId} onChange={e=>setForm(x=>({...x,serialId:e.target.value}))}/>
          <input aria-label="Hết hạn Inventory Lock" type="datetime-local" value={form.expiresAt} onChange={e=>setForm(x=>({...x,expiresAt:e.target.value}))}/>
          <input aria-label="Lý do Inventory Lock" value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} placeholder="Lý do / evidence" required/>
          <button type="submit" disabled={busy||!form.warehouseId||!form.reason.trim()}>Tạo Lock</button>
        </form>
        <p className="ui-muted-text">QUALITY_HOLD / INVESTIGATION_HOLD / RECALL_HOLD phải release thủ công; không cho auto-expiry.</p>
      </UiCard>}

      <UiCard title="Danh sách Inventory Lock">
        <p className="ui-muted-text">Foundation hiện hỗ trợ scope Warehouse / Location / Product / Status / Lot / Serial. Owner, HU, partial-quantity và privileged override chưa canonical.</p>
        {loading?<p role="status">Đang tải Inventory Lock...</p>:<UiTableScroll>
          <table aria-label="Inventory locks">
            <thead><tr><th>Lock</th><th>Scope</th><th>Trạng thái</th><th>Lý do</th><th>Thời gian</th>{canManage&&<th>Thao tác</th>}</tr></thead>
            <tbody>{locks.length===0?<tr><td colSpan={canManage?6:5} className="ui-empty-cell">Chưa có Inventory Lock.</td></tr>:
              locks.map(item=><tr key={item.id}>
                <td><strong>{item.lockType}</strong><br/><small>#{item.id}</small></td>
                <td>{item.warehouseName}<br/><small>
                  {item.locationCode??'mọi location'} • {item.productCode??'mọi product'} • {item.inventoryStatus??'mọi status'} • {item.lotNumber??'mọi lot'} • {item.serialNumber??'mọi serial'}
                </small></td>
                <td><UiBadge tone={tone(item.status)}>{item.status}</UiBadge></td>
                <td>{item.reason}{item.releaseReason&&<><br/><small>Release: {item.releaseReason}</small></>}</td>
                <td>{new Date(item.createdAt).toLocaleString('vi-VN')}<br/><small>{item.expiresAt?`Hết hạn ${new Date(item.expiresAt).toLocaleString('vi-VN')}`:'Không auto-expire'}</small></td>
                {canManage&&<td>{item.status==='Active'&&<button type="button" onClick={()=>{setRelease(item);setReleaseReason('')}}>Release</button>}</td>}
              </tr>)}
            </tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {release&&canManage&&<UiCard title={`Release Lock #${release.id}`}>
        <form onSubmit={releaseLock} className="ui-form-grid">
          <input aria-label="Lý do release Inventory Lock" value={releaseReason} onChange={e=>setReleaseReason(e.target.value)} placeholder="Lý do release" required/>
          <div className="ui-inline-actions">
            <button type="submit" disabled={busy||!releaseReason.trim()}>Xác nhận release</button>
            <button type="button" disabled={busy} onClick={()=>setRelease(null)}>Hủy</button>
          </div>
        </form>
      </UiCard>}
    </div>
  </UiPage>;
}
