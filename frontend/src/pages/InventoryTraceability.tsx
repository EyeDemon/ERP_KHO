import { useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbarField } from '../ui/ProductionUi';

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
  const [validationError,setValidationError]=useState('');
  const [requestError,setRequestError]=useState('');

  const referencePairInvalid=Boolean(
    (form.referenceType.trim()&&!form.referenceId)||(!form.referenceType.trim()&&form.referenceId)
  );

  const updateField=(field:keyof typeof form,value:string)=>{
    setForm(x=>({...x,[field]:value}));
    if(validationError)setValidationError('');
    if(requestError)setRequestError('');
  };

  const search=async(e:FormEvent)=>{
    e.preventDefault();setValidationError('');setRequestError('');
    const hasIdentity=form.productId||form.lotNumber.trim()||form.serialNumber.trim();
    const hasReference=form.referenceType.trim()&&form.referenceId;
    if(referencePairInvalid){
      setValidationError('Reference Type và Reference ID phải được nhập cùng nhau.');return;
    }
    if(!hasIdentity&&!hasReference){
      setValidationError('Nhập ít nhất Product, Lot, Serial hoặc Reference.');return;
    }
    setResult(null);
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
    }catch(e){setResult(null);setRequestError(errorMessage(e))}
    finally{setLoading(false)}
  };

  const resultStatus=loading
    ? 'Đang truy vết inventory.'
    : result
      ? `Đã tải ${result.currentBuckets.length} bucket hiện tại và ${result.events.length} ledger event.`
      : '';

  return <UiPage>
    <UiPageHeader eyebrow="Inventory Control" title="Traceability & Genealogy"
      description="Truy current bucket và immutable ledger timeline theo Product, Lot, Serial hoặc document/reference trong warehouse scope được phép."/>
    {requestError&&<p role="alert">{requestError}</p>}
    <UiCard title="Điều kiện truy vết">
      <form onSubmit={search} className="ui-form-grid" aria-busy={loading}>
        <UiToolbarField label="Warehouse ID (tùy chọn)">
          <input type="number" min="1" value={form.warehouseId} onChange={e=>updateField('warehouseId',e.target.value)} inputMode="numeric"/>
        </UiToolbarField>
        <UiToolbarField label="Product ID">
          <input type="number" min="1" value={form.productId} onChange={e=>updateField('productId',e.target.value)} inputMode="numeric"/>
        </UiToolbarField>
        <UiToolbarField label="Lot number">
          <input value={form.lotNumber} onChange={e=>updateField('lotNumber',e.target.value)} autoComplete="off"/>
        </UiToolbarField>
        <UiToolbarField label="Serial number">
          <input value={form.serialNumber} onChange={e=>updateField('serialNumber',e.target.value)} autoComplete="off"/>
        </UiToolbarField>
        <UiToolbarField label="Reference Type">
          <input
            value={form.referenceType}
            onChange={e=>updateField('referenceType',e.target.value)}
            aria-invalid={referencePairInvalid||undefined}
            aria-describedby={referencePairInvalid?'traceability-reference-help traceability-validation-error':'traceability-reference-help'}
            autoComplete="off"
          />
        </UiToolbarField>
        <UiToolbarField label="Reference ID">
          <input
            type="number"
            min="1"
            value={form.referenceId}
            onChange={e=>updateField('referenceId',e.target.value)}
            aria-invalid={referencePairInvalid||undefined}
            aria-describedby={referencePairInvalid?'traceability-reference-help traceability-validation-error':'traceability-reference-help'}
            inputMode="numeric"
          />
        </UiToolbarField>
        <p id="traceability-reference-help" className="ui-muted-text">
          Reference Type và Reference ID là một cặp; nhập cả hai khi truy theo chứng từ.
        </p>
        {validationError&&<p id="traceability-validation-error" role="alert">{validationError}</p>}
        <button type="submit" disabled={loading}>{loading?'Đang truy vết...':'Truy vết'}</button>
        {resultStatus&&<p role="status" aria-live="polite" className="ui-muted-text">{resultStatus}</p>}
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
