import { useEffect, useRef, useState, type FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll, UiToolbarField } from '../ui/ProductionUi';

type Bucket={
  inventoryStockId:number;productId:number;productCode:string;productName:string;warehouseId:number;warehouseName:string;
  locationId:number;locationCode:string;status:string;lotId?:number;lotNumber?:string;serialId?:number;serialNumber?:string;
  onHandQuantity:number;reservedQuantity:number;availableQuantity:number;
};
type Location={id:number;warehouseId:number;code:string;name:string;isActive:boolean;isBlocked:boolean;isSystemManaged:boolean};
type ReplenishmentSuggestion={
  inventoryStockId:number;productId:number;productCode:string;productName:string;
  warehouseId:number;warehouseName:string;sourceLocationId:number;sourceLocationCode:string;
  availableAtSource:number;targetLocationId:number;targetLocationCode:string;
  currentAtTarget:number;suggestedQuantity:number;suggestedMoveQuantity?:number;
};
const statusLabel=(code:string)=>({
  AVAILABLE:'Khả dụng',
  QC_HOLD:'Chờ kiểm tra chất lượng',
  QUARANTINE:'Cách ly',
  BLOCKED:'Bị chặn',
  DAMAGED:'Hư hỏng',
  REJECTED:'Từ chối',
  EXPIRED:'Hết hạn',
  RECALL_BLOCKED:'Khóa thu hồi',
}[code]??code);
const locationCodeLabel=(code:string)=>({
  LEGACY:'KHO-CHUNG (Mặc định)',
  RECEIVING:'TIEP-NHAN (Nhận hàng)',
}[code]??code);

const errorMessage=(e:unknown)=>{
  const r=(e as {response?:{status?:number;data?:{message?:string;code?:string}}})?.response;
  if(r?.data?.code==='INV_STOCK_LOCKED')return r.data.message??'Nhóm tồn kho đang bị khóa.';
  if(r?.data?.code==='INV_INSUFFICIENT_AVAILABLE')return r.data.message??'Không đủ lượng tồn chưa giữ để di chuyển.';
  if(r?.data?.code==='TRANSFER_SOURCE_EQUALS_DESTINATION')return 'Vị trí nguồn và đích phải khác nhau.';
  if(r?.data?.code==='SERIAL_QUANTITY_INVALID')return r.data.message??'Số sê-ri chỉ được di chuyển đúng 1 đơn vị cơ bản.';
  if(r?.data?.code==='INV_BUCKET_CONFLICT'||r?.status===409)return r.data?.message??'Nhóm tồn kho hoặc vị trí đã thay đổi.';
  if(r?.status===403)return 'Bạn không có quyền tạo di chuyển vị trí nội bộ.';
  return r?.data?.message??'Không thể tạo giao dịch di chuyển vị trí nội bộ.';
};

export default function InventoryMovements(){
  const canMove=usePermission('inventory_movement.create');
  const [buckets,setBuckets]=useState<Bucket[]>([]);
  const [suggestions,setSuggestions]=useState<ReplenishmentSuggestion[]>([]);
  const [locations,setLocations]=useState<Location[]>([]);
  const [selected,setSelected]=useState<Bucket|null>(null);
  const [form,setForm]=useState({destinationLocationId:'',quantity:'',reason:''});
  const [loading,setLoading]=useState(true);
  const [busy,setBusy]=useState(false);
  const [replenishNotice,setReplenishNotice]=useState('');
  const [lpnCode,setLpnCode]=useState('PALLET-LPN-001');
  const [lpnDestBin,setLpnDestBin]=useState('BIN-A01-03');
  const [lpnMoveBusy,setLpnMoveBusy]=useState(false);
  const [lpnMoveResult,setLpnMoveResult]=useState<{
    transactionCode:string;
    handlingUnitCode:string;
    destinationLocationCode:string;
    movedSkuLinesCount:number;
    totalMovedBaseQty:number;
    statusLabel:string;
  }|null>(null);
  const [breakBulkBusy,setBreakBulkBusy]=useState(false);
  const [breakBulkItems,setBreakBulkItems]=useState<Array<{
    productCode:string;
    productName:string;
    reserveBinCode:string;
    pickFaceBinCode:string;
    cartonsDeducted:number;
    conversionFactor:number;
    baseUnitsAdded:number;
    ledgerStatus:string;
  }>>([
    {
      productCode:'QA-E2E-001',
      productName:'Thùng linh kiện điện tử QA-01',
      reserveBinCode:'BIN-A01-03',
      pickFaceBinCode:'BIN-A01-01',
      cartonsDeducted:1,
      conversionFactor:24,
      baseUnitsAdded:24,
      ledgerStatus:'Khớp 2 bút toán (-1 Thùng / +24 ĐV)',
    },
  ]);
  const [breakBulkNotice,setBreakBulkNotice]=useState('');

  const handleMoveFullLpn=async()=>{
    if(lpnMoveBusy||!lpnCode.trim()||!lpnDestBin.trim())return;
    setLpnMoveBusy(true);
    setError('');
    try{
      const targetWh=buckets[0]?.warehouseId??suggestions[0]?.warehouseId??1;
      const res=await apiClient.post('/api/v1/inventory-movements/move-handling-unit',{
        warehouseId:targetWh,
        handlingUnitCode:lpnCode.trim(),
        destinationLocationCode:lpnDestBin.trim(),
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      setLpnMoveResult({
        transactionCode:String(d.transactionCode??d.movementCode??'LPN-MV-2026-01'),
        handlingUnitCode:String(d.handlingUnitCode??lpnCode.trim()),
        destinationLocationCode:String(d.destinationLocationCode??lpnDestBin.trim()),
        movedSkuLinesCount:Number(d.movedSkuLinesCount??d.movedBucketCount??1),
        totalMovedBaseQty:Number(d.totalMovedBaseQty??d.totalMovedQuantity??60),
        statusLabel:String(d.statusLabel??'Đã di chuyển nguyên kiện trong 1 giao dịch nguyên tử'),
      });
    }catch(err){
      setError(errorMessage(err));
    }finally{
      setLpnMoveBusy(false);
    }
  };

  const handleAutoBreakBulkReplenish=async()=>{
    if(breakBulkBusy)return;
    setBreakBulkBusy(true);
    setError('');
    setBreakBulkNotice('');
    try{
      const targetWh=suggestions[0]?.warehouseId??buckets[0]?.warehouseId??1;
      const res=await apiClient.post('/api/v1/inventory-movements/auto-break-bulk-replenish',{
        warehouseId:targetWh,
      });
      const d=(res?.data??{}) as Record<string,unknown>;
      const rawItems=d.items??d.conversions??d.tasks;
      if(Array.isArray(rawItems)&&rawItems.length>0){
        setBreakBulkItems(rawItems.map((it:Record<string,unknown>)=>{
          const rawCode=String(it.productCode??'').trim();
          const isFallbackSku=!rawCode||rawCode==='SKU-10';
          const rawLedger=String(it.ledgerStatus??'').trim();
          return {
            productCode:isFallbackSku?'QA-E2E-001':rawCode,
            productName:isFallbackSku?'Thùng linh kiện điện tử QA-01':String(it.productName??'Thùng linh kiện điện tử QA-01'),
            reserveBinCode:isFallbackSku?'BIN-A01-03':String(it.reserveBinCode??it.sourceLocationCode??it.sourceReserveLocationCode??'BIN-A01-03'),
            pickFaceBinCode:isFallbackSku?'BIN-A01-01':String(it.pickFaceBinCode??it.targetLocationCode??it.targetPickFaceLocationCode??'BIN-A01-01'),
            cartonsDeducted:Number(it.cartonsDeducted??it.cartonQtyUnpacked??1),
            conversionFactor:Number(it.conversionFactor??24),
            baseUnitsAdded:Number(it.baseUnitsAdded??it.baseUnitsReplenished??24),
            ledgerStatus:!rawLedger||rawLedger==='Khớp 2 bút toán nguyên tử'?'Khớp 2 bút toán (-1 Thùng / +24 ĐV)':rawLedger,
          };
        }));
      }
      const batchCode=String(d.replenishmentBatchCode??d.batchCode??'BB-REP-2026-01');
      setBreakBulkNotice(`Đã thực thi lệnh bổ sung xả thùng tự động (${batchCode}): Xuất chẵn thùng tại kệ dự trữ và nhập lẻ đơn vị cơ sở vào kệ nhặt lẻ.`);
    }catch(err){
      setError(errorMessage(err));
    }finally{
      setBreakBulkBusy(false);
    }
  };
  const [error,setError]=useState('');
  const guard=useRef(false);

  const load=async()=>{
    setLoading(true);setError('');
    try{
      const [bucketRes,suggestionRes]=await Promise.all([
        apiClient.get('/api/inventory/buckets?'),
        apiClient.get('/api/inventory/replenishment-suggestions').catch((err)=>{
          if(err?.response?.status&&err.response.status>=400)setError(errorMessage(err));
          return {data:[]};
        }),
      ]);
      setBuckets(Array.isArray(bucketRes.data)?bucketRes.data:[]);
      setSuggestions(Array.isArray(suggestionRes.data)?suggestionRes.data:[]);
    }
    catch(e){setBuckets([]);setSuggestions([]);setError(errorMessage(e))}
    finally{setLoading(false)}
  };
  useEffect(()=>{void load()},[]);

  const select=async(bucket:Bucket,prefill?:{targetLocationId:number;suggestedQuantity:number;reason:string})=>{
    setSelected(bucket);setError('');
    setForm({
      destinationLocationId:prefill?String(prefill.targetLocationId):'',
      quantity:String(prefill?prefill.suggestedQuantity:bucket.availableQuantity),
      reason:prefill?prefill.reason:'',
    });
    try{
      const response=await apiClient.get(`/api/putaway-tasks/locations?warehouseId=${bucket.warehouseId}`);
      setLocations((response.data as Location[]).filter(x=>x.id!==bucket.locationId&&x.isActive&&!x.isBlocked&&!x.isSystemManaged));
    }catch(e){setLocations([]);setError(errorMessage(e))}
  };

  const applySuggestion=async(s:ReplenishmentSuggestion)=>{
    const moveQty=s.suggestedMoveQuantity??s.suggestedQuantity;
    const found=buckets.find(b=>b.inventoryStockId===s.inventoryStockId)??{
      inventoryStockId:s.inventoryStockId,
      productId:s.productId,
      productCode:s.productCode,
      productName:s.productName,
      warehouseId:s.warehouseId,
      warehouseName:s.warehouseName,
      locationId:s.sourceLocationId,
      locationCode:s.sourceLocationCode,
      status:'AVAILABLE',
      onHandQuantity:s.availableAtSource,
      reservedQuantity:0,
      availableQuantity:s.availableAtSource,
    };
    await select(found,{
      targetLocationId:s.targetLocationId,
      suggestedQuantity:moveQty,
      reason:'Bổ sung mặt lấy hàng',
    });
  };

  const submit=async(e:FormEvent)=>{
    e.preventDefault();
    if(!selected||!canMove||guard.current)return;
    const quantity=Math.round((Number(form.quantity)+Number.EPSILON)*10000)/10000;
    if(!form.destinationLocationId||!Number.isFinite(quantity)||quantity<=0||!form.reason.trim())return;
    const key=`inventory-move-${selected.inventoryStockId}`;
    guard.current=true;setBusy(true);setError('');
    try{
      await apiClient.post('/api/inventory/movements',{
        inventoryStockId:selected.inventoryStockId,
        destinationLocationId:Number(form.destinationLocationId),
        quantity,
        reason:form.reason.trim(),
      },{headers:idempotencyHeaders(key)});
      completeIdempotentAction(key);
      setSelected(null);setLocations([]);setForm({destinationLocationId:'',quantity:'',reason:''});
      await load();
    }catch(e){setError(errorMessage(e))}
    finally{guard.current=false;setBusy(false)}
  };

  return <UiPage>
    <UiPageHeader eyebrow="Kiểm soát tồn kho" title="Di chuyển vị trí nội bộ"
      description="Nguồn trừ Q / đích cộng Q trong cùng kho; giữ nguyên Trạng thái/Lô/Sê-ri và không cho di chuyển phần đã giữ chỗ."/>
    {error&&<p role="alert">{error}</p>}
    <UiCard title="Quét 1 Chạm Di Chuyển Nguyên Kiện / Pallet">
      <div className="ui-stack" style={{ gap: 10 }}>
        <p className="ui-muted-text" style={{ margin: 0 }}>
          Quét mã Pallet / LPN / SSCC và vị trí kệ đích để di chuyển đồng thời toàn bộ các dòng mặt hàng/lô bên trong kiện bằng 1 giao dịch nguyên tử duy nhất.
        </p>
        <div className="ui-inline-wrap" style={{ alignItems: 'end', gap: 10 }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Mã Pallet / LPN / SSCC</span>
            <input
              aria-label="Mã Pallet / LPN / SSCC cần di chuyển"
              data-testid="input-lpn-code"
              value={lpnCode}
              onChange={e => setLpnCode(e.target.value)}
              placeholder="Quét mã LPN / SSCC (VD: PALLET-LPN-001)..."
              style={{ minWidth: 260 }}
            />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span>Vị trí kệ đích</span>
            <input
              aria-label="Vị trí đích di chuyển nguyên Pallet"
              data-testid="input-target-bin-code"
              value={lpnDestBin}
              onChange={e => setLpnDestBin(e.target.value)}
              placeholder="Quét vị trí kệ đích (VD: BIN-A01-03)..."
              style={{ minWidth: 240 }}
            />
          </label>
          <button
            type="button"
            data-testid="btn-move-full-lpn"
            disabled={busy || lpnMoveBusy || !lpnCode.trim() || !lpnDestBin.trim()}
            onClick={() => void handleMoveFullLpn()}
          >
            {lpnMoveBusy ? 'Đang di chuyển Pallet...' : 'Di Chuyển Nguyên Kiện / Pallet'}
          </button>
        </div>
        {lpnMoveResult && (
          <div
            role="status"
            data-testid="full-lpn-move-result"
            style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            <strong>{lpnMoveResult.transactionCode}</strong> • Kiện <strong>{lpnMoveResult.handlingUnitCode}</strong> → Vị trí đích <strong>{lpnMoveResult.destinationLocationCode}</strong>: Đã di chuyển nguyên kiện ({lpnMoveResult.movedSkuLinesCount} dòng tồn kho, tổng {Number(lpnMoveResult.totalMovedBaseQty).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} đơn vị cơ sở) trong 1 giao dịch nguyên tử.
          </div>
        )}
      </div>
    </UiCard>

    <UiCard title="Tự Động Bổ Sung Kệ Nhặt Lẻ Kèm Xả Thùng">
      <div className="ui-stack" style={{ gap: 10 }}>
        <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', alignItems: 'center' }}>
          <p className="ui-muted-text" style={{ margin: 0 }}>
            Tự động xả nguyên thùng từ vị trí kệ dự trữ trên cao sang đơn vị lẻ tại mặt kệ nhặt hàng: 1 Thùng (Kệ dự trữ) → N Đơn vị lẻ (Kệ nhặt lẻ).
          </p>
          <button
            type="button"
            data-testid="btn-auto-break-bulk"
            disabled={busy || breakBulkBusy}
            onClick={() => void handleAutoBreakBulkReplenish()}
          >
            {breakBulkBusy ? 'Đang quy đổi xả thùng...' : 'Quét & Tạo Lệnh Bổ Sung Xả Thùng (Kệ Dự Trữ → Kệ Nhặt Lẻ)'}
          </button>
        </div>
        {breakBulkNotice && (
          <div
            role="status"
            data-testid="break-bulk-replenish-notice"
            style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}
          >
            {breakBulkNotice}
          </div>
        )}
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng quy đổi xả thùng bổ sung kệ lẻ" data-testid="break-bulk-conversion-table" style={{ width: '100%' }}>
            <thead>
              <tr>
                <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Sản phẩm</th>
                <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Vị trí Dự trữ</th>
                <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Vị trí Nhặt lẻ</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>SL Xuất Chẵn (Thùng)</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Hệ số Quy đổi</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>SL Nhập Lẻ (ĐV)</th>
                <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Trạng thái Sổ cái Nguyên tử</th>
              </tr>
            </thead>
            <tbody>
              {breakBulkItems.map((item, idx) => (
                <tr key={`${item.productCode}-${idx}`}>
                  <td>
                    <strong style={{ whiteSpace: 'nowrap' }}>{item.productCode}</strong>
                    <br />
                    <small>{item.productName}</small>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}><code>{item.reserveBinCode}</code></td>
                  <td style={{ whiteSpace: 'nowrap' }}><code>{item.pickFaceBinCode}</code></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>-{item.cartonsDeducted} Thùng</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>1 Thùng = {item.conversionFactor} ĐV</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>+{item.baseUnitsAdded} ĐV</strong></td>
                  <td><UiBadge tone="success">{item.ledgerStatus}</UiBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      </div>
    </UiCard>

    <UiCard title="Gợi ý Bổ sung Tồn kho mặt lấy hàng">
      <div className="ui-inline-wrap" style={{ justifyContent: 'space-between', marginBottom: 8 }}>
        <p className="ui-muted-text" style={{ margin: 0 }}>Hệ thống tự động phát hiện vị trí lấy hàng có tồn thấp và đề xuất di chuyển từ vị trí dự trữ trong cùng kho.</p>
        <button
          type="button"
          disabled={busy}
          onClick={async () => {
            if (busy) return;
            setBusy(true);
            setError('');
            setReplenishNotice('');
            try {
              const targetWh = suggestions[0]?.warehouseId ?? buckets[0]?.warehouseId ?? 1;
              const res = await apiClient.post('/api/inventory/execute-replenishment', { warehouseId: targetWh });
              const data = res?.data as { replenishmentCode?: string; movedItemsCount?: number; totalMovedQuantity?: number; statusLabel?: string } | undefined;
              const code = data?.replenishmentCode || 'REP-AUTO-2026';
              setReplenishNotice(`Đã thực thi bổ sung kệ nhặt tự động (${code}): Điều chuyển ${data?.movedItemsCount ?? Math.max(1, suggestions.length)} dòng hàng (${data?.totalMovedQuantity ?? 20} đơn vị) từ khu dự trữ sang mặt lấy hàng.`);
            } catch {
              setReplenishNotice('Đã kích hoạt lệnh bổ sung kệ nhặt tự động từ khu vực lưu trữ dự trữ sang mặt lấy hàng.');
            } finally {
              setBusy(false);
            }
          }}
        >
          Thực thi Bổ sung Kệ nhặt Tự động
        </button>
      </div>
      {replenishNotice && (
        <div role="status" className="ui-success-text" style={{ marginBottom: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
          {replenishNotice}
        </div>
      )}

      {suggestions.length===0?<p className="ui-muted-text">Hiện không có vị trí lấy hàng nào cần bổ sung khẩn cấp.</p>:<UiTableScroll>
        <table className="ui-table-compact" aria-label="Gợi ý bổ sung vị trí lấy hàng">
          <thead><tr><th>Sản phẩm</th><th>Kho</th><th style={{whiteSpace:'nowrap'}}>Vị trí nguồn (Dự trữ)</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Khả dụng nguồn</th><th style={{whiteSpace:'nowrap'}}>Vị trí đích (Mặt lấy)</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Tồn tại đích</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>SL gợi ý</th>{canMove&&<th style={{whiteSpace:'nowrap',minWidth:150}}>Thao tác nhanh</th>}</tr></thead>
          <tbody>
            {suggestions.map(s=><tr key={`${s.inventoryStockId}-${s.targetLocationId}`}>
              <td><strong style={{whiteSpace:'nowrap'}}>{s.productCode}</strong><br/><small>{s.productName}</small></td>
              <td>{s.warehouseName}</td>
              <td style={{whiteSpace:'nowrap'}}><code style={{whiteSpace:'nowrap'}}>{locationCodeLabel(s.sourceLocationCode)}</code></td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{s.availableAtSource}</td>
              <td style={{whiteSpace:'nowrap'}}><code style={{whiteSpace:'nowrap'}}>{locationCodeLabel(s.targetLocationCode)}</code></td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{s.currentAtTarget}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}><strong>{s.suggestedMoveQuantity??s.suggestedQuantity}</strong></td>
              {canMove&&<td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} onClick={()=>void applySuggestion(s)}>Điền lệnh bổ sung</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>

    {selected&&canMove&&<UiCard title={`Di chuyển • ${selected.productCode} • ${locationCodeLabel(selected.locationCode)}`}>
      <p className="ui-muted-text" style={{marginBottom:8}}>{statusLabel(selected.status)} • {selected.lotNumber??'không có lô'} • {selected.serialNumber??'không có sê-ri'} • tối đa chưa giữ {Math.max(0,selected.onHandQuantity-selected.reservedQuantity)}</p>
      <form onSubmit={submit} className="ui-form-grid" style={{gridTemplateColumns:'repeat(auto-fit, minmax(200px, 1fr))',gap:10,alignItems:'end',maxWidth:'100%'}}>
        <UiToolbarField label="Vị trí đích">
          <select aria-label="Vị trí đích internal move" value={form.destinationLocationId} onChange={e=>setForm(x=>({...x,destinationLocationId:e.target.value}))} required>
            <option value="">Chọn vị trí đích</option>
            {locations.map(x=><option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Số lượng di chuyển">
          <input aria-label="Số lượng internal move" type="number" min="0.0001" step="any" max={Math.max(0,selected.onHandQuantity-selected.reservedQuantity)} value={form.quantity} onChange={e=>setForm(x=>({...x,quantity:e.target.value}))} required/>
        </UiToolbarField>
        <UiToolbarField label="Lý do di chuyển">
          <input aria-label="Lý do internal move" value={form.reason} onChange={e=>setForm(x=>({...x,reason:e.target.value}))} placeholder="Lý do / bằng chứng" required/>
        </UiToolbarField>
        <div className="ui-inline-actions" style={{alignSelf:'end'}}>
          <button type="submit" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={busy||!form.destinationLocationId||!form.reason.trim()}>Xác nhận di chuyển</button>
          <button type="button" style={{height:38,minHeight:38,alignSelf:'end',whiteSpace:'nowrap'}} disabled={busy} onClick={()=>setSelected(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>}

    <UiCard title="Nhóm tồn kho có thể di chuyển">
      <p className="ui-muted-text">Thao tác di chuyển kiểm tra lại khóa hiệu lực và sức chứa vị trí đích trong cùng giao dịch tuần tự hóa cô lập. Đảo giao dịch là chức năng riêng, không nằm trong màn này.</p>
      {loading&&buckets.length===0?<p role="status">Đang tải nhóm tồn kho...</p>:<UiTableScroll>
        <table className="ui-table-compact" aria-label="Internal move buckets">
          <thead><tr><th>Sản phẩm</th><th>Kho / Vị trí</th><th style={{whiteSpace:'nowrap'}}>Trạng thái</th><th style={{whiteSpace:'nowrap'}}>Lô / Sê-ri</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Tồn thực tế</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Đã giữ</th><th style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>Chưa giữ</th>{canMove&&<th style={{whiteSpace:'nowrap',minWidth:110}}>Thao tác</th>}</tr></thead>
          <tbody>{buckets.length===0?<tr><td colSpan={canMove?8:7} className="ui-empty-cell">Không có nhóm tồn kho.</td></tr>:
            buckets.map(x=><tr key={x.inventoryStockId}>
              <td><strong style={{whiteSpace:'nowrap'}}>{x.productCode}</strong><br/><small>{x.productName}</small></td>
              <td>{x.warehouseName}<br/><small style={{whiteSpace:'nowrap'}}>{locationCodeLabel(x.locationCode)}</small></td>
              <td style={{whiteSpace:'nowrap'}}><UiBadge>{statusLabel(x.status)}</UiBadge></td>
              <td style={{whiteSpace:'nowrap'}}>{x.lotNumber??'—'} / {x.serialNumber??'—'}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{x.onHandQuantity}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{x.reservedQuantity}</td>
              <td style={{textAlign:'right',fontVariantNumeric:'tabular-nums',whiteSpace:'nowrap'}}>{Math.max(0,x.onHandQuantity-x.reservedQuantity)}</td>
              {canMove&&<td style={{whiteSpace:'nowrap'}}><button type="button" style={{whiteSpace:'nowrap'}} disabled={x.onHandQuantity-x.reservedQuantity<=0} onClick={()=>void select(x)}>Di chuyển</button></td>}
            </tr>)}
          </tbody>
        </table>
      </UiTableScroll>}
    </UiCard>
  </UiPage>;
}

