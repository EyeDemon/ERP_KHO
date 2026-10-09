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
type RelatedDocument={
  warehouseId:number;warehouseName:string;referenceType:string;referenceId:number;
  eventCount:number;firstTransactionDate:string;lastTransactionDate:string;lastTransactionId:number;
};
type ReceiptExposure={
  receiptId:number;warehouseId:number;receiptCode:string;postedQuantity:number;
  lastPostedAt:string;ledgerEventCount:number;lastTransactionId:number;
};
type ShipmentExposure={
  shipmentId:number;warehouseId:number;shipmentCode:string;shipmentStatus:string;
  dispatchedAt?:string|null;dispatchedQuantity:number;ledgerEventCount:number;lastTransactionId:number;
};
type ShipmentPickingEvidence={
  shipmentId:number;shipmentCode:string;warehouseId:number;
  packingSessionId:number;packingSessionCode:string;
  pickingTaskId:number;pickingTaskCode:string;pickingTaskLineId:number;
  allocationId:number;sourceLocationCode:string;pickedQuantity:number;
};
type ShipmentHuEvidence={
  shipmentId:number;warehouseId:number;pickingTaskLineId:number;
  rootHandlingUnitId:number;rootHandlingUnitCode:string;
  contentHandlingUnitId:number;contentHandlingUnitCode:string;
  contentHandlingUnitBarcode:string;parentHandlingUnitId?:number|null;
  hierarchyPath:string;packedQuantity:number;
};
type Result={
  currentBuckets:Bucket[];
  events:Event[];
  relatedDocuments?:RelatedDocument[];
  relatedDocumentsTruncated?:boolean;
  receiptExposures?:ReceiptExposure[];
  receiptExposuresTruncated?:boolean;
  shipmentExposures?:ShipmentExposure[];
  shipmentExposuresTruncated?:boolean;
  shipmentPickingEvidence?:ShipmentPickingEvidence[];
  shipmentPickingEvidenceTruncated?:boolean;
  shipmentHuEvidence?:ShipmentHuEvidence[];
  shipmentHuEvidenceTruncated?:boolean;
  eventAnchorId?:number|null;
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

const shipmentStatusLabel=(value:string)=>({
  Draft:'Nháp',Ready:'Sẵn sàng',Staging:'Chờ bốc hàng',Loading:'Đang bốc hàng',
  Loaded:'Đã bốc hàng',Dispatched:'Đã xuất giao hàng',InTransit:'Đang vận chuyển',
  Delivered:'Đã xác nhận giao',DeliveryFailed:'Giao không thành công',
  Cancelled:'Đã hủy',ReturnToWarehouse:'Đang xử lý hoàn về kho',Completed:'Hoàn tất'
}[value]??value);

const referenceTypeLabel=(value:string)=>({
  GoodsReceipt:'Phiếu nhập kho', Receipt:'Phiếu nhập kho', StockTransfer:'Phiếu điều chuyển',
  Shipment:'Chuyến giao hàng', InventoryReversal:'Phiếu đảo giao dịch',
  InventoryAdjustment:'Phiếu điều chỉnh', InventoryMove:'Phiếu di chuyển',
  Return:'Phiếu trả hàng', PickTask:'Phiếu lấy hàng',
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
  const [eventOffset,setEventOffset]=useState(0);
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
    setEventOffset(0);
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
    setEventOffset(0);
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

  const requestTrace=async(offset:number,nextEventOffset=eventOffset,resetAnchor=false)=>{
    if(loading||offset<0||offset>50_000||offset%500!==0||
      nextEventOffset<0||nextEventOffset>50_000||nextEventOffset%eventLimit!==0)return;
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
    if(nextEventOffset>0 && (resetAnchor || result?.eventAnchorId==null)){
      setRequestError('Thiếu mốc lịch sử. Vui lòng truy vết lại từ trang đầu.');
      return;
    }
    setResult(null);
    setBucketOffset(offset);
    setEventOffset(nextEventOffset);
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
      if(nextEventOffset>0)params.set('eventOffset',String(nextEventOffset));
      // Keep the same ledger ID fence across event AND stock paging.
      // A new search explicitly requests a fresh fence instead.
      if(!resetAnchor && result?.eventAnchorId!=null)
        params.set('eventAnchorId',String(result.eventAnchorId));
      const response=await apiClient.get<Result>('/api/inventory/traceability?'+params.toString());
      if(sequence===requestSequence.current)setResult(response.data);
    }catch(e){
      if(sequence===requestSequence.current){setResult(null);setRequestError(errorMessage(e))}
    }finally{if(sequence===requestSequence.current)setLoading(false)}
  };
  const search=(e:FormEvent)=>{e.preventDefault();void requestTrace(0,0,true)};

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
      <form onSubmit={search} noValidate className="ui-form-grid" aria-busy={loading}>
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


      {form.productId&&(form.lotNumber.trim()||form.serialNumber.trim())&&!form.referenceType.trim()&&
        <UiCard title="Chứng từ có giao dịch cùng lô / sê-ri">
          <p className="ui-muted-text">
            Tổng hợp chứng từ có phát sinh sổ cái của đúng sản phẩm và lô/sê-ri trong các kho được cấp quyền, tại mốc lịch sử đang xem.
            Đây là liên hệ theo danh tính hàng hóa, chưa chứng minh quan hệ giao nhận, chủ sở hữu hoặc luồng thu hồi.
          </p>
          {result.relatedDocumentsTruncated&&<p role="status" className="ui-muted-text">
            Chỉ hiển thị 100 chứng từ mới nhất trong phạm vi truy vết. Hãy chọn một kho hoặc lọc thêm sê-ri để thu hẹp kết quả.
          </p>}
          <UiTableScroll><table aria-label="Chứng từ liên quan cùng lô hoặc sê-ri">
            <thead><tr><th>Chứng từ</th><th>Kho</th><th>Sự kiện</th><th>Phát sinh đầu</th><th>Phát sinh cuối</th></tr></thead>
            <tbody>{!result.relatedDocuments?.length?
              <tr><td colSpan={5} className="ui-empty-cell">Chưa có chứng từ có giao dịch sổ cái khớp lô hoặc sê-ri trong phạm vi được phép.</td></tr>:
              result.relatedDocuments.map(doc=>
                <tr key={doc.warehouseId+'-'+doc.referenceType+'-'+doc.referenceId}>
                  <td><strong>{referenceTypeLabel(doc.referenceType)}</strong><br/><small>{doc.referenceType} #{doc.referenceId}</small></td>
                  <td>{doc.warehouseName}</td>
                  <td>{doc.eventCount}</td>
                  <td>{new Date(doc.firstTransactionDate).toLocaleString('vi-VN')}</td>
                  <td>{new Date(doc.lastTransactionDate).toLocaleString('vi-VN')}</td>
                </tr>)}
            </tbody>
          </table></UiTableScroll>
        </UiCard>}


      {form.productId&&(form.lotNumber.trim()||form.serialNumber.trim())&&!form.referenceType.trim()&&
        <UiCard title="Phiếu nhập đã ghi sổ cùng lô / sê-ri">
          <p className="ui-muted-text">
            Chỉ hiển thị phiếu nhập đã ghi sổ thực tế (IMPORT) khớp sản phẩm và lô/sê-ri trong kho được cấp quyền.
            Số lượng là tổng nhập theo sổ cái tại mốc lịch sử, không chứng minh QC riêng từng lô hoặc nguồn trực tiếp của Shipment.
          </p>
          {result.receiptExposuresTruncated&&<p role="status" className="ui-muted-text">
            Đã đạt giới hạn 100 phiếu nhập; hãy chọn một kho hoặc thu hẹp lô/sê-ri để xem đầy đủ.
          </p>}
          <UiTableScroll><table aria-label="Phiếu nhập đã ghi sổ theo lô hoặc sê-ri">
            <thead><tr><th>Mã phiếu nhập</th><th>Trạng thái</th><th>Lần ghi sổ cuối</th><th>Đã nhập</th><th>Giao dịch sổ cái</th></tr></thead>
            <tbody>{!result.receiptExposures?.length?
              <tr><td colSpan={5} className="ui-empty-cell">Không có phiếu nhập đã ghi sổ khớp danh tính hàng hóa trong phạm vi được phép.</td></tr>:
              result.receiptExposures.map(receipt=>
                <tr key={receipt.warehouseId+'-'+receipt.receiptId}>
                  <td><strong>{receipt.receiptCode}</strong><br/><small>#{receipt.receiptId} • Kho #{receipt.warehouseId}</small></td>
                  <td><UiBadge>Đã ghi sổ</UiBadge></td>
                  <td>{new Date(receipt.lastPostedAt).toLocaleString('vi-VN')}</td>
                  <td>{receipt.postedQuantity}</td>
                  <td>{receipt.ledgerEventCount}</td>
                </tr>)}
            </tbody>
          </table></UiTableScroll>
        </UiCard>}

      {form.productId&&(form.lotNumber.trim()||form.serialNumber.trim())&&!form.referenceType.trim()&&
        <UiCard title="Shipment cần rà soát khi thu hồi">
          <p className="ui-muted-text">
            Chỉ hiển thị chuyến hàng có giao dịch xuất giao thực tế (SHIP) khớp sản phẩm và lô/sê-ri.
            Số lượng là tổng đã xuất, chưa trừ hàng hoàn; trạng thái giao hàng có thể thay đổi sau mốc lịch sử.
            Danh sách này là dữ liệu hỗ trợ rà soát, không tự động thu hồi hoặc xác định người nhận cuối.
          </p>
          {result.shipmentExposuresTruncated&&<p role="status" className="ui-muted-text">
            Đã đạt giới hạn 100 Shipment; hãy chọn một kho hoặc thu hẹp điều kiện để xem đầy đủ.
          </p>}
          <UiTableScroll><table aria-label="Shipment có giao dịch xuất giao cần rà soát">
            <thead><tr><th>Mã Shipment</th><th>Trạng thái hiện tại</th><th>Xuất giao lúc</th><th>Đã xuất</th><th>Giao dịch sổ cái</th></tr></thead>
            <tbody>{!result.shipmentExposures?.length?
              <tr><td colSpan={5} className="ui-empty-cell">Không tìm thấy Shipment đã ghi nhận xuất giao trong phạm vi truy vết.</td></tr>:
              result.shipmentExposures.map(shipment=>
                <tr key={shipment.warehouseId+'-'+shipment.shipmentId}>
                  <td><strong>{shipment.shipmentCode}</strong><br/><small>#{shipment.shipmentId} • Kho #{shipment.warehouseId}</small></td>
                  <td><UiBadge>{shipmentStatusLabel(shipment.shipmentStatus)}</UiBadge></td>
                  <td>{shipment.dispatchedAt?new Date(shipment.dispatchedAt).toLocaleString('vi-VN'):'—'}</td>
                  <td>{shipment.dispatchedQuantity}</td>
                  <td>{shipment.ledgerEventCount}</td>
                </tr>)}
            </tbody>
          </table></UiTableScroll>
        </UiCard>}


      {form.productId&&(form.lotNumber.trim()||form.serialNumber.trim())&&!form.referenceType.trim()&&
        <UiCard title="Liên kết Shipment → Packing → Picking đã ghi nhận">
          <p className="ui-muted-text">
            Liên kết được đối chiếu trực tiếp theo Shipment, phiên đóng gói, dòng Picking và phân bổ tồn,
            đồng thời bắt buộc có sự kiện xuất giao SHIP khớp vị trí, trạng thái và lô/sê-ri.
            Số lượng bên dưới là đã Picking, không phải lượng đã giao; chưa chứng minh quan hệ đến phiếu nhập hoặc Handling Unit riêng.
          </p>
          {result.shipmentPickingEvidenceTruncated&&<p role="status" className="ui-muted-text">
            Chỉ hiển thị 100 dòng Picking liên kết đầu tiên; hãy giới hạn thêm kho hoặc lô/sê-ri để xem đầy đủ.
          </p>}
          <UiTableScroll><table aria-label="Liên kết Shipment Packing Picking theo lô hoặc sê-ri">
            <thead><tr><th>Shipment</th><th>Packing</th><th>Picking</th><th>Phân bổ / Vị trí</th><th>Đã Picking</th></tr></thead>
            <tbody>{!result.shipmentPickingEvidence?.length?
              <tr><td colSpan={5} className="ui-empty-cell">Chưa có dòng Picking được xác thực bằng sự kiện xuất giao trong phạm vi truy vết.</td></tr>:
              result.shipmentPickingEvidence.map(link=>
                <tr key={link.shipmentId+'-'+link.pickingTaskLineId}>
                  <td><strong>{link.shipmentCode}</strong><small> #{link.shipmentId}</small></td>
                  <td>{link.packingSessionCode}<br/><small>#{link.packingSessionId}</small></td>
                  <td>{link.pickingTaskCode}<br/><small>Dòng #{link.pickingTaskLineId}</small></td>
                  <td>#{link.allocationId}<br/><small>{link.sourceLocationCode}</small></td>
                  <td>{link.pickedQuantity}</td>
                </tr>)}
            </tbody>
          </table></UiTableScroll>
        </UiCard>}


      {form.productId&&(form.lotNumber.trim()||form.serialNumber.trim())&&!form.referenceType.trim()&&
        <UiCard title="Chuỗi kiện HU của Shipment đã xuất">
          <p className="ui-muted-text">
            Chỉ hiển thị kiện đã đóng hàng theo dòng Picking và nối tới HU gốc thực sự gắn Shipment.
            Mỗi chuỗi được đối chiếu cùng giao dịch SHIP, sản phẩm, vị trí, trạng thái và lô/sê-ri trong kho được phép.
            Cấu trúc HU là trạng thái hiện tại, còn giao dịch SHIP được cố định theo mốc lịch sử.
            Số lượng đã đóng không phải số lượng giao thành công hoặc hoàn trả.
          </p>
          {result.shipmentHuEvidenceTruncated&&<p role="status" className="ui-muted-text">
            Dữ liệu kiện HU có giới hạn truy vết (100 dòng, tối đa 16 cấp cha). Hãy thu hẹp kho/lô/sê-ri; không coi danh sách này là đầy đủ để ra quyết định thu hồi.
          </p>}
          <UiTableScroll><table aria-label="Chuỗi Handling Unit gốc con đã xuất theo lô hoặc sê-ri">
            <thead><tr><th>Shipment</th><th>HU gốc</th><th>Chuỗi HU</th><th>Mã quét kiện chứa hàng</th><th>Dòng Picking</th><th>Đã đóng</th></tr></thead>
            <tbody>{!result.shipmentHuEvidence?.length?
              <tr><td colSpan={6} className="ui-empty-cell">Chưa xác minh được kiện HU gắn Shipment với sản phẩm/lô/sê-ri này.</td></tr>:
              result.shipmentHuEvidence.map(hu=>
                <tr key={hu.shipmentId+'-'+hu.contentHandlingUnitId+'-'+hu.pickingTaskLineId}>
                  <td>#{hu.shipmentId}</td>
                  <td><strong>{hu.rootHandlingUnitCode}</strong><br/><small>#{hu.rootHandlingUnitId}</small></td>
                  <td>{hu.hierarchyPath}<br/><small>Kiện chứa hàng: #{hu.contentHandlingUnitId}</small></td>
                  <td>{hu.contentHandlingUnitBarcode}</td>
                  <td>#{hu.pickingTaskLineId}</td>
                  <td>{hu.packedQuantity}</td>
                </tr>)}
            </tbody>
          </table></UiTableScroll>
        </UiCard>}

      <UiCard title="Dòng thời gian sổ cái bất biến">
        <p className="ui-muted-text">Sự kiện đảo giao dịch là dấu mốc hiệu chỉnh, không xóa giao dịch gốc. Lịch sử được cố định theo mốc lúc truy vết; chọn “Truy vết” để cập nhật sự kiện mới, tồn kho hiện tại vẫn được tải mới.</p>
        {result.eventsTruncated&&<p role="status" className="ui-muted-text">
          Chỉ lấy các sự kiện mới nhất trong giới hạn truy vấn và các sự kiện liên quan để đủ chuỗi đảo. Lịch sử còn dữ liệu cũ hơn; dùng “Sự kiện sau” để xem trang kế tiếp hoặc thu hẹp điều kiện tìm kiếm.
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
        <nav aria-label="Phân trang sự kiện sổ cái" className="ui-toolbar">
          <button type="button" disabled={loading||eventOffset===0}
            onClick={()=>void requestTrace(bucketOffset,eventOffset-eventLimit)}>Sự kiện trước</button>
          <span aria-live="polite">Trang sự kiện {Math.floor(eventOffset/eventLimit)+1}</span>
          <button type="button" disabled={loading||!result.eventsTruncated||eventOffset>=50_000}
            onClick={()=>void requestTrace(bucketOffset,eventOffset+eventLimit)}>Sự kiện sau</button>
          {eventOffset>=50_000&&result.eventsTruncated&&
            <span className="ui-muted-text">Đã đến giới hạn xem lịch sử; hãy lọc thêm để thu hẹp kết quả.</span>}
        </nav>
      </UiCard>
    </div>}
  </UiPage>;
}
