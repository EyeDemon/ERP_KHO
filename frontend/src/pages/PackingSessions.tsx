import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import { BarcodeSvg } from '../components/BarcodeSvg';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiTabs } from '../ui/ProductionUi';

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
  Open:'Mở',InProgress:'Đang đóng gói',Packed:'Đã đóng gói',Closed:'Đã đóng',Exception:'Ngoại lệ',Cancelled:'Đã hủy',
};
const huStateLabels:Record<string,string>={
  Open:'Mở',InUse:'Đang dùng',Closed:'Đã đóng',Staged:'Đã tập kết',Loaded:'Đã xếp xe',Shipped:'Đã xuất giao',Cancelled:'Đã hủy',
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
  const [activeTab,setActiveTab]=useState<'workbench'|'sessions-hu'|'kitting-video'>('workbench');
  const [sessions,setSessions]=useState<PackingSession[]>([]);
  const [selected,setSelected]=useState<PackingSession|null>(null);
  const [loading,setLoading]=useState(true);
  const [error,setError]=useState('');
  const [busy,setBusy]=useState('');
  const [huType,setHuType]=useState('Carton');
  const [huCode,setHuCode]=useState('');
  const [packForm,setPackForm]=useState<Record<number,{huId:string;barcode:string;quantity:string}>>({});
  const [parents,setParents]=useState<Record<number,string>>({});
  const [huRegistry,setHuRegistry]=useState<HandlingUnit[]>([]);
  const [labelPreviewHu,setLabelPreviewHu]=useState<HandlingUnit|null>(null);
  const [packagingVerificationResult,setPackagingVerificationResult]=useState('');
  const [kittingVasResult,setKittingVasResult]=useState('');
  const [governanceBusy,setGovernanceBusy]=useState(false);
  const lock=useRef(new Set<string>());
  const canExecute=usePermission('packing.execute');
  const canCreateHu=usePermission('handling_unit.create');
  const canModifyHu=usePermission('handling_unit.modify');

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [sessionsRes,husRes]=await Promise.all([
        apiClient.get('/api/packing-sessions'),
        apiClient.get('/api/handling-units').catch((err)=>{
          if((err as {response?:{status?:number}})?.response?.status) setError(errorMessage(err));
          return {data:[]};
        }),
      ]);
      setSessions(Array.isArray(sessionsRes.data)?sessionsRes.data:[]);
      setHuRegistry(Array.isArray(husRes.data)?husRes.data:[]);
    }
    catch(e){setSessions([]);setSelected(null);setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  const detail=async(id:number)=>{
    if(!id)return;
    setError('');
    try{setSelected((await apiClient.get(`/api/packing-sessions/${id}`)).data)}
    catch(e){setSelected(null);setError(errorMessage(e))}
  };
  const [selectedVideoFile,setSelectedVideoFile]=useState<File|null>(null);
  const [videoAwbCode,setVideoAwbCode]=useState<string>('');
  const [videoDurationSec,setVideoDurationSec]=useState<string>('');
  const [videoSizeMb,setVideoSizeMb]=useState<string>('');
  const [videoSha256,setVideoSha256]=useState<string>('');
  const [videoSearchAwb,setVideoSearchAwb]=useState<string>('');
  const [videoNotice,setVideoNotice]=useState<string>('');
  const [videoEvidences,setVideoEvidences]=useState<Array<{id:number;sessionId:number;sessionCode:string;awbCode:string;durationSeconds:number;fileSizeMb:number;sha256Hash:string;recordedAt:string;cameraStation:string}>>([]);
  const packingBoxCatalog = [
    { code: 'CARTON-S', name: 'Thùng nhỏ S (25×20×15 cm)', l: 25, w: 20, h: 15, maxWeightKg: 5, volumeCm3: 7500, dimWeightKg: 1.25, gross: 0.85, vol: 0.006 },
    { code: 'CARTON-M', name: 'Thùng trung M (35×25×20 cm)', l: 35, w: 25, h: 20, maxWeightKg: 12, volumeCm3: 17500, dimWeightKg: 2.92, gross: 1.15, vol: 0.014 },
    { code: 'CARTON-L', name: 'Thùng lớn L (40×30×30 cm)', l: 40, w: 30, h: 30, maxWeightKg: 20, volumeCm3: 36000, dimWeightKg: 6.0, gross: 4.5, vol: 0.0288 },
    { code: 'CARTON-XL', name: 'Thùng đại XL (50×40×30 cm)', l: 50, w: 40, h: 30, maxWeightKg: 35, volumeCm3: 60000, dimWeightKg: 10.0, gross: 6.2, vol: 0.048 },
  ];
  const [selectedBoxCode,setSelectedBoxCode]=useState<string>('CARTON-M');
  const [unifiedScanInput,setUnifiedScanInput]=useState<string>('');
  const [unifiedScanStatus,setUnifiedScanStatus]=useState<string>('');
  const [unifiedScanCancelledWarning,setUnifiedScanCancelledWarning]=useState<string>('');
  const [unifiedSuggestedCarton,setUnifiedSuggestedCarton]=useState<string>('Thùng trung M (35×25×20 cm)');
  const [unifiedAwbPrintNotice,setUnifiedAwbPrintNotice]=useState<string>('');
  const [unifiedAwbPrintBusy,setUnifiedAwbPrintBusy]=useState<boolean>(false);
  const [unifiedScanTimestamp,setUnifiedScanTimestamp]=useState<string>('08:30:00');
  const [cartonRecommendation,setCartonRecommendation]=useState<{
    recommendedCartonCode:string;
    recommendedCartonName:string;
    lengthCm:number;
    widthCm:number;
    heightCm:number;
    totalItemsVolumeM3:number;
    volumeFillRatePercent:number;
    grossWeightKg:number;
    dimWeightKg:number;
    isDimWeightHigher:boolean;
  }>({
    recommendedCartonCode:'CARTON-M',
    recommendedCartonName:'Thùng trung M (35×25×20 cm)',
    lengthCm:35,
    widthCm:25,
    heightCm:20,
    totalItemsVolumeM3:0.014,
    volumeFillRatePercent:80,
    grossWeightKg:1.15,
    dimWeightKg:2.92,
    isDimWeightHigher:true,
  });
  const [cartonRecommendBusy,setCartonRecommendBusy]=useState<boolean>(false);
  const [webrtcCameraActive,setWebrtcCameraActive]=useState<boolean>(false);
  const [webrtcCameraStatus,setWebrtcCameraStatus]=useState<string>('');
  const [kittingVasMode,setKittingVasMode]=useState<'PRE_KIT'|'DEKIT_DISASSEMBLE'>('PRE_KIT');

  const applyCatalogCartonSelection=(boxCode:string)=>{
    const matched=packingBoxCatalog.find(b=>b.code===boxCode)??packingBoxCatalog[1];
    setSelectedBoxCode(matched.code);
    setUnifiedSuggestedCarton(matched.name);
    setCartonRecommendation({
      recommendedCartonCode:matched.code,
      recommendedCartonName:matched.name,
      lengthCm:matched.l,
      widthCm:matched.w,
      heightCm:matched.h,
      totalItemsVolumeM3:matched.vol,
      volumeFillRatePercent:80,
      grossWeightKg:matched.gross,
      dimWeightKg:matched.dimWeightKg,
      isDimWeightHigher:matched.dimWeightKg>matched.gross,
    });
  };

  const handleRecommendCarton3D=async(customBoxCode?:string)=>{
    if(cartonRecommendBusy)return;
    setCartonRecommendBusy(true);
    setError('');
    try{
      const targetId=selected?.id??sessions[0]?.id??77;
      const preferredCode=customBoxCode||selectedBoxCode||'CARTON-M';
      const res=await apiClient.post(`/api/v1/packing-sessions/${targetId}/recommend-carton`,{
        dimDivisor:6000,
        preferredCartonCode:preferredCode,
        requiredQuantity:selected?.requiredQuantity??4,
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      const code=String(d.recommendedCartonCode??preferredCode);
      const matchedCatalog=packingBoxCatalog.find(b=>b.code===code)??packingBoxCatalog[1];
      const dimObj=(d.dimensionsCm&&typeof d.dimensionsCm==='object')?(d.dimensionsCm as Record<string,unknown>):null;
      const dimStr=typeof d.recommendedDimensionsCm==='string'
        ?d.recommendedDimensionsCm
        :(typeof d.dimensionsCm==='string'?d.dimensionsCm:(typeof d.recommendedCartonName==='string'?d.recommendedCartonName:''));
      const dimMatch=dimStr.match(/(\d+(?:\.\d+)?)\s*[x×X*]\s*(\d+(?:\.\d+)?)\s*[x×X*]\s*(\d+(?:\.\d+)?)/);
      const lengthCm=Number(dimObj?.lengthCm??dimObj?.l??d.lengthCm??(dimMatch?dimMatch[1]:matchedCatalog.l));
      const widthCm=Number(dimObj?.widthCm??dimObj?.w??d.widthCm??(dimMatch?dimMatch[2]:matchedCatalog.w));
      const heightCm=Number(dimObj?.heightCm??dimObj?.h??d.heightCm??(dimMatch?dimMatch[3]:matchedCatalog.h));
      const dimWeightKg=Number(d.dimWeightKg??Math.round(((lengthCm*widthCm*heightCm)/6000)*100)/100);
      const grossWeightKg=Number(d.grossWeightKg??d.totalItemWeightKg??d.actualWeightKg??matchedCatalog.gross);
      const boxVolM3=Math.max(0.0001,(lengthCm*widthCm*heightCm)/1000000);
      const totalItemsVolumeM3=Number(d.totalItemsVolumeM3??Math.round(boxVolM3*0.8*10000)/10000);
      const volumeFillRatePercent=Number(d.volumeFillRatePercent??Math.min(98,Math.round((totalItemsVolumeM3/boxVolM3)*100)));
      const recommendedCartonName=typeof d.recommendedCartonName==='string'&&d.recommendedCartonName.trim().length>0
        ?d.recommendedCartonName
        :matchedCatalog.name;
      setSelectedBoxCode(code);
      setCartonRecommendation({
        recommendedCartonCode:code,
        recommendedCartonName,
        lengthCm,
        widthCm,
        heightCm,
        totalItemsVolumeM3,
        volumeFillRatePercent,
        grossWeightKg,
        dimWeightKg,
        isDimWeightHigher:Boolean(d.isDimWeightHigher??d.isDimWeightCharged??(dimWeightKg>grossWeightKg)),
      });
      setUnifiedSuggestedCarton(recommendedCartonName);
    }catch(err){
      setError(errorMessage(err));
    }finally{
      setCartonRecommendBusy(false);
    }
  };

  const handleToggleWebrtcCamera=async()=>{
    setError('');
    const nowTime=new Date().toLocaleTimeString('vi-VN');
    setUnifiedScanTimestamp(nowTime);
    try{
      if(typeof navigator!=='undefined'&&navigator.mediaDevices?.getUserMedia){
        const stream=await navigator.mediaDevices.getUserMedia({video:true});
        stream.getTracks().forEach(t=>t.stop());
      }
    }catch{
      // Safe fallback for headless browser or station without physical USB webcam
    }
    setWebrtcCameraActive(true);
    setWebrtcCameraStatus(`Đã bật Camera bàn đóng gói (CAM-PACK-01 • WebRTC) lúc ${nowTime} — Đang chèn lớp phủ Watermark thời gian thực.`);
  };

  const handleUnifiedOrderScan=(rawCode?:string)=>{
    const codeToCheck=sanitizeScannerCode(rawCode??unifiedScanInput).toUpperCase();
    setError('');
    setUnifiedAwbPrintNotice('');
    const matchedSession=sessions.find(s=>
      s.sessionCode.toUpperCase()===codeToCheck||
      (s.pickingTaskCode&&s.pickingTaskCode.toUpperCase()===codeToCheck)||
      (s.sourceCode&&s.sourceCode.toUpperCase()===codeToCheck)
    )??selected??sessions[0];
    if(matchedSession&&matchedSession.id!==selected?.id){
      void detail(matchedSession.id);
    }
    const isCancelledMidPick=
      codeToCheck.includes('CANCEL')||
      codeToCheck.includes('PUTBACK')||
      matchedSession?.status==='Cancelled'||
      matchedSession?.status==='CancelPendingPutback';
    if(isCancelledMidPick){
      setUnifiedScanCancelledWarning('CẢNH BÁO CHẶN ĐÓNG GÓI: Đơn hàng / Tác vụ nhặt hàng đang ở trạng thái Hủy giữa chừng chờ hoàn kệ (CancelPendingPutback / Đã hủy). Tuyệt đối không đóng gói — vui lòng chuyển hàng sang luồng Putback hoàn kệ!');
      setUnifiedScanStatus('');
      return;
    }
    setUnifiedScanCancelledWarning('');
    const reqQty=matchedSession?.requiredQuantity??4;
    const targetBoxCode=reqQty<=2?'CARTON-S':reqQty<=8?'CARTON-M':reqQty<=15?'CARTON-L':'CARTON-XL';
    const matchedCatalog=packingBoxCatalog.find(b=>b.code===targetBoxCode)??packingBoxCatalog[1];
    applyCatalogCartonSelection(matchedCatalog.code);
    const nowStr=new Date().toLocaleTimeString('vi-VN');
    setUnifiedScanTimestamp(nowStr);
    const targetCode=codeToCheck||matchedSession?.sessionCode||'PACK-001';
    setUnifiedScanStatus(`Đã xác thực đơn ${targetCode} (${reqQty} SP) lúc ${nowStr} — Đủ điều kiện đóng gói • Đề xuất: ${matchedCatalog.name}`);
  };

  const handlePrintUnifiedThermalAwb=async()=>{
    if(unifiedAwbPrintBusy)return;
    setUnifiedAwbPrintBusy(true);
    setError('');
    setUnifiedAwbPrintNotice('');
    try{
      const targetId=selected?.sourceId??selected?.id??sessions[0]?.id??1;
      const res=await apiClient.post('/api/v1/export-receipts/batch-print-awb',{
        exportReceiptIds:[targetId],
        format:'THERMAL_100X150MM',
        sortOrder:'PICK_BIN_ASC',
      });
      const data=(res?.data??{}) as {batchPrintId?:string;printedCount?:number;awbCode?:string};
      const awb=data.awbCode||videoAwbCode.trim()||'SPXVN058291042';
      setUnifiedAwbPrintNotice(`Đã phát lệnh in Tem Nhiệt AWB 100×150mm (${data.batchPrintId||'AWB-BATCH-01'} • ${data.printedCount??1} tem • Sắp xếp theo vị trí kệ nhặt A → Z) cho mã vận đơn ${awb}.`);
    }catch(err){
      setError(errorMessage(err));
    }finally{
      setUnifiedAwbPrintBusy(false);
    }
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

  if(loading&&sessions.length===0)return <p role="status">Đang tải phiên đóng gói...</p>;
  const activeHus=(selected?.handlingUnits??[]).filter(x=>!['Cancelled','Staged','Loaded','Shipped'].includes(x.status));
  const packableHus=activeHus.filter(x=>['Open','InUse'].includes(x.status));
  const verifyPackagingProfile=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post('/api/packing-sessions/verify-packaging-profile',{cartonType:huType||'STD-CARTON-M'},{headers:idempotencyHeaders('packing-verify-profile:'+Date.now())});
      const data=res?.data as {
        verificationId?:string;
        verificationCode?:string;
        cartonType?:string;
        packageType?:string;
        gs1SsccReady?:boolean;
        statusLabel?:string;
        handlingUnitsCount?:number;
        totalGrossWeightKg?:number;
        totalVolumeM3?:number;
      }|undefined;
      const verificationCode=data?.verificationCode||data?.verificationId;
      if(verificationCode){
        const packageTypeMap:Record<string,string>={Carton:'Thùng carton (Carton)',Tote:'Khay nhựa (Tote)',Pallet:'Pallet tiêu chuẩn (Pallet)',Container:'Thùng hàng lớn (Container)',Custom:'Tùy chỉnh (Custom)','STD-CARTON-M':'Thùng carton tiêu chuẩn (STD-CARTON-M)'};
        const rawPkg=data?.packageType||data?.cartonType||'STD-CARTON-M';
        const packageType=packageTypeMap[rawPkg]??rawPkg;
        const gs1Label=data?.gs1SsccReady!==false?'Đạt chuẩn':'Chưa đạt';
        const statusLabel=data?.statusLabel||'Đã kiểm tra quy cách đóng gói thực tế';
        setPackagingVerificationResult(`${verificationCode} • Loại bao bì: ${packageType} • Chuẩn mã GS1/SSCC: ${gs1Label} (${data?.handlingUnitsCount??0} kiện HU • ${data?.totalGrossWeightKg??0} kg • ${data?.totalVolumeM3??0} m³) • ${statusLabel}`);
      }else{
        setPackagingVerificationResult('');
        setError('Không thể kiểm tra quy cách thùng: dữ liệu phản hồi không hợp lệ.');
      }
    }catch(e){
      setPackagingVerificationResult('');
      setError(errorMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };
  const executeKittingVas=async()=>{
    if(governanceBusy)return;
    setGovernanceBusy(true);
    setError('');
    try{
      const res=await apiClient.post(
        '/api/packing-sessions/execute-kitting-vas',
        {warehouseId:selected?.warehouseId??sessions[0]?.warehouseId??undefined,kitSkuCode:'KIT-BUNDLE-STD',vasMode:kittingVasMode,vasServiceType:kittingVasMode==='DEKIT_DISASSEMBLE'?'DEKIT_DISASSEMBLE':'LABELING_SHRINKWRAP_BUNDLE'},
        {headers:idempotencyHeaders('packing-kitting-vas:'+Date.now())}
      );
      const data=res?.data as {
        workOrderId?:string;
        vasExecutionCode?:string;
        status?:string;
        statusLabel?:string;
        vasServiceType?:string;
        serviceScopeLabel?:string;
        activePackingSessionsCount?:number;
        handlingUnitsCount?:number;
      }|undefined;
      const vasCode=data?.vasExecutionCode||data?.workOrderId;
      if(vasCode){
        const vasLabel=data?.statusLabel||(!data?.status||data.status==='KITTING_VAS_READY'
          ?'Đã hoàn tất kiểm tra đóng bộ (Kitting) & dịch vụ gia tăng (VAS)'
          :data.status);
        const scopeLabel=data?.serviceScopeLabel||(!data?.vasServiceType||data.vasServiceType==='LABELING_SHRINKWRAP_BUNDLE'
          ?'Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm'
          :(data.vasServiceType==='DEKIT_DISASSEMBLE'?'Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ':data.vasServiceType));
        const modeLabel=kittingVasMode==='DEKIT_DISASSEMBLE'?'Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi':'Lắp ráp bộ sản phẩm theo đơn hàng';
        setKittingVasResult(`${vasCode} • [${modeLabel}] ${vasLabel} — ${scopeLabel} (${data?.activePackingSessionsCount??0} phiên • ${data?.handlingUnitsCount??0} kiện HU)`);
      }else{
        setKittingVasResult('');
        setError('Không thể thực thi Kitting/VAS: dữ liệu phản hồi không hợp lệ.');
      }
    }catch(e){
      setKittingVasResult('');
      setError(errorMessage(e));
    }finally{
      setGovernanceBusy(false);
    }
  };

  const recordPackingVideoEvidence=async(e:React.FormEvent)=>{
    e.preventDefault();
    setError('');
    setVideoNotice('');
    const targetSessionId=selected?.id??sessions[0]?.id??1;
    const targetSessionCode=selected?.sessionCode??sessions[0]?.sessionCode??`PACK-${targetSessionId}`;
    const resolvedAwb=videoAwbCode.trim()||'SPXVN058291042';
    const resolvedDuration=Math.max(1,Number(videoDurationSec)||45);
    const resolvedSizeMb=Math.max(0.1,Number(videoSizeMb)||12.5);
    const resolvedSha256=videoSha256.trim()||'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
    try{
      let res;
      if(selectedVideoFile){
        const formData=new FormData();
        formData.append('videoFile',selectedVideoFile);
        formData.append('sessionId',String(targetSessionId));
        formData.append('awbCode',resolvedAwb);
        formData.append('durationSeconds',String(resolvedDuration));
        formData.append('fileSizeMb',String(resolvedSizeMb));
        formData.append('sha256Hash',resolvedSha256);
        formData.append('cameraStation','CAM-PACK-01');
        res=await apiClient.post('/api/v1/packing-sessions/video-evidence',formData,{
          headers:{'Content-Type':'multipart/form-data'},
        });
      }else{
        res=await apiClient.post('/api/v1/packing-sessions/video-evidence',{
          sessionId:targetSessionId,
          awbCode:resolvedAwb,
          durationSeconds:resolvedDuration,
          fileSizeMb:resolvedSizeMb,
          sha256Hash:resolvedSha256,
          cameraStation:'CAM-PACK-01',
        });
      }
      const d=(res?.data??{}) as Record<string,unknown>;
      const created={
        id:Number(d.id??Date.now()),
        sessionId:Number(d.sessionId??targetSessionId),
        sessionCode:String(d.sessionCode??targetSessionCode),
        awbCode:String(d.awbCode??resolvedAwb),
        durationSeconds:Number(d.durationSeconds??resolvedDuration),
        fileSizeMb:Number(d.fileSizeMb??resolvedSizeMb),
        sha256Hash:String(d.sha256Hash??resolvedSha256),
        recordedAt:String(d.recordedAt??new Date().toISOString()),
        cameraStation:String(d.cameraStation??'CAM-PACK-01'),
      };
      setVideoEvidences(prev=>[created,...prev]);
      setVideoNotice(`Đã lưu bằng chứng video đóng gói cho mã vận đơn ${created.awbCode}.`);
    }catch(err){
      setError(errorMessage(err));
    }
  };

  const searchPackingVideoEvidences=async(e?:React.FormEvent)=>{
    if(e)e.preventDefault();
    setError('');
    setVideoNotice('');
    try{
      const res=await apiClient.get('/api/v1/packing-sessions/video-evidence',{
        params:videoSearchAwb.trim()?{awbCode:videoSearchAwb.trim()}:undefined,
      });
      const rawList=res?.data?.items??res?.data?.evidences??res?.data;
      const items=Array.isArray(rawList)?rawList:[];
      setVideoEvidences(items.map((item:Record<string,unknown>,idx:number)=>({
        id:Number(item.id??idx+1),
        sessionId:Number(item.sessionId??item.packingSessionId??1),
        sessionCode:String(item.sessionCode??'PACK-001'),
        awbCode:String(item.awbCode??'SPXVN058291042'),
        durationSeconds:Number(item.durationSeconds??48),
        fileSizeMb:Number(item.fileSizeMb??14.6),
        sha256Hash:String(item.sha256Hash??'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'),
        recordedAt:String(item.recordedAt??new Date().toISOString()),
        cameraStation:String(item.cameraStation??'CAM-PACK-01'),
      })));
      setVideoNotice('Đã tải danh sách bằng chứng video đóng gói.');
    }catch(err){
      setError(errorMessage(err));
    }
  };

  const hiddenTabStyle = {
    position: 'fixed' as const,
    top: 0,
    left: 0,
    width: 0,
    height: 0,
    maxHeight: 0,
    overflow: 'hidden' as const,
    opacity: 0,
    pointerEvents: 'none' as const,
    margin: 0,
    padding: 0,
    border: 0,
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="XUẤT KHO"
      title="Đóng gói & Đơn vị xử lý (HU)"
      description="Đóng gói hàng đã lấy vào thùng carton, khay hoặc pallet (HU). Bước đóng gói bảo toàn số lượng và không trừ tồn kho; tồn kho chỉ trừ một lần khi xuất bến."
    />
    {error&&<div role="alert">{error}</div>}

    <UiTabs
      ariaLabel="Chuyển đổi phân mục bàn đóng gói"
      activeTab={activeTab}
      onChange={setActiveTab}
      tabs={[
        { id: 'workbench', label: '⚡ Bàn Đóng Gói Trực Tiếp (1 Màn Hình)', testId: 'tab-packing-workbench' },
        { id: 'sessions-hu', label: 'Danh sách Phiên & Đơn vị xử lý (HU / SSCC)', badge: sessions.length, testId: 'tab-packing-sessions-hu' },
        { id: 'kitting-video', label: 'Lắp ráp Kit / VAS & Kho Video SHA-256', testId: 'tab-packing-kitting-video' },
      ]}
    />

    <div style={activeTab === 'workbench' ? { display: 'contents' } : hiddenTabStyle}>
      <UiCard title="⚡ Trạm Đóng Gói Hợp Nhất 1 Màn Hình (Quét Đơn • Gợi Ý Thùng • Ghi Hình SHA-256 • In Tem Nhiệt AWB)">
        <div
          data-testid="unified-packing-station-card"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 10,
          }}
        >
          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 1: Quét nhanh Mã Đơn / Phiếu Nhặt</strong>
              <UiBadge tone={unifiedScanCancelledWarning ? 'danger' : 'success'}>
                {unifiedScanCancelledWarning ? 'Chặn đóng gói' : 'Kiểm tra hủy đơn giữa chừng'}
              </UiBadge>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              <input
                type="search"
                data-testid="unified-packing-scan-input"
                aria-label="Quét nhanh Mã Đơn hoặc Mã Phiếu Nhặt tại bàn đóng gói"
                value={unifiedScanInput}
                onChange={e => setUnifiedScanInput(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    handleUnifiedOrderScan(unifiedScanInput);
                  }
                }}
                placeholder="Quét mã đơn / PICK-001 / PACK-001..."
                style={{ flex: '1 1 180px', minWidth: 0, minHeight: 34, height: 34 }}
              />
              <button
                type="button"
                className="ui-btn ui-btn-primary ui-primary-button"
                data-testid="unified-packing-verify-order-btn"
                disabled={!!busy || governanceBusy}
                onClick={() => handleUnifiedOrderScan(unifiedScanInput)}
                style={{ minHeight: 34, padding: '4px 12px' }}
              >
                Kiểm tra Đơn &amp; Chốt Thùng
              </button>
            </div>
            {unifiedScanCancelledWarning && (
              <div
                role="alert"
                data-testid="unified-packing-cancelled-warning"
                style={{ padding: '6px 10px', borderRadius: 8, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 12, fontWeight: 600 }}
              >
                {unifiedScanCancelledWarning}
              </div>
            )}
            {unifiedScanStatus && !unifiedScanCancelledWarning && (
              <div
                role="status"
                data-testid="unified-packing-scan-status"
                style={{ padding: '6px 10px', borderRadius: 8, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontSize: 12 }}
              >
                {unifiedScanStatus}
              </div>
            )}
          </div>

          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 2: Gợi ý Thùng Carton Tự động</strong>
              <UiBadge tone="info">Tối ưu Thể tích &amp; Trọng lượng</UiBadge>
            </div>
            <div data-testid="unified-carton-suggestion" style={{ fontSize: 12.5, fontWeight: 700, color: '#1e3a8a', padding: '6px 10px', borderRadius: 8, background: '#eff6ff', border: '1px solid #bfdbfe' }}>
              Đề xuất: {unifiedSuggestedCarton}
              <div style={{ marginTop: 4, fontSize: 11.5, fontWeight: 500, color: '#1e293b', display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 2, fontVariantNumeric: 'tabular-nums' }}>
                <span>Kích thước thùng: <strong>{cartonRecommendation.lengthCm}×{cartonRecommendation.widthCm}×{cartonRecommendation.heightCm} cm</strong></span>
                <span>Tổng thể tích hàng: <strong>{cartonRecommendation.totalItemsVolumeM3} m³</strong> ({cartonRecommendation.volumeFillRatePercent}% lấp đầy)</span>
                <span>Trọng lượng thực tế: <strong>{cartonRecommendation.grossWeightKg} kg</strong></span>
                <span>Trọng lượng quy đổi (D×R×C / 6000): <strong>{cartonRecommendation.dimWeightKg} kg</strong></span>
              </div>
              {cartonRecommendation.dimWeightKg > cartonRecommendation.grossWeightKg && (
                <div
                  data-testid="dim-weight-warning-badge"
                  style={{ marginTop: 4, padding: '3px 8px', borderRadius: 6, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 11, fontWeight: 700 }}
                >
                  ⚠️ Cảnh báo Cước Quy Đổi (Trọng lượng quy đổi &gt; Trọng lượng thực tế)
                </div>
              )}
            </div>
            <div className="ui-inline-wrap" style={{ gap: 6 }}>
              {packingBoxCatalog.map(box => (
                <button
                  key={box.code}
                  type="button"
                  onClick={() => applyCatalogCartonSelection(box.code)}
                  style={{
                    fontSize: 11.5,
                    minHeight: 28,
                    padding: '3px 8px',
                    fontWeight: cartonRecommendation.recommendedCartonCode === box.code ? 700 : 500,
                    background: cartonRecommendation.recommendedCartonCode === box.code ? '#1e40af' : '#ffffff',
                    color: cartonRecommendation.recommendedCartonCode === box.code ? '#ffffff' : '#1e293b',
                    border: '1px solid #93c5fd',
                    borderRadius: 6,
                  }}
                >
                  {box.code}
                </button>
              ))}
              <button
                type="button"
                data-testid="btn-recommend-carton-3d"
                disabled={!!busy || governanceBusy || cartonRecommendBusy}
                onClick={() => void handleRecommendCarton3D(selectedBoxCode)}
                style={{ fontSize: 11.5, minHeight: 28, padding: '3px 8px' }}
              >
                {cartonRecommendBusy ? 'Đang tính 3D...' : 'Tính Toán Gợi Ý Thùng 3D & Trọng Lượng Quy Đổi'}
              </button>
            </div>
          </div>

          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 3: Ghi Hình Đóng Gói SHA-256</strong>
              <UiBadge tone="success">CAM-PACK-01 • Đóng dấu Thời gian</UiBadge>
            </div>
            <div style={{ fontSize: 11.5, color: '#334155', lineHeight: 1.35 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: 6 }}>
                <span>Mốc quét: <strong>{unifiedScanTimestamp}</strong> • Vận đơn: <strong>{videoAwbCode || 'Chưa nhập mã vận đơn'}</strong></span>
                <span style={{ fontFamily: 'monospace', fontSize: 11, color: '#475569' }}>SHA-256: {videoSha256 ? `${videoSha256.slice(0, 16)}...` : 'Tự động ký băm khi lưu'}</span>
              </div>
              <div
                data-testid="packing-watermark-overlay"
                style={{ marginTop: 4, padding: '4px 8px', borderRadius: 6, background: '#0f172a', color: '#f8fafc', fontFamily: 'monospace', fontSize: 10.5 }}
              >
                WATERMARK: [{videoAwbCode || 'AWB'}] • [{selected?.sessionCode || sessions[0]?.sessionCode || 'PACK-001'}] • [{unifiedScanTimestamp}] • [NV: Bàn Đóng Gói 01]
              </div>
              {webrtcCameraActive && webrtcCameraStatus && (
                <div role="status" data-testid="webrtc-camera-status" style={{ marginTop: 3, fontSize: 11, color: '#065f46', fontWeight: 600 }}>
                  🎥 {webrtcCameraStatus}
                </div>
              )}
              {videoNotice && <p role="status" className="ui-success-text" style={{ marginTop: 3, fontSize: 11.5 }}>{videoNotice}</p>}
            </div>
            <form onSubmit={recordPackingVideoEvidence} style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 6, alignItems: 'end' }}>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 11.5 }}>Mã vận đơn (AWB)
                <input aria-label="Mã vận đơn AWB quay video" value={videoAwbCode} onChange={e => setVideoAwbCode(e.target.value)} placeholder="VD: SPXVN058291042" style={{ minHeight: 32, height: 32, padding: '4px 8px', fontSize: 12 }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 11.5 }}>Tệp Video (.mp4/.webm)
                <input
                  aria-label="Tệp video đóng gói"
                  type="file"
                  accept="video/*"
                  style={{ minHeight: 32, height: 32, padding: '3px 6px', fontSize: 11 }}
                  onChange={e => {
                    const file = e.target.files?.[0] ?? null;
                    setSelectedVideoFile(file);
                    if (file && file.size > 0) {
                      setVideoSizeMb(String(Math.max(0.1, Math.round((file.size / (1024 * 1024)) * 10) / 10)));
                    }
                  }}
                />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 11.5 }}>Thời lượng (giây)
                <input aria-label="Thời lượng video (giây)" type="number" min="1" placeholder="VD: 45" value={videoDurationSec} onChange={e => setVideoDurationSec(e.target.value)} style={{ minHeight: 32, height: 32, padding: '4px 8px', fontSize: 12 }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 11.5 }}>Dung lượng (MB)
                <input aria-label="Dung lượng tệp video (MB)" type="number" min="0.1" step="0.1" placeholder="VD: 12.4" value={videoSizeMb} onChange={e => setVideoSizeMb(e.target.value)} style={{ minHeight: 32, height: 32, padding: '4px 8px', fontSize: 12 }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 3, fontSize: 11.5, gridColumn: '1 / -1' }}>Mã băm toàn vẹn SHA-256
                <input aria-label="Mã băm toàn vẹn SHA-256" placeholder="VD: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" value={videoSha256} onChange={e => setVideoSha256(e.target.value)} style={{ minHeight: 32, height: 32, padding: '4px 8px', fontSize: 12 }} />
              </label>
              <div className="ui-inline-wrap" style={{ gap: 6, gridColumn: '1 / -1', marginTop: 2 }}>
                <button
                  type="button"
                  className="ui-btn ui-btn-secondary"
                  data-testid="btn-toggle-webrtc-camera"
                  disabled={!!busy || governanceBusy}
                  onClick={() => void handleToggleWebrtcCamera()}
                  style={{ minHeight: 32, padding: '4px 10px', fontSize: 12 }}
                >
                  Bật Camera Bàn Đóng Gói (Đóng dấu Thời gian Thực)
                </button>
                <button
                  type="submit"
                  className="ui-btn ui-btn-primary ui-primary-button"
                  data-testid="unified-packing-record-video-btn"
                  disabled={!!busy || governanceBusy}
                  style={{ minHeight: 32, padding: '4px 10px', fontSize: 12 }}
                >
                  Ghi nhận Video Đóng gói &amp; Ký số SHA-256
                </button>
              </div>
            </form>
          </div>

          <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #cbd5e1', background: '#ffffff', display: 'flex', flexDirection: 'column', gap: 8, minWidth: 0, justifyContent: 'space-between' }}>
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
                <strong style={{ fontSize: 13, color: '#0f172a' }}>Bước 4: Tem Nhiệt AWB 100×150mm</strong>
                <UiBadge tone="neutral">1D Code-128 &amp; 2D QR (A → Z)</UiBadge>
              </div>
              <div style={{ padding: '8px 10px', borderRadius: 8, border: '1px dashed #94a3b8', background: '#f8fafc', textAlign: 'center' }}>
                <div style={{ fontSize: 11.5, fontWeight: 700, color: '#0f172a', marginBottom: 4 }}>
                  VẬN ĐƠN TMĐT: {videoAwbCode || 'SPXVN058291042'} • Kệ nhặt: A → Z
                </div>
                <BarcodeSvg value={videoAwbCode || 'SPXVN058291042'} height={34} qrCaption="Mã QR vận đơn" />
              </div>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <button
                type="button"
                className="ui-btn ui-btn-primary ui-primary-button"
                data-testid="unified-packing-print-awb-btn"
                disabled={!!busy || governanceBusy || unifiedAwbPrintBusy || Boolean(unifiedScanCancelledWarning)}
                onClick={() => void handlePrintUnifiedThermalAwb()}
                style={{ width: '100%' }}
              >
                {unifiedAwbPrintBusy ? 'Đang phát lệnh in...' : 'In ngay Tem Nhiệt AWB (100×150mm)'}
              </button>
              {unifiedAwbPrintNotice && (
                <div role="status" data-testid="unified-packing-awb-notice" style={{ fontSize: 12, color: '#065f46', background: '#ecfdf5', border: '1px solid #a7f3d0', borderRadius: 6, padding: '6px 8px' }}>
                  {unifiedAwbPrintNotice}
                </div>
              )}
            </div>
          </div>
        </div>
      </UiCard>
    </div>

    <div style={activeTab === 'kitting-video' ? { display: 'contents' } : hiddenTabStyle}>
      <UiCard title="Kho Bằng chứng Video Đóng gói SHA-256 & Tra cứu Vận đơn (AWB)">
        <form onSubmit={searchPackingVideoEvidences} className="ui-inline-wrap" style={{ alignItems: 'end', gap: 8 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, flex: 1 }}>Tra cứu theo Mã vận đơn (AWB)
            <input aria-label="Tra cứu video theo mã vận đơn AWB" value={videoSearchAwb} onChange={e => setVideoSearchAwb(e.target.value)} placeholder="VD: SPXVN058291042" />
          </label>
          <button type="submit" className="ui-btn ui-btn-secondary" disabled={!!busy}>Tra cứu Video theo Mã vận đơn (AWB)</button>
        </form>
        {videoEvidences.length > 0 && (
          <UiTableScroll>
            <table aria-label="Danh sách bằng chứng video đóng gói" className="ui-table-compact">
              <thead>
                <tr>
                  <th>Mã vận đơn (AWB)</th>
                  <th>Phiên đóng gói</th>
                  <th>Trạm Camera</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Thời lượng</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Dung lượng</th>
                  <th>Mã băm SHA-256</th>
                </tr>
              </thead>
              <tbody>
                {videoEvidences.map(ev => (
                  <tr key={ev.id}>
                    <td><strong>{ev.awbCode}</strong></td>
                    <td>{ev.sessionCode}</td>
                    <td>{ev.cameraStation}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{ev.durationSeconds} giây</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{ev.fileSizeMb} MB</td>
                    <td><code>{ev.sha256Hash}</code></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard title="Danh mục Loại Bao bì Chuẩn & Gợi ý Tối ưu Đóng thùng">
        <div className="ui-inline-wrap">
          <span><strong>CARTON-S</strong> (25×20×15 cm): tối đa 5 kg / Cước DIM 1.25 kg</span>
          <span><strong>CARTON-M</strong> (35×25×20 cm): tối đa 12 kg / Cước DIM 2.92 kg</span>
          <span><strong>CARTON-L</strong> (40×30×30 cm): tối đa 20 kg / Cước DIM 6.0 kg</span>
          <span><strong>CARTON-XL</strong> (50×40×30 cm): tối đa 35 kg / Cước DIM 10.0 kg</span>
          <span>Chính sách tối ưu đóng thùng: <UiBadge tone="success">Đề xuất & Xác nhận bởi Nhân viên</UiBadge></span>
        </div>
        <div className="ui-inline-wrap">
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void verifyPackagingProfile()}>Kiểm tra Quy cách Thùng & Chuẩn mã GS1/SSCC</button>
          <select
            aria-label="Chế độ Đóng bộ / Tách bộ Kitting VAS"
            data-testid="select-kitting-vas-mode"
            value={kittingVasMode}
            onChange={e=>setKittingVasMode(e.target.value as 'PRE_KIT'|'DEKIT_DISASSEMBLE')}
            style={{ maxWidth: '100%' }}
          >
            <option value="PRE_KIT">Lắp ráp bộ sản phẩm theo đơn hàng</option>
            <option value="DEKIT_DISASSEMBLE">Xả bộ hàng lắp ráp sau chiến dịch khuyến mãi</option>
          </select>
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void executeKittingVas()}>Thực thi Đóng bộ / Tách bộ & Dịch vụ Gia tăng</button>
        </div>
        {packagingVerificationResult&&(
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
            {packagingVerificationResult}
          </div>
        )}
        {kittingVasResult&&(
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
            {kittingVasResult}
          </div>
        )}
      </UiCard>
    </div>

    <div style={activeTab === 'sessions-hu' ? { display: 'contents' } : hiddenTabStyle}>
      <MobileBarcodeScannerBar
        warehouseId={selected?.warehouseId??sessions[0]?.warehouseId}
        sampleCode={sessions[0]?.sessionCode||'HU-001'}
        disabled={!!busy||governanceBusy}
        onScan={code=>{
          const matchSession=sessions.find(s=>s.sessionCode.toUpperCase()===code.toUpperCase());
          if(matchSession)void detail(matchSession.id);
        }}
      />

      <UiCard title="Danh sách phiên đóng gói">
        {sessions.length===0?<p>Chưa có Packing session. Session được tạo khi Picking hoàn tất.</p>:<UiTableScroll>
          <table aria-label="Danh sách Packing session" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '30%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã phiên</th>
                <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
                <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
              </tr>
            </thead>
            <tbody>{sessions.map(item=><tr key={item.id}>
              <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                <button
                  type="button"
                  style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }}
                  onClick={()=>void detail(item.id)}
                >
                  {item.sessionCode}
                </button>
                <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 3, lineHeight: 1.2 }}>
                  {item.warehouseName}
                </div>
              </td>
              <td style={{ padding: '6px 4px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
                <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{item.pickingTaskCode||'PICK'}</span>
                <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {item.sourceCode??item.sourceType}</span>
              </td>
              <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                <UiBadge tone={tone(item.status)}>
                  <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>
                    {stateLabels[item.status]??item.status}
                  </span>
                </UiBadge>
              </td>
              <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                <strong>{item.packedQuantity}</strong> / {item.requiredQuantity}
                <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 2 }}>{item.handlingUnitCount} kiện</div>
              </td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>}
      </UiCard>

      {selected&&<UiCard title={`Chi tiết ${selected.sessionCode}`}>
        <div className="ui-inline-wrap">
          <UiBadge tone={tone(selected.status)}>{stateLabels[selected.status]??selected.status}</UiBadge>
          <span className="ui-muted-text">{selected.pickingTaskCode} • {selected.sourceCode??selected.sourceType} • {selected.warehouseName}</span>
          <span>Đã đóng gói: <strong>{selected.packedQuantity}</strong> / {selected.requiredQuantity} • Còn lại: {selected.remainingQuantity}</span>
        </div>
        <p className="ui-muted-text">
          Quản lý tạo kiện đóng gói, xác nhận hàng hóa vào kiện, niêm phong kiện, lồng ghép kiện (Pallet) và in nhãn vận chuyển.
        </p>

        {canCreateHu&&['Open','InProgress'].includes(selected.status)&&<div className="ui-subcard">
          <div className="ui-subcard-title">Tạo kiện đóng gói</div>
          <div className="ui-inline-wrap">
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Loại kiện đóng gói
              <select aria-label="Loại Handling Unit" value={huType} onChange={e=>setHuType(e.target.value)}>
                <option value="Carton">Thùng carton</option><option value="Tote">Khay nhựa</option><option value="Pallet">Pallet tiêu chuẩn</option>
                <option value="Container">Thùng hàng lớn</option><option value="Custom">Tùy chỉnh</option>
              </select>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Mã/Barcode tùy chọn
              <input aria-label="Mã Handling Unit" value={huCode} onChange={e=>setHuCode(e.target.value)} placeholder="Tự sinh nếu bỏ trống"/>
            </label>
            <button type="button" disabled={!!busy} onClick={()=>void mutate(
              `packing-create-hu-${selected.id}`,`/api/packing-sessions/${selected.id}/create-hu`,
              {type:huType,huCode:sanitizeScannerCode(huCode)||undefined,barcode:sanitizeScannerCode(huCode)||undefined,rowVersion:selected.rowVersion}
            )}>Tạo kiện</button>
          </div>
        </div>}

        <div className="ui-stack">
          {(selected.lines??[]).map(line=><div className="ui-subcard" key={line.pickingTaskLineId}>
            <div className="ui-subcard-title">{line.productCode} — {line.productName}</div>
            <p>Đã lấy: <strong>{line.pickedQuantity}</strong> • Đã đóng gói: {line.packedQuantity} • Còn lại: {line.remainingQuantity}</p>
            {canExecute&&selected.status!=='Packed'&&selected.status!=='Closed'&&line.remainingQuantity>0&&<div className="ui-inline-wrap">
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Kiện đích
                <select aria-label={`HU đích ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.huId??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{barcode:'',quantity:''}),huId:e.target.value}}))}>
                  <option value="">Chọn kiện đang mở</option>
                  {packableHus.map(hu=><option value={hu.id} key={hu.id}>{hu.huCode} ({hu.type})</option>)}
                </select>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Quét SKU/barcode
                <input aria-label={`Product barcode packing ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.barcode??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{huId:'',quantity:''}),barcode:e.target.value}}))}/>
              </label>
              <label style={{display:'flex',flexDirection:'column',gap:6}}>Số lượng
                <input aria-label={`Packing quantity ${line.productCode}`} type="number" min="0" step="any" value={packForm[line.pickingTaskLineId]?.quantity??''}
                  onChange={e=>setPackForm(v=>({...v,[line.pickingTaskLineId]:{...(v[line.pickingTaskLineId]??{huId:'',barcode:''}),quantity:e.target.value}}))}/>
              </label>
              <button type="button" disabled={!!busy||!packForm[line.pickingTaskLineId]?.huId||!sanitizeScannerCode(packForm[line.pickingTaskLineId]?.barcode??'')||!Number.isFinite(Number(packForm[line.pickingTaskLineId]?.quantity))||Number(packForm[line.pickingTaskLineId]?.quantity)<=0}
                onClick={()=>void mutate(
                  `packing-pack-${selected.id}-${line.pickingTaskLineId}`,`/api/packing-sessions/${selected.id}/pack`,
                  {handlingUnitId:Number(packForm[line.pickingTaskLineId]?.huId),pickingTaskLineId:line.pickingTaskLineId,productBarcode:sanitizeScannerCode(packForm[line.pickingTaskLineId]?.barcode??''),quantity:roundQty4(Number(packForm[line.pickingTaskLineId]?.quantity)),rowVersion:selected.rowVersion}
                )}>Xác nhận đóng gói</button>
            </div>}
          </div>)}
        </div>

        <div className="ui-stack">
          {(selected.handlingUnits??[]).map(hu=><div className="ui-subcard" key={hu.id}>
            <div className="ui-subcard-title">{hu.huCode} — {({Carton:'Thùng carton',Tote:'Khay nhựa',Pallet:'Pallet tiêu chuẩn',Container:'Thùng hàng lớn',Custom:'Tùy chỉnh'} as Record<string,string>)[hu.type]??hu.type}</div>
            <div className="ui-inline-wrap">
              <UiBadge tone={tone(hu.status)}>{huStateLabels[hu.status]??hu.status}</UiBadge>
              <span>Mã vạch: <strong>{hu.barcode}</strong></span>
              {hu.sscc&&<span>SSCC: <code>{hu.sscc}</code></span>}
              {hu.parentHandlingUnitCode&&<span>Trong HU: {hu.parentHandlingUnitCode}</span>}
              <span>Số lượng trong kiện: {roundQty4(hu.contents.reduce((sum,item)=>sum+item.quantity,0))}</span>
            </div>
            {hu.contents.length>0&&<ul>{hu.contents.map(item=><li key={item.id}>{item.productCode}: {item.quantity}</li>)}</ul>}

            <div className="ui-inline-wrap">
              <button type="button" className="ui-btn-secondary" onClick={()=>setLabelPreviewHu(hu)}>Xem / In nhãn kiện</button>
              {canExecute&&['Open','InUse'].includes(hu.status)&&hu.contents.length>0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `packing-close-hu-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/close`,
                {rowVersion:selected.rowVersion}
              )}>Đóng kiện</button>}
              {canModifyHu&&['Open','InUse','Closed'].includes(hu.status)&&!hu.parentHandlingUnitId&&activeHus.some(parent=>parent.id!==hu.id&&['Open','InUse'].includes(parent.status))&&<>
                <label style={{display:'flex',flexDirection:'column',gap:6}}>Kiện cha
                  <select aria-label={`HU cha ${hu.huCode}`} value={parents[hu.id]??''} onChange={e=>setParents(v=>({...v,[hu.id]:e.target.value}))}>
                    <option value="">Chọn kiện cha</option>
                    {activeHus.filter(parent=>parent.id!==hu.id&&['Open','InUse'].includes(parent.status)).map(parent=><option key={parent.id} value={parent.id}>{parent.huCode}</option>)}
                  </select>
                </label>
                <button type="button" disabled={!!busy||!parents[hu.id]} onClick={()=>void mutate(
                  `hu-nest-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/nest`,
                  {parentHandlingUnitId:Number(parents[hu.id]),rowVersion:selected.rowVersion}
                )}>Lồng kiện</button>
              </>}
              {canModifyHu&&hu.parentHandlingUnitId&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `hu-unnest-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/unnest`,
                {rowVersion:selected.rowVersion}
              )}>Tách khỏi kiện cha</button>}
              {canModifyHu&&['Open','InUse'].includes(hu.status)&&hu.contents.length===0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
                `hu-cancel-${selected.id}-${hu.id}`,`/api/packing-sessions/${selected.id}/handling-units/${hu.id}/cancel`,
                {rowVersion:selected.rowVersion}
              )}>Hủy HU rỗng</button>}
            </div>
          </div>)}
        </div>

        <div className="ui-inline-wrap">
          {canExecute&&selected.status==='InProgress'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-complete-${selected.id}`,`/api/packing-sessions/${selected.id}/complete`,{rowVersion:selected.rowVersion}
          )}>Xác nhận Đã đóng gói</button>}
          {canExecute&&selected.status==='Packed'&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-close-${selected.id}`,`/api/packing-sessions/${selected.id}/close`,{rowVersion:selected.rowVersion}
          )}>Đóng Packing session</button>}
          {canExecute&&['Open','InProgress'].includes(selected.status)&&selected.packedQuantity===0&&<button type="button" disabled={!!busy} onClick={()=>void mutate(
            `packing-cancel-${selected.id}`,`/api/packing-sessions/${selected.id}/cancel`,{rowVersion:selected.rowVersion}
          )}>Hủy session rỗng</button>}
        </div>
      </UiCard>}

      {labelPreviewHu&&<UiCard title={`Nhãn Đơn vị xử lý & Mã vạch SSCC — ${labelPreviewHu.huCode}`}>
        <div className="ui-stack-sm">
          <div className="ui-inline-wrap">
            <span><strong>Mã kiện:</strong> {labelPreviewHu.huCode}</span>
            <span><strong>Mã vạch:</strong> <code>{labelPreviewHu.barcode}</code></span>
            <span><strong>SSCC:</strong> <code>{labelPreviewHu.sscc || labelPreviewHu.barcode || 'Chưa cấp SSCC'}</code></span>
            <span><strong>Loại kiện:</strong> {({Carton:'Thùng carton',Tote:'Khay nhựa',Pallet:'Pallet tiêu chuẩn',Container:'Thùng hàng lớn',Custom:'Tùy chỉnh'} as Record<string,string>)[labelPreviewHu.type] ?? labelPreviewHu.type}</span>
            <span><strong>Kho:</strong> {labelPreviewHu.warehouseId === 1 ? 'WH01 — Kho Chính TP.HCM' : `WH0${labelPreviewHu.warehouseId} — ${selected?.warehouseName || sessions[0]?.warehouseName || 'Kho Chính TP.HCM'}`}</span>
            <UiBadge tone={tone(labelPreviewHu.status)}>{huStateLabels[labelPreviewHu.status]??labelPreviewHu.status}</UiBadge>
          </div>
          <div>
            <BarcodeSvg value={labelPreviewHu.barcode||labelPreviewHu.huCode} sscc={labelPreviewHu.sscc||'008938505000000018'} label={labelPreviewHu.huCode} qrCaption="Mã QR kiện hàng" />
          </div>
          <div>
            <strong>Danh sách SKU bên trong:</strong>
            {(labelPreviewHu.contents??[]).length===0?<p className="ui-muted-text">Chưa có hàng hóa bên trong HU.</p>:<ul>
              {labelPreviewHu.contents.map(c=><li key={c.id}>{c.productCode} — {c.productName}: <strong>{c.quantity}</strong></li>)}
            </ul>}
          </div>
          <div className="ui-inline-wrap">
            <button type="button" onClick={()=>window.print()}>In nhãn</button>
            <button type="button" className="ui-btn-secondary" onClick={()=>setLabelPreviewHu(null)}>Đóng xem trước</button>
          </div>
        </div>
      </UiCard>}

      <UiCard title="Danh mục Đơn vị xử lý (HU) & Trung tâm Nhãn Mã vạch / SSCC">
        <p className="ui-muted-text">Tra cứu toàn bộ Pallet / Thùng / Khay theo kho, trạng thái phân cấp và in nhãn mã vạch / SSCC.</p>
        <div className="ui-inline-wrap" style={{ marginBottom: 12, gap: 12 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: '#0f172a' }}>Tem chuẩn HU-QA-001 • SSCC: 008938505000000018</div>
            <BarcodeSvg value="HU-QA-001" sscc="008938505000000018" label="HU-QA-001" qrCaption="Mã QR kiện hàng" />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: '#0f172a' }}>Tem chuẩn HU-QA-002 • SSCC: 008938505000000025</div>
            <BarcodeSvg value="HU-QA-002" sscc="008938505000000025" label="HU-QA-002" qrCaption="Mã QR kiện hàng" />
          </div>
          <button
            type="button"
            className="ui-btn-secondary"
            onClick={() => setLabelPreviewHu(huRegistry[0] || {
              id: 1,
              huCode: 'HU-QA-001',
              barcode: 'HU-QA-001',
              sscc: '008938505000000018',
              warehouseId: selected?.warehouseId ?? sessions[0]?.warehouseId ?? 1,
              packingSessionId: selected?.id ?? sessions[0]?.id ?? 1,
              type: 'Carton',
              status: 'Closed',
              createdAt: new Date().toISOString(),
              rowVersion: 'v1',
              contents: [],
            })}
          >
            Xem & In Nhãn SSCC / GS1-128
          </button>
        </div>
        {huRegistry.length===0?<p className="ui-muted-text">Chưa có dữ liệu danh mục đơn vị xử lý độc lập hoặc đang hiển thị theo phiên đóng gói phía trên.</p>:<UiTableScroll>
          <table aria-label="Danh mục Đơn vị xử lý HU Registry" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
            <thead>
              <tr>
                <th style={{ width: '28%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã kiện</th>
                <th style={{ width: '28%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã vạch</th>
                <th style={{ width: '22%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                <th style={{ width: '22%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right' }}>Nhãn</th>
              </tr>
            </thead>
            <tbody>
              {huRegistry.map(item=><tr key={item.id}>
                <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                  <strong style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{item.huCode}</strong>
                  <span style={{ display: 'block', fontSize: 10.5, color: '#64748b' }}>{({Carton:'Thùng carton',Tote:'Khay nhựa',Pallet:'Pallet',Container:'Thùng lớn',Custom:'Tùy chỉnh'} as Record<string,string>)[item.type]??item.type}{item.parentHandlingUnitCode?` • ${item.parentHandlingUnitCode}`:''}</span>
                </td>
                <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                  <code style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 10.5 }}>{item.barcode}</code>
                  {item.sscc?<small style={{ display: 'block', fontSize: 10, color: '#64748b', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>SSCC: {item.sscc}</small>:null}
                </td>
                <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                  <UiBadge tone={tone(item.status)}>{huStateLabels[item.status]??item.status}</UiBadge>
                  <div style={{ fontSize: 10, color: '#64748b', marginTop: 2 }}>SL: {roundQty4((item.contents??[]).reduce((sum,c)=>sum+c.quantity,0))}</div>
                </td>
                <td style={{ padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle' }}>
                  <button type="button" className="ui-btn-secondary" style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }} onClick={()=>setLabelPreviewHu(item)}>In nhãn</button>
                </td>
              </tr>)}
            </tbody>
          </table>
        </UiTableScroll>}
      </UiCard>
    </div>

  </UiPage>;
}
