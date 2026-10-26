import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { sanitizeScannerCode } from '../pages/productCatalog';
import { UiBadge } from '../ui/ProductionUi';
import {
  decodeBarcodeFromVideoFrame,
  describeEanPrefixCountry,
  isRetailProductBarcode,
} from '../utils/cameraBarcodeDecoder';

function playScannerBeep(): void {
  try {
    const AudioCtxCtor =
      (window as unknown as { AudioContext?: typeof AudioContext; webkitAudioContext?: typeof AudioContext })
        .AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AudioCtxCtor) return;
    const ctx = new AudioCtxCtor();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(880, ctx.currentTime);
    gain.gain.setValueAtTime(0.14, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.08);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.08);
    window.setTimeout(() => {
      void ctx.close().catch(() => {});
    }, 150);
  } catch {
    // Bỏ qua nếu trình duyệt chặn phát âm thanh tự động
  }
}

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

function isScannerStationVisible(sectionEl: HTMLElement | null): boolean {
  if (!sectionEl) return false;
  let curr: HTMLElement | null = sectionEl;
  while (curr) {
    if (curr.hidden || curr.getAttribute('aria-hidden') === 'true') return false;
    const style = curr.style;
    if (
      style &&
      (style.display === 'none' ||
        style.visibility === 'hidden' ||
        style.pointerEvents === 'none' ||
        style.opacity === '0')
    ) {
      return false;
    }
    curr = curr.parentElement;
  }
  return true;
}

function extractScannerChar(e: KeyboardEvent): string {
  if (e.key && e.key.length === 1) {
    return e.key;
  }
  const code = e.keyCode || e.which || e.charCode || 0;
  if (code >= 32 && code < 127) {
    return String.fromCharCode(code);
  }
  return '';
}

export function MobileBarcodeScannerBar({
  label = 'Quét mã vạch Thiết bị Cầm tay (Camera hoặc Súng quét)',
  placeholder = 'Nhập hoặc quét mã kệ, mã SP, số lô, số sê-ri, mã GS1-128...',
  sampleCode = 'BIN-A01',
  warehouseId,
  disabled = false,
  resolveViaApi = true,
  onScan,
}: MobileBarcodeScannerBarProps) {
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [cameraActive, setCameraActive] = useState(false);
  const [isCameraLoading, setIsCameraLoading] = useState(false);
  const [hasActiveVideoStream, setHasActiveVideoStream] = useState(false);
  const [cameraError, setCameraError] = useState(false);
  const [cameraFlashSuccess, setCameraFlashSuccess] = useState(false);
  const [cameraNotice, setCameraNotice] = useState('');
  const [lastResolved, setLastResolved] = useState<ResolvedBarcodeResult | null>(null);
  const [parsedGs1, setParsedGs1] = useState<ParsedGs1Data | null>(null);
  const [decodedProductBanner, setDecodedProductBanner] = useState<string>('');
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3>(1);
  const [autoAdvance, setAutoAdvance] = useState(true);
  const [stepHistory, setStepHistory] = useState<{ 1: string; 2: string; 3: string }>({
    1: '',
    2: '',
    3: '',
  });
  const [step3Completed, setStep3Completed] = useState(false);
  const [lastScanFeedback, setLastScanFeedback] = useState<{
    step: 1 | 2 | 3;
    code: string;
    transitionText: string;
  } | null>(null);

  const sectionRef = useRef<HTMLElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const timerRef = useRef<number | null>(null);
  const probeTimerRef = useRef<number | null>(null);
  const lastCameraScanRef = useRef<{ code: string; timeMs: number }>({ code: '', timeMs: 0 });

  const bufferRef = useRef('');
  const inputValueRef = useRef('');
  const wizardStepRef = useRef<1 | 2 | 3>(1);
  const autoAdvanceRef = useRef(true);
  const stepHistoryRef = useRef<{ 1: string; 2: string; 3: string }>({ 1: '', 2: '', 3: '' });
  const step3CompletedRef = useRef(false);
  const lastKeyTimeRef = useRef(0);
  const lastHandledKeydownTimeRef = useRef(0);
  const scanQueueRef = useRef<Promise<void>>(Promise.resolve());
  const triggerRef = useRef<(rawCode: string) => Promise<void>>(async () => {});

  useEffect(() => {
    wizardStepRef.current = wizardStep;
  }, [wizardStep]);

  useEffect(() => {
    autoAdvanceRef.current = autoAdvance;
  }, [autoAdvance]);

  const stopCamera = () => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (probeTimerRef.current !== null) {
      window.clearTimeout(probeTimerRef.current);
      probeTimerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
      streamRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
    setCameraActive(false);
    setIsCameraLoading(false);
    setHasActiveVideoStream(false);
    setCameraError(false);
  };

  useEffect(() => {
    return () => {
      stopCamera();
    };
  }, []);

  const handleTrigger = async (rawCode: string) => {
    const clean = sanitizeScannerCode(rawCode);
    if (!clean || disabled) return;

    bufferRef.current = '';
    inputValueRef.current = '';
    setInput('');
    setCameraNotice('');

    const gs1 = parseGs1BarcodeClient(rawCode);
    if (gs1) {
      setParsedGs1(gs1);
    }

    const isProductCode = Boolean(gs1) || isRetailProductBarcode(clean);
    let activeStep: 1 | 2 | 3 = wizardStepRef.current;
    let autoRoutedStageIn = false;

    if (activeStep === 3 && step3CompletedRef.current) {
      // Bắt đầu chu trình 3 bước mới nếu chu trình trước đã hoàn tất cả 3 bước
      step3CompletedRef.current = false;
      setStep3Completed(false);
      if (isProductCode) {
        stepHistoryRef.current = { 1: 'STAGE-IN-01', 2: '', 3: '' };
        activeStep = 2;
        autoRoutedStageIn = true;
      } else {
        stepHistoryRef.current = { 1: '', 2: '', 3: '' };
        activeStep = 1;
      }
    } else if (activeStep === 1 && isProductCode && !stepHistoryRef.current[1]) {
      // Tự động Định tuyến Bước Quét Thông Minh (Smart Step Auto-Routing):
      // Đang ở Bước 1 mà đưa thẳng Sản phẩm thật (EAN-13 / EAN-8 / UPC-A / GS1 / SP-...) vào quét
      stepHistoryRef.current = {
        ...stepHistoryRef.current,
        1: 'STAGE-IN-01',
      };
      activeStep = 2;
      autoRoutedStageIn = true;
    }

    const updatedHistory = {
      ...stepHistoryRef.current,
      [activeStep]: clean,
    };
    stepHistoryRef.current = updatedHistory;
    setStepHistory(updatedHistory);

    if (isProductCode) {
      const country = describeEanPrefixCountry(clean);
      const countrySegment = country ? ` • Quốc gia: ${country}` : '';
      const routingSegment = autoRoutedStageIn
        ? ' • Đã tự động gán Kệ nguồn STAGE-IN-01 & chuyển sang Bước 3 (Quét Kệ đích)'
        : ' • Chuyển sang Bước 3 (Quét Kệ đích)';
      setDecodedProductBanner(
        `📦 Đã nhận diện Mã vạch Sản phẩm (EAN-13 / GTIN): ${clean}${countrySegment}${routingSegment}`,
      );
    }

    let nextStep: 1 | 2 | 3 = activeStep;
    let transitionText = `Đã ghi nhận tại Bước ${activeStep}`;

    if (autoAdvanceRef.current) {
      if (activeStep === 1) {
        nextStep = 2;
        transitionText = 'Chuyển sang Bước 2';
      } else if (activeStep === 2) {
        nextStep = 3;
        transitionText = autoRoutedStageIn
          ? 'Đã tự động gán Kệ nguồn STAGE-IN-01 & chuyển sang Bước 3'
          : 'Chuyển sang Bước 3';
      } else {
        nextStep = 3;
        step3CompletedRef.current = true;
        setStep3Completed(true);
        transitionText = 'Hoàn tất chu trình 3 bước';
      }
      wizardStepRef.current = nextStep;
      setWizardStep(nextStep);
      queueMicrotask(() => inputRef.current?.focus());
    }

    setLastScanFeedback({
      step: activeStep,
      code: clean,
      transitionText,
    });

    const runAsyncTask = async () => {
      setBusy(true);
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
      } finally {
        setBusy(false);
      }
    };

    scanQueueRef.current = scanQueueRef.current.then(runAsyncTask, runAsyncTask);
    await scanQueueRef.current;
  };

  triggerRef.current = handleTrigger;

  useEffect(() => {
    if (disabled) return;

    const processScannerKeyEvent = (e: KeyboardEvent, isKeypressFallback: boolean) => {
      if (!isScannerStationVisible(sectionRef.current)) return;
      if (e.isTrusted && (e.ctrlKey || e.altKey || e.metaKey)) return;

      const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
      const deltaMs = now - lastKeyTimeRef.current;
      const isRapidHidScan = lastKeyTimeRef.current > 0 && deltaMs <= 80;

      const activeEl = (typeof document !== 'undefined' ? document.activeElement : null) as HTMLElement | null;
      const targetEl = (e.target as HTMLElement | null) ?? activeEl;
      const isScannerInput = targetEl === inputRef.current || activeEl === inputRef.current;

      const isOtherEditableField =
        !isScannerInput &&
        Boolean(
          targetEl &&
            (targetEl.tagName === 'INPUT' ||
              targetEl.tagName === 'TEXTAREA' ||
              targetEl.tagName === 'SELECT' ||
              targetEl.isContentEditable),
        );

      // Nếu người dùng đang gõ bàn phím thật bình thường (> 80ms) trong một ô input khác thì bỏ qua
      if (e.isTrusted && isOtherEditableField && !isRapidHidScan) {
        return;
      }

      const isEnter =
        e.key === 'Enter' || e.code === 'Enter' || e.keyCode === 13 || e.which === 13 || e.charCode === 13;

      if (isEnter) {
        lastKeyTimeRef.current = now;
        if (!isKeypressFallback) {
          lastHandledKeydownTimeRef.current = now;
        }
        const candidate = sanitizeScannerCode(bufferRef.current || inputValueRef.current);
        if (candidate) {
          e.preventDefault();
          e.stopPropagation();
          bufferRef.current = '';
          inputValueRef.current = '';
          setInput('');
          void triggerRef.current(candidate);
        }
        return;
      }

      const ch = extractScannerChar(e);
      if (!ch) return;

      lastKeyTimeRef.current = now;
      if (!isKeypressFallback) {
        lastHandledKeydownTimeRef.current = now;
      }

      // Khi sự kiện phát ra từ Chrome Extension Barcode Scan Simulator (!e.isTrusted)
      // hoặc khi focus không nằm ở inputRef.current (VD: document.body sau khi đóng popup Extension)
      // hoặc súng quét HID bắn tốc độ cao: tự ghép ký tự vào bufferRef và đồng bộ lên state input
      if (!e.isTrusted || !isScannerInput || isRapidHidScan) {
        bufferRef.current += ch;
        inputValueRef.current = bufferRef.current;
        setInput(bufferRef.current);
        const decoded = parseGs1BarcodeClient(bufferRef.current);
        if (decoded) {
          setParsedGs1(decoded);
        }
      }
    };

    const onWindowKeyDown = (e: KeyboardEvent) => {
      processScannerKeyEvent(e, false);
    };

    const onWindowKeyPress = (e: KeyboardEvent) => {
      const now = typeof performance !== 'undefined' ? performance.now() : Date.now();
      if (now - lastHandledKeydownTimeRef.current < 25) {
        return;
      }
      processScannerKeyEvent(e, true);
    };

    window.addEventListener('keydown', onWindowKeyDown);
    window.addEventListener('keypress', onWindowKeyPress);
    return () => {
      window.removeEventListener('keydown', onWindowKeyDown);
      window.removeEventListener('keypress', onWindowKeyPress);
    };
  }, [disabled]);

  const handleFillGs1Sample = () => {
    bufferRef.current = SAMPLE_GS1_CODE;
    inputValueRef.current = SAMPLE_GS1_CODE;
    setInput(SAMPLE_GS1_CODE);
    const decoded = parseGs1BarcodeClient(SAMPLE_GS1_CODE);
    setParsedGs1(decoded);
    wizardStepRef.current = 2;
    setWizardStep(2);
  };

  const startCameraStream = async () => {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current);
      timerRef.current = null;
    }
    if (probeTimerRef.current !== null) {
      window.clearTimeout(probeTimerRef.current);
      probeTimerRef.current = null;
    }
    if (streamRef.current) {
      streamRef.current.getTracks().forEach(track => track.stop());
      streamRef.current = null;
    }

    setCameraActive(true);
    setIsCameraLoading(true);
    setHasActiveVideoStream(false);
    setCameraError(false);
    setCameraNotice('');

    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setIsCameraLoading(false);
      setHasActiveVideoStream(false);
      setCameraError(true);
      setCameraNotice(
        'Trình duyệt hiện tại chưa kết nối Camera trực tiếp. Bạn có thể nhập mã hoặc dùng súng quét USB/Bluetooth ngay tại ô bên trên.',
      );
      return;
    }

    // Watchdog if getUserMedia hangs (e.g., permission prompt dismissed or virtual driver stalled)
    probeTimerRef.current = window.setTimeout(() => {
      const videoEl = videoRef.current;
      if (!videoEl || videoEl.readyState < 2 || videoEl.videoWidth <= 0) {
        setHasActiveVideoStream(false);
        setCameraError(true);
        setIsCameraLoading(false);
      }
    }, 3000);

    try {
      let stream: MediaStream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: {
            facingMode: { ideal: 'environment' },
            width: { ideal: 1280, min: 640 },
            height: { ideal: 720, min: 480 },
          },
        });
      } catch {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'environment' },
        });
      }
      streamRef.current = stream;

      const hasLiveTrack = stream.getVideoTracks().some(t => t.readyState === 'live' && t.enabled);
      if (!hasLiveTrack) {
        if (probeTimerRef.current !== null) {
          window.clearTimeout(probeTimerRef.current);
          probeTimerRef.current = null;
        }
        setIsCameraLoading(false);
        setHasActiveVideoStream(false);
        setCameraError(true);
        setCameraNotice(
          'Không tìm thấy luồng video từ Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
        );
        return;
      }

      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => {});
      }

      if (probeTimerRef.current !== null) {
        window.clearTimeout(probeTimerRef.current);
      }
      probeTimerRef.current = window.setTimeout(() => {
        const videoEl = videoRef.current;
        if (videoEl && videoEl.readyState >= 2 && videoEl.videoWidth > 0) {
          setHasActiveVideoStream(true);
          setCameraError(false);
          setIsCameraLoading(false);
        } else {
          setHasActiveVideoStream(false);
          setCameraError(true);
          setIsCameraLoading(false);
          setCameraNotice(
            'Thiết bị chưa cấp quyền hoặc không có Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
          );
        }
      }, 450);

      // Vòng lặp 15 FPS (65ms) giải mã đa dòng quét trên <canvas> + window.BarcodeDetector
      let decodingFrame = false;
      timerRef.current = window.setInterval(async () => {
        if (decodingFrame) return;
        const videoEl = videoRef.current;
        const canvasEl = canvasRef.current;
        if (!videoEl || !canvasEl || videoEl.readyState < 2 || videoEl.videoWidth <= 0) return;
        decodingFrame = true;
        try {
          const hit = await decodeBarcodeFromVideoFrame(videoEl, canvasEl);
          if (hit?.code) {
            const now = Date.now();
            if (
              lastCameraScanRef.current.code === hit.code &&
              now - lastCameraScanRef.current.timeMs < 2000
            ) {
              return;
            }
            lastCameraScanRef.current = { code: hit.code, timeMs: now };
            playScannerBeep();
            setCameraFlashSuccess(true);
            window.setTimeout(() => setCameraFlashSuccess(false), 550);
            await triggerRef.current(hit.code);
          }
        } catch {
          // Bỏ qua lỗi giải mã từng khung hình
        } finally {
          decodingFrame = false;
        }
      }, 65);
    } catch {
      setIsCameraLoading(false);
      setHasActiveVideoStream(false);
      setCameraError(true);
      setCameraNotice(
        'Thiết bị chưa cấp quyền hoặc không có Camera vật lý. Bạn có thể nhập mã trực tiếp, dùng súng quét USB/Bluetooth hoặc điền mã mẫu.',
      );
    }
  };

  const toggleCamera = async () => {
    if (cameraActive) {
      stopCamera();
      return;
    }
    await startCameraStream();
  };

  const isLiveVideo = cameraActive && hasActiveVideoStream && !cameraError;
  const isDisconnectedCamera = cameraActive && !isCameraLoading && (!hasActiveVideoStream || cameraError);

  return (
    <section
      ref={sectionRef}
      className="ui-card scanner-station-card"
      aria-label="Trạm quét mã vạch thiết bị cầm tay"
    >
      {/* Tầng 1 — Thanh Tiến trình Bước Quét */}
      <div className="scanner-header-bar">
        <div
          className="scanner-steps-list"
          data-testid="rf-scan-wizard-steps"
        >
          {([
            { step: 1 as const, text: 'Bước 1: Quét Vị trí / Kệ nguồn' },
            { step: 2 as const, text: 'Bước 2: Quét Mã vạch SP / GS1-128' },
            { step: 3 as const, text: 'Bước 3: Quét Kệ đích & Xác nhận' },
          ]).map((item, idx) => {
            const isCurrent = wizardStep === item.step;
            const isCompleted =
              wizardStep > item.step ||
              Boolean(stepHistory[item.step]) ||
              (item.step === 2 && Boolean(parsedGs1)) ||
              (item.step === 3 && step3Completed);
            return (
              <div key={item.step} style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                <span
                  data-testid={`wizard-step-pill-${item.step}`}
                  data-step-active={isCurrent ? 'true' : 'false'}
                  data-step-completed={isCompleted ? 'true' : 'false'}
                  onClick={() => {
                    wizardStepRef.current = item.step;
                    setWizardStep(item.step);
                  }}
                  style={{
                    cursor: 'pointer',
                    padding: '5px 11px',
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
        </div>

        <label className="scanner-auto-advance">
          <input
            type="checkbox"
            data-testid="scanner-auto-advance-toggle"
            checked={autoAdvance}
            onChange={e => {
              autoAdvanceRef.current = e.target.checked;
              setAutoAdvance(e.target.checked);
            }}
            style={{ width: 20, height: 20, minHeight: 20, margin: 0, flexShrink: 0 }}
          />
          <span>Tự động chuyển bước sau khi quét</span>
        </label>
      </div>

      {/* Tầng 2 — Bàn Làm Việc Quét Mã 2 Cột Cân Đối */}
      <div className="scanner-workbench-grid">
        {/* Cột Trái (320px) — Khung Ngắm Camera / Skeleton Camera */}
        <div
          className={`scanner-camera-viewport${isLiveVideo ? ' is-live' : ''}${isDisconnectedCamera ? ' is-warning' : ''}`}
          data-testid="scanner-camera-viewport"
          style={
            cameraFlashSuccess
              ? {
                  borderColor: '#10b981',
                  boxShadow: '0 0 0 4px rgba(16, 185, 129, 0.38)',
                }
              : undefined
          }
        >
          <span className="viewfinder-corner tl" aria-hidden="true" />
          <span className="viewfinder-corner tr" aria-hidden="true" />
          <span className="viewfinder-corner bl" aria-hidden="true" />
          <span className="viewfinder-corner br" aria-hidden="true" />

          <canvas ref={canvasRef} style={{ display: 'none' }} aria-hidden="true" />

          <video
            ref={videoRef}
            muted
            playsInline
            onLoadedData={() => {
              if (videoRef.current && videoRef.current.videoWidth > 0 && videoRef.current.readyState >= 2) {
                setHasActiveVideoStream(true);
                setCameraError(false);
                setIsCameraLoading(false);
              }
            }}
            style={
              isLiveVideo
                ? {
                    position: 'absolute',
                    inset: 0,
                    width: '100%',
                    height: '100%',
                    objectFit: 'cover',
                    borderRadius: 11,
                    zIndex: 1,
                  }
                : {
                    position: 'absolute',
                    width: 1,
                    height: 1,
                    opacity: 0,
                    pointerEvents: 'none',
                  }
            }
          />

          {/* 1. Khi Camera chưa bật (!cameraActive) — Skeleton Trạng thái Chờ */}
          {!cameraActive && (
            <div
              data-testid="scanner-camera-skeleton-idle"
              style={{
                position: 'relative',
                zIndex: 2,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 8,
                maxWidth: 276,
              }}
            >
              <svg width="42" height="42" viewBox="0 0 48 48" fill="none" aria-hidden="true">
                <rect x="6" y="12" width="36" height="26" rx="5" stroke="#38bdf8" strokeWidth="2.5" fill="rgba(56, 189, 248, 0.1)" />
                <path d="M17 12L19.5 8H28.5L31 12" stroke="#38bdf8" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
                <circle cx="24" cy="25" r="6.5" stroke="#e2e8f0" strokeWidth="2.2" />
                <path d="M13 21V29M35 21V29" stroke="#94a3b8" strokeWidth="2" strokeLinecap="round" />
              </svg>
              <strong style={{ fontSize: 13.5, color: '#f8fafc', lineHeight: 1.3 }}>
                Camera quét mã đang tắt
              </strong>
              <span style={{ fontSize: 11.5, color: '#cbd5e1', lineHeight: 1.4 }}>
                Bấm «Bật Camera» để quét bằng ống kính hoặc dùng súng quét bắn trực tiếp vào ô bên phải
              </span>
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                disabled={disabled || busy}
                onClick={() => void toggleCamera()}
                style={{
                  marginTop: 2,
                  minHeight: 34,
                  padding: '5px 12px',
                  fontSize: 12,
                  fontWeight: 700,
                  background: 'rgba(15, 23, 42, 0.85)',
                  color: '#38bdf8',
                  borderColor: '#38bdf8',
                }}
              >
                📷 Bật Camera
              </button>
            </div>
          )}

          {/* 2. Khi Đang khởi tạo / Đang chờ kết nối (cameraActive && isCameraLoading) — Skeleton Shimmer Động */}
          {cameraActive && isCameraLoading && (
            <>
              <div className="scanner-skeleton-shimmer" />
              <div className="scanner-laser-line" />
              <div
                data-testid="scanner-camera-skeleton-loading"
                style={{
                  position: 'relative',
                  zIndex: 3,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  gap: 6,
                  maxWidth: 276,
                  padding: '10px 14px',
                  borderRadius: 10,
                  background: 'rgba(15, 23, 42, 0.86)',
                  border: '1px solid rgba(56, 189, 248, 0.28)',
                }}
              >
                <svg width="36" height="36" viewBox="0 0 48 48" fill="none" aria-hidden="true">
                  <rect x="8" y="12" width="32" height="24" rx="4" stroke="#38bdf8" strokeWidth="2.5" strokeDasharray="4 3" />
                  <path d="M16 18V30M21 18V30M26 18V30M32 18V30" stroke="#e2e8f0" strokeWidth="2.2" strokeLinecap="round" />
                </svg>
                <strong style={{ fontSize: 13, color: '#f8fafc' }}>
                  Đang kết nối ống kính camera...
                </strong>
                <small style={{ fontSize: 11, color: '#cbd5e1', lineHeight: 1.35 }}>
                  Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
                </small>
              </div>
            </>
          )}

          {/* 3. Khi Chưa kết nối được Camera / Trình duyệt không có Webcam */}
          {isDisconnectedCamera && (
            <>
              <div className="scanner-skeleton-shimmer" />
              <div className="scanner-laser-line" />
              <div
                data-testid="scanner-camera-skeleton-disconnected"
                style={{
                  position: 'relative',
                  zIndex: 3,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 6,
                  width: '100%',
                  height: '100%',
                }}
              >
                <div
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    gap: 5,
                    maxWidth: 284,
                    padding: '8px 12px',
                    borderRadius: 10,
                    background: 'rgba(15, 23, 42, 0.88)',
                    border: '1px solid rgba(245, 158, 11, 0.35)',
                  }}
                >
                  <svg width="32" height="32" viewBox="0 0 48 48" fill="none" aria-hidden="true">
                    <rect x="6" y="12" width="36" height="26" rx="5" stroke="#f59e0b" strokeWidth="2.5" fill="rgba(245, 158, 11, 0.12)" />
                    <path d="M15 20V30M20 20V30M28 20V30M33 20V30" stroke="#fcd34d" strokeWidth="2" strokeLinecap="round" />
                    <path d="M10 10L38 38" stroke="#f87171" strokeWidth="2.5" strokeLinecap="round" />
                  </svg>
                  <strong style={{ fontSize: 12.5, color: '#fef3c7', lineHeight: 1.25 }}>
                    Chưa kết nối thiết bị Camera
                  </strong>
                  <span style={{ fontSize: 11, color: '#e2e8f0', lineHeight: 1.3 }}>
                    Không tìm thấy camera vật lý hoặc chưa cấp quyền trình duyệt. Hãy kết nối camera hoặc dùng súng quét ở khung bên phải
                  </span>
                  <button
                    type="button"
                    className="ui-btn ui-btn-secondary"
                    disabled={disabled || busy}
                    onClick={() => void startCameraStream()}
                    style={{
                      minHeight: 28,
                      padding: '3px 10px',
                      fontSize: 11.5,
                      fontWeight: 700,
                      background: 'rgba(15, 23, 42, 0.95)',
                      color: '#fcd34d',
                      borderColor: '#f59e0b',
                    }}
                  >
                    Thử kết nối lại
                  </button>
                </div>
                <small style={{ fontSize: 10.5, color: '#cbd5e1', lineHeight: 1.2, background: 'rgba(15, 23, 42, 0.75)', padding: '2px 8px', borderRadius: 4 }}>
                  Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
                </small>
              </div>
            </>
          )}

          {/* 4. Khi Camera đã kết nối và có hình thật */}
          {isLiveVideo && (
            <>
              <div className="scanner-laser-line" />
              <small
                style={{
                  position: 'absolute',
                  bottom: 8,
                  left: 12,
                  right: 12,
                  zIndex: 3,
                  padding: '3px 8px',
                  borderRadius: 6,
                  background: 'rgba(15, 23, 42, 0.78)',
                  color: '#f8fafc',
                  fontSize: 11,
                }}
              >
                Hướng camera về phía mã vạch kệ hoặc sản phẩm (SKU)...
              </small>
            </>
          )}
        </div>

        {/* Cột Phải (minmax(0, 1fr)) — Bảng Điều Khiển Súng Quét & Nhập Mã */}
        <div
          className="scanner-control-panel"
          data-testid="scanner-control-panel"
        >
          {/* Hàng 1: Header của khung điều khiển */}
          <div className="scanner-control-header">
            <span className="scanner-control-title">{label}</span>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <UiBadge tone="success">🟢 Sẵn sàng nhận súng quét</UiBadge>
              {isLiveVideo ? (
                <UiBadge tone="success">🟢 Camera đang quét</UiBadge>
              ) : cameraActive && isCameraLoading ? (
                <UiBadge tone="warning">🟡 Đang kết nối Camera</UiBadge>
              ) : isDisconnectedCamera ? (
                <UiBadge tone="warning">🟠 Chưa kết nối được Camera</UiBadge>
              ) : (
                <UiBadge tone="neutral">⚪ Camera chưa bật</UiBadge>
              )}
            </div>
          </div>

          {/* Hàng 2: Input + Action Buttons */}
          <div className="scanner-input-row">
            <input
              ref={inputRef}
              type="search"
              aria-label={label}
              value={input}
              disabled={disabled}
              placeholder={placeholder}
              onChange={e => {
                const nextVal = e.target.value;
                bufferRef.current = nextVal;
                inputValueRef.current = nextVal;
                setInput(nextVal);
                const decoded = parseGs1BarcodeClient(nextVal);
                if (decoded) setParsedGs1(decoded);
              }}
              onKeyDown={e => {
                if (e.key === 'Enter' || e.keyCode === 13) {
                  e.preventDefault();
                  e.stopPropagation();
                  const candidate = sanitizeScannerCode(input || bufferRef.current);
                  bufferRef.current = '';
                  inputValueRef.current = '';
                  setInput('');
                  if (candidate) {
                    void handleTrigger(candidate);
                  }
                }
              }}
            />
            <button
              type="button"
              className="ui-btn ui-btn-primary ui-primary-button"
              style={{ height: 42, minHeight: 42, whiteSpace: 'nowrap' }}
              disabled={disabled || busy || !sanitizeScannerCode(input)}
              onClick={() => void handleTrigger(input)}
            >
              {busy ? 'Đang xử lý...' : 'Xác nhận mã quét'}
            </button>
            <button
              type="button"
              data-testid="btn-toggle-camera"
              className="ui-btn ui-btn-secondary"
              style={{ height: 42, minHeight: 42, whiteSpace: 'nowrap' }}
              disabled={disabled || busy}
              onClick={() => void toggleCamera()}
            >
              {cameraActive ? 'Tắt Camera' : 'Bật Camera'}
            </button>
            {import.meta.env.VITE_QA_WEBHOOK_SIMULATOR === 'true' && (
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                style={{ height: 42, minHeight: 42, whiteSpace: 'nowrap' }}
                disabled={disabled || busy}
                onClick={handleFillGs1Sample}
              >
                Điền mẫu GS1-128
              </button>
            )}
          </div>

          {/* Hàng 2B: Phản hồi Mã vừa nhận & Lịch sử 3 Bước Quét (Hỗ trợ Barcode Scan Simulator & Súng quét HID) */}
          {lastScanFeedback && (
            <div
              data-testid="scanner-last-scan-feedback"
              aria-live="polite"
              style={{
                display: 'flex',
                flexWrap: 'wrap',
                alignItems: 'center',
                gap: 8,
                padding: '7px 12px',
                borderRadius: 8,
                border: '1px solid #a7f3d0',
                background: '#ecfdf5',
                color: '#065f46',
                fontSize: 12.5,
                fontWeight: 700,
              }}
            >
              <span>{`✅ Đã nhận mã [Bước ${lastScanFeedback.step}]: ${lastScanFeedback.code} → ${lastScanFeedback.transitionText}`}</span>
            </div>
          )}

          <div
            data-testid="scanner-step-history-chips"
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              gap: 8,
            }}
          >
            {([
              { step: 1 as const, labelText: 'Bước 1 (Vị trí/Kệ nguồn)', val: stepHistory[1] },
              { step: 2 as const, labelText: 'Bước 2 (Mã SP/GS1-128)', val: stepHistory[2] },
              { step: 3 as const, labelText: 'Bước 3 (Kệ đích)', val: stepHistory[3] },
            ]).map(chip => {
              const hasValue = Boolean(chip.val);
              return (
                <span
                  key={chip.step}
                  data-testid={`scanner-step-chip-${chip.step}`}
                  title={hasValue ? `${chip.labelText}: ${chip.val}` : `${chip.labelText}: Chưa quét`}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 6,
                    padding: '4px 10px',
                    borderRadius: 999,
                    fontSize: 12,
                    fontWeight: hasValue ? 700 : 600,
                    background: hasValue ? '#ecfdf5' : '#ffffff',
                    color: hasValue ? '#065f46' : '#475569',
                    border: `1px solid ${hasValue ? '#a7f3d0' : '#cbd5e1'}`,
                    maxWidth: '100%',
                  }}
                >
                  <span>{`Bước ${chip.step}:`}</span>
                  <code
                    style={{
                      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
                      fontSize: 11.5,
                      color: hasValue ? '#047857' : '#64748b',
                      maxWidth: 280,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {chip.val || 'Chưa quét'}
                  </code>
                </span>
              );
            })}
          </div>

          {decodedProductBanner && (
            <div
              data-testid="product-barcode-decoded-banner"
              aria-live="polite"
              style={{
                display: 'flex',
                flexWrap: 'wrap',
                alignItems: 'center',
                gap: 8,
                padding: '8px 12px',
                borderRadius: 8,
                border: '1px solid #a7f3d0',
                background: '#ecfdf5',
                color: '#065f46',
                fontSize: 12.5,
                fontWeight: 600,
              }}
            >
              <span>{decodedProductBanner}</span>
            </div>
          )}

          {lastResolved && (
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
              <UiBadge tone={lastResolved.entityType === 'Unknown' ? 'warning' : 'success'}>
                {`${ENTITY_LABELS[lastResolved.entityType] ?? lastResolved.entityType}: ${lastResolved.code ?? lastResolved.barcode}${lastResolved.name ? ` — ${lastResolved.name}` : ''}`}
              </UiBadge>
            </div>
          )}

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
                fontSize: 12.5,
              }}
            >
              <span>{cameraNotice}</span>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                <button
                  type="button"
                  className="ui-btn ui-btn-secondary"
                  style={{ minHeight: 34, padding: '4px 10px', whiteSpace: 'nowrap' }}
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
                  className="ui-btn ui-btn-secondary"
                  style={{ minHeight: 34, padding: '4px 10px', whiteSpace: 'nowrap' }}
                  onClick={() => setCameraNotice('')}
                >
                  Đóng
                </button>
              </div>
            </div>
          )}

          {/* Hàng 3: Thanh gợi ý chuẩn GS1-128 & thao tác nhanh */}
          <div className="scanner-hint-footer">
            💡 Hỗ trợ súng quét USB/Bluetooth (nhấn Enter tự động gửi) &amp; chuẩn GS1-128: (01) GTIN • (10) Số lô • (17) Hạn dùng • (21) Sê-ri
          </div>
        </div>
      </div>
    </section>
  );
}
export default MobileBarcodeScannerBar;
