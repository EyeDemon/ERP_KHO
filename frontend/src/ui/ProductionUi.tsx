import { Children, cloneElement, isValidElement, useCallback, useEffect, useRef, useState, type ReactElement, type ReactNode } from 'react';
import { useDialogFocusTrap } from './useDialogFocusTrap';

export const UiPage = ({ children, className = '' }: { children: ReactNode; className?: string }) => (
  <section className={('ui-page ' + className).trim()}>{children}</section>
);

export const UiPageHeader = ({
  eyebrow, title, description, actions,
}: {
  eyebrow?: string; title: string; description?: string; actions?: ReactNode;
}) => (
  <header className="ui-page-header">
    <div>
      {eyebrow && <div className="ui-eyebrow">{eyebrow}</div>}
      <h1>{title}</h1>
      {description && <p className="ui-description">{description}</p>}
    </div>
    {actions && <div className="ui-page-header-actions">{actions}</div>}
  </header>
);

export const UiToolbar = ({ children }: { children: ReactNode }) => <div className="ui-toolbar">{children}</div>;

export const UiToolbarField = ({ label, children }: { label: string; children: ReactNode }) => {
  const enhancedChildren = Children.map(children, (child) => {
    if (isValidElement(child) && typeof child.type === 'string' && ['input', 'select', 'textarea'].includes(child.type)) {
      const props = child.props as Record<string, unknown>;
      if (!props['aria-label'] && !props['aria-labelledby']) {
        return cloneElement(child as ReactElement<Record<string, unknown>>, { 'aria-label': label });
      }
    }
    return child;
  });
  return (
    <label className="ui-toolbar-group ui-toolbar-field">
      <span>{label}</span>
      {enhancedChildren}
    </label>
  );
};

export const UiCard = ({
  title, subtitle, actions, children,
}: {
  title?: string; subtitle?: string; actions?: ReactNode; children: ReactNode;
}) => (
  <section className="ui-card">
    {actions ? (
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, marginBottom: 10 }}>
        <div>
          {title && <h2 className="ui-card-title" style={{ marginBottom: subtitle ? 4 : 0 }}>{title}</h2>}
          {subtitle && <p className="ui-muted-text" style={{ margin: 0, fontSize: 13 }}>{subtitle}</p>}
        </div>
        <div>{actions}</div>
      </div>
    ) : (
      <>
        {title && <h2 className="ui-card-title">{title}</h2>}
        {subtitle && <p className="ui-muted-text" style={{ marginTop: -4, marginBottom: 10, fontSize: 13 }}>{subtitle}</p>}
      </>
    )}
    {children}
  </section>
);

export const UiBadge = ({
  children, tone = 'neutral', title,
}: {
  children: ReactNode; tone?: 'neutral' | 'success' | 'warning' | 'danger' | 'info'; title?: string;
}) => (
  <span className={'ui-badge ' + (tone === 'neutral' ? '' : tone)} title={title}>
    {children}
  </span>
);

export const UiEmptyState = ({ title, detail }: { title: string; detail?: string }) => (
  <div className="ui-empty">
    <div>
      <strong>{title}</strong>
      {detail && <div className="ui-empty-detail">{detail}</div>}
    </div>
  </div>
);

export const UiTableScroll = ({
  children, className = '', 'data-testid': testId,
}: {
  children: ReactNode; className?: string; 'data-testid'?: string;
}) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollState, setScrollState] = useState({ hasOverflow: false, canScrollLeft: false, canScrollRight: false });

  const measure = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    const hasOverflow = el.scrollWidth - el.clientWidth > 0;
    const canScrollLeft = hasOverflow && el.scrollLeft > 1;
    const canScrollRight = hasOverflow && el.scrollLeft + el.clientWidth < el.scrollWidth - 1;
    setScrollState((prev) =>
      prev.hasOverflow === hasOverflow && prev.canScrollLeft === canScrollLeft && prev.canScrollRight === canScrollRight
        ? prev
        : { hasOverflow, canScrollLeft, canScrollRight },
    );
  }, []);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    measure();
    const rafId = typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function'
      ? window.requestAnimationFrame(() => measure())
      : 0;
    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(() => measure());
      observer.observe(el);
      if (el.firstElementChild) observer.observe(el.firstElementChild);
    }
    window.addEventListener('resize', measure);
    return () => {
      if (rafId && typeof window.cancelAnimationFrame === 'function') window.cancelAnimationFrame(rafId);
      observer?.disconnect();
      window.removeEventListener('resize', measure);
    };
  }, [measure, children]);

  const classes = [
    'ui-table-scroll',
    scrollState.hasOverflow ? 'ui-table-scroll--has-overflow' : '',
    scrollState.canScrollLeft ? 'ui-table-scroll--can-scroll-left' : '',
    scrollState.canScrollRight ? 'ui-table-scroll--can-scroll-right' : '',
    className,
  ].filter(Boolean).join(' ');

  return (
    <div
      ref={scrollRef}
      className={classes}
      data-testid={testId}
      data-has-overflow={scrollState.hasOverflow ? 'true' : 'false'}
      tabIndex={scrollState.hasOverflow ? 0 : undefined}
      role={scrollState.hasOverflow ? 'region' : undefined}
      aria-label={scrollState.hasOverflow ? 'Vùng bảng dữ liệu có thể cuộn ngang' : undefined}
      onScroll={measure}
    >
      {children}
    </div>
  );
};

export const UiSortTh = ({
  label, active, direction, onToggle, align = 'left', className = '', title, style,
}: {
  label: ReactNode; active: boolean; direction: 'asc' | 'desc'; onToggle: () => void;
  align?: 'left' | 'right'; className?: string; title?: string; style?: React.CSSProperties;
}) => (
  <th
    aria-sort={active ? (direction === 'asc' ? 'ascending' : 'descending') : 'none'}
    className={('ui-sort-th ' + className).trim()}
    style={{ textAlign: align, paddingRight: 14, whiteSpace: 'nowrap', ...style }}
  >
    <button
      type="button"
      className="ui-sort-th-btn"
      title={title}
      onClick={onToggle}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        whiteSpace: 'nowrap',
        justifyContent: align === 'right' ? 'flex-end' : 'flex-start',
      }}
    >
      <span>{label}</span>
      <span aria-hidden="true" className="ui-sort-indicator">
        {active ? (direction === 'asc' ? '↑' : '↓') : '↕'}
      </span>
    </button>
  </th>
);

export const UiMetricGrid = ({ children, 'data-testid': testId }: { children: ReactNode; 'data-testid'?: string }) => (
  <div className="ui-metric-grid" data-testid={testId}>{children}</div>
);

export const UiMetric = ({
  value, label, subtitle, isZero,
}: {
  value: ReactNode; label: ReactNode; subtitle?: ReactNode; isZero?: boolean;
}) => {
  const zero = isZero ?? (value === 0 || value === '0');
  return (
    <article className={'ui-metric' + (zero ? ' ui-metric--zero' : '')} data-zero={zero ? 'true' : undefined}>
      <strong className="ui-metric-value">{value}</strong>
      {subtitle !== undefined && subtitle !== null && <small className="ui-metric-subtitle">{subtitle}</small>}
      <span className="ui-metric-label">{label}</span>
    </article>
  );
};

export interface UiTabItem<T extends string = string> {
  id?: T;
  key?: T;
  value?: T;
  label: ReactNode;
  badge?: ReactNode;
  disabled?: boolean;
  testId?: string;
}

export const UiTabs = <T extends string = string>({
  tabs,
  items,
  activeTab,
  value,
  onChange,
  onTabChange,
  ariaLabel = 'Chuyển đổi phân mục',
  className = '',
  children,
}: {
  tabs?: readonly UiTabItem<T>[];
  items?: readonly UiTabItem<T>[];
  activeTab?: T;
  value?: T;
  onChange?: (tab: T) => void;
  onTabChange?: (tab: T) => void;
  ariaLabel?: string;
  className?: string;
  children?: ReactNode;
}) => {
  const list = tabs ?? items;
  const current = activeTab ?? value;
  const handleSelect = onChange ?? onTabChange;

  return (
    <div role="tablist" aria-label={ariaLabel} className={('ui-tabs ' + className).trim()}>
      {list
        ? list.map((tab, idx) => {
            const tabKey = (tab.id ?? tab.key ?? tab.value ?? String(idx)) as T;
            const isSelected = current === tabKey;
            return (
              <button
                key={String(tabKey)}
                type="button"
                role="tab"
                aria-selected={isSelected}
                disabled={tab.disabled}
                data-testid={tab.testId}
                className={'ui-tab' + (isSelected ? ' active' : '')}
                onClick={() => handleSelect?.(tabKey)}
              >
                <span>{tab.label}</span>
                {tab.badge !== undefined && tab.badge !== null && (
                  <span className="ui-tab-badge">{tab.badge}</span>
                )}
              </button>
            );
          })
        : children}
    </div>
  );
};

export const UiDrawer = ({
  open,
  title,
  subtitle,
  titleId = 'ui-drawer-title',
  busy = false,
  returnFocusTo,
  onClose,
  footer,
  children,
}: {
  open: boolean;
  title: ReactNode;
  subtitle?: ReactNode;
  titleId?: string;
  busy?: boolean;
  returnFocusTo?: HTMLElement | null;
  onClose: () => void;
  footer?: ReactNode;
  children: ReactNode;
}) => {
  const panelRef = useRef<HTMLDivElement>(null);
  useDialogFocusTrap(panelRef, { open, onEscape: onClose, escapeDisabled: busy, returnFocusTo });

  if (!open) return null;

  return (
    <div
      className="ui-drawer-backdrop"
      data-testid="ui-drawer-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy) onClose();
      }}
    >
      <aside
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        data-testid="ui-drawer"
        className="ui-drawer"
      >
        <header className="ui-drawer-header">
          <div>
            <h2 id={titleId}>{title}</h2>
            {subtitle && <div className="ui-drawer-subtitle">{subtitle}</div>}
          </div>
          <button
            type="button"
            className="ui-drawer-close"
            aria-label="Đóng ngăn trượt"
            disabled={busy}
            onClick={onClose}
          >
            ✕
          </button>
        </header>
        <div className="ui-drawer-body">{children}</div>
        {footer && <footer className="ui-drawer-footer">{footer}</footer>}
      </aside>
    </div>
  );
};

export const UiPrintSignatures = () => (
  <section className="ui-print-signatures" aria-label="Chữ ký xác nhận chứng từ">
    <div className="ui-print-signature-col">
      <strong>Người lập phiếu</strong>
      <span>(Ký, ghi rõ họ tên)</span>
    </div>
    <div className="ui-print-signature-col">
      <strong>Người giao / nhận hàng</strong>
      <span>(Ký, ghi rõ họ tên)</span>
    </div>
    <div className="ui-print-signature-col">
      <strong>Thủ kho</strong>
      <span>(Ký, ghi rõ họ tên)</span>
    </div>
    <div className="ui-print-signature-col">
      <strong>Quản lý kho / Kế toán</strong>
      <span>(Ký, ghi rõ họ tên)</span>
    </div>
  </section>
);
