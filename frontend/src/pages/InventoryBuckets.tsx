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

const statusTone=(code:string):'neutral'|'success'|'warning'|'danger'=>
  code==='AVAILABLE'?'success':
  ['QC_HOLD','QUARANTINE'].includes(code)?'warning':
  ['BLOCKED','DAMAGED','EXPIRED','RECALL_BLOCKED','REJECTED'].includes(code)?'danger':'neutral';

const inventoryStatusLabel=(code:string)=>({
  AVAILABLE:'Khả dụng',
  QC_HOLD:'Chờ kiểm tra chất lượng',
  QUARANTINE:'Cách ly',
  BLOCKED:'Bị chặn',
  DAMAGED:'Hư hỏng',
  REJECTED:'Từ chối',
  EXPIRED:'Hết hạn',
  RECALL_BLOCKED:'Khóa thu hồi',
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
  const [busy,setBusy]=useState(false);
  const lock=useRef(false);

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
    setSelected(bucket);
    setChange({toStatus:'',quantity:String(Math.max(0,bucket.onHandQuantity-bucket.reservedQuantity)),reason:''});
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

  return <div className="ui-stack">
    <UiCard title="Tồn kho theo Nhóm / Lô / Sê-ri / Trạng thái">
      <p className="ui-muted-text">
        Đây là số dư vận hành hiện tại theo từng chiều dữ liệu. Lô hết hạn và trạng thái không đủ điều kiện sẽ không được giữ hàng, phân bổ, lấy hàng hoặc giao hàng.
      </p>
      <form onSubmit={e=>{e.preventDefault();void load()}}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select aria-label="Kho nhóm tồn" value={filters.warehouseId} onChange={e=>setFilters(x=>({...x,warehouseId:e.target.value}))}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm">
            <input aria-label="ID sản phẩm nhóm tồn" type="number" min="1" value={filters.productId} onChange={e=>setFilters(x=>({...x,productId:e.target.value}))}/>
          </UiToolbarField>
          <UiToolbarField label="Trạng thái">
            <select aria-label="Lọc trạng thái tồn kho" value={filters.status} onChange={e=>setFilters(x=>({...x,status:e.target.value}))}>
              <option value="">Tất cả trạng thái</option>
              {statuses.map(x=><option key={x.code} value={x.code}>{inventoryStatusLabel(x.code)}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Lô">
            <input aria-label="Lọc mã lô" value={filters.lotNumber} onChange={e=>setFilters(x=>({...x,lotNumber:e.target.value}))} placeholder="LOT-..."/>
          </UiToolbarField>
          <UiToolbarField label="Sê-ri">
            <input aria-label="Lọc số sê-ri" value={filters.serialNumber} onChange={e=>setFilters(x=>({...x,serialNumber:e.target.value}))} placeholder="SER-..."/>
          </UiToolbarField>
          <button type="submit">Lọc nhóm tồn</button>
        </UiToolbar>
      </form>

      {error&&<p role="alert">{error}</p>}
      {loading?<p role="status">Đang tải nhóm tồn kho...</p>:<UiTableScroll>
        <table aria-label="Nhóm tồn kho">
          <thead><tr>
            <th>Sản phẩm</th><th>Kho / Vị trí</th><th>Trạng thái</th><th>Lô / Hạn dùng</th><th>Sê-ri</th>
            <th>Điều kiện sử dụng</th><th className="inventory-numeric">Tồn thực tế</th><th className="inventory-numeric">Đã giữ</th><th className="inventory-numeric">Khả dụng</th>
            {canChange&&<th>Thao tác</th>}
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
                    Giữ:{bucket.isReservable?'✓':'—'} • Phân bổ:{bucket.isAllocatable?'✓':'—'} •
                    Lấy:{bucket.isPickable?'✓':'—'} • Giao:{bucket.isShippable?'✓':'—'}
                  </small>
                </td>
                <td className="inventory-numeric">{bucket.onHandQuantity}</td>
                <td className="inventory-numeric">{bucket.reservedQuantity}</td>
                <td className="inventory-numeric inventory-available">{bucket.availableQuantity}</td>
                {canChange&&<td><button type="button" disabled={bucket.onHandQuantity-bucket.reservedQuantity<=0} onClick={()=>startChange(bucket)}>Đổi trạng thái</button></td>}
              </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canChange&&<UiCard title={`Đổi trạng thái • ${selected.productCode} • ${selected.status}`}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">
          Bucket {selected.locationCode} • {selected.lotNumber||'không có lô'} • {selected.serialNumber||'không có sê-ri'}.
          Tối đa chưa được giữ: {Math.max(0,selected.onHandQuantity-selected.reservedQuantity)}.
        </p>
        <select aria-label="Status đích" value={change.toStatus} onChange={e=>setChange(x=>({...x,toStatus:e.target.value}))} required>
          <option value="">Chọn status đích</option>
          {statuses.filter(x=>x.code!==selected.status).map(x=><option key={x.code} value={x.code}>{x.code} — {x.name}</option>)}
        </select>
        <input aria-label="Số lượng đổi status" type="number" min="0.0001" step="any" max={Math.max(0,selected.onHandQuantity-selected.reservedQuantity)} value={change.quantity} onChange={e=>setChange(x=>({...x,quantity:e.target.value}))} required/>
        <input aria-label="Lý do đổi status" value={change.reason} onChange={e=>setChange(x=>({...x,reason:e.target.value}))} placeholder="Lý do / bằng chứng" required/>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!change.toStatus||!change.reason.trim()}>Xác nhận status change</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </div>;
}
