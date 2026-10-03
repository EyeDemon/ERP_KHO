import type { ReactNode } from 'react';

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
}) => (
  <label className="ui-toolbar-group">
    <span>{label}</span>
    {children}
  </label>
);

export const UiCard = ({
  title,
  children,
}: {
  title?: string;
  children: ReactNode;
}) => (
  <section className="ui-card">
    {title && <h2 className="ui-card-title">{title}</h2>}
    {children}
  </section>
);

export const UiBadge = ({
  children,
  tone = 'neutral',
}: {
  children: ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'danger';
}) => <span className={'ui-badge ' + (tone === 'neutral' ? '' : tone)}>{children}</span>;

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
