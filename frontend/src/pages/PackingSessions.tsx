import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type SourceLine = {
  pickingTaskLineId:number; productId:number; productCode:string; productName:string;
  pickedQuantity:number; packedQuantity:number; remainingQuantity:number;
};
type HuContent = {
  id:number; pickingTaskLineId:number; productId:number; productCode:string; productName:string;
  quantity:number; packedAt:string;
};
type HandlingUnit = {
  id:number; huCode:string; barcode:string; sscc?:string; warehouseId:number; packingSessionId:number;
  parentHandlingUnitId?:number; parentHandlingUnitCode?:string; type:string; status:string;
  grossWeightKg?:number; netWeightKg?:number; volumeM3?:number; sealedAt?:string; createdAt:string; closedAt?:string;
  rowVersion:string; contents:HuContent[];
};
type PackingSession = {
  id:number; sessionCode:string; pickingTaskId:number; pickingTaskCode:string; sourceType:string; sourceId?:number;
  sourceCode?:string; warehouseId:number; warehouseName:string; status:string; requiredQuantity:number; packedQuantity:number;
  remainingQuantity:number; handlingUnitCount:number; createdAt:string; startedAt?:string; packedAt?:string; closedAt?:string;
  rowVersion?:string; lines?:SourceLine[]; handlingUnits?:HandlingUnit[];
};

const stateLabels:Record<string,string>={
  Open:'Mở',InProgress:'Đang đóng gói',Packed:'Đã PACKED',Closed:'Đã đóng',Exception:'Ngoại lệ',Cancelled:'Đã hủy',
};
const huStateLabels:Record<string,string>={
  Open:'Mở',InUse:'Đang dùng',Closed:'Đã đóng',Staged:'Đã staging',Loaded:'Đã load',Shipped:'Đã ship',Cancelled:'Đã hủy',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  ['Packed','Closed','Shipped'].includes(status)?'success':status==='Cancelled'||status==='Exception'?'danger':status==='InProgress'||status==='InUse'?'warning':'neutral';

const errorMessage=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='PACK_QUANTITY_EXCEEDS_PICKED')return 'Số lượng đóng gói vượt số lượng đã Picking.';
  if(code==='HU_CIRCULAR_NESTING')return 'Không thể tạo vòng lặp trong cấu trúc Handling Unit.';
  if(code==='HU_CLOSED')return response?.data?.message??'Handling Unit đã đóng hoặc đã đi vào downstream.';
  if(code==='HU_CONTENT_MISMATCH')return response?.data?.message??'Content/HU không khớp Packing session.';
  if(code==='HU_NOT_FOUND'||response?.status===404)return 'Không tìm thấy Packing session/Handling Unit trong phạm vi kho của bạn.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác Packing này.';
  if(response?.status===409)return response.data?.message??'Dữ liệu Packing đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message??'Không thể xử lý Packing.';
};

export default function PackingSessions(){
  const [sessions,setSessions]=useState<PackingSession[]>([]);
  const [selected,setSelected]=useState<PackingSession|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState('');
  const [huType,setHuType]=useState('Carton');
  const [huCode,setHuCode]=useState('');
  const [packForm,setPackForm]=useState<Record<number,{huId:string;barcode:string;quantity:string}>>({});
  const [parents,setParents]=useState<Record<number,string>>({});
  const lock=useRef(new Set<string>());
  const canExecute=usePermission('packing.execute');
  const canCreateHu=usePermission('handling_unit.create');
  const canModifyHu=usePermission('handling_unit.modify');

  const load=async()=>{
    setLoading(true);setError('');
    try{setSessions((await apiClient.get('/api/packing-sessions')).data)}
    catch(e){setSessions([]);setSelected(null);setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  const detail=async(id:number)=>{
    if(!id)return;
    setError('');
    try{setSelected((await apiClient.get(`/api/packing-sessions/${id}`)).data)}
    catch(e){setSelected(null);setError(errorMessage(e))}
  };
  useEffect(()=>{void load()},[]);

  const mutate=async(key:string,path:string,body:object)=>{
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');
    try{
      const result=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as PackingSession;
      completeIdempotentAction(key);
      setSelected(result);
      await load();
    }catch(e){
      if((e as {response?:{status?:number}})?.response?.status===409&&selected?.id) await detail(selected.id);
      setError(errorMessage(e));
    }finally{lock.current.delete(key);setBusy('')}
  };

  if(loading)return <p role="status">Đang tải Packing session...</p>;
  const activeHus=(selected?.handlingUnits??[]).filter(x=>!['Cancelled','Staged','Loaded','Shipped'].includes(x.status));
  const packableHus=activeHus.filter(x=>['Open','InUse'].includes(x.status));

  return <UiPage>
    <UiPageHeader
      eyebrow="Outbound"
      title="Packing & Handling Unit"
      description="Đóng gói lượng đã Picking vào carton/tote/HU. Packing bảo toàn quantity và không trừ OnHand; shipment dispatch mới là boundary xuất kho."
    />
    {error&&<div role="alert">{error}</div>}

    <div className="ui-stack">
      <UiCard title="Packing sessions">
        {sessions.length===0?<p>Chưa có Packing session. Session được tạo khi Picking hoàn tất.</p>:<UiTableScroll>
          <table aria-label="Danh sách Packing session">
            <thead><tr><th>Session</th><th>Nguồn</th><th>Kho</th><th>Trạng thái</th><th>Tiến độ</th><th>HU</th></tr></thead>
            <tbody>{sessions.map(item=><tr key={item.id}>
              <td><button type="button" onClick={()=>void detail(item.id)}>{item.sessionCode}</button></td>
              <td>{item.sourceCode??item.pickingTaskCode}</td>
              <td>{item.warehouseName}</td>
              <td><UiBadge tone={tone(item.status)}>{stateLabels[item.status]??item.status}</UiBadge></td>
              <td><strong>{item.packedQuantity}</strong> / {item.requiredQuantity}</td>
              <td>{item.handlingUnitCount}</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {selected&&<UiCard title={`Chi tiết ${selected.sessionCode}`}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{stateLabels[selected.status]??selected.status}</UiBadge>
          <span className="ui-muted-text">{selected.pickingTaskCode} • {selected.sourceCode??selected.sourceType} • {selected.warehouseName}</span>
          <span>Đã pack <strong>{selected.packedQuantity}</strong> / {selected.requiredQuantity} • Còn {selected.remainingQuantity}</span>
        </div>
        <p className="ui-muted-text">
          Foundation hiện có HU create/content confirmation/close/nesting và quantity conservation. Lot/Serial canonical, split/merge/repack,
          label printing, cartonization recommendation và Shipment ownership/loading chưa được bật.
        </p>

        {canCreateHu&&['Open','InProgress'].includes(selected.status)&&<fieldset>
          <legend>Tạo Handling Unit</legend>
          <div className="ui-inline-wrap">
            <label>Loại HU
              <select aria-label="Loại Handling Unit" value={huType} onChange={e=>setHuType(e.target.value)}>
                <option value="Carton">Carton</option><option value="Tote">Tote</option><option value="Pallet">Pallet</option>
                <option value="Container">Container</option><option value="Custom">Custom</option>
              </select>
            </label>
            <label>Mã/Barcode tùy chọn
              <input aria-label="Mã Handling Unit" value={huCode} onChange={e=>setHuCode(e.target.value)} placeholder="Tự sinh nếu bỏ trống"/>
            </label>
            <button type="button" disabled={!!busy} onClick={()=>void mutate(
              `packing-create-hu-${selected.id}`,`/api/packing-sessions/${selected.id}/create-hu`,
              {type:huType,huCode:huCode||undefined,barcode:huCode||undefined,rowVersion:selected.rowVersion}
            )}>Tạo HU</button>
          </div>
        </fieldset>}

        <div className="ui-stack">
          {(selected.lines??[]).map(line=><fieldset key={line.pickingTaskLineId}>
            <legend>{line.productCode} — {line.productName}</legend>
            <p>Picked <strong>{line.pickedQuantity}</strong> • Packed {line.packedQuantity} • Còn {line.remainingQuantity}</p>
            {canExecute&&selected.status!=='Packed'&&selected.status!=='Closed'&&line.remainingQuantity>0&&<div className="ui-inline-wrap">
              <label>HU đích
                <select aria-label={`HU đích ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.huId??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{barcode:'',quantity:''}),huId:e.target.value}}))}>
                  <option value="">Chọn HU đang mở</option>
                  {packableHus.map(hu=><option value={hu.id} key={hu.id}>{hu.huCode} ({hu.type})</option>)}
                </select>
              </label>
              <label>Quét SKU/barcode
                <input aria-label={`Product barcode packing ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.barcode??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{huId:'',quantity:''}),barcode:e.target.value}}))}/>
              </label>
              <label>Số lượng
                <input aria-label={`Packing quantity ${line.productCode}`} type="number" min="0" step="any" value={packForm[line.pickingTaskLineId]?.quantity??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{huId:'',barcode:''}),quantity:e.target.value}}))}/>
              </label>
              <button type="button" disabled={!!busy||!packForm[line.pickingTaskLineId]?.huId||!packForm[line.pickingTaskLineId]?.barcode||!Number(packForm[line.pickingTaskLineId]?.quantity)}
                onClick={()=>void mutate(
                  `packing-pack-${selected.id}-${line.pickingTaskLineId}`,`/api/packing-sessions/${selected.id}/pack`,
                  {handlingUnitId:Number(packForm[line.pickingTaskLineId]?.huId),pickingTaskLineId:line.pickingTaskLineId,productBarcode:packForm[line.pickingTaskLineId]?.barcode,quantity:Number(packForm[line.pickingTaskLineId]?.quantity),rowVersion:selected.rowVersion}
                )}>Xác nhận Pack</button>
            </div>}
          </fieldset>)}
        </div>

        <div className="ui-stack">
          {(selected.handlingUnits??[]).map(hu=><fieldset key={hu.id}>
            <legend>{hu.huCode} — {hu.type}</legend>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(hu.status)}>{huStateLabels[hu.status]??hu.status}</UiBadge>
              <span>Barcode: <strong>{hu.barcode}</strong></span>
              {hu.parentHandlingUnitCode&&<span>Trong HU: {hu.parentHandlingUnitCode}</span>}
              <span>Content: {hu.contents.reduce((sum,item)=>sum+item.quantity,0)}</span>
            </div>
            {hu.contents.length>0&&<ul>{hu.contents.map(item=><li key={item.id}>{item.productCode}: {item.quantity}</li>)}</ul>}

            <div className="ui-inline-wrap">
              {canExecute&&['Open','InUse'].includes(hu.status)&&hu.contents.length>0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `packing-close-hu-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/close`,
                {rowVersion:selected.rowVersion}
              )}>Đóng HU</button>}
              {canModifyHu&&['Open','InUse','Closed'].includes(hu.status)&&!hu.parentHandlingUnitId&&activeHus.some(parent=>parent.id!==hu.id&&['Open','InUse'].includes(parent.status))&&<>
                <label>HU cha
                  <select aria-label={`HU cha ${hu.huCode}`} value={parents[hu.id]??''} onChange={e=>setParents(v=>({...v,[hu.id]:e.target.value}))}>
                    <option value="">Chọn HU cha</option>
                    {activeHus.filter(parent=>parent.id!==hu.id&&['Open','InUse'].includes(parent.status)).map(parent=><option key={parent.id} value={parent.id}>{parent.huCode}</option>)}
                  </select>
                </label>
                <button type="button" disabled={!!busy||!parents[hu.id]} onClick={()=>void mutate(
                  `hu-nest-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/nest`,
                  {parentHandlingUnitId:Number(parents[hu.id]),rowVersion:selected.rowVersion}
                )}>Lồng HU</button>
              </>}
              {canModifyHu&&hu.parentHandlingUnitId&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `hu-unnest-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/unnest`,
                {rowVersion:selected.rowVersion}
              )}>Tách khỏi HU cha</button>}
              {canModifyHu&&['Open','InUse'].includes(hu.status)&&hu.contents.length===0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `hu-cancel-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/cancel`,
                {rowVersion:selected.rowVersion}
              )}>Hủy HU rỗng</button>}
            </div>
          </fieldset>)}
        </div>

        <div className="ui-inline-wrap">
          {canExecute&&selected.status==='InProgress'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-complete-${selected.id}`,`/api/packing-sessions/${selected.id}/complete`,{rowVersion:selected.rowVersion}
          )}>Xác nhận PACKED</button>}
          {canExecute&&selected.status==='Packed'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-close-${selected.id}`,`/api/packing-sessions/${selected.id}/close`,{rowVersion:selected.rowVersion}
          )}>Đóng Packing session</button>}
          {canExecute&&['Open','InProgress'].includes(selected.status)&&selected.packedQuantity===0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-cancel-${selected.id}`,`/api/packing-sessions/${selected.id}/cancel`,{rowVersion:selected.rowVersion}
          )}>Hủy session rỗng</button>}
        </div>
      </UiCard>}
    </div>
  </UiPage>;
}
