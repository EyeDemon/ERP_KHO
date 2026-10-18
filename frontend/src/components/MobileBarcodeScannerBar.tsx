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

interface MobileBarcodeScannerBarProps {
  label?: string;
  placeholder?: string;
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
  placeholder = 'Quét hoặc nhập mã vạch Bin, SKU, Lot, Serial, HU rồi nhấn Enter...',
  warehouseId,
  disabled = false,
  resolveViaApi = true,
  onScan,
}: MobileBarcodeScannerBarProps) {
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [cameraActive, setCameraActive] = useState(false);
  const [cameraError, setCameraError] = useState('');
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
    setCameraError('');
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
    setCameraError('');
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setCameraError('Trình duyệt hiện tại không hỗ trợ truy cập Camera trực tiếp. Vui lòng dùng súng quét hoặc nhập mã.');
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

      const BarcodeDetectorCtor = (window as unknown as { BarcodeDetector?: new (opts?: { formats?: string[] }) => { detect: (el: HTMLVideoElement) => Promise<Array<{ rawValue?: string }>> } }).BarcodeDetector;
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
      setCameraError('Không thể mở Camera thiết bị. Kiểm tra quyền Camera hoặc nhập mã trực tiếp.');
      stopCamera();
    }
  };

  return (
    <div className="ui-stack" style={{ marginBottom: 8 }}>
      <div className="ui-inline-wrap" style={{ alignItems: 'flex-end' }}>
        <label style={{ flex: '1 1 280px' }}>
          {label}
          <input
            aria-label={label}
            value={input}
            disabled={disabled || busy}
            placeholder={placeholder}
            onChange={e => setInput(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') {
                e.preventDefault();
                void handleTrigger(input);
              }
            }}
          />
        </label>
        <button
          type="button"
          className="ui-primary-button"
          disabled={disabled || busy || !sanitizeScannerCode(input)}
          onClick={() => void handleTrigger(input)}
        >
          {busy ? 'Đang xử lý...' : 'Xác nhận mã quét'}
        </button>
        <button
          type="button"
          disabled={disabled || busy}
          onClick={() => void toggleCamera()}
        >
          {cameraActive ? 'Tắt Camera' : 'Bật Camera Quét'}
        </button>
        {lastResolved && (
          <UiBadge tone={lastResolved.entityType === 'Unknown' ? 'warning' : 'success'}>
            {`${ENTITY_LABELS[lastResolved.entityType] ?? lastResolved.entityType}: ${lastResolved.code ?? lastResolved.barcode}${lastResolved.name ? ` — ${lastResolved.name}` : ''}`}
          </UiBadge>
        )}
      </div>
      {cameraError && <p role="alert" className="ui-muted-text">{cameraError}</p>}
      {cameraActive && (
        <div style={{ maxWidth: 360, border: '1px solid #cbd5e1', borderRadius: 8, padding: 8 }}>
          <video ref={videoRef} muted playsInline style={{ width: '100%', borderRadius: 6 }} />
          <small className="ui-muted-text">Hướng camera về phía mã vạch kệ (Bin) hoặc sản phẩm (SKU)...</small>
        </div>
      )}
    </div>
  );
}
export default MobileBarcodeScannerBar;
