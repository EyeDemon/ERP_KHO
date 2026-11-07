import { CheckCircle2, Package } from 'lucide-react';
import { UiBadge } from '../../ui/ProductionUi';
import {
  ENTITY_LABELS,
  type LastScanFeedback,
  type ParsedGs1Data,
  type ResolvedBarcodeResult,
} from './scannerTypes';

interface ScannerWizardHeaderProps {
  wizardStep: 1 | 2 | 3;
  stepHistory: { 1: string; 2: string; 3: string };
  parsedGs1: ParsedGs1Data | null;
  step3Completed: boolean;
  autoAdvance: boolean;
  stepLabels?: [string, string, string];
  compactMobile?: boolean;
  onSelectStep: (step: 1 | 2 | 3) => void;
  onToggleAutoAdvance: (checked: boolean) => void;
}

export function ScannerWizardHeader({
  wizardStep,
  stepHistory,
  parsedGs1,
  step3Completed,
  autoAdvance,
  stepLabels,
  compactMobile = false,
  onSelectStep,
  onToggleAutoAdvance,
}: ScannerWizardHeaderProps) {
  const defaultLabels: [string, string, string] = [
    'Bước 1: Quét Vị trí / Kệ nguồn',
    'Bước 2: Quét Mã vạch SP / GS1-128',
    'Bước 3: Quét Kệ đích & Xác nhận',
  ];
  const resolvedLabels = stepLabels ?? defaultLabels;

  if (compactMobile) {
    const activeTitle = resolvedLabels[wizardStep - 1].replace(/^Bước\s+\d+:\s*/i, '');
    return (
      <div
        className="scanner-header-bar scanner-header-bar--compact"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 8,
          width: '100%',
          maxWidth: '100%',
          boxSizing: 'border-box',
          paddingBottom: 6,
          marginBottom: 6,
          borderBottom: '1px solid #e2e8f0',
        }}
      >
        <span
          data-testid="rf-scan-active-step-label"
          style={{
            fontSize: 12,
            fontWeight: 700,
            color: '#0f172a',
            minWidth: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}
        >
          {`Bước ${wizardStep}/3: ${activeTitle}`}
        </span>
        <div
          className="scanner-steps-list"
          data-testid="rf-scan-wizard-steps"
          style={{ display: 'inline-flex', alignItems: 'center', gap: 4, flexShrink: 0 }}
        >
          {([1, 2, 3] as const).map((stepNum) => {
            const isCurrent = wizardStep === stepNum;
            const isCompleted =
              wizardStep > stepNum ||
              Boolean(stepHistory[stepNum]) ||
              (stepNum === 2 && Boolean(parsedGs1)) ||
              (stepNum === 3 && step3Completed);
            return (
              <span
                key={stepNum}
                data-testid={`wizard-step-pill-${stepNum}`}
                data-step-active={isCurrent ? 'true' : 'false'}
                data-step-completed={isCompleted ? 'true' : 'false'}
                title={resolvedLabels[stepNum - 1]}
                onClick={() => onSelectStep(stepNum)}
                className="scanner-step-pill"
                style={{
                  cursor: 'pointer',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  minWidth: 26,
                  height: 24,
                  padding: '0 7px',
                  borderRadius: 999,
                  fontSize: 11.5,
                  fontWeight: 700,
                  background: isCompleted ? '#ecfdf5' : isCurrent ? '#1d4ed8' : '#f1f5f9',
                  color: isCompleted ? '#065f46' : isCurrent ? '#ffffff' : '#475569',
                  border: `1px solid ${isCompleted ? '#a7f3d0' : isCurrent ? '#1d4ed8' : '#cbd5e1'}`,
                  whiteSpace: 'nowrap',
                }}
              >
                {stepNum}
              </span>
            );
          })}
        </div>
      </div>
    );
  }

  return (
    <div className="scanner-header-bar">
      <div className="scanner-steps-list" data-testid="rf-scan-wizard-steps">
        {([
          { step: 1 as const, text: resolvedLabels[0] },
          { step: 2 as const, text: resolvedLabels[1] },
          { step: 3 as const, text: resolvedLabels[2] },
        ]).map((item, idx) => {
          const isCurrent = wizardStep === item.step;
          const isCompleted =
            wizardStep > item.step ||
            Boolean(stepHistory[item.step]) ||
            (item.step === 2 && Boolean(parsedGs1)) ||
            (item.step === 3 && step3Completed);
          return (
            <div key={item.step} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <span
                data-testid={`wizard-step-pill-${item.step}`}
                data-step-active={isCurrent ? 'true' : 'false'}
                data-step-completed={isCompleted ? 'true' : 'false'}
                onClick={() => onSelectStep(item.step)}
                className="scanner-step-pill"
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
              {idx < 2 && <span className="scanner-step-arrow" style={{ color: '#475569', fontWeight: 700 }}>→</span>}
            </div>
          );
        })}
      </div>

      <label className="scanner-auto-advance">
        <input
          type="checkbox"
          data-testid="scanner-auto-advance-toggle"
          checked={autoAdvance}
          onChange={e => onToggleAutoAdvance(e.target.checked)}
          style={{ width: 20, height: 20, minHeight: 20, margin: 0, flexShrink: 0 }}
        />
        <span>Tự động chuyển bước sau khi quét</span>
      </label>
    </div>
  );
}

interface ScannerFeedbackPanelsProps {
  lastScanFeedback: LastScanFeedback | null;
  stepHistory: { 1: string; 2: string; 3: string };
  decodedProductBanner: string;
  lastResolved: ResolvedBarcodeResult | null;
  parsedGs1: ParsedGs1Data | null;
  cameraNotice: string;
  disabled: boolean;
  busy: boolean;
  compactMobile?: boolean;
  onApplySample: () => void;
  onDismissNotice: () => void;
}

export function ScannerFeedbackPanels({
  lastScanFeedback,
  stepHistory,
  decodedProductBanner,
  lastResolved,
  parsedGs1,
  cameraNotice,
  disabled,
  busy,
  compactMobile = false,
  onApplySample,
  onDismissNotice,
}: ScannerFeedbackPanelsProps) {
  const hasAnyStepScan = Boolean(stepHistory[1] || stepHistory[2] || stepHistory[3]);

  return (
    <>
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
          <CheckCircle2 size={15} strokeWidth={2.2} aria-hidden="true" />
          <span>{`Đã nhận mã [Bước ${lastScanFeedback.step}]: ${lastScanFeedback.code} → ${lastScanFeedback.transitionText}`}</span>
        </div>
      )}

      {(!compactMobile || hasAnyStepScan) && (
        <div
          className="scanner-step-history-chips"
          data-testid="scanner-step-history-chips"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            gap: compactMobile ? 6 : 8,
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
                  padding: compactMobile ? '2px 8px' : '4px 10px',
                  borderRadius: 999,
                  fontSize: compactMobile ? 11.5 : 12,
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
                    color: hasValue ? '#047857' : '#475569',
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
      )}

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
          <Package size={15} strokeWidth={2.2} aria-hidden="true" />
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
              onClick={onApplySample}
            >
              Điền mã mẫu
            </button>
            <button
              type="button"
              className="ui-btn ui-btn-secondary"
              style={{ minHeight: 34, padding: '4px 10px', whiteSpace: 'nowrap' }}
              onClick={onDismissNotice}
            >
              Đóng
            </button>
          </div>
        </div>
      )}

      <div className="scanner-hint-footer">
        Hỗ trợ súng quét USB/Bluetooth (nhấn Enter tự động gửi) &amp; chuẩn GS1-128: (01) GTIN • (10) Số lô • (17) Hạn dùng • (21) Sê-ri
      </div>
    </>
  );
}
