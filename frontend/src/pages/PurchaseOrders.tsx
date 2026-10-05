import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiPage,
  UiTableScroll,
} from '../ui/ProductionUi';
import { InboundPlanningOverview, productUnitOptions, runInboundStateCommand } from './inboundPlanningShared';
import './InboundPlanning.css';

type PurchaseOrderList = {
  id:number; externalPoId:string; code:string; supplierCode:string; supplierName:string;
  warehouseId:number; warehouseName:string; status:string; orderDate:string; expectedDate?:string;
  baseOrderedQuantity:number;
};
type PurchaseOrderLine = {
  id:number; externalLineId:string; lineNo:number; productId:number; productCode:string; productName:string;
  orderedQuantity:number; operationUnitCode:string; baseOrderedQuantity:number; baseUnitCode:string;
  allowedOverReceiptPct:number; allowedUnderReceiptPct:number;
};
type PurchaseOrderDetail = PurchaseOrderList & {
  sourceSystem:string; currency?:string; externalVersion?:string; rowVersion?:string; lines:PurchaseOrderLine[];
};
type Warehouse={id:number;name:string};
type Partner={id:number;code:string;name:string;isActive:boolean};
type ProductUom={unitId:number;unitCode:string;unitName:string;decimalPlaces:number;conversionFactor:number;version:number};
type Product={id:number;code:string;name:string;unitId:number;unitCode:string;unitName:string;uoms:ProductUom[]};
type LineForm={externalLineId:string;productId:number|'';operationUnitId:number|'';orderedQuantity:number|'';allowedOverReceiptPct:number|'';allowedUnderReceiptPct:number|''};

const labels:Record<string,string>={
  Draft:'Nháp',Open:'Đang mở',PartiallyReceived:'Đã nhận một phần',Received:'Đã nhận đủ',Closed:'Đã đóng',Cancelled:'Đã hủy',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Received'||status==='Closed'?'success':status==='PartiallyReceived'?'warning':status==='Cancelled'?'danger':'neutral';
const isoDay=(value?:string)=>value?new Date(value).toLocaleDateString('vi-VN'):'—';
const emptyLine=():LineForm=>({externalLineId:'',productId:'',operationUnitId:'',orderedQuantity:'',allowedOverReceiptPct:0,allowedUnderReceiptPct:0});

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
  const [busy,setBusy]=useState('');
  const requestSeq=useRef(0);
  const mutationLock=useRef(new Set<string>());

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
    const q=search.trim().toLowerCase();
    if(!q)return items;
    return items.filter(item=>[item.code,item.externalPoId,item.supplierCode,item.supplierName,item.warehouseName].join(' ').toLowerCase().includes(q));
  },[items,search]);

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
    if(lines.length===0||lines.some(line=>!line.externalLineId.trim()||!line.productId||!line.operationUnitId||Number(line.orderedQuantity)<=0)){
      setError('Mỗi dòng cần mã dòng nguồn, sản phẩm, đơn vị và số lượng lớn hơn 0.');return;
    }
    setCreating(true);
    const action='purchase-order-create:'+code.trim()+':'+externalPoId.trim();
    try{
      const payload={
        sourceSystem,externalPoId,code,supplierId,warehouseId,
        orderDate:orderDate?orderDate+'T00:00:00':undefined,
        expectedDate:expectedDate?expectedDate+'T00:00:00':null,
        currency:currency||null,
        lines:lines.map(line=>({
          externalLineId:line.externalLineId,productId:Number(line.productId),operationUnitId:Number(line.operationUnitId),
          orderedQuantity:Number(line.orderedQuantity),allowedOverReceiptPct:Number(line.allowedOverReceiptPct||0),
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

  if(loading)return <p role="status">Đang tải đơn mua...</p>;

  return <UiPage>
    <InboundPlanningOverview
      title="Đơn mua (PO)"
      description="Theo dõi nguồn hàng dự kiến từ ERP/Procurement trước khi tạo ASN và tiếp nhận. PO không làm tăng tồn kho."
      error={error}
      success={success}
      metrics={[
        {value:items.length,label:'PO trong phạm vi hiện tại'},
        {value:items.filter(x=>x.status==='Open').length,label:'Đang mở'},
        {value:items.filter(x=>x.status==='PartiallyReceived').length,label:'Đã nhận một phần'},
        {value:items.filter(x=>x.status==='Received').length,label:'Đã nhận đủ'},
      ]}
      labels={labels}
      statusFilter={statusFilter}
      setStatusFilter={setStatusFilter}
      search={search}
      setSearch={setSearch}
      placeholder="Mã PO, mã nguồn, nhà cung cấp, kho"
    />

    <div className="inbound-planning-grid">
      <UiCard title="Danh sách đơn mua">
        {filtered.length===0?<UiEmptyState title="Chưa có đơn mua phù hợp" detail="Thay đổi bộ lọc hoặc tạo PO mới nếu bạn có quyền."/>:
          <UiTableScroll>
            <table className="inbound-planning-table" aria-label="Danh sách đơn mua">
              <thead><tr><th>Mã PO</th><th>Nhà cung cấp</th><th>Kho</th><th>Ngày dự kiến</th><th className="inbound-planning-numeric">SL cơ sở</th><th>Trạng thái</th></tr></thead>
              <tbody>{filtered.map(item=><tr key={item.id}>
                <td><button type="button" onClick={()=>void viewDetail(item.id)}><strong>{item.code}</strong><br/><small>{item.externalPoId}</small></button></td>
                <td>{item.supplierCode}<br/><small>{item.supplierName}</small></td>
                <td>{item.warehouseName}</td>
                <td>{isoDay(item.expectedDate)}</td>
                <td className="inbound-planning-numeric">{item.baseOrderedQuantity.toLocaleString('vi-VN')}</td>
                <td><UiBadge tone={tone(item.status)}>{labels[item.status]??item.status}</UiBadge></td>
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
          </div>
          <div className="inbound-planning-actions">
            {canRelease&&selected.status==='Draft'&&<button className="ui-primary-button" disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/open','po-open:'+selected.id,'Đơn mua đã được mở cho quy trình nhận hàng.')}>{busy?'Đang lưu...':'Mở đơn mua'}</button>}
            {canClose&&['PartiallyReceived','Received'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/close','po-close:'+selected.id,'Đơn mua đã được đóng.')}>Đóng đơn mua</button>}
            {canCancel&&['Draft','Open'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/purchase-orders/'+selected.id+'/cancel','po-cancel:'+selected.id,'Đơn mua đã được hủy.')}>Hủy đơn mua</button>}
          </div>
          <UiTableScroll>
            <table aria-label={'Dòng đơn mua '+selected.code}>
              <thead><tr><th>Dòng</th><th>Sản phẩm</th><th className="inbound-planning-numeric">Đặt mua</th><th className="inbound-planning-numeric">SL cơ sở</th><th>Dung sai</th></tr></thead>
              <tbody>{selected.lines.map(line=><tr key={line.id}>
                <td>{line.externalLineId}</td>
                <td>{line.productCode}<br/><small>{line.productName}</small></td>
                <td className="inbound-planning-numeric">{line.orderedQuantity.toLocaleString('vi-VN')} {line.operationUnitCode}</td>
                <td className="inbound-planning-numeric">{line.baseOrderedQuantity.toLocaleString('vi-VN')} {line.baseUnitCode}</td>
                <td>+{line.allowedOverReceiptPct}% / -{line.allowedUnderReceiptPct}%</td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>}

        {canCreate&&<UiCard title="Tạo đơn mua nháp">
          {!canPrepareCreate&&<div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br/>Cần quyền đọc kho, sản phẩm và đối tác để tạo PO an toàn.</div>}
          {canPrepareCreate&&<form className="inbound-planning-form" onSubmit={submitCreate}>
            <div className="inbound-planning-form-grid">
              <label>Hệ thống nguồn<input required value={sourceSystem} onChange={e=>setSourceSystem(e.target.value)} /></label>
              <label>Mã PO ngoài hệ thống<input required value={externalPoId} onChange={e=>setExternalPoId(e.target.value)} /></label>
              <label>Mã PO WMS<input required value={code} onChange={e=>setCode(e.target.value)} /></label>
              <label>Tiền tệ tham chiếu<input value={currency} onChange={e=>setCurrency(e.target.value.toUpperCase())} maxLength={10}/></label>
              <label>Nhà cung cấp<select required value={supplierId} onChange={e=>setSupplierId(e.target.value?Number(e.target.value):'')}><option value="">Chọn nhà cung cấp</option>{suppliers.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
              <label>Kho nhận<select required value={warehouseId} onChange={e=>setWarehouseId(e.target.value?Number(e.target.value):'')}><option value="">Chọn kho</option>{warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
              <label>Ngày đặt<input required type="date" value={orderDate} onChange={e=>setOrderDate(e.target.value)}/></label>
              <label>Ngày dự kiến<input type="date" value={expectedDate} onChange={e=>setExpectedDate(e.target.value)}/></label>
            </div>

            <div>
              <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng</strong><button type="button" onClick={()=>setLines(current=>[...current,emptyLine()])}>Thêm dòng</button></div>
              <div className="inbound-planning-lines">
                {lines.map((line,index)=><div className="inbound-planning-line" key={index}>
                  <label>Mã dòng nguồn<input required value={line.externalLineId} onChange={e=>changeLine(index,{externalLineId:e.target.value})}/></label>
                  <label>Sản phẩm<select required value={line.productId} onChange={e=>changeLine(index,{productId:e.target.value?Number(e.target.value):''})}><option value="">Chọn sản phẩm</option>{products.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
                  <label>Đơn vị<select required value={line.operationUnitId} onChange={e=>changeLine(index,{operationUnitId:e.target.value?Number(e.target.value):''})}><option value="">Chọn đơn vị</option>{productUnitOptions(products,line.productId).map(x=><option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
                  <label>Số lượng<input required min="0" step="any" type="number" value={line.orderedQuantity} onChange={e=>changeLine(index,{orderedQuantity:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Nhận vượt %<input min="0" max="100" step="any" type="number" value={line.allowedOverReceiptPct} onChange={e=>changeLine(index,{allowedOverReceiptPct:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <label>Nhận thiếu %<input min="0" max="100" step="any" type="number" value={line.allowedUnderReceiptPct} onChange={e=>changeLine(index,{allowedUnderReceiptPct:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <button type="button" disabled={lines.length===1} onClick={()=>setLines(current=>current.filter((_,i)=>i!==index))}>Xóa</button>
                </div>)}
              </div>
            </div>
            <p className="inbound-planning-helper">Tạo PO chỉ ghi nhận expected inbound; tồn kho chỉ thay đổi khi Phiếu nhập được POST.</p>
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
