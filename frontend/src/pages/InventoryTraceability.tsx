import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { useLocation } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses, type TraceabilityWarehouse } from '../services/traceabilityWarehouses';
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
  transactionDate:string;createdBy:number;createdByName:string;note?:string|null;reasonCode?:string|null;reversalOfTransactionId?:number|null;
  correctiveTransactionId?:number|null;reversalTransactionId?:number|null;isReversed:boolean;
};
type Result={
  currentBuckets:Bucket[];
  events:Event[];
  eventsTruncated?:boolean;
  bucketsTruncated?:boolean;
};

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
  const requestedReferenceType = initialQuery.get('referenceType');
  const linkedReferenceType = requestedReferenceType === 'InventoryReversal' || requestedReferenceType === 'StockTransfer'
    ? requestedReferenceType : null;
  const linkedDocument = linkedReferenceType !== null
    && /^[1-9]\d*$/.test(initialReferenceId) && Number.isSafeInteger(Number(initialReferenceId));
  const [form,setForm]=useState({
    warehouseId:'',productId:'',lotNumber:'',serialNumber:'',
    referenceType:linkedDocument?(linkedReferenceType??''):'',
    referenceId:linkedDocument?initialReferenceId:''
  });
  const [result,setResult]=useState<Result|null>(null);
  const [loading,setLoading]=useState(false);
  const [eventLimit,setEventLimit]=useState<50|100|200|500>(200);
  const [bucketOffset,setBucketOffset]=useState(0);
  const [warehouses,setWarehouses]=useState<TraceabilityWarehouse[]>([]);
  const [warehousesLoading,setWarehousesLoading]=useState(true);
  const [warehousesError,setWarehousesError]=useState('');
  const warehouseRequestSequence=useRef(0);
  const [validationError,setValidationError]=useState('');
  const [requestError,setRequestError]=useState('');
  const productIdRef=useRef<HTMLInputElement>(null);
  const referenceTypeRef=useRef<HTMLInputElement>(null);
  const referenceIdRef=useRef<HTMLInputElement>(null);
  // One generation counter guards both automatic deep links and manual searches.
  const requestSequence=useRef(0);

  const loadWarehouses=useCallback(async()=>{
    const seq=++warehouseRequestSequence.current;
    setWarehousesLoading(true);
    setWarehousesError('');
    try{
      const accessible=await getTraceabilityWarehouses();
      if(seq===warehouseRequestSequence.current)setWarehouses(accessible);
    }catch{
      if(seq===warehouseRequestSequence.current){
        setWarehouses([]);
        setWarehousesError('Không thể tải danh sách kho được phân quyền. Hãy thử lại.');
      }
    }finally{
      if(seq===warehouseRequestSequence.current)setWarehousesLoading(false);
    }
  },[]);

  useEffect(()=>{
    void loadWarehouses();
    return ()=>{warehouseRequestSequence.current+=1};
  },[loadWarehouses]);

  useEffect(()=>{
    const sequence=++requestSequence.current;
    setBucketOffset(0);
    if(!linkedDocument){
      // A route transition back to an unfiltered trace view must not leave
      // the previous document or warehouse filters visible as current data.
      setForm({warehouseId:'',productId:'',lotNumber:'',serialNumber:'',referenceType:'',referenceId:''});
      setEventLimit(200);
      setResult(null);
      setRequestError('');
      setValidationError('');
      setLoading(false);
      return;
    }
    let active=true;
    setForm({
      warehouseId:'',productId:'',lotNumber:'',serialNumber:'',
      referenceType:linkedReferenceType??'',referenceId:initialReferenceId
    });
    // Linked searches use the default 200-event window; keep the UI in sync.
    setEventLimit(200);
    setValidationError('');
    setRequestError('');
    setResult(null);
    const params=new URLSearchParams({
      referenceType:linkedReferenceType??'',referenceId:initialReferenceId,limit:'200'
    });
    setLoading(true);
    void apiClient.get<Result>('/api/inventory/traceability?'+params.toString())
      .then(response=>{if(active&&sequence===requestSequence.current)setResult(response.data)})
      .catch(e=>{if(active&&sequence===requestSequence.current)setRequestError(errorMessage(e))})
      .finally(()=>{if(active&&sequence===requestSequence.current)setLoading(false)});
    return ()=>{active=false};
  },[linkedDocument,linkedReferenceType,initialReferenceId]);

  const referencePairInvalid=Boolean(
    (form.referenceType.trim()&&!form.referenceId)||(!form.referenceType.trim()&&form.referenceId)
  );

  const invalidateResults=()=>{
    // Changing filters invalidates the old results and pending responses;
    // paging must never reuse stale query conditions.
    requestSequence.current+=1;
    setBucketOffset(0);
    setResult(null);
    setLoading(false);
  };

  const updateField=(field:keyof typeof form,value:string)=>{
    setForm(x=>({...x,[field]:value}));
    invalidateResults();
    if(validationError)setValidationError('');
    if(requestError)setRequestError('');
  };

  const validPositiveId=(value:string)=>/^[1-9]\d*$/.test(value)&&Number.isSafeInteger(Number(value));

  const requestTrace=async(offset:number)=>{
    if(loading||offset<0||offset>50_000||offset%500!==0)return;
    setValidationError('');setRequestError('');
    const hasIdentity=form.warehouseId||form.productId||form.lotNumber.trim()||form.serialNumber.trim();
    const hasReference=form.referenceType.trim()&&form.referenceId;
    if(referencePairInvalid){
      setValidationError('Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.');
      if(!form.referenceType.trim())referenceTypeRef.current?.focus();
      else referenceIdRef.current?.focus();
      return;
    }
    if(!hasIdentity&&!hasReference){
      setValidationError('Nhập ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu.');
      productIdRef.current?.focus();
      return;
    }
    if(form.productId&&!validPositiveId(form.productId)){
      setValidationError('ID sản phẩm phải là số nguyên dương hợp lệ.');
      productIdRef.current?.focus();
      return;
    }
    if(form.referenceId&&!validPositiveId(form.referenceId)){
      setValidationError('ID tham chiếu phải là số nguyên dương hợp lệ.');
      referenceIdRef.current?.focus();
      return;
    }
    setResult(null);
    setBucketOffset(offset);
    setLoading(true);
    const sequence=++requestSequence.current;
    try{
      const params=new URLSearchParams();
      if(form.warehouseId)params.set('warehouseId',form.warehouseId);
      if(form.productId)params.set('productId',form.productId);
      if(form.lotNumber.trim())params.set('lotNumber',form.lotNumber.trim());
      if(form.serialNumber.trim())params.set('serialNumber',form.serialNumber.trim());
      if(form.referenceType.trim())params.set('referenceType',form.referenceType.trim());
      if(form.referenceId)params.set('referenceId',form.referenceId);
      params.set('limit',String(eventLimit));
      if(offset>0)params.set('bucketOffset',String(offset));
      const response=await apiClient.get<Result>('/api/inventory/traceability?'+params.toString());
      if(sequence===requestSequence.current)setResult(response.data);
    }catch(e){
      if(sequence===requestSequence.current){setResult(null);setRequestError(errorMessage(e))}
    }finally{if(sequence===requestSequence.current)setLoading(false)}
  };
  const search=(e:FormEvent)=>{e.preventDefault();void requestTrace(0)};

  const referencePairErrorActive=Boolean(validationError&&referencePairInvalid);
  const resultStatus=loading
    ? 'Đang truy vết tồn kho.'
    : result
      ? `Đã tải ${result.currentBuckets.length} nhóm tồn kho hiện tại và ${result.events.length} sự kiện sổ cái.`
      : '';

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Truy vết & phả hệ tồn kho"
      description="Xem toàn bộ nhóm tồn của một kho được phép, hoặc truy theo sản phẩm, lô, sê-ri và chứng từ. Dòng thời gian sổ cái giữ nguyên lịch sử bất biến."/>
    {requestError&&<p role="alert">{requestError}</p>}
    <UiCard title="Điều kiện truy vết">
      <form onSubmit={search} className="ui-form-grid" aria-busy={loading}>
        <UiToolbarField label="Kho truy vết">
          <select aria-label="Kho truy vết" value={form.warehouseId}
            disabled={warehousesLoading||Boolean(warehousesError)||warehouses.length===0}
            aria-describedby="traceability-warehouse-help"
            onChange={e=>updateField('warehouseId',e.target.value)}>
            <option value="">Tất cả kho được phân quyền (cần thêm bộ lọc)</option>
            {warehouses.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
          </select>
        </UiToolbarField>
        <p id="traceability-warehouse-help" className="ui-muted-text">
          Chọn một kho để xem toàn bộ tồn hiện tại và sự kiện gần nhất; 500 nhóm tồn mỗi trang. Để truy vết nhiều kho, cần nhập thêm sản phẩm, lô, sê-ri hoặc chứng từ.
        </p>
        {warehousesLoading&&<p role="status" className="ui-muted-text">Đang tải kho được cấp quyền...</p>}
        {warehousesError&&<div className="ui-stack">
          <p role="alert">{warehousesError}</p>
          <button type="button" onClick={()=>void loadWarehouses()}>Tải lại danh sách kho</button>
        </div>}
        {!warehousesLoading&&!warehousesError&&warehouses.length===0&&
          <p role="status" className="ui-muted-text">
            Chưa có kho được cấp quyền truy vết. Liên hệ quản trị để kiểm tra quyền truy cập.
          </p>}
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
          Loại tham chiếu và ID tham chiếu là một cặp; nhập cả hai khi truy theo chứng từ. Kết hợp chứng từ với bộ lọc khác chỉ lấy nhóm tồn liên quan đến chứng từ đó, kể cả khi giới hạn sự kiện thấp.
        </p>
        {validationError&&<p id="traceability-validation-error" role="alert">{validationError}</p>}
        <UiToolbarField label="Giới hạn sự kiện">
          <select aria-label="Giới hạn sự kiện truy vết" value={eventLimit} disabled={loading}
            onChange={e=>{
              setEventLimit(Number(e.target.value) as 50|100|200|500);
              invalidateResults();
            }}>
            <option value={50}>50 sự kiện</option>
            <option value={100}>100 sự kiện</option>
            <option value={200}>200 sự kiện</option>
            <option value={500}>500 sự kiện</option>
          </select>
        </UiToolbarField>
        <button type="submit" disabled={loading}>{loading?'Đang truy vết...':'Truy vết'}</button>
        {resultStatus&&<p role="status" aria-live="polite" className="ui-muted-text">{resultStatus}</p>}
      </form>
    </UiCard>

    {result&&<div className="ui-stack">
      <UiCard title="Nhóm tồn kho hiện tại">
        {result.bucketsTruncated&&<p role="status" className="ui-muted-text">
          Chỉ hiển thị 500 nhóm tồn của trang hiện tại. Vẫn còn dữ liệu khác; chọn “Trang sau” hoặc lọc thêm theo kho, sản phẩm, lô, sê-ri.
        </p>}
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
        <nav aria-label="Phân trang nhóm tồn kho" className="ui-toolbar">
          <button type="button" disabled={loading||bucketOffset===0}
            onClick={()=>void requestTrace(bucketOffset-500)}>Trang trước</button>
          <span aria-live="polite">Trang {Math.floor(bucketOffset/500)+1}</span>
          <button type="button" disabled={loading||!result.bucketsTruncated||bucketOffset>=50_000}
            onClick={()=>void requestTrace(bucketOffset+500)}>Trang sau</button>
          {bucketOffset>=50_000&&result.bucketsTruncated&&
            <span className="ui-muted-text">Đã tới giới hạn xem trang; hãy lọc chi tiết hơn.</span>}
        </nav>
      </UiCard>

      <UiCard title="Dòng thời gian sổ cái bất biến">
        <p className="ui-muted-text">Sự kiện đảo giao dịch là dấu mốc hiệu chỉnh, không xóa giao dịch gốc. Giao dịch gốc đã đảo được đánh dấu riêng.</p>
        {result.eventsTruncated&&<p role="status" className="ui-muted-text">
          Chỉ lấy các sự kiện mới nhất trong giới hạn truy vấn và các sự kiện liên quan để đủ chuỗi đảo. Lịch sử còn dữ liệu cũ hơn; hãy tăng giới hạn hoặc thu hẹp điều kiện tìm kiếm.
        </p>}
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
              <td>{x.createdByName||('Người dùng #'+x.createdBy)}<br/>
                {x.reasonCode&&<small>Mã lý do: {x.reasonCode}<br/></small>}
                <small>{x.note??''}</small>
              </td>
            </tr>)}
          </tbody>
        </table></UiTableScroll>
      </UiCard>
    </div>}
  </UiPage>;
}
