import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useDialogFocusTrap } from './useDialogFocusTrap';
import { WMS_GLOSSARY } from './wmsGlossary';

export const UiEmptyState = ({ title, detail }: { title: string; detail?: string }) => (
  <div className="ui-empty">
    <div>
      <strong>{title}</strong>
      {detail && <div className="ui-empty-detail">{detail}</div>}
    </div>
  </div>
);

export const UiDrawer = ({
  open, title, subtitle, titleId = 'ui-drawer-title', busy = false, returnFocusTo, onClose, footer, children,
}: {
  open: boolean; title: ReactNode; subtitle?: ReactNode; titleId?: string; busy?: boolean;
  returnFocusTo?: HTMLElement | null; onClose: () => void; footer?: ReactNode; children: ReactNode;
}) => {
  const panelRef = useRef<HTMLDivElement>(null);
  useDialogFocusTrap(panelRef, { open, onEscape: onClose, escapeDisabled: busy, returnFocusTo });
  if (!open) return null;

  return (
    <div className="ui-drawer-backdrop" data-testid="ui-drawer-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose(); }}>
      <aside ref={panelRef} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby={titleId} data-testid="ui-drawer" className="ui-drawer">
        <header className="ui-drawer-header">
          <div>
            <h2 id={titleId}>{title}</h2>
            {subtitle && <div className="ui-drawer-subtitle">{subtitle}</div>}
          </div>
          <button type="button" className="ui-drawer-close ui-icon-touch-target" aria-label="Đóng hộp thoại" title="Đóng hộp thoại" disabled={busy} onClick={onClose}>×</button>
        </header>
        <div className="ui-drawer-body">{children}</div>
        {footer && <footer className="ui-drawer-footer">{footer}</footer>}
      </aside>
    </div>
  );
};

export const UiPrintSignatures = () => (
  <section className="ui-print-signatures" aria-label="Chữ ký xác nhận chứng từ">
    <div className="ui-print-signature-col"><strong>Người lập phiếu</strong><span>(Ký, ghi rõ họ tên)</span></div>
    <div className="ui-print-signature-col"><strong>Người giao / nhận hàng</strong><span>(Ký, ghi rõ họ tên)</span></div>
    <div className="ui-print-signature-col"><strong>Thủ kho</strong><span>(Ký, ghi rõ họ tên)</span></div>
    <div className="ui-print-signature-col"><strong>Quản lý kho / Kế toán</strong><span>(Ký, ghi rõ họ tên)</span></div>
  </section>
);

export const UiTooltip = ({
  term, label, description, children, className = '', 'data-testid': testId,
}: {
  term?: keyof typeof WMS_GLOSSARY; label?: ReactNode; description?: string;
  children?: ReactNode; className?: string; 'data-testid'?: string;
}) => {
  const tooltipId = useId();
  const wrapperRef = useRef<HTMLSpanElement>(null);
  const pinnedRef = useRef(false);
  const focusedRef = useRef(false);
  const pointerDownActiveRef = useRef(false);
  const wasPinnedOnPointerDownRef = useRef(false);
  const [open, setOpen] = useState(false);

  const entry = term ? WMS_GLOSSARY[term] : undefined;
  const displayLabel = label ?? children ?? (entry ? entry.term : '');
  const tooltipText = description ?? (entry ? `${entry.label}: ${entry.description}` : '');

  const closeAll = useCallback(() => {
    pinnedRef.current = false;
    focusedRef.current = false;
    setOpen(false);
  }, []);

  useEffect(() => {
    if (!open || typeof document === 'undefined') return undefined;
    const handleKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') closeAll(); };
    const handleOutside = (event: Event) => { if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) closeAll(); };
    document.addEventListener('keydown', handleKeyDown);
    document.addEventListener('pointerdown', handleOutside);
    document.addEventListener('mousedown', handleOutside);
    document.addEventListener('click', handleOutside);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('pointerdown', handleOutside);
      document.removeEventListener('mousedown', handleOutside);
      document.removeEventListener('click', handleOutside);
    };
  }, [open, closeAll]);

  return (
    <span
      ref={wrapperRef}
      data-testid={testId}
      className={('ui-tooltip-wrap ' + className).trim()}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => { if (!pinnedRef.current && !focusedRef.current) setOpen(false); }}
      onBlur={(event) => { if (!wrapperRef.current?.contains(event.relatedTarget as Node | null)) closeAll(); }}
    >
      <button
        type="button"
        className="ui-tooltip-trigger"
        aria-expanded={open}
        aria-describedby={open ? tooltipId : undefined}
        onPointerDown={() => { pointerDownActiveRef.current = true; wasPinnedOnPointerDownRef.current = open && pinnedRef.current; }}
        onMouseDown={() => { if (!pointerDownActiveRef.current) { pointerDownActiveRef.current = true; wasPinnedOnPointerDownRef.current = open && pinnedRef.current; } }}
        onFocus={() => { focusedRef.current = true; setOpen(true); }}
        onClick={() => {
          if (pointerDownActiveRef.current) {
            pointerDownActiveRef.current = false;
            if (wasPinnedOnPointerDownRef.current) closeAll();
            else { pinnedRef.current = true; setOpen(true); }
            return;
          }
          if (open && pinnedRef.current) closeAll();
          else { pinnedRef.current = true; setOpen(true); }
        }}
      >
        {displayLabel}
      </button>
      {open && tooltipText && <span id={tooltipId} role="tooltip" className="ui-tooltip-bubble">{tooltipText}</span>}
    </span>
  );
};

export const UiSkeleton = ({
  lines = 3, height, className = '', label = 'Đang tải dữ liệu...', 'data-testid': testId,
}: {
  lines?: number; height?: number | string; className?: string; label?: string; 'data-testid'?: string;
}) => {
  const count = Math.max(1, lines);
  const lineHeight = height !== undefined ? (typeof height === 'number' ? `${height}px` : height) : undefined;
  return (
    <div role="status" aria-busy="true" aria-live="polite" aria-label={label} data-testid={testId ?? 'ui-skeleton'} className={('ui-skeleton ' + className).trim()}>
      {Array.from({ length: count }, (_, idx) => (
        <span key={idx} aria-hidden="true" className="ui-skeleton-line" style={{ height: lineHeight, width: idx === count - 1 && count > 1 ? '68%' : '100%' }} />
      ))}
      <span className="sr-only">{label}</span>
    </div>
  );
};

export const UiErrorBanner = ({
  title, message, errorCode, correlationId, onRetry, retryLabel = 'Thử lại', className = '', 'data-testid': testId,
}: {
  title?: string; message: string; errorCode?: string; correlationId?: string;
  onRetry?: () => void; retryLabel?: string; className?: string; 'data-testid'?: string;
}) => (
  <div role="alert" aria-live="assertive" data-testid={testId ?? 'ui-error-banner'} className={('ui-error-banner ' + className).trim()}>
    <div className="ui-error-banner-body">
      {title && <strong className="ui-error-banner-title">{title}</strong>}
      <span className="ui-error-banner-message">{message}</span>
      {(errorCode || correlationId) && (
        <details className="ui-err-tech-details" data-testid="ui-error-banner-tech-details">
          <summary>Chi tiết kỹ thuật</summary>
          <code className="ui-err-code">{errorCode ? `${errorCode}${correlationId ? ` • ${correlationId}` : ''}` : correlationId}</code>
        </details>
      )}
    </div>
    {onRetry && (
      <button
        type="button"
        data-testid="ui-error-banner-retry"
        className="ui-btn ui-btn-secondary ui-error-banner-retry"
        style={{ minHeight: 44 }}
        onClick={onRetry}
      >
        {retryLabel}
      </button>
    )}
  </div>
);
