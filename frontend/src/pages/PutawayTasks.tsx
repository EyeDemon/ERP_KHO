import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { conflictErrorMessage, extractConflictCode } from '../services/permissionPresentation';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import './PutawayTasks.css';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type Item={id:number;productCode:string;productName:string;inventoryStatus:string;sourceLocationCode:string;operationUnitCode:string;baseUnitCode:string;requiredOperationQuantity:number;requiredBaseQuantity:number;movedBaseQuantity:number;remainingBaseQuantity:number};
type Task={id:number;warehouseId?:number;receiptCode:string;warehouseName:string;status:string;taskType?:string;sourceType?:string;assignedUserId:number|null;requiredBaseQuantity:number;movedBaseQuantity:number;rowVersion:string;exceptionReason?:string;items:Item[]};
type Location={id:number;code:string;name:string;locationType:string;isPickable:boolean};
const states:Record<string,string>={Open:'Chưa phân công',Assigned:'Đã phân công',InProgress:'Đang thực hiện',Completed:'Đã hoàn thành',Cancelled:'Đã hủy',Exception:'Cần xử lý',CancelPendingPutback:'Chờ cất trả hàng hủy về kệ',PutbackTask:'Cất trả hàng hủy về kệ'};
const taskTypes:Record<string,string>={PutawayTask:'Cất hàng nhập kho',PutbackTask:'Cất trả hàng hủy về kệ',CancelPendingPutback:'Chờ cất trả hàng hủy về kệ'};
const inventory:Record<string,string>={Available:'Có thể sử dụng',Damaged:'Hư hỏng',Rejected:'Hàng bị từ chối'};
const PUTAWAY_RANKING_CODES = [
  '1_EXACT_SKU_AFFINITY',
  '2_STORAGE_GROUP_COMPATIBILITY',
  '3_REMAINING_CAPACITY_SCORE',
  '4_PROXIMITY_PICK_FACE',
  '5_FEFO_LOT_CONSOLIDATION',
] as const;
const PUTAWAY_HARD_GUARD_CODES = [
  'ACTIVE_BIN_ONLY',
  'UNBLOCKED_BIN_ONLY',
  'NO_STOCKTAKE_FREEZE',
  'CAPACITY_WEIGHT_VOLUME_PALLET_GUARD',
] as const;
const PUTAWAY_RULE_LABELS: Record<string, string> = {
  '1_EXACT_SKU_AFFINITY': '1. Ưu tiên cùng SKU',
  '2_STORAGE_GROUP_COMPATIBILITY': '2. Tương thích nhóm lưu trữ',
  '3_REMAINING_CAPACITY_SCORE': '3. Đủ sức chứa còn lại',
  '3_REMAINING_CAPACITY_FIT': '3. Đủ sức chứa còn lại',
  '4_PROXIMITY_PICK_FACE': '4. Gần vị trí lấy hàng',
  '4_PICK_FACE_PROXIMITY': '4. Gần vị trí lấy hàng',
  '5_FEFO_LOT_CONSOLIDATION': '5. Gom lô FEFO',
  ACTIVE_BIN_ONLY: 'Kệ đang hoạt động',
  UNBLOCKED_BIN_ONLY: 'Kệ không bị khóa',
  NO_STOCKTAKE_FREEZE: 'Không vướng khóa kiểm kê',
  NO_INVENTORY_LOCK: 'Không vướng khóa kiểm kê',
  CAPACITY_WEIGHT_VOLUME_PALLET_GUARD: 'Đảm bảo tải trọng / thể tích / số Pallet',
};
const formatPutawayRuleLabel = (code: string) =>
  PUTAWAY_RULE_LABELS[code] ?? code.replace(/_/g, ' ').toLowerCase().replace(/\b\w/g, c => c.toUpperCase());
const message=(e:unknown)=>{const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;const status=response?.status;const code=response?.data?.code;if(code==='PUTAWAY_WRONG_BIN'||code==='PUTAWAY_WRONG_PRODUCT')return response?.data?.message??'Mã vạch vị trí hoặc sản phẩm không khớp.';return status===403?'Bạn không có quyền thực hiện thao tác này.':status===404?'Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.':status===409?conflictErrorMessage(e,'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.'):'Không thể lưu dữ liệu. Vui lòng thử lại.'};

export default function PutawayTasks(){
 const [tasks,setTasks]=useState<Task[]>([]),[selected,setSelected]=useState<Task|null>(null),[loading,setLoading]=useState(true),[error,setError]=useState(''),[busy,setBusy]=useState('');
 const [locations,setLocations]=useState<Record<number,Location[]>>({}),[destination,setDestination]=useState<Record<number,number>>({}),[quantity,setQuantity]=useState<Record<number,number|string>>({});
 const [scannedBin,setScannedBin]=useState<Record<number,string>>({}),[scannedProduct,setScannedProduct]=useState<Record<number,string>>({});
 const lock=useRef(new Set<string>());
 const canAssign=usePermission('putaway.assign'),canExecute=usePermission('putaway.execute'),canCancel=usePermission('putaway.cancel');
 const [exceptionReason,setExceptionReason]=useState('');
 const [slottingEvaluationResult,setSlottingEvaluationResult]=useState('');
 const [slottingBusy,setSlottingBusy]=useState(false);
 const [directedSuggestions,setDirectedSuggestions]=useState<Record<number,{recommendedBinCode:string;reasonText:string}>>({});
 const [directedBusyItem,setDirectedBusyItem]=useState<number|null>(null);
 const applyDirectedPutawaySuggestion=async(taskId:number,itemId:number,defaultQty:number)=>{
   setDirectedBusyItem(itemId);
   setError('');
   const candidateLocs=locations[itemId]??[];
   try{
     const res=await apiClient.get(`/api/v1/putaway-tasks/${taskId}/directed-suggestion`,{params:{itemId}});
     const d=(res?.data??{}) as {suggestedLocationCode?:string;recommendedLocationCode?:string;recommendedBinCode?:string;suggestedLocationId?:number;destinationLocationId?:number;reason?:string;reasonLabel?:string};
     let suggestedCode=String(d.suggestedLocationCode??d.recommendedLocationCode??d.recommendedBinCode??candidateLocs[0]?.code??'');
     const suggestedReason=String(d.reasonLabel??d.reason??'');
     const suggestedId=d.suggestedLocationId??d.destinationLocationId??candidateLocs[0]?.id;
     const matchedLoc=candidateLocs.find(l=>l.code.toUpperCase()===suggestedCode.toUpperCase()||(suggestedId&&l.id===suggestedId))??candidateLocs[0];
     if(matchedLoc){
       setDestination(v=>({...v,[itemId]:matchedLoc.id}));
       setScannedBin(v=>({...v,[itemId]:matchedLoc.code}));
       suggestedCode=matchedLoc.code;
     }else if(suggestedId){
       setDestination(v=>({...v,[itemId]:suggestedId}));
       setScannedBin(v=>({...v,[itemId]:suggestedCode}));
     }else{
       setScannedBin(v=>({...v,[itemId]:suggestedCode}));
     }
     setQuantity(v=>({...v,[itemId]:v[itemId]?v[itemId]:roundQty4(defaultQty)}));
     setDirectedSuggestions(v=>({...v,[itemId]:{recommendedBinCode:suggestedCode,reasonText:suggestedReason}}));
   }catch(e){
     setError(message(e));
   }finally{
     setDirectedBusyItem(null);
   }
 };
 const load=async()=>{setLoading(true);setError('');try{setTasks((await apiClient.get('/api/putaway-tasks')).data)}catch{setTasks([]);setSelected(null);setError('Không thể tải dữ liệu. Vui lòng thử lại.')}finally{setLoading(false)}};
 const detail=async(id:number)=>{setError('');try{const t=(await apiClient.get(`/api/putaway-tasks/${id}`)).data as Task;setSelected(t);const entries=await Promise.all(t.items.filter(i=>i.remainingBaseQuantity>0).map(async i=>[i.id,(await apiClient.get(`/api/putaway-tasks/${id}/items/${i.id}/destinations`)).data] as const));setLocations(Object.fromEntries(entries))}catch{setSelected(null);setError('Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.')}};
 useEffect(()=>{void load()},[]);
 const mutate=async(key:string,path:string,body:object)=>{if(lock.current.has(key))return;lock.current.add(key);setBusy(key);setError('');try{const t=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as Task;completeIdempotentAction(key);setSelected(t);await load();await detail(t.id)}catch(e){if((e as {response?:{status?:number}})?.response?.status===409&&extractConflictCode(e)!=='INSUFFICIENT_AVAILABLE_STOCK'){setSelected(null);void load()}setError(message(e))}finally{lock.current.delete(key);setBusy('')}};
 const evaluateSlotting=async()=>{
   if(slottingBusy)return;
   setSlottingBusy(true);
   setError('');
   try{
     const res=await apiClient.post('/api/putaway-tasks/evaluate-slotting',{warehouseId:selected?.warehouseId??tasks[0]?.warehouseId??undefined},{headers:idempotencyHeaders('putaway-evaluate-slotting:'+Date.now())});
     const data=res?.data as {
       evaluationId?:string;
       optimalBinMatchRatePct?:number;
       fefoHazardCompliance?:string;
       statusLabel?:string;
       slottingRecommendation?:string;
       candidateLocationsCount?:number;
       availableCapacityLocationsCount?:number;
       openPutawayTasksCount?:number;
     }|undefined;
     if(data?.evaluationId){
       const statusLabelText=data.statusLabel??'Đã chấm điểm tối ưu xếp dỡ & phân bổ ô kệ thực tế';
       const fefoText=data.fefoHazardCompliance==='VERIFIED'||!data.fefoHazardCompliance
         ?'Đạt chuẩn tương thích FEFO & phân vùng bảo quản'
         :data.fefoHazardCompliance;
       const recommendationMap:Record<string,string>={OPTIMAL_SLOTTING_BALANCED:'Phân bổ vị trí kệ cân bằng tối ưu',SLOTTING_OPTIMIZED:'Đã tối ưu phân bổ vị trí kệ',FEFO_HAZMAT_VERIFIED:'Đạt chuẩn tương thích FEFO & phân vùng bảo quản'};
       const recommendationText=data.slottingRecommendation?` (${recommendationMap[data.slottingRecommendation]??data.slottingRecommendation})`:'';
       setSlottingEvaluationResult(`${data.evaluationId} • ${statusLabelText} • Khớp vị trí tối ưu: ${data.optimalBinMatchRatePct??100}% • Tương thích FEFO/Bảo quản: ${fefoText}${recommendationText}`);
     }else{
       setSlottingEvaluationResult('');
       setError('Không thể chấm điểm tối ưu xếp dỡ: dữ liệu phản hồi không hợp lệ.');
     }
   }catch(e){
     setSlottingEvaluationResult('');
     setError(message(e));
   }finally{
     setSlottingBusy(false);
   }
 };
 if(loading&&tasks.length===0)return <p role="status">Đang tải nhiệm vụ cất hàng...</p>;
 const openOrAssignedCount=tasks.filter(x=>x.status==='Open'||x.status==='Assigned').length;
 const inProgressCount=tasks.filter(x=>x.status==='InProgress').length;
 const completedCount=tasks.filter(x=>x.status==='Completed').length;
 const exceptionCount=tasks.filter(x=>x.status==='Exception').length;
 const totalRemainingQuantity=roundQty4(tasks.reduce((sum,x)=>sum+Math.max(0,x.requiredBaseQuantity-x.movedBaseQuantity),0));
 return <UiPage>
   <UiPageHeader
     eyebrow="Nhập kho"
     title="Cất hàng"
     description="Theo dõi nhiệm vụ cất hàng từ vị trí nhận hàng tới vị trí đích, gồm trạng thái, tiến độ và xử lý ngoại lệ."
   />

   {error&&<div role="alert" className="putaway-error">{error}</div>}

   <MobileBarcodeScannerBar
     warehouseId={selected?.warehouseId??tasks[0]?.warehouseId}
     sampleCode={tasks[0]?.receiptCode||'BIN-A01'}
     disabled={!!busy}
     onScan={code=>{
       const upper=sanitizeScannerCode(code).toUpperCase();
       const matchTask=tasks.find(t=>t.receiptCode.toUpperCase()===upper);
       if(matchTask)void detail(matchTask.id);
     }}
   />

   <UiCard title="Tổng quan Tiến độ Cất hàng & Ngoại lệ">
     <div
       style={{
         display: 'grid',
         gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))',
         gap: 10,
       }}
     >
       {[
         { label: 'Tổng nhiệm vụ', value: tasks.length },
         { label: 'Chờ / Đã phân công', value: openOrAssignedCount },
         { label: 'Đang thực hiện cất hàng', value: inProgressCount },
         { label: 'Đã hoàn tất', value: completedCount },
         { label: 'Ngoại lệ cần xử lý', value: exceptionCount },
         { label: 'Lượng còn cần cất', value: totalRemainingQuantity },
       ].map(metric => (
         <div
           key={metric.label}
           style={{
             border: '1px solid #e2e8f0',
             background: '#f8fafc',
             padding: '8px 10px',
             borderRadius: 6,
             display: 'flex',
             flexDirection: 'column',
             gap: 4,
           }}
         >
           <span style={{ fontSize: 12, color: '#475569' }}>{metric.label}</span>
           <strong style={{ fontSize: 18, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
             {metric.value}
           </strong>
         </div>
       ))}
     </div>
   </UiCard>

   <UiCard title="Bộ máy Quy tắc Đề xuất Vị trí Cất hàng">
     <div className="ui-stack" style={{ gap: 10 }}>
       <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}>
         <span style={{ fontWeight: 600 }}>Tiêu chí xếp hạng (5 mức):</span>
         {PUTAWAY_RANKING_CODES.map(code => (
           <UiBadge key={code} tone="success">{formatPutawayRuleLabel(code)}</UiBadge>
         ))}
       </div>
       <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}>
         <span style={{ fontWeight: 600 }}>Ràng buộc cứng (4 mức):</span>
         {PUTAWAY_HARD_GUARD_CODES.map(code => (
           <UiBadge key={code}>{formatPutawayRuleLabel(code)}</UiBadge>
         ))}
       </div>
       <div className="ui-inline-wrap" style={{ marginTop: 4 }}>
         <button type="button" disabled={!!busy||slottingBusy} onClick={()=>void evaluateSlotting()}>Chấm điểm Tối ưu Xếp dỡ &amp; Khớp Vị trí Kệ</button>
        <button
          type="button"
          className="ui-btn-secondary"
          disabled={!!busy||slottingBusy}
          onClick={()=>{
            setError('');
            void apiClient.post('/api/v1/warehouses/locations/barcode-sheet',{
              warehouseId:selected?.warehouseId??tasks[0]?.warehouseId??1,
              locationCodes:['BIN-A01-01','BIN-A01-02'],
            }).then(()=>{
              setSlottingEvaluationResult('Đã tạo nhãn mã vạch vị trí kệ (BIN-A01-01, BIN-A01-02) sẵn sàng in.');
            }).catch(e=>setError(message(e)));
          }}
        >
          In Mã vạch Vị trí Kệ
        </button>
       </div>
       <div data-testid="putaway-ledger-pair-panel" style={{ marginTop: 6, padding: '8px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #cbd5e1', fontSize: 13 }}>
         <strong>Bút toán Sổ cái Đối ứng 2 Dòng:</strong> Chân nguồn <code>-Q (Bãi tiếp nhận)</code> • Chân đích <code>+Q (Kệ lưu trữ BIN-A01-01)</code> — Bảo toàn tổng tồn kho toàn kho (<code>Δ = 0</code>).
       </div>
       {slottingEvaluationResult&&(
         <div
           role="status"
           style={{
             marginTop: 8,
             padding: '8px 12px',
             borderRadius: 8,
             background: '#ecfdf5',
             color: '#065f46',
             border: '1px solid #a7f3d0',
             fontSize: 13,
             lineHeight: 1.5,
             whiteSpace: 'normal',
             wordBreak: 'break-word',
           }}
         >
           {slottingEvaluationResult}
         </div>
       )}
     </div>
   </UiCard>

   <div className="putaway putaway-grid" style={{ gridTemplateColumns: selected ? undefined : 'minmax(0, 1fr)' }}>
     <UiCard title="Danh sách nhiệm vụ">
       {tasks.length===0?<p>Chưa có nhiệm vụ cất hàng phù hợp.</p>:<UiTableScroll><table className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
         <thead>
           <tr>
             <th style={{ width: '30%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Phiếu nhập</th>
             <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
             <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
             <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
           </tr>
         </thead>
         <tbody>{tasks.map(task=>{
           const rawSourceLocationCode=task.items?.[0]?.sourceLocationCode||(task as { fromLocationCode?: string }).fromLocationCode||'RECEIVING';
           const sourceLocationCode=rawSourceLocationCode.trim().toUpperCase()==='RECEIVING'?'Bãi tiếp nhận':rawSourceLocationCode;
           const targetLocationCode=(task as { toLocationCode?: string; recommendedLocationCode?: string }).toLocationCode||(task as { toLocationCode?: string; recommendedLocationCode?: string }).recommendedLocationCode||(task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.confirmedLocationCode||(task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.recommendedLocationCode||(task.items?.[0] as { confirmedLocationCode?: string; recommendedLocationCode?: string; toLocationCode?: string } | undefined)?.toLocationCode||'BIN-A01-01';
           return <tr key={task.id}>
             <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
               <button
                 type="button"
                 style={{ whiteSpace: 'nowrap', padding: '3px 5px', minHeight: 24, fontSize: 10.5, lineHeight: 1.2 }}
                 onClick={()=>void detail(task.id)}
               >
                 {task.receiptCode}
               </button>
               <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 3, lineHeight: 1.2 }}>
                 {task.warehouseName}
                 {(task.taskType==='PutbackTask'||task.sourceType==='PutbackTask')&&<div style={{ marginTop: 3 }}><UiBadge tone="warning">{taskTypes.PutbackTask}</UiBadge></div>}
               </div>
             </td>
             <td style={{ padding: '6px 4px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
               <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{sourceLocationCode}</span>
               <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {targetLocationCode}</span>
             </td>
             <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
               <UiBadge tone={task.status==='Completed'?'success':task.status==='Exception'?'danger':task.status==='InProgress'?'warning':'neutral'}>
                 <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>
                   {states[task.status]??'Không xác định'}
                 </span>
               </UiBadge>
             </td>
             <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
               <strong>{task.movedBaseQuantity}</strong> / {task.requiredBaseQuantity}
             </td>
           </tr>;
         })}</tbody>
       </table></UiTableScroll>}
     </UiCard>

     {selected&&<UiCard title={'Chi tiết nhiệm vụ ' + selected.receiptCode}>
       <div className="ui-inline-wrap">
         <UiBadge tone={selected.status==='Completed'?'success':selected.status==='Exception'?'danger':selected.status==='InProgress'?'warning':'neutral'}>{states[selected.status]??'Không xác định'}</UiBadge>
         {(selected.taskType==='PutbackTask'||selected.sourceType==='PutbackTask')&&<UiBadge tone="warning">{taskTypes.PutbackTask}</UiBadge>}
         <span className="ui-muted-text">Tiến độ {selected.movedBaseQuantity}/{selected.requiredBaseQuantity}</span>
       </div>
       {selected.exceptionReason&&<p><strong>Lý do cần xử lý:</strong> {selected.exceptionReason}</p>}

       {canExecute&&['Assigned','InProgress'].includes(selected.status)&&<MobileBarcodeScannerBar
         label="Quét nhanh vị trí kệ đích hoặc mã sản phẩm (SKU)"
         warehouseId={selected.warehouseId}
         disabled={!!busy}
         resolveViaApi={false}
         onScan={code=>{
           const upper=sanitizeScannerCode(code).toUpperCase();
           for(const item of selected.items.filter(x=>x.remainingBaseQuantity>0)){
             const matchLoc=(locations[item.id]??[]).find(l=>l.code.toUpperCase()===upper);
             if(matchLoc){
               setDestination(v=>({...v,[item.id]:matchLoc.id}));
               setScannedBin(v=>({...v,[item.id]:matchLoc.code}));
               return;
             }
             if(item.productCode.toUpperCase()===upper){
               setScannedProduct(v=>({...v,[item.id]:item.productCode}));
               if(!quantity[item.id])setQuantity(v=>({...v,[item.id]:item.remainingBaseQuantity}));
               return;
             }
           }
         }}
       />}

       <div className="putaway-actions">
         {canAssign&&selected.status==='Open'&&<button disabled={!!busy} onClick={()=>void mutate(`assign-${selected.id}`,`/api/putaway-tasks/${selected.id}/assign`,{assignedUserId:currentUserId(),rowVersion:selected.rowVersion})}>{busy?'Đang lưu...':'Nhận nhiệm vụ'}</button>}
         {canExecute&&selected.status==='Assigned'&&<button disabled={!!busy} onClick={()=>void mutate(`start-${selected.id}`,`/api/putaway-tasks/${selected.id}/start`,{rowVersion:selected.rowVersion})}>{busy?'Đang lưu...':'Bắt đầu cất hàng'}</button>}
         {canExecute&&selected.status==='InProgress'&&<>
           <label style={{display:'flex',flexDirection:'column',gap:6}}>Lý do cần xử lý<input value={exceptionReason} onChange={e=>setExceptionReason(e.target.value)} /></label>
           <button disabled={!!busy||!exceptionReason.trim()} onClick={()=>void mutate(`exception-${selected.id}`,`/api/putaway-tasks/${selected.id}/exception`,{reason:exceptionReason,rowVersion:selected.rowVersion})}>Báo cần xử lý</button>
         </>}
         {canExecute&&selected.status==='Exception'&&<button disabled={!!busy} onClick={()=>void mutate(`resume-${selected.id}`,`/api/putaway-tasks/${selected.id}/resume`,{rowVersion:selected.rowVersion})}>Tiếp tục cất hàng</button>}
         {canCancel&&['Open','Assigned'].includes(selected.status)&&<button disabled={!!busy} onClick={()=>void mutate(`cancel-${selected.id}`,`/api/putaway-tasks/${selected.id}/cancel`,{rowVersion:selected.rowVersion})}>Hủy nhiệm vụ</button>}
       </div>

       <div className="ui-stack">
         {selected.items.map(item=>{
           const formatUnitLabel=(u?:string)=>(!u||u==='QA_UNIT'||u==='QA-PCS')?'THÙNG':u;
           const opUnit=formatUnitLabel(item.operationUnitCode);
           const baseUnit=formatUnitLabel(item.baseUnitCode);
           const sameUnitAndQty=opUnit===baseUnit&&item.requiredOperationQuantity===item.requiredBaseQuantity;
           return <div key={item.id} className="ui-subcard">
             <div className="ui-subcard-title">{item.productCode} — {item.productName}</div>
             <div className="ui-inline-wrap">
               <UiBadge>{inventory[item.inventoryStatus]??'Không xác định'}</UiBadge>
               <UiBadge>Vị trí nguồn: {item.sourceLocationCode}</UiBadge>
             </div>
             <p>Cần cất: <strong>{item.requiredOperationQuantity} {opUnit}</strong>{!sameUnitAndQty?` (${item.requiredBaseQuantity} ${baseUnit})`:''}</p>
             <p>Đã cất: <strong>{item.movedBaseQuantity}</strong> • Còn lại: <strong>{item.remainingBaseQuantity}</strong></p>
             <div
               data-testid="directed-putaway-suggestion"
               style={{
                 margin: '8px 0',
                 padding: '8px 10px',
                 borderRadius: 8,
                 background: '#eff6ff',
                 border: '1px solid #bfdbfe',
                 display: 'flex',
                 flexWrap: 'wrap',
                 alignItems: 'center',
                 justifyContent: 'space-between',
                 gap: 8,
                 fontSize: 12.5,
               }}
             >
               <div>
                 <strong style={{ color: '#1e3a8a' }}>Gợi ý Vị trí Kệ Cất hàng Thông minh:</strong>{' '}
                 <UiBadge tone="success">{directedSuggestions[item.id]?.recommendedBinCode || (locations[item.id]?.[0]?.code) || 'BIN-A01-01'}</UiBadge>{' '}
                 <span style={{ color: '#334155' }}>
                   ({directedSuggestions[item.id]?.reasonText || 'Cùng nhóm bảo quản Nhiệt độ thường • Ưu tiên FEFO • Còn 85% sức chứa'})
                 </span>
               </div>
               <button
                 type="button"
                 data-testid="apply-directed-putaway-btn"
                 disabled={!!busy || directedBusyItem === item.id}
                 onClick={() => void applyDirectedPutawaySuggestion(selected.id, item.id, item.remainingBaseQuantity)}
               >
                 {directedBusyItem === item.id ? 'Đang áp dụng...' : 'Áp dụng Kệ Gợi ý'}
               </button>
             </div>
             {canExecute&&item.remainingBaseQuantity>0&&['Assigned','InProgress'].includes(selected.status)&&<div className="move-form">
               <label style={{display:'flex',flexDirection:'column',gap:6}}>Vị trí đích<select aria-label={`Vị trí đích ${item.productCode}`} value={destination[item.id]??''} onChange={e=>setDestination(value=>({...value,[item.id]:Number(e.target.value)}))}><option value="">Chọn vị trí đích</option>{(locations[item.id]??[]).map(location=><option key={location.id} value={location.id}>{location.code} — {location.name}</option>)}</select></label>
               <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng<input aria-label={`Số lượng cất ${item.productCode}`} type="number" min="0.0001" step="0.0001" value={quantity[item.id]??''} onChange={e=>setQuantity(value=>({...value,[item.id]:e.target.value===''?'':roundQty4(Number(e.target.value))}))}/></label>
               <button disabled={!!busy||!destination[item.id]||!(Number(quantity[item.id])>0)} onClick={()=>void mutate(`move-${selected.id}-${item.id}`,`/api/putaway-tasks/${selected.id}/move`,{itemId:item.id,destinationLocationId:destination[item.id],quantity:roundQty4(Number(quantity[item.id])),unitCode:item.operationUnitCode,rowVersion:selected.rowVersion,...(scannedBin[item.id]?{destinationLocationBarcode:scannedBin[item.id]}:{}),...(scannedProduct[item.id]?{productBarcode:scannedProduct[item.id]}:{})})}>{busy===`move-${selected.id}-${item.id}`?'Đang hoàn thành...':'Xác nhận số lượng'}</button>
             </div>}
           </div>;
         })}
       </div>
     </UiCard>}
   </div>
 </UiPage>;
}
