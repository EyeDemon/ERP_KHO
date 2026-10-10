import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type TransactionRow={
  id:number;productId:number;productCode?:string;productName?:string;warehouseId:number;warehouseName?:string;
  transactionType:string;inventoryStatus:string;fromInventoryStatus?:string|null;toInventoryStatus?:string|null;
  lotNumber?:string|null;serialNumber?:string|null;quantity:number;referenceId?:number|null;referenceType?:string|null;
  transactionDate:string;createdByName?:string|null;note?:string|null;
};
type Page={items:TransactionRow[]};

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
  Available:'Khả dụng',
  QcHold:'Chờ kiểm tra chất lượng',
  Quarantine:'Cách ly',
  Damaged:'Hư hỏng',
  Rejected:'Từ chối',
  Blocked:'Bị chặn',
  Expired:'Hết hạn',
  RecallBlocked:'Khóa thu hồi',
}[value]??value);

export default function InventoryReversals(){
  const canReverse=usePermission('inventory_reversal.create');
  const [rows,setRows]=useState<TransactionRow[]>([]);
  const [reversedIds,setReversedIds]=useState<Set<number>>(new Set());
  const [selected,setSelected]=useState<TransactionRow|null>(null);
  const [reason,setReason]=useState('');
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const guard=useRef(false);

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const params='page=1&pageSize=100';
      const [moves,statusChanges,reversals]=await Promise.all([
        apiClient.get('/api/InventoryTransactions?transactionType=Move&'+params),
        apiClient.get('/api/InventoryTransactions?transactionType=StatusChange&'+params),
        apiClient.get('/api/InventoryTransactions?transactionType=Reversal&'+params),
      ]);
      const candidates=[...(moves.data as Page).items,...(statusChanges.data as Page).items]
        .sort((a,b)=>new Date(b.transactionDate).getTime()-new Date(a.transactionDate).getTime());
      const reversed=new Set<number>(
        (reversals.data as Page).items
          .filter(x=>x.referenceType==='InventoryReversal'&&typeof x.referenceId==='number')
          .map(x=>x.referenceId as number)
      );
      setRows(candidates);setReversedIds(reversed);
    }catch(e){setRows([]);setReversedIds(new Set());setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  useEffect(()=>{void load()},[]);

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||!canReverse||guard.current||!reason.trim())return;
    const key='inventory-reversal-'+selected.id;
    guard.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/reversals',{
        originalTransactionId:selected.id,
        reason:reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);setReason('');
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Đảo giao dịch tồn kho"
      description="Không sửa hoặc xóa sổ cái đã ghi. Thao tác đảo tạo một giao dịch hiệu chỉnh mới và một dấu mốc liên kết về giao dịch gốc."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Giao dịch có thể đảo hiệu chỉnh">
      <p className="ui-muted-text">Phạm vi hiện tại chỉ hỗ trợ di chuyển vị trí nội bộ và đổi trạng thái tồn kho. Giao hàng, điều chuyển và giao dịch gắn với chứng từ phải được đảo tại quy trình nghiệp vụ sở hữu.</p>
      {loading?<p role="status">Đang tải giao dịch...</p>:<UiTableScroll>
        <table aria-label="Danh sách giao dịch có thể đảo">
          <thead><tr><th>Giao dịch</th><th>Sản phẩm</th><th>Kho</th><th>Chi tiết theo dõi</th><th>Số lượng</th><th>Thời gian</th><th>Trạng thái đảo</th>{canReverse&&<th>Thao tác</th>}</tr></thead>
          <tbody>{rows.length===0?<tr><td colSpan={canReverse?8:7} className="ui-empty-cell">Không có giao dịch phù hợp.</td></tr>:
            rows.map(x=>{
              const reversed=reversedIds.has(x.id);
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
    </UiCard>

    {selected&&canReverse&&<UiCard title={'Đảo giao dịch #'+selected.id}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">{selected.transactionType} • {selected.productCode??selected.productId} • số lượng {selected.quantity}. Thao tác hiệu chỉnh vẫn kiểm tra lại khóa tồn, lượng đã giữ, sức chứa và chính sách trạng thái trong cùng giao dịch Serializable.</p>
        <input aria-label="Lý do đảo giao dịch tồn kho" value={reason} onChange={e=>setReason(e.target.value)} placeholder="Lý do / bằng chứng bắt buộc" required/>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!reason.trim()}>Xác nhận đảo giao dịch</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </UiPage>;
}
