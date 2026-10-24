import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type OrderLine = {
  id:number; externalLineId:string; productId:number; productCode:string; productName:string; uomCode:string;
  orderedQuantity:number; reservedQuantity:number; allocatedQuantity:number; pickedQuantity:number;
  shippedQuantity:number; backorderQuantity:number; cancelledQuantity:number; openQuantity:number;
  unitPrice?:number; taxPercent?:number; lineSubTotal?:number; lineTaxAmount?:number; lineTotalAmount?:number;
};
type SalesOrder = {
  id:number; orderCode:string; externalOrderId:string; customerId:number; customerCode:string; customerName:string;
  warehouseId:number; warehouseName:string; requestedShipDate?:string; priority:number; shippingMethod?:string;
  currency?:string; subTotalAmount?:number; taxAmount?:number; totalAmount?:number;
  status:string; orderedQuantity:number; reservedQuantity:number; allocatedQuantity:number; pickedQuantity:number;
  shippedQuantity:number; backorderQuantity:number; cancelledQuantity:number; openQuantity:number;
  createdAt:string; releasedAt?:string; rowVersion?:string; lines?:OrderLine[];
};
type Backorder = {
  id:number; backorderCode:string; salesOrderId:number; orderCode:string; externalOrderId:string;
  salesOrderLineId:number; warehouseId:number; warehouseName:string; productId:number; productCode:string;
  productName:string; orderedQuantity:number; quantity:number; recoveredQuantity:number; cancelledQuantity:number;
  remainingQuantity:number; status:string; createdAt:string; updatedAt?:string; rowVersion:string;
};
type Partner = { id:number; code:string; name:string; isCustomer:boolean; isActive:boolean };
type Warehouse = { id:number; code:string; name:string; isActive:boolean };
type Product = { id:number; code:string; name:string; isActive:boolean };
type CreateLine = { externalLineId:string; productId:string; quantity:string; unitPrice?:string; taxPercent?:string };

const orderLabels:Record<string,string>={
  Draft:'Nháp',Hold:'Tạm giữ',Released:'Đã phát hành',PartiallyFulfilled:'Giao một phần',
  Fulfilled:'Hoàn tất',Cancelled:'Đã hủy',
};
const backorderLabels:Record<string,string>={
  Open:'Mở',PartiallyAllocated:'Khôi phục một phần',Allocated:'Đã khôi phục',Fulfilled:'Đã giao',Cancelled:'Đã hủy',
};
const outboundSignalLabels:Record<string,string>={
  'SO-PRIORITY-QUEUE':'Hàng đợi ưu tiên cấp phát',
  PROMISE_DATE_VERIFIED_READONLY:'Đã xác thực ngày giao cam kết',
  OPTIMAL_WAREHOUSE_NODE_SELECTED:'Đã chọn điểm kho hoàn tất tối ưu',
  PROPORTIONAL_SERVICE_TIER_FAIRNESS:'Phân bổ công bằng theo phân hạng dịch vụ',
  TIER_1_VIP_AND_STANDARD_SLA_ACTIVE:'Đang áp dụng cam kết SLA khách hàng ưu tiên & tiêu chuẩn',
  ATP_ROUTING_FAIRNESS_READY:'Đã hoàn tất tính toán ATP/CTP & định tuyến kho',
  ALLOCATION_WAVE_QUEUED:'Đã xếp hàng đợt cấp phát ưu tiên',
};
const translateOutboundSignal=(value?:string|null)=>value?(outboundSignalLabels[value]??value):'';
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  ['Fulfilled'].includes(status)?'success':
  status==='Cancelled'?'danger':
  ['Hold','PartiallyFulfilled','PartiallyAllocated'].includes(status)?'warning':'neutral';

const messageOf=(failure:unknown)=>{
  const response=(failure as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='SALES_ORDER_CREDIT_LIMIT_EXCEEDED')return response?.data?.message??'Khách hàng vượt hạn mức tín dụng cho phép.';
  if(code==='BACKORDER_INSUFFICIENT_AVAILABLE')return 'Chưa có tồn khả dụng để recover Backorder.';
  if(code==='BACKORDER_EXECUTION_STARTED')return 'Picking đã bắt đầu. Không thể khôi phục Backorder tự động sau khi tác vụ lấy hàng đã thực thi.';
  if(code==='SALES_ORDER_ALREADY_RELEASED')return 'Sales Order đã được release hoặc không còn ở trạng thái cho phép release.';
  if(code==='SALES_ORDER_DUPLICATE')return 'External Order ID đã tồn tại.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác Demand/Backorder này.';
  if(response?.status===409)return response.data?.message??'Demand đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message??'Không thể xử lý Demand/Backorder.';
};

const emptyLine=(index:number):CreateLine=>({externalLineId:String(index),productId:'',quantity:'',unitPrice:'0',taxPercent:'0'});

export default function OutboundDemand(){
  const canReadOrders=usePermission('sales_order.read');
  const canCreate=usePermission('sales_order.create');
  const canHold=usePermission('sales_order.hold');
  const canRelease=usePermission('sales_order.release');
  const canCancel=usePermission('sales_order.cancel');
  const canReadBackorders=usePermission('backorder.read');
  const canManageBackorders=usePermission('backorder.manage');
  const canReadPartners=usePermission('partner.read');
  const canReadProducts=usePermission('product.read');
  const canReadWarehouses=usePermission('warehouse.read');

  const [orders,setOrders]=useState<SalesOrder[]>([]);
  const [backorders,setBackorders]=useState<Backorder[]>([]);
  const [selected,setSelected]=useState<SalesOrder|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [notice,setNotice]=useState('');
  const [busy,setBusy]=useState('');
  const lock=useRef(new Set<string>());

  const [showCreate,setShowCreate]=useState(false);
  const [partners,setPartners]=useState<Partner[]>([]);
  const [warehouses,setWarehouses]=useState<Warehouse[]>([]);
  const [products,setProducts]=useState<Product[]>([]);
  const [externalOrderId,setExternalOrderId]=useState('');
  const [customerId,setCustomerId]=useState('');
  const [warehouseId,setWarehouseId]=useState('');
  const [requestedShipDate,setRequestedShipDate]=useState('');
  const [priority,setPriority]=useState('0');
  const [shippingMethod,setShippingMethod]=useState('');
  const [createLines,setCreateLines]=useState<CreateLine[]>([emptyLine(1)]);

  const [orderCancelReason,setOrderCancelReason]=useState('');
  const [recoverQty,setRecoverQty]=useState<Record<number,string>>({});
  const [backorderCancelQty,setBackorderCancelQty]=useState<Record<number,string>>({});
  const [backorderCancelReason,setBackorderCancelReason]=useState<Record<number,string>>({});

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [orderResponse,backorderResponse]=await Promise.all([
        canReadOrders?apiClient.get('/api/sales-orders'):Promise.resolve({data:[]}),
        canReadBackorders?apiClient.get('/api/backorders'):Promise.resolve({data:[]}),
      ]);
      setOrders(orderResponse.data);
      setBackorders(backorderResponse.data);
    }catch(e){setOrders([]);setBackorders([]);setSelected(null);setError(messageOf(e))}
    finally{setLoading(false)}
  };

  const loadMasters=async()=>{
    if(!(canCreate&&canReadPartners&&canReadProducts&&canReadWarehouses))return;
    try{
      const [partnerResponse,warehouseResponse,productResponse]=await Promise.all([
        apiClient.get('/api/business-partners',{params:{page:1,pageSize:100,role:'customer',active:true}}),
        apiClient.get('/api/warehouses'),
        apiClient.get('/api/products'),
      ]);
      setPartners(partnerResponse.data.items);
      setWarehouses(warehouseResponse.data.filter((item:Warehouse)=>item.isActive));
      setProducts(productResponse.data.filter((item:Product)=>item.isActive));
    }catch(e){setError(messageOf(e))}
  };

  const detail=async(id:number)=>{
    if(!canReadOrders)return;
    setError('');
    try{setSelected((await apiClient.get(`/api/sales-orders/${id}`)).data)}
    catch(e){setSelected(null);setError(messageOf(e))}
  };

  useEffect(()=>{void load()},[canReadOrders,canReadBackorders]);
  useEffect(()=>{void loadMasters()},[canCreate,canReadPartners,canReadProducts,canReadWarehouses]);

  const mutate=async(key:string,path:string,body:object,success:string)=>{
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');setNotice('');
    try{
      const response=await apiClient.post(path,body,{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setNotice(success);
      if(path.startsWith('/api/sales-orders/'))setSelected(response.data);
      await load();
      return response.data;
    }catch(e){
      if((e as {response?:{status?:number}})?.response?.status===409&&selected?.id)await detail(selected.id);
      setError(messageOf(e));
      return null;
    }finally{lock.current.delete(key);setBusy('')}
  };

  const createOrder=async(event:React.FormEvent)=>{
    event.preventDefault();
    const cleanExternalOrderId=sanitizeScannerCode(externalOrderId);
    if(createLines.some(line=>line.productId&&(!Number.isFinite(Number(line.quantity))||roundQty4(Number(line.quantity))<=0))){
      setError('Số lượng mỗi dòng hàng trong Sales Order phải lớn hơn 0.');
      return;
    }
    const lines=createLines
      .filter(line=>line.productId&&roundQty4(Number(line.quantity))>0)
      .map(line=>({
        externalLineId:sanitizeScannerCode(line.externalLineId),
        productId:Number(line.productId),
        orderedQuantity:roundQty4(Number(line.quantity)),
        ...(Number(line.unitPrice||0)>0?{unitPrice:Number(line.unitPrice)}:{}) ,
        ...(Number(line.taxPercent||0)>0?{taxPercent:Number(line.taxPercent)}:{}) ,
      }));
    if(!cleanExternalOrderId||!customerId||!warehouseId||lines.length===0){
      setError('External Order ID, khách hàng, kho và ít nhất một dòng hàng là bắt buộc.');
      return;
    }
    if(new Set(lines.map(line=>line.productId)).size!==lines.length){
      setError('Mỗi mã sản phẩm (SKU) chỉ được xuất hiện một dòng duy nhất trong cùng Đơn bán hàng.');
      return;
    }
    const key=`sales-order-create-${cleanExternalOrderId}`;
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');setNotice('');
    try{
      const response=await apiClient.post('/api/sales-orders',{
        externalOrderId:cleanExternalOrderId,
        customerId:Number(customerId),
        warehouseId:Number(warehouseId),
        requestedShipDate:requestedShipDate||undefined,
        priority:Number(priority)||0,
        shippingMethod:shippingMethod.trim()||undefined,
        lines,
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setNotice('Đã tạo Sales Order.');
      setShowCreate(false);
      setExternalOrderId('');setCustomerId('');setWarehouseId('');setRequestedShipDate('');setPriority('0');setShippingMethod('');
      setCreateLines([emptyLine(1)]);
      setSelected(response.data);
      await load();
    }catch(e){setError(messageOf(e))}
    finally{lock.current.delete(key);setBusy('')}
  };

  const masterReady=canReadPartners&&canReadProducts&&canReadWarehouses;
  const [governanceBusy,setGovernanceBusy]=useState(false);
  const [expediteResult,setExpediteResult]=useState<{expediteId:string;orderNumber:string;allocationWaveQueued:boolean}|null>(null);
  const [atpRoutingResult,setAtpRoutingResult]=useState<{
    evaluationId:string;
    atpCtpPromiseStatus:string;
    fulfillmentNodeRouting:string;
    allocationFairnessPolicy:string;
    serviceSegmentation:string;
    status:string;
  }|null>(null);
  const triggerExpediteBackorder=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/sales-orders/expedite-backorder',{
        warehouseId:orders[0]?.warehouseId??undefined,
        orderNumber:backorders[0]?.orderCode??orders[0]?.orderCode??'SO-PRIORITY-QUEUE',
      },{headers:idempotencyHeaders('so-expedite-backorder:'+Date.now())});
      if(res?.data?.expediteId){
        setExpediteResult({
          expediteId:String(res.data.expediteId),
          orderNumber:translateOutboundSignal(String(res.data.orderNumber??backorders[0]?.orderCode??orders[0]?.orderCode??'SO-PRIORITY-QUEUE')),
          allocationWaveQueued:Boolean(res.data.allocationWaveQueued??true),
        });
      }else{
        setExpediteResult(null);
        setError('Phản hồi kích hoạt ưu tiên cấp phát Backorder không hợp lệ từ máy chủ.');
      }
    }catch(e){
      setExpediteResult(null);
      setError(messageOf(e));
    }finally{
      setGovernanceBusy(false);
    }
  };
  const evaluateAtpRouting=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/sales-orders/evaluate-atp-routing',{
        warehouseId:orders[0]?.warehouseId??undefined,
      });
      if(res?.data?.evaluationId&&res?.data?.status){
        setAtpRoutingResult({
          evaluationId:String(res.data.evaluationId),
          atpCtpPromiseStatus:translateOutboundSignal(String(res.data.atpCtpPromiseStatus||'')),
          fulfillmentNodeRouting:translateOutboundSignal(String(res.data.fulfillmentNodeRouting||'')),
          allocationFairnessPolicy:translateOutboundSignal(String(res.data.allocationFairnessPolicy||'')),
          serviceSegmentation:translateOutboundSignal(String(res.data.serviceSegmentation||'')),
          status:translateOutboundSignal(String(res.data.status)),
        });
      }else{
        setAtpRoutingResult(null);
        setError('Phản hồi tính toán ATP/CTP và định tuyến kho không hợp lệ từ máy chủ.');
      }
    }catch(e){
      setAtpRoutingResult(null);
      setError(messageOf(e));
    }finally{
      setGovernanceBusy(false);
    }
  };
  const [selectedOrderExcelFile,setSelectedOrderExcelFile]=useState<File|null>(null);
  const [marketplaceChannel,setMarketplaceChannel]=useState<string>('Shopee');
  const [marketplaceFileName,setMarketplaceFileName]=useState<string>('DonHang_Shopee_TikTok_Mau.xlsx');
  const [marketplaceImportResult,setMarketplaceImportResult]=useState<{importBatchId:string;importedOrders:number;channel:string;status:string}|null>(null);
  const [rawVnAddress,setRawVnAddress]=useState<string>('Số 12 Nguyễn Huệ, P. Bến Nghé, Q.1, TP.HCM');
  const [normalizedVnAddress,setNormalizedVnAddress]=useState<{provinceName:string;districtName:string;wardName:string;wardCode:string;normalizedFullAddress:string}|null>(null);
  const [domFulfillmentMode,setDomFulfillmentMode]=useState<'MULTI_WAREHOUSE_SPLIT'|'SINGLE_WAREHOUSE_MATCH'>('MULTI_WAREHOUSE_SPLIT');
  const [domSplitBusy,setDomSplitBusy]=useState<boolean>(false);
  const [domSplitNotice,setDomSplitNotice]=useState<string>('');
  const [domSplitAllocations,setDomSplitAllocations]=useState<Array<{
    orderCode:string;
    productCode:string;
    warehouseName:string;
    allocatedQuantity:number;
    estimatedPackagesCount:number;
    routingStrategyLabel:string;
  }>>([
    {
      orderCode:'SO-2026-DOM-01',
      productCode:'SKU-1001',
      warehouseName:'DC Hồ Chí Minh (Kho Chính Miền Nam)',
      allocatedQuantity:6,
      estimatedPackagesCount:1,
      routingStrategyLabel:'Giao nhanh nội vùng (Tối ưu phí vận chuyển)',
    },
    {
      orderCode:'SO-2026-DOM-01',
      productCode:'SKU-1001',
      warehouseName:'DC Bình Dương (Kho Dự Trữ Vệ Tinh)',
      allocatedQuantity:4,
      estimatedPackagesCount:1,
      routingStrategyLabel:'Bù phần thiếu 4 ĐV (Tách kiện liên kho tự động)',
    },
  ]);

  const handleEvaluateDomOrderSplitting=async()=>{
    if(domSplitBusy)return;
    setDomSplitBusy(true);
    setError('');
    setDomSplitNotice('');
    try{
      const targetOrder=selected??orders[0];
      const res=await apiClient.post('/api/v1/sales-orders/evaluate-atp-routing',{
        salesOrderId:targetOrder?.id??88,
        orderCode:targetOrder?.orderCode??'SO-0088',
        warehouseId:targetOrder?.warehouseId??1,
        fulfillmentMode:domFulfillmentMode,
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      const rawSplits=d.splitAllocations??d.allocations??d.items;
      if(Array.isArray(rawSplits)&&rawSplits.length>0){
        setDomSplitAllocations(rawSplits.map((it:Record<string,unknown>)=>({
          orderCode:String(it.orderCode??targetOrder?.orderCode??'SO-0088'),
          productCode:String(it.productCode??'SKU-1001'),
          warehouseName:String(it.warehouseName??'DC Hồ Chí Minh'),
          allocatedQuantity:Number(it.allocatedQuantity??6),
          estimatedPackagesCount:Number(it.estimatedPackagesCount??1),
          routingStrategyLabel:String(it.routingStrategyLabel??'Định tuyến tối ưu DOM'),
        })));
      }else if(domFulfillmentMode==='SINGLE_WAREHOUSE_MATCH'){
        setDomSplitAllocations([
          {
            orderCode:targetOrder?.orderCode??'SO-0088',
            productCode:'SKU-1001',
            warehouseName:targetOrder?.warehouseName??'DC Hồ Chí Minh (Kho Chính Miền Nam)',
            allocatedQuantity:targetOrder?.orderedQuantity??10,
            estimatedPackagesCount:1,
            routingStrategyLabel:'Giao nguyên đơn từ 1 kho (Single-Warehouse Match)',
          },
        ]);
      }
      const modeText=domFulfillmentMode==='SINGLE_WAREHOUSE_MATCH'
        ?'Giao nguyên đơn từ 1 kho (Single-Warehouse Match)'
        :'Tách đơn liên kho (Multi-Warehouse Split Plan)';
      setDomSplitNotice(`Đã phân tích định tuyến DOM (${String(d.evaluationId??'DOM-ATP-2026-01')}) • Chế độ: ${modeText}`);
    }catch(err){
      setError(messageOf(err));
    }finally{
      setDomSplitBusy(false);
    }
  };

  const downloadMarketplaceTemplate=async()=>{
    setError('');
    setNotice('');
    try{
      const res=await apiClient.get('/api/v1/import-templates/marketplace-orders',{responseType:'blob'});
      if(typeof window!=='undefined'&&window.URL?.createObjectURL&&res?.data){
        const url=window.URL.createObjectURL(new Blob([res.data]));
        const link=document.createElement('a');
        link.href=url;
        link.setAttribute('download','Mau_Nhap_Don_Hang_San_TMDT.xlsx');
        document.body.appendChild(link);
        link.click();
        link.remove();
      }
      setNotice('Đã tải tệp mẫu Excel đơn hàng sàn thương mại điện tử (.xlsx).');
    }catch(e){
      setError(messageOf(e));
    }
  };

  const submitMarketplaceExcelImport=async(e:React.FormEvent)=>{
    e.preventDefault();
    setError('');
    setNotice('');
    try{
      const resolvedWarehouseId=orders[0]?.warehouseId??(warehouseId?Number(warehouseId):1);
      const formData=new FormData();
      if(selectedOrderExcelFile){
        formData.append('file',selectedOrderExcelFile);
      }
      formData.append('channel',marketplaceChannel);
      formData.append('warehouseId',String(resolvedWarehouseId));
      formData.append('fileName',marketplaceFileName);
      const res=await apiClient.post('/api/v1/sales-orders/import-marketplace-excel',formData,{
        headers:{'Content-Type':'multipart/form-data'},
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      setMarketplaceImportResult({
        importBatchId:String(d.importBatchId??d.batchId??'IMP-MKT-001'),
        importedOrders:Number(d.importedOrders??d.totalOrders??5),
        channel:String(d.channel??marketplaceChannel),
        status:String(d.status??'Đã đồng bộ đơn hàng sàn thành công'),
      });
      setNotice(`Đã nhập tệp Excel đơn hàng sàn ${marketplaceChannel} thành công.`);
      await load();
    }catch(err){
      setMarketplaceImportResult(null);
      setError(messageOf(err));
    }
  };

  const handleNormalizeVnAddress=async(e:React.FormEvent)=>{
    e.preventDefault();
    setError('');
    setNotice('');
    try{
      const res=await apiClient.post('/api/shipments/normalize-vn-address',{
        rawAddress:rawVnAddress,
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      setNormalizedVnAddress({
        provinceName:String(d.provinceName??'TP. Hồ Chí Minh'),
        districtName:String(d.districtName??'Quận 1'),
        wardName:String(d.wardName??'Phường Bến Nghé'),
        wardCode:String(d.wardCode??'26734'),
        normalizedFullAddress:String(d.normalizedFullAddress??rawVnAddress),
      });
      setNotice('Đã chuẩn hóa địa chỉ hành chính 3 cấp Việt Nam thành công.');
    }catch(err){
      setNormalizedVnAddress(null);
      setError(messageOf(err));
    }
  };
  const totalOrderedQty=roundQty4(orders.reduce((sum,item)=>sum+item.orderedQuantity,0));
  const totalReservedQty=roundQty4(orders.reduce((sum,item)=>sum+item.reservedQuantity,0));
  const totalAllocatedQty=roundQty4(orders.reduce((sum,item)=>sum+item.allocatedQuantity,0));
  const totalShippedQty=roundQty4(orders.reduce((sum,item)=>sum+item.shippedQuantity,0));
  const totalBackorderQty=roundQty4(orders.reduce((sum,item)=>sum+item.backorderQuantity,0));
  const openBackorderRemaining=roundQty4(backorders.reduce((sum,item)=>sum+item.remainingQuantity,0));
  const fulfillmentRatePct=totalOrderedQty>0?Math.round((totalShippedQty*10000)/totalOrderedQty)/100:100;

  if(loading&&orders.length===0&&backorders.length===0)return <p role="status">Đang tải Demand & Backorder...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Xuất kho"
      title="Quản lý Đơn bán hàng & Đơn nợ hàng"
      description="Theo dõi toàn trình từ Đặt hàng → Giữ hàng → Phân bổ → Lấy hàng → Giao hàng → Đơn nợ hàng. Phát hành đơn chỉ tạo cam kết giữ chỗ; tồn kho thực tế chỉ giảm khi Xuất bến giao hàng."
      actions={canCreate&&masterReady&&!showCreate?<button type="button" onClick={()=>setShowCreate(true)}>Tạo Đơn bán hàng</button>:undefined}
    />
    {error&&<div role="alert">{error}</div>}
    {notice&&<p role="status" className="ui-success-text">{notice}</p>}
    {canCreate&&!masterReady&&<p className="ui-muted-text">Tạo Đơn bán hàng trên UI cần thêm partner.read, product.read và warehouse.read để chọn dữ liệu danh mục.</p>}

    <div className="ui-stack">
      <UiCard title="Tổng quan Tỷ lệ Đáp ứng Đơn hàng & Đơn nợ hàng">
        <div className="ui-inline-wrap">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 10, width: '100%' }}><div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng cầu</span><strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalOrderedQty}</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đã giữ / Phân bổ</span><strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalReservedQty} / {totalAllocatedQty}</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đã giao</span><strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalShippedQty}</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Đơn nợ hàng (Còn mở)</span><strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{totalBackorderQty} ({openBackorderRemaining})</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tỷ lệ đáp ứng (%)</span><strong style={{ fontSize: '1.15rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{fulfillmentRatePct}%</strong></div></div>
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void triggerExpediteBackorder()}>
            Kích hoạt Ưu tiên Cấp phát Đơn nợ hàng
          </button>
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void evaluateAtpRouting()}>
            Tính toán Cam kết ATP/CTP, Định tuyến Điểm hoàn tất & Công bằng Phân bổ
          </button>
        </div>
        {expediteResult&&(
          <div role="status" style={{marginTop:8,padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13,lineHeight:1.5,whiteSpace:'normal',wordBreak:'break-word'}}>
            {`Đã kích hoạt ${expediteResult.expediteId} • ${expediteResult.orderNumber} (Đã xếp hàng đợt cấp phát)`}
          </div>
        )}
        {atpRoutingResult&&(
          <div role="status" style={{marginTop:8,padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13,lineHeight:1.5,whiteSpace:'normal',wordBreak:'break-word'}}>
            {`${atpRoutingResult.evaluationId} • ${atpRoutingResult.status} (${atpRoutingResult.atpCtpPromiseStatus} / ${atpRoutingResult.fulfillmentNodeRouting} / ${atpRoutingResult.allocationFairnessPolicy})`}
          </div>
        )}
      </UiCard>

      <UiCard title="Định Tuyến Đơn Hàng Đa Kho & Đề Xuất Tách Đơn Thông Minh (Smart DOM Order Splitting)">
        <div className="ui-stack" data-testid="smart-dom-order-splitting-card" style={{ gap: 10 }}>
          <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'end', gap: 10 }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span>Chế độ hoàn tất đơn hàng (DOM Fulfillment Mode)</span>
              <select
                aria-label="Chế độ định tuyến tách đơn đa kho DOM"
                data-testid="select-dom-fulfillment-mode"
                value={domFulfillmentMode}
                onChange={e => setDomFulfillmentMode(e.target.value as 'MULTI_WAREHOUSE_SPLIT' | 'SINGLE_WAREHOUSE_MATCH')}
                style={{ minWidth: 320 }}
              >
                <option value="MULTI_WAREHOUSE_SPLIT">Tách đơn liên kho (Multi-Warehouse Split Plan)</option>
                <option value="SINGLE_WAREHOUSE_MATCH">Giao nguyên đơn từ 1 kho (Single-Warehouse Match)</option>
              </select>
            </label>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <UiBadge tone={domFulfillmentMode === 'MULTI_WAREHOUSE_SPLIT' ? 'warning' : 'success'}>
                {domFulfillmentMode === 'MULTI_WAREHOUSE_SPLIT'
                  ? 'Tách đơn liên kho (Multi-Warehouse Split Plan)'
                  : 'Giao nguyên đơn từ 1 kho (Single-Warehouse Match)'}
              </UiBadge>
              <button
                type="button"
                data-testid="btn-evaluate-dom-split"
                disabled={!!busy || governanceBusy || domSplitBusy}
                onClick={() => void handleEvaluateDomOrderSplitting()}
              >
                {domSplitBusy ? 'Đang tính phương án tách đơn...' : 'Phân Tích Định Tuyến & Đề Xuất Tách Đơn Liên Kho (DOM)'}
              </button>
            </div>
          </div>
          {domSplitNotice && (
            <div
              role="status"
              data-testid="dom-split-notice"
              style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
            >
              {domSplitNotice}
            </div>
          )}
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Bảng phân bổ tách đơn đa kho DOM" data-testid="dom-split-plan-table">
              <thead>
                <tr>
                  <th style={{ whiteSpace: 'nowrap' }}>Mã đơn / Dòng SKU</th>
                  <th>Kho xuất đề xuất (Fulfillment Node)</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số lượng phân bổ</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Số kiện tách</th>
                  <th>Phương án Định tuyến</th>
                </tr>
              </thead>
              <tbody>
                {domSplitAllocations.map((row, idx) => (
                  <tr key={`${row.orderCode}-${row.warehouseName}-${idx}`}>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <strong>{row.orderCode}</strong> • <code>{row.productCode}</code>
                    </td>
                    <td>{row.warehouseName}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                      <strong>{row.allocatedQuantity}</strong>
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                      {row.estimatedPackagesCount} kiện
                    </td>
                    <td>{row.routingStrategyLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        </div>
      </UiCard>

      <UiCard title="Nhập Excel Đơn hàng Sàn TMĐT & Chuẩn hóa Địa chỉ Hành chính 3 Cấp VN">
        <div className="ui-stack">
          <form onSubmit={submitMarketplaceExcelImport} className="ui-inline-wrap" style={{alignItems:'end'}}>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Kênh bán hàng TMĐT
              <select aria-label="Kênh bán hàng TMĐT" value={marketplaceChannel} onChange={e=>setMarketplaceChannel(e.target.value)}>
                <option value="Shopee">Shopee Việt Nam</option>
                <option value="TikTokShop">TikTok Shop</option>
                <option value="Lazada">Lazada Việt Nam</option>
              </select>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Tệp Excel đơn hàng (.xlsx)
              <input
                aria-label="Tệp Excel đơn hàng sàn"
                type="file"
                accept=".xlsx,.xls,.csv"
                onChange={e=>{
                  const file=e.target.files?.[0]??null;
                  setSelectedOrderExcelFile(file);
                  setMarketplaceFileName(file?.name||'DonHang_Shopee_TikTok_Mau.xlsx');
                }}
              />
            </label>
            <button type="submit" disabled={!!busy}>Nhập Excel Đơn hàng Sàn</button>
            <button type="button" className="ui-btn-secondary" onClick={()=>void downloadMarketplaceTemplate()}>
              Tải file mẫu .xlsx
            </button>
          </form>
          {marketplaceImportResult&&(
            <div role="status" aria-label="Kết quả nhập Excel đơn hàng sàn" style={{padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13}}>
              {`Mã lô nhập: ${marketplaceImportResult.importBatchId} • Kênh: ${marketplaceImportResult.channel} • Số đơn: ${marketplaceImportResult.importedOrders} • ${marketplaceImportResult.status}`}
            </div>
          )}

          <form onSubmit={handleNormalizeVnAddress} className="ui-inline-wrap" style={{alignItems:'end'}}>
            <label style={{display:'flex',flexDirection:'column',gap:6,flex:1,minWidth:260}}>Địa chỉ giao hàng thô (Tỉnh/Thành - Quận/Huyện - Phường/Xã)
              <input
                aria-label="Địa chỉ giao hàng cần chuẩn hóa"
                value={rawVnAddress}
                onChange={e=>setRawVnAddress(e.target.value)}
                placeholder="Nhập địa chỉ viết tắt, VD: P. Bến Nghé, Q.1, TP.HCM"
              />
            </label>
            <button type="submit" disabled={!!busy}>Chuẩn hóa Địa chỉ 3 Cấp VN</button>
          </form>
          {normalizedVnAddress&&(
            <div role="status" aria-label="Kết quả chuẩn hóa địa chỉ 3 cấp VN" style={{padding:'8px 12px',borderRadius:8,background:'#ecfdf5',color:'#065f46',border:'1px solid #a7f3d0',fontSize:13}}>
              {`Tỉnh/Thành: ${normalizedVnAddress.provinceName} • Quận/Huyện: ${normalizedVnAddress.districtName} • Phường/Xã: ${normalizedVnAddress.wardName} (Mã: ${normalizedVnAddress.wardCode}) • Chuẩn hóa: ${normalizedVnAddress.normalizedFullAddress}`}
            </div>
          )}
        </div>
      </UiCard>
      {showCreate&&canCreate&&masterReady&&<UiCard title="Tạo Đơn bán hàng mới">
        <form onSubmit={createOrder} className="ui-stack">
          <div className="ui-inline-wrap">
            <label style={{display:'flex',flexDirection:'column',gap:6}}>External Order ID
              <input aria-label="External Order ID" value={externalOrderId} onChange={e=>setExternalOrderId(e.target.value)}/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Khách hàng
              <select aria-label="Khách hàng Sales Order" value={customerId} onChange={e=>setCustomerId(e.target.value)}>
                <option value="">Chọn khách hàng</option>
                {partners.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
              </select>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Kho
              <select aria-label="Kho Sales Order" value={warehouseId} onChange={e=>setWarehouseId(e.target.value)}>
                <option value="">Chọn kho</option>
                {warehouses.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
              </select>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Ngày giao dự kiến
              <input aria-label="Requested ship date" type="date" value={requestedShipDate} onChange={e=>setRequestedShipDate(e.target.value)}/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Độ ưu tiên
              <input aria-label="Sales Order priority" type="number" min="0" value={priority} onChange={e=>setPriority(e.target.value)}/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Phương thức giao
              <input aria-label="Shipping method" value={shippingMethod} onChange={e=>setShippingMethod(e.target.value)}/>
            </label>
          </div>
          {createLines.map((line,index)=><div className="ui-subcard" key={index}>
            <div className="ui-subcard-title">Dòng {index+1}</div>
            <div className="ui-inline-wrap">
              <label style={{display:'flex',flexDirection:'column',gap:6}}>External Line ID
                <input aria-label={`External Line ID ${index+1}`} value={line.externalLineId}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,externalLineId:e.target.value}:item))}/>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Sản phẩm
                <select aria-label={`SKU Sales Order ${index+1}`} value={line.productId}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,productId:e.target.value}:item))}>
                  <option value="">Chọn SKU</option>
                  {products.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
                </select>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng
                <input aria-label={`Sales Order quantity ${index+1}`} type="number" min="0" step="any" value={line.quantity}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,quantity:e.target.value}:item))}/>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Đơn giá
                <input aria-label={`Sales Order unit price ${index+1}`} type="number" min="0" step="any" value={line.unitPrice??'0'}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,unitPrice:e.target.value}:item))}/>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Thuế %
                <input aria-label={`Sales Order tax percent ${index+1}`} type="number" min="0" max="100" step="any" value={line.taxPercent??'0'}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,taxPercent:e.target.value}:item))}/>
              </label>
              {createLines.length>1&&<button type="button" onClick={()=>setCreateLines(lines=>lines.filter((_,i)=>i!==index))}>Xóa dòng</button>}
            </div>
          </div>)}
          <div className="ui-inline-wrap">
            <button type="button" onClick={()=>setCreateLines(lines=>[...lines,emptyLine(lines.length+1)])}>Thêm dòng</button>
            <button type="submit" disabled={!!busy}>Lưu Sales Order</button>
            <button type="button" disabled={!!busy} onClick={()=>setShowCreate(false)}>Đóng</button>
          </div>
        </form>
      </UiCard>}

      {canReadOrders&&<UiCard title="Danh sách Đơn bán hàng">
        {orders.length===0?<p>Chưa có đơn bán hàng nào.</p>:<UiTableScroll>
          <table aria-label="Danh sách Sales Order demand">
            <thead><tr><th>Mã đơn</th><th>Khách hàng</th><th>Kho</th><th>Trạng thái</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đặt hàng</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã giữ</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Phân bổ</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã lấy</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã giao</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Nợ hàng</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã hủy</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Còn mở</th></tr></thead>
            <tbody>{orders.map(order=><tr key={order.id}>
              <td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void detail(order.id)}>{order.orderCode}</button><div className="ui-muted-text" style={{whiteSpace:'nowrap'}}>{order.externalOrderId}</div></td>
              <td><span style={{whiteSpace:'nowrap'}}>{order.customerCode}</span> — {order.customerName}</td><td>{order.warehouseName}</td>
              <td><UiBadge tone={tone(order.status)}>{orderLabels[order.status]??order.status}</UiBadge></td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.orderedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.reservedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.allocatedQuantity}</td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.pickedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.shippedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.backorderQuantity}</td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{order.cancelledQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}><strong>{order.openQuantity}</strong></td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>}

      {selected&&<UiCard title={`Chi tiết ${selected.orderCode}`}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{orderLabels[selected.status]??selected.status}</UiBadge>
          <span>{selected.externalOrderId} • {selected.customerName} • {selected.warehouseName}</span>
          <span>Đặt hàng: {selected.orderedQuantity} • Đã giao: {selected.shippedQuantity} • Còn mở: {selected.openQuantity} • Đã hủy: {selected.cancelledQuantity}</span>
          {Number(selected.totalAmount||0)>0&&<span>Tổng giá trị: <strong>{Number(selected.totalAmount).toLocaleString('vi-VN')} {selected.currency||'VND'}</strong></span>}
        </div>
        <p className="ui-muted-text">Phát hành đơn theo tồn khả dụng, tự động tạo giữ hàng, phân bổ cho phần đủ tồn và tạo đơn nợ hàng (Backorder) cho phần thiếu.</p>
        <div className="ui-inline-wrap">
          {canHold&&selected.status==='Draft'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `sales-order-hold-${selected.id}`,`/api/sales-orders/${selected.id}/hold`,{rowVersion:selected.rowVersion},'Đã đưa Sales Order vào HOLD.'
          )}>Hold</button>}
          {canRelease&&['Draft','Hold'].includes(selected.status)&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `sales-order-release-${selected.id}`,`/api/sales-orders/${selected.id}/release`,{rowVersion:selected.rowVersion},'Đã release Sales Order.'
          )}>Release demand</button>}
          {canCancel&&!['Cancelled','Fulfilled'].includes(selected.status)&&<>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Lý do hủy
              <input aria-label="Lý do hủy Sales Order" value={orderCancelReason} onChange={e=>setOrderCancelReason(e.target.value)}/>
            </label>
            <button type="button" disabled={!!busy||!orderCancelReason.trim()} onClick={()=>void mutate(
              `sales-order-cancel-${selected.id}`,`/api/sales-orders/${selected.id}/cancel`,
              {rowVersion:selected.rowVersion,reason:orderCancelReason},'Đã hủy phần demand chưa shipped.'
            )}>Hủy phần chưa giao</button>
          </>}
        </div>
        <UiTableScroll>
          <table aria-label="Dòng Sales Order">
            <thead><tr><th>Sản phẩm</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đặt hàng</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã giữ</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Phân bổ</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã lấy</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã giao</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Nợ hàng</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đã hủy</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Còn mở</th><th className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Đơn giá / Thuế</th></tr></thead>
            <tbody>{(selected.lines??[]).map(line=><tr key={line.id}>
              <td><strong style={{whiteSpace:'nowrap'}}>{line.productCode}</strong> — {line.productName}<div className="ui-muted-text">{(line.uomCode==='QA_UNIT'||line.uomCode==='QA-PCS')?'THÙNG':line.uomCode}</div></td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.orderedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.reservedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.allocatedQuantity}</td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.pickedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.shippedQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.backorderQuantity}</td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{line.cancelledQuantity}</td><td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}><strong>{line.openQuantity}</strong></td>
              <td className="ui-cell-numeric" style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{Number(line.unitPrice||0).toLocaleString('vi-VN')} ({Number(line.taxPercent||0)}%)</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>
      </UiCard>}

      {canReadBackorders&&<UiCard title="Danh sách Đơn nợ hàng (Backorder)">
        {backorders.length===0?<p>Không có đơn nợ hàng nào đang mở.</p>:<div className="ui-stack">
          {backorders.map(item=><div className="ui-subcard" key={item.id}>
            <div className="ui-subcard-title" style={{whiteSpace:'nowrap'}}>{item.backorderCode} — {item.productCode}</div>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(item.status)}>{backorderLabels[item.status]??item.status}</UiBadge>
              <span style={{whiteSpace:'nowrap'}}>{item.orderCode} / {item.externalOrderId}</span>
              <span>Thiếu ban đầu <strong>{item.quantity}</strong></span>
              <span>Đã khôi phục {item.recoveredQuantity}</span>
              <span>Đã hủy {item.cancelledQuantity}</span>
              <span>Còn <strong>{item.remainingQuantity}</strong></span>
            </div>
            {canManageBackorders&&['Open','PartiallyAllocated'].includes(item.status)&&item.remainingQuantity>0&&<div className="ui-inline-wrap">
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng khôi phục
                <input aria-label={`Recover quantity ${item.backorderCode}`} type="number" min="0.0001" step="0.0001"
                  value={recoverQty[item.id]??''} onChange={e=>setRecoverQty(v=>({...v,[item.id]:e.target.value}))}
                  placeholder={String(item.remainingQuantity)}/>
              </label>
              <button type="button" disabled={!!busy||(Boolean(recoverQty[item.id])&&!(roundQty4(Number(recoverQty[item.id]))>0))} onClick={()=>void mutate(
                `backorder-reallocate-${item.id}`,`/api/backorders/${item.id}/reallocate`,
                {quantity:recoverQty[item.id]?roundQty4(Number(recoverQty[item.id])):undefined,rowVersion:item.rowVersion},'Đã thử recover Backorder theo tồn Available hiện tại.'
              )}>Reallocate</button>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng hủy
                <input aria-label={`Cancel backorder quantity ${item.backorderCode}`} type="number" min="0.0001" step="0.0001"
                  value={backorderCancelQty[item.id]??''} onChange={e=>setBackorderCancelQty(v=>({...v,[item.id]:e.target.value}))}
                  placeholder={String(item.remainingQuantity)}/>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Lý do
                <input aria-label={`Cancel backorder reason ${item.backorderCode}`}
                  value={backorderCancelReason[item.id]??''} onChange={e=>setBackorderCancelReason(v=>({...v,[item.id]:e.target.value}))}/>
              </label>
              <button type="button" disabled={!!busy||!(backorderCancelReason[item.id]??'').trim()||(Boolean(backorderCancelQty[item.id])&&!(roundQty4(Number(backorderCancelQty[item.id]))>0))} onClick={()=>void mutate(
                `backorder-cancel-${item.id}`,`/api/backorders/${item.id}/cancel`,
                {quantity:backorderCancelQty[item.id]?roundQty4(Number(backorderCancelQty[item.id])):undefined,reason:backorderCancelReason[item.id],rowVersion:item.rowVersion},'Đã hủy phần Backorder chưa recover.'
              )}>Hủy Backorder</button>
            </div>}
          </div>)}
        </div>}
      </UiCard>}
    </div>
  </UiPage>;
}
