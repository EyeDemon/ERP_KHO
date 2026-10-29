import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import { Layers3, RefreshCw, Repeat2, Unlock } from 'lucide-react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import {
  UiBadge, UiCard, UiDrawer, UiPage, UiPageHeader,
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

const allocationStatusLabel=(status:string)=>({
  Active:'Đang hiệu lực',
  Allocated:'Đã phân bổ',
  Picking:'Đang lấy hàng',
  Picked:'Đã lấy hàng',
  Consumed:'Đã xuất kho',
  Released:'Đã giải phóng',
  Reallocated:'Đã phân bổ lại',
}[status]??status);

const strategyLabel=(strategy:string)=>({
  LocationOrder:'Thứ tự vị trí',
  Manual:'Chỉ định thủ công',
  FEFO:'Cận hạn xuất trước',
  FIFO:'Nhập trước xuất trước',
}[strategy]??strategy);

const selectionReasonLabel=(reason?:string)=>{
  if(!reason)return '—';
  return reason
    .replace(/Auto-allocated by FEFO/gi,'Phân bổ tự động theo cận hạn xuất trước')
    .replace(/Auto-allocated by FIFO/gi,'Phân bổ tự động theo nhập trước xuất trước')
    .replace(/Auto-allocated by location order/gi,'Phân bổ tự động theo thứ tự vị trí')
    .replace(/Manual location allocation/gi,'Phân bổ thủ công theo vị trí');
};

const sourceTypeLabel=(sourceType:string)=>({
  ExportReceipt:'Phiếu xuất kho',
  SalesOrder:'Đơn bán hàng',
  StockTransfer:'Phiếu điều chuyển',
}[sourceType]??sourceType);

export default function StockAllocations(){
  const inRouter=useInRouterContext();
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
  const [fefoVerification,setFefoVerification]=useState<{verificationId:string;fefoComplianceStatus:string}|null>(null);
  const [fefoBusy,setFefoBusy]=useState(false);

  const verifyFefoCompliance=async()=>{
    if(fefoBusy)return;
    setFefoBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/inventory/allocations/verify-fefo-compliance',{});
      if(res?.data?.verificationId){
        setFefoVerification({
          verificationId:String(res.data.verificationId),
          fefoComplianceStatus:String(res.data.fefoComplianceStatus??'VERIFIED'),
        });
      }else{
        setFefoVerification(null);
        setError('Phản hồi kiểm định tuân thủ phân bổ cận hạn / nhập trước không hợp lệ từ máy chủ.');
      }
    }catch(failure){
      setFefoVerification(null);
      setError(messageOf(failure,'Không thể kiểm định tuân thủ phân bổ cận hạn / nhập trước.'));
    }finally{
      setFefoBusy(false);
    }
  };

  const selectedReservation=useMemo(
    ()=>reservations.find(item=>item.reservationId===selectedReservationId),
    [reservations,selectedReservationId],
  );

  const loadPage=useCallback(async(page=1)=>{
    setLoading(true);setError('');
    try{
      const response=await apiClient.get('/api/inventory/allocations',{params:{page,pageSize:20,status:status||undefined}});
      setData(response.data);
    }catch(failure){setError(messageOf(failure,'Không thể tải danh sách phân bổ vị trí.'))}
    finally{setLoading(false)}
  },[status]);

  const loadReservations=useCallback(async()=>{
    setReferenceLoading(true);
    try{
      const response=await apiClient.get('/api/inventory/allocations/reservations');
      const items=response.data as AllocatableReservation[];
      setReservations(items);
      setSelectedReservationId(current=>current&&items.some(item=>item.reservationId===current)?current:(items[0]?.reservationId??''));
    }catch(failure){setError(messageOf(failure,'Không thể tải danh sách giữ hàng có thể phân bổ.'))}
    finally{setReferenceLoading(false)}
  },[]);

  useEffect(()=>{void loadPage(1)},[loadPage]);
  useEffect(()=>{void loadReservations()},[loadReservations]);
  useEffect(()=>{
    if(!selectedReservationId){setCandidates([]);setLocationId('');setQuantity('');return}
    let active=true;setReferenceLoading(true);
    apiClient.get('/api/inventory/allocations/candidates',{params:{reservationId:selectedReservationId}})
      .then(response=>{if(active){setCandidates(response.data);setLocationId('')}})
      .catch(failure=>{if(active)setError(messageOf(failure,'Không thể tải vị trí có thể phân bổ.'))})
      .finally(()=>{if(active)setReferenceLoading(false)});
    return()=>{active=false};
  },[selectedReservationId]);
  useEffect(()=>{if(selectedReservation)setQuantity(String(selectedReservation.allocatableQuantity))},[selectedReservation]);

  const refresh=async()=>{await Promise.all([loadPage(data.pageIndex),loadReservations()])};

  const allocate=async(manual:boolean)=>{
    if(submitting.current||!selectedReservation)return;
    const parsed=Number(quantity);
    if(!Number.isFinite(parsed)||parsed<=0||parsed>selectedReservation.allocatableQuantity){
      setError('Số lượng phân bổ phải lớn hơn 0 và không vượt phần giữ hàng còn có thể phân bổ.');return;
    }
    if(manual&&!locationId){setError('Hãy chọn vị trí khi phân bổ thủ công.');return}
    submitting.current=true;setBusy(true);setError('');
    const logical=`allocation:${manual?'manual':'auto'}:${selectedReservation.reservationId}:${parsed}:${manual?locationId:'auto'}`;
    try{
      await apiClient.post(manual?'/api/inventory/allocations':'/api/inventory/allocations/auto',{
        reservationId:selectedReservation.reservationId,quantity:parsed,locationId:manual?locationId:null,
      },{headers:idempotencyHeaders(logical)});
      completeIdempotentAction(logical);await refresh();
    }catch(failure){setError(messageOf(failure,'Không thể tạo phân bổ vị trí.'))}
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
    }catch(failure){setError(messageOf(failure,action.kind==='release'?'Không thể giải phóng phân bổ.':'Không thể phân bổ lại vị trí.'))}
    finally{submitting.current=false;setBusy(false)}
  };

  const pages=Math.max(1,Math.ceil(data.totalRecords/data.pageSize));
  const activeQuantity=data.items.filter(item=>['Active','Picking','Picked'].includes(item.status)).reduce((sum,item)=>sum+item.quantity,0);
  const activeAllocationsCount=data.items.filter(item=>['Active','Allocated','Picking'].includes(item.status)).length;
  const unallocatedReservationCount=reservations.filter(item=>item.allocatableQuantity>0).length;
  const totalUnallocatedQuantity=reservations.reduce((sum,item)=>sum+item.allocatableQuantity,0);

  return <UiPage>
    <UiPageHeader eyebrow="Xuất kho" title="Phân bổ tồn kho theo vị trí"
      description="Gắn phần giữ hàng vào vị trí lấy hàng cụ thể. Phân bổ không làm giảm tồn khả dụng lần thứ hai; tồn thực tế chỉ thay đổi khi xuất giao."
      actions={<button type="button" style={{whiteSpace:'nowrap'}} disabled={loading||referenceLoading||busy} onClick={()=>void refresh()}><RefreshCw size={16} aria-hidden="true"/> Làm mới</button>}
    />
    <div
      data-testid="reservation-allocation-tabs"
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: 8,
        marginBottom: 12,
        padding: 6,
        borderRadius: 10,
        background: '#f1f5f9',
        border: '1px solid #cbd5e1',
      }}
    >
      {[
        { to: '/stock-reservations', label: 'Giữ hàng tồn kho', testId: 'tab-stock-reservations', active: false },
        { to: '/stock-allocations', label: 'Phân bổ vị trí kệ', testId: 'tab-stock-allocations', active: true },
      ].map((tab) => {
        const style: React.CSSProperties = {
          padding: '8px 14px',
          borderRadius: 8,
          fontWeight: 700,
          fontSize: 13,
          textDecoration: 'none',
          background: tab.active ? '#0f172a' : '#ffffff',
          color: tab.active ? '#ffffff' : '#334155',
          border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
        };
        return inRouter ? (
          <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
            {tab.label}
          </Link>
        ) : (
          <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
            {tab.label}
          </a>
        );
      })}
    </div>
    {error&&<p role="alert">{error}</p>}

    <UiCard>
      <div style={{display:'flex',flexWrap:'wrap',justifyContent:'space-between',alignItems:'center',gap:12,marginBottom:8}}>
        <h2 className="ui-card-title" style={{margin:0}}>Tổng quan Phân bổ Tồn kho theo Vị trí & Cam kết Chưa phân bổ</h2>
        <div className="ui-inline-actions">
          <button type="button" style={{whiteSpace:'normal',textAlign:'left'}} disabled={fefoBusy||busy} onClick={()=>void verifyFefoCompliance()}>
            Kiểm định Tuân thủ Phân bổ Cận hạn & Nhập trước theo Vị trí
          </button>
          {fefoVerification&&(
            <UiBadge tone="success">
              {fefoVerification.verificationId} • {fefoVerification.fefoComplianceStatus==='VERIFIED'?'ĐÃ XÁC MINH':fefoVerification.fefoComplianceStatus}
            </UiBadge>
          )}
        </div>
      </div>
      <div className="ui-kpi-grid" style={{display:'grid',gridTemplateColumns:'repeat(6, minmax(0, 1fr))',gap:8}}>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>Phân bổ phù hợp</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{data.totalRecords}</strong>
        </div>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>SL đang cam kết trên trang</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{activeQuantity}</strong>
        </div>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>Giữ hàng còn có thể phân bổ</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{reservations.length}</strong>
        </div>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>Phân bổ đang hiệu lực / soạn hàng</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{activeAllocationsCount}</strong>
        </div>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>Giữ hàng chưa phân bổ hết</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{unallocatedReservationCount}</strong>
        </div>
        <div className="ui-subcard" style={{padding:'6px 10px'}}>
          <span className="ui-muted-text" style={{fontSize:12,display:'block'}}>Tổng lượng cam kết chờ phân bổ</span>
          <strong style={{fontSize:16,fontVariantNumeric:'tabular-nums'}}>{totalUnallocatedQuantity}</strong>
        </div>
      </div>
    </UiCard>

    {canCreate&&<UiCard title="Tạo phân bổ vị trí">
      <UiToolbar>
        <UiToolbarField label="Phiếu giữ hàng">
          <select aria-label="Chọn reservation để Allocation" value={selectedReservationId} disabled={referenceLoading||busy}
            onChange={event=>setSelectedReservationId(event.target.value?Number(event.target.value):'')}>
            <option value="">Chọn phiếu giữ hàng</option>
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
        <button type="button" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={!selectedReservation||busy||referenceLoading} onClick={()=>void allocate(false)}><Layers3 size={16} aria-hidden="true"/> Tự động</button>
        <button type="button" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={!selectedReservation||!locationId||busy||referenceLoading} onClick={()=>void allocate(true)}><Layers3 size={16} aria-hidden="true"/> Theo vị trí</button>
      </UiToolbar>
      {selectedReservation&&<p className="ui-muted-text" style={{margin:'4px 0 0',fontSize:12}} role="status">
        {selectedReservation.reservationCode} • {selectedReservation.warehouseName} • {selectedReservation.productCode} – {selectedReservation.productName}
        {' '}• Đã giữ {selectedReservation.reservedQuantity} • Đã phân bổ {selectedReservation.allocatedQuantity}
        {' '}• Còn {selectedReservation.allocatableQuantity} • Hết hạn {new Date(selectedReservation.expiresAt).toLocaleString('vi-VN')}
      </p>}
    </UiCard>}

    <UiCard title="Danh sách phân bổ vị trí">
      <div style={{marginBottom:4}}>
        <UiToolbar>
          <UiToolbarField label="Trạng thái">
            <select aria-label="Lọc trạng thái Allocation" value={status} onChange={event=>setStatus(event.target.value)}>
              <option value="">Tất cả</option>
              {['Active','Picking','Picked','Consumed','Released','Reallocated'].map(value=><option key={value} value={value}>{allocationStatusLabel(value)}</option>)}
            </select>
          </UiToolbarField>
          <div className="ui-muted-text ui-auto-actions">Trang {data.pageIndex}/{pages} • {data.totalRecords} bản ghi</div>
        </UiToolbar>
      </div>
      <UiTableScroll><table className="ui-table-compact" aria-label="Danh sách Allocation">
        <thead><tr><th style={{whiteSpace:'nowrap'}}>Mã phân bổ</th><th style={{whiteSpace:'nowrap'}}>Giữ hàng / Nguồn</th><th>Sản phẩm</th><th>Kho / Vị trí</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>SL</th><th>Chiến lược</th><th style={{whiteSpace:'nowrap'}}>Trạng thái</th><th>Lý do chọn</th>{(canRelease||canReallocate)&&<th style={{whiteSpace:'nowrap'}}>Thao tác</th>}</tr></thead>
        <tbody>
          {loading&&data.items.length===0?<tr><td className="ui-empty-cell" colSpan={(canRelease||canReallocate)?9:8}>Đang tải...</td></tr>:
          data.items.length===0?<tr><td className="ui-empty-cell" colSpan={(canRelease||canReallocate)?9:8}>Không có phân bổ vị trí phù hợp.</td></tr>:
          data.items.map(item=><tr key={item.id}>
            <td style={{whiteSpace:'nowrap'}}><strong style={{whiteSpace:'nowrap'}}>{item.allocationCode}</strong><div className="ui-muted-text" style={{whiteSpace:'nowrap'}}>{new Date(item.allocatedAt).toLocaleString('vi-VN')}</div></td>
            <td><strong style={{whiteSpace:'nowrap'}}>{item.reservationCode}</strong><div className="ui-muted-text">{sourceTypeLabel(item.sourceType)}{item.sourceCode?` / ${item.sourceCode}`:''}</div></td>
            <td><strong style={{whiteSpace:'nowrap'}}>{item.productCode}</strong> – {item.productName}</td>
            <td>{item.warehouseName}<div className="ui-muted-text"><span style={{whiteSpace:'nowrap'}}>{item.locationCode}</span> – {item.locationName}</div></td>
            <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}><strong>{item.quantity}</strong></td>
            <td>{strategyLabel(item.strategy)}</td>
            <td style={{whiteSpace:'nowrap'}}><UiBadge tone={toneOf(item.status)}>{allocationStatusLabel(item.status)}</UiBadge></td>
            <td>{selectionReasonLabel(item.selectionReason)}</td>
            {(canRelease||canReallocate)&&<td style={{whiteSpace:'nowrap'}}>{item.status==='Active'?<div className="ui-inline-actions" style={{flexWrap:'nowrap',gap:6}}>
              {canRelease&&<button type="button" style={{whiteSpace:'nowrap',padding:'4px 8px',fontSize:12}} disabled={busy} onClick={event=>void openAction('release',item,event.currentTarget)}><Unlock size={14} aria-hidden="true"/> Giải phóng</button>}
              {canReallocate&&<button type="button" style={{whiteSpace:'nowrap',padding:'4px 8px',fontSize:12}} disabled={busy} onClick={event=>void openAction('reallocate',item,event.currentTarget)}><Repeat2 size={14} aria-hidden="true"/> Phân bổ lại</button>}
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

    {action&&<UiDrawer
      open={Boolean(action)}
      title={action.kind==='release'?'Giải phóng phân bổ':'Phân bổ lại vị trí'}
      subtitle={<><strong>{action.allocation.allocationCode}</strong> • {action.allocation.productCode} • {action.allocation.quantity} tại {action.allocation.locationCode}</>}
      titleId="allocation-action-title"
      busy={busy}
      returnFocusTo={action.trigger}
      onClose={()=>setAction(null)}
      footer={
        <div style={{display:'flex',gap:8,justifyContent:'flex-end',width:'100%'}}>
          <button type="button" className="ui-btn ui-btn-secondary" disabled={busy} onClick={()=>setAction(null)}>Hủy</button>
          <button type="button" className="ui-btn ui-btn-primary ui-primary-button" disabled={busy||!reason.trim()} onClick={()=>void submitAction()}>{busy?'Đang xử lý...':'Xác nhận'}</button>
        </div>
      }
    >
      {action.kind==='reallocate'&&<label style={{display:'flex',flexDirection:'column',gap:6,fontSize:13,fontWeight:600,color:'#0f172a',width:'100%'}}>Vị trí mới
        <select className="ui-select" style={{width:'100%',boxSizing:'border-box',minHeight:38}} aria-label="Vị trí phân bổ lại" value={actionLocationId} disabled={busy} onChange={event=>setActionLocationId(event.target.value?Number(event.target.value):'')}>
          <option value="">Tự động chọn vị trí khác</option>
          {actionCandidates.map(item=><option key={item.locationId} value={item.locationId}>{item.locationCode} • còn {item.allocatableQuantity}</option>)}
        </select>
      </label>}
      <label style={{display:'flex',flexDirection:'column',gap:6,fontSize:13,fontWeight:600,color:'#0f172a',width:'100%'}}>Lý do
        <textarea className="ui-input" rows={4} style={{width:'100%',boxSizing:'border-box',padding:'8px 10px'}} data-initial-focus aria-label="Lý do thao tác Allocation" placeholder="Nhập lý do giải phóng hoặc phân bổ lại vị trí..." value={reason} disabled={busy} onChange={event=>setReason(event.target.value)}/>
      </label>
    </UiDrawer>}
  </UiPage>;
}
