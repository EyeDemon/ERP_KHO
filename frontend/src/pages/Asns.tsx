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
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
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
  Draft:'Nháp',Confirmed:'Đã xác nhận',InTransit:'Đang vận chuyển',Arrived:'Đã đến',
  Receiving:'Đang tiếp nhận',Completed:'Hoàn tất ASN',Cancelled:'Đã hủy',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Completed'?'success':status==='Arrived'||status==='Receiving'?'warning':status==='Cancelled'?'danger':'neutral';
const dateTime=(value?:string)=>value?new Date(value).toLocaleString('vi-VN'):'—';
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
  const [busy,setBusy]=useState('');
  const requestSeq=useRef(0);
  const mutationLock=useRef(new Set<string>());

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
    const q=search.trim().toLowerCase();
    if(!q)return items;
    return items.filter(item=>[item.code,item.purchaseOrderCode??'',item.supplierCode,item.supplierName,item.warehouseName].join(' ').toLowerCase().includes(q));
  },[items,search]);

  const viewDetail=async(id:number)=>{
    setError('');setSuccess('');
    try{setSelected((await apiClient.get('/api/asns/'+id)).data);}
    catch(err:unknown){setSelected(null);setError(permissionError(err,'Không tìm thấy ASN hoặc bạn không có quyền truy cập.'));}
  };

  const productUnits=(productId:number|'')=>{
    const product=products.find(x=>x.id===productId);
    if(!product)return [];
    const base={unitId:product.unitId,unitCode:product.unitCode,unitName:product.unitName};
    return [base,...(product.uoms??[])].filter((item,index,array)=>array.findIndex(x=>x.unitId===item.unitId)===index);
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
    if(lines.length===0||lines.some(line=>!line.productId||!line.operationUnitId||Number(line.expectedQuantity)<=0||(purchaseOrderId&&!line.purchaseOrderLineId))){
      setError('Mỗi dòng ASN cần nguồn PO (nếu có), sản phẩm, đơn vị và số lượng lớn hơn 0.');return;
    }
    setCreating(true);
    const action='asn-create:'+code.trim();
    try{
      const payload={
        code,purchaseOrderId:purchaseOrderId||null,
        supplierId:purchaseOrderId?0:supplierId,warehouseId:purchaseOrderId?0:warehouseId,
        expectedArrivalAtUtc:expectedArrival?new Date(expectedArrival).toISOString():null,
        carrierName:carrierName||null,vehiclePlate:vehiclePlate||null,note:note||null,
        lines:lines.map(line=>({
          purchaseOrderLineId:line.purchaseOrderLineId||null,productId:Number(line.productId),
          operationUnitId:Number(line.operationUnitId),expectedQuantity:Number(line.expectedQuantity),
        })),
      };
      const response=await apiClient.post('/api/asns',payload,{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action);
      setSuccess('Đã tạo ASN nháp. ASN chỉ ghi nhận hàng dự kiến, chưa tăng tồn kho.');
      resetForm();await loadList();await viewDetail(response.data.id);
    }catch(err:unknown){setError(permissionError(err,'Không thể tạo ASN. Kiểm tra số lượng PO và dữ liệu tham khảo.'));}
    finally{setCreating(false);}
  };

  const command=async(path:string,action:string,successMessage:string)=>{
    if(!selected?.rowVersion||mutationLock.current.has(action))return;
    mutationLock.current.add(action);setBusy(action);setError('');setSuccess('');
    try{
      const response=await apiClient.post(path,{rowVersion:selected.rowVersion},{headers:idempotencyHeaders(action)});
      completeIdempotentAction(action);setSelected(response.data);setSuccess(successMessage);await loadList();
    }catch(err:unknown){
      if((err as {response?:{status?:number}})?.response?.status===409)setSelected(null);
      setError(permissionError(err,'Không thể chuyển trạng thái ASN. Vui lòng tải lại và thử lại.'));
    }finally{mutationLock.current.delete(action);setBusy('');}
  };

  const eligiblePurchaseOrders=purchaseOrders.filter(x=>['Open','PartiallyReceived'].includes(x.status));

  if(loading)return <p role="status">Đang tải ASN...</p>;

  return <UiPage>
    <UiPageHeader
      eyebrow="Nhập kho"
      title="ASN dự kiến"
      description="Theo dõi lô hàng dự kiến từ nhà cung cấp tới kho trước khi tiếp nhận. Hoàn tất ASN vẫn chưa tạo tồn kho."
    />

    {error&&<div role="alert">{error}</div>}
    {success&&<p role="status" className="ui-success-text">{success}</p>}

    <UiMetricGrid>
      <UiMetric value={items.length} label="ASN trong phạm vi hiện tại"/>
      <UiMetric value={items.filter(x=>x.status==='InTransit').length} label="Đang vận chuyển"/>
      <UiMetric value={items.filter(x=>x.status==='Arrived').length} label="Đã đến kho"/>
      <UiMetric value={items.filter(x=>x.status==='Receiving').length} label="Đang tiếp nhận"/>
    </UiMetricGrid>

    <UiToolbar>
      <UiToolbarField label="Trạng thái">
        <select className="inbound-planning-status-filter" value={statusFilter} onChange={e=>setStatusFilter(e.target.value)}>
          <option value="">Tất cả trạng thái</option>
          {Object.entries(labels).map(([value,label])=><option key={value} value={value}>{label}</option>)}
        </select>
      </UiToolbarField>
      <UiToolbarField label="Tìm trong danh sách">
        <input className="inbound-planning-search" value={search} onChange={e=>setSearch(e.target.value)} placeholder="Mã ASN, PO, nhà cung cấp, kho"/>
      </UiToolbarField>
    </UiToolbar>

    <div className="inbound-planning-grid">
      <UiCard title="Danh sách ASN">
        {filtered.length===0?<UiEmptyState title="Chưa có ASN phù hợp" detail="Thay đổi bộ lọc hoặc tạo ASN mới nếu bạn có quyền."/>:
          <UiTableScroll>
            <table className="inbound-planning-table" aria-label="Danh sách ASN">
              <thead><tr><th>ASN</th><th>PO nguồn</th><th>Nhà cung cấp</th><th>Kho</th><th>Dự kiến đến</th><th className="inbound-planning-numeric">SL cơ sở</th><th>Trạng thái</th></tr></thead>
              <tbody>{filtered.map(item=><tr key={item.id}>
                <td><button type="button" onClick={()=>void viewDetail(item.id)}><strong>{item.code}</strong></button></td>
                <td>{item.purchaseOrderCode||'Không gắn PO'}</td>
                <td>{item.supplierCode}<br/><small>{item.supplierName}</small></td>
                <td>{item.warehouseName}</td>
                <td>{dateTime(item.expectedArrivalAtUtc)}</td>
                <td className="inbound-planning-numeric">{item.baseExpectedQuantity.toLocaleString('vi-VN')}</td>
                <td><UiBadge tone={tone(item.status)}>{labels[item.status]??item.status}</UiBadge></td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>}
      </UiCard>

      <div className="ui-stack">
        {selected&&<UiCard title={'Chi tiết '+selected.code}>
          <div className="inbound-planning-detail-meta">
            <div><strong>Trạng thái</strong><br/><UiBadge tone={tone(selected.status)}>{labels[selected.status]??selected.status}</UiBadge></div>
            <div><strong>PO nguồn</strong><br/>{selected.purchaseOrderCode||'Không gắn PO'}</div>
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
            {canReceive&&selected.status==='Receiving'&&<button className="ui-primary-button" disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/complete','asn-complete:'+selected.id,'ASN đã hoàn tất. Tồn kho vẫn chưa thay đổi cho tới khi Receipt POST.')}>Hoàn tất ASN</button>}
            {canCancel&&['Draft','Confirmed'].includes(selected.status)&&<button disabled={!!busy||!selected.rowVersion} onClick={()=>void command('/api/asns/'+selected.id+'/cancel','asn-cancel:'+selected.id,'ASN đã được hủy.')}>Hủy ASN</button>}
          </div>
          <UiTableScroll>
            <table aria-label={'Dòng ASN '+selected.code}>
              <thead><tr><th>Dòng</th><th>Sản phẩm</th><th className="inbound-planning-numeric">Dự kiến</th><th className="inbound-planning-numeric">SL cơ sở</th></tr></thead>
              <tbody>{selected.lines.map(line=><tr key={line.id}>
                <td>{line.lineNo}</td>
                <td>{line.productCode}<br/><small>{line.productName}</small></td>
                <td className="inbound-planning-numeric">{line.expectedQuantity.toLocaleString('vi-VN')} {line.operationUnitCode}</td>
                <td className="inbound-planning-numeric">{line.baseExpectedQuantity.toLocaleString('vi-VN')} {line.baseUnitCode}</td>
              </tr>)}</tbody>
            </table>
          </UiTableScroll>
        </UiCard>}

        {canCreate&&<UiCard title="Tạo ASN nháp">
          {!canPrepareCreate&&<div className="inbound-planning-notice"><strong>Thiếu quyền dữ liệu tham khảo.</strong><br/>Cần quyền đọc kho, sản phẩm và đối tác để tạo ASN an toàn.</div>}
          {canPrepareCreate&&<form className="inbound-planning-form" onSubmit={submitCreate}>
            <div className="inbound-planning-form-grid">
              <label>Mã ASN<input required value={code} onChange={e=>setCode(e.target.value)}/></label>
              <label>PO nguồn
                <select value={purchaseOrderId} onChange={e=>void choosePurchaseOrder(e.target.value)} disabled={!canReadPo}>
                  <option value="">{canReadPo?'Không gắn PO':'Không có quyền đọc PO'}</option>
                  {eligiblePurchaseOrders.map(x=><option key={x.id} value={x.id}>{x.code} — {x.supplierCode} — {x.warehouseName}</option>)}
                </select>
              </label>
              {purchaseOrderId&&poDetail?<>
                <div><strong>Nhà cung cấp theo PO</strong><br/>{poDetail.supplierCode} — {poDetail.supplierName}</div>
                <div><strong>Kho theo PO</strong><br/>{poDetail.warehouseName}</div>
              </>:<>
                <label>Nhà cung cấp<select required={!purchaseOrderId} value={supplierId} onChange={e=>setSupplierId(e.target.value?Number(e.target.value):'')}><option value="">Chọn nhà cung cấp</option>{suppliers.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>
                <label>Kho nhận<select required={!purchaseOrderId} value={warehouseId} onChange={e=>setWarehouseId(e.target.value?Number(e.target.value):'')}><option value="">Chọn kho</option>{warehouses.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
              </>}
              <label>Dự kiến đến kho<input type="datetime-local" value={expectedArrival} onChange={e=>setExpectedArrival(e.target.value)}/></label>
              <label>Đơn vị vận chuyển<input value={carrierName} onChange={e=>setCarrierName(e.target.value)}/></label>
              <label>Biển số xe<input value={vehiclePlate} onChange={e=>setVehiclePlate(e.target.value.toUpperCase())}/></label>
              <label className="span-2">Ghi chú<textarea rows={2} value={note} onChange={e=>setNote(e.target.value)}/></label>
            </div>

            <div>
              <div className="ui-inline-wrap inbound-planning-summary"><strong>Dòng hàng dự kiến</strong><button type="button" onClick={()=>setLines(current=>[...current,emptyLine()])}>Thêm dòng</button></div>
              <div className="inbound-planning-lines">
                {lines.map((line,index)=><div className="inbound-planning-line compact" key={index}>
                  {purchaseOrderId?<label>Dòng PO<select required value={line.purchaseOrderLineId} onChange={e=>changeLine(index,{purchaseOrderLineId:e.target.value?Number(e.target.value):''})}><option value="">Chọn dòng PO</option>{poDetail?.lines.map(x=><option key={x.id} value={x.id}>{x.externalLineId} — {x.productCode} — còn dự kiến tối đa theo PO</option>)}</select></label>:
                    <label>Sản phẩm<select required value={line.productId} onChange={e=>changeLine(index,{productId:e.target.value?Number(e.target.value):''})}><option value="">Chọn sản phẩm</option>{products.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select></label>}
                  <label>Đơn vị<select required value={line.operationUnitId} onChange={e=>changeLine(index,{operationUnitId:e.target.value?Number(e.target.value):''})}><option value="">Chọn đơn vị</option>{productUnits(line.productId).map(x=><option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}</select></label>
                  <label>Số lượng dự kiến<input required min="0" step="any" type="number" value={line.expectedQuantity} onChange={e=>changeLine(index,{expectedQuantity:e.target.value===''?'':Number(e.target.value)})}/></label>
                  <div><strong>{products.find(x=>x.id===line.productId)?.code||'Chưa chọn'}</strong><br/><small>{products.find(x=>x.id===line.productId)?.name||'Chọn nguồn dòng để xác định sản phẩm'}</small></div>
                  <button type="button" disabled={lines.length===1} onClick={()=>setLines(current=>current.filter((_,i)=>i!==index))}>Xóa</button>
                </div>)}
              </div>
            </div>
            <p className="inbound-planning-helper">Tổng ASN theo từng dòng PO không được vượt số lượng đặt cộng dung sai nhận vượt. ASN không tạo tồn kho.</p>
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
