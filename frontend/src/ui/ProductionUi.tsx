import { Children, cloneElement, isValidElement, useEffect, useRef, type ReactElement, type ReactNode } from 'react';

export const UiPage = ({ children }: { children: ReactNode }) => <section className="ui-page">{children}</section>;

export const UiPageHeader = ({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  actions?: ReactNode;
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

export const UiToolbarField = ({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) => {
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
  title,
  subtitle,
  children,
}: {
  title?: string;
  subtitle?: string;
  children: ReactNode;
}) => (
  <section className="ui-card">
    {title && <h2 className="ui-card-title">{title}</h2>}
    {subtitle && <p className="ui-muted-text" style={{ marginTop: -4, marginBottom: 10, fontSize: 13 }}>{subtitle}</p>}
    {children}
  </section>
);

export const UiBadge = ({
  children,
  tone = 'neutral',
  title,
}: {
  children: ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'danger' | 'info';
  title?: string;
}) => (
  <span className={'ui-badge ' + (tone === 'neutral' ? '' : tone)} title={title}>
    {children}
  </span>
);

export const UiEmptyState = ({
  title,
  detail,
}: {
  title: string;
  detail?: string;
}) => (
  <div className="ui-empty">
    <div>
      <strong>{title}</strong>
      {detail && <div className="ui-empty-detail">{detail}</div>}
    </div>
  </div>
);

export const UiTableScroll = ({ children }: { children: ReactNode }) => <div className="ui-table-scroll">{children}</div>;

export const UiMetricGrid = ({ children }: { children: ReactNode }) => <div className="ui-metric-grid">{children}</div>;

export const UiMetric = ({
  value,
  label,
}: {
  value: ReactNode;
  label: string;
}) => (
  <article className="ui-metric">
    <strong>{value}</strong>
    <span>{label}</span>
  </article>
);

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

  useEffect(() => {
    if (!open) return;
    const initial =
      panelRef.current?.querySelector<HTMLElement>('[data-initial-focus]') ??
      panelRef.current?.querySelector<HTMLElement>(
        'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])',
      );
    initial?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !busy) {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      returnFocusTo?.focus?.();
    };
  }, [open, busy, onClose, returnFocusTo]);

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
