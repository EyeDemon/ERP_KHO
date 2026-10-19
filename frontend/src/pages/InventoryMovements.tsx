import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbarField } from '../ui/ProductionUi';

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
const statusLabel=(code:string)=>({
  AVAILABLE:'Khả dụng',
  QC_HOLD:'Chờ kiểm tra chất lượng',
  QUARANTINE:'Cách ly',
  BLOCKED:'Bị chặn',
  DAMAGED:'Hư hỏng',
  REJECTED:'Từ chối',
  EXPIRED:'Hết hạn',
  RECALL_BLOCKED:'Khóa thu hồi',
}[code]??code);
const locationCodeLabel=(code:string)=>({
  LEGACY:'KHO-CHUNG (Mặc định)',
  RECEIVING:'TIEP-NHAN (Nhận hàng)',
}[code]??code);

const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Nhóm tồn kho đang bị khóa.';
  if(r?.data?.code==='INV_INSUFFICIENT_AVAILABLE')return r.data.message??'Không đủ lượng tồn chưa giữ để di chuyển.';
  if(r?.data?.code==='TRANSFER_SOURCE_EQUALS_DESTINATION')return 'Vị trí nguồn và đích phải khác nhau.';
  if(r?.data?.code==='SERIAL_QUANTITY_INVALID')return r.data.message??'Số sê-ri chỉ được di chuyển đúng 1 đơn vị cơ bản.';
  if(r?.data?.code==='INV_BUCKET_CONFLICT'||r?.status===409)return r.data?.message??'Nhóm tồn kho hoặc vị trí đã thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền tạo di chuyển vị trí nội bộ.';
  return r?.data?.message??'Không thể tạo giao dịch di chuyển vị trí nội bộ.';
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
        apiClient.get('/api/inventory/replenishment-suggestions').catch((err)=>{
          if(err?.response?.status&&err.response.status>=400)setError(errorMessage(err));
          return {data:[]};
        }),
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
      reason:'Bổ sung mặt lấy hàng',
    });
  };

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||!canMove||guard.current)return;
    const quantity=Math.round((Number(form.quantity)+Number.EPSILON)*10000)/10000;
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
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Di chuyển vị trí nội bộ"
      description="Nguồn trừ Q / đích cộng Q trong cùng kho; giữ nguyên Trạng thái/Lô/Sê-ri và không cho di chuyển phần đã giữ chỗ."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Gợi ý Bổ sung Tồn kho mặt lấy hàng">
      <p className="ui-muted-text">Hệ thống tự động phát hiện vị trí lấy hàng có tồn thấp và đề xuất di chuyển từ vị trí dự trữ trong cùng kho.</p>
      {suggestions.length===0?<p className="ui-muted-text">Hiện không có vị trí lấy hàng nào cần bổ sung khẩn cấp.</p>:<UiTableScroll>
        <table className="ui-table-compact" aria-label="Gợi ý bổ sung vị trí lấy hàng">
          <thead><tr><th>Sản phẩm</th><th>Kho</th><th style={{whiteSpace:'nowrap'}}>Vị trí nguồn (Dự trữ)</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Khả dụng nguồn</th><th style={{whiteSpace:'nowrap'}}>Vị trí đích (Mặt lấy)</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Tồn tại đích</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>SL gợi ý</th>{canMove&&<th style={{whiteSpace:'nowrap',minWidth:150}}>Thao tác nhanh</th>}</tr></thead>
          <tbody>
            {suggestions.map(s=><tr key={`${s.inventoryStockId}-${s.targetLocationId}`}>
              <td><strong style={{whiteSpace:'nowrap'}}>{s.productCode}</strong><br/><small>{s.productName}</small></td>
              <td>{s.warehouseName}</td>
              <td style={{whiteSpace:'nowrap'}}><code style={{whiteSpace:'nowrap'}}>{locationCodeLabel(s.sourceLocationCode)}</code></td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{s.availableAtSource}</td>
              <td style={{whiteSpace:'nowrap'}}><code style={{whiteSpace:'nowrap'}}>{locationCodeLabel(s.targetLocationCode)}</code></td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{s.currentAtTarget}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}><strong>{s.suggestedMoveQuantity??s.suggestedQuantity}</strong></td>
              {canMove&&<td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void applySuggestion(s)}>Điền lệnh bổ sung</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canMove&&<UiCard title={`Di chuyển • ${selected.productCode} • ${locationCodeLabel(selected.locationCode)}`}>
      <p className="ui-muted-text" style={{marginBottom:8}}>{statusLabel(selected.status)} • {selected.lotNumber??'không có lô'} • {selected.serialNumber??'không có sê-ri'} • tối đa chưa giữ {Math.max(0,selected.onHandQuantity-selected.reservedQuantity)}</p>
      <form onSubmit={submit} className="ui-form-grid" style={{gridTemplateColumns:'repeat(auto-fit, minmax(200px, 1fr))',gap:10,alignItems:'end',maxWidth:'100%'}}>
        <UiToolbarField label="Vị trí đích">
          <select aria-label="Vị trí đích internal move" value={form.destinationLocationId} onChange={e=>setForm(x=>({...x,destinationLocationId:e.target.value}))} required>
            <option value="">Chọn vị trí đích</option>
            {locations.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Số lượng di chuyển">
          <input aria-label="Số lượng internal move" type="number" min="0.0001" step="any" max={Math.max(0,selected.onHandQuantity-selected.reservedQuantity)} value={form.quantity} onChange={e=>setForm(x=>({...x,quantity:e.target.value}))} required/>
        </UiToolbarField>
        <UiToolbarField label="Lý do di chuyển">
          <input aria-label="Lý do internal move" value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} placeholder="Lý do / bằng chứng" required/>
        </UiToolbarField>
        <div className="ui-inline-actions" style={{alignSelf:'end'}}>
          <button type="submit" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={busy||!form.destinationLocationId||!form.reason.trim()}>Xác nhận di chuyển</button>
          <button type="button" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}

    <UiCard title="Nhóm tồn kho có thể di chuyển">
      <p className="ui-muted-text">Thao tác di chuyển kiểm tra lại khóa hiệu lực và sức chứa vị trí đích trong cùng giao dịch tuần tự hóa cô lập. Đảo giao dịch là chức năng riêng, không nằm trong màn này.</p>
      {loading&&buckets.length===0?<p role="status">Đang tải nhóm tồn kho...</p>:<UiTableScroll>
        <table className="ui-table-compact" aria-label="Internal move buckets">
          <thead><tr><th>Sản phẩm</th><th>Kho / Vị trí</th><th style={{whiteSpace:'nowrap'}}>Trạng thái</th><th style={{whiteSpace:'nowrap'}}>Lô / Sê-ri</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Tồn thực tế</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Đã giữ</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Chưa giữ</th>{canMove&&<th style={{whiteSpace:'nowrap',minWidth:110}}>Thao tác</th>}</tr></thead>
          <tbody>{buckets.length===0?<tr><td colSpan={canMove?8:7} className="ui-empty-cell">Không có nhóm tồn kho.</td></tr>:
            buckets.map(x=><tr key={x.inventoryStockId}>
              <td><strong style={{whiteSpace:'nowrap'}}>{x.productCode}</strong><br/><small>{x.productName}</small></td>
              <td>{x.warehouseName}<br/><small style={{whiteSpace:'nowrap'}}>{locationCodeLabel(x.locationCode)}</small></td>
              <td style={{whiteSpace:'nowrap'}}><UiBadge>{statusLabel(x.status)}</UiBadge></td>
              <td style={{whiteSpace:'nowrap'}}>{x.lotNumber??'—'} / {x.serialNumber??'—'}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{x.onHandQuantity}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{x.reservedQuantity}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{Math.max(0,x.onHandQuantity-x.reservedQuantity)}</td>
              {canMove&&<td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} disabled={x.onHandQuantity-x.reservedQuantity<=0} onClick={()=>void select(x)}>Di chuyển</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>
  </UiPage>;
}

