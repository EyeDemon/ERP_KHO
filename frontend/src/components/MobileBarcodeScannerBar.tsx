import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { sanitizeScannerCode } from '../pages/productCatalog';
import { UiBadge } from '../ui/ProductionUi';

export interface ResolvedBarcodeResult {
  barcode: string;
  entityType: 'Location' | 'HandlingUnit' | 'Serial' | 'Lot' | 'Product' | 'Unknown';
  entityId?: number | null;
  code?: string | null;
  name?: string | null;
  warehouseId?: number | null;
  productId?: number | null;
  productCode?: string | null;
  productName?: string | null;
  unitId?: number | null;
  unitCode?: string | null;
  conversionFactor?: number | null;
  status?: string | null;
}

export interface ParsedGs1Data {
  raw: string;
  gtin?: string;
  lotNumber?: string;
  expiryDate?: string;
  expiryDisplay?: string;
  serialNumber?: string;
  sscc?: string;
}

export function parseGs1BarcodeClient(rawInput: string): ParsedGs1Data | null {
  const text = rawInput.trim();
  if (!text) return null;
  const hasGs1Markers = /\((00|01|10|17|21)\)/.test(text) || /^01\d{14}10/.test(text);
  if (!hasGs1Markers) return null;

  const gtinMatch = text.match(/\(01\)(\d{13,14})/) || text.match(/^01(\d{14})/);
  const ssccMatch = text.match(/\(00\)(\d{18})/);
  const lotMatch = text.match(/\(10\)([^(]+)/);
  const expMatch = text.match(/\(17\)(\d{6})/);
  const serMatch = text.match(/\(21\)([^(]+)/);

  let expiryDate: string | undefined;
  let expiryDisplay: string | undefined;
  if (expMatch?.[1]) {
    const yy = expMatch[1].slice(0, 2);
    const mm = expMatch[1].slice(2, 4);
    const dd = expMatch[1].slice(4, 6);
    expiryDate = `20${yy}-${mm}-${dd}`;
    expiryDisplay = `${dd}/${mm}/20${yy}`;
  }

  return {
    raw: text,
    gtin: gtinMatch?.[1],
    sscc: ssccMatch?.[1],
    lotNumber: lotMatch?.[1]?.trim(),
    expiryDate,
    expiryDisplay,
    serialNumber: serMatch?.[1]?.trim(),
  };
}

export interface MobileBarcodeScannerBarProps {
  label?: string;
  placeholder?: string;
  sampleCode?: string;
  warehouseId?: number;
  disabled?: boolean;
  resolveViaApi?: boolean;
  onScan: (code: string, resolved?: ResolvedBarcodeResult | null) => void | Promise<void>;
}

const ENTITY_LABELS: Record<string, string> = {
  Location: 'Vị trí kệ',
  HandlingUnit: 'Đơn vị đóng gói (Kiện/Pallet)',
  Serial: 'Số sê-ri',
  Lot: 'Số lô',
  Product: 'Sản phẩm (SKU)',
  Unknown: 'Chưa nhận diện',
};

const SAMPLE_GS1_CODE = '(01)08938505000517(10)LOT-QA-002(17)271231(21)SER-QA-01';

export function MobileBarcodeScannerBar({
  label = 'Quét mã vạch Thiết bị Cầm tay (Camera hoặc Súng quét)',
  placeholder = 'Nhập hoặc quét mã kệ, mã SP, số lô, số sê-ri, mã kiện...',
  sampleCode = 'BIN-A01',
  warehouseId,
  disabled = false,
  resolveViaApi = true,
  onScan,
}: MobileBarcodeScannerBarProps) {
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [cameraActive, setCameraActive] = useState(false);
  const [cameraNotice, setCameraNotice] = useState('');
  const [lastResolved, setLastResolved] = useState<ResolvedBarcodeResult | null>(null);
  const [parsedGs1, setParsedGs1] = useState<ParsedGs1Data | null>(null);
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3>(1);
  const [autoAdvance, setAutoAdvance] = useState(true);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const timerRef = useRef<number | null>(null);

  const stopCamera = () => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
      streamRef.current = null;
    }
    setCameraActive(false);
  };

  useEffect(() => {
    return () => {
      stopCamera();
    };
  }, []);

  const handleTrigger = async (rawCode: string) => {
    const gs1 = parseGs1BarcodeClient(rawCode);
    if (gs1) {
      setParsedGs1(gs1);
    }
    const clean = sanitizeScannerCode(rawCode);
    if (!clean || busy || disabled) return;
    setBusy(true);
    setCameraNotice('');
    try {
      let resolved: ResolvedBarcodeResult | null = null;
      if (resolveViaApi) {
        try {
          const res = await apiClient.get<ResolvedBarcodeResult>('/api/barcode-lookup/resolve', {
            params: { code: clean, warehouseId: warehouseId || undefined },
          });
          resolved = res.data ?? null;
          setLastResolved(resolved);
        } catch {
          setLastResolved(null);
        }
      }
      await onScan(clean, resolved);
      if (autoAdvance) {
        setWizardStep(prev => (gs1 ? 3 : prev === 1 ? 2 : 3));
        queueMicrotask(() => inputRef.current?.focus());
      }
      setInput('');
    } finally {
      setBusy(false);
    }
  };

  const handleFillGs1Sample = () => {
    setInput(SAMPLE_GS1_CODE);
    const decoded = parseGs1BarcodeClient(SAMPLE_GS1_CODE);
    setParsedGs1(decoded);
    setWizardStep(2);
  };

  const toggleCamera = async () => {
    if (cameraActive) {
      stopCamera();
      return;
    }
    setCameraNotice('');
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setCameraNotice(
        'Trình duyệt hiện tại chưa kết nối Camera trực tiếp. Bạn có thể nhập mã hoặc dùng súng quét USB/Bluetooth ngay tại ô bên trên.',
      );
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment' },
      });
      streamRef.current = stream;
      setCameraActive(true);
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => {});
      }

      const BarcodeDetectorCtor = (
        window as unknown as {
          BarcodeDetector?: new (opts?: { formats?: string[] }) => {
            detect: (el: HTMLVideoElement) => Promise<Array<{ rawValue?: string }>>;
          };
        }
      ).BarcodeDetector;
      if (BarcodeDetectorCtor) {
        const detector = new BarcodeDetectorCtor({
          formats: ['code_128', 'ean_13', 'ean_8', 'qr_code', 'code_39', 'upc_a', 'itf'],
        });
        timerRef.current = window.setInterval(async () => {
          if (!videoRef.current || videoRef.current.readyState < 2) return;
          try {
            const codes = await detector.detect(videoRef.current);
            const first = codes.find(c => Boolean(c.rawValue));
            if (first?.rawValue) {
              stopCamera();
              await handleTrigger(first.rawValue);
            }
          } catch {
            // Ignore frame detection error
          }
        }, 350);
      }
    } catch {
      setCameraNotice(
        'Thiết bị chưa cấp quyền hoặc không có Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
      );
      stopCamera();
    }
  };

  return (
    <section
      className="ui-card"
      aria-label="Trạm quét mã vạch thiết bị cầm tay"
      style={{
        padding: '14px 16px',
        marginBottom: 16,
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
        border: '1px solid #cbd5e1',
        background: '#f8fafc',
      }}
    >
      <div
        data-testid="rf-scan-wizard-steps"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          padding: '6px 10px',
          borderRadius: 8,
          background: '#ffffff',
          border: '1px solid #e2e8f0',
          fontSize: 12,
        }}
      >
        {([
          { step: 1 as const, text: 'Bước 1: Quét Vị trí / Kệ nguồn' },
          { step: 2 as const, text: 'Bước 2: Quét Mã vạch SP / GS1-128' },
          { step: 3 as const, text: 'Bước 3: Quét Kệ đích & Xác nhận' },
        ]).map((item, idx) => {
          const isCurrent = wizardStep === item.step;
          const isCompleted = wizardStep > item.step || (item.step === 2 && Boolean(parsedGs1));
          return (
            <div key={item.step} style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
              <span
                onClick={() => setWizardStep(item.step)}
                style={{
                  cursor: 'pointer',
                  padding: '4px 10px',
                  borderRadius: 999,
                  fontWeight: isCurrent || isCompleted ? 700 : 600,
                  background: isCompleted ? '#ecfdf5' : isCurrent ? '#dbeafe' : '#f1f5f9',
                  color: isCompleted ? '#065f46' : isCurrent ? '#1e3a8a' : '#475569',
                  border: `1px solid ${isCompleted ? '#a7f3d0' : isCurrent ? '#93c5fd' : '#cbd5e1'}`,
                  whiteSpace: 'nowrap',
                }}
              >
                {item.text}
              </span>
              {idx < 2 && <span style={{ color: '#64748b', fontWeight: 700 }}>→</span>}
            </div>
          );
        })}
        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginLeft: 'auto', cursor: 'pointer', fontWeight: 600, color: '#0f172a' }}>
          <input
            type="checkbox"
            data-testid="scanner-auto-advance-toggle"
            checked={autoAdvance}
            onChange={e => setAutoAdvance(e.target.checked)}
          />
          <span>Tự động chuyển bước sau khi quét (Auto-advance)</span>
        </label>
      </div>
      <div
        className="ui-inline-wrap"
        style={{ alignItems: 'flex-end', gap: 8, width: '100%' }}
      >
        <label style={{ flex: '1 1 240px', display: 'grid', gap: 4, minWidth: 0 }}>
          <span>{label}</span>
          <input
            ref={inputRef}
            type="search"
            aria-label={label}
            value={input}
            disabled={disabled || busy}
            placeholder={placeholder}
            style={{ minHeight: 40, width: '100%', fontSize: 13, padding: '6px 10px' }}
            onChange={e => {
              const nextVal = e.target.value;
              setInput(nextVal);
              const decoded = parseGs1BarcodeClient(nextVal);
              if (decoded) setParsedGs1(decoded);
            }}
            onKeyDown={e => {
              if (e.key === 'Enter') {
                e.preventDefault();
                void handleTrigger(input);
              }
            }}
          />
        </label>
        <div
          className="ui-inline-actions"
          style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}
        >
          <button
            type="button"
            className="ui-primary-button"
            style={{ minHeight: 40, whiteSpace: 'nowrap' }}
            disabled={disabled || busy || !sanitizeScannerCode(input)}
            onClick={() => void handleTrigger(input)}
          >
            {busy ? 'Đang xử lý...' : 'Xác nhận mã quét'}
          </button>
          <button
            type="button"
            style={{ minHeight: 40, whiteSpace: 'nowrap' }}
            disabled={disabled || busy}
            onClick={() => void toggleCamera()}
          >
            {cameraActive ? 'Tắt Camera' : 'Bật Camera Quét'}
          </button>
          <button
            type="button"
            style={{ minHeight: 40, whiteSpace: 'nowrap' }}
            disabled={disabled || busy}
            onClick={handleFillGs1Sample}
          >
            Điền mẫu GS1-128
          </button>
        </div>
        {lastResolved && (
          <UiBadge tone={lastResolved.entityType === 'Unknown' ? 'warning' : 'success'}>
            {`${ENTITY_LABELS[lastResolved.entityType] ?? lastResolved.entityType}: ${lastResolved.code ?? lastResolved.barcode}${lastResolved.name ? ` — ${lastResolved.name}` : ''}`}
          </UiBadge>
        )}
      </div>

      {parsedGs1 && (
        <div
          role="status"
          data-testid="gs1-parsed-summary"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 8,
            alignItems: 'center',
            padding: '8px 12px',
            borderRadius: 8,
            border: '1px solid #a7f3d0',
            background: '#ecfdf5',
            color: '#065f46',
            fontSize: 12.5,
          }}
        >
          <strong>Đã giải mã GS1-128:</strong>
          {parsedGs1.gtin && <UiBadge tone="success">{`Mã GTIN / SKU: ${parsedGs1.gtin.replace(/^0/, '')} (${parsedGs1.gtin})`}</UiBadge>}
          {parsedGs1.lotNumber && <UiBadge tone="info">{`Số Lô: ${parsedGs1.lotNumber}`}</UiBadge>}
          {parsedGs1.expiryDisplay && <UiBadge tone="warning">{`Hạn dùng: ${parsedGs1.expiryDisplay} (${parsedGs1.expiryDate})`}</UiBadge>}
          {(parsedGs1.serialNumber || parsedGs1.sscc) && (
            <UiBadge tone="neutral">{`Số Sê-ri / SSCC: ${parsedGs1.serialNumber || parsedGs1.sscc}`}</UiBadge>
          )}
        </div>
      )}

      {cameraNotice && (
        <div
          role="status"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 8,
            padding: '8px 12px',
            borderRadius: 6,
            border: '1px solid #cbd5e1',
            background: '#ffffff',
            color: '#334155',
            fontSize: 13,
          }}
        >
          <span>{cameraNotice}</span>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button
              type="button"
              style={{ minHeight: 36, whiteSpace: 'nowrap' }}
              disabled={disabled || busy}
              onClick={() => {
                setInput(sampleCode);
                setCameraNotice('');
                void handleTrigger(sampleCode);
              }}
            >
              Điền mã mẫu
            </button>
            <button
              type="button"
              style={{ minHeight: 36, whiteSpace: 'nowrap' }}
              onClick={() => setCameraNotice('')}
            >
              Đóng
            </button>
          </div>
        </div>
      )}

      {cameraActive && (
        <div style={{ maxWidth: 360, border: '1px solid #cbd5e1', borderRadius: 8, padding: 8, background: '#ffffff' }}>
          <video ref={videoRef} muted playsInline style={{ width: '100%', borderRadius: 6 }} />
          <small className="ui-muted-text">Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...</small>
        </div>
      )}
    </section>
  );
}
export default MobileBarcodeScannerBar;
