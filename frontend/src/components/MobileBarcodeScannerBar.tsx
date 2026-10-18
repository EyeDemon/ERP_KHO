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
  Location: 'Vị trí kệ (Bin)',
  HandlingUnit: 'Đơn vị đóng gói (HU/Pallet)',
  Serial: 'Số Serial',
  Lot: 'Số Lô (Lot)',
  Product: 'Sản phẩm (SKU)',
  Unknown: 'Chưa nhận diện',
};

export function MobileBarcodeScannerBar({
  label = 'Quét mã vạch Mobile / RF (Camera hoặc súng quét)',
  placeholder = 'Nhập hoặc quét mã Bin, SKU, Lot, Serial, LPN...',
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
      setInput('');
    } finally {
      setBusy(false);
    }
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
      aria-label="Trạm quét mã vạch nhanh Mobile RF"
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
        className="ui-inline-wrap"
        style={{ alignItems: 'flex-end', gap: 8, width: '100%' }}
      >
        <label style={{ flex: '1 1 240px', display: 'grid', gap: 4, minWidth: 0 }}>
          <span>{label}</span>
          <input
            type="search"
            aria-label={label}
            value={input}
            disabled={disabled || busy}
            placeholder={placeholder}
            style={{ minHeight: 40, width: '100%', fontSize: 13, padding: '6px 10px' }}
            onChange={e => setInput(e.target.value)}
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
        </div>
        {lastResolved && (
          <UiBadge tone={lastResolved.entityType === 'Unknown' ? 'warning' : 'success'}>
            {`${ENTITY_LABELS[lastResolved.entityType] ?? lastResolved.entityType}: ${lastResolved.code ?? lastResolved.barcode}${lastResolved.name ? ` — ${lastResolved.name}` : ''}`}
          </UiBadge>
        )}
      </div>

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
          <small className="ui-muted-text">Hướng camera về phía mã vạch kệ (Bin) hoặc sản phẩm (SKU)...</small>
        </div>
      )}
    </section>
  );
}
export default MobileBarcodeScannerBar;
