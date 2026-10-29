import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiDrawer, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type TransactionRow={
  id:number;productId:number;productCode?:string;productName?:string;warehouseId:number;warehouseName?:string;
  transactionType:string;inventoryStatus:string;fromInventoryStatus?:string|null;toInventoryStatus?:string|null;
  lotNumber?:string|null;serialNumber?:string|null;quantity:number;referenceId?:number|null;referenceType?:string|null;
  transactionDate:string;isReversed:boolean;
};
type Page={items:TransactionRow[];totalRecords:number;pageIndex:number;pageSize:number;totalPages:number};
type WarehouseOption={id:number;code:string;name:string};
type ReversalReasonOption={code:string;name:string;transactionType?:string|null};
type ReversalChainIntegrityResult={
  verificationId:string;warehouseId?:number|null;duplicateReversalBlocked:boolean;appendOnlyLedgerStatus:string;verifiedAtUtc:string;
};

const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_ALREADY_REVERSED')return r.data.message??'Giao dịch đã được đảo trước đó.';
  if(r?.data?.code==='INV_REVERSAL_UNSUPPORTED')return r.data.message??'Loại giao dịch này phải được đảo tại quy trình nghiệp vụ chuyên biệt.';
  if(r?.data?.code==='INV_REVERSAL_SOURCE_NOT_FOUND')return r.data.message??'Không còn nhóm tồn kho phù hợp để đảo giao dịch.';
  if(r?.data?.code==='INV_REVERSAL_INSUFFICIENT_STOCK')return r.data.message??'Tồn hiện tại không đủ để đảo giao dịch; hãy kiểm tra các giao dịch phát sinh sau.';
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Tồn kho đang bị khóa.';
  if(r?.status===403)return 'Bạn không có quyền tạo giao dịch đảo tồn kho.';
  if(r?.status===409)return r.data?.message??'Trạng thái tồn kho đã thay đổi; vui lòng tải lại.';
  return r?.data?.message??'Không thể xử lý giao dịch đảo tồn kho.';
};

const transactionTypeLabel=(value:string)=>({
  Import:'Nhập kho',
  Export:'Xuất kho',
  Move:'Di chuyển vị trí',
  Ship:'Xuất giao',
  TransferOut:'Xuất điều chuyển',
  TransferIn:'Nhập điều chuyển',
  AdjustmentIncrease:'Điều chỉnh tăng',
  AdjustmentDecrease:'Điều chỉnh giảm',
  StatusChange:'Đổi trạng thái',
  Reversal:'Đảo bút toán',
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
  const canTrace=usePermission('inventory_traceability.read');
  const [rows,setRows]=useState<TransactionRow[]>([]);
  const [page,setPage]=useState(1);
  const [warehouseId,setWarehouseId]=useState<number|null>(null);
  const [warehouses,setWarehouses]=useState<WarehouseOption[]>([]);
  const [reversalReasons,setReversalReasons]=useState<ReversalReasonOption[]>([]);
  const [reasonsError,setReasonsError]=useState('');
  const [transactionIdInput,setTransactionIdInput]=useState('');
  const [transactionId,setTransactionId]=useState<number|null>(null);
  const [productCodeInput,setProductCodeInput]=useState('');
  const [productCode,setProductCode]=useState('');
  const [reversalState,setReversalState]=useState<'all'|'pending'|'reversed'>('all');
  const [warehouseError,setWarehouseError]=useState('');
  const [totalRecords,setTotalRecords]=useState(0);
  const [totalPages,setTotalPages]=useState(0);
  const [success,setSuccess]=useState('');
  const requestSequence=useRef(0);
  const [selected,setSelected]=useState<TransactionRow|null>(null);
  const [reason,setReason]=useState('');
  const [reasonCode,setReasonCode]=useState('');
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [chainIntegrityResult,setChainIntegrityResult]=useState<ReversalChainIntegrityResult|null>(null);
  const [chainIntegrityBusy,setChainIntegrityBusy]=useState(false);
  const guard=useRef(false);

  const verifyReversalChainIntegrity=async()=>{
    if(chainIntegrityBusy)return;
    setChainIntegrityBusy(true);
    setError('');
    try{
      const response=await apiClient.post<ReversalChainIntegrityResult>('/api/inventory/reversals/verify-chain-integrity',{warehouseId});
      if(response?.data?.verificationId){
        setChainIntegrityResult(response.data);
      }else{
        setChainIntegrityResult(null);
        setError('Phản hồi xác minh chuỗi đảo giao dịch không hợp lệ từ máy chủ.');
      }
    }catch(e){
      setChainIntegrityResult(null);
      setError(errorMessage(e));
    }finally{
      setChainIntegrityBusy(false);
    }
  };

  useEffect(()=>{
    let active=true;
    void apiClient.get<ReversalReasonOption[]>('/api/inventory/reversal-reasons')
      .then(response=>{if(active)setReversalReasons(response.data)})
      .catch(()=>{if(active)setReasonsError('Không thể tải danh mục mã lý do đảo. Không thể xác nhận giao dịch khi chưa có mã hợp lệ.')});
    return ()=>{active=false};
  },[]);

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
      const reversalFilter=reversalState==='all'?'':'&isReversed='+(reversalState==='reversed');
      const productFilter=productCode?'&productCode='+encodeURIComponent(productCode):'';
      const response=await apiClient.get<Page>('/api/inventory/reversal-candidates?page='+targetPage+'&pageSize=20'+warehouseFilter+transactionFilter+reversalFilter+productFilter);
      if(sequence!==requestSequence.current)return;
      setRows(response.data.items);
      setTotalRecords(response.data.totalRecords);
      setTotalPages(response.data.totalPages);
    }catch(e){
      if(sequence!==requestSequence.current)return;
      setRows([]);setTotalRecords(0);setTotalPages(0);setError(errorMessage(e));
    }finally{if(sequence===requestSequence.current)setLoading(false)}
  },[warehouseId,transactionId,reversalState,productCode]);
  useEffect(()=>{void load(page)},[load,page]);

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||selected.isReversed||!canReverse||guard.current||!reason.trim()||
      !reversalReasons.some(option=>option.code===reasonCode &&
        (!option.transactionType||option.transactionType===selected.transactionType)))return;
    // Bind retries to both the original transaction and its normalized reason.
    // A changed reason is a different command and must not reuse its idempotency key.
    const key='inventory-reversal-'+selected.id+':'+reasonCode+':'+reason.trim();
    guard.current=true;setBusy(true);setError('');setSuccess('');
    try{
      await apiClient.post('/api/inventory/reversals',{
        originalTransactionId:selected.id,
        reasonCode,
        reason:reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);setReason('');setReasonCode('');
      setSuccess('Đã ghi nhận giao dịch đảo thành công.');
      // The last filtered item can disappear after reversal. Return to the
      // first valid server page, preserving the active warehouse/SKU filters.
      if(page===1)await load(1);
      else setPage(1);
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

  const reversibleCount=rows.filter(x=>!x.isReversed).length;
  const alreadyReversedCount=rows.filter(x=>x.isReversed).length;
  const moveTransactionCount=rows.filter(x=>x.transactionType==='Move').length;
  const statusChangeTransactionCount=rows.filter(x=>x.transactionType==='StatusChange').length;

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Đảo giao dịch tồn kho"
      description="Không sửa hoặc xóa sổ cái đã ghi. Thao tác đảo tạo một giao dịch hiệu chỉnh mới và một dấu mốc liên kết về giao dịch gốc."/>
    {error&&<p role="alert">{error}</p>}
    {warehouseError&&<p role="alert">{warehouseError}</p>}
    {reasonsError&&<p role="alert">{reasonsError}</p>}
    {success&&<p role="status">{success}</p>}
    <UiCard title="Tổng quan Giao dịch Đủ điều kiện Đảo & Kiểm soát Bất biến Sổ cái">
      <UiMetricGrid>
        <UiMetric value={totalRecords} label="Tổng giao dịch khớp lọc" />
        <UiMetric value={reversibleCount} label="Khả dụng đảo trên trang" />
        <UiMetric value={alreadyReversedCount} label="Đã tạo bút toán đảo trên trang" />
        <UiMetric value={`${moveTransactionCount} / ${statusChangeTransactionCount}`} label="Chuyển vị trí / Đổi trạng thái" />
      </UiMetricGrid>
      <div className="ui-inline-actions" style={{marginTop:8}}>
        <button type="button" disabled={chainIntegrityBusy} onClick={()=>void verifyReversalChainIntegrity()}>
          Xác minh Toàn vẹn Chuỗi Đảo giao dịch & Chống Đảo lặp
        </button>
        {chainIntegrityResult&&(
          <span>
            Mã xác minh: <strong>{chainIntegrityResult.verificationId}</strong> • Sổ cái chỉ ghi thêm: <strong>{chainIntegrityResult.appendOnlyLedgerStatus==='IMMUTABLE_VERIFIED'?'ĐÃ XÁC THỰC BẤT BIẾN':chainIntegrityResult.appendOnlyLedgerStatus.replace(/IMMUTABLE_VERIFIED/gi,'ĐÃ XÁC THỰC BẤT BIẾN')}</strong> • Chống đảo lặp: <strong>{chainIntegrityResult.duplicateReversalBlocked?'ĐÃ KHÓA':'MỞ'}</strong>
          </span>
        )}
      </div>
    </UiCard>
    <UiCard title="Lịch sử giao dịch hỗ trợ đảo hiệu chỉnh">
      <p className="ui-muted-text">Chỉ hỗ trợ di chuyển vị trí nội bộ và đổi trạng thái tồn kho. Trạng thái “Chưa đảo” không bảo đảm đủ điều kiện thực hiện; hệ thống kiểm tra khóa, lượng tồn và các ràng buộc ngay khi xác nhận.</p>
      <UiToolbar>
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
          setProductCode(productCodeInput.trim());
        }} className="ui-inline-actions" style={{display:'flex',flexWrap:'wrap',gap:10,alignItems:'end',width:'100%'}}>
          <UiToolbarField label="Kho">
            <select aria-label="Lọc theo kho" disabled={busy||loading} value={warehouseId??''}
              onChange={e=>{setSelected(null);setPage(1);setWarehouseId(e.target.value?Number(e.target.value):null)}}>
              <option value="">Tất cả kho được phân quyền</option>
              {warehouses.map(x=><option key={x.id} value={x.id}>{x.code} – {x.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Trạng thái đảo">
            <select aria-label="Lọc theo trạng thái đảo" disabled={busy||loading} value={reversalState}
              onChange={e=>{
                setSelected(null);setPage(1);
                setReversalState(e.target.value as 'all'|'pending'|'reversed');
              }}>
              <option value="all">Tất cả</option>
              <option value="pending">Chưa đảo</option>
              <option value="reversed">Đã đảo</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Mã sản phẩm">
            <input aria-label="Tìm theo mã sản phẩm" disabled={busy||loading} value={productCodeInput} maxLength={64}
              onChange={e=>setProductCodeInput(e.target.value)} placeholder="Nhập mã sản phẩm..." />
          </UiToolbarField>
          <UiToolbarField label="ID giao dịch">
            <input aria-label="Tìm theo ID giao dịch" disabled={busy||loading} type="number" min="1" step="1"
              value={transactionIdInput} onChange={e=>setTransactionIdInput(e.target.value)}
              placeholder="Ví dụ: 12345" />
          </UiToolbarField>
          <button type="submit" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={loading||busy}>Tìm giao dịch</button>
          <button type="button" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={loading||busy} onClick={()=>void load(page)}>Tải lại danh sách</button>
        </form>
      </UiToolbar>
      {!loading&&<p role="status" className="ui-muted-text">{'Trang '+page+' / '+Math.max(1,totalPages)+' • '+totalRecords+' giao dịch'}</p>}
      {loading && rows.length === 0 ? <p role="status">Đang tải giao dịch...</p> : <UiTableScroll>
        <table className="ui-table-compact" aria-label="Danh sách giao dịch có thể đảo">
          <thead><tr><th>Giao dịch</th><th>Sản phẩm</th><th>Kho</th><th>Chi tiết theo dõi</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Số lượng</th><th>Thời gian</th><th style={{whiteSpace:'nowrap'}}>Trạng thái đảo</th>{canReverse&&<th style={{whiteSpace:'nowrap'}}>Thao tác</th>}</tr></thead>
          <tbody>{rows.length===0?<tr><td colSpan={canReverse?8:7} className="ui-empty-cell">Không có giao dịch phù hợp.</td></tr>:
            rows.map(x=>{
              const reversed=x.isReversed;
              return <tr key={x.id}>
                <td style={{whiteSpace:'nowrap'}}><strong>{transactionTypeLabel(x.transactionType)}</strong><br/><small>#{x.id}</small></td>
                <td style={{ whiteSpace: 'nowrap' }}>{x.productCode??('#'+x.productId)}<br/><small>{x.productName??''}</small></td>
                <td>{x.warehouseName??('#'+x.warehouseId)}</td>
                <td>{x.lotNumber??'không có lô'} / {x.serialNumber??'không có sê-ri'}<br/><small>{x.fromInventoryStatus&&x.toInventoryStatus?(inventoryStatusLabel(x.fromInventoryStatus)+' → '+inventoryStatusLabel(x.toInventoryStatus)):inventoryStatusLabel(x.inventoryStatus)}</small></td>
                <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{x.quantity}</td>
                <td>{new Date(x.transactionDate).toLocaleString('vi-VN')}</td>
                <td style={{whiteSpace:'nowrap'}}><UiBadge tone={reversed?'success':'warning'}>{reversed?'Đã đảo':'Chưa đảo'}</UiBadge>
                  {reversed&&canTrace&&<><br/><Link to={'/inventory-traceability?referenceType=InventoryReversal&referenceId='+x.id}>Truy vết chuỗi đảo</Link></>}
                </td>
                {canReverse&&<td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} disabled={reversed} onClick={()=>{setSelected(x);setReason('');setReasonCode('')}}>Đảo giao dịch</button></td>}
              </tr>
            })}
          </tbody>
        </table>
      </UiTableScroll>}
      <nav className="ui-inline-actions" aria-label="Phân trang giao dịch đảo">
        <button type="button" disabled={loading||busy||page<=1} onClick={()=>{setSelected(null);setPage(p=>Math.max(1,p-1))}}>Trang trước</button>
        <button type="button" disabled={loading||busy||page>=totalPages} onClick={()=>{setSelected(null);setPage(p=>p+1)}}>Trang sau</button>
      </nav>
    </UiCard>

    {selected&&canReverse&&<UiDrawer open={Boolean(selected&&canReverse)} title={'Đảo giao dịch #'+selected.id} titleId="reversal-drawer-title" busy={busy} onClose={()=>setSelected(null)}>
      <form onSubmit={submit} className="ui-form-grid">
        <p className="ui-muted-text">{transactionTypeLabel(selected.transactionType)} • {selected.productCode??selected.productId} • số lượng {selected.quantity}. Thao tác hiệu chỉnh vẫn kiểm tra lại khóa tồn, lượng đã giữ, sức chứa và chính sách trạng thái trong cùng giao dịch Serializable.</p>
        <UiToolbarField label="Mã lý do đảo (bắt buộc)">
          <select aria-label="Mã lý do đảo giao dịch" value={reasonCode}
            required disabled={busy||reversalReasons.length===0}
            onChange={e=>setReasonCode(e.target.value)}>
            <option value="">Chọn mã lý do</option>
            {reversalReasons.filter(option=>!option.transactionType||
              option.transactionType===selected.transactionType)
              .map(option=><option key={option.code} value={option.code}>
                {option.name} ({option.code})
              </option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Lý do đảo giao dịch">
          <input aria-label="Lý do đảo giao dịch tồn kho" value={reason} onChange={e=>setReason(e.target.value)} placeholder="Lý do / bằng chứng bắt buộc (tối đa 400 ký tự)" maxLength={400} required/>
        </UiToolbarField>
        <div className="ui-inline-actions">
          <button type="submit" disabled={busy||!reason.trim()||!reasonCode||reversalReasons.length===0}>Xác nhận đảo giao dịch</button>
          <button type="button" disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiDrawer>}
  </UiPage>;
}
