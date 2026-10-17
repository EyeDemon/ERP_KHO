import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type Bucket={
  inventoryStockId:number;productId:number;productCode:string;productName:string;warehouseId:number;warehouseName:string;
  locationId:number;locationCode:string;status:string;lotId?:number;lotNumber?:string;serialId?:number;serialNumber?:string;
  onHandQuantity:number;reservedQuantity:number;availableQuantity:number;
};
type Location={id:number;warehouseId:number;code:string;name:string;isActive:boolean;isBlocked:boolean;isSystemManaged:boolean};
type ReplenishmentSuggestion={
  inventoryStockId:number;productId:number;productCode:string;productName:string;
  warehouseId:number;warehouseName:string;sourceLocationId:number;sourceLocationCode:string;
  availableAtSource:number;targetLocationId:number;targetLocationCode:string;
  currentAtTarget:number;suggestedQuantity:number;suggestedMoveQuantity?:number;
};
const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Inventory bucket đang bị lock.';
  if(r?.data?.code==='INV_INSUFFICIENT_AVAILABLE')return r.data.message??'Không đủ unreserved quantity để move.';
  if(r?.data?.code==='TRANSFER_SOURCE_EQUALS_DESTINATION')return 'Vị trí nguồn và đích phải khác nhau.';
  if(r?.data?.code==='SERIAL_QUANTITY_INVALID')return r.data.message??'Serial chỉ được move đúng 1 base unit.';
  if(r?.data?.code==='INV_BUCKET_CONFLICT'||r?.status===409)return r.data?.message??'Inventory bucket/location đã thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền tạo internal move.';
  return r?.data?.message??'Không thể tạo internal inventory move.';
};

export default function InventoryMovements(){
  const canMove=usePermission('inventory_movement.create');
  const [buckets,setBuckets]=useState<Bucket[]>([]);
  const [suggestions,setSuggestions]=useState<ReplenishmentSuggestion[]>([]);
  const [locations,setLocations]=useState<Location[]>([]);
  const [selected,setSelected]=useState<Bucket|null>(null);
  const [form,setForm]=useState({destinationLocationId:'',quantity:'',reason:''});
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const guard=useRef(false);

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [bucketRes,suggestionRes]=await Promise.all([
        apiClient.get('/api/inventory/buckets?'),
        apiClient.get('/api/inventory/replenishment-suggestions').catch(()=>({data:[]})),
      ]);
      setBuckets(Array.isArray(bucketRes.data)?bucketRes.data:[]);
      setSuggestions(Array.isArray(suggestionRes.data)?suggestionRes.data:[]);
    }
    catch(e){setBuckets([]);setSuggestions([]);setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  useEffect(()=>{void load()},[]);

  const select=async(bucket:Bucket,prefill?:{targetLocationId:number;suggestedQuantity:number;reason:string})=>{
    setSelected(bucket);setError('');
    setForm({
      destinationLocationId:prefill?String(prefill.targetLocationId):'',
      quantity:String(prefill?prefill.suggestedQuantity:bucket.availableQuantity),
      reason:prefill?prefill.reason:'',
    });
    try{
      const response=await apiClient.get(`/api/putaway-tasks/locations?warehouseId=${bucket.warehouseId}`);
      setLocations((response.data as Location[]).filter(x=>x.id!==bucket.locationId&&x.isActive&&!x.isBlocked&&!x.isSystemManaged));
    }catch(e){setLocations([]);setError(errorMessage(e))}
  };

  const applySuggestion=async(s:ReplenishmentSuggestion)=>{
    const moveQty=s.suggestedMoveQuantity??s.suggestedQuantity;
    const found=buckets.find(b=>b.inventoryStockId===s.inventoryStockId)??{
      inventoryStockId:s.inventoryStockId,
      productId:s.productId,
      productCode:s.productCode,
      productName:s.productName,
      warehouseId:s.warehouseId,
      warehouseName:s.warehouseName,
      locationId:s.sourceLocationId,
      locationCode:s.sourceLocationCode,
      status:'AVAILABLE',
      onHandQuantity:s.availableAtSource,
      reservedQuantity:0,
      availableQuantity:s.availableAtSource,
    };
    await select(found,{
      targetLocationId:s.targetLocationId,
      suggestedQuantity:moveQty,
      reason:'Bổ sung mặt lấy hàng (TR-05)',
    });
  };

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||!canMove||guard.current)return;
    const quantity=Number(form.quantity);
    if(!form.destinationLocationId||!Number.isFinite(quantity)||quantity<=0||!form.reason.trim())return;
    const key=`inventory-move-${selected.inventoryStockId}`;
    guard.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/movements',{
        inventoryStockId:selected.inventoryStockId,
        destinationLocationId:Number(form.destinationLocationId),
        quantity,
        reason:form.reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);setLocations([]);setForm({destinationLocationId:'',quantity:'',reason:''});
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Inventory Control" title="Internal Location Move"
      description="Source -Q / destination +Q trong cùng Warehouse; giữ nguyên Status/Lot/Serial và không cho di chuyển phần reserved."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Gợi ý Bổ sung Tồn kho mặt lấy hàng (Replenishment Suggestions — TR-05)">
      <p className="ui-muted-text">Hệ thống tự động phát hiện vị trí lấy hàng (Pick Face) có tồn thấp và đề xuất di chuyển từ vị trí dự trữ (Reserve) trong cùng kho.</p>
      {suggestions.length===0?<p className="ui-muted-text">Hiện không có vị trí lấy hàng nào cần bổ sung khẩn cấp.</p>:<UiTableScroll>
        <table aria-label="Gợi ý bổ sung vị trí lấy hàng">
          <thead><tr><th>Sản phẩm</th><th>Kho</th><th>Vị trí nguồn (Reserve)</th><th>Khả dụng nguồn</th><th>Vị trí đích (Pick Face)</th><th>Tồn tại đích</th><th>SL gợi ý</th>{canMove&&<th>Thao tác nhanh</th>}</tr></thead>
          <tbody>
            {suggestions.map(s=><tr key={`${s.inventoryStockId}-${s.targetLocationId}`}>
              <td><strong>{s.productCode}</strong><br/><small>{s.productName}</small></td>
              <td>{s.warehouseName}</td>
              <td><code>{s.sourceLocationCode}</code></td>
              <td>{s.availableAtSource}</td>
              <td><code>{s.targetLocationCode}</code></td>
              <td>{s.currentAtTarget}</td>
              <td><strong>{s.suggestedMoveQuantity??s.suggestedQuantity}</strong></td>
              {canMove&&<td><button type="button" onClick={()=>void applySuggestion(s)}>Điền lệnh bổ sung</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    <UiCard title="Inventory buckets có thể di chuyển">
      <p className="ui-muted-text">Move re-check active locks và destination capacity trong cùng Serializable transaction. Reversal là capability riêng (INV-09), không nằm trong màn này.</p>
      {loading?<p role="status">Đang tải inventory bucket...</p>:<UiTableScroll>
        <table aria-label="Internal move buckets">
          <thead><tr><th>Sản phẩm</th><th>Warehouse / Location</th><th>Status</th><th>Lot / Serial</th><th>OnHand</th><th>Reserved</th><th>Unreserved</th>{canMove&&<th>Thao tác</th>}</tr></thead>
          <tbody>{buckets.length===0?<tr><td colSpan={canMove?8:7} className="ui-empty-cell">Không có bucket.</td></tr>:
            buckets.map(x=><tr key={x.inventoryStockId}>
              <td><strong>{x.productCode}</strong><br/><small>{x.productName}</small></td>
              <td>{x.warehouseName}<br/><small>{x.locationCode}</small></td>
              <td><UiBadge>{x.status}</UiBadge></td>
              <td>{x.lotNumber??'—'} / {x.serialNumber??'—'}</td>
              <td>{x.onHandQuantity}</td><td>{x.reservedQuantity}</td><td>{Math.max(0,x.onHandQuantity-x.reservedQuantity)}</td>
              {canMove&&<td><button type="button" disabled={x.onHandQuantity-x.reservedQuantity<=0} onClick={()=>void select(x)}>Move</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canMove&&<UiCard title={`Move • ${selected.productCode} • ${selected.locationCode}`}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">{selected.status} • {selected.lotNumber??'không lot'} • {selected.serialNumber??'không serial'} • tối đa unreserved {Math.max(0,selected.onHandQuantity-selected.reservedQuantity)}</p>
        <select aria-label="Vị trí đích internal move" value={form.destinationLocationId} onChange={e=>setForm(x=>({...x,destinationLocationId:e.target.value}))} required>
          <option value="">Chọn vị trí đích</option>
          {locations.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
        </select>
        <input aria-label="Số lượng internal move" type="number" min="0.0001" step="any" max={Math.max(0,selected.onHandQuantity-selected.reservedQuantity)} value={form.quantity} onChange={e=>setForm(x=>({...x,quantity:e.target.value}))} required/>
        <input aria-label="Lý do internal move" value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} placeholder="Lý do / evidence" required/>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!form.destinationLocationId||!form.reason.trim()}>Xác nhận Move</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </UiPage>;
}

