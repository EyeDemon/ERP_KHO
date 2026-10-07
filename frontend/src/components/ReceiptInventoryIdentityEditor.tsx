import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiTableScroll } from '../ui/ProductionUi';

export type ReceiptTrackingLine = {
  id:number; productId:number; productCode:string; productName:string;
  trackingType:'None'|'Lot'|'Serial'; expiryControl:boolean; shelfLifeDays?:number|null;
  baseAcceptedQuantity:number; baseDamagedQuantity:number; baseRejectedQuantity:number; baseUnitCode:string;
};
type IdentityRow = {
  id?:number; lineId:number; productId:number; targetStatus:'AVAILABLE'|'DAMAGED'|'REJECTED';
  baseQuantity:number|string; lotNumber?:string; manufactureDate?:string; expiryDate?:string; serialNumber?:string;
};

const messageOf=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='LOT_REQUIRED'||code==='SERIAL_REQUIRED'||code==='EXPIRY_REQUIRED')return response?.data?.message??'Chưa khai báo đủ Lot/Serial/Expiry.';
  if(code==='SERIAL_QUANTITY_INVALID')return 'Mỗi serial phải có BaseQuantity = 1.';
  if(code==='SERIAL_DUPLICATE'||code==='SERIAL_ALREADY_ON_HAND')return response?.data?.message??'Serial bị trùng.';
  if(code==='LOT_EXPIRED')return response?.data?.message??'Lot đã hết hạn.';
  if(code==='EXPIRY_BEFORE_MANUFACTURE')return 'Expiry không được trước manufacture date.';
  if(code==='LOT_ALREADY_EXISTS')return response?.data?.message??'Lot đã tồn tại với metadata khác.';
  if(response?.status===403)return 'Bạn không có quyền cấu hình Lot/Serial cho phiếu nhập.';
  if(response?.status===409)return response.data?.message??'Dữ liệu Lot/Serial đã thay đổi.';
  return response?.data?.message??'Không thể xử lý Lot/Serial/Expiry.';
};

const requiredFor=(line:ReceiptTrackingLine,status:IdentityRow['targetStatus'])=>
  status==='AVAILABLE'?line.baseAcceptedQuantity:status==='DAMAGED'?line.baseDamagedQuantity:line.baseRejectedQuantity;

export default function ReceiptInventoryIdentityEditor({
  receiptId,status,lines,canUpdate,
}:{receiptId:number;status:string;lines:ReceiptTrackingLine[];canUpdate:boolean}){
  const tracked=lines.filter(x=>x.trackingType!=='None');
  const [rows,setRows]=useState<IdentityRow[]>([]);
  const [loading,setLoading]=useState(false);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const lock=useRef(false);
  const editable=canUpdate&&['Received','QcCompleted'].includes(status);

  useEffect(()=>{
    let active=true;
    if(tracked.length===0){setRows([]);return()=>{active=false}};
    setLoading(true);setError('');
    apiClient.get(`/api/importreceipts/${receiptId}/inventory-identities`)
      .then(response=>{
        if(!active)return;
        setRows((response.data as Array<Record<string,unknown>>).map(item=>({
          id:Number(item.id),
          lineId:Number(item.lineId),
          productId:Number(item.productId),
          targetStatus:String(item.targetStatus).toUpperCase() as IdentityRow['targetStatus'],
          baseQuantity:Number(item.baseQuantity),
          lotNumber:item.lotNumber?String(item.lotNumber):'',
          manufactureDate:item.manufactureDate?String(item.manufactureDate).slice(0,10):'',
          expiryDate:item.expiryDate?String(item.expiryDate).slice(0,10):'',
          serialNumber:item.serialNumber?String(item.serialNumber):'',
        })));
      })
      .catch(e=>{if(active){setRows([]);setError(messageOf(e))}})
      .finally(()=>{if(active)setLoading(false)});
    return()=>{active=false};
  },[receiptId]); // tracked line definitions are immutable for an opened receipt

  if(tracked.length===0)return null;

  const lineFor=(lineId:number)=>tracked.find(x=>x.id===lineId)!;
  const allocated=(lineId:number,targetStatus:IdentityRow['targetStatus'])=>rows
    .filter(x=>x.lineId===lineId&&x.targetStatus===targetStatus)
    .reduce((sum,x)=>sum+Number(x.baseQuantity||0),0);

  const addRow=(line:ReceiptTrackingLine)=>{
    const statuses:IdentityRow['targetStatus'][]=['AVAILABLE','DAMAGED','REJECTED'];
    const targetStatus=statuses.find(s=>allocated(line.id,s)<requiredFor(line,s))??'AVAILABLE';
    const remaining=Math.max(0,requiredFor(line,targetStatus)-allocated(line.id,targetStatus));
    setRows(current=>[...current,{
      lineId:line.id,productId:line.productId,targetStatus,
      baseQuantity:line.trackingType==='Serial'?1:(remaining||''),
      lotNumber:'',manufactureDate:'',expiryDate:'',serialNumber:'',
    }]);
  };

  const update=(index:number,next:Partial<IdentityRow>)=>setRows(current=>current.map((row,i)=>i===index?{...row,...next}:row));
  const remove=(index:number)=>setRows(current=>current.filter((_,i)=>i!==index));

  const save=async()=>{
    if(!editable||lock.current)return;
    lock.current=true;setBusy(true);setError('');
    const key=`receipt-inventory-identity-${receiptId}`;
    try{
      const response=await apiClient.post(`/api/importreceipts/${receiptId}/inventory-identities`,{
        lines:rows.map(row=>({
          lineId:row.lineId,
          targetStatus:row.targetStatus,
          baseQuantity:Number(row.baseQuantity),
          lotNumber:row.lotNumber?.trim()||null,
          manufactureDate:row.manufactureDate||null,
          expiryDate:row.expiryDate||null,
          serialNumber:row.serialNumber?.trim()||null,
        }))
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setRows((response.data as Array<Record<string,unknown>>).map(item=>({
        id:Number(item.id),lineId:Number(item.lineId),productId:Number(item.productId),
        targetStatus:String(item.targetStatus).toUpperCase() as IdentityRow['targetStatus'],
        baseQuantity:Number(item.baseQuantity),
        lotNumber:item.lotNumber?String(item.lotNumber):'',
        manufactureDate:item.manufactureDate?String(item.manufactureDate).slice(0,10):'',
        expiryDate:item.expiryDate?String(item.expiryDate).slice(0,10):'',
        serialNumber:item.serialNumber?String(item.serialNumber):'',
      })));
    }catch(e){setError(messageOf(e))}
    finally{lock.current=false;setBusy(false)}
  };

  return <UiCard title="Lot / Serial / Expiry trước Post">
    <p className="ui-muted-text">
      Tổng identity theo AVAILABLE / DAMAGED / REJECTED phải khớp Base UOM của từng dòng. Serial luôn 1 đơn vị.
      Post receipt là inventory boundary duy nhất.
    </p>
    {error&&<p role="alert">{error}</p>}
    {loading?<p role="status">Đang tải Lot/Serial...</p>:<>
      {tracked.map(line=><fieldset key={line.id} className="ui-stack">
        <legend>{line.productCode} — {line.productName} <UiBadge>{line.trackingType}</UiBadge></legend>
        <div className="ui-inline-wrap">
          <span>AVAILABLE {allocated(line.id,'AVAILABLE')}/{line.baseAcceptedQuantity} {line.baseUnitCode}</span>
          <span>DAMAGED {allocated(line.id,'DAMAGED')}/{line.baseDamagedQuantity} {line.baseUnitCode}</span>
          <span>REJECTED {allocated(line.id,'REJECTED')}/{line.baseRejectedQuantity} {line.baseUnitCode}</span>
          {line.expiryControl&&<span>Expiry bắt buộc{line.shelfLifeDays?` • shelf life ${line.shelfLifeDays} ngày`:''}</span>}
          {editable&&<button type="button" onClick={()=>addRow(line)}>Thêm {line.trackingType==='Serial'?'serial':'lot'}</button>}
        </div>
        <UiTableScroll>
          <table aria-label={`Lot Serial ${line.productCode}`}>
            <thead><tr><th>Status</th><th>Base Qty</th><th>Lot</th><th>Manufacture</th><th>Expiry</th><th>Serial</th>{editable&&<th>Thao tác</th>}</tr></thead>
            <tbody>
              {rows.map((row,index)=>({row,index})).filter(x=>x.row.lineId===line.id).map(({row,index})=><tr key={row.id??`new-${index}`}>
                <td><select aria-label={`Identity status ${line.productCode} ${index}`} disabled={!editable} value={row.targetStatus} onChange={e=>update(index,{targetStatus:e.target.value as IdentityRow['targetStatus']})}>
                  <option value="AVAILABLE">AVAILABLE</option><option value="DAMAGED">DAMAGED</option><option value="REJECTED">REJECTED</option>
                </select></td>
                <td><input aria-label={`Identity quantity ${line.productCode} ${index}`} disabled={!editable||line.trackingType==='Serial'} type="number" min="0.0001" step="any" value={row.baseQuantity} onChange={e=>update(index,{baseQuantity:e.target.value})}/></td>
                <td><input aria-label={`Identity lot ${line.productCode} ${index}`} disabled={!editable} value={row.lotNumber??''} onChange={e=>update(index,{lotNumber:e.target.value})}/></td>
                <td><input aria-label={`Identity manufacture ${line.productCode} ${index}`} disabled={!editable} type="date" value={row.manufactureDate??''} onChange={e=>update(index,{manufactureDate:e.target.value})}/></td>
                <td><input aria-label={`Identity expiry ${line.productCode} ${index}`} disabled={!editable} type="date" value={row.expiryDate??''} onChange={e=>update(index,{expiryDate:e.target.value})}/></td>
                <td><input aria-label={`Identity serial ${line.productCode} ${index}`} disabled={!editable} value={row.serialNumber??''} onChange={e=>update(index,{serialNumber:e.target.value})}/></td>
                {editable&&<td><button type="button" onClick={()=>remove(index)}>Xóa</button></td>}
              </tr>)}
              {rows.every(row=>row.lineId!==line.id)&&<tr><td colSpan={editable?7:6} className="ui-empty-cell">Chưa khai báo identity.</td></tr>}
            </tbody>
          </table>
        </UiTableScroll>
      </fieldset>)}
      {editable&&<div className="ui-inline-actions"><button type="button" disabled={busy} onClick={()=>void save()}>{busy?'Đang lưu...':'Lưu Lot / Serial / Expiry'}</button></div>}
      {!editable&&<p className="ui-muted-text">Identity đang ở chế độ chỉ đọc tại trạng thái {status}.</p>}
    </>}
  </UiCard>;
}
