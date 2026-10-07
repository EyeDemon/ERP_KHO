import { useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type Bucket={
  inventoryStockId:number;productId:number;productCode:string;productName:string;warehouseId:number;warehouseName:string;
  locationId?:number|null;locationCode?:string|null;inventoryStatus:string;lotId?:number|null;lotNumber?:string|null;
  expiryDate?:string|null;serialId?:number|null;serialNumber?:string|null;onHandQuantity:number;reservedQuantity:number;
};
type Event={
  transactionId:number;productId:number;productCode:string;productName:string;warehouseId:number;warehouseName:string;
  transactionType:string;inventoryStatus:string;fromInventoryStatus?:string|null;toInventoryStatus?:string|null;
  locationCode?:string|null;fromLocationCode?:string|null;toLocationCode?:string|null;lotNumber?:string|null;
  expiryDate?:string|null;serialNumber?:string|null;quantity:number;referenceType?:string|null;referenceId?:number|null;
  transactionDate:string;createdBy:number;createdByName:string;note?:string|null;reversalOfTransactionId?:number|null;
  correctiveTransactionId?:number|null;reversalTransactionId?:number|null;isReversed:boolean;
};
type Result={currentBuckets:Bucket[];events:Event[]};

const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string}}})?.response;
  if(r?.status===403)return 'Bạn không có quyền truy vết inventory.';
  return r?.data?.message??'Không thể tải traceability.';
};
const tone=(x:Event):'neutral'|'success'|'warning'|'danger'=>
  x.transactionType==='Reversal'?'warning':x.isReversed?'neutral':'success';

export default function InventoryTraceability(){
  const [form,setForm]=useState({warehouseId:'',productId:'',lotNumber:'',serialNumber:'',referenceType:'',referenceId:''});
  const [result,setResult]=useState<Result|null>(null);
  const [loading,setLoading]=useState(false);
  const [error,setError]=useState('');

  const search=async(e:FormEvent)=>{
    e.preventDefault();setError('');
    const hasIdentity=form.productId||form.lotNumber.trim()||form.serialNumber.trim();
    const hasReference=form.referenceType.trim()&&form.referenceId;
    if((form.referenceType.trim()&&!form.referenceId)||(!form.referenceType.trim()&&form.referenceId)){
      setError('Reference Type và Reference ID phải được nhập cùng nhau.');return;
    }
    if(!hasIdentity&&!hasReference){setError('Nhập ít nhất Product, Lot, Serial hoặc Reference.');return}
    setLoading(true);
    try{
      const params=new URLSearchParams();
      if(form.warehouseId)params.set('warehouseId',form.warehouseId);
      if(form.productId)params.set('productId',form.productId);
      if(form.lotNumber.trim())params.set('lotNumber',form.lotNumber.trim());
      if(form.serialNumber.trim())params.set('serialNumber',form.serialNumber.trim());
      if(form.referenceType.trim())params.set('referenceType',form.referenceType.trim());
      if(form.referenceId)params.set('referenceId',form.referenceId);
      params.set('limit','200');
      setResult((await apiClient.get('/api/inventory/traceability?'+params.toString())).data);
    }catch(e){setResult(null);setError(errorMessage(e))}
    finally{setLoading(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Inventory Control" title="Traceability & Genealogy"
      description="Truy current bucket và immutable ledger timeline theo Product, Lot, Serial hoặc document/reference trong warehouse scope được phép."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Điều kiện truy vết">
      <form onSubmit={search} className="ui-form-grid">
        <input aria-label="Warehouse ID traceability" type="number" min="1" value={form.warehouseId} onChange={e=>setForm(x=>({...x,warehouseId:e.target.value}))} placeholder="Warehouse ID (tùy chọn)"/>
        <input aria-label="Product ID traceability" type="number" min="1" value={form.productId} onChange={e=>setForm(x=>({...x,productId:e.target.value}))} placeholder="Product ID"/>
        <input aria-label="Lot traceability" value={form.lotNumber} onChange={e=>setForm(x=>({...x,lotNumber:e.target.value}))} placeholder="Lot number"/>
        <input aria-label="Serial traceability" value={form.serialNumber} onChange={e=>setForm(x=>({...x,serialNumber:e.target.value}))} placeholder="Serial number"/>
        <input aria-label="Reference Type traceability" value={form.referenceType} onChange={e=>setForm(x=>({...x,referenceType:e.target.value}))} placeholder="Reference Type"/>
        <input aria-label="Reference ID traceability" type="number" min="1" value={form.referenceId} onChange={e=>setForm(x=>({...x,referenceId:e.target.value}))} placeholder="Reference ID"/>
        <button type="submit" disabled={loading}>{loading?'Đang truy vết...':'Truy vết'}</button>
      </form>
    </UiCard>

    {result&&<div className="ui-stack">
      <UiCard title="Current inventory buckets">
        <UiTableScroll><table aria-label="Traceability current buckets">
          <thead><tr><th>Sản phẩm</th><th>Warehouse / Location</th><th>Status</th><th>Lot / Serial</th><th>OnHand</th><th>Reserved</th></tr></thead>
          <tbody>{result.currentBuckets.length===0?<tr><td colSpan={6} className="ui-empty-cell">Không còn current bucket khớp điều kiện.</td></tr>:
            result.currentBuckets.map(x=><tr key={x.inventoryStockId}>
              <td><strong>{x.productCode}</strong><br/><small>{x.productName}</small></td>
              <td>{x.warehouseName}<br/><small>{x.locationCode??'—'}</small></td>
              <td><UiBadge>{x.inventoryStatus}</UiBadge></td>
              <td>{x.lotNumber??'—'} / {x.serialNumber??'—'}{x.expiryDate&&<><br/><small>EXP {new Date(x.expiryDate).toLocaleDateString('vi-VN')}</small></>}</td>
              <td>{x.onHandQuantity}</td><td>{x.reservedQuantity}</td>
            </tr>)}
          </tbody>
        </table></UiTableScroll>
      </UiCard>

      <UiCard title="Immutable ledger timeline">
        <p className="ui-muted-text">Event Reversal là marker corrective, không xóa transaction gốc. Transaction gốc đã đảo được đánh dấu riêng.</p>
        <UiTableScroll><table aria-label="Traceability ledger timeline">
          <thead><tr><th>Thời gian</th><th>Event</th><th>Sản phẩm</th><th>Location / Status</th><th>Lot / Serial</th><th>Qty</th><th>Reference</th><th>Reversal chain</th><th>Actor / Note</th></tr></thead>
          <tbody>{result.events.length===0?<tr><td colSpan={9} className="ui-empty-cell">Không có ledger event khớp điều kiện.</td></tr>:
            result.events.map(x=><tr key={x.transactionId}>
              <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
              <td><UiBadge tone={tone(x)}>{x.transactionType}</UiBadge><br/><small>#{x.transactionId}{x.isReversed?' • đã reversal':''}{x.reversalOfTransactionId?(' • đảo #'+x.reversalOfTransactionId):''}</small></td>
              <td>{x.productCode}<br/><small>{x.warehouseName}</small></td>
              <td>{x.fromLocationCode&&x.toLocationCode?(x.fromLocationCode+' → '+x.toLocationCode):(x.locationCode??'—')}<br/><small>{x.fromInventoryStatus&&x.toInventoryStatus?(x.fromInventoryStatus+' → '+x.toInventoryStatus):x.inventoryStatus}</small></td>
              <td>{x.lotNumber??'—'} / {x.serialNumber??'—'}</td>
              <td>{x.quantity}</td>
              <td>{x.referenceType?(x.referenceType+' #'+(x.referenceId??'—')):'—'}</td>
              <td>
                {x.reversalOfTransactionId?<>Original #{x.reversalOfTransactionId}<br/></>:null}
                {x.correctiveTransactionId?<>Corrective #{x.correctiveTransactionId}<br/></>:null}
                {x.reversalTransactionId?<>Marker #{x.reversalTransactionId}</>:null}
                {!x.reversalOfTransactionId&&!x.correctiveTransactionId&&!x.reversalTransactionId?'—':null}
              </td>
              <td>{x.createdByName||('User #'+x.createdBy)}<br/><small>{x.note??''}</small></td>
            </tr>)}
          </tbody>
        </table></UiTableScroll>
      </UiCard>
    </div>}
  </UiPage>;
}
