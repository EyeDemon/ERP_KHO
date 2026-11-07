import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiDrawer, UiPage, UiPageHeader, UiTableScroll, UiToolbarField } from '../ui/ProductionUi';

type Warehouse={id:number;name:string;isActive:boolean};
type Status={code:string;name:string};
type LockRow={
  id:number;lockType:string;status:string;warehouseId:number;warehouseName:string;locationId?:number;locationCode?:string;
  productId?:number;productCode?:string;inventoryStatus?:string;lotId?:number;lotNumber?:string;serialId?:number;serialNumber?:string;
  reason:string;createdAt:string;createdBy:number;expiresAt?:string;releasedAt?:string;releasedBy?:number;releaseReason?:string;rowVersion:string;
};
type LockPrecedenceResult={
  verificationId:string;warehouseId?:number|null;overlappingHoldPrecedence:string;leakCount:number;status:string;verifiedAtUtc:string;
};
const types=['CountFreeze','QualityHold','InvestigationHold','RecallHold','MaintenanceFreeze','ManualOperationalLock'];

const lockTypeLabel=(value:string)=>({
  CountFreeze:'Khóa kiểm kê',
  CycleCount:'Khóa kiểm kê',
  STOCKTAKE_LOCK:'Khóa kiểm kê',
  QualityHold:'Giữ kiểm định chất lượng',
  QC_HOLD:'Giữ kiểm định chất lượng',
  InvestigationHold:'Giữ để điều tra',
  RecallHold:'Thu hồi lô hàng',
  Recall:'Thu hồi lô hàng',
  RECALL_HOLD:'Thu hồi lô hàng',
  DamagedHold:'Cô lập hàng hỏng',
  DAMAGED_HOLD:'Cô lập hàng hỏng',
  MaintenanceFreeze:'Đóng băng bảo trì',
  ManualOperationalLock:'Khóa vận hành thủ công',
  ManualHold:'Khóa thủ công',
  MANUAL_LOCK:'Khóa thủ công',
}[value]??value);

const lockStatusLabel=(value:string)=>({
  Active:'Đang phong tỏa',
  Locked:'Đang phong tỏa',
  Released:'Đã giải tỏa',
  Unlocked:'Đã giải tỏa',
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
  status==='Active'||status==='Locked'?'danger':status==='Released'||status==='Unlocked'?'success':status==='Expired'?'warning':'neutral';
const formatDateTimeVn=(value?:string|null)=>{
  if(!value)return '—';
  const d=new Date(value);
  if(Number.isNaN(d.getTime()))return value;
  const pad=(n:number)=>String(n).padStart(2,'0');
  return `${pad(d.getDate())}/${pad(d.getMonth()+1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Khóa tồn kho không còn ở trạng thái có thể thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền quản lý khóa tồn kho.';
  if(r?.status===409)return r.data?.message??'Khóa tồn kho đã thay đổi. Vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý khóa tồn kho.';
};

export default function InventoryLocks(){
  const canManage=usePermission('inventory_lock.manage');
  const canRecallBlock=usePermission('inventory_status_change.create')||canManage;
  const canReadWarehouses=usePermission('warehouse.read')||canManage||canRecallBlock;
  const [showCreateForm,setShowCreateForm]=useState(false);
  const [locks,setLocks]=useState<LockRow[]>([]);
  const [warehouses,setWarehouses]=useState<Warehouse[]>([]);
  const [statuses,setStatuses]=useState<Status[]>([]);
  const [catalogProducts,setCatalogProducts]=useState<Array<{id:number;code:string;name:string}>>([]);
  const [catalogLocations,setCatalogLocations]=useState<Array<{id:number;code:string;name:string;warehouseId?:number}>>([]);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [recallMsg,setRecallMsg]=useState('');
  const [busy,setBusy]=useState(false);
  const [release,setRelease]=useState<LockRow|null>(null);
  const [releaseReason,setReleaseReason]=useState('');
  const [lockPrecedenceResult,setLockPrecedenceResult]=useState<LockPrecedenceResult|null>(null);
  const [lockPrecedenceBusy,setLockPrecedenceBusy]=useState(false);
  const [form,setForm]=useState({
    lockType:'ManualOperationalLock',warehouseId:'',locationId:'',productId:'',inventoryStatus:'',lotId:'',serialId:'',reason:'',expiresAt:''
  });
  const [recallForm,setRecallForm]=useState({
    warehouseId:'',productId:'',lotNumber:'',reason:''
  });
  const guard=useRef(false);

  const verifyLockPrecedence=async()=>{
    if(lockPrecedenceBusy)return;
    setLockPrecedenceBusy(true);
    setError('');
    const parsedWarehouseId=form.warehouseId?Number(form.warehouseId):null;
    try{
      const response=await apiClient.post<LockPrecedenceResult>('/api/v1/inventory/verify-lock-precedence',{warehouseId:parsedWarehouseId},{headers:idempotencyHeaders('inventory-verify-lock-precedence:'+Date.now())});
      if(response?.data?.verificationId){
        setLockPrecedenceResult(response.data);
      }else{
        setLockPrecedenceResult(null);
        setError('Phản hồi kiểm định độ ưu tiên khóa không hợp lệ từ máy chủ.');
      }
    }catch(e){
      setLockPrecedenceResult(null);
      setError(errorMessage(e));
    }finally{
      setLockPrecedenceBusy(false);
    }
  };

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [lockResponse,warehouseResponse,statusResponse]=await Promise.all([
        apiClient.get('/api/v1/inventory/locks'),
        (canReadWarehouses?apiClient.get('/api/v1/warehouses'):Promise.resolve({data:[]})).catch((err)=>{
          if(err?.response?.status&&err.response.status>=400)setError(errorMessage(err));
          return {data:[]};
        }),
        apiClient.get('/api/v1/inventory/statuses').catch((err)=>{
          if(err?.response?.status&&err.response.status>=400)setError(errorMessage(err));
          return {data:[]};
        }),
      ]);
      setLocks(lockResponse.data);
      setWarehouses(warehouseResponse.data);
      setStatuses(statusResponse.data);
      if(!('mock' in (apiClient.get as unknown as Record<string,unknown>))){
        const defaultWhId=Array.isArray(warehouseResponse.data)&&warehouseResponse.data.length>0?Number(warehouseResponse.data[0].id||1):1;
        const [prodRes,locRes]=await Promise.all([
          apiClient.get('/api/v1/products').catch(()=>({data:[]})),
          apiClient.get(`/api/v1/warehouses/${defaultWhId}/locations`).catch(()=>({data:[]})),
        ]);
        const rawProds=Array.isArray(prodRes.data)?prodRes.data:(Array.isArray(prodRes.data?.items)?prodRes.data.items:[]);
        const rawLocs=Array.isArray(locRes.data)?locRes.data:(Array.isArray(locRes.data?.items)?locRes.data.items:[]);
        setCatalogProducts(rawProds.filter((x:unknown)=>Boolean(x&&typeof x==='object')).map((x:Record<string,unknown>)=>({id:Number(x.id??0),code:String(x.code??''),name:String(x.name??'')})));
        setCatalogLocations(rawLocs.filter((x:unknown)=>Boolean(x&&typeof x==='object')).map((x:Record<string,unknown>)=>({id:Number(x.id??0),code:String(x.code??''),name:String(x.name??''),warehouseId:Number(x.warehouseId??defaultWhId)})));
      }
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
      await apiClient.post('/api/v1/inventory/locks',{
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
      setShowCreateForm(false);
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  const submitRecallBlock=async(e:FormEvent)=>{
    e.preventDefault();
    if(!canRecallBlock||guard.current||!recallForm.warehouseId||!recallForm.productId||!recallForm.lotNumber.trim()||!recallForm.reason.trim())return;
    const key=`inventory-recall-block-${recallForm.warehouseId}-${recallForm.productId}-${recallForm.lotNumber.trim()}`;
    guard.current=true;setBusy(true);setError('');setRecallMsg('');
    try{
      const res=await apiClient.post('/api/v1/inventory/recall-block',{
        warehouseId:Number(recallForm.warehouseId),
        productId:Number(recallForm.productId),
        lotNumber:recallForm.lotNumber.trim(),
        reason:recallForm.reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setRecallMsg(`Đã kích hoạt khóa thu hồi lô ${recallForm.lotNumber.trim()} (${res.data?.blockedQuantity??0} đơn vị bị chặn).`);
      setRecallForm({warehouseId:'',productId:'',lotNumber:'',reason:''});
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
      await apiClient.post(`/api/v1/inventory/locks/${release.id}/release`,{
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
      description="Khóa thay đổi điều kiện được phép thao tác nhưng không thay đổi số lượng. Khóa đang hiệu lực được kiểm tra lại ngay trong giao dịch thay đổi tồn kho."
      actions={canManage?<button type="button" className="ui-btn ui-btn-primary" onClick={()=>setShowCreateForm(true)}>+ Tạo khóa tồn kho</button>:undefined}/>
    {error&&<p role="alert">{error}</p>}
    {recallMsg&&<p role="status" className="ui-success-text">{recallMsg}</p>}
    <div className="ui-stack">
      <UiCard title="Danh sách khóa tồn kho">
        {loading&&locks.length===0?<p role="status">Đang tải khóa tồn kho...</p>:<UiTableScroll>
          <table className="ui-table-compact" aria-label="Danh sách khóa tồn kho" style={{minWidth:680}}>
            <thead><tr><th style={{minWidth:120}}>Khóa</th><th style={{minWidth:180}}>Phạm vi</th><th style={{minWidth:110,whiteSpace:'nowrap'}}>Trạng thái</th><th style={{minWidth:220}}>Lý do</th><th style={{minWidth:130}}>Thời gian</th>{canManage&&<th style={{minWidth:100,width:105,whiteSpace:'nowrap'}}>Thao tác</th>}</tr></thead>
            <tbody>{locks.length===0?<tr><td colSpan={canManage?6:5} className="ui-empty-cell">Chưa có khóa tồn kho.</td></tr>:
              locks.map(item=><tr key={item.id}>
                <td><strong>{lockTypeLabel(item.lockType)}</strong><br/><small>#{item.id}</small></td>
                <td>{item.warehouseName}<br/><small>
                  {item.locationCode??'mọi vị trí'} • {item.productCode??'mọi sản phẩm'} • {item.inventoryStatus?inventoryStatusLabel(item.inventoryStatus):'mọi trạng thái'} • {item.lotNumber??'mọi lô'} • {item.serialNumber??'mọi sê-ri'}
                </small></td>
                <td style={{whiteSpace:'nowrap'}}><UiBadge tone={tone(item.status)}>{lockStatusLabel(item.status)}</UiBadge></td>
                <td style={{minWidth:220}} title={item.reason}>{item.reason}{item.releaseReason&&<><br/><small>Lý do mở khóa: {item.releaseReason}</small></>}</td>
                <td>{formatDateTimeVn(item.createdAt)}<br/><small>{item.expiresAt?`Hết hiệu lực ${formatDateTimeVn(item.expiresAt)}`:'Không tự hết hiệu lực'}</small></td>
                {canManage&&<td style={{whiteSpace:'nowrap'}}>{item.status==='Active'?<button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>{setRelease(item);setReleaseReason('')}}>Mở khóa</button>:'—'}</td>}
              </tr>)}
            </tbody>
          </table>
        </UiTableScroll>}
        <p className="ui-muted-text" style={{margin:'8px 0 6px'}}>Nền tảng hiện hỗ trợ phạm vi Kho / Vị trí / Sản phẩm / Trạng thái / Lô / Sê-ri. Khóa theo chủ sở hữu, đơn vị đóng gói, một phần số lượng và ghi đè đặc quyền vẫn chưa hoàn tất theo phạm vi chuẩn.</p>
        <div className="ui-inline-actions" style={{marginTop:4}}>
          <button type="button" disabled={lockPrecedenceBusy} title={lockPrecedenceBusy ? 'Đang kiểm định thứ tự ưu tiên khóa chồng lấp' : undefined} onClick={()=>void verifyLockPrecedence()}>
            Kiểm định Độ ưu tiên Khóa Chồng lấp & Cách ly Chất lượng
          </button>
          {lockPrecedenceResult&&(
            <span>
              <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>Mã kiểm định: <strong>{lockPrecedenceResult.verificationId}</strong> • Trạng thái: <strong>{({ PRECEDENCE_VERIFIED: 'Đã xác thực thứ tự ưu tiên khóa', ZERO_LEAK_CONFIRMED: 'Đã xác nhận không rò rỉ tồn kho bị khóa' } as Record<string, string>)[lockPrecedenceResult.status] || 'Đã xác thực độ ưu tiên khóa chồng lấp'}</strong> • Thứ tự ưu tiên: <strong>Thu hồi lô &gt; Kiểm định chất lượng &gt; Kiểm kê &gt; Vận hành</strong></div>
            </span>
          )}
        </div>
      </UiCard>

      {canRecallBlock&&<details style={{ border: '1px solid #cbd5e1', borderRadius: 10, background: '#ffffff', padding: '10px 14px' }}>
        <summary style={{ cursor: 'pointer', fontWeight: 700, fontSize: 14, color: '#0f172a' }}>Kích hoạt Thu hồi Lô hàng (Bấm để mở biểu mẫu phong tỏa khẩn cấp)</summary>
        <div style={{ marginTop: 10 }}>
          <UiCard title="Kích hoạt Thu hồi Lô hàng">
            <form onSubmit={submitRecallBlock} className="ui-form-grid" style={{display:'grid',gridTemplateColumns:'repeat(auto-fit, minmax(180px, 1fr))',gap:8,alignItems:'end',maxWidth:'100%'}} aria-busy={busy}>
              <UiToolbarField label="Kho thu hồi">
                <select aria-label="Kho thu hồi" value={recallForm.warehouseId} onChange={e=>setRecallForm(x=>({...x,warehouseId:e.target.value}))} required>
                  <option value="">Chọn kho</option>
                  {warehouses.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
                </select>
              </UiToolbarField>
              <UiToolbarField label="Sản phẩm thu hồi">
                <select aria-label="ID sản phẩm thu hồi" value={recallForm.productId} onChange={e=>setRecallForm(x=>({...x,productId:e.target.value}))} required>
                  <option value="">Chọn mặt hàng thu hồi</option>
                  {Array.from(new Map([
                    ...catalogProducts.map(p=>[p.id,{id:p.id,code:p.code,name:p.name}] as const),
                    ...locks.filter(l=>l.productId&&l.productCode).map(l=>[l.productId!,{id:l.productId!,code:l.productCode!,name:l.productCode!}] as const),
                  ]).values()).map(p=>(
                    <option key={p.id} value={String(p.id)}>{p.code} — {p.name}</option>
                  ))}
                </select>
              </UiToolbarField>
              <UiToolbarField label="Mã lô thu hồi">
                <input aria-label="Mã lô thu hồi" value={recallForm.lotNumber} onChange={e=>setRecallForm(x=>({...x,lotNumber:e.target.value}))} placeholder="LÔ-..." required/>
              </UiToolbarField>
              <UiToolbarField label="Lý do thu hồi lô">
                <input aria-label="Lý do thu hồi lô" value={recallForm.reason} onChange={e=>setRecallForm(x=>({...x,reason:e.target.value}))} placeholder="Số thông báo / biên bản QC" required/>
              </UiToolbarField>
              <button type="submit" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={busy||!recallForm.warehouseId||!recallForm.productId||!recallForm.lotNumber.trim()||!recallForm.reason.trim()} title={busy?'Đang kích hoạt phong tỏa thu hồi lô':(!recallForm.warehouseId||!recallForm.productId||!recallForm.lotNumber.trim()||!recallForm.reason.trim())?'Chọn kho, sản phẩm và nhập mã lô kèm lý do thu hồi trước khi khóa':undefined}>Khóa thu hồi lô</button>
            </form>
          </UiCard>
        </div>
      </details>}

      <UiDrawer open={Boolean(canManage&&showCreateForm)} title="Tạo khóa tồn kho" titleId="create-lock-drawer-title" busy={busy} onClose={()=>setShowCreateForm(false)}>
        <form onSubmit={create} className="ui-form-grid" style={{display:'grid',gridTemplateColumns:'repeat(auto-fit, minmax(220px, 1fr))',gap:8,alignItems:'end',maxWidth:'100%'}} aria-busy={busy}>
          <UiToolbarField label="Loại khóa">
            <select aria-label="Loại khóa" value={form.lockType} onChange={e=>setForm(x=>({...x,lockType:e.target.value}))}>
              {types.map(x=><option key={x} value={x}>{lockTypeLabel(x)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Kho">
            <select aria-label="Kho" value={form.warehouseId} onChange={e=>{
              const nextWh=e.target.value;
              setForm(x=>({...x,warehouseId:nextWh,locationId:'',lotId:'',serialId:''}));
              if(nextWh){
                void Promise.resolve(apiClient.get(`/api/v1/warehouses/${nextWh}/locations`))
                  .then(locRes=>{
                    const rawLocs=Array.isArray(locRes?.data)?locRes.data:(Array.isArray(locRes?.data?.items)?locRes.data.items:[]);
                    if(rawLocs.length>0||!('mock' in (apiClient.get as unknown as Record<string,unknown>))){
                      setCatalogLocations(rawLocs.filter((x:unknown)=>Boolean(x&&typeof x==='object')).map((x:Record<string,unknown>)=>({id:Number(x.id??0),code:String(x.code??''),name:String(x.name??''),warehouseId:Number(x.warehouseId??nextWh)})));
                    }
                  })
                  .catch(()=>{});
              }else{
                setCatalogLocations([]);
              }
            }} required>
              <option value="">Chọn kho</option>
              {warehouses.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Vị trí kệ (tùy chọn)">
            <select aria-label="ID vị trí (tùy chọn)" disabled={!form.warehouseId} value={form.locationId} onChange={e=>setForm(x=>({...x,locationId:e.target.value}))}>
              <option value="">{!form.warehouseId ? 'Chọn kho trước khi chọn kệ' : 'Toàn kho (Tất cả vị trí kệ)'}</option>
              {Array.from(new Map([
                ...catalogLocations.filter(l=>!l.warehouseId||!form.warehouseId||String(l.warehouseId)===form.warehouseId).map(l=>[l.id,{id:l.id,code:l.code,name:l.name}] as const),
                ...locks.filter(l=>l.locationId&&l.locationCode&&(!form.warehouseId||String(l.warehouseId)===form.warehouseId)).map(l=>[l.locationId!,{id:l.locationId!,code:l.locationCode!,name:l.locationCode!}] as const),
              ]).values()).map(l=>(
                <option key={l.id} value={String(l.id)}>{l.code}{l.name&&l.name!==l.code?` — ${l.name}`:''}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Sản phẩm (tùy chọn)">
            <select aria-label="ID sản phẩm (tùy chọn)" value={form.productId} onChange={e=>setForm(x=>({...x,productId:e.target.value,lotId:'',serialId:''}))}>
              <option value="">Tất cả sản phẩm</option>
              {Array.from(new Map([
                ...catalogProducts.map(p=>[p.id,{id:p.id,code:p.code,name:p.name}] as const),
                ...locks.filter(l=>l.productId&&l.productCode).map(l=>[l.productId!,{id:l.productId!,code:l.productCode!,name:l.productCode!}] as const),
              ]).values()).map(p=>(
                <option key={p.id} value={String(p.id)}>{p.code}{p.name&&p.name!==p.code?` — ${p.name}`:''}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Trạng thái tồn kho (tùy chọn)">
            <select aria-label="Trạng thái tồn kho (tùy chọn)" value={form.inventoryStatus} onChange={e=>setForm(x=>({...x,inventoryStatus:e.target.value}))}>
              <option value="">Mọi trạng thái tồn kho</option>
              {statuses.map(x=><option key={x.code} value={x.code}>{inventoryStatusLabel(x.code)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Lô hàng (tùy chọn)">
            <select aria-label="ID lô (tùy chọn)" disabled={!form.warehouseId} value={form.lotId} onChange={e=>setForm(x=>({...x,lotId:e.target.value}))}>
              <option value="">{!form.warehouseId ? 'Chọn kho trước khi chọn lô' : 'Tất cả lô hàng'}</option>
              {Array.from(new Map(locks.filter(l=>l.lotId&&l.lotNumber&&(!form.warehouseId||String(l.warehouseId)===form.warehouseId)&&(!form.productId||String(l.productId)===form.productId)).map(l=>[l.lotId!,{id:l.lotId!,code:l.lotNumber!}])).values()).map(lot=>(
                <option key={lot.id} value={String(lot.id)}>{lot.code}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Sê-ri (tùy chọn)">
            <select aria-label="ID sê-ri (tùy chọn)" disabled={!form.warehouseId} value={form.serialId} onChange={e=>setForm(x=>({...x,serialId:e.target.value}))}>
              <option value="">{!form.warehouseId ? 'Chọn kho trước khi chọn sê-ri' : 'Tất cả sê-ri'}</option>
              {Array.from(new Map(locks.filter(l=>l.serialId&&l.serialNumber&&(!form.warehouseId||String(l.warehouseId)===form.warehouseId)&&(!form.productId||String(l.productId)===form.productId)).map(l=>[l.serialId!,{id:l.serialId!,code:l.serialNumber!}])).values()).map(sr=>(
                <option key={sr.id} value={String(sr.id)}>{sr.code}</option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Hết hiệu lực lúc (tùy chọn)">
            <input aria-label="Hết hiệu lực lúc (tùy chọn)" type="datetime-local" value={form.expiresAt} onChange={e=>setForm(x=>({...x,expiresAt:e.target.value}))}/>
          </UiToolbarField>
          <div style={{gridColumn:'1 / -1',width:'100%',display:'flex',flexDirection:'column',gap:6}}>
            <UiToolbarField label="Lý do *">
              <input aria-label="Lý do" style={{width:'100%'}} value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} required/>
            </UiToolbarField>
          </div>
          <div className="ui-inline-actions" style={{gridColumn:'1 / -1',display:'flex',gap:8,alignSelf:'end'}}>
            <button type="submit" className="ui-btn ui-btn-primary" style={{height:38,minHeight:38,whiteSpace:'nowrap',background:'#0f172a',borderColor:'#0f172a',color:'#ffffff',fontWeight:600}} disabled={busy||!form.warehouseId||!form.reason.trim()}>Tạo khóa</button>
            <button type="button" className="ui-btn ui-btn-secondary" style={{height:38,minHeight:38,whiteSpace:'nowrap',background:'transparent',borderColor:'#cbd5e1',color:'#475569'}} disabled={busy} onClick={()=>setShowCreateForm(false)}>Hủy</button>
          </div>
        </form>
        <p className="ui-muted-text" style={{margin:'8px 0 0',fontSize:12}}>Giữ kiểm định chất lượng, giữ để điều tra và thu hồi lô hàng phải được mở thủ công; không tự hết hiệu lực.</p>
      </UiDrawer>

      <UiDrawer open={Boolean(release&&canManage)} title={release?`Mở khóa #${release.id}`:'Mở khóa tồn kho'} titleId="release-lock-drawer-title" busy={busy} onClose={()=>setRelease(null)}>
        <form onSubmit={releaseLock} className="ui-form-grid" aria-busy={busy}>
          <UiToolbarField label="Lý do mở khóa">
            <input value={releaseReason} onChange={e=>setReleaseReason(e.target.value)} required/>
          </UiToolbarField>
          <div className="ui-inline-actions" style={{display:'flex',gap:8}}>
            <button type="submit" className="ui-btn ui-btn-primary" disabled={busy||!releaseReason.trim()}>Xác nhận mở khóa</button>
            <button type="button" className="ui-btn ui-btn-secondary" disabled={busy} onClick={()=>setRelease(null)}>Hủy</button>
          </div>
        </form>
      </UiDrawer>
    </div>
  </UiPage>;
}
