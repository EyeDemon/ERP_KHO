import { Component, type ErrorInfo, type ReactNode } from 'react';

export type WorkspaceErrorBoundaryProps = {
  children: ReactNode;
  locationKey?: string;
  onReset?: () => void;
};

type WorkspaceErrorBoundaryState = {
  hasError: boolean;
  error: Error | null;
  diagnosticCode: string;
  copied: boolean;
  lastPathname: string;
};

const getCurrentPathname = (): string => {
  if (typeof window === 'undefined') return '';
  return window.location?.pathname ?? '';
};

const buildDiagnosticCode = (error?: Error | null): string => {
  const rawMessage = (error?.message ?? 'UI_RENDER_EXCEPTION').trim();
  const slug = rawMessage
    .replace(/[^a-zA-Z0-9_-]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 24)
    .toUpperCase();
  return `ERR-UI-${slug || 'RUNTIME'}`;
};

export class WorkspaceErrorBoundary extends Component<
  WorkspaceErrorBoundaryProps,
  WorkspaceErrorBoundaryState
> {
  constructor(props: WorkspaceErrorBoundaryProps) {
    super(props);
    this.state = {
      hasError: false,
      error: null,
      diagnosticCode: '',
      copied: false,
      lastPathname: props.locationKey ?? getCurrentPathname(),
    };
  }

  static getDerivedStateFromError(error: Error): Partial<WorkspaceErrorBoundaryState> {
    return {
      hasError: true,
      error,
      diagnosticCode: buildDiagnosticCode(error),
      copied: false,
    };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        window.localStorage.setItem('lastErrorCode', buildDiagnosticCode(error));
        window.localStorage.setItem(
          'lastUiExceptionDetail',
          `${error.message} | ${errorInfo.componentStack?.slice(0, 240) ?? ''}`,
        );
      }
    } catch {
      // Ignore storage access errors in restricted environments
    }
  }

  componentDidMount(): void {
    if (typeof window !== 'undefined') {
      window.addEventListener('popstate', this.handleLocationChange);
    }
  }

  componentWillUnmount(): void {
    if (typeof window !== 'undefined') {
      window.removeEventListener('popstate', this.handleLocationChange);
    }
  }

  componentDidUpdate(prevProps: WorkspaceErrorBoundaryProps): void {
    const currentPathname = this.props.locationKey ?? getCurrentPathname();
    const prevKey = prevProps.locationKey ?? this.state.lastPathname;
    if (this.state.hasError && currentPathname !== prevKey) {
      this.setState({
        hasError: false,
        error: null,
        diagnosticCode: '',
        copied: false,
        lastPathname: currentPathname,
      });
    }
  }

  private handleLocationChange = (): void => {
    const nextPath = getCurrentPathname();
    if (this.state.hasError && nextPath !== this.state.lastPathname) {
      this.setState({
        hasError: false,
        error: null,
        diagnosticCode: '',
        copied: false,
        lastPathname: nextPath,
      });
    }
  };

  private handleReload = (): void => {
    this.setState({
      hasError: false,
      error: null,
      diagnosticCode: '',
      copied: false,
      lastPathname: this.props.locationKey ?? getCurrentPathname(),
    });
    this.props.onReset?.();
  };

  private handleCopyDiagnostics = (): void => {
    const route = this.props.locationKey || getCurrentPathname() || '/';
    const message = this.state.error?.message || 'Lỗi ngoại lệ không xác định khi dựng giao diện';
    const refCode = this.state.diagnosticCode || buildDiagnosticCode(this.state.error);
    const payload = `Ref=${refCode} | Route=${route} | Message=${message}`;
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      void Promise.resolve(navigator.clipboard.writeText(payload)).catch(() => {});
    }
    this.setState({ copied: true });
  };

  render(): ReactNode {
    if (!this.state.hasError) {
      return this.props.children;
    }

    const errorMessage =
      this.state.error?.message || 'Đã xảy ra lỗi không mong muốn trong quá trình hiển thị dữ liệu.';
    const diagnosticCode = this.state.diagnosticCode || buildDiagnosticCode(this.state.error);

    return (
      <section
        className="ui-card"
        data-testid="workspace-error-boundary"
        role="alert"
        aria-live="assertive"
        style={{
          margin: '16px 0',
          padding: '20px 24px',
          borderRadius: 12,
          border: '1px solid #fecaca',
          background: '#fff1f2',
          color: '#881337',
        }}
      >
        <div className="ui-stack" style={{ gap: 10 }}>
          <h2 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: '#881337' }}>
            Không thể hiển thị phân hệ này do lỗi ngoại lệ giao diện
          </h2>
          <p style={{ margin: 0, fontSize: 13.5, color: '#9f1239', lineHeight: 1.5 }}>
            Thanh điều hướng và phiên làm việc của bạn vẫn an toàn. Bạn có thể bấm tải lại trang hoặc sao chép mã chẩn đoán để gửi bộ phận kỹ thuật.
          </p>
          <div
            style={{
              padding: '8px 12px',
              borderRadius: 8,
              background: '#ffffff',
              border: '1px solid #fda4af',
              fontSize: 12.5,
              color: '#0f172a',
              fontFamily: 'var(--font-mono, monospace)',
            }}
          >
            <div>
              <strong>Mã tham chiếu:</strong> <code>{diagnosticCode}</code>
            </div>
            <div style={{ marginTop: 4 }}>
              <strong>Chi tiết chẩn đoán:</strong> <span>{errorMessage}</span>
            </div>
          </div>
          <div className="ui-inline-wrap" style={{ gap: 10, marginTop: 4 }}>
            <button
              type="button"
              data-testid="error-boundary-reload-btn"
              onClick={this.handleReload}
              style={{
                padding: '6px 14px',
                minHeight: 36,
                borderRadius: 8,
                border: '1px solid #0f172a',
                background: '#0f172a',
                color: '#ffffff',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              Tải lại trang
            </button>
            <button
              type="button"
              className="ui-btn-secondary"
              data-testid="error-boundary-copy-btn"
              onClick={this.handleCopyDiagnostics}
              style={{
                padding: '6px 14px',
                minHeight: 36,
                borderRadius: 8,
                border: '1px solid #cbd5e1',
                background: '#ffffff',
                color: '#1e293b',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              Sao chép mã chẩn đoán
            </button>
          </div>
          {this.state.copied && (
            <div
              role="status"
              data-testid="error-boundary-copied-notice"
              style={{
                fontSize: 12.5,
                color: '#065f46',
                background: '#ecfdf5',
                border: '1px solid #a7f3d0',
                borderRadius: 6,
                padding: '6px 10px',
              }}
            >
              Đã sao chép mã chẩn đoán ({diagnosticCode}) vào bộ nhớ tạm.
            </div>
          )}
        </div>
      </section>
    );
  }
}

export default WorkspaceErrorBoundary;
