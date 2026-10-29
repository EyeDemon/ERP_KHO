import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
} from '../ui/ProductionUi';
import { InboundPlanningFeedback, InboundPlanningToolbar, productUnitOptions, runInboundStateCommand } from './inboundPlanningShared';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import './InboundPlanning.css';

type AsnList={
  id:number;code:string;purchaseOrderId?:number;purchaseOrderCode?:string;supplierCode:string;supplierName:string;
  warehouseId:number;warehouseName:string;status:string;expectedArrivalAtUtc?:string;baseExpectedQuantity:number;
};
type AsnLine={
  id:number;purchaseOrderLineId?:number;lineNo:number;productId:number;productCode:string;productName:string;
  expectedQuantity:number;operationUnitCode:string;baseExpectedQuantity:number;baseUnitCode:string;
};
type AsnDetail=AsnList&{carrierName?:string;vehiclePlate?:string;note?:string;rowVersion?:string;lines:AsnLine[]};
type PurchaseOrderList={
  id:number;code:string;supplierCode:string;supplierName:string;warehouseId:number;warehouseName:string;status:string;baseOrderedQuantity:number;
};
type PurchaseOrderLine={id:number;lineNo:number;externalLineId:string;productId:number;productCode:string;productName:string;orderedQuantity:number;operationUnitCode:string;baseOrderedQuantity:number;baseUnitCode:string};
type PurchaseOrderDetail=PurchaseOrderList&{lines:PurchaseOrderLine[]};
type Warehouse={id:number;name:string};
type Partner={id:number;code:string;name:string};
type ProductUom={unitId:number;unitCode:string;unitName:string};
type Product={id:number;code:string;name:string;unitId:number;unitCode:string;unitName:string;uoms:ProductUom[]};
type LineForm={purchaseOrderLineId:number|'';productId:number|'';operationUnitId:number|'';expectedQuantity:number|''};

const labels:Record<string,string>={
  Draft:'Nháp',Confirmed:'Đã xác nhận',InTransit:'Đang vận chuyển',Arrived:'Đã đến cổng',
  Receiving:'Đang tiếp nhận',Received:'Đã hoàn tất',Completed:'Đã hoàn tất',Cancelled:'Đã hủy',
};
const stagingSignalLabels:Record<string,string>={
  STAGE_PRE_ALLOCATED:'Đã cấp phát trước bãi đệm tiếp nhận',
  'STAGE-IN-01':'Làn đệm tiếp nhận 01 (STAGE-IN-01)',
  'ASN-NEXT-ARRIVAL':'Lô ASN dự kiến tiếp theo',
};
const translateStagingSignal=(value?:string|null)=>value?(stagingSignalLabels[value]??value):'';
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Completed'||status==='Received'?'success':status==='Arrived'||status==='Receiving'?'warning':status==='Cancelled'?'danger':'neutral';
const dateTime=(value?:string)=>value?new Date(value).toLocaleString('vi-VN'):'—';
const normalizeUnitCode=(unitCode?:string)=>(unitCode==='QA_UNIT'||unitCode==='QA-PCS')?'THÙNG':(unitCode||'');
const emptyLine=():LineForm=>({purchaseOrderLineId:'',productId:'',operationUnitId:'',expectedQuantity:''});

export default function Asns(){
  const canRead=usePermission('asn.read');
  const canCreate=usePermission('asn.create');
  const canUpdate=usePermission('asn.update');
  const canConfirm=usePermission('asn.confirm');
  const canReceive=usePermission('asn.receive');
  const canCancel=usePermission('asn.cancel');
  const canReadPo=usePermission('purchase_order.read');
  const canReadWarehouses=usePermission('warehouse.read');
  const canReadProducts=usePermission('product.read');
  const canReadPartners=usePermission('partner.read');

  const [items,setItems]=useState<AsnList[]>([]);
  const [selected,setSelected]=useState<AsnDetail|null>(null);
  const [purchaseOrders,setPurchaseOrders]=useState<PurchaseOrderList[]>([]);
  const [poDetail,setPoDetail]=useState<PurchaseOrderDetail|null>(null);
  const [warehouses,setWarehouses]=useState<Warehouse[]>([]);
  const [suppliers,setSuppliers]=useState<Partner[]>([]);
  const [products,setProducts]=useState<Product[]>([]);
  const [statusFilter,setStatusFilter]=useState('');
  const [search,setSearch]=useState('');
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [success,setSuccess]=useState('');
  const [creating,setCreating]=useState(false);
  const [showCreateForm,setShowCreateForm]=useState(false);
  const [busy,setBusy]=useState('');
  const [stagingBusy,setStagingBusy]=useState(false);
  const [stagingReservation,setStagingReservation]=useState<{stagingReservationId:string;stagingLaneCode:string}|null>(null);
  const requestSeq=useRef(0);
  const mutationLock=useRef(new Set<string>());

  const preAllocateStaging=async()=>{
    if(stagingBusy)return;
    setStagingBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/asns/pre-allocate-staging',{
        warehouseId:warehouseId?Number(warehouseId):(items[0]?.warehouseId??undefined),
        asnNumber:selected?.code||items[0]?.code||'ASN-NEXT-ARRIVAL',
      });
      if(res?.data?.stagingReservationId&&res?.data?.stagingLaneCode){
        setStagingReservation({
          stagingReservationId:String(res.data.stagingReservationId),
          stagingLaneCode:translateStagingSignal(String(res.data.stagingLaneCode)),
        });
      }else{
        setStagingReservation(null);
        setError('Phản hồi cấp phát bãi đệm tiếp nhận không hợp lệ từ máy chủ.');
      }
    }catch(err:unknown){
      setStagingReservation(null);
      setError(permissionError(err,'Không thể cấp phát trước bãi đệm tiếp nhận.'));
    }finally{
      setStagingBusy(false);
    }
  };

  const [code,setCode]=useState('');
  const [purchaseOrderId,setPurchaseOrderId]=useState<number|''>('');
  const [supplierId,setSupplierId]=useState<number|''>('');
  const [warehouseId,setWarehouseId]=useState<number|''>('');
  const [expectedArrival,setExpectedArrival]=useState('');
  const [carrierName,setCarrierName]=useState('');
  const [vehiclePlate,setVehiclePlate]=useState('');
  const [note,setNote]=useState('');
  const [lines,setLines]=useState<LineForm[]>([emptyLine()]);

  const canPrepareCreate=canCreate&&canReadProducts&&canReadWarehouses&&canReadPartners;

  const loadList=useCallback(async()=>{
    if(!canRead){setItems([]);setSelected(null);setLoading(false);return;}
    const request=++requestSeq.current;setLoading(true);setError('');
    try{
      const response=await apiClient.get('/api/asns',{params:{status:statusFilter||undefined}});
      if(request!==requestSeq.current||!hasPermission('asn.read'))return;
      setItems(response.data);
    }catch(err:unknown){
      if(request!==requestSeq.current)return;
      setItems([]);setSelected(null);setError(permissionError(err,'Không thể tải danh sách ASN. Vui lòng thử lại.'));
    }finally{if(request===requestSeq.current)setLoading(false);}
  },[canRead,statusFilter]);

  const loadReferences=useCallback(async()=>{
    if(!canCreate)return;
    try{
      const [poRes,warehouseRes,productRes,partnerRes]=await Promise.all([
        canReadPo?apiClient.get('/api/purchase-orders'):Promise.resolve({data:[]}),
        canReadWarehouses?apiClient.get('/api/warehouses'):Promise.resolve({data:[]}),
        canReadProducts?apiClient.get('/api/products'):Promise.resolve({data:[]}),
        canReadPartners?apiClient.get('/api/business-partners',{params:{role:'supplier',active:true,pageSize:100}}):Promise.resolve({data:{items:[]}}),
      ]);
      setPurchaseOrders(hasPermission('purchase_order.read')?poRes.data:[]);
      setWarehouses(hasPermission('warehouse.read')?warehouseRes.data:[]);
      setProducts(hasPermission('product.read')?productRes.data:[]);
      setSuppliers(hasPermission('partner.read')?partnerRes.data.items:[]);
    }catch(err:unknown){
      setPurchaseOrders([]);setWarehouses([]);setProducts([]);setSuppliers([]);
      setError(permissionError(err,'Không thể tải dữ liệu tham khảo để tạo ASN.'));
    }
  },[canCreate,canReadPo,canReadWarehouses,canReadProducts,canReadPartners]);

  useEffect(()=>{void loadList();return()=>{requestSeq.current++;}},[loadList]);
  useEffect(()=>{void loadReferences();},[loadReferences]);

  const filtered=useMemo(()=>{
    const q=sanitizeScannerCode(search).toLowerCase();
    if(!q)return items;
    return items.filter(item=>[item.code,item.purchaseOrderCode??'',item.supplierCode,item.supplierName,item.warehouseName].join(' ').toLowerCase().includes(q));
  },[items,search]);

  const arrivalSummary=useMemo(()=>{
    const nowMs=Date.now();
    const overdueArrivalCount=items.filter(x=>(x.status==='Confirmed'||x.status==='InTransit')&&x.expectedArrivalAtUtc&&new Date(x.expectedArrivalAtUtc).getTime()<nowMs).length;
    const linkedPoAsnCount=items.filter(x=>x.purchaseOrderId!=null).length;
    const unlinkedPoAsnCount=items.filter(x=>x.purchaseOrderId==null).length;
    const inboundExpectedVolume=roundQty4(items
      .filter(x=>['Confirmed','InTransit','Arrived','Receiving'].includes(x.status))
      .reduce((sum,x)=>sum+(x.baseExpectedQuantity||0),0));
    return {
      overdueArrivalCount,
      linkedPoAsnCount,
      unlinkedPoAsnCount,
      inboundExpectedVolume,
    };
  },[items]);

  const viewDetail=async(id:number)=>{
    setError('');setSuccess('');
    try{setSelected((await apiClient.get('/api/asns/'+id)).data);}
    catch(err:unknown){setSelected(null);setError(permissionError(err,'Không tìm thấy ASN hoặc bạn không có quyền truy cập.'));}
  };

  const choosePurchaseOrder=async(value:string)=>{
    const id=value?Number(value):'';
    setPurchaseOrderId(id);setPoDetail(null);setLines([emptyLine()]);
    if(!id)return;
    try{
      const detail=(await apiClient.get('/api/purchase-orders/'+id)).data as PurchaseOrderDetail;
      setPoDetail(detail);
      const first=detail.lines[0];
      if(first){
        const product=products.find(x=>x.id===first.productId);
        setLines([{purchaseOrderLineId:first.id,productId:first.productId,operationUnitId:product?.unitId??'',expectedQuantity:first.orderedQuantity}]);
      }
    }catch(err:unknown){setPurchaseOrderId('');setError(permissionError(err,'Không thể tải chi tiết PO đã chọn.'));}
  };

  const changeLine=(index:number,patch:Partial<LineForm>)=>{
    setLines(current=>current.map((line,i)=>{
      if(i!==index)return line;
      const next={...line,...patch};
      if('purchaseOrderLineId' in patch&&patch.purchaseOrderLineId){
        const poLine=poDetail?.lines.find(x=>x.id===patch.purchaseOrderLineId);
        const product=products.find(x=>x.id===poLine?.productId);
        next.productId=poLine?.productId??'';
        next.operationUnitId=product?.unitId??'';
        next.expectedQuantity=poLine?.orderedQuantity??'';
      }else if('productId' in patch){
        const product=products.find(x=>x.id===patch.productId);
        next.operationUnitId=product?.unitId??'';
      }
      return next;
    }));
  };

  const resetForm=()=>{
    setCode('');setPurchaseOrderId('');setPoDetail(null);setSupplierId('');setWarehouseId('');
    setExpectedArrival('');setCarrierName('');setVehiclePlate('');setNote('');setLines([emptyLine()]);
  };

  const submitCreate=async(event:FormEvent)=>{
    event.preventDefault();
    if(!canPrepareCreate||creating)return;
    setError('');setSuccess('');
    if(!purchaseOrderId&&(!supplierId||!warehouseId)){setError('ASN không gắn PO cần nhà cung cấp và kho nhận.');return;}
    if(lines.length===0||lines.some(line=>!line.productId||!line.operationUnitId||roundQty4(Number(line.expectedQuantity))<=0||(purchaseOrderId&&!line.purchaseOrderLineId))){
      setError('Mỗi dòng ASN cần nguồn PO (nếu có), sản phẩm, đơn vị và số lượng lớn hơn 0.');return;
    }
    setCreating(true);
    const cleanCode=sanitizeScannerCode(code);
    const action='asn-create:'+cleanCode;
    try{
      const payload={
        code:cleanCode,purchaseOrderId:purchaseOrderId||null,
        supplierId:purchaseOrderId?0:supplierId,warehouseId:purchaseOrderId?0:warehouseId,
        expectedArrivalAtUtc:expectedArrival?new Date(expectedArrival).toISOString():null,
        carrierName:carrierName||null,vehiclePlate:vehiclePlate||null,note:note||null,
        lines:lines.map(line=>({
          purchaseOrderLineId:line.purchaseOrderLineId||null,productId:Number(line.productId),
          operationUnitId:Number(line.operationUnitId),expectedQuantity:roundQty4(Number(line.expectedQuantity)),
        })),
      };
      const response=await apiClient.post('/api/asns',payload,{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action);
      setSuccess('Đã tạo ASN nháp. ASN chỉ ghi nhận hàng dự kiến, chưa tăng tồn kho.');
      resetForm();await loadList();await viewDetail(response.data.id);
    }catch(err:unknown){setError(permissionError(err,'Không thể tạo ASN. Kiểm tra số lượng PO và dữ liệu tham khảo.'));}
    finally{setCreating(false);}
  };

  const command=(path:string,action:string,successMessage:string)=>
    runInboundStateCommand({
      selected,path,action,successMessage,errorMessage:'Không thể chuyển trạng thái ASN. Vui lòng tải lại và thử lại.',
      mutationLock,setBusy,setError,setSuccess,setSelected,reload:loadList,
    });

  const eligiblePurchaseOrders=purchaseOrders.filter(x=>['Open','PartiallyReceived'].includes(x.status));

  if(loading&&items.length===0)return <p role="status">Đang tải ASN...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Nhập kho"
      title="ASN dự kiến"
      description="Theo dõi lô hàng dự kiến từ nhà cung cấp tới kho trước khi tiếp nhận. Hoàn tất ASN vẫn chưa tạo tồn kho."
      actions={canCreate ? (
        <button type="button" className="ui-primary-button" onClick={() => setShowCreateForm(prev => !prev)}>
          {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo ASN nháp'}
        </button>
      ) : undefined}
    />

    <InboundPlanningFeedback error={error} success={success}/>

    <UiMetricGrid>
      <UiMetric value={items.length} label="ASN trong phạm vi hiện tại"/>
      <UiMetric value={items.filter(x=>x.status==='InTransit').length} label="Đang vận chuyển"/>
      <UiMetric value={items.filter(x=>x.status==='Arrived').length} label="Đã đến cổng"/>
      <UiMetric value={items.filter(x=>x.status==='Receiving').length} label="Đang tiếp nhận"/>
    </UiMetricGrid>

    <UiCard title="Giám sát Lịch đến Kho & Liên kết Đơn mua của ASN">
      <div className="inbound-planning-detail-meta">
        <div>
          <strong>ASN trễ giờ dự kiến đến</strong><br/>
          <UiBadge tone={arrivalSummary.overdueArrivalCount>0?'danger':'success'}>
            {arrivalSummary.overdueArrivalCount} ASN trễ lịch
          </UiBadge>
        </div>
        <div>
          <strong>Liên kết đơn mua nguồn</strong><br/>
          {arrivalSummary.linkedPoAsnCount} gắn đơn mua / {arrivalSummary.unlinkedPoAsnCount} chưa gắn đơn mua
        </div>
        <div>
          <strong>Tổng SL cơ sở đang về / chờ nhận</strong><br/>
          {arrivalSummary.inboundExpectedVolume.toLocaleString('vi-VN')}
        </div>
      </div>
      <div className="ui-inline-actions" style={{marginTop:8}}>
        <button type="button" disabled={!!busy||creating||stagingBusy} onClick={()=>void preAllocateStaging()}>
          Cấp phát Trước Bãi đệm Tiếp nhận
        </button>
      </div>
      {stagingReservation&&(
        <div role="status" style={{marginTop:8,padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13,lineHeight:1.5,whiteSpace:'normal',wordBreak:'break-word'}}>
          {`Mã cấp phát: ${stagingReservation.stagingReservationId} • Vị trí đệm: ${stagingReservation.stagingLaneCode}`}
        </div>
      )}
    </UiCard>

    <InboundPlanningToolbar labels={labels} statusFilter={statusFilter} setStatusFilter={setStatusFilter} search={search} setSearch={setSearch} placeholder="Mã ASN, đơn mua, nhà cung cấp, kho"/>

    <div className="inbound-planning-grid">
      <UiCard title="Danh sách ASN">
        {filtered.length===0?<UiEmptyState title="Chưa có ASN phù hợp" detail="Thay đổi bộ lọc hoặc tạo ASN mới nếu bạn có quyền."/>:
          <UiTableScroll>
            <table className="inbound-planning-table ui-table-compact" aria-label="Danh sách ASN">
              <thead><tr><th>Mã ASN</th><th>Đơn mua (PO)</th><th>Nhà cung cấp</th><th>Kho nhận</th><th>Dự kiến đến</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL dự kiến</th><th>Trạng thái</th></tr></thead>
              <tbody>{filtered.map(item=><tr key={item.id}>
                <td className="ui-cell-nowrap" style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void viewDetail(item.id)}><strong style={{whiteSpace:'nowrap'}}>{item.code}</strong></button></td>
                <td style={{whiteSpace:'nowrap'}}>{item.purchaseOrderCode||'Không gắn đơn mua'}</td>
                <td><span style={{whiteSpace:'nowrap'}}>{item.supplierCode}</span><br/><small>{item.supplierName}</small></td>
                <td>{item.warehouseName}</td>
                <td>{dateTime(item.expectedArrivalAtUtc)}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{item.baseExpectedQuantity.toLocaleString('vi-VN')}</td>
                <td><UiBadge tone={tone(item.status)}>{labels[item.status]??item.status}</UiBadge></td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>}
      </UiCard>

      <div className="ui-stack">
        {selected&&<UiCard title={'Chi tiết '+selected.code}>
          <div className="inbound-planning-detail-meta">
            <div><strong>Trạng thái</strong><br/><UiBadge tone={tone(selected.status)}>{labels[selected.status]??selected.status}</UiBadge></div>
            <div><strong>Đơn mua (PO)</strong><br/>{selected.purchaseOrderCode||'Không gắn đơn mua'}</div>
            <div><strong>Nhà cung cấp</strong><br/>{selected.supplierCode} — {selected.supplierName}</div>
            <div><strong>Kho nhận</strong><br/>{selected.warehouseName}</div>
            <div><strong>Dự kiến đến</strong><br/>{dateTime(selected.expectedArrivalAtUtc)}</div>
            <div><strong>Đơn vị vận chuyển</strong><br/>{selected.carrierName||'—'}</div>
            <div><strong>Biển số xe</strong><br/>{selected.vehiclePlate||'—'}</div>
            <div><strong>Tổng SL cơ sở</strong><br/>{selected.baseExpectedQuantity.toLocaleString('vi-VN')}</div>
          </div>
          {selected.note&&<p><strong>Ghi chú:</strong> {selected.note}</p>}
          <div className="inbound-planning-actions">
            {canConfirm&&selected.status==='Draft'&&<button className="ui-primary-button" disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/confirm','asn-confirm:'+selected.id,'ASN đã được xác nhận.')}>Xác nhận ASN</button>}
            {canUpdate&&selected.status==='Confirmed'&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/mark-in-transit','asn-transit:'+selected.id,'ASN đã chuyển sang trạng thái đang vận chuyển.')}>Đánh dấu đang vận chuyển</button>}
            {canReceive&&selected.status==='InTransit'&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/arrive','asn-arrive:'+selected.id,'Đã ghi nhận ASN đến kho.')}>Ghi nhận đến kho</button>}
            {canReceive&&selected.status==='Arrived'&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/start-receiving','asn-receiving:'+selected.id,'Đã bắt đầu tiếp nhận ASN.')}>Bắt đầu tiếp nhận</button>}
            {canReceive&&selected.status==='Receiving'&&<button className="ui-primary-button" disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/complete','asn-complete:'+selected.id,'ASN đã hoàn tất. Tồn kho vẫn chưa thay đổi cho tới khi ghi sổ phiếu nhập.')}>Hoàn tất ASN</button>}
            {canCancel&&['Draft','Confirmed'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/cancel','asn-cancel:'+selected.id,'ASN đã được hủy.')}>Hủy ASN</button>}
          </div>
          <UiTableScroll>
            <table className="ui-table-compact" aria-label={'Dòng ASN '+selected.code}>
              <thead><tr><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Dòng</th><th>Sản phẩm</th><th>Đơn vị</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL dự kiến</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL cơ sở</th></tr></thead>
              <tbody>{selected.lines.map(line=>{
                const opUnit=normalizeUnitCode(line.operationUnitCode);
                const baseUnit=normalizeUnitCode(line.baseUnitCode);
                return <tr key={line.id}>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.lineNo}</td>
                <td><strong style={{whiteSpace:'nowrap'}}>{line.productCode}</strong><br/><small>{line.productName}</small></td>
                <td>{opUnit||'—'}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.expectedQuantity.toLocaleString('vi-VN')}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.baseExpectedQuantity.toLocaleString('vi-VN')}{(line.baseUnitCode==='QA_UNIT'||line.baseUnitCode==='QA-PCS')&&baseUnit===opUnit?'':' '+baseUnit}</td>
              </tr>;
              })}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>}

        {canCreate&&<UiCard title="Tạo ASN nháp">
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: showCreateForm ? 12 : 0 }}>
            <button type="button" onClick={() => setShowCreateForm(prev => !prev)}>
              {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo ASN nháp'}
            </button>
          </div>
          {showCreateForm&&!canPrepareCreate&&<div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br/>Cần quyền đọc kho, sản phẩm và đối tác để tạo ASN an toàn.</div>}
          {showCreateForm&&canPrepareCreate&&<form className="inbound-planning-form" onSubmit={submitCreate}>
            <div className="inbound-planning-form-grid">
              <label><span>Mã ASN</span><input aria-label="Mã ASN" required value={code} onChange={e=>setCode(e.target.value)}/></label>
              <label><span>Đơn mua nguồn</span>
                <select aria-label="Đơn mua nguồn" value={purchaseOrderId} onChange={e=>void choosePurchaseOrder(e.target.value)} disabled={!canReadPo}>
                  <option value="">{canReadPo?'Không gắn đơn mua':'Không có quyền đọc đơn mua'}</option>
                  {eligiblePurchaseOrders.map(x=><option key={x.id} value={x.id}>{x.code} — {x.supplierCode} — {x.warehouseName}</option>)}
                </select>
              </label>
              {purchaseOrderId&&poDetail?<>
                <div><strong>Nhà cung cấp theo đơn mua</strong><br/>{poDetail.supplierCode} — {poDetail.supplierName}</div>
                <div><strong>Kho theo đơn mua</strong><br/>{poDetail.warehouseName}</div>
              </>:<>
                <label><span>Nhà cung cấp</span><select aria-label="Nhà cung cấp" required={!purchaseOrderId} value={supplierId} onChange={e=>setSupplierId(e.target.value?Number(e.target.value):'')}><option value="">Chọn nhà cung cấp</option>{suppliers.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
                <label><span>Kho nhận</span><select aria-label="Kho nhận" required={!purchaseOrderId} value={warehouseId} onChange={e=>setWarehouseId(e.target.value?Number(e.target.value):'')}><option value="">Chọn kho</option>{warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
              </>}
              <label><span>Dự kiến đến kho</span><input aria-label="Dự kiến đến kho" type="datetime-local" value={expectedArrival} onChange={e=>setExpectedArrival(e.target.value)}/></label>
              <label><span>Đơn vị vận chuyển</span><input aria-label="Đơn vị vận chuyển" value={carrierName} onChange={e=>setCarrierName(e.target.value)}/></label>
              <label><span>Biển số xe</span><input aria-label="Biển số xe" value={vehiclePlate} onChange={e=>setVehiclePlate(e.target.value.toUpperCase())}/></label>
              <label className="span-2"><span>Ghi chú</span><textarea aria-label="Ghi chú" rows={2} value={note} onChange={e=>setNote(e.target.value)}/></label>
            </div>

            <div>
              <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng dự kiến</strong><button type="button" onClick={()=>setLines(current=>[...current,emptyLine()])}>Thêm dòng</button></div>
              <div className="inbound-planning-lines">
                {lines.map((line,index)=><div className="inbound-planning-line inbound-planning-line-grid compact" key={index}>
                  {purchaseOrderId?<label><span>Dòng đơn mua</span><select aria-label="Dòng đơn mua" required value={line.purchaseOrderLineId} onChange={e=>changeLine(index,{purchaseOrderLineId:e.target.value?Number(e.target.value):''})}><option value="">Chọn dòng đơn mua</option>{poDetail?.lines.map(x=><option key={x.id} value={x.id}>{x.externalLineId} — {x.productCode} — còn dự kiến tối đa theo đơn mua</option>)}</select></label>:
                    <label><span>Sản phẩm</span><select aria-label="Sản phẩm" required value={line.productId} onChange={e=>changeLine(index,{productId:e.target.value?Number(e.target.value):''})}><option value="">Chọn sản phẩm</option>{products.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>}
                  <label><span>Đơn vị</span><select aria-label="Đơn vị" required value={line.operationUnitId} onChange={e=>changeLine(index,{operationUnitId:e.target.value?Number(e.target.value):''})}><option value="">Chọn đơn vị</option>{productUnitOptions(products,line.productId).map(x=><option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
                  <label><span>Số lượng dự kiến</span><input aria-label="Số lượng dự kiến" required min="0" step="any" type="number" value={line.expectedQuantity} onChange={e=>changeLine(index,{expectedQuantity:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <div><strong>{products.find(x=>x.id===line.productId)?.code||'Chưa chọn'}</strong><br/><small>{products.find(x=>x.id===line.productId)?.name||'Chọn nguồn dòng để xác định sản phẩm'}</small></div>
                  <button type="button" style={{ height: 38, alignSelf: 'end' }} disabled={lines.length===1} onClick={()=>setLines(current=>current.filter((_,i)=>i!==index))}>Xóa</button>
                </div>)}
              </div>
            </div>
            <p className="inbound-planning-helper">Tổng ASN theo từng dòng đơn mua không được vượt số lượng đặt cộng dung sai nhận vượt. ASN không tạo tồn kho.</p>
            <div className="inbound-planning-form-actions">
              <button type="submit" disabled={creating}>{creating?'Đang tạo...':'Tạo ASN'}</button>
              <button type="button" disabled={creating} onClick={resetForm}>Làm lại</button>
            </div>
          </form>}
        </UiCard>}
      </div>
    </div>
  </UiPage>;
}
