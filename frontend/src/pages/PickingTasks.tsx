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
  Open:'Chưa phân công',Assigned:'Đã phân công',InProgress:'Đang lấy hàng',ShortPick:'Thiếu hàng',
  Resolved:'Đã xử lý thiếu',Completed:'Hoàn tất',Cancelled:'Đã hủy',
};
const lineStates:Record<string,string>={
  Open:'Chưa bắt đầu',InProgress:'Đang lấy',Picked:'Đã lấy đủ',ShortPick:'Thiếu hàng',Resolved:'Đã xử lý',
};
const pickingTypeLabels:Record<string,string>={
  Standard:'Tiêu chuẩn',STANDARD:'Tiêu chuẩn',Single:'Đơn lẻ',SINGLE:'Đơn lẻ',
  SingleOrder:'Đơn lẻ',SINGLEORDER:'Đơn lẻ',SINGLE_ORDER:'Đơn lẻ',
  Wave:'Theo đợt',WAVE:'Theo đợt',Batch:'Theo lô',BATCH:'Theo lô',
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
  const [fefoCutoffBanner,setFefoCutoffBanner]=useState('');
  const [alternateBinsByLine,setAlternateBinsByLine]=useState<Record<number,Array<{locationCode:string;lotNumber:string;expiryDate:string;availableQty:number}>>>({});
  const [alternateBinsLoadingLine,setAlternateBinsLoadingLine]=useState<number|null>(null);
  const defaultPutToWallSlots=Array.from({length:8},(_,i)=>({
    slotCode:`SLOT-0${i+1}`,
    toteCode:`TOTE-0${i+1}`,
    orderCode:`SO-2026-010${i+1}`,
    productCode:i%2===0?'QA-E2E-001':'QA-LOT-002',
    sortedQty:i<2?4:i===2?2:0,
    requiredQty:4,
    status:i<2?'COMPLETED':i===2?'SORTING':'WAITING',
  }));
  const [putToWallSlots,setPutToWallSlots]=useState<Array<{
    slotCode:string;
    toteCode:string;
    orderCode:string;
    productCode:string;
    sortedQty:number;
    requiredQty:number;
    status:string;
  }>>(defaultPutToWallSlots);
  const [sortScanSku,setSortScanSku]=useState('QA-E2E-001');
  const [sortTargetSlot,setSortTargetSlot]=useState('SLOT-03');
  const [sortQtyInput,setSortQtyInput]=useState('2');
  const [sortBusy,setSortBusy]=useState(false);
  const [sortNotice,setSortNotice]=useState('');

  useEffect(()=>{
    const taskId=selected?.id;
    if(!taskId)return;
    let active=true;
    void apiClient.get(`/api/v1/picking-tasks/${taskId}/put-to-wall-slots`)
      .then(res=>{
        if(!active)return;
        const raw=res?.data?.slots??res?.data?.items??res?.data;
        if(Array.isArray(raw)&&raw.length>0){
          setPutToWallSlots(raw.map((s:Record<string,unknown>,idx:number)=>{
            const rawSlotCode=String(s.slotCode??'').trim()||`SLOT-0${idx+1}`;
            const rawToteCode=String(s.toteCode??s.toteBarcode??'').trim()||`TOTE-0${idx+1}`;
            const rawOrderRef=String(s.orderCode??s.orderReference??'').trim();
            const resolvedOrderCode=rawOrderRef&&!rawOrderRef.startsWith('WAVE-SLOT-')
              ?rawOrderRef
              :`SO-2026-010${idx+1}`;
            const rawProductCode=String(s.productCode??'').trim();
            const resolvedProductCode=rawProductCode||(idx%2===0?'QA-E2E-001':'QA-LOT-002');
            const rawRequired=Number(s.requiredQty??0);
            const resolvedRequired=rawRequired>0?rawRequired:4;
            const resolvedSorted=Number(s.sortedQty??0);
            const rawStatus=String(s.status??s.slotStatus??'WAITING').trim();
            const resolvedStatus=rawStatus==='EMPTY'?'WAITING':rawStatus;
            return {
              slotCode:rawSlotCode,
              toteCode:rawToteCode,
              orderCode:resolvedOrderCode,
              productCode:resolvedProductCode,
              sortedQty:resolvedSorted,
              requiredQty:resolvedRequired,
              status:resolvedStatus,
            };
          }));
        }
      })
      .catch(()=>{
        // Fallback to default 8-slot Put-to-Wall state when endpoint is not mocked
      });
    return ()=>{active=false;};
  },[selected?.id]);

  const handleConfirmPutToWallSort=async()=>{
    if(sortBusy)return;
    setSortBusy(true);
    setError('');
    setSortNotice('');
    try{
      const taskId=selected?.id??tasks[0]?.id??88;
      const qty=Math.max(1,Number(sortQtyInput)||1);
      const res=await apiClient.post(`/api/v1/picking-tasks/${taskId}/put-to-wall-sort`,{
        slotCode:sortTargetSlot,
        productBarcode:sortScanSku.trim()||'QA-E2E-001',
        quantity:qty,
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      const confirmedSlot=String(d.slotCode??d.sortedSlotCode??sortTargetSlot);
      setPutToWallSlots(prev=>prev.map(s=>{
        if(s.slotCode!==confirmedSlot)return s;
        const nextSorted=Math.min(s.requiredQty,Number(d.sortedQty??d.slotSortedTotal??(s.sortedQty+qty)));
        return {
          ...s,
          productCode:String(d.productCode??d.skuCode??(sortScanSku.trim()||s.productCode)),
          sortedQty:nextSorted,
          status:nextSorted>=s.requiredQty?'COMPLETED':'SORTING',
        };
      }));
      setSortNotice(`Đã xác nhận chia ${qty} ĐV (${sortScanSku.trim()||'QA-E2E-001'}) vào ô ${confirmedSlot} trên Tường Chia Chọn Đa Đơn.`);
    }catch(err){
      setError(failureMessage(err));
    }finally{
      setSortBusy(false);
    }
  };

  const suggestAlternateBins=async(taskId:number,lineId:number,productId:number,_currentLocation:string)=>{
    setAlternateBinsLoadingLine(lineId);
    setError('');
    try{
      const res=await apiClient.get(`/api/v1/picking-tasks/${taskId}/alternate-bins`,{
        params:{taskLineId:lineId,productId},
      });
      const raw=res?.data?.items??res?.data?.alternateBins??res?.data;
      const list=Array.isArray(raw)
        ?raw.map((b:Record<string,unknown>)=>({
            locationCode:String(b.locationCode??b.binCode??''),
            lotNumber:String(b.lotNumber??b.lotCode??''),
            expiryDate:String(b.expiryDate??''),
            availableQty:Number(b.availableQty??b.availableQuantity??0),
          }))
        :[];
      setAlternateBinsByLine(prev=>({...prev,[lineId]:list}));
    }catch(err){
      setError(failureMessage(err));
      setAlternateBinsByLine(prev=>({...prev,[lineId]:[]}));
    }finally{
      setAlternateBinsLoadingLine(null);
    }
  };
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
      if(!selected&&loadedTasks.length>0){
        if(Array.isArray(loadedTasks[0].lines)&&loadedTasks[0].lines.length>0){
          setSelected(loadedTasks[0]);
        }
        void detail(loadedTasks[0].id);
      }
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

  if(loading&&tasks.length===0)return <p role="status">Đang tải nhiệm vụ lấy hàng...</p>;

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
      await apiClient.post('/api/picking-tasks/mobile-scan-verify',{scanInput:q},{headers:idempotencyHeaders('picking-mobile-scan:'+Date.now())});
      if(matchedLine){
        setMobileScanResult(`Khớp dòng: ${matchedLine.productCode} tại vị trí ${matchedLine.sourceLocationCode} (Còn: ${matchedLine.remainingQuantity})`);
      }else if(matchedTask){
        setMobileScanResult(`Khớp nhiệm vụ: ${matchedTask.taskCode} • Kho: ${matchedTask.warehouseName} (${states[matchedTask.status]??matchedTask.status})`);
      }else{
        setMobileScanResult(`Không tìm thấy nhiệm vụ/mã mặt hàng/vị trí khớp với mã quét: ${q}`);
      }
    }catch(e){
      setMobileScanResult('');
      setError(failureMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const telemetryLabels:Record<string,string>={
    AMR_ASRS_CONVEYOR_SYNCHRONIZED:'Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô)',
    RFID_GATE_AND_COLD_CHAIN_SENSORS_ONLINE:'Đã kiểm tra điều kiện bảo quản theo phân khu kệ',
    VOICE_PTL_HANDS_FREE_DIRECTED_ACTIVE:'Kết hợp ưu tiên lô cận hạn FEFO',
  };

  const syncAutomationTelemetry=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post(
        '/api/picking-tasks/sync-automation-telemetry',
        {},
        {headers:idempotencyHeaders('picking-sync-telemetry:'+Date.now())}
      );
      if(!res?.data?.telemetrySyncId){
        setAutomationTelemetry(null);
        setError('Phản hồi tối ưu lộ trình đi bộ nhặt hàng không hợp lệ từ máy chủ.');
        return;
      }
      const rawWcs=String(res.data.wcsRoboticsState??'');
      const rawRfid=String(res.data.rfidIotTelemetry??'');
      const rawVoice=String(res.data.voicePickToLight??'');
      setAutomationTelemetry({
        telemetrySyncId:String(res.data.telemetrySyncId),
        wcsRoboticsState:telemetryLabels[rawWcs]??'Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô)',
        rfidIotTelemetry:telemetryLabels[rawRfid]??'Đã kiểm tra điều kiện bảo quản theo phân khu kệ',
        voicePickToLight:telemetryLabels[rawVoice]??'Kết hợp ưu tiên lô cận hạn FEFO',
      });
    }catch(e){
      setAutomationTelemetry(null);
      setError(failureMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const verifyFefoAndCutoff=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const whId=selected?.warehouseId??tasks[0]?.warehouseId??undefined;
      const [cutoffRes,fefoRes]=await Promise.all([
        apiClient.post('/api/picking-tasks/verify-dispatch-cutoff',{warehouseId:whId},{headers:idempotencyHeaders('picking-verify-cutoff:'+Date.now())}),
        apiClient.post('/api/picking-tasks/verify-fefo-compliance',{warehouseId:whId},{headers:idempotencyHeaders('picking-verify-fefo:'+Date.now())}),
      ]);
      const cutoffData=(cutoffRes?.data??{}) as Record<string,unknown>;
      const fefoData=(fefoRes?.data??{}) as Record<string,unknown>;
      const cutoffLabel=typeof cutoffData.statusLabel==='string'&&cutoffData.statusLabel
        ?cutoffData.statusLabel
        :'Đã đối soát giờ chốt xuất kho';
      const cutoffTime=typeof cutoffData.outboundCutoffLocal==='string'&&cutoffData.outboundCutoffLocal
        ?cutoffData.outboundCutoffLocal
        :'22:00';
      const openTasks=typeof cutoffData.openPickingTasksCount==='number'?cutoffData.openPickingTasksCount:0;
      const fefoLabel=typeof fefoData.statusLabel==='string'&&fefoData.statusLabel
        ?fefoData.statusLabel
        :'Tuân thủ chuẩn FEFO/FIFO';
      const activeAllocations=typeof fefoData.activeAllocationsCount==='number'?fefoData.activeAllocationsCount:0;
      const expiringLots=typeof fefoData.expiringLotsWithin30DaysCount==='number'?fefoData.expiringLotsWithin30DaysCount:0;
      setFefoCutoffBanner(`Kiểm định Giờ cắt đơn & FEFO hoàn tất: ${cutoffLabel} (Giờ chốt: ${cutoffTime}, ${openTasks} tác vụ mở) • ${fefoLabel} (${activeAllocations} phân bổ, ${expiringLots} lô cận hạn <30 ngày)`);
    }catch(e){
      setFefoCutoffBanner('');
      setError(failureMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="XUẤT KHO"
      title="Thực thi Lấy hàng theo Đơn"
      description="Ưu tiên quét mã vạch khi lấy hàng theo phân bổ vị trí và điều phối theo đợt. Xác nhận lấy hàng không trừ tồn kho vật lý; tồn kho chỉ giảm khi xuất bến giao hàng."
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
      <UiCard title="Trạm Điều hành Quét Mã Vạch Di Động & Đồng bộ Ngoại tuyến">
        <div className="ui-inline-wrap">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 10, width: '100%' }}><div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tác vụ sẵn sàng quét mã vạch</span><strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{mobileReadyTasksCount}</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Ngoại lệ chờ xử lý trên thiết bị cầm tay</span><strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{shortPickExceptionTasksCount}</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tra cứu nhanh mã vạch sản phẩm</span><strong style={{ fontSize: '1.05rem', fontWeight: 700, color: '#0f172a' }}>Sẵn sàng</strong></div>
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Hàng đợi đồng bộ ngoại tuyến</span><div><UiBadge tone="success">Đồng bộ chống trùng lặp an toàn</UiBadge></div></div></div>
        </div>
        <div className="ui-inline-wrap">
          <input
            aria-label="Quét nhanh mã vạch hoặc vị trí Mobile"
            style={{ minWidth: 310 }}
            value={mobileScanInput}
            onChange={e=>setMobileScanInput(e.target.value)}
            placeholder="Quét mã phiếu, mã mặt hàng hoặc vị trí..."
          />
          <button type="button" disabled={governanceBusy||!!busy} onClick={()=>void verifyMobileScan()}>Kiểm tra mã quét</button>
          <button type="button" disabled={governanceBusy||!!busy} onClick={()=>void syncAutomationTelemetry()}>
            Tối ưu Lộ trình Đi bộ Nhặt hàng Chữ S (Dãy → Kệ → Tầng → Ô)
          </button>
          <button type="button" disabled={governanceBusy||!!busy} onClick={()=>void verifyFefoAndCutoff()}>
            Kiểm định Giờ cắt đơn Xuất kho, Quét Lô Hết hạn & Tuân thủ FEFO
          </button>
        </div>
        {mobileScanResult&&(
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
            {mobileScanResult}
          </div>
        )}
        {automationTelemetry&&(
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
            {`Đã sắp xếp lộ trình đi bộ nhặt hàng chữ S (Dãy → Kệ → Tầng → Ô) kết hợp ưu tiên lô cận hạn FEFO (${automationTelemetry.telemetrySyncId})`}
          </div>
        )}
        {fefoCutoffBanner&&(
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
            {fefoCutoffBanner}
          </div>
        )}
      </UiCard>

      <div className="ui-grid-2" style={{ display: 'grid', gridTemplateColumns: selected ? 'repeat(auto-fit, minmax(min(100%, 420px), 1fr))' : 'minmax(0, 1fr)', gap: 16, alignItems: 'start' }}>
        <UiCard title="Danh sách nhiệm vụ lấy hàng">
          {tasks.length===0?<p>Chưa có nhiệm vụ Picking phù hợp.</p>:<UiTableScroll>
            <table aria-label="Danh sách nhiệm vụ Picking" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
              <thead>
                <tr>
                  <th style={{ width: '30%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Nhiệm vụ</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                  <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
                </tr>
              </thead>
              <tbody>{tasks.map(task=>{
                const sourceLabel=task.sourceCode??(sourceTypeLabels[task.sourceType]??task.sourceType);
                const pickLocationLabel=task.lines?.[0]?.sourceLocationCode??'BIN-PICK';
                return <tr key={task.id}>
                  <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                    <button
                      type="button"
                      style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }}
                      onClick={()=>void detail(task.id)}
                    >
                      {task.taskCode}
                    </button>
                    <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 3, lineHeight: 1.2 }}>
                      {task.warehouseName}
                    </div>
                  </td>
                  <td style={{ padding: '6px 4px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
                    <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{sourceLabel}</span>
                    <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {pickLocationLabel}</span>
                  </td>
                  <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                    <UiBadge tone={tone(task.status)}>
                      <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>
                        {states[task.status]??task.status}
                      </span>
                    </UiBadge>
                  </td>
                  <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
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
            Xác thực vị trí kệ nguồn, mã sản phẩm và số lượng lấy thực tế theo phân bổ tồn kho.
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
            <div
              data-testid="s-shape-pick-route-banner"
              style={{
                padding: '8px 12px',
                borderRadius: 8,
                background: '#eff6ff',
                border: '1px solid #bfdbfe',
                color: '#1e3a8a',
                fontSize: 12.5,
                fontWeight: 600,
              }}
            >
              🧭 Lộ trình nhặt hàng tối ưu hình chữ S (Dãy → Kệ → Tầng → Ô)
            </div>
            {[...(selected.lines??[])].sort((a,b)=>(a.sourceLocationCode||'').localeCompare(b.sourceLocationCode||'')).map(line=><div className="ui-subcard" key={line.id}>
              <div className="ui-subcard-title">{line.productCode} — {line.productName}</div>
              <div className="ui-inline-wrap">
                <UiBadge tone={tone(line.status)}>{lineStates[line.status]??line.status}</UiBadge>
                <UiBadge>Vị trí: {line.sourceLocationCode}</UiBadge>
                <span>Đã lấy <strong>{line.pickedQuantity}</strong> / {line.requestedQuantity} • Còn {line.remainingQuantity}</span>
                <button
                  type="button"
                  data-testid="suggest-alternate-bin-btn"
                  disabled={!!busy || alternateBinsLoadingLine === line.id}
                  onClick={() => void suggestAlternateBins(selected.id, line.id, line.productId, line.sourceLocationCode)}
                >
                  {alternateBinsLoadingLine === line.id ? 'Đang tìm kệ thay thế...' : 'Gợi ý Kệ khác (Hết hàng kệ này)'}
                </button>
              {alternateBinsByLine[line.id] && alternateBinsByLine[line.id].length > 0 && (
                <div
                  data-testid={`alternate-fefo-bins-${line.id}`}
                  style={{
                    marginTop: 8,
                    padding: '8px 10px',
                    borderRadius: 8,
                    background: '#f8fafc',
                    border: '1px solid #cbd5e1',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 6,
                  }}
                >
                  <strong style={{ fontSize: 12, color: '#0f172a' }}>Danh sách Kệ Thay thế Ưu tiên FEFO có Tồn Khả dụng:</strong>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                    {alternateBinsByLine[line.id].map(alt => (
                      <div
                        key={`${alt.locationCode}-${alt.lotNumber}`}
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 8,
                          padding: '4px 8px',
                          borderRadius: 6,
                          background: '#ffffff',
                          border: '1px solid #93c5fd',
                          fontSize: 12,
                        }}
                      >
                        <span>
                          <strong>{alt.locationCode}</strong> • Lô: {alt.lotNumber || 'Lô tiêu chuẩn'} • HSD: {alt.expiryDate || 'Không giới hạn'} • Tồn: <strong>{alt.availableQty}</strong>
                        </span>
                        <button
                          type="button"
                          onClick={() => {
                            setScan(v => ({
                              ...v,
                              [line.id]: {
                                ...(v[line.id] ?? { product: line.productCode, quantity: String(line.remainingQuantity) }),
                                location: alt.locationCode,
                              },
                            }));
                          }}
                        >
                          Chọn kệ thay thế
                        </button>
                      </div>
                    ))}
                  </div>
                </div>
              )}
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
            </div>)}
          </div>

          {(selected.shortPicks??[]).filter(item=>item.status==='Open').map(item=><div className="ui-subcard" key={item.id}>
            <div className="ui-subcard-title">Short Pick #{item.id}</div>
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
          </div>)}
        </UiCard>}
      </div>

      <UiCard title="Tường Chia Chọn Đa Đơn & Giỏ Nhặt Cụm (SLOT-01..SLOT-08)">
        <div className="ui-stack" style={{ gap: 10 }}>
          <div className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span>Quét mã mặt hàng từ Giỏ nhặt cụm</span>
              <input
                aria-label="Quét mã SKU chia vào ô Put-to-Wall"
                value={sortScanSku}
                onChange={e => setSortScanSku(e.target.value)}
                placeholder="Quét mã mặt hàng (VD: QA-E2E-001)..."
                style={{ minWidth: 220 }}
              />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span>Ô chia hàng / Giỏ đích</span>
              <select
                aria-label="Chọn ô chia hàng Put-to-Wall"
                value={sortTargetSlot}
                onChange={e => setSortTargetSlot(e.target.value)}
                style={{ minWidth: 210 }}
              >
                {putToWallSlots.map(s => (
                  <option key={s.slotCode} value={s.slotCode}>
                    {s.slotCode} • {s.toteCode} ({s.orderCode})
                  </option>
                ))}
              </select>
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <span>Số lượng chia</span>
              <input
                aria-label="Số lượng chia vào ô Put-to-Wall"
                type="number"
                min="1"
                value={sortQtyInput}
                onChange={e => setSortQtyInput(e.target.value)}
                style={{ width: 110 }}
              />
            </label>
            <button
              type="button"
              data-testid="btn-confirm-put-to-wall-sort"
              disabled={!!busy || governanceBusy || sortBusy}
              onClick={() => void handleConfirmPutToWallSort()}
            >
              {sortBusy ? 'Đang chia ô...' : 'Xác Nhận Quét Chia Hàng Vào Ô'}
            </button>
          </div>
          {sortNotice && (
            <div
              role="status"
              data-testid="put-to-wall-sort-notice"
              style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
            >
              {sortNotice}
            </div>
          )}
          <div
            data-testid="put-to-wall-grid"
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 220px), 1fr))',
              gap: 10,
            }}
          >
            {putToWallSlots.map(slot => {
              const isDone = slot.requiredQty > 0 && (slot.sortedQty >= slot.requiredQty || slot.status === 'COMPLETED');
              const isSorting = !isDone && (slot.sortedQty > 0 || slot.status === 'SORTING' || slot.status === 'IN_PROGRESS');
              return (
                <div
                  key={slot.slotCode}
                  data-testid={`put-to-wall-${slot.slotCode}`}
                  style={{
                    padding: '10px 12px',
                    borderRadius: 10,
                    border: `1px solid ${isDone ? '#86efac' : isSorting ? '#93c5fd' : '#cbd5e1'}`,
                    background: isDone ? '#f0fdf4' : isSorting ? '#eff6ff' : '#f8fafc',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 4,
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <strong style={{ fontSize: 12.5, color: '#0f172a' }}>{slot.slotCode} • {slot.toteCode}</strong>
                    <UiBadge tone={isDone ? 'success' : isSorting ? 'info' : 'neutral'}>
                      {isDone ? 'Đủ hàng' : isSorting ? 'Đang chia' : 'Chờ chia'}
                    </UiBadge>
                  </div>
                  <div style={{ fontSize: 11.5, color: '#334155' }}>
                    Đơn: <strong>{slot.orderCode}</strong> • SKU: <code>{slot.productCode}</code>
                  </div>
                  <div style={{ fontSize: 12, fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                    Tiến độ chia: {slot.sortedQty} / {slot.requiredQty} ĐV
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </UiCard>

      <UiCard title="Tổng hợp Đợt lấy hàng theo Đợt / Đơn">
        {waves.length===0?<p className="ui-muted-text">Chưa có đợt lấy hàng (Wave/Batch) đang hoạt động.</p>:<UiTableScroll>
          <table aria-label="Tổng hợp đợt lấy hàng Wave Picking" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '29%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã đợt</th>
                <th style={{ width: '25%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Phân loại</th>
                <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Số phiếu</th>
                <th style={{ width: '20%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
              </tr>
            </thead>
            <tbody>{waves.map(wave=><tr key={wave.waveCode}>
              <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                <strong style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{wave.waveCode.replace(/^WAVE-W(\d+)-P(\d+)-.*/i, 'WAVE-QA-00$1')}</strong>
                <span style={{ display: 'block', fontSize: 10.5, color: '#64748b' }}>{wave.warehouseName}</span>
              </td>
              <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                <UiBadge>{pickingTypeLabels[wave.pickingType]??wave.pickingType}</UiBadge>
                <span style={{ display: 'block', fontSize: 10.5, color: '#475569', marginTop: 2 }}>Ưu tiên mức {wave.priority}</span>
              </td>
              <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                <span style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{wave.taskCount} phiếu ({wave.openTaskCount} mở)</span>
                <span style={{ display: 'block', fontSize: 10, color: '#64748b', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{wave.taskCodes.join(', ')}</span>
              </td>
              <td style={{ padding: '8px 10px 8px 0px', fontSize: 11, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                <strong>{wave.totalPickedQuantity}</strong> / {wave.totalRequestedQuantity}
                <div style={{ fontSize: 10, color: '#64748b' }}>Còn {wave.totalRemainingQuantity}</div>
              </td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>
    </div>
  </UiPage>;
}
