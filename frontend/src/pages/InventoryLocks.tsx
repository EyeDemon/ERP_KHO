import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbarField } from '../ui/ProductionUi';

type Warehouse={id:number;name:string;isActive:boolean};
type Status={code:string;name:string};
type LockRow={
  id:number;lockType:string;status:string;warehouseId:number;warehouseName:string;locationId?:number;locationCode?:string;
  productId?:number;productCode?:string;inventoryStatus?:string;lotId?:number;lotNumber?:string;serialId?:number;serialNumber?:string;
  reason:string;createdAt:string;createdBy:number;expiresAt?:string;releasedAt?:string;releasedBy?:number;releaseReason?:string;rowVersion:string;
};
const types=['CountFreeze','QualityHold','InvestigationHold','RecallHold','MaintenanceFreeze','ManualOperationalLock'];

const lockTypeLabel=(value:string)=>({
  CountFreeze:'Đóng băng kiểm kê',
  QualityHold:'Giữ do chất lượng',
  InvestigationHold:'Giữ để điều tra',
  RecallHold:'Giữ do thu hồi',
  MaintenanceFreeze:'Đóng băng bảo trì',
  ManualOperationalLock:'Khóa vận hành thủ công',
}[value]??value);

const lockStatusLabel=(value:string)=>({
  Active:'Đang hiệu lực',
  Released:'Đã mở khóa',
  Expired:'Đã hết hiệu lực',
}[value]??value);

const inventoryStatusLabel=(value:string)=>({
  AVAILABLE:'Khả dụng',
  QC_HOLD:'Chờ kiểm tra chất lượng',
  QUARANTINE:'Cách ly',
  BLOCKED:'Bị chặn',
  DAMAGED:'Hư hỏng',
  REJECTED:'Từ chối',
  EXPIRED:'Hết hạn',
  RECALL_BLOCKED:'Khóa thu hồi',
}[value]??value);

const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Active'?'danger':status==='Released'?'success':status==='Expired'?'warning':'neutral';
const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Khóa tồn kho không còn ở trạng thái có thể thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền quản lý khóa tồn kho.';
  if(r?.status===409)return r.data?.message??'Khóa tồn kho đã thay đổi. Vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý khóa tồn kho.';
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
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Khóa / đóng băng tồn kho"
      description="Khóa thay đổi điều kiện được phép thao tác nhưng không thay đổi số lượng. Khóa đang hiệu lực được kiểm tra lại ngay trong giao dịch thay đổi tồn kho."/>
    {error&&<p role="alert">{error}</p>}
    <div className="ui-stack">
      {canManage&&<UiCard title="Tạo khóa tồn kho">
        <form onSubmit={create} className="ui-form-grid" aria-busy={busy}>
          <UiToolbarField label="Loại khóa">
            <select value={form.lockType} onChange={e=>setForm(x=>({...x,lockType:e.target.value}))}>
              {types.map(x=><option key={x} value={x}>{lockTypeLabel(x)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Kho">
            <select value={form.warehouseId} onChange={e=>setForm(x=>({...x,warehouseId:e.target.value}))} required>
              <option value="">Chọn kho</option>
              {warehouses.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID vị trí (tùy chọn)">
            <input type="number" min="1" value={form.locationId} onChange={e=>setForm(x=>({...x,locationId:e.target.value}))} inputMode="numeric"/>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm (tùy chọn)">
            <input type="number" min="1" value={form.productId} onChange={e=>setForm(x=>({...x,productId:e.target.value}))} inputMode="numeric"/>
          </UiToolbarField>
          <UiToolbarField label="Trạng thái tồn kho (tùy chọn)">
            <select value={form.inventoryStatus} onChange={e=>setForm(x=>({...x,inventoryStatus:e.target.value}))}>
              <option value="">Mọi trạng thái tồn kho</option>
              {statuses.map(x=><option key={x.code} value={x.code}>{inventoryStatusLabel(x.code)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID lô (tùy chọn)">
            <input type="number" min="1" value={form.lotId} onChange={e=>setForm(x=>({...x,lotId:e.target.value}))} inputMode="numeric"/>
          </UiToolbarField>
          <UiToolbarField label="ID sê-ri (tùy chọn)">
            <input type="number" min="1" value={form.serialId} onChange={e=>setForm(x=>({...x,serialId:e.target.value}))} inputMode="numeric"/>
          </UiToolbarField>
          <UiToolbarField label="Hết hiệu lực lúc (tùy chọn)">
            <input type="datetime-local" value={form.expiresAt} onChange={e=>setForm(x=>({...x,expiresAt:e.target.value}))}/>
          </UiToolbarField>
          <UiToolbarField label="Lý do">
            <input value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} required/>
          </UiToolbarField>
          <button type="submit" disabled={busy||!form.warehouseId||!form.reason.trim()}>Tạo khóa</button>
        </form>
        <p className="ui-muted-text">Giữ do chất lượng, giữ để điều tra và giữ do thu hồi phải được mở thủ công; không tự hết hiệu lực.</p>
      </UiCard>}

      <UiCard title="Danh sách khóa tồn kho">
        <p className="ui-muted-text">Nền tảng hiện hỗ trợ phạm vi Kho / Vị trí / Sản phẩm / Trạng thái / Lô / Sê-ri. Khóa theo chủ sở hữu, HU, một phần số lượng và ghi đè đặc quyền vẫn chưa hoàn tất theo phạm vi chuẩn.</p>
        {loading?<p role="status">Đang tải khóa tồn kho...</p>:<UiTableScroll>
          <table aria-label="Danh sách khóa tồn kho">
            <thead><tr><th>Khóa</th><th>Phạm vi</th><th>Trạng thái</th><th>Lý do</th><th>Thời gian</th>{canManage&&<th>Thao tác</th>}</tr></thead>
            <tbody>{locks.length===0?<tr><td colSpan={canManage?6:5} className="ui-empty-cell">Chưa có khóa tồn kho.</td></tr>:
              locks.map(item=><tr key={item.id}>
                <td><strong>{lockTypeLabel(item.lockType)}</strong><br/><small>#{item.id}</small></td>
                <td>{item.warehouseName}<br/><small>
                  {item.locationCode??'mọi vị trí'} • {item.productCode??'mọi sản phẩm'} • {item.inventoryStatus?inventoryStatusLabel(item.inventoryStatus):'mọi trạng thái'} • {item.lotNumber??'mọi lô'} • {item.serialNumber??'mọi sê-ri'}
                </small></td>
                <td><UiBadge tone={tone(item.status)}>{lockStatusLabel(item.status)}</UiBadge></td>
                <td>{item.reason}{item.releaseReason&&<><br/><small>Lý do mở khóa: {item.releaseReason}</small></>}</td>
                <td>{new Date(item.createdAt).toLocaleString('vi-VN')}<br/><small>{item.expiresAt?`Hết hiệu lực ${new Date(item.expiresAt).toLocaleString('vi-VN')}`:'Không tự hết hiệu lực'}</small></td>
                {canManage&&<td>{item.status==='Active'&&<button type="button" onClick={()=>{setRelease(item);setReleaseReason('')}}>Mở khóa</button>}</td>}
              </tr>)}
            </tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {release&&canManage&&<UiCard title={`Mở khóa #${release.id}`}>
        <form onSubmit={releaseLock} className="ui-form-grid" aria-busy={busy}>
          <UiToolbarField label="Lý do mở khóa">
            <input value={releaseReason} onChange={e=>setReleaseReason(e.target.value)} required/>
          </UiToolbarField>
          <div className="ui-inline-actions">
            <button type="submit" disabled={busy||!releaseReason.trim()}>Xác nhận mở khóa</button>
            <button type="button" disabled={busy} onClick={()=>setRelease(null)}>Hủy</button>
          </div>
        </form>
      </UiCard>}
    </div>
  </UiPage>;
}
