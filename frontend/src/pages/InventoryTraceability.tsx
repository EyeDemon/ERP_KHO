import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useLocation } from 'react-router-dom';
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
  if(r?.status===403)return 'Bạn không có quyền truy vết tồn kho. Liên hệ quản trị nếu cần quyền truy cập.';
  if(r?.data?.message)return r.data.message+' Hãy điều chỉnh điều kiện hoặc thử lại.';
  return 'Không thể tải dữ liệu truy vết tồn kho. Hãy thử lại; nếu lỗi tiếp diễn, liên hệ quản trị.';
};

const transactionTypeLabel=(value:string)=>({
  Import:'Nhập kho',
  Export:'Xuất kho',
  AdjustmentIncrease:'Điều chỉnh tăng',
  AdjustmentDecrease:'Điều chỉnh giảm',
  TransferOut:'Xuất điều chuyển',
  TransferIn:'Nhập điều chuyển',
  TransferAdjustment:'Điều chỉnh điều chuyển',
  Ship:'Xuất giao hàng',
  StatusChange:'Đổi trạng thái',
  Move:'Di chuyển vị trí',
  Reversal:'Đảo giao dịch',
}[value]??value);

const inventoryStatusLabel=(value:string)=>({
  available:'Khả dụng',
  qchold:'Chờ kiểm tra chất lượng',
  quarantine:'Cách ly',
  damaged:'Hư hỏng',
  rejected:'Từ chối',
  blocked:'Bị chặn',
  expired:'Hết hạn',
  recallblocked:'Khóa thu hồi',
}[value.replace(/[_\s-]/g,'').toLowerCase()]??value);

const tone=(x:Event):'neutral'|'success'|'warning'|'danger'=>
  x.transactionType==='Reversal'?'warning':x.isReversed?'neutral':'success';

export default function InventoryTraceability(){
  // Read URL state from the router so navigation between two reversal links
  // updates the existing screen rather than reusing stale form/query data.
  const location=useLocation();
  const initialQuery=new URLSearchParams(location.search);
  const initialReferenceId = initialQuery.get('referenceId') ?? '';
  const linkedReversal = initialQuery.get('referenceType') === 'InventoryReversal'
    && /^[1-9]\d*$/.test(initialReferenceId) && Number.isSafeInteger(Number(initialReferenceId));
  const [form,setForm]=useState({
    warehouseId:'',productId:'',lotNumber:'',serialNumber:'',
    referenceType:linkedReversal?'InventoryReversal':'',
    referenceId:linkedReversal?initialReferenceId:''
  });
  const [result,setResult]=useState<Result|null>(null);
  const [loading,setLoading]=useState(false);
  const [validationError,setValidationError]=useState('');
  const [requestError,setRequestError]=useState('');
  const productIdRef=useRef<HTMLInputElement>(null);
  const referenceTypeRef=useRef<HTMLInputElement>(null);
  const referenceIdRef=useRef<HTMLInputElement>(null);

  useEffect(()=>{
    if(!linkedReversal)return;
    let active=true;
    setForm({
      warehouseId:'',productId:'',lotNumber:'',serialNumber:'',
      referenceType:'InventoryReversal',referenceId:initialReferenceId
    });
    setValidationError('');
    setRequestError('');
    setResult(null);
    const params=new URLSearchParams({
      referenceType:'InventoryReversal',referenceId:initialReferenceId,limit:'200'
    });
    setLoading(true);
    void apiClient.get<Result>('/api/inventory/traceability?'+params.toString())
      .then(response=>{if(active)setResult(response.data)})
      .catch(e=>{if(active)setRequestError(errorMessage(e))})
      .finally(()=>{if(active)setLoading(false)});
    return ()=>{active=false};
  },[linkedReversal,initialReferenceId]);

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
      setValidationError('Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.');
      if(!form.referenceType.trim())referenceTypeRef.current?.focus();
      else referenceIdRef.current?.focus();
      return;
    }
    if(!hasIdentity&&!hasReference){
      setValidationError('Nhập ít nhất Sản phẩm, Lô, Sê-ri hoặc Tham chiếu.');
      productIdRef.current?.focus();
      return;
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

  const referencePairErrorActive=Boolean(validationError&&referencePairInvalid);
  const resultStatus=loading
    ? 'Đang truy vết tồn kho.'
    : result
      ? `Đã tải ${result.currentBuckets.length} nhóm tồn kho hiện tại và ${result.events.length} sự kiện sổ cái.`
      : '';

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Truy vết & phả hệ tồn kho"
      description="Truy nhóm tồn hiện tại và dòng thời gian sổ cái bất biến theo Sản phẩm, Lô, Sê-ri hoặc chứng từ/tham chiếu trong phạm vi kho được phép."/>
    {requestError&&<p role="alert">{requestError}</p>}
    <UiCard title="Điều kiện truy vết">
      <form onSubmit={search} className="ui-form-grid" aria-busy={loading}>
        <UiToolbarField label="ID kho (tùy chọn)">
          <input type="number" min="1" value={form.warehouseId} onChange={e=>updateField('warehouseId',e.target.value)} inputMode="numeric"/>
        </UiToolbarField>
        <UiToolbarField label="ID sản phẩm">
          <input ref={productIdRef} type="number" min="1" value={form.productId} onChange={e=>updateField('productId',e.target.value)} inputMode="numeric"/>
        </UiToolbarField>
        <UiToolbarField label="Mã lô">
          <input value={form.lotNumber} onChange={e=>updateField('lotNumber',e.target.value)} autoComplete="off"/>
        </UiToolbarField>
        <UiToolbarField label="Số sê-ri">
          <input value={form.serialNumber} onChange={e=>updateField('serialNumber',e.target.value)} autoComplete="off"/>
        </UiToolbarField>
        <UiToolbarField label="Loại tham chiếu">
          <input
            ref={referenceTypeRef}
            value={form.referenceType}
            onChange={e=>updateField('referenceType',e.target.value)}
            aria-invalid={referencePairErrorActive||undefined}
            aria-describedby={referencePairErrorActive?'traceability-reference-help traceability-validation-error':'traceability-reference-help'}
            autoComplete="off"
          />
        </UiToolbarField>
        <UiToolbarField label="ID tham chiếu">
          <input
            ref={referenceIdRef}
            type="number"
            min="1"
            value={form.referenceId}
            onChange={e=>updateField('referenceId',e.target.value)}
            aria-invalid={referencePairErrorActive||undefined}
            aria-describedby={referencePairErrorActive?'traceability-reference-help traceability-validation-error':'traceability-reference-help'}
            inputMode="numeric"
          />
        </UiToolbarField>
        <p id="traceability-reference-help" className="ui-muted-text">
          Loại tham chiếu và ID tham chiếu là một cặp; nhập cả hai khi truy theo chứng từ.
        </p>
        {validationError&&<p id="traceability-validation-error" role="alert">{validationError}</p>}
        <button type="submit" disabled={loading}>{loading?'Đang truy vết...':'Truy vết'}</button>
        {resultStatus&&<p role="status" aria-live="polite" className="ui-muted-text">{resultStatus}</p>}
      </form>
    </UiCard>

    {result&&<div className="ui-stack">
      <UiCard title="Nhóm tồn kho hiện tại">
        <UiTableScroll><table aria-label="Nhóm tồn kho hiện tại phục vụ truy vết">
          <thead><tr><th>Sản phẩm</th><th>Kho / Vị trí</th><th>Trạng thái</th><th>Lô / Sê-ri</th><th>Tồn thực tế</th><th>Đã giữ</th></tr></thead>
          <tbody>{result.currentBuckets.length===0?<tr><td colSpan={6} className="ui-empty-cell">Không còn nhóm tồn kho hiện tại khớp điều kiện.</td></tr>:
            result.currentBuckets.map(x=><tr key={x.inventoryStockId}>
              <td><strong>{x.productCode}</strong><br/><small>{x.productName}</small></td>
              <td>{x.warehouseName}<br/><small>{x.locationCode??'—'}</small></td>
              <td><UiBadge>{inventoryStatusLabel(x.inventoryStatus)}</UiBadge></td>
              <td>{x.lotNumber??'—'} / {x.serialNumber??'—'}{x.expiryDate&&<><br/><small>HSD {new Date(x.expiryDate).toLocaleDateString('vi-VN')}</small></>}</td>
              <td>{x.onHandQuantity}</td><td>{x.reservedQuantity}</td>
            </tr>)}
          </tbody>
        </table></UiTableScroll>
      </UiCard>

      <UiCard title="Dòng thời gian sổ cái bất biến">
        <p className="ui-muted-text">Sự kiện đảo giao dịch là dấu mốc hiệu chỉnh, không xóa giao dịch gốc. Giao dịch gốc đã đảo được đánh dấu riêng.</p>
        <UiTableScroll><table aria-label="Dòng thời gian sổ cái phục vụ truy vết">
          <thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Sản phẩm</th><th>Vị trí / Trạng thái</th><th>Lô / Sê-ri</th><th>Số lượng</th><th>Tham chiếu</th><th>Chuỗi đảo giao dịch</th><th>Người thực hiện / Ghi chú</th></tr></thead>
          <tbody>{result.events.length===0?<tr><td colSpan={9} className="ui-empty-cell">Không có sự kiện sổ cái khớp điều kiện.</td></tr>:
            result.events.map(x=><tr key={x.transactionId}>
              <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
              <td><UiBadge tone={tone(x)}>{transactionTypeLabel(x.transactionType)}</UiBadge><br/><small>#{x.transactionId}{x.isReversed?' • đã đảo':''}{x.reversalOfTransactionId?(' • đảo #'+x.reversalOfTransactionId):''}</small></td>
              <td>{x.productCode}<br/><small>{x.warehouseName}</small></td>
              <td>{x.fromLocationCode&&x.toLocationCode?(x.fromLocationCode+' → '+x.toLocationCode):(x.locationCode??'—')}<br/><small>{x.fromInventoryStatus&&x.toInventoryStatus?(inventoryStatusLabel(x.fromInventoryStatus)+' → '+inventoryStatusLabel(x.toInventoryStatus)):inventoryStatusLabel(x.inventoryStatus)}</small></td>
              <td>{x.lotNumber??'—'} / {x.serialNumber??'—'}</td>
              <td>{x.quantity}</td>
              <td>{x.referenceType?(x.referenceType+' #'+(x.referenceId??'—')):'—'}</td>
              <td>
                {x.reversalOfTransactionId?<>Gốc #{x.reversalOfTransactionId}<br/></>:null}
                {x.correctiveTransactionId?<>Hiệu chỉnh #{x.correctiveTransactionId}<br/></>:null}
                {x.reversalTransactionId?<>Dấu đảo #{x.reversalTransactionId}</>:null}
                {!x.reversalOfTransactionId&&!x.correctiveTransactionId&&!x.reversalTransactionId?'—':null}
              </td>
              <td>{x.createdByName||('Người dùng #'+x.createdBy)}<br/><small>{x.note??''}</small></td>
            </tr>)}
          </tbody>
        </table></UiTableScroll>
      </UiCard>
    </div>}
  </UiPage>;
}
