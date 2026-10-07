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
  if(r?.data?.code==='INV_ALREADY_REVERSED')return r.data.message??'Transaction đã được reversal.';
  if(r?.data?.code==='INV_REVERSAL_UNSUPPORTED')return r.data.message??'Loại transaction này phải reversal ở workflow chuyên biệt.';
  if(r?.data?.code==='INV_REVERSAL_SOURCE_NOT_FOUND')return r.data.message??'Không còn bucket phù hợp để reversal.';
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Inventory đang bị khóa.';
  if(r?.status===403)return 'Bạn không có quyền tạo inventory reversal.';
  if(r?.status===409)return r.data?.message??'Inventory state đã thay đổi; vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý inventory reversal.';
};

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
    <UiPageHeader eyebrow="Inventory Control" title="Inventory Reversal"
      description="Không sửa/xóa ledger đã post. Reversal tạo corrective transaction mới và một marker liên kết về transaction gốc."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Transaction có thể corrective reversal">
      <p className="ui-muted-text">Foundation chỉ hỗ trợ Internal Move và Inventory Status Change. Shipment, Transfer và document-bound transaction phải đảo tại workflow sở hữu.</p>
      {loading?<p role="status">Đang tải transaction...</p>:<UiTableScroll>
        <table aria-label="Inventory reversal candidates">
          <thead><tr><th>Transaction</th><th>Sản phẩm</th><th>Warehouse</th><th>Dimension</th><th>Số lượng</th><th>Thời gian</th><th>Trạng thái reversal</th>{canReverse&&<th>Thao tác</th>}</tr></thead>
          <tbody>{rows.length===0?<tr><td colSpan={canReverse?8:7} className="ui-empty-cell">Không có transaction phù hợp.</td></tr>:
            rows.map(x=>{
              const reversed=reversedIds.has(x.id);
              return <tr key={x.id}>
                <td><strong>{x.transactionType}</strong><br/><small>#{x.id}</small></td>
                <td>{x.productCode??('#'+x.productId)}<br/><small>{x.productName??''}</small></td>
                <td>{x.warehouseName??('#'+x.warehouseId)}</td>
                <td>{x.lotNumber??'không lot'} / {x.serialNumber??'không serial'}<br/><small>{x.fromInventoryStatus&&x.toInventoryStatus?(x.fromInventoryStatus+' → '+x.toInventoryStatus):x.inventoryStatus}</small></td>
                <td>{x.quantity}</td>
                <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
                <td><UiBadge tone={reversed?'success':'warning'}>{reversed?'Đã reversal':'Chưa reversal'}</UiBadge></td>
                {canReverse&&<td><button type="button" disabled={reversed} onClick={()=>{setSelected(x);setReason('')}}>Reversal</button></td>}
              </tr>
            })}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canReverse&&<UiCard title={'Reversal transaction #'+selected.id}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">{selected.transactionType} • {selected.productCode??selected.productId} • quantity {selected.quantity}. Corrective action vẫn re-check lock, reserved quantity, capacity/status policy trong Serializable transaction.</p>
        <input aria-label="Lý do inventory reversal" value={reason} onChange={e=>setReason(e.target.value)} placeholder="Lý do / evidence bắt buộc" required/>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!reason.trim()}>Xác nhận Reversal</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}
  </UiPage>;
}
