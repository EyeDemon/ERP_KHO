import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type OrderLine = {
  id:number; externalLineId:string; productId:number; productCode:string; productName:string; uomCode:string;
  orderedQuantity:number; reservedQuantity:number; allocatedQuantity:number; pickedQuantity:number;
  shippedQuantity:number; backorderQuantity:number; cancelledQuantity:number; openQuantity:number;
};
type SalesOrder = {
  id:number; orderCode:string; externalOrderId:string; customerId:number; customerCode:string; customerName:string;
  warehouseId:number; warehouseName:string; requestedShipDate?:string; priority:number; shippingMethod?:string;
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
type CreateLine = { externalLineId:string; productId:string; quantity:string };

const orderLabels:Record<string,string>={
  Draft:'Nháp',Hold:'Tạm giữ',Released:'Đã release',PartiallyFulfilled:'Fulfill một phần',
  Fulfilled:'Hoàn tất',Cancelled:'Đã hủy',
};
const backorderLabels:Record<string,string>={
  Open:'Mở',PartiallyAllocated:'Recover một phần',Allocated:'Đã recover',Fulfilled:'Đã fulfill',Cancelled:'Đã hủy',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  ['Fulfilled'].includes(status)?'success':
  status==='Cancelled'?'danger':
  ['Hold','PartiallyFulfilled','PartiallyAllocated'].includes(status)?'warning':'neutral';

const messageOf=(failure:unknown)=>{
  const response=(failure as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='BACKORDER_INSUFFICIENT_AVAILABLE')return 'Chưa có tồn khả dụng để recover Backorder.';
  if(code==='BACKORDER_EXECUTION_STARTED')return 'Picking đã bắt đầu. Foundation hiện chưa recover Backorder bằng multi-shipment sau execution.';
  if(code==='SALES_ORDER_ALREADY_RELEASED')return 'Sales Order đã được release hoặc không còn ở trạng thái cho phép release.';
  if(code==='SALES_ORDER_DUPLICATE')return 'External Order ID đã tồn tại.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác Demand/Backorder này.';
  if(response?.status===409)return response.data?.message??'Demand đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message??'Không thể xử lý Demand/Backorder.';
};

const emptyLine=(index:number):CreateLine=>({externalLineId:String(index),productId:'',quantity:''});

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
    const lines=createLines
      .filter(line=>line.productId&&Number(line.quantity)>0)
      .map(line=>({externalLineId:line.externalLineId.trim(),productId:Number(line.productId),orderedQuantity:Number(line.quantity)}));
    if(!externalOrderId.trim()||!customerId||!warehouseId||lines.length===0){
      setError('External Order ID, khách hàng, kho và ít nhất một dòng hàng là bắt buộc.');
      return;
    }
    if(new Set(lines.map(line=>line.productId)).size!==lines.length){
      setError('Foundation hiện yêu cầu mỗi SKU chỉ xuất hiện một dòng trong Sales Order.');
      return;
    }
    const key=`sales-order-create-${externalOrderId.trim()}`;
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');setNotice('');
    try{
      const response=await apiClient.post('/api/sales-orders',{
        externalOrderId:externalOrderId.trim(),
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

  if(loading)return <p role="status">Đang tải Demand & Backorder...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Outbound"
      title="Sales Order & Backorder"
      description="Theo dõi Ordered / Reserved / Allocated / Picked / Shipped / Backorder / Cancelled. Release chỉ tạo commitment; OnHand chỉ giảm tại Shipment Dispatch."
      actions={canCreate&&masterReady&&!showCreate?<button type="button" onClick={()=>setShowCreate(true)}>Tạo Sales Order</button>:undefined}
    />
    {error&&<div role="alert">{error}</div>}
    {notice&&<p role="status" className="ui-success-text">{notice}</p>}
    {canCreate&&!masterReady&&<p className="ui-muted-text">Tạo Sales Order trên UI cần thêm partner.read, product.read và warehouse.read để chọn master data.</p>}

    <div className="ui-stack">
      {showCreate&&canCreate&&masterReady&&<UiCard title="Tạo Sales Order">
        <form onSubmit={createOrder} className="ui-stack">
          <div className="ui-inline-wrap">
            <label>External Order ID
              <input aria-label="External Order ID" value={externalOrderId} onChange={e=>setExternalOrderId(e.target.value)}/>
            </label>
            <label>Khách hàng
              <select aria-label="Khách hàng Sales Order" value={customerId} onChange={e=>setCustomerId(e.target.value)}>
                <option value="">Chọn khách hàng</option>
                {partners.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
              </select>
            </label>
            <label>Kho
              <select aria-label="Kho Sales Order" value={warehouseId} onChange={e=>setWarehouseId(e.target.value)}>
                <option value="">Chọn kho</option>
                {warehouses.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
              </select>
            </label>
            <label>Requested ship date
              <input aria-label="Requested ship date" type="date" value={requestedShipDate} onChange={e=>setRequestedShipDate(e.target.value)}/>
            </label>
            <label>Priority
              <input aria-label="Sales Order priority" type="number" min="0" value={priority} onChange={e=>setPriority(e.target.value)}/>
            </label>
            <label>Shipping method
              <input aria-label="Shipping method" value={shippingMethod} onChange={e=>setShippingMethod(e.target.value)}/>
            </label>
          </div>
          {createLines.map((line,index)=><fieldset key={index}>
            <legend>Dòng {index+1}</legend>
            <div className="ui-inline-wrap">
              <label>External Line ID
                <input aria-label={`External Line ID ${index+1}`} value={line.externalLineId}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,externalLineId:e.target.value}:item))}/>
              </label>
              <label>SKU
                <select aria-label={`SKU Sales Order ${index+1}`} value={line.productId}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,productId:e.target.value}:item))}>
                  <option value="">Chọn SKU</option>
                  {products.map(item=><option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}
                </select>
              </label>
              <label>Số lượng
                <input aria-label={`Sales Order quantity ${index+1}`} type="number" min="0" step="any" value={line.quantity}
                  onChange={e=>setCreateLines(lines=>lines.map((item,i)=>i===index?{...item,quantity:e.target.value}:item))}/>
              </label>
              {createLines.length>1&&<button type="button" onClick={()=>setCreateLines(lines=>lines.filter((_,i)=>i!==index))}>Xóa dòng</button>}
            </div>
          </fieldset>)}
          <div className="ui-inline-wrap">
            <button type="button" onClick={()=>setCreateLines(lines=>[...lines,emptyLine(lines.length+1)])}>Thêm dòng</button>
            <button type="submit" disabled={!!busy}>Lưu Sales Order</button>
            <button type="button" disabled={!!busy} onClick={()=>setShowCreate(false)}>Đóng</button>
          </div>
        </form>
      </UiCard>}

      {canReadOrders&&<UiCard title="Sales Order demand">
        {orders.length===0?<p>Chưa có Sales Order.</p>:<UiTableScroll>
          <table aria-label="Danh sách Sales Order demand">
            <thead><tr><th>Order</th><th>Khách hàng</th><th>Kho</th><th>Trạng thái</th><th>Ordered</th><th>Reserved</th><th>Allocated</th><th>Picked</th><th>Shipped</th><th>Backorder</th><th>Cancelled</th><th>Open</th></tr></thead>
            <tbody>{orders.map(order=><tr key={order.id}>
              <td><button type="button" onClick={()=>void detail(order.id)}>{order.orderCode}</button><div className="ui-muted-text">{order.externalOrderId}</div></td>
              <td>{order.customerCode} — {order.customerName}</td><td>{order.warehouseName}</td>
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
          <span>Ordered {selected.orderedQuantity} = Shipped {selected.shippedQuantity} + Open {selected.openQuantity} + Cancelled {selected.cancelledQuantity}</span>
        </div>
        <p className="ui-muted-text">Foundation hiện release theo tồn Available, tạo Reservation + Allocation cho phần giữ được và Backorder cho phần thiếu. Amendment/update/close, SLA priority automation và multi-shipment recovery sau khi Picking bắt đầu chưa hoàn tất.</p>
        <div className="ui-inline-wrap">
          {canHold&&selected.status==='Draft'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `sales-order-hold-${selected.id}`,`/api/sales-orders/${selected.id}/hold`,{rowVersion:selected.rowVersion},'Đã đưa Sales Order vào HOLD.'
          )}>Hold</button>}
          {canRelease&&['Draft','Hold'].includes(selected.status)&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `sales-order-release-${selected.id}`,`/api/sales-orders/${selected.id}/release`,{rowVersion:selected.rowVersion},'Đã release Sales Order.'
          )}>Release demand</button>}
          {canCancel&&!['Cancelled','Fulfilled'].includes(selected.status)&&<>
            <label>Lý do hủy
              <input aria-label="Lý do hủy Sales Order" value={orderCancelReason} onChange={e=>setOrderCancelReason(e.target.value)}/>
            </label>
            <button type="button" disabled={!!busy||!orderCancelReason.trim()} onClick={()=>void mutate(
              `sales-order-cancel-${selected.id}`,`/api/sales-orders/${selected.id}/cancel`,
              {rowVersion:selected.rowVersion,reason:orderCancelReason},'Đã hủy phần demand chưa shipped.'
            )}>Hủy phần chưa fulfill</button>
          </>}
        </div>
        <UiTableScroll>
          <table aria-label="Dòng Sales Order">
            <thead><tr><th>SKU</th><th>Ordered</th><th>Reserved</th><th>Allocated</th><th>Picked</th><th>Shipped</th><th>Backorder</th><th>Cancelled</th><th>Open</th></tr></thead>
            <tbody>{(selected.lines??[]).map(line=><tr key={line.id}>
              <td>{line.productCode} — {line.productName}<div className="ui-muted-text">{line.uomCode}</div></td>
              <td>{line.orderedQuantity}</td><td>{line.reservedQuantity}</td><td>{line.allocatedQuantity}</td>
              <td>{line.pickedQuantity}</td><td>{line.shippedQuantity}</td><td>{line.backorderQuantity}</td>
              <td>{line.cancelledQuantity}</td><td><strong>{line.openQuantity}</strong></td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>
      </UiCard>}

      {canReadBackorders&&<UiCard title="Backorder workbench">
        {backorders.length===0?<p>Không có Backorder đang theo dõi.</p>:<div className="ui-stack">
          {backorders.map(item=><fieldset key={item.id}>
            <legend>{item.backorderCode} — {item.productCode}</legend>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(item.status)}>{backorderLabels[item.status]??item.status}</UiBadge>
              <span>{item.orderCode} / {item.externalOrderId}</span>
              <span>Thiếu ban đầu <strong>{item.quantity}</strong></span>
              <span>Đã recover {item.recoveredQuantity}</span>
              <span>Đã hủy {item.cancelledQuantity}</span>
              <span>Còn <strong>{item.remainingQuantity}</strong></span>
            </div>
            {canManageBackorders&&['Open','PartiallyAllocated'].includes(item.status)&&item.remainingQuantity>0&&<div className="ui-inline-wrap">
              <label>Số lượng recover
                <input aria-label={`Recover quantity ${item.backorderCode}`} type="number" min="0" step="any"
                  value={recoverQty[item.id]??''} onChange={e=>setRecoverQty(v=>({...v,[item.id]:e.target.value}))}
                  placeholder={String(item.remainingQuantity)}/>
              </label>
              <button type="button" disabled={!!busy} onClick={()=>void mutate(
                `backorder-reallocate-${item.id}`,`/api/backorders/${item.id}/reallocate`,
                {quantity:recoverQty[item.id]?Number(recoverQty[item.id]):undefined,rowVersion:item.rowVersion},'Đã thử recover Backorder theo tồn Available hiện tại.'
              )}>Reallocate</button>
              <label>Số lượng hủy
                <input aria-label={`Cancel backorder quantity ${item.backorderCode}`} type="number" min="0" step="any"
                  value={backorderCancelQty[item.id]??''} onChange={e=>setBackorderCancelQty(v=>({...v,[item.id]:e.target.value}))}
                  placeholder={String(item.remainingQuantity)}/>
              </label>
              <label>Lý do
                <input aria-label={`Cancel backorder reason ${item.backorderCode}`}
                  value={backorderCancelReason[item.id]??''} onChange={e=>setBackorderCancelReason(v=>({...v,[item.id]:e.target.value}))}/>
              </label>
              <button type="button" disabled={!!busy||!(backorderCancelReason[item.id]??'').trim()} onClick={()=>void mutate(
                `backorder-cancel-${item.id}`,`/api/backorders/${item.id}/cancel`,
                {quantity:backorderCancelQty[item.id]?Number(backorderCancelQty[item.id]):undefined,reason:backorderCancelReason[item.id],rowVersion:item.rowVersion},'Đã hủy phần Backorder chưa recover.'
              )}>Hủy Backorder</button>
            </div>}
          </fieldset>)}
        </div>}
      </UiCard>}
    </div>
  </UiPage>;
}
