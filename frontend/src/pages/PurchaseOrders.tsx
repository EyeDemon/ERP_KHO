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

type PurchaseOrderList = {
  id:number; externalPoId:string; code:string; supplierCode:string; supplierName:string;
  warehouseId:number; warehouseName:string; status:string; orderDate:string; expectedDate?:string;
  baseOrderedQuantity:number; subTotalAmount?:number; taxAmount?:number; totalAmount?:number;
};
type PurchaseOrderLine = {
  id:number; externalLineId:string; lineNo:number; productId:number; productCode:string; productName:string;
  orderedQuantity:number; operationUnitCode:string; baseOrderedQuantity:number; baseUnitCode:string;
  unitPrice?:number; taxPercent?:number; lineSubTotal?:number; lineTaxAmount?:number; lineTotal?:number; lineTotalAmount?:number;
  allowedOverReceiptPct:number; allowedUnderReceiptPct:number;
};
type PurchaseOrderDetail = PurchaseOrderList & {
  sourceSystem:string; currency?:string; externalVersion?:string; rowVersion?:string; lines:PurchaseOrderLine[];
};
type Warehouse={id:number;name:string};
type Partner={id:number;code:string;name:string;isActive:boolean};
type ProductUom={unitId:number;unitCode:string;unitName:string;decimalPlaces:number;conversionFactor:number;version:number};
type Product={id:number;code:string;name:string;unitId:number;unitCode:string;unitName:string;uoms:ProductUom[]};
type LineForm={externalLineId:string;productId:number|'';operationUnitId:number|'';orderedQuantity:number|'';unitPrice:number|'';taxPercent:number|'';allowedOverReceiptPct:number|'';allowedUnderReceiptPct:number|''};

const labels:Record<string,string>={
  Draft:'Nháp',Open:'Đang mở',Approved:'Đã xác nhận',Confirmed:'Đã xác nhận',PartiallyReceived:'Nhận một phần',Received:'Đã nhận đủ',Closed:'Đã đóng',Cancelled:'Đã hủy',
};
const procurementSignalLabels:Record<string,string>={
  DISPATCHED_TO_SUPPLIER:'Đã gửi thông báo đôn đốc nhà cung cấp & giữ cửa ưu tiên',
  PROCUREMENT_INTELLIGENCE_READY:'Đề xuất mua hàng thông minh sẵn sàng',
  REORDER_THRESHOLD_PROPOSALS_READY:'Đã lập đề xuất điểm đặt hàng lại',
  P90_LEAD_TIME_CALIBRATED:'Đã hiệu chuẩn thời gian giao hàng P90',
  EXPEDITE_AND_DEFER_BALANCED:'Đã cân bằng tín hiệu đôn đốc & giãn tiến độ',
};
const translateProcurementSignal=(value?:string|null)=>value?(procurementSignalLabels[value]??value):'';
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Received'||status==='Closed'?'success':status==='PartiallyReceived'?'warning':status==='Cancelled'?'danger':'neutral';
const isoDay=(value?:string)=>value?new Date(value).toLocaleDateString('vi-VN'):'—';
const normalizeUnitCode=(unitCode?:string)=>(unitCode==='QA_UNIT'||unitCode==='QA-PCS')?'THÙNG':(unitCode||'');
const emptyLine=():LineForm=>({externalLineId:'',productId:'',operationUnitId:'',orderedQuantity:'',unitPrice:0,taxPercent:0,allowedOverReceiptPct:0,allowedUnderReceiptPct:0});

export default function PurchaseOrders(){
  const canRead=usePermission('purchase_order.read');
  const canCreate=usePermission('purchase_order.create');
  const canRelease=usePermission('purchase_order.release');
  const canClose=usePermission('purchase_order.close');
  const canCancel=usePermission('purchase_order.cancel');
  const canReadWarehouses=usePermission('warehouse.read');
  const canReadProducts=usePermission('product.read');
  const canReadPartners=usePermission('partner.read');

  const [items,setItems]=useState<PurchaseOrderList[]>([]);
  const [selected,setSelected]=useState<PurchaseOrderDetail|null>(null);
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
  const [governanceBusy,setGovernanceBusy]=useState(false);
  const [expediteResult,setExpediteResult]=useState<{expediteId:string;vendorNotificationStatus:string}|null>(null);
  const [procurementRecResult,setProcurementRecResult]=useState<{
    recommendationId:string;
    procurementSuggestionStatus:string;
    supplierLeadTimePercentile:string;
    expediteDeferSignal:string;
    status:string;
  }|null>(null);
  const requestSeq=useRef(0);
  const mutationLock=useRef(new Set<string>());

  const expediteOverdue=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/purchase-orders/expedite-overdue',{supplierId:supplierId||undefined},{headers:idempotencyHeaders('po-expedite-overdue:'+Date.now())});
      if(res?.data?.expediteId&&res?.data?.vendorNotificationStatus){
        setExpediteResult({
          expediteId:String(res.data.expediteId),
          vendorNotificationStatus:translateProcurementSignal(String(res.data.vendorNotificationStatus)),
        });
      }else{
        setExpediteResult(null);
        setError('Phản hồi đôn đốc nhà cung cấp không hợp lệ từ máy chủ.');
      }
    }catch(err:unknown){
      setExpediteResult(null);
      setError(permissionError(err,'Không thể phát tín hiệu đôn đốc nhà cung cấp.'));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const generateProcurementRecommendations=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/purchase-orders/generate-procurement-recommendations',{
        warehouseId:warehouseId||items[0]?.warehouseId||undefined,
      },{headers:idempotencyHeaders('po-procurement-rec:'+Date.now())});
      if(res?.data?.recommendationId&&res?.data?.status){
        setProcurementRecResult({
          recommendationId:String(res.data.recommendationId),
          procurementSuggestionStatus:translateProcurementSignal(String(res.data.procurementSuggestionStatus||'')),
          supplierLeadTimePercentile:translateProcurementSignal(String(res.data.supplierLeadTimePercentile||'')),
          expediteDeferSignal:translateProcurementSignal(String(res.data.expediteDeferSignal||'')),
          status:translateProcurementSignal(String(res.data.status)),
        });
      }else{
        setProcurementRecResult(null);
        setError('Phản hồi đề xuất mua hàng thông minh không hợp lệ từ máy chủ.');
      }
    }catch(err:unknown){
      setProcurementRecResult(null);
      setError(permissionError(err,'Không thể phân tích đề xuất đặt hàng thông minh.'));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const [sourceSystem,setSourceSystem]=useState('ERP');
  const [externalPoId,setExternalPoId]=useState('');
  const [code,setCode]=useState('');
  const [supplierId,setSupplierId]=useState<number|''>('');
  const [warehouseId,setWarehouseId]=useState<number|''>('');
  const [orderDate,setOrderDate]=useState(new Date().toISOString().slice(0,10));
  const [expectedDate,setExpectedDate]=useState('');
  const [currency,setCurrency]=useState('VND');
  const [lines,setLines]=useState<LineForm[]>([emptyLine()]);

  const canPrepareCreate=canCreate&&canReadWarehouses&&canReadProducts&&canReadPartners;

  const loadList=useCallback(async()=>{
    if(!canRead){setItems([]);setSelected(null);setLoading(false);return;}
    const request=++requestSeq.current;
    setLoading(true);setError('');
    try{
      const response=await apiClient.get('/api/purchase-orders',{params:{status:statusFilter||undefined}});
      if(request!==requestSeq.current||!hasPermission('purchase_order.read'))return;
      setItems(response.data);
    }catch(err:unknown){
      if(request!==requestSeq.current)return;
      setItems([]);setSelected(null);
      setError(permissionError(err,'Không thể tải danh sách đơn mua. Vui lòng thử lại.'));
    }finally{if(request===requestSeq.current)setLoading(false);}
  },[canRead,statusFilter]);

  const loadReferences=useCallback(async()=>{
    if(!canCreate)return;
    try{
      const [warehouseRes,productRes,partnerRes]=await Promise.all([
        canReadWarehouses?apiClient.get('/api/warehouses'):Promise.resolve({data:[]}),
        canReadProducts?apiClient.get('/api/products'):Promise.resolve({data:[]}),
        canReadPartners?apiClient.get('/api/business-partners',{params:{role:'supplier',active:true,pageSize:100}}):Promise.resolve({data:{items:[]}}),
      ]);
      setWarehouses(hasPermission('warehouse.read')?warehouseRes.data:[]);
      setProducts(hasPermission('product.read')?productRes.data:[]);
      setSuppliers(hasPermission('partner.read')?partnerRes.data.items:[]);
    }catch(err:unknown){
      setWarehouses([]);setProducts([]);setSuppliers([]);
      setError(permissionError(err,'Không thể tải dữ liệu tham khảo để tạo đơn mua.'));
    }
  },[canCreate,canReadWarehouses,canReadProducts,canReadPartners]);

  useEffect(()=>{void loadList();return()=>{requestSeq.current++;}},[loadList]);
  useEffect(()=>{void loadReferences();},[loadReferences]);

  const filtered=useMemo(()=>{
    const q=sanitizeScannerCode(search).toLowerCase();
    if(!q)return items;
    return items.filter(item=>[item.code,item.externalPoId,item.supplierCode,item.supplierName,item.warehouseName].join(' ').toLowerCase().includes(q));
  },[items,search]);

  const fulfillmentSummary=useMemo(()=>{
    const todayMs=new Date(new Date().toISOString().slice(0,10)).getTime();
    const overdueItems=items.filter(x=>(x.status==='Open'||x.status==='PartiallyReceived')&&x.expectedDate&&new Date(x.expectedDate).getTime()<todayMs);
    const activePriorityItems=items
      .filter(x=>x.status==='Open'||x.status==='PartiallyReceived')
      .sort((a,b)=>(a.expectedDate||'9999-12-31').localeCompare(b.expectedDate||'9999-12-31'))
      .slice(0,5);
    const receivedOrClosedCount=items.filter(x=>x.status==='Received'||x.status==='Closed').length;
    const totalBaseOrderedQuantity=roundQty4(items.reduce((sum,x)=>sum+(x.baseOrderedQuantity||0),0));
    const completionRate=items.length>0?Math.round((receivedOrClosedCount/items.length)*100):0;
    return {
      overdueExpectedCount:overdueItems.length,
      receivedOrClosedCount,
      totalBaseOrderedQuantity,
      completionRate,
      activePriorityItems,
    };
  },[items]);

  const viewDetail=async(id:number)=>{
    setError('');setSuccess('');
    try{setSelected((await apiClient.get('/api/purchase-orders/'+id)).data);}
    catch(err:unknown){setSelected(null);setError(permissionError(err,'Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.'));}
  };

  const resetForm=()=>{
    setSourceSystem('ERP');setExternalPoId('');setCode('');setSupplierId('');setWarehouseId('');
    setOrderDate(new Date().toISOString().slice(0,10));setExpectedDate('');setCurrency('VND');setLines([emptyLine()]);
  };

  const changeLine=(index:number,patch:Partial<LineForm>)=>{
    setLines(current=>current.map((line,i)=>{
      if(i!==index)return line;
      const next={...line,...patch};
      if('productId' in patch){
        const product=products.find(x=>x.id===patch.productId);
        next.operationUnitId=product?.unitId??'';
      }
      return next;
    }));
  };

  const submitCreate=async(event:FormEvent)=>{
    event.preventDefault();
    if(!canPrepareCreate||creating)return;
    setError('');setSuccess('');
    if(!supplierId||!warehouseId){setError('Chọn nhà cung cấp và kho nhận.');return;}
    if(lines.length===0||lines.some(line=>!sanitizeScannerCode(line.externalLineId)||!line.productId||!line.operationUnitId||roundQty4(Number(line.orderedQuantity))<=0)){
      setError('Mỗi dòng cần mã dòng nguồn, sản phẩm, đơn vị và số lượng lớn hơn 0.');return;
    }
    setCreating(true);
    const cleanCode=sanitizeScannerCode(code);
    const cleanExternalPoId=sanitizeScannerCode(externalPoId);
    const action='purchase-order-create:'+cleanCode+':'+cleanExternalPoId;
    try{
      const payload={
        sourceSystem:sanitizeScannerCode(sourceSystem),externalPoId:cleanExternalPoId,code:cleanCode,supplierId,warehouseId,
        orderDate:orderDate?orderDate+'T00:00:00':undefined,
        expectedDate:expectedDate?expectedDate+'T00:00:00':null,
        currency:currency||null,
        lines:lines.map(line=>({
          externalLineId:sanitizeScannerCode(line.externalLineId),productId:Number(line.productId),operationUnitId:Number(line.operationUnitId),
          orderedQuantity:roundQty4(Number(line.orderedQuantity)),
          unitPrice:Number(line.unitPrice||0),taxPercent:Number(line.taxPercent||0),
          allowedOverReceiptPct:Number(line.allowedOverReceiptPct||0),
          allowedUnderReceiptPct:Number(line.allowedUnderReceiptPct||0),
        })),
      };
      const response=await apiClient.post('/api/purchase-orders',payload,{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action);
      setSuccess('Đã tạo đơn mua nháp. Đơn mua chưa làm thay đổi tồn kho.');
      resetForm();await loadList();await viewDetail(response.data.id);
    }catch(err:unknown){setError(permissionError(err,'Không thể tạo đơn mua. Kiểm tra dữ liệu và thử lại.'));}
    finally{setCreating(false);}
  };

  const command=(path:string,action:string,successMessage:string)=>
    runInboundStateCommand({
      selected,path,action,successMessage,errorMessage:'Không thể chuyển trạng thái đơn mua. Vui lòng tải lại và thử lại.',
      mutationLock,setBusy,setError,setSuccess,setSelected,reload:loadList,
    });

  if(loading&&items.length===0)return <p role="status">Đang tải đơn mua...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Nhập kho"
      title="Đơn mua (PO)"
      description="Theo dõi đơn đặt hàng dự kiến từ hệ thống mua hàng trước khi tạo thông báo giao hàng (ASN) và tiếp nhận. Đơn mua không làm tăng tồn kho."
      actions={canCreate ? (
        <button type="button" className="ui-primary-button" onClick={() => setShowCreateForm(prev => !prev)}>
          {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo đơn mua nháp'}
        </button>
      ) : undefined}
    />

    <InboundPlanningFeedback error={error} success={success}/>

    <UiMetricGrid>
      <UiMetric value={items.length} label="Đơn mua trong phạm vi hiện tại"/>
      <UiMetric value={items.filter(x=>x.status==='Open').length} label="Đang mở"/>
      <UiMetric value={items.filter(x=>x.status==='PartiallyReceived').length} label="Nhận một phần"/>
      <UiMetric value={items.filter(x=>x.status==='Received').length} label="Đã nhận đủ"/>
    </UiMetricGrid>

    <UiCard title="Giám sát Tiến độ Tiếp nhận & Cam kết Đơn mua">
      <div className="inbound-planning-detail-meta">
        <div>
          <strong>Đơn mua quá hạn ngày dự kiến</strong><br/>
          <UiBadge tone={fulfillmentSummary.overdueExpectedCount>0?'danger':'success'}>
            {fulfillmentSummary.overdueExpectedCount} đơn mua quá hạn
          </UiBadge>
        </div>
        <div>
          <strong>Tổng SL cơ sở đặt hàng</strong><br/>
          {fulfillmentSummary.totalBaseOrderedQuantity.toLocaleString('vi-VN')}
        </div>
        <div>
          <strong>Tỷ lệ hoàn tất (Nhận đủ / Đóng)</strong><br/>
          {fulfillmentSummary.completionRate}% ({fulfillmentSummary.receivedOrClosedCount}/{items.length})
        </div>
        <div>
          <strong>Đơn mua sắp/quá hạn cần ưu tiên tiếp nhận</strong><br/>
          {fulfillmentSummary.activePriorityItems.length===0
            ?'Không có đơn mua mở cần ưu tiên'
            :fulfillmentSummary.activePriorityItems.map(po=>`${po.code} (${po.supplierCode} • Hạn: ${isoDay(po.expectedDate)})`).join(' | ')}
        </div>
      </div>
      <div className="ui-inline-actions" style={{marginTop:8}}>
        <button type="button" disabled={!!busy||creating||governanceBusy} onClick={()=>void expediteOverdue()}>
          Gửi Yêu cầu Đôn đốc NCC &amp; Giữ Cửa Tiếp nhận Ưu tiên
        </button>
        <button type="button" disabled={!!busy||creating||governanceBusy} onClick={()=>void generateProcurementRecommendations()}>
          Sinh Đề xuất Mua hàng, Phân tích Thời gian giao NCC &amp; Khuyến nghị Đôn đốc/Hoãn
        </button>
      </div>
      {expediteResult&&(
        <div role="status" style={{marginTop:8,padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13,lineHeight:1.5,whiteSpace:'normal',wordBreak:'break-word'}}>
          {`${expediteResult.expediteId} • ${expediteResult.vendorNotificationStatus}`}
        </div>
      )}
      {procurementRecResult&&(
        <div role="status" style={{marginTop:8,padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13,lineHeight:1.5,whiteSpace:'normal',wordBreak:'break-word'}}>
          {`${procurementRecResult.recommendationId} • ${procurementRecResult.status} (${procurementRecResult.procurementSuggestionStatus} / ${procurementRecResult.supplierLeadTimePercentile} / ${procurementRecResult.expediteDeferSignal})`}
        </div>
      )}
    </UiCard>

    <InboundPlanningToolbar labels={labels} statusFilter={statusFilter} setStatusFilter={setStatusFilter} search={search} setSearch={setSearch} placeholder="Mã đơn mua, mã nguồn, nhà cung cấp, kho"/>

    <div className="inbound-planning-grid">
      <UiCard title="Danh sách đơn mua">
        {filtered.length===0?<UiEmptyState title="Chưa có đơn mua phù hợp" detail="Thay đổi bộ lọc hoặc tạo đơn mua mới nếu bạn có quyền."/>:
          <UiTableScroll>
            <table className="inbound-planning-table ui-table-compact" aria-label="Danh sách đơn mua">
              <thead><tr><th>Mã đơn mua</th><th>Nhà cung cấp</th><th>Kho nhận</th><th>Trạng thái</th><th>Ngày dự kiến</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL đặt</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Tổng tiền</th></tr></thead>
              <tbody>{filtered.map(item=><tr key={item.id}>
                <td className="ui-cell-nowrap" style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void viewDetail(item.id)}><strong style={{whiteSpace:'nowrap'}}>{item.code}</strong></button><div className="ui-muted-text" style={{whiteSpace:'nowrap'}}>{item.externalPoId}</div></td>
                <td><span style={{whiteSpace:'nowrap'}}>{item.supplierCode}</span><br/><small>{item.supplierName}</small></td>
                <td>{item.warehouseName}</td>
                <td><UiBadge tone={tone(item.status)}>{labels[item.status]??item.status}</UiBadge></td>
                <td>{isoDay(item.expectedDate)}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{item.baseOrderedQuantity.toLocaleString('vi-VN')}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{Number(item.totalAmount||0).toLocaleString('vi-VN')}</td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>}
      </UiCard>

      <div className="ui-stack">
        {selected&&<UiCard title={'Chi tiết '+selected.code}>
          <div className="inbound-planning-detail-meta">
            <div><strong>Trạng thái</strong><br/><UiBadge tone={tone(selected.status)}>{labels[selected.status]??selected.status}</UiBadge></div>
            <div><strong>Nhà cung cấp</strong><br/>{selected.supplierCode} — {selected.supplierName}</div>
            <div><strong>Nguồn ngoài hệ thống</strong><br/>{selected.sourceSystem} / {selected.externalPoId}</div>
            <div><strong>Kho nhận</strong><br/>{selected.warehouseName}</div>
            <div><strong>Ngày đặt</strong><br/>{isoDay(selected.orderDate)}</div>
            <div><strong>Ngày dự kiến</strong><br/>{isoDay(selected.expectedDate)}</div>
            <div><strong>Tiền tệ tham chiếu</strong><br/>{selected.currency||'—'}</div>
            <div><strong>Tổng SL cơ sở</strong><br/>{selected.baseOrderedQuantity.toLocaleString('vi-VN')}</div>
            <div><strong>Tổng tiền đơn mua (Đã gồm thuế)</strong><br/>{Number(selected.totalAmount||0).toLocaleString('vi-VN')} {selected.currency||'VND'}</div>
          </div>
          <div className="inbound-planning-actions">
            {canRelease&&selected.status==='Draft'&&<button className="ui-primary-button" disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/open','po-open:'+selected.id,'Đơn mua đã được mở cho quy trình nhận hàng.')}>{busy?'Đang lưu...':'Mở đơn mua'}</button>}
            {canClose&&['PartiallyReceived','Received'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/close','po-close:'+selected.id,'Đơn mua đã được đóng.')}>Đóng đơn mua</button>}
            {canCancel&&['Draft','Open'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/cancel','po-cancel:'+selected.id,'Đơn mua đã được hủy.')}>Hủy đơn mua</button>}
          </div>
          <UiTableScroll>
            <table className="ui-table-compact" aria-label={'Dòng đơn mua '+selected.code}>
              <thead><tr><th>Dòng</th><th>Sản phẩm</th><th>Đơn vị</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL đặt</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>SL cơ sở</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đơn giá / Thuế</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Thành tiền</th><th className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Dung sai</th></tr></thead>
              <tbody>{selected.lines.map(line=>{
                const opUnit=normalizeUnitCode(line.operationUnitCode);
                const baseUnit=normalizeUnitCode(line.baseUnitCode);
                return <tr key={line.id}>
                <td>{line.externalLineId}</td>
                <td><strong style={{whiteSpace:'nowrap'}}>{line.productCode}</strong><br/><small>{line.productName}</small></td>
                <td>{opUnit||'—'}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.orderedQuantity.toLocaleString('vi-VN')}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.baseOrderedQuantity.toLocaleString('vi-VN')}{(line.baseUnitCode==='QA_UNIT'||line.baseUnitCode==='QA-PCS')&&baseUnit===opUnit?'':' '+baseUnit}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{Number(line.unitPrice||0).toLocaleString('vi-VN')} ({Number(line.taxPercent||0)}%)</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{Number(line.lineTotal ?? line.lineTotalAmount ?? 0).toLocaleString('vi-VN')}</td>
                <td className="inbound-planning-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>+{line.allowedOverReceiptPct}% / -{line.allowedUnderReceiptPct}%</td>
              </tr>;
              })}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>}

        {canCreate&&<UiCard title="Tạo đơn mua nháp">
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: showCreateForm ? 12 : 0 }}>
            <button type="button" onClick={() => setShowCreateForm(prev => !prev)}>
              {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo đơn mua nháp'}
            </button>
          </div>
          {showCreateForm&&!canPrepareCreate&&<div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br/>Cần quyền đọc kho, sản phẩm và đối tác để tạo đơn mua an toàn.</div>}
          {showCreateForm&&canPrepareCreate&&<form className="inbound-planning-form" onSubmit={submitCreate}>
            <div className="inbound-planning-form-grid">
              <label>Hệ thống nguồn<input required value={sourceSystem} onChange={e=>setSourceSystem(e.target.value)} /></label>
              <label>Mã đơn mua ngoài hệ thống<input required value={externalPoId} onChange={e=>setExternalPoId(e.target.value)} /></label>
              <label>Mã đơn mua kho<input required value={code} onChange={e=>setCode(e.target.value)} /></label>
              <label>Tiền tệ tham chiếu<input value={currency} onChange={e=>setCurrency(e.target.value.toUpperCase())} maxLength={10}/></label>
              <label>Nhà cung cấp<select required value={supplierId} onChange={e=>setSupplierId(e.target.value?Number(e.target.value):'')}><option value="">Chọn nhà cung cấp</option>{suppliers.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
              <label>Kho nhận<select required value={warehouseId} onChange={e=>setWarehouseId(e.target.value?Number(e.target.value):'')}><option value="">Chọn kho</option>{warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
              <label>Ngày đặt<input required type="date" value={orderDate} onChange={e=>setOrderDate(e.target.value)}/></label>
              <label>Ngày dự kiến<input type="date" value={expectedDate} onChange={e=>setExpectedDate(e.target.value)}/></label>
            </div>

            <div>
              <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng</strong><button type="button" onClick={()=>setLines(current=>[...current,emptyLine()])}>Thêm dòng</button></div>
              <div className="inbound-planning-lines">
                {lines.map((line,index)=><div className="inbound-planning-line inbound-planning-line-grid" key={index}>
                  <label>Mã dòng nguồn<input required value={line.externalLineId} onChange={e=>changeLine(index,{externalLineId:e.target.value})}/></label>
                  <label>Sản phẩm<select required value={line.productId} onChange={e=>changeLine(index,{productId:e.target.value?Number(e.target.value):''})}><option value="">Chọn sản phẩm</option>{products.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
                  <label>Đơn vị<select required value={line.operationUnitId} onChange={e=>changeLine(index,{operationUnitId:e.target.value?Number(e.target.value):''})}><option value="">Chọn đơn vị</option>{productUnitOptions(products,line.productId).map(x=><option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
                  <label>Số lượng<input required min="0" step="any" type="number" value={line.orderedQuantity} onChange={e=>changeLine(index,{orderedQuantity:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Đơn giá<input min="0" step="any" type="number" value={line.unitPrice} onChange={e=>changeLine(index,{unitPrice:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Thuế %<input min="0" max="100" step="any" type="number" value={line.taxPercent} onChange={e=>changeLine(index,{taxPercent:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Nhận vượt %<input min="0" max="100" step="any" type="number" value={line.allowedOverReceiptPct} onChange={e=>changeLine(index,{allowedOverReceiptPct:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Nhận thiếu %<input min="0" max="100" step="any" type="number" value={line.allowedUnderReceiptPct} onChange={e=>changeLine(index,{allowedUnderReceiptPct:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <button type="button" style={{ height: 38, alignSelf: 'end' }} disabled={lines.length===1} onClick={()=>setLines(current=>current.filter((_,i)=>i!==index))}>Xóa</button>
                </div>)}
              </div>
            </div>
            <p className="inbound-planning-helper">Tạo đơn mua chỉ ghi nhận lượng hàng dự kiến về; tồn kho chỉ thay đổi khi Phiếu nhập kho được ghi sổ.</p>
            <div className="inbound-planning-form-actions">
              <button type="submit" disabled={creating}>{creating?'Đang tạo...':'Tạo đơn mua'}</button>
              <button type="button" disabled={creating} onClick={resetForm}>Làm lại</button>
            </div>
          </form>}
        </UiCard>}
      </div>
    </div>
  </UiPage>;
}
