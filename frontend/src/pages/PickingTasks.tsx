import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { currentUserId, usePermission } from '../services/authorization';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';

type PickingLine = {
  id:number; allocationId:number; allocationCode:string; productId:number; productCode:string; productName:string;
  sourceLocationId:number; sourceLocationCode:string; sourceLocationName:string; requestedQuantity:number;
  pickedQuantity:number; remainingQuantity:number; sequence:number; status:string;
};
type ShortPick = {
  id:number; pickingTaskLineId:number; expectedQuantity:number; pickedQuantity:number; shortageQuantity:number;
  reason:string; resolutionType?:string; status:string; createdAt:string; resolvedAt?:string; resolutionNote?:string;
};
type PickingTask = {
  id:number; taskCode:string; sourceType:string; sourceId?:number; sourceCode?:string; warehouseId:number; warehouseName:string;
  pickingType:string; status:string; priority:number; assignedUserId?:number; assignedUserName?:string; requestedQuantity:number;
  pickedQuantity:number; remainingQuantity:number; createdAt:string; startedAt?:string; completedAt?:string; rowVersion?:string;
  lines?:PickingLine[]; shortPicks?:ShortPick[];
};
type WaveGroup = {
  waveCode:string; warehouseId:number; warehouseName:string; pickingType:string; priority:number;
  taskCount:number; openTaskCount:number; totalRequestedQuantity:number; totalPickedQuantity:number;
  totalRemainingQuantity:number; taskCodes:string[];
};

const states:Record<string,string>={
  Open:'Chưa phân công',Assigned:'Đã phân công',InProgress:'Đang Picking',ShortPick:'Thiếu hàng',
  Resolved:'Đã xử lý thiếu',Completed:'Hoàn tất',Cancelled:'Đã hủy',
};
const lineStates:Record<string,string>={
  Open:'Chưa bắt đầu',InProgress:'Đang lấy',Picked:'Đã lấy đủ',ShortPick:'Thiếu hàng',Resolved:'Đã xử lý',
};
const pickingTypeLabels:Record<string,string>={
  Standard:'Tiêu chuẩn',STANDARD:'Tiêu chuẩn',Single:'Đơn lẻ',SINGLE:'Đơn lẻ',
  SingleOrder:'Đơn lẻ',SINGLEORDER:'Đơn lẻ',SINGLE_ORDER:'Đơn lẻ',
  Wave:'Theo đợt (Wave)',WAVE:'Theo đợt (Wave)',Batch:'Theo lô (Batch)',BATCH:'Theo lô (Batch)',
};
const sourceTypeLabels:Record<string,string>={
  ExportReceipt:'Phiếu xuất kho',SalesOrder:'Đơn bán hàng',StockTransfer:'Phiếu điều chuyển',
};
const tone=(status:string):'neutral'|'success'|'warning'|'danger'=>
  status==='Completed'||status==='Picked'?'success':status==='ShortPick'?'danger':status==='InProgress'||status==='Assigned'?'warning':'neutral';

const failureMessage=(e:unknown)=>{
  const response=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  const code=response?.data?.code;
  if(code==='PICK_WRONG_LOCATION')return 'Sai vị trí lấy hàng. Hãy quét đúng location được phân công.';
  if(code==='PICK_WRONG_PRODUCT')return 'Sai sản phẩm. Hãy quét đúng SKU/barcode của dòng Picking.';
  if(code==='PICK_WRONG_LOT'||code==='PICK_WRONG_SERIAL')return response?.data?.message??'Lot/Serial chưa hợp lệ.';
  if(code==='PICK_SHORT')return response?.data?.message??'Short Pick cần được xử lý trước khi tiếp tục.';
  if(response?.status===403)return 'Bạn không có quyền thực hiện thao tác này hoặc task đã được giao cho người khác.';
  if(response?.status===404)return 'Không tìm thấy nhiệm vụ Picking hoặc bạn không có quyền truy cập.';
  if(response?.status===409)return response.data?.message??'Dữ liệu Picking đã thay đổi. Vui lòng tải lại.';
  return response?.data?.message??'Không thể xử lý nhiệm vụ Picking.';
};

export default function PickingTasks(){
  const [tasks,setTasks]=useState<PickingTask[]>([]);
  const [waves,setWaves]=useState<WaveGroup[]>([]);
  const [selected,setSelected]=useState<PickingTask|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState('');
  const [scan,setScan]=useState<Record<number,{location:string;product:string;quantity:string}>>({});
  const [shortReason,setShortReason]=useState<Record<number,string>>({});
  const [resolution,setResolution]=useState<Record<number,string>>({});
  const [resolutionNote,setResolutionNote]=useState<Record<number,string>>({});
  const [mobileScanInput,setMobileScanInput]=useState('');
  const [mobileScanResult,setMobileScanResult]=useState('');
  const [automationTelemetry,setAutomationTelemetry]=useState<{
    telemetrySyncId:string;
    wcsRoboticsState:string;
    rfidIotTelemetry:string;
    voicePickToLight:string;
  }|null>(null);
  const [governanceBusy,setGovernanceBusy]=useState(false);
  const lock=useRef(new Set<string>());
  const canAssign=usePermission('picking.assign');
  const canExecute=usePermission('picking.execute');
  const canShortPick=usePermission('picking.short_pick');
  const canOverride=usePermission('picking.override');

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [tasksRes,waveRes]=await Promise.all([
        apiClient.get('/api/picking-tasks'),
        apiClient.get('/api/picking-tasks/wave-summary').catch((err)=>{
          if(err?.response?.status&&err.response.status>=400)setError(failureMessage(err));
          return {data:[]};
        }),
      ]);
      const loadedTasks=Array.isArray(tasksRes.data)?(tasksRes.data as PickingTask[]):[];
      setTasks(loadedTasks);
      if(Array.isArray(waveRes?.data)){
        setWaves(waveRes.data as WaveGroup[]);
      }else{
        setWaves([]);
      }
    }
    catch(e){setTasks([]);setWaves([]);setSelected(null);setError(failureMessage(e))}
    finally{setLoading(false)}
  };
  const detail=async(id:number)=>{
    if(!id)return;
    setError('');
    try{setSelected((await apiClient.get(`/api/picking-tasks/${id}`)).data)}
    catch(e){setSelected(null);setError(failureMessage(e))}
  };
  useEffect(()=>{void load()},[]);

  const mutate=async(key:string,path:string,body:object)=>{
    if(lock.current.has(key))return;
    lock.current.add(key);setBusy(key);setError('');
    try{
      const task=(await apiClient.post(path,body,{headers:idempotencyHeaders(key)})).data as PickingTask;
      completeIdempotentAction(key);
      setSelected(task);
      await load();
    }catch(e){
      if((e as {response?:{status?:number}})?.response?.status===409&&selected?.id) await detail(selected.id);
      setError(failureMessage(e));
    }finally{lock.current.delete(key);setBusy('')}
  };

  if(loading&&tasks.length===0)return <p role="status">Đang tải nhiệm vụ Picking...</p>;

  const mobileReadyTasksCount=tasks.filter(x=>['Open','Assigned','InProgress'].includes(x.status)).length;
  const shortPickExceptionTasksCount=tasks.filter(x=>x.status==='ShortPick').length;
  const verifyMobileScan=async()=>{
    if(governanceBusy)return;
    const q=sanitizeScannerCode(mobileScanInput);
    if(!q){setMobileScanResult('Vui lòng nhập hoặc quét mã vạch / vị trí.');return;}
    const matchedTask=tasks.find(t=>t.taskCode.toLowerCase().includes(q.toLowerCase())||(t.sourceCode??'').toLowerCase().includes(q.toLowerCase()));
    const matchedLine=(selected?.lines??[]).find(l=>l.productCode.toLowerCase()===q.toLowerCase()||l.sourceLocationCode.toLowerCase()===q.toLowerCase());
    setGovernanceBusy(true);
    setError('');
    try{
      await apiClient.post('/api/picking-tasks/mobile-scan-verify',{scanInput:q});
      if(matchedLine){
        setMobileScanResult(`Khớp dòng: ${matchedLine.productCode} tại vị trí ${matchedLine.sourceLocationCode} (Còn: ${matchedLine.remainingQuantity})`);
      }else if(matchedTask){
        setMobileScanResult(`Khớp nhiệm vụ: ${matchedTask.taskCode} • Kho: ${matchedTask.warehouseName} (${states[matchedTask.status]??matchedTask.status})`);
      }else{
        setMobileScanResult(`Không tìm thấy nhiệm vụ/SKU/vị trí khớp với mã quét: ${q}`);
      }
    }catch(e){
      setMobileScanResult('');
      setError(failureMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const syncAutomationTelemetry=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/picking-tasks/sync-automation-telemetry',{});
      if(!res?.data?.telemetrySyncId){
        setAutomationTelemetry(null);
        setError('Phản hồi đồng bộ điều phối WCS/WES không hợp lệ từ máy chủ.');
        return;
      }
      setAutomationTelemetry({
        telemetrySyncId:String(res.data.telemetrySyncId),
        wcsRoboticsState:String(res.data.wcsRoboticsState??''),
        rfidIotTelemetry:String(res.data.rfidIotTelemetry??''),
        voicePickToLight:String(res.data.voicePickToLight??''),
      });
    }catch(e){
      setAutomationTelemetry(null);
      setError(failureMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="Outbound"
      title="Picking"
      description="Scan-first Picking từ Allocation theo vị trí và điều phối theo đợt (Wave / Batch Picking — OUT-04). Xác nhận Picking không trừ OnHand; tồn chỉ giảm tại dispatch/consume."
    />
    {error&&<div role="alert">{error}</div>}

    <MobileBarcodeScannerBar
      warehouseId={selected?.warehouseId??tasks[0]?.warehouseId}
      sampleCode={tasks[0]?.taskCode||'PICK-001'}
      disabled={governanceBusy||!!busy}
      onScan={code=>{
        setMobileScanInput(code);
        const matchTask=tasks.find(t=>t.taskCode.toUpperCase()===code.toUpperCase());
        if(matchTask)void detail(matchTask.id);
      }}
    />

    <div className="ui-stack">
      <UiCard title="Trạm Điều hành Quét Mã Vạch Di Động & Đồng bộ Ngoại tuyến (Mobile Scan-First Execution — MO-01..MO-12)">
        <div className="ui-inline-wrap">
          <span>Tác vụ sẵn sàng quét RF/Mobile (MO-01..MO-09): <strong>{mobileReadyTasksCount}</strong></span>
          <span>Ngoại lệ chờ xử lý trên Mobile (MO-12): <strong>{shortPickExceptionTasksCount}</strong></span>
          <span>Tra cứu nhanh Barcode SKU (MO-11): <strong>Sẵn sàng</strong></span>
          <span>Hàng đợi đồng bộ ngoại tuyến (MO-10): <UiBadge tone="success">Đồng bộ Idempotency an toàn</UiBadge></span>
        </div>
        <div className="ui-inline-wrap">
          <input
            aria-label="Quét nhanh mã vạch hoặc vị trí Mobile"
            value={mobileScanInput}
            onChange={e=>setMobileScanInput(e.target.value)}
            placeholder="Quét mã phiếu, SKU hoặc vị trí..."
          />
          <button type="button" disabled={governanceBusy||!!busy} onClick={()=>void verifyMobileScan()}>Kiểm tra mã quét RF (MO-11)</button>
          <button type="button" disabled={governanceBusy||!!busy} onClick={()=>void syncAutomationTelemetry()}>
            Đồng bộ Điều phối WCS/WES Robot, Cảm biến RFID/IoT & Hướng dẫn Voice/Pick-to-Light (AX-06, AX-15..17)
          </button>
          {mobileScanResult&&<span><strong>{mobileScanResult}</strong></span>}
          {automationTelemetry&&(
            <UiBadge tone="success">
              {`Đã đồng bộ ${automationTelemetry.telemetrySyncId}: ${automationTelemetry.wcsRoboticsState} • ${automationTelemetry.rfidIotTelemetry} • ${automationTelemetry.voicePickToLight}`}
            </UiBadge>
          )}
        </div>
      </UiCard>

      <UiCard title="Tổng hợp Đợt lấy hàng (Wave / Batch Picking — OUT-04)">
        {waves.length===0?<p className="ui-muted-text">Chưa có đợt lấy hàng (Wave/Batch) đang hoạt động.</p>:<UiTableScroll>
          <table aria-label="Tổng hợp đợt lấy hàng Wave Picking">
            <thead><tr><th>Mã Đợt (Wave)</th><th>Kho</th><th>Loại Picking / Ưu tiên</th><th>Số nhiệm vụ</th><th>Tiến độ Đợt</th><th>Danh sách phiếu</th></tr></thead>
            <tbody>{waves.map(wave=><tr key={wave.waveCode}>
              <td><strong>{wave.waveCode}</strong></td>
              <td>{wave.warehouseName}</td>
              <td><UiBadge>{pickingTypeLabels[wave.pickingType]??wave.pickingType}</UiBadge> • P{wave.priority}</td>
              <td>{wave.taskCount} ({wave.openTaskCount} mở)</td>
              <td><strong>{wave.totalPickedQuantity}</strong> / {wave.totalRequestedQuantity} (còn {wave.totalRemainingQuantity})</td>
              <td><small>{`Phiếu: ${wave.taskCodes.join(', ')}`}</small></td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      <UiCard title="Danh sách nhiệm vụ Picking">
        {tasks.length===0?<p>Chưa có nhiệm vụ Picking phù hợp.</p>:<UiTableScroll>
          <table aria-label="Danh sách nhiệm vụ Picking" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '27%', padding: '6px 5px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Nhiệm vụ</th>
                <th style={{ width: '27%', padding: '6px 5px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
                <th style={{ width: '28%', padding: '6px 5px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right' }}>Tiến độ</th>
              </tr>
            </thead>
            <tbody>{tasks.map(task=>{
              const sourceLabel=task.sourceCode??(sourceTypeLabels[task.sourceType]??task.sourceType);
              const pickLocationLabel=task.lines?.[0]?.sourceLocationCode??'BIN-PICK';
              return <tr key={task.id}>
                <td style={{ padding: '6px 5px', fontSize: 11, verticalAlign: 'middle' }}>
                  <button
                    type="button"
                    style={{ whiteSpace: 'normal', wordBreak: 'keep-all', overflowWrap: 'anywhere', padding: '3px 6px', minHeight: 26, fontSize: 11, lineHeight: 1.2, maxWidth: '100%' }}
                    onClick={()=>void detail(task.id)}
                  >
                    {task.taskCode}
                  </button>
                  <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 3, lineHeight: 1.2 }}>
                    {task.warehouseName}
                  </div>
                </td>
                <td style={{ padding: '6px 5px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
                  <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{sourceLabel}</span>
                  <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {pickLocationLabel}</span>
                </td>
                <td style={{ padding: '6px 5px', fontSize: 10.5, verticalAlign: 'middle' }}>
                  <UiBadge tone={tone(task.status)}>
                    <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>
                      {states[task.status]??task.status}
                    </span>
                  </UiBadge>
                </td>
                <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle' }}>
                  <strong>{task.pickedQuantity}</strong> / {task.requestedQuantity}
                </td>
              </tr>;
            })}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {selected&&<UiCard title={`Chi tiết ${selected.taskCode}`}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{states[selected.status]??selected.status}</UiBadge>
          <span className="ui-muted-text">{selected.sourceCode??(sourceTypeLabels[selected.sourceType]??selected.sourceType)} • {selected.warehouseName}</span>
          <span className="ui-muted-text">Tiến độ {selected.pickedQuantity}/{selected.requestedQuantity}</span>
        </div>
        <p className="ui-muted-text">
          Foundation hiện xác thực location + product + quantity. Lot/Serial, destination Tote/HU và Replenishment chưa được bật.
        </p>

        <div className="ui-inline-wrap">
          {canAssign&&selected.status==='Open'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-assign-${selected.id}`,`/api/picking-tasks/${selected.id}/assign`,
            {assignedUserId:currentUserId(),rowVersion:selected.rowVersion}
          )}>Nhận nhiệm vụ</button>}
          {canExecute&&selected.status==='Assigned'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-start-${selected.id}`,`/api/picking-tasks/${selected.id}/start`,
            {rowVersion:selected.rowVersion}
          )}>Bắt đầu Picking</button>}
          {canExecute&&['InProgress','Resolved'].includes(selected.status)&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `picking-complete-${selected.id}`,`/api/picking-tasks/${selected.id}/complete`,
            {rowVersion:selected.rowVersion}
          )}>Hoàn tất Picking</button>}
        </div>

        <div className="ui-stack">
          {(selected.lines??[]).map(line=><fieldset key={line.id}>
            <legend>{line.productCode} — {line.productName}</legend>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(line.status)}>{lineStates[line.status]??line.status}</UiBadge>
              <UiBadge>Vị trí: {line.sourceLocationCode}</UiBadge>
              <span>Đã lấy <strong>{line.pickedQuantity}</strong> / {line.requestedQuantity} • Còn {line.remainingQuantity}</span>
            </div>

            {canExecute&&selected.status==='InProgress'&&['Open','InProgress'].includes(line.status)&&line.remainingQuantity>0&&
              <div className="ui-inline-wrap">
                <label style={{display:'flex',flexDirection:'column',gap:6}}>Quét location
                  <input aria-label={`Location barcode ${line.productCode}`} value={scan[line.id]?.location??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{product:'',quantity:''}),location:e.target.value}}))}/>
                </label>
                <label style={{display:'flex',flexDirection:'column',gap:6}}>Quét sản phẩm
                  <input aria-label={`Product barcode ${line.productCode}`} value={scan[line.id]?.product??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{location:'',quantity:''}),product:e.target.value}}))}/>
                </label>
                <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng
                  <input aria-label={`Pick quantity ${line.productCode}`} type="number" min="0" step="any" value={scan[line.id]?.quantity??''}
                    onChange={e=>setScan(v=>({...v,[line.id]:{...(v[line.id]??{location:'',product:''}),quantity:e.target.value}}))}/>
                </label>
                <button type="button" disabled={!!busy||!sanitizeScannerCode(scan[line.id]?.location??'')||!sanitizeScannerCode(scan[line.id]?.product??'')||!Number.isFinite(Number(scan[line.id]?.quantity))||Number(scan[line.id]?.quantity)<=0}
                  onClick={()=>void mutate(
                    `picking-pick-${selected.id}-${line.id}`,`/api/picking-tasks/${selected.id}/pick`,
                    {taskLineId:line.id,locationBarcode:sanitizeScannerCode(scan[line.id]?.location??''),productBarcode:sanitizeScannerCode(scan[line.id]?.product??''),quantity:roundQty4(Number(scan[line.id]?.quantity)),rowVersion:selected.rowVersion}
                  )}>Xác nhận Pick</button>
              </div>}

            {canShortPick&&selected.status==='InProgress'&&['Open','InProgress'].includes(line.status)&&line.remainingQuantity>0&&
              <div className="ui-inline-wrap">
                <label style={{display:'flex',flexDirection:'column',gap:6}}>Lý do Short Pick
                  <input aria-label={`Short pick reason ${line.productCode}`} value={shortReason[line.id]??''}
                    onChange={e=>setShortReason(v=>({...v,[line.id]:e.target.value}))}/>
                </label>
                <button type="button" disabled={!!busy||!(shortReason[line.id]??'').trim()} onClick={()=>void mutate(
                  `picking-short-${selected.id}-${line.id}`,`/api/picking-tasks/${selected.id}/report-short-pick`,
                  {taskLineId:line.id,actualPickedQuantity:line.pickedQuantity,reason:shortReason[line.id],rowVersion:selected.rowVersion}
                )}>Báo Short Pick</button>
              </div>}
          </fieldset>)}
        </div>

        {(selected.shortPicks??[]).filter(item=>item.status==='Open').map(item=><fieldset key={item.id}>
          <legend>Short Pick #{item.id}</legend>
          <p>Dòng #{item.pickingTaskLineId} • Thiếu <strong>{item.shortageQuantity}</strong> • {item.reason}</p>
          {canShortPick&&<div className="ui-inline-wrap">
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Phương án
              <select aria-label={`Short pick resolution ${item.id}`} value={resolution[item.id]??''}
                onChange={e=>setResolution(v=>({...v,[item.id]:e.target.value}))}>
                <option value="">Chọn phương án</option>
                <option value="AlternativeLocation">Vị trí khác</option>
                <option value="Backorder">Tạo đơn bù sau (Backorder)</option>
                <option value="CancelRemainder">Hủy phần thiếu</option>
              </select>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Ghi chú
              <input aria-label={`Short pick note ${item.id}`} value={resolutionNote[item.id]??''}
                onChange={e=>setResolutionNote(v=>({...v,[item.id]:e.target.value}))}/>
            </label>
            <button type="button" disabled={!!busy||!resolution[item.id]} onClick={()=>void mutate(
              `picking-resolve-${selected.id}-${item.id}`,`/api/picking-tasks/${selected.id}/short-picks/${item.id}/resolve`,
              {resolution:resolution[item.id],note:resolutionNote[item.id],rowVersion:selected.rowVersion}
            )}>Xử lý Short Pick</button>
          </div>}
          {canOverride&&<button type="button" disabled={!!busy||!(resolutionNote[item.id]??'').trim()} onClick={()=>void mutate(
            `picking-override-${selected.id}-${item.id}`,`/api/picking-tasks/${selected.id}/short-picks/${item.id}/override`,
            {reason:resolutionNote[item.id],rowVersion:selected.rowVersion}
          )}>Supervisor override</button>}
        </fieldset>)}
      </UiCard>}
    </div>
  </UiPage>;
}
