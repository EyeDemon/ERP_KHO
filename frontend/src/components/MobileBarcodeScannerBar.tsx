import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { useHardwareScannerWedge } from '../hooks/useHardwareScannerWedge';
import { sanitizeScannerCode } from '../pages/productCatalog';
import { UiBadge } from '../ui/ProductionUi';
import { describeEanPrefixCountry, isRetailProductBarcode } from '../utils/cameraBarcodeDecoder';
import {
  parseGs1BarcodeClient,
  playScannerBeep,
  playScannerErrorBuzz,
} from './scanner/scannerAudioAndGs1';
import { ScannerCameraViewport } from './scanner/ScannerCameraViewport';
import { ScannerFeedbackPanels, ScannerWizardHeader } from './scanner/ScannerStatusAndSteps';
import {
  SAMPLE_GS1_CODE,
  type LastScanFeedback,
  type MobileBarcodeScannerBarProps,
  type ParsedGs1Data,
  type ResolvedBarcodeResult,
} from './scanner/scannerTypes';
import { useScannerCameraController } from './scanner/useScannerCameraController';
import { useScannerKeyboardCapture } from './scanner/useScannerKeyboardCapture';

export { playScannerBeep, playScannerErrorBuzz, parseGs1BarcodeClient };
export type { ResolvedBarcodeResult, ParsedGs1Data, MobileBarcodeScannerBarProps };

export function MobileBarcodeScannerBar({
  label = 'Quét mã vạch Thiết bị Cầm tay (Camera hoặc Súng quét)',
  placeholder = 'Quét mã kệ, mã SP hoặc số lô...',
  sampleCode = 'BIN-A01',
  warehouseId,
  disabled = false,
  resolveViaApi = true,
  stepLabels,
  onScan,
  onUndoLastScan,
}: MobileBarcodeScannerBarProps) {
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [scanHistoryStack, setScanHistoryStack] = useState<string[]>([]);
  const [lastResolved, setLastResolved] = useState<ResolvedBarcodeResult | null>(null);
  const [parsedGs1, setParsedGs1] = useState<ParsedGs1Data | null>(null);
  const [decodedProductBanner, setDecodedProductBanner] = useState<string>('');
  const [wizardStep, setWizardStep] = useState<1 | 2 | 3>(1);
  const [autoAdvance, setAutoAdvance] = useState(true);
  const [stepHistory, setStepHistory] = useState<{ 1: string; 2: string; 3: string }>({ 1: '', 2: '', 3: '' });
  const [step3Completed, setStep3Completed] = useState(false);
  const [lastScanFeedback, setLastScanFeedback] = useState<LastScanFeedback | null>(null);
  const [showGs1Hint, setShowGs1Hint] = useState(false);
  const [isMobileViewport, setIsMobileViewport] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth > 0 && window.innerWidth <= 767,
  );
  const [isCompact480, setIsCompact480] = useState<boolean>(
    () => typeof window !== 'undefined' && window.innerWidth > 0 && window.innerWidth <= 480,
  );

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const onResize = () => {
      setIsMobileViewport(window.innerWidth > 0 && window.innerWidth <= 767);
      setIsCompact480(window.innerWidth > 0 && window.innerWidth <= 480);
    };
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  const sectionRef = useRef<HTMLElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const bufferRef = useRef('');
  const inputValueRef = useRef('');
  const wizardStepRef = useRef<1 | 2 | 3>(1);
  const autoAdvanceRef = useRef(true);
  const stepHistoryRef = useRef<{ 1: string; 2: string; 3: string }>({ 1: '', 2: '', 3: '' });
  const step3CompletedRef = useRef(false);
  const scanQueueRef = useRef<Promise<void>>(Promise.resolve());
  const triggerRef = useRef<(rawCode: string) => Promise<void>>(async () => {});

  const {
    cameraActive, isCameraLoading, cameraFlashSuccess, cameraNotice, setCameraNotice,
    videoRef, canvasRef, startCameraStream, toggleCamera, onVideoLoadedData, isLiveVideo, isDisconnectedCamera,
  } = useScannerCameraController(triggerRef);

  useEffect(() => { wizardStepRef.current = wizardStep; }, [wizardStep]);
  useEffect(() => { autoAdvanceRef.current = autoAdvance; }, [autoAdvance]);

  const handleTrigger = async (rawCode: string) => {
    const clean = sanitizeScannerCode(rawCode);
    if (!clean || disabled) {
      if (!clean && !disabled) playScannerErrorBuzz();
      return;
    }

    bufferRef.current = '';
    inputValueRef.current = '';
    setInput('');
    setCameraNotice('');
    setScanHistoryStack(prev => [...prev, clean]);

    const gs1 = parseGs1BarcodeClient(rawCode);
    if (gs1) setParsedGs1(gs1);

    const isProductCode = Boolean(gs1) || isRetailProductBarcode(clean);
    let activeStep: 1 | 2 | 3 = wizardStepRef.current;
    let autoRoutedStageIn = false;

    if (activeStep === 3 && step3CompletedRef.current) {
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
      stepHistoryRef.current = { ...stepHistoryRef.current, 1: 'STAGE-IN-01' };
      activeStep = 2;
      autoRoutedStageIn = true;
    }

    const updatedHistory = { ...stepHistoryRef.current, [activeStep]: clean };
    stepHistoryRef.current = updatedHistory;
    setStepHistory(updatedHistory);

    if (isProductCode) {
      const country = describeEanPrefixCountry(clean);
      const countrySegment = country ? ` • Quốc gia: ${country}` : '';
      const routingSegment = autoRoutedStageIn
        ? ' • Đã tự động gán Kệ nguồn STAGE-IN-01 & chuyển sang Bước 3 (Quét Kệ đích)'
        : ' • Chuyển sang Bước 3 (Quét Kệ đích)';
      setDecodedProductBanner(`Đã nhận diện Mã vạch Sản phẩm (EAN-13 / GTIN): ${clean}${countrySegment}${routingSegment}`);
    }

    let nextStep: 1 | 2 | 3 = activeStep;
    let transitionText = `Đã ghi nhận tại Bước ${activeStep}`;

    if (autoAdvanceRef.current) {
      if (activeStep === 1) {
        nextStep = 2;
        transitionText = 'Chuyển sang Bước 2';
      } else if (activeStep === 2) {
        nextStep = 3;
        transitionText = autoRoutedStageIn ? 'Đã tự động gán Kệ nguồn STAGE-IN-01 & chuyển sang Bước 3' : 'Chuyển sang Bước 3';
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

    setLastScanFeedback({ step: activeStep, code: clean, transitionText });

    const runAsyncTask = async () => {
      setBusy(true);
      try {
        let resolved: ResolvedBarcodeResult | null = null;
        if (resolveViaApi) {
          try {
            const res = await apiClient.get<ResolvedBarcodeResult>('/api/v1/barcode-lookup/resolve', {
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

  const handleUndoLastScan = () => {
    if (scanHistoryStack.length === 0) return;
    const lastCode = scanHistoryStack[scanHistoryStack.length - 1];
    setScanHistoryStack(prev => prev.slice(0, -1));
    setLastScanFeedback({ step: wizardStepRef.current, code: lastCode, transitionText: 'Đã hoàn tác lần quét gần nhất' });
    onUndoLastScan?.(lastCode);
  };

  useHardwareScannerWedge({
    enabled: !disabled,
    onScan: barcode => { void triggerRef.current(barcode); },
  });

  useScannerKeyboardCapture({
    disabled, sectionRef, inputRef, bufferRef, inputValueRef, triggerRef, setInput, setParsedGs1,
  });

  const handleFillGs1Sample = () => {
    bufferRef.current = SAMPLE_GS1_CODE;
    inputValueRef.current = SAMPLE_GS1_CODE;
    setInput(SAMPLE_GS1_CODE);
    setParsedGs1(parseGs1BarcodeClient(SAMPLE_GS1_CODE));
    wizardStepRef.current = 2;
    setWizardStep(2);
  };

  return (
    <section
      ref={sectionRef}
      className="ui-card scanner-station-card"
      aria-label="Trạm quét mã vạch thiết bị cầm tay"
      style={isMobileViewport ? { padding: '8px 10px' } : undefined}
    >
      <ScannerWizardHeader
        wizardStep={wizardStep}
        stepHistory={stepHistory}
        parsedGs1={parsedGs1}
        step3Completed={step3Completed}
        autoAdvance={autoAdvance}
        stepLabels={stepLabels}
        compactMobile={isMobileViewport}
        onSelectStep={step => { wizardStepRef.current = step; setWizardStep(step); }}
        onToggleAutoAdvance={checked => { autoAdvanceRef.current = checked; setAutoAdvance(checked); }}
      />

      <div className="scanner-workbench-grid" style={isMobileViewport ? { gap: 8 } : undefined}>
        <ScannerCameraViewport
          cameraActive={cameraActive}
          isCameraLoading={isCameraLoading}
          isLiveVideo={isLiveVideo}
          isDisconnectedCamera={isDisconnectedCamera}
          cameraFlashSuccess={cameraFlashSuccess}
          disabled={disabled}
          busy={busy}
          compactMobile={isMobileViewport}
          videoRef={videoRef}
          canvasRef={canvasRef}
          onVideoLoadedData={onVideoLoadedData}
          onToggleCamera={() => void toggleCamera()}
          onRetryCamera={() => void startCameraStream()}
        />

        <div
          className="scanner-control-panel"
          data-testid="scanner-control-panel"
          data-mobile-hint-open={showGs1Hint ? 'true' : 'false'}
          style={isMobileViewport ? { padding: '6px 8px', gap: 6, minHeight: 0 } : undefined}
        >
          <div className="scanner-control-header">
            <span className="scanner-control-title">{label}</span>
            <div className="scanner-control-badges" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <UiBadge tone="success">Sẵn sàng nhận súng quét</UiBadge>
              {isLiveVideo ? (
                <UiBadge tone="success">Camera đang quét</UiBadge>
              ) : cameraActive && isCameraLoading ? (
                <UiBadge tone="warning">Đang kết nối Camera</UiBadge>
              ) : isDisconnectedCamera ? (
                <UiBadge tone="warning">Chưa kết nối được Camera</UiBadge>
              ) : (
                <UiBadge tone="neutral">Camera chưa bật</UiBadge>
              )}
            </div>
          </div>

          <div
            className="scanner-input-row"
            style={
              isMobileViewport
                ? { display: 'flex', flexDirection: 'column', flexWrap: 'nowrap', alignItems: 'stretch', gap: 6, width: '100%' }
                : undefined
            }
          >
            <input
              ref={inputRef}
              type="search"
              aria-label={label}
              value={input}
              disabled={disabled}
              placeholder={placeholder}
              style={isMobileViewport ? { flex: '0 0 auto', minWidth: 0, minHeight: 40, height: 40, width: '100%', boxSizing: 'border-box' } : undefined}
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
                  const rawCandidate = bufferRef.current || input;
                  const candidate = sanitizeScannerCode(rawCandidate);
                  bufferRef.current = '';
                  inputValueRef.current = '';
                  setInput('');
                  if (candidate) void handleTrigger(rawCandidate);
                }
              }}
            />
            <div
              data-testid="scanner-primary-actions-row"
              style={{
                display: 'grid',
                gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
                gap: 8,
                width: isMobileViewport ? '100%' : 'auto',
                maxWidth: '100%',
                boxSizing: 'border-box',
                flex: isMobileViewport ? '0 0 auto' : '0 0 auto',
              }}
            >
              <button
                type="button"
                data-testid="scanner-confirm-btn"
                className="ui-btn ui-btn-primary ui-primary-button"
                style={{ minHeight: 42, whiteSpace: 'nowrap', width: '100%', background: '#1d4ed8', borderColor: '#1d4ed8', color: '#ffffff' }}
                disabled={disabled || busy || !sanitizeScannerCode(input)}
                title={
                  busy
                    ? 'Hệ thống đang xử lý mã vạch vừa quét'
                    : !sanitizeScannerCode(input)
                      ? 'Quét hoặc nhập mã vạch hợp lệ trước khi xác nhận'
                      : disabled
                        ? 'Trạm quét hiện đang tạm khóa thao tác'
                        : undefined
                }
                onClick={() => void handleTrigger(bufferRef.current || input)}
              >
                {busy ? 'Đang xử lý...' : 'Xác nhận'}
              </button>
              <button
                type="button"
                data-testid="scanner-undo-last-btn"
                className="ui-btn ui-btn-secondary"
                style={{ minHeight: 42, whiteSpace: 'nowrap', width: '100%' }}
                disabled={disabled || busy || scanHistoryStack.length === 0}
                title={
                  scanHistoryStack.length === 0
                    ? 'Chưa có thao tác quét nào vừa thực hiện để hoàn tác'
                    : busy
                      ? 'Hệ thống đang xử lý mã vạch vừa quét'
                      : disabled
                        ? 'Trạm quét hiện đang tạm khóa thao tác'
                        : undefined
                }
                onClick={handleUndoLastScan}
              >
                {`Hoàn tác${scanHistoryStack.length > 0 ? ` (${scanHistoryStack.length})` : ''}`}
              </button>
            </div>
            {(!isMobileViewport || cameraActive) && (
              <button
                type="button"
                data-testid="btn-toggle-camera"
                className="ui-btn ui-btn-secondary"
                style={{ minHeight: 42, whiteSpace: 'nowrap' }}
                disabled={disabled || busy}
                onClick={() => void toggleCamera()}
              >
                {cameraActive ? 'Tắt Camera' : 'Bật Camera'}
              </button>
            )}
            {import.meta.env.VITE_QA_WEBHOOK_SIMULATOR === 'true' && (
              <button
                type="button"
                className="ui-btn ui-btn-secondary"
                style={{ minHeight: 42, whiteSpace: 'nowrap' }}
                disabled={disabled || busy}
                onClick={handleFillGs1Sample}
              >
                Điền mẫu GS1-128
              </button>
            )}
          </div>

          <ScannerFeedbackPanels
            lastScanFeedback={lastScanFeedback}
            stepHistory={stepHistory}
            decodedProductBanner={decodedProductBanner}
            lastResolved={lastResolved}
            parsedGs1={parsedGs1}
            cameraNotice={cameraNotice}
            disabled={disabled}
            busy={busy}
            compactMobile={isMobileViewport}
            onApplySample={() => { setInput(sampleCode); setCameraNotice(''); void handleTrigger(sampleCode); }}
            onDismissNotice={() => setCameraNotice('')}
          />
          {isCompact480 && (
            <button
              type="button"
              className="scanner-gs1-hint-toggle"
              data-testid="btn-toggle-gs1-hint"
              aria-expanded={showGs1Hint}
              onClick={() => setShowGs1Hint(prev => !prev)}
            >
              Hướng dẫn mã GS1-128
            </button>
          )}
        </div>
      </div>
    </section>
  );
}

export default MobileBarcodeScannerBar;
