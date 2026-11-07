import { Children, Fragment, cloneElement, isValidElement, useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactElement, type ReactNode } from 'react';
import { Lock } from 'lucide-react';

export { WMS_GLOSSARY, type WmsGlossaryEntry, type WmsGlossaryKey } from './wmsGlossary';
export {
  UiDrawer,
  UiEmptyState,
  UiErrorBanner,
  UiPrintSignatures,
  UiSkeleton,
  UiTooltip,
} from './ProductionUiFeedback';

export const UiPage = ({ children, className = '', style }: { children: ReactNode; className?: string; style?: CSSProperties }) => (
  <section className={('ui-page ' + className).trim()} style={style}>{children}</section>
);

export const UiPageHeader = ({ eyebrow, title, description, actions }: { eyebrow?: string; title: string; description?: string; actions?: ReactNode }) => (
  <header className="ui-page-header">
    <div>
      {eyebrow && <div className="ui-eyebrow">{eyebrow}</div>}
      <h1>{title}</h1>
      {description && <p className="ui-description">{description}</p>}
    </div>
    {actions && <div className="ui-page-header-actions">{actions}</div>}
  </header>
);

function flattenToolbarNodes(nodes: ReactNode): ReactNode[] {
  const result: ReactNode[] = [];
  Children.forEach(nodes, (node) => {
    if (node === null || node === undefined || typeof node === 'boolean') return;
    if (isValidElement(node) && node.type === Fragment) result.push(...flattenToolbarNodes((node.props as { children?: ReactNode }).children));
    else result.push(node);
  });
  return result;
}

function isFilterValueActive(val: unknown): boolean {
  if (val === undefined || val === null || typeof val === 'boolean') return false;
  const text = String(val).trim();
  return text !== '' && text.toUpperCase() !== 'ALL';
}

function countActiveReactFilters(nodes: ReactNode): number {
  let count = 0;
  Children.forEach(nodes, (node) => {
    if (!isValidElement(node) || node.type === 'option') return;
    const props = node.props as Record<string, unknown>;
    if (typeof node.type === 'string') {
      if (node.type === 'input' || node.type === 'select' || node.type === 'textarea') {
        if (isFilterValueActive(props.value)) count += 1;
        return;
      }
      count += countActiveReactFilters(props.children as ReactNode);
      return;
    }
    if ('value' in props) {
      if (isFilterValueActive(props.value)) count += 1;
      return;
    }
    count += countActiveReactFilters(props.children as ReactNode);
  });
  return count;
}

function isToolbarActionNode(node: ReactNode): boolean {
  if (!isValidElement(node)) return false;
  if (node.type === 'button') return true;
  const props = node.props as { className?: unknown; children?: ReactNode };
  if (typeof props.className === 'string' && /\b[\w-]*actions?\b/i.test(props.className)) return true;
  if (node.type === 'div' || node.type === 'span') {
    const inner = flattenToolbarNodes(props.children);
    if (inner.length > 0 && inner.every((child) => isValidElement(child) && child.type === 'button')) return true;
  }
  return false;
}

const TOOLBAR_CONTROL_SELECTOR = 'input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]), select, textarea';

export const UiToolbar = ({
  children, onApplyFilters, onResetFilters, 'data-testid': testId,
}: {
  children: ReactNode; onApplyFilters?: () => void; onResetFilters?: () => void; 'data-testid'?: string;
}) => {
  const [isMobileViewport, setIsMobileViewport] = useState<boolean>(() => typeof window !== 'undefined' && window.innerWidth <= 768);
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
  const [domActiveCount, setDomActiveCount] = useState<number | null>(null);
  const toolbarRootRef = useRef<HTMLDivElement>(null);
  const secondaryContainerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (typeof window === 'undefined') return undefined;
    const handleResize = () => setIsMobileViewport(window.innerWidth <= 768);
    handleResize();
    window.addEventListener('resize', handleResize);
    const mql = typeof window.matchMedia === 'function' ? window.matchMedia('(max-width: 768px)') : null;
    const handleMediaChange = (event: MediaQueryListEvent) => setIsMobileViewport(event.matches);
    mql?.addEventListener?.('change', handleMediaChange);
    return () => {
      window.removeEventListener('resize', handleResize);
      mql?.removeEventListener?.('change', handleMediaChange);
    };
  }, []);

  const topNodes = flattenToolbarNodes(children);
  let wrappedEl: ReactElement<Record<string, unknown>> | null = null;
  let rawItems = topNodes;
  if (topNodes.length === 1 && isValidElement(topNodes[0])) {
    const first = topNodes[0] as ReactElement<Record<string, unknown>>;
    const inner = flattenToolbarNodes(first.props.children as ReactNode);
    if (first.type === 'form' || (first.type === 'div' && inner.length > 2)) {
      wrappedEl = first;
      rawItems = inner;
    }
  }

  const filterItems: ReactNode[] = [];
  const actionItems: ReactNode[] = [];
  rawItems.forEach((item) => (isToolbarActionNode(item) ? actionItems : filterItems).push(item));

  const isCollapsedMode = isMobileViewport && filterItems.length > 2;
  const primaryFilters = isCollapsedMode ? filterItems.slice(0, 2) : filterItems;
  const secondaryFilters = isCollapsedMode ? filterItems.slice(2) : [];

  const syncDomActiveCount = useCallback(() => {
    const container = secondaryContainerRef.current;
    if (!container) { setDomActiveCount(null); return; }
    const controls = container.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(TOOLBAR_CONTROL_SELECTOR);
    if (controls.length === 0) { setDomActiveCount(null); return; }
    const active = Array.from(controls).filter((ctrl) => isFilterValueActive(ctrl.value)).length;
    setDomActiveCount((prev) => (prev === active ? prev : active));
  }, []);

  useEffect(() => {
    if (!isCollapsedMode) { setDomActiveCount(null); return; }
    syncDomActiveCount();
  }, [isCollapsedMode, children, syncDomActiveCount]);

  if (!isCollapsedMode) return <div className="ui-toolbar" data-testid={testId}>{children}</div>;

  const extraCount = secondaryFilters.length;
  const activeExtraCount = Math.max(countActiveReactFilters(secondaryFilters), domActiveCount ?? 0);

  const handleApplyFilters = () => {
    if (onApplyFilters) {
      onApplyFilters();
    } else {
      const formEl = toolbarRootRef.current?.querySelector('form') ?? toolbarRootRef.current?.closest('form');
      if (formEl) {
        try {
          if (typeof formEl.requestSubmit === 'function') formEl.requestSubmit();
          else formEl.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        } catch {
          formEl.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        }
      }
    }
    setMobileFiltersOpen(false);
  };

  const handleResetFilters = () => {
    const container = secondaryContainerRef.current;
    if (container && typeof window !== 'undefined') {
      const controls = container.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(TOOLBAR_CONTROL_SELECTOR);
      controls.forEach((ctrl) => {
        if (ctrl instanceof window.HTMLSelectElement) {
          const emptyOpt = Array.from(ctrl.options).find((o) => o.value === '' || o.value.toUpperCase() === 'ALL');
          const nextVal = emptyOpt ? emptyOpt.value : (ctrl.options[0]?.value ?? '');
          const setter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set;
          if (setter) setter.call(ctrl, nextVal); else ctrl.value = nextVal;
        } else if (ctrl instanceof window.HTMLTextAreaElement) {
          const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value')?.set;
          if (setter) setter.call(ctrl, ''); else ctrl.value = '';
        } else {
          const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
          if (setter) setter.call(ctrl, ''); else ctrl.value = '';
        }
        ctrl.dispatchEvent(new Event('input', { bubbles: true }));
        ctrl.dispatchEvent(new Event('change', { bubbles: true }));
      });
    }
    onResetFilters?.();
    syncDomActiveCount();
  };

  const mobileContent = (
    <>
      <div className="ui-toolbar-primary-bar">
        {primaryFilters}
        <button type="button" className="ui-btn ui-btn-secondary ui-toolbar-filter-toggle" data-testid="btn-toggle-toolbar-filters" aria-expanded={mobileFiltersOpen} style={{ minHeight: 44 }} onClick={() => setMobileFiltersOpen((v) => !v)}>
          <span>{`Bộ lọc (${extraCount})`}</span>
          {activeExtraCount > 0 && <span className="ui-toolbar-filter-badge" data-testid="toolbar-active-filter-badge">{`Đang lọc: ${activeExtraCount}`}</span>}
        </button>
        {actionItems}
      </div>
      <div ref={secondaryContainerRef} className="ui-toolbar-secondary-panel" data-testid="ui-toolbar-secondary-panel" hidden={!mobileFiltersOpen} style={{ display: mobileFiltersOpen ? undefined : 'none' }} onInput={syncDomActiveCount} onChange={syncDomActiveCount}>
        <div className="ui-toolbar-secondary-fields">{secondaryFilters}</div>
        <div className="ui-toolbar-secondary-actions">
          <button type="button" className="ui-btn ui-btn-primary" data-testid="btn-apply-toolbar-filters" onClick={handleApplyFilters}>Áp dụng</button>
          <button type="button" className="ui-btn ui-btn-secondary" data-testid="btn-reset-toolbar-filters" onClick={handleResetFilters}>Xóa lọc</button>
        </div>
      </div>
    </>
  );

  const wrapperStyle: CSSProperties = { ...((wrappedEl?.props.style as CSSProperties | undefined) ?? {}), display: 'flex', flexDirection: 'column', alignItems: 'stretch', gap: 8, width: '100%', gridTemplateColumns: undefined };
  return (
    <div ref={toolbarRootRef} className="ui-toolbar ui-toolbar--mobile-collapsible" data-testid={testId}>
      {wrappedEl ? cloneElement(wrappedEl, { style: wrapperStyle }, mobileContent) : mobileContent}
    </div>
  );
};

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
  <span className={'ui-badge ' + (tone === 'neutral' ? 'neutral' : tone)} title={title}>
    {children}
  </span>
);

export const UiCostMaskedBadge = ({ 'data-testid': testId }: { 'data-testid'?: string }) => (
  <span data-testid={testId} className="ui-cost-masked-badge" style={{ whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
    <Lock size={12} aria-hidden="true" /> Ẩn theo quyền
  </span>
);

const useIsomorphicLayoutEffect = typeof window !== 'undefined' ? useLayoutEffect : useEffect;

export const UiTableScroll = ({
  children, className = '', ariaLabel, 'data-testid': testId,
}: {
  children: ReactNode; className?: string; ariaLabel?: string; 'data-testid'?: string;
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

  useIsomorphicLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    measure();
    const rafId = typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function' ? window.requestAnimationFrame(() => measure()) : 0;
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

  const classes = ['ui-table-scroll', scrollState.hasOverflow ? 'ui-table-scroll--has-overflow' : '', scrollState.canScrollLeft ? 'ui-table-scroll--can-scroll-left' : '', scrollState.canScrollRight ? 'ui-table-scroll--can-scroll-right' : '', className].filter(Boolean).join(' ');
  const resolvedLabel = ariaLabel ?? 'Bảng dữ liệu có thể cuộn ngang (dùng phím mũi tên Trái/Phải để cuộn)';

  return (
    <div ref={scrollRef} className={classes} data-testid={testId} data-has-overflow={scrollState.hasOverflow ? 'true' : 'false'} tabIndex={0} role="region" aria-label={resolvedLabel} onScroll={measure}>
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
  <th aria-sort={active ? (direction === 'asc' ? 'ascending' : 'descending') : 'none'} className={('ui-sort-th ' + className).trim()} style={{ textAlign: align, paddingRight: 14, whiteSpace: 'nowrap', ...style }}>
    <button type="button" className="ui-sort-th-btn" title={title} onClick={onToggle} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap', justifyContent: align === 'right' ? 'flex-end' : 'flex-start' }}>
      <span>{label}</span>
      <span aria-hidden="true" className="ui-sort-indicator">{active ? (direction === 'asc' ? '↑' : '↓') : '↕'}</span>
    </button>
  </th>
);

export const UiMetricGrid = ({ children, 'data-testid': testId }: { children: ReactNode; 'data-testid'?: string }) => (
  <div className="ui-metric-grid" data-testid={testId}>{children}</div>
);

export const UiMetric = ({ value, label, subtitle, isZero }: { value: ReactNode; label: ReactNode; subtitle?: ReactNode; isZero?: boolean }) => {
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
  id?: T; key?: T; value?: T; label: ReactNode; badge?: ReactNode; disabled?: boolean; testId?: string;
}

export const UiTabs = <T extends string = string>({
  tabs, items, activeTab, value, onChange, onTabChange, ariaLabel = 'Chuyển đổi phân mục', className = '', children,
}: {
  tabs?: readonly UiTabItem<T>[]; items?: readonly UiTabItem<T>[]; activeTab?: T; value?: T;
  onChange?: (tab: T) => void; onTabChange?: (tab: T) => void; ariaLabel?: string; className?: string; children?: ReactNode;
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
              <button key={String(tabKey)} type="button" role="tab" aria-selected={isSelected} disabled={tab.disabled} data-testid={tab.testId} className={'ui-tab' + (isSelected ? ' active' : '')} onClick={() => handleSelect?.(tabKey)}>
                <span>{tab.label}</span>
                {tab.badge !== undefined && tab.badge !== null && <span className="ui-tab-badge">{tab.badge}</span>}
              </button>
            );
          })
        : children}
    </div>
  );
};
