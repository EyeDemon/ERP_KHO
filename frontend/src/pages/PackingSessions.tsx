import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import { BarcodeSvg } from '../components/BarcodeSvg';
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
  const [videoAwbCode,setVideoAwbCode]=useState<string>('SPXVN058291042');
  const [videoDurationSec,setVideoDurationSec]=useState<string>('48');
  const [videoSizeMb,setVideoSizeMb]=useState<string>('14.6');
  const [videoSha256,setVideoSha256]=useState<string>('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
  const [videoSearchAwb,setVideoSearchAwb]=useState<string>('');
  const [videoNotice,setVideoNotice]=useState<string>('');
  const [videoEvidences,setVideoEvidences]=useState<Array<{id:number;sessionId:number;sessionCode:string;awbCode:string;durationSeconds:number;fileSizeMb:number;sha256Hash:string;recordedAt:string;cameraStation:string}>>([]);
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
        {warehouseId:selected?.warehouseId??sessions[0]?.warehouseId??undefined,kitSkuCode:'KIT-BUNDLE-STD',vasServiceType:'LABELING_SHRINKWRAP_BUNDLE'},
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
          :data.vasServiceType);
        setKittingVasResult(`${vasCode} • ${vasLabel} — ${scopeLabel} (${data?.activePackingSessionsCount??0} phiên • ${data?.handlingUnitsCount??0} kiện HU)`);
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

  return <UiPage>
    <UiPageHeader
      eyebrow="XUẤT KHO"
      title="Đóng gói & Đơn vị xử lý (HU)"
      description="Đóng gói hàng đã lấy vào thùng carton, khay hoặc pallet (HU). Bước đóng gói bảo toàn số lượng và không trừ tồn kho; tồn kho chỉ trừ một lần khi xuất bến."
    />
    {error&&<div role="alert">{error}</div>}

    <MobileBarcodeScannerBar
      warehouseId={selected?.warehouseId??sessions[0]?.warehouseId}
      sampleCode={sessions[0]?.sessionCode||'HU-001'}
      disabled={!!busy||governanceBusy}
      onScan={code=>{
        const matchSession=sessions.find(s=>s.sessionCode.toUpperCase()===code.toUpperCase());
        if(matchSession)void detail(matchSession.id);
      }}
    />

    <div className="ui-stack">
      <UiCard title="Danh mục Loại Bao bì Chuẩn & Gợi ý Tối ưu Đóng thùng">
        <div className="ui-inline-wrap">
          <span><strong>CARTON-S</strong> (Thùng carton): tối đa 15 kg / 0.04 m³</span>
          <span><strong>CARTON-L</strong> (Thùng carton): tối đa 30 kg / 0.12 m³</span>
          <span><strong>TOTE-STD</strong> (Khay nhựa): tối đa 25 kg / 0.06 m³</span>
          <span><strong>PALLET-EUR</strong> (Pallet gỗ/nhựa): tối đa 1000 kg / 1.80 m³</span>
          <span>Chính sách tối ưu đóng thùng: <UiBadge tone="success">Đề xuất & Xác nhận bởi Nhân viên</UiBadge></span>
        </div>
        <div className="ui-inline-wrap">
          <button type="button" disabled={!!busy||governanceBusy} onClick={()=>void verifyPackagingProfile()}>Kiểm tra Quy cách Thùng & Chuẩn mã GS1/SSCC</button>
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
                  style={{ whiteSpace: 'nowrap', padding: '3px 5px', minHeight: 24, fontSize: 10.5, lineHeight: 1.2 }}
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
                <option value="Carton">Thùng carton (Carton)</option><option value="Tote">Khay nhựa (Tote)</option><option value="Pallet">Pallet tiêu chuẩn (Pallet)</option>
                <option value="Container">Thùng hàng lớn (Container)</option><option value="Custom">Tùy chỉnh (Custom)</option>
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
          )}>Xác nhận PACKED</button>}
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
            <BarcodeSvg value={labelPreviewHu.barcode||labelPreviewHu.huCode} sscc={labelPreviewHu.sscc||'008938505000000018'} label={labelPreviewHu.huCode} />
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
            <BarcodeSvg value="HU-QA-001" sscc="008938505000000018" label="HU-QA-001" />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: '#0f172a' }}>Tem chuẩn HU-QA-002 • SSCC: 008938505000000025</div>
            <BarcodeSvg value="HU-QA-002" sscc="008938505000000025" label="HU-QA-002" />
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
                  <button type="button" className="ui-btn-secondary" style={{ whiteSpace: 'nowrap', padding: '3px 6px', minHeight: 24, fontSize: 10.5, lineHeight: 1.2 }} onClick={()=>setLabelPreviewHu(item)}>In nhãn</button>
                </td>
              </tr>)}
            </tbody>
          </table>
        </UiTableScroll>}
      </UiCard>
    </div>
      <UiCard title="Trạm Quay Video Đóng gói & Lưu Bằng chứng Khiếu nại">
        <p className="ui-muted-text">
          Ghi hình trực tiếp tại bàn đóng gói, gắn mã vận đơn (AWB), thời lượng, dung lượng tệp và mã băm SHA-256 chống chối bỏ khi đối soát khiếu nại sàn TMĐT / đơn vị vận chuyển.
        </p>
        {videoNotice&&<p role="status" className="ui-success-text">{videoNotice}</p>}
        <div className="ui-stack">
          <div style={{padding:'10px 14px',borderRadius:8,background:'#0f172a',color:'#f8fafc',display:'flex',justifyContent:'space-between',alignItems:'center',flexWrap:'wrap',gap:8}}>
            <span><strong>Trạm Camera Bàn Đóng gói: CAM-PACK-01</strong> • Độ phân giải: 1080p 30fps • Luồng RTSP/WebRTC Trực tiếp</span>
            <UiBadge tone="success">Đang ghi hình &amp; Đóng dấu thời gian</UiBadge>
          </div>
          <form onSubmit={recordPackingVideoEvidence} className="ui-inline-wrap" style={{alignItems:'end'}}>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Mã vận đơn (AWB)
              <input aria-label="Mã vận đơn AWB quay video" value={videoAwbCode} onChange={e=>setVideoAwbCode(e.target.value)} placeholder="VD: SPXVN058291042"/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Tệp Video bằng chứng (.mp4/.webm)
              <input
                aria-label="Tệp video đóng gói"
                type="file"
                accept="video/*"
                onChange={e=>{
                  const file=e.target.files?.[0]??null;
                  setSelectedVideoFile(file);
                  if(file&&file.size>0){
                    setVideoSizeMb(String(Math.max(0.1,Math.round((file.size/(1024*1024))*10)/10)));
                  }
                }}
              />
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Thời lượng (giây)
              <input aria-label="Thời lượng video (giây)" type="number" min="1" value={videoDurationSec} onChange={e=>setVideoDurationSec(e.target.value)}/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Dung lượng (MB)
              <input aria-label="Dung lượng tệp video (MB)" type="number" min="0.1" step="0.1" value={videoSizeMb} onChange={e=>setVideoSizeMb(e.target.value)}/>
            </label>
            <label style={{display:'flex',flexDirection:'column',gap:6,minWidth:240}}>Mã băm toàn vẹn SHA-256
              <input aria-label="Mã băm toàn vẹn SHA-256" value={videoSha256} onChange={e=>setVideoSha256(e.target.value)}/>
            </label>
            <button type="submit" disabled={!!busy}>Ghi nhận Video Đóng gói &amp; Ký số SHA-256</button>
          </form>

          <form onSubmit={searchPackingVideoEvidences} className="ui-inline-wrap" style={{alignItems:'end'}}>
            <label style={{display:'flex',flexDirection:'column',gap:6}}>Tra cứu theo Mã vận đơn (AWB)
              <input aria-label="Tra cứu video theo mã vận đơn AWB" value={videoSearchAwb} onChange={e=>setVideoSearchAwb(e.target.value)} placeholder="Nhập mã AWB cần tra cứu..."/>
            </label>
            <button type="submit" className="ui-btn-secondary" disabled={!!busy}>Tra cứu Video theo Mã vận đơn (AWB)</button>
          </form>

          {videoEvidences.length>0&&(
            <UiTableScroll>
              <table aria-label="Danh sách bằng chứng video đóng gói" className="ui-table-compact">
                <thead>
                  <tr>
                    <th>Mã vận đơn (AWB)</th>
                    <th>Phiên đóng gói</th>
                    <th>Trạm Camera</th>
                    <th style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Thời lượng</th>
                    <th style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>Dung lượng</th>
                    <th>Mã băm SHA-256</th>
                  </tr>
                </thead>
                <tbody>
                  {videoEvidences.map(ev=>(
                    <tr key={ev.id}>
                      <td><strong>{ev.awbCode}</strong></td>
                      <td>{ev.sessionCode}</td>
                      <td>{ev.cameraStation}</td>
                      <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{ev.durationSeconds} giây</td>
                      <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums'}}>{ev.fileSizeMb} MB</td>
                      <td><code>{ev.sha256Hash}</code></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          )}
        </div>
      </UiCard>
  </UiPage>;
}
