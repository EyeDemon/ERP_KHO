import { useState } from 'react';
import { roundQty4, sanitizeScannerCode } from '../../pages/productCatalog';
import { MobileBarcodeScannerBar } from '../../components/MobileBarcodeScannerBar';
import { BarcodeSvg } from '../../components/BarcodeSvg';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { HandlingUnit, PackingSession } from './packingSessionsTypes';
import { hiddenTabStyle, huStateLabels, stateLabels, tone } from './packingSessionsTypes';

interface PackingSessionsAndHuTabProps {
  active: boolean;
  sessions: PackingSession[];
  selected: PackingSession | null;
  huRegistry: HandlingUnit[];
  huType: string;
  setHuType: (val: string) => void;
  busy: string;
  governanceBusy: boolean;
  canExecute: boolean;
  canCreateHu: boolean;
  canModifyHu: boolean;
  detail: (id: number) => Promise<void>;
  mutate: (key: string, path: string, body: object) => Promise<void>;
}

export function PackingSessionsAndHuTab({
  active,
  sessions,
  selected,
  huRegistry,
  huType,
  setHuType,
  busy,
  governanceBusy,
  canExecute,
  canCreateHu,
  canModifyHu,
  detail,
  mutate,
}: PackingSessionsAndHuTabProps) {
  const [huCode, setHuCode] = useState('');
  const [packForm, setPackForm] = useState<Record<number, { huId: string; barcode: string; quantity: string }>>({});
  const [parents, setParents] = useState<Record<number, string>>({});
  const [labelPreviewHu, setLabelPreviewHu] = useState<HandlingUnit | null>(null);

  const activeHus = (selected?.handlingUnits ?? []).filter(x => !['Cancelled', 'Staged', 'Loaded', 'Shipped'].includes(x.status));
  const packableHus = activeHus.filter(x => ['Open', 'InUse'].includes(x.status));

  return (
    <div style={active ? { display: 'contents' } : hiddenTabStyle}>
      <MobileBarcodeScannerBar
        warehouseId={selected?.warehouseId ?? sessions[0]?.warehouseId}
        sampleCode={sessions[0]?.sessionCode || 'HU-001'}
        disabled={!!busy || governanceBusy}
        onScan={code => {
          const matchSession = sessions.find(s => s.sessionCode.toUpperCase() === code.toUpperCase());
          if (matchSession) void detail(matchSession.id);
        }}
      />

      <UiCard title="Danh sách phiên đóng gói">
        {sessions.length === 0 ? (
          <p>Chưa có Packing session. Session được tạo khi Picking hoàn tất.</p>
        ) : (
          <UiTableScroll>
            <table aria-label="Danh sách Packing session" className="ui-table-compact" style={{ width: '100%', minWidth: 0, tableLayout: 'fixed' }}>
              <thead>
                <tr>
                  <th style={{ width: '30%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Mã phiên</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Lộ trình</th>
                  <th style={{ width: '26%', padding: '6px 4px', fontSize: 10.5, whiteSpace: 'nowrap' }}>Trạng thái</th>
                  <th style={{ width: '18%', padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tiến độ</th>
                </tr>
              </thead>
              <tbody>
                {sessions.map(item => (
                  <tr key={item.id}>
                    <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                      <button type="button" style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }} onClick={() => void detail(item.id)}>{item.sessionCode}</button>
                      <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 3, lineHeight: 1.2 }}>{item.warehouseName}</div>
                    </td>
                    <td style={{ padding: '6px 4px', fontSize: 10.5, lineHeight: 1.25, verticalAlign: 'middle' }}>
                      <span style={{ display: 'block', whiteSpace: 'nowrap', color: '#475569' }}>{item.pickingTaskCode || 'PICK'}</span>
                      <span style={{ display: 'block', whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a' }}>→ {item.sourceCode ?? item.sourceType}</span>
                    </td>
                    <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                      <UiBadge tone={tone(item.status)}>
                        <span style={{ whiteSpace: 'normal', lineHeight: 1.2, fontSize: 10.5, display: 'inline-block', maxWidth: 'none', textAlign: 'center' }}>{stateLabels[item.status] ?? item.status}</span>
                      </UiBadge>
                    </td>
                    <td style={{ padding: '8px 10px 8px 0px', fontSize: 11.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle', fontVariantNumeric: 'tabular-nums' }}>
                      <strong>{item.packedQuantity}</strong> / {item.requiredQuantity}
                      <div style={{ fontSize: 10.5, color: '#64748b', marginTop: 2 }}>{item.handlingUnitCount} kiện</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      {selected && (
        <UiCard title={`Chi tiết ${selected.sessionCode}`}>
          <div className="ui-inline-wrap">
            <UiBadge tone={tone(selected.status)}>{stateLabels[selected.status] ?? selected.status}</UiBadge>
            <span className="ui-muted-text">{selected.pickingTaskCode} • {selected.sourceCode ?? selected.sourceType} • {selected.warehouseName}</span>
            <span>Đã đóng gói: <strong>{selected.packedQuantity}</strong> / {selected.requiredQuantity} • Còn lại: {selected.remainingQuantity}</span>
          </div>
          <p className="ui-muted-text">Quản lý tạo kiện đóng gói, xác nhận hàng hóa vào kiện, niêm phong kiện, lồng ghép kiện (Pallet) và in nhãn vận chuyển.</p>

          {canCreateHu && ['Open', 'InProgress'].includes(selected.status) && (
            <div className="ui-subcard">
              <div className="ui-subcard-title">Tạo kiện đóng gói</div>
              <div className="ui-inline-wrap">
                <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Loại kiện đóng gói
                  <select aria-label="Loại Handling Unit" value={huType} onChange={e => setHuType(e.target.value)}>
                    <option value="Carton">Thùng carton</option><option value="Tote">Khay nhựa</option><option value="Pallet">Pallet tiêu chuẩn</option>
                    <option value="Container">Thùng hàng lớn</option><option value="Custom">Tùy chỉnh</option>
                  </select>
                </label>
                <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Mã/Barcode tùy chọn
                  <input aria-label="Mã Handling Unit" value={huCode} onChange={e => setHuCode(e.target.value)} placeholder="Tự sinh nếu bỏ trống" />
                </label>
                <button type="button" disabled={!!busy} onClick={() => void mutate(`packing-create-hu-${selected.id}`, `/api/v1/packing-sessions/${selected.id}/create-hu`, { type: huType, huCode: sanitizeScannerCode(huCode) || undefined, barcode: sanitizeScannerCode(huCode) || undefined, rowVersion: selected.rowVersion })}>Tạo kiện</button>
              </div>
            </div>
          )}

          <div className="ui-stack">
            {(selected.lines ?? []).map(line => (
              <div className="ui-subcard" key={line.pickingTaskLineId}>
                <div className="ui-subcard-title">{line.productCode} — {line.productName}</div>
                <p>Đã lấy: <strong>{line.pickedQuantity}</strong> • Đã đóng gói: {line.packedQuantity} • Còn lại: {line.remainingQuantity}</p>
                {canExecute && selected.status !== 'Packed' && selected.status !== 'Closed' && line.remainingQuantity > 0 && (
                  <div className="ui-inline-wrap">
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Kiện đích
                      <select aria-label={`HU đích ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.huId ?? ''} onChange={e => setPackForm(v => ({ ...v, [line.pickingTaskLineId]: { ...(v[line.pickingTaskLineId] ?? { barcode: '', quantity: '' }), huId: e.target.value } }))}>
                        <option value="">Chọn kiện đang mở</option>
                        {packableHus.map(hu => <option value={hu.id} key={hu.id}>{hu.huCode} ({hu.type})</option>)}
                      </select>
                    </label>
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Quét SKU/barcode
                      <input aria-label={`Product barcode packing ${line.productCode}`} value={packForm[line.pickingTaskLineId]?.barcode ?? ''} onChange={e => setPackForm(v => ({ ...v, [line.pickingTaskLineId]: { ...(v[line.pickingTaskLineId] ?? { huId: '', quantity: '' }), barcode: e.target.value } }))} />
                    </label>
                    <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Số lượng
                      <input aria-label={`Packing quantity ${line.productCode}`} type="number" min="0" step="any" value={packForm[line.pickingTaskLineId]?.quantity ?? ''} onChange={e => setPackForm(v => ({ ...v, [line.pickingTaskLineId]: { ...(v[line.pickingTaskLineId] ?? { huId: '', barcode: '' }), quantity: e.target.value } }))} />
                    </label>
                    <button
                      type="button"
                      disabled={!!busy || !packForm[line.pickingTaskLineId]?.huId || !sanitizeScannerCode(packForm[line.pickingTaskLineId]?.barcode ?? '') || !Number.isFinite(Number(packForm[line.pickingTaskLineId]?.quantity)) || Number(packForm[line.pickingTaskLineId]?.quantity) <= 0}
                      onClick={() => void mutate(`packing-pack-${selected.id}-${line.pickingTaskLineId}`, `/api/v1/packing-sessions/${selected.id}/pack`, {
                        handlingUnitId: Number(packForm[line.pickingTaskLineId]?.huId),
                        pickingTaskLineId: line.pickingTaskLineId,
                        productBarcode: sanitizeScannerCode(packForm[line.pickingTaskLineId]?.barcode ?? ''),
                        quantity: roundQty4(Number(packForm[line.pickingTaskLineId]?.quantity)),
                        rowVersion: selected.rowVersion,
                      })}
                    >
                      Xác nhận đóng gói
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="ui-stack">
            {(selected.handlingUnits ?? []).map(hu => (
              <div className="ui-subcard" key={hu.id}>
                <div className="ui-subcard-title">{hu.huCode} — {({ Carton: 'Thùng carton', Tote: 'Khay nhựa', Pallet: 'Pallet tiêu chuẩn', Container: 'Thùng hàng lớn', Custom: 'Tùy chỉnh' } as Record<string, string>)[hu.type] ?? hu.type}</div>
                <div className="ui-inline-wrap">
                  <UiBadge tone={tone(hu.status)}>{huStateLabels[hu.status] ?? hu.status}</UiBadge>
                  <span>Mã vạch: <strong>{hu.barcode}</strong></span>
                  {hu.sscc && <span>SSCC: <code>{hu.sscc}</code></span>}
                  {hu.parentHandlingUnitCode && <span>Trong HU: {hu.parentHandlingUnitCode}</span>}
                  <span>Số lượng trong kiện: {roundQty4(hu.contents.reduce((sum, item) => sum + item.quantity, 0))}</span>
                </div>
                {hu.contents.length > 0 && <ul>{hu.contents.map(item => <li key={item.id}>{item.productCode}: {item.quantity}</li>)}</ul>}

                <div className="ui-inline-wrap">
                  <button type="button" className="ui-btn-secondary" onClick={() => setLabelPreviewHu(hu)}>Xem / In nhãn kiện</button>
                  {canExecute && ['Open', 'InUse'].includes(hu.status) && hu.contents.length > 0 && (
                    <button type="button" disabled={!!busy} onClick={() => void mutate(`packing-close-hu-${selected.id}-${hu.id}`, `/api/v1/packing-sessions/${selected.id}/handling-units/${hu.id}/close`, { rowVersion: selected.rowVersion })}>Đóng kiện</button>
                  )}
                  {canModifyHu && ['Open', 'InUse', 'Closed'].includes(hu.status) && !hu.parentHandlingUnitId && activeHus.some(parent => parent.id !== hu.id && ['Open', 'InUse'].includes(parent.status)) && (
                    <>
                      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>Kiện cha
                        <select aria-label={`HU cha ${hu.huCode}`} value={parents[hu.id] ?? ''} onChange={e => setParents(v => ({ ...v, [hu.id]: e.target.value }))}>
                          <option value="">Chọn kiện cha</option>
                          {activeHus.filter(parent => parent.id !== hu.id && ['Open', 'InUse'].includes(parent.status)).map(parent => (
                            <option key={parent.id} value={parent.id}>{parent.huCode}</option>
                          ))}
                        </select>
                      </label>
                      <button type="button" disabled={!!busy || !parents[hu.id]} onClick={() => void mutate(`hu-nest-${selected.id}-${hu.id}`, `/api/v1/packing-sessions/${selected.id}/handling-units/${hu.id}/nest`, { parentHandlingUnitId: Number(parents[hu.id]), rowVersion: selected.rowVersion })}>Lồng kiện</button>
                    </>
                  )}
                  {canModifyHu && hu.parentHandlingUnitId && (
                    <button type="button" disabled={!!busy} onClick={() => void mutate(`hu-unnest-${selected.id}-${hu.id}`, `/api/v1/packing-sessions/${selected.id}/handling-units/${hu.id}/unnest`, { rowVersion: selected.rowVersion })}>Tách khỏi kiện cha</button>
                  )}
                  {canModifyHu && ['Open', 'InUse'].includes(hu.status) && hu.contents.length === 0 && (
                    <button type="button" disabled={!!busy} onClick={() => void mutate(`hu-cancel-${selected.id}-${hu.id}`, `/api/v1/packing-sessions/${selected.id}/handling-units/${hu.id}/cancel`, { rowVersion: selected.rowVersion })}>Hủy HU rỗng</button>
                  )}
                </div>
              </div>
            ))}
          </div>

          <div className="ui-inline-wrap">
            {canExecute && selected.status === 'InProgress' && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`packing-complete-${selected.id}`, `/api/v1/packing-sessions/${selected.id}/complete`, { rowVersion: selected.rowVersion })}>Xác nhận Đã đóng gói</button>
            )}
            {canExecute && selected.status === 'Packed' && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`packing-close-${selected.id}`, `/api/v1/packing-sessions/${selected.id}/close`, { rowVersion: selected.rowVersion })}>Đóng Packing session</button>
            )}
            {canExecute && ['Open', 'InProgress'].includes(selected.status) && selected.packedQuantity === 0 && (
              <button type="button" disabled={!!busy} onClick={() => void mutate(`packing-cancel-${selected.id}`, `/api/v1/packing-sessions/${selected.id}/cancel`, { rowVersion: selected.rowVersion })}>Hủy session rỗng</button>
            )}
          </div>
        </UiCard>
      )}

      {labelPreviewHu && (
        <UiCard title={`Nhãn Đơn vị xử lý & Mã vạch SSCC — ${labelPreviewHu.huCode}`}>
          <div className="ui-stack-sm">
            <div className="ui-inline-wrap">
              <span><strong>Mã kiện:</strong> {labelPreviewHu.huCode}</span>
              <span><strong>Mã vạch:</strong> <code>{labelPreviewHu.barcode}</code></span>
              <span><strong>SSCC:</strong> <code>{labelPreviewHu.sscc || labelPreviewHu.barcode || 'Chưa cấp SSCC'}</code></span>
              <span><strong>Loại kiện:</strong> {({ Carton: 'Thùng carton', Tote: 'Khay nhựa', Pallet: 'Pallet tiêu chuẩn', Container: 'Thùng hàng lớn', Custom: 'Tùy chỉnh' } as Record<string, string>)[labelPreviewHu.type] ?? labelPreviewHu.type}</span>
              <span><strong>Kho:</strong> {labelPreviewHu.warehouseId === 1 ? 'WH01 — Kho Chính TP.HCM' : `WH0${labelPreviewHu.warehouseId} — ${selected?.warehouseName || sessions[0]?.warehouseName || 'Kho Chính TP.HCM'}`}</span>
              <UiBadge tone={tone(labelPreviewHu.status)}>{huStateLabels[labelPreviewHu.status] ?? labelPreviewHu.status}</UiBadge>
            </div>
            <div><BarcodeSvg value={labelPreviewHu.barcode || labelPreviewHu.huCode} sscc={labelPreviewHu.sscc || '008938505000000018'} label={labelPreviewHu.huCode} qrCaption="Mã QR kiện hàng" /></div>
            <div>
              <strong>Danh sách SKU bên trong:</strong>
              {(labelPreviewHu.contents ?? []).length === 0 ? <p className="ui-muted-text">Chưa có hàng hóa bên trong HU.</p> : <ul>{labelPreviewHu.contents.map(c => <li key={c.id}>{c.productCode} — {c.productName}: <strong>{c.quantity}</strong></li>)}</ul>}
            </div>
            <div className="ui-inline-wrap">
              <button type="button" onClick={() => window.print()}>In nhãn</button>
              <button type="button" className="ui-btn-secondary" onClick={() => setLabelPreviewHu(null)}>Đóng xem trước</button>
            </div>
          </div>
        </UiCard>
      )}

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
              id: 1, huCode: 'HU-QA-001', barcode: 'HU-QA-001', sscc: '008938505000000018',
              warehouseId: selected?.warehouseId ?? sessions[0]?.warehouseId ?? 1,
              packingSessionId: selected?.id ?? sessions[0]?.id ?? 1,
              type: 'Carton', status: 'Closed', createdAt: new Date().toISOString(), rowVersion: 'v1', contents: [],
            })}
          >
            Xem &amp; In Nhãn SSCC / GS1-128
          </button>
        </div>
        {huRegistry.length === 0 ? (
          <p className="ui-muted-text">Chưa có dữ liệu danh mục đơn vị xử lý độc lập hoặc đang hiển thị theo phiên đóng gói phía trên.</p>
        ) : (
          <UiTableScroll>
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
                {huRegistry.map(item => (
                  <tr key={item.id}>
                    <td style={{ padding: '6px 4px', fontSize: 11, verticalAlign: 'middle' }}>
                      <strong style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 11 }}>{item.huCode}</strong>
                      <span style={{ display: 'block', fontSize: 10.5, color: '#64748b' }}>
                        {({ Carton: 'Thùng carton', Tote: 'Khay nhựa', Pallet: 'Pallet', Container: 'Thùng lớn', Custom: 'Tùy chỉnh' } as Record<string, string>)[item.type] ?? item.type}
                        {item.parentHandlingUnitCode ? ` • ${item.parentHandlingUnitCode}` : ''}
                      </span>
                    </td>
                    <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                      <code style={{ display: 'block', whiteSpace: 'nowrap', fontSize: 10.5 }}>{item.barcode}</code>
                      {item.sscc ? <small style={{ display: 'block', fontSize: 10, color: '#64748b', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>SSCC: {item.sscc}</small> : null}
                    </td>
                    <td style={{ padding: '6px 4px', fontSize: 10.5, verticalAlign: 'middle' }}>
                      <UiBadge tone={tone(item.status)}>{huStateLabels[item.status] ?? item.status}</UiBadge>
                      <div style={{ fontSize: 10, color: '#64748b', marginTop: 2 }}>SL: {roundQty4((item.contents ?? []).reduce((sum, c) => sum + c.quantity, 0))}</div>
                    </td>
                    <td style={{ padding: '8px 10px 8px 0px', fontSize: 10.5, whiteSpace: 'nowrap', textAlign: 'right', verticalAlign: 'middle' }}>
                      <button type="button" className="ui-btn-secondary" style={{ whiteSpace: 'nowrap', padding: '4px 8px', minHeight: 28, fontSize: 11, lineHeight: 1.2 }} onClick={() => setLabelPreviewHu(item)}>In nhãn</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </div>
  );
}
