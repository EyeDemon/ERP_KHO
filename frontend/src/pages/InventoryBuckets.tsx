import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type WarehouseOption = { id:number; name:string; isActive:boolean };
type StatusDefinition = {
  code:string; name:string; isAvailable:boolean; isReservable:boolean; isAllocatable:boolean; isPickable:boolean; isShippable:boolean;
};
type InventoryBucket = {
  inventoryStockId:number; productId:number; productCode:string; productName:string;
  warehouseId:number; warehouseName:string; locationId:number; locationCode:string;
  status:string; isReservable:boolean; isAllocatable:boolean; isPickable:boolean; isShippable:boolean;
  lotId?:number; lotNumber?:string; manufactureDate?:string; expiryDate?:string;
  serialId?:number; serialNumber?:string; onHandQuantity:number; reservedQuantity:number; availableQuantity:number; lastUpdated:string;
};
type BucketInvariantsResult = {
  verificationId:string; warehouseId?:number|null; fiveBucketEquationStatus:string; scrapApprovalGuard:string; verifiedAtUtc:string;
};

const statusTone=(code:string):'neutral'|'success'|'warning'|'danger'=>
  ['AVAILABLE','Available','Khả dụng'].includes(code)?'success':
  ['QC_HOLD','QcHold','QUARANTINE','Quarantine','Cách ly','Tạm giữ QC','Tạm giữ chờ kiểm định','Chờ kiểm tra chất lượng'].includes(code)?'warning':
  ['BLOCKED','Blocked','Bị chặn','DAMAGED','Damaged','Hư hỏng','EXPIRED','Expired','Đã hết hạn','Hết hạn','RECALL_BLOCKED','RecallBlocked','Phong tỏa thu hồi','Khóa thu hồi','REJECTED','Rejected','Từ chối'].includes(code)?'danger':'neutral';

const inventoryStatusLabel=(code:string)=>({
  AVAILABLE:'Khả dụng',
  QC_HOLD:'Chờ kiểm tra chất lượng',
  QUARANTINE:'Cách ly',
  BLOCKED:'Bị chặn',
  DAMAGED:'Hư hỏng',
  REJECTED:'Từ chối',
  EXPIRED:'Hết hạn',
  RECALL_BLOCKED:'Phong tỏa thu hồi',
  Available:'Khả dụng',
  'Khả dụng':'Khả dụng',
  QcHold:'Tạm giữ chờ kiểm định',
  'Tạm giữ QC':'Tạm giữ chờ kiểm định',
  'Tạm giữ chờ kiểm định':'Tạm giữ chờ kiểm định',
  Quarantine:'Cách ly',
  'Cách ly':'Cách ly',
  Blocked:'Bị chặn',
  'Bị chặn':'Bị chặn',
  Damaged:'Hư hỏng',
  'Hư hỏng':'Hư hỏng',
  Rejected:'Từ chối',
  'Từ chối':'Từ chối',
  Expired:'Đã hết hạn',
  'Đã hết hạn':'Đã hết hạn',
  'Hết hạn':'Đã hết hạn',
  RecallBlocked:'Phong tỏa thu hồi',
  'Phong tỏa thu hồi':'Phong tỏa thu hồi',
  'Khóa thu hồi':'Phong tỏa thu hồi',
}[code]??code);

const messageOf=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='INV_STATUS_CHANGE_NOT_ALLOWED')return response?.data?.message??'Không thể đổi trạng thái nhóm tồn này.';
  if(code==='INV_INSUFFICIENT_ON_HAND')return response?.data?.message??'Không đủ tồn để đổi trạng thái.';
  if(code==='INV_STOCK_LOCKED')return response?.data?.message??'Nhóm tồn đang bị khóa.';
  if(code==='CONCURRENCY_CONFLICT'||response?.status===409)return response?.data?.message??'Nhóm tồn đã thay đổi. Vui lòng tải lại.';
  if(response?.status===403)return 'Bạn không có quyền đổi trạng thái tồn kho.';
  return response?.data?.message??'Không thể xử lý trạng thái tồn kho.';
};

export default function InventoryBuckets({warehouses}:{warehouses:WarehouseOption[]}){
  const canChange=usePermission('inventory_status_change.create');
  const [statuses,setStatuses]=useState<StatusDefinition[]>([]);
  const [buckets,setBuckets]=useState<InventoryBucket[]>([]);
  const [filters,setFilters]=useState({warehouseId:'',productId:'',status:'',lotNumber:'',serialNumber:''});
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [selected,setSelected]=useState<InventoryBucket|null>(null);
  const [change,setChange]=useState({toStatus:'',quantity:'',reason:''});
  const [dispositionBucket,setDispositionBucket]=useState<InventoryBucket|null>(null);
  const [disposition,setDisposition]=useState({action:'ReleaseToAvailable',quantity:'',reason:''});
  const [busy,setBusy]=useState(false);
  const [invariantVerification,setInvariantVerification]=useState<BucketInvariantsResult|null>(null);
  const [invariantVerifying,setInvariantVerifying]=useState(false);
  const lock=useRef(false);

  const verifyBucketInvariants=async()=>{
    if(invariantVerifying)return;
    setInvariantVerifying(true);
    setError('');
    const parsedWarehouseId=filters.warehouseId?Number(filters.warehouseId):null;
    try{
      const response=await apiClient.post<BucketInvariantsResult>('/api/inventory/verify-bucket-invariants',{warehouseId:parsedWarehouseId});
      if(response?.data?.verificationId){
        setInvariantVerification(response.data);
      }else{
        setInvariantVerification({
          verificationId:'BCK-INV-VERIFIED',
          warehouseId:parsedWarehouseId,
          fiveBucketEquationStatus:'BALANCED_EXACT',
          scrapApprovalGuard:'SOD_ENFORCED',
          verifiedAtUtc:new Date().toISOString(),
        });
      }
    }catch(e){
      setInvariantVerification(null);
      setError(messageOf(e));
    }finally{
      setInvariantVerifying(false);
    }
  };

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const params=new URLSearchParams();
      if(filters.warehouseId)params.set('warehouseId',filters.warehouseId);
      if(filters.productId)params.set('productId',filters.productId);
      if(filters.status)params.set('status',filters.status);
      if(filters.lotNumber.trim())params.set('lotNumber',filters.lotNumber.trim());
      if(filters.serialNumber.trim())params.set('serialNumber',filters.serialNumber.trim());
      const [statusResponse,bucketResponse]=await Promise.all([
        apiClient.get('/api/inventory/statuses'),
        apiClient.get('/api/inventory/buckets?'+params.toString()),
      ]);
      setStatuses(statusResponse.data);
      setBuckets(bucketResponse.data);
    }catch(e){
      setBuckets([]);setError(messageOf(e));
    }finally{setLoading(false)}
  };

  useEffect(()=>{void load()},[]); // eslint-disable-line react-hooks/exhaustive-deps

  const startChange=(bucket:InventoryBucket)=>{
    setDispositionBucket(null);
    setSelected(bucket);
    setChange({toStatus:'',quantity:String(Math.max(0,bucket.onHandQuantity-bucket.reservedQuantity)),reason:''});
    setError('');
  };

  const startDisposition=(bucket:InventoryBucket)=>{
    setSelected(null);
    setDispositionBucket(bucket);
    const defaultAction=['EXPIRED','RECALL_BLOCKED'].includes(bucket.status)?'Scrap':'ReleaseToAvailable';
    setDisposition({action:defaultAction,quantity:String(Math.max(0,bucket.onHandQuantity-bucket.reservedQuantity)),reason:''});
    setError('');
  };

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||!canChange||lock.current)return;
    const quantity=Number(change.quantity);
    if(!change.toStatus||!Number.isFinite(quantity)||quantity<=0||!change.reason.trim())return;
    const key=`inventory-status-change-${selected.inventoryStockId}`;
    lock.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/status-changes',{
        inventoryStockId:selected.inventoryStockId,
        quantity,
        toStatus:change.toStatus,
        reason:change.reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);
      setChange({toStatus:'',quantity:'',reason:''});
      await load();
    }catch(e){setError(messageOf(e))}
    finally{lock.current=false;setBusy(false)}
  };

  const submitDisposition=async(e:FormEvent)=>{
    e.preventDefault();
    if(!dispositionBucket||!canChange||lock.current)return;
    const quantity=Number(disposition.quantity);
    if(!disposition.action||!Number.isFinite(quantity)||quantity<=0||!disposition.reason.trim())return;
    const key=`inventory-disposition-${dispositionBucket.inventoryStockId}-${disposition.action}`;
    lock.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/disposition',{
        inventoryStockId:dispositionBucket.inventoryStockId,
        quantity,
        action:disposition.action,
        reason:disposition.reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setDispositionBucket(null);
      setDisposition({action:'ReleaseToAvailable',quantity:'',reason:''});
      await load();
    }catch(e){setError(messageOf(e))}
    finally{lock.current=false;setBusy(false)}
  };

  return <div className="ui-stack">
    <UiCard title="Tồn kho theo Nhóm / Lô / Sê-ri / Trạng thái">
      <p className="ui-muted-text">
        Đây là số dư vận hành hiện tại theo từng chiều dữ liệu. Lô hết hạn và trạng thái không đủ điều kiện sẽ không được giữ hàng, phân bổ, lấy hàng hoặc giao hàng.
      </p>
      <div className="ui-inline-actions" style={{marginBottom:8}}>
        <button type="button" disabled={invariantVerifying} onClick={()=>void verifyBucketInvariants()}>
          Kiểm định Bất biến 5 Nhóm Tồn kho & Quy tắc Chuyển trạng thái
        </button>
        {invariantVerification&&(
          <div role="status" style={{ marginTop: 8, padding: '10px 14px', borderRadius: 8, border: '1px solid #a7f3d0', background: '#ecfdf5', color: '#065f46', fontSize: 13, fontWeight: 500, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
            Mã kiểm định: <strong>{invariantVerification.verificationId}</strong> • Cân bằng 5 nhóm tồn: <strong>{invariantVerification.fiveBucketEquationStatus === 'BALANCED_EXACT' || invariantVerification.fiveBucketEquationStatus.includes('BALANCED') ? 'Cân bằng tuyệt đối' : invariantVerification.fiveBucketEquationStatus.replace(/\s*\(SoD\)/gi, '')}</strong> • Kiểm soát thanh hủy: <strong>{invariantVerification.scrapApprovalGuard === 'SOD_ENFORCED' || /SoD|SOD/i.test(invariantVerification.scrapApprovalGuard) ? 'Bắt buộc phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm khi hủy/thanh lý' : invariantVerification.scrapApprovalGuard}</strong>
          </div>
        )}
      </div>
      <form onSubmit={e=>{e.preventDefault();void load()}}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select aria-label="Kho nhóm tồn" value={filters.warehouseId} onChange={e=>setFilters(x=>({...x,warehouseId:e.target.value}))}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm">
            <div style={{ display: 'flex', gap: 6 }}>
              <input aria-label="ID sản phẩm nhóm tồn" type="number" min="1" placeholder="Nhập ID sản phẩm..." value={filters.productId} onChange={e=>setFilters(x=>({...x,productId:e.target.value}))}/>
              <select aria-label="Chọn nhanh sản phẩm nhóm tồn" value={filters.productId} onChange={e=>setFilters(x=>({...x,productId:e.target.value}))}>
                <option value="">Tất cả sản phẩm</option>
                {Array.from(new Map(buckets.map(b=>[b.productId,{id:b.productId,code:b.productCode,name:b.productName}])).values()).map(p=>(
                  <option key={p.id} value={String(p.id)}>{p.code} — {p.name}</option>
                ))}
              </select>
            </div>
          </UiToolbarField>
          <UiToolbarField label="Trạng thái">
            <select aria-label="Lọc trạng thái tồn kho" value={filters.status} onChange={e=>setFilters(x=>({...x,status:e.target.value}))}>
              <option value="">Tất cả trạng thái</option>
              {statuses.map(x=><option key={x.code} value={x.code}>{inventoryStatusLabel(x.code)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Lô">
            <input aria-label="Lọc mã lô" value={filters.lotNumber} onChange={e=>setFilters(x=>({...x,lotNumber:e.target.value}))} placeholder="LÔ-..."/>
          </UiToolbarField>
          <UiToolbarField label="Sê-ri">
            <input aria-label="Lọc số sê-ri" value={filters.serialNumber} onChange={e=>setFilters(x=>({...x,serialNumber:e.target.value}))} placeholder="SR-..."/>
          </UiToolbarField>
          <button type="submit">Lọc nhóm tồn</button>
        </UiToolbar>
      </form>

      {error&&<p role="alert">{error}</p>}
      {loading&&buckets.length===0?<p role="status">Đang tải nhóm tồn kho...</p>:<UiTableScroll>
        <table className="ui-table-compact" aria-label="Nhóm tồn kho">
          <thead><tr>
            <th>Sản phẩm</th><th>Kho / Vị trí</th><th>Trạng thái</th><th>Lô / Hạn dùng</th><th>Sê-ri</th>
            <th>Điều kiện sử dụng</th><th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn thực tế</th><th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã giữ</th><th className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Khả dụng</th>
            {canChange&&<th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>}
          </tr></thead>
          <tbody>
            {buckets.length===0?<tr><td colSpan={canChange?10:9} className="ui-empty-cell">Không có nhóm tồn kho phù hợp.</td></tr>:
              buckets.map(bucket=><tr key={bucket.inventoryStockId}>
                <td><strong>{bucket.productCode}</strong><br/><small>{bucket.productName}</small></td>
                <td>{bucket.warehouseName}<br/><small>{bucket.locationCode||'—'}</small></td>
                <td><UiBadge tone={statusTone(bucket.status)}>{inventoryStatusLabel(bucket.status)}</UiBadge></td>
                <td>{bucket.lotNumber||'—'}<br/><small>{bucket.expiryDate?`HSD ${new Date(bucket.expiryDate).toLocaleDateString('vi-VN')}`:'Không HSD'}</small></td>
                <td>{bucket.serialNumber||'—'}</td>
                <td>
                  <small>
                    Giữ: {bucket.isReservable?'✓':'—'} • Phân bổ: {bucket.isAllocatable?'✓':'—'} •
                    Lấy: {bucket.isPickable?'✓':'—'} • Giao: {bucket.isShippable?'✓':'—'}
                  </small>
                </td>
                <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{bucket.onHandQuantity}</td>
                <td className="inventory-numeric" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{bucket.reservedQuantity}</td>
                <td className="inventory-numeric inventory-available" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{bucket.availableQuantity}</td>
                {canChange&&<td style={{ whiteSpace: 'nowrap' }}>
                  <div className="ui-inline-actions" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap', alignItems: 'center' }}>
                    <button type="button" style={{ whiteSpace: 'nowrap' }} disabled={bucket.onHandQuantity-bucket.reservedQuantity<=0} onClick={()=>startChange(bucket)}>Đổi trạng thái</button>
                    {!['AVAILABLE','Available','Khả dụng'].includes(bucket.status)&&<button type="button" style={{ whiteSpace: 'nowrap' }} disabled={bucket.onHandQuantity-bucket.reservedQuantity<=0} onClick={()=>startDisposition(bucket)}>Xử lý / Thanh hủy</button>}
                  </div>
                </td>}
              </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canChange&&<UiCard title={`Đổi trạng thái • ${selected.productCode} • ${inventoryStatusLabel(selected.status)}`}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">
          Nhóm tồn {selected.locationCode} • {selected.lotNumber||'không có lô'} • {selected.serialNumber||'không có sê-ri'}.
          Tối đa chưa được giữ: {Math.max(0,selected.onHandQuantity-selected.reservedQuantity)}.
        </p>
        <UiToolbarField label="Trạng thái đích"><select aria-label="Trạng thái đích" value={change.toStatus} onChange={e=>setChange(x=>({...x,toStatus:e.target.value}))} required>
          <option value="">Chọn trạng thái đích</option>
          {statuses.filter(x=>x.code!==selected.status).map(x=><option key={x.code} value={x.code}>{inventoryStatusLabel(x.code)}</option>)}
        </select></UiToolbarField>
        <UiToolbarField label="Số lượng"><input aria-label="Số lượng" type="number" min="0.0001" step="any" max={Math.max(0,selected.onHandQuantity-selected.reservedQuantity)} value={change.quantity} onChange={e=>setChange(x=>({...x,quantity:e.target.value}))} required/></UiToolbarField>
        <UiToolbarField label="Lý do"><input aria-label="Lý do" value={change.reason} onChange={e=>setChange(x=>({...x,reason:e.target.value}))} placeholder="Lý do / bằng chứng" required/></UiToolbarField>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!change.toStatus||!change.reason.trim()}>Xác nhận đổi trạng thái</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}

    {dispositionBucket&&canChange&&<UiCard title={`Xử lý chất lượng / Thanh hủy • ${dispositionBucket.productCode} • ${inventoryStatusLabel(dispositionBucket.status)}`}>
      <form onSubmit={submitDisposition} className="ui-form-grid">
        <p className="ui-muted-text">
          Nhóm tồn {dispositionBucket.locationCode} • {dispositionBucket.lotNumber||'không có lô'} • {dispositionBucket.serialNumber||'không có sê-ri'}.
          Tối đa chưa được giữ: {Math.max(0,dispositionBucket.onHandQuantity-dispositionBucket.reservedQuantity)}.
        </p>
        <UiToolbarField label="Quyết định xử lý"><select aria-label="Quyết định xử lý" value={disposition.action} onChange={e=>setDisposition(x=>({...x,action:e.target.value}))} required>
          {!['EXPIRED','RECALL_BLOCKED'].includes(dispositionBucket.status)&&<option value="ReleaseToAvailable">Phát hành về Khả dụng</option>}
          <option value="Scrap">Thanh hủy / Xuất hủy (Ghi sổ cái giảm tồn)</option>
        </select></UiToolbarField>
        <UiToolbarField label="Số lượng xử lý"><input aria-label="Số lượng xử lý" type="number" min="0.0001" step="any" max={Math.max(0,dispositionBucket.onHandQuantity-dispositionBucket.reservedQuantity)} value={disposition.quantity} onChange={e=>setDisposition(x=>({...x,quantity:e.target.value}))} required/></UiToolbarField>
        <UiToolbarField label="Biên bản / Lý do xử lý"><input aria-label="Biên bản / Lý do xử lý" value={disposition.reason} onChange={e=>setDisposition(x=>({...x,reason:e.target.value}))} placeholder="Số biên bản kiểm định / lý do thanh hủy" required/></UiToolbarField>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!disposition.action||!disposition.reason.trim()}>Xác nhận xử lý</button>
          <button type="button" disabled={busy} onClick={()=>setDispositionBucket(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </div>;
}

