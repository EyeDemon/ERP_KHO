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
      });
      if(res?.data?.expediteId){
        setExpediteResult({
          expediteId:String(res.data.expediteId),
          orderNumber:String(res.data.orderNumber??backorders[0]?.orderCode??orders[0]?.orderCode??'SO-PRIORITY-QUEUE'),
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
          atpCtpPromiseStatus:String(res.data.atpCtpPromiseStatus||''),
          fulfillmentNodeRouting:String(res.data.fulfillmentNodeRouting||''),
          allocationFairnessPolicy:String(res.data.allocationFairnessPolicy||''),
          serviceSegmentation:String(res.data.serviceSegmentation||''),
          status:String(res.data.status),
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
      description="Theo dõi toàn trình từ Đặt hàng → Giữ hàng → Phân bổ → Lấy hàng → Giao hàng → Đơn nợ hàng (Backorder). Phát hành đơn chỉ tạo cam kết giữ chỗ; tồn kho thực tế chỉ giảm khi Xuất bến giao hàng."
      actions={canCreate&&masterReady&&!showCreate?<button type="button" onClick={()=>setShowCreate(true)}>Tạo Đơn bán hàng</button>:undefined}
    />
    {error&&<div role="alert">{error}</div>}
    {notice&&<p role="status" className="ui-success-text">{notice}</p>}
    {canCreate&&!masterReady&&<p className="ui-muted-text">Tạo Đơn bán hàng trên UI cần thêm partner.read, product.read và warehouse.read để chọn dữ liệu danh mục.</p>}

    <div className="ui-stack">
      <UiCard title="Tổng quan Tỷ lệ Đáp ứng Đơn hàng & Đơn nợ hàng — OUT-01 / OUT-09">
        <div className="ui-inline-wrap">
          <span>Tổng cầu: <strong>{totalOrderedQty}</strong></span>
          <span>Đã giữ / Phân bổ: <strong>{totalReservedQty} / {totalAllocatedQty}</strong></span>
          <span>Đã giao: <strong>{totalShippedQty}</strong></span>
          <span>Đơn nợ hàng: <strong>{totalBackorderQty}</strong> (Còn mở: {openBackorderRemaining})</span>
          <span>Tỷ lệ đáp ứng (%): <strong>{fulfillmentRatePct}%</strong></span>
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void triggerExpediteBackorder()}>
            Kích hoạt Ưu tiên Cấp phát Đơn tồn thiếu Backorder (OUT-09)
          </button>
          {expediteResult&&(
            <UiBadge tone="success">
              {`Đã kích hoạt ${expediteResult.expediteId} • ${expediteResult.orderNumber} (Đã xếp hàng đợt cấp phát)`}
            </UiBadge>
          )}
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void evaluateAtpRouting()}>
            Tính toán Cam kết ATP/CTP, Định tuyến Điểm hoàn tất & Công bằng Phân bổ (AX-23, AX-26, AX-39, AX-40)
          </button>
          {atpRoutingResult&&(
            <UiBadge tone="success">
              {`${atpRoutingResult.evaluationId} • ${atpRoutingResult.status} (${atpRoutingResult.atpCtpPromiseStatus} / ${atpRoutingResult.fulfillmentNodeRouting} / ${atpRoutingResult.allocationFairnessPolicy})`}
            </UiBadge>
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
            <thead><tr><th>Mã đơn</th><th>Khách hàng</th><th>Kho</th><th>Trạng thái</th><th>Đặt hàng</th><th>Đã giữ</th><th>Phân bổ</th><th>Đã lấy</th><th>Đã giao</th><th>Nợ hàng</th><th>Đã hủy</th><th>Còn mở</th></tr></thead>
            <tbody>{orders.map(order=><tr key={order.id}>
              <td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void detail(order.id)}>{order.orderCode}</button><div className="ui-muted-text" style={{whiteSpace:'nowrap'}}>{order.externalOrderId}</div></td>
              <td><span style={{whiteSpace:'nowrap'}}>{order.customerCode}</span> — {order.customerName}</td><td>{order.warehouseName}</td>
              <td><UiBadge tone={tone(order.status)}>{orderLabels[order.status]??order.status}</UiBadge></td>
              <td>{order.orderedQuantity}</td><td>{order.reservedQuantity}</td><td>{order.allocatedQuantity}</td>
              <td>{order.pickedQuantity}</td><td>{order.shippedQuantity}</td><td>{order.backorderQuantity}</td>
              <td>{order.cancelledQuantity}</td><td><strong>{order.openQuantity}</strong></td>
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
            <thead><tr><th>Sản phẩm</th><th>Đặt hàng</th><th>Đã giữ</th><th>Phân bổ</th><th>Đã lấy</th><th>Đã giao</th><th>Nợ hàng</th><th>Đã hủy</th><th>Còn mở</th><th>Đơn giá / Thuế</th></tr></thead>
            <tbody>{(selected.lines??[]).map(line=><tr key={line.id}>
              <td><strong style={{whiteSpace:'nowrap'}}>{line.productCode}</strong> — {line.productName}<div className="ui-muted-text">{(line.uomCode==='QA_UNIT'||line.uomCode==='QA-PCS')?'THÙNG':line.uomCode}</div></td>
              <td>{line.orderedQuantity}</td><td>{line.reservedQuantity}</td><td>{line.allocatedQuantity}</td>
              <td>{line.pickedQuantity}</td><td>{line.shippedQuantity}</td><td>{line.backorderQuantity}</td>
              <td>{line.cancelledQuantity}</td><td><strong>{line.openQuantity}</strong></td>
              <td>{Number(line.unitPrice||0).toLocaleString('vi-VN')} ({Number(line.taxPercent||0)}%)</td>
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
              <span>Đã recover {item.recoveredQuantity}</span>
              <span>Đã hủy {item.cancelledQuantity}</span>
              <span>Còn <strong>{item.remainingQuantity}</strong></span>
            </div>
            {canManageBackorders&&['Open','PartiallyAllocated'].includes(item.status)&&item.remainingQuantity>0&&<div className="ui-inline-wrap">
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng recover
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
