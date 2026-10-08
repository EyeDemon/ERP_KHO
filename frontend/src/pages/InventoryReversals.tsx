import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type TransactionRow={
  id:number;productId:number;productCode?:string;productName?:string;warehouseId:number;warehouseName?:string;
  transactionType:string;inventoryStatus:string;fromInventoryStatus?:string|null;toInventoryStatus?:string|null;
  lotNumber?:string|null;serialNumber?:string|null;quantity:number;referenceId?:number|null;referenceType?:string|null;
  transactionDate:string;isReversed:boolean;
};
type Page={items:TransactionRow[];totalRecords:number;pageIndex:number;pageSize:number;totalPages:number};
type WarehouseOption={id:number;code:string;name:string};

const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_ALREADY_REVERSED')return r.data.message??'Giao dịch đã được đảo trước đó.';
  if(r?.data?.code==='INV_REVERSAL_UNSUPPORTED')return r.data.message??'Loại giao dịch này phải được đảo tại quy trình nghiệp vụ chuyên biệt.';
  if(r?.data?.code==='INV_REVERSAL_SOURCE_NOT_FOUND')return r.data.message??'Không còn nhóm tồn kho phù hợp để đảo giao dịch.';
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Tồn kho đang bị khóa.';
  if(r?.status===403)return 'Bạn không có quyền tạo giao dịch đảo tồn kho.';
  if(r?.status===409)return r.data?.message??'Trạng thái tồn kho đã thay đổi; vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý giao dịch đảo tồn kho.';
};

const transactionTypeLabel=(value:string)=>({
  Move:'Di chuyển vị trí',
  StatusChange:'Đổi trạng thái',
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

export default function InventoryReversals(){
  const canReverse=usePermission('inventory_reversal.create');
  const [rows,setRows]=useState<TransactionRow[]>([]);
  const [page,setPage]=useState(1);
  const [warehouseId,setWarehouseId]=useState<number|null>(null);
  const [warehouses,setWarehouses]=useState<WarehouseOption[]>([]);
  const [transactionIdInput,setTransactionIdInput]=useState('');
  const [transactionId,setTransactionId]=useState<number|null>(null);
  const [warehouseError,setWarehouseError]=useState('');
  const [totalRecords,setTotalRecords]=useState(0);
  const [totalPages,setTotalPages]=useState(0);
  const [success,setSuccess]=useState('');
  const requestSequence=useRef(0);
  const [selected,setSelected]=useState<TransactionRow|null>(null);
  const [reason,setReason]=useState('');
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const guard=useRef(false);

  useEffect(()=>{
    let active=true;
    void apiClient.get<WarehouseOption[]>('/api/inventory/reversal-warehouses')
      .then(response=>{if(active)setWarehouses(response.data)})
      .catch(()=>{if(active)setWarehouseError('Không thể tải danh sách kho được phân quyền.')});
    return ()=>{active=false};
  },[]);

  const load=useCallback(async(targetPage:number)=>{
    const sequence=++requestSequence.current;
    setLoading(true);setError('');
    try{
      const warehouseFilter=warehouseId===null?'':'&warehouseId='+warehouseId;
      const transactionFilter=transactionId===null?'':'&transactionId='+transactionId;
      const response=await apiClient.get<Page>('/api/inventory/reversal-candidates?page='+targetPage+'&pageSize=20'+warehouseFilter+transactionFilter);
      if(sequence!==requestSequence.current)return;
      setRows(response.data.items);
      setTotalRecords(response.data.totalRecords);
      setTotalPages(response.data.totalPages);
    }catch(e){
      if(sequence!==requestSequence.current)return;
      setRows([]);setTotalRecords(0);setTotalPages(0);setError(errorMessage(e));
    }finally{if(sequence===requestSequence.current)setLoading(false)}
  },[warehouseId,transactionId]);
  useEffect(()=>{void load(page)},[load,page]);

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||selected.isReversed||!canReverse||guard.current||!reason.trim())return;
    const key='inventory-reversal-'+selected.id;
    guard.current=true;setBusy(true);setError('');setSuccess('');
    try{
      await apiClient.post('/api/inventory/reversals',{
        originalTransactionId:selected.id,
        reason:reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);setReason('');
      setSuccess('Đã ghi nhận giao dịch đảo thành công.');
      await load(page);
    }catch(e){
      const message=errorMessage(e);
      const code=(e as {response?:{data?:{code?:string}}})?.response?.data?.code;
      if(code==='INV_ALREADY_REVERSED'){
        setSelected(null);
        await load(page);
      }
      setError(message);
    }
    finally{guard.current=false;setBusy(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Đảo giao dịch tồn kho"
      description="Không sửa hoặc xóa sổ cái đã ghi. Thao tác đảo tạo một giao dịch hiệu chỉnh mới và một dấu mốc liên kết về giao dịch gốc."/>
    {error&&<p role="alert">{error}</p>}
    {warehouseError&&<p role="alert">{warehouseError}</p>}
    {success&&<p role="status">{success}</p>}
    <UiCard title="Lịch sử giao dịch hỗ trợ đảo hiệu chỉnh">
      <p className="ui-muted-text">Chỉ hỗ trợ di chuyển vị trí nội bộ và đổi trạng thái tồn kho. Trạng thái “Chưa đảo” không bảo đảm đủ điều kiện thực hiện; hệ thống kiểm tra khóa, lượng tồn và các ràng buộc ngay khi xác nhận.</p>
      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Lọc theo kho" value={warehouseId??''}
            onChange={e=>{setSelected(null);setPage(1);setWarehouseId(e.target.value?Number(e.target.value):null)}}>
            <option value="">Tất cả kho được phân quyền</option>
            {warehouses.map(x=><option key={x.id} value={x.id}>{x.code} – {x.name}</option>)}
          </select>
        </UiToolbarField>
        <form onSubmit={e=>{
          e.preventDefault();
          const searched=transactionIdInput.trim();
          const parsed=Number(searched);
          if(searched!==''&&(!Number.isSafeInteger(parsed)||parsed<=0)){
            setError('ID giao dịch phải là số nguyên dương.');
            return;
          }
          setError('');
          setSelected(null);
          setPage(1);
          setTransactionId(searched===''?null:parsed);
        }} className="ui-inline-actions">
          <UiToolbarField label="ID giao dịch">
            <input aria-label="Tìm theo ID giao dịch" type="number" min="1" step="1"
              value={transactionIdInput} onChange={e=>setTransactionIdInput(e.target.value)}
              placeholder="Ví dụ: 12345" />
          </UiToolbarField>
          <button type="submit" disabled={loading||busy}>Tìm giao dịch</button>
        </form>
        <button type="button" disabled={loading||busy} onClick={()=>void load(page)}>Tải lại danh sách</button>
      </UiToolbar>
      <p role="status" className="ui-muted-text">{loading?'Đang tải giao dịch...':('Trang '+page+' / '+Math.max(1,totalPages)+' • '+totalRecords+' giao dịch')}</p>
      {loading?<p role="status">Đang tải giao dịch...</p>:<UiTableScroll>
        <table aria-label="Danh sách giao dịch có thể đảo">
          <thead><tr><th>Giao dịch</th><th>Sản phẩm</th><th>Kho</th><th>Chi tiết theo dõi</th><th>Số lượng</th><th>Thời gian</th><th>Trạng thái đảo</th>{canReverse&&<th>Thao tác</th>}</tr></thead>
          <tbody>{rows.length===0?<tr><td colSpan={canReverse?8:7} className="ui-empty-cell">Không có giao dịch phù hợp.</td></tr>:
            rows.map(x=>{
              const reversed=x.isReversed;
              return <tr key={x.id}>
                <td><strong>{transactionTypeLabel(x.transactionType)}</strong><br/><small>#{x.id}</small></td>
                <td>{x.productCode??('#'+x.productId)}<br/><small>{x.productName??''}</small></td>
                <td>{x.warehouseName??('#'+x.warehouseId)}</td>
                <td>{x.lotNumber??'không có lô'} / {x.serialNumber??'không có sê-ri'}<br/><small>{x.fromInventoryStatus&&x.toInventoryStatus?(inventoryStatusLabel(x.fromInventoryStatus)+' → '+inventoryStatusLabel(x.toInventoryStatus)):inventoryStatusLabel(x.inventoryStatus)}</small></td>
                <td>{x.quantity}</td>
                <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
                <td><UiBadge tone={reversed?'success':'warning'}>{reversed?'Đã đảo':'Chưa đảo'}</UiBadge></td>
                {canReverse&&<td><button type="button" disabled={reversed} onClick={()=>{setSelected(x);setReason('')}}>Đảo giao dịch</button></td>}
              </tr>
            })}
          </tbody>
        </table>
      </UiTableScroll>}
      <div className="ui-inline-actions" aria-label="Phân trang giao dịch đảo">
        <button type="button" disabled={loading||busy||page<=1} onClick={()=>{setSelected(null);setPage(p=>Math.max(1,p-1))}}>Trang trước</button>
        <button type="button" disabled={loading||busy||page>=totalPages} onClick={()=>{setSelected(null);setPage(p=>p+1)}}>Trang sau</button>
      </div>
    </UiCard>

    {selected&&canReverse&&<UiCard title={'Đảo giao dịch #'+selected.id}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">{transactionTypeLabel(selected.transactionType)} • {selected.productCode??selected.productId} • số lượng {selected.quantity}. Thao tác hiệu chỉnh vẫn kiểm tra lại khóa tồn, lượng đã giữ, sức chứa và chính sách trạng thái trong cùng giao dịch Serializable.</p>
        <input aria-label="Lý do đảo giao dịch tồn kho" value={reason} onChange={e=>setReason(e.target.value)} placeholder="Lý do / bằng chứng bắt buộc (tối đa 400 ký tự)" maxLength={400} required/>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!reason.trim()}>Xác nhận đảo giao dịch</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </UiPage>;
}
