// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceErrorBoundary } from '../WorkspaceErrorBoundary';
import {
  permissionDescription,
  permissionLabel,
} from '../../../security/permissionPresentation';
import {
  permissionDescription as servicePermissionDescription,
  permissionLabel as servicePermissionLabel,
} from '../../../services/permissionPresentation';

function CrashableChild({ shouldCrash }: { shouldCrash: boolean }) {
  if (shouldCrash) {
    throw new Error('Simulated render crash');
  }
  return <div data-testid="healthy-workspace-child">Nội dung phân hệ hoạt động bình thường</div>;
}

describe('WorkspaceErrorBoundary & Opening Balance Permission Presentation (TASK-W1-05)', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
    cleanup();
  });

  it('bắt lỗi render crash cục bộ, giữ nguyên AppLayout shell bên ngoài và cho phép sao chép mã chẩn đoán', async () => {
    render(
      <div data-testid="outer-app-shell">
        <span>AppLayout Shell</span>
        <WorkspaceErrorBoundary locationKey="/inventory/opening-balances">
          <CrashableChild shouldCrash={true} />
        </WorkspaceErrorBoundary>
      </div>,
    );

    // Outer shell must remain intact in DOM
    expect(screen.getByTestId('outer-app-shell')).toBeTruthy();
    expect(screen.getByText('AppLayout Shell')).toBeTruthy();

    // Error boundary card is rendered with exact Vietnamese title and error details
    const boundary = screen.getByTestId('workspace-error-boundary');
    expect(boundary).toBeTruthy();
    expect(
      screen.getByText('Không thể hiển thị phân hệ này do lỗi ngoại lệ giao diện'),
    ).toBeTruthy();
    expect(screen.getByText('Simulated render crash')).toBeTruthy();
    expect(screen.getByText('ERR-UI-SIMULATED-RENDER-CRASH')).toBeTruthy();

    // Reload and Copy buttons are present with exact labels
    const reloadBtn = screen.getByTestId('error-boundary-reload-btn');
    const copyBtn = screen.getByTestId('error-boundary-copy-btn');
    expect(reloadBtn.textContent).toContain('Tải lại trang');
    expect(copyBtn.textContent).toContain('Sao chép mã chẩn đoán');

    // Clicking copy button displays copied notice
    fireEvent.click(copyBtn);
    await waitFor(() => {
      const notice = screen.getByTestId('error-boundary-copied-notice');
      expect(notice).toBeTruthy();
      expect(notice.textContent).toContain('ERR-UI-SIMULATED-RENDER-CRASH');
    });
  });

  it('tự động phục hồi và hiển thị lại nội dung bình thường khi đổi locationKey', () => {
    const { rerender } = render(
      <div data-testid="outer-app-shell">
        <WorkspaceErrorBoundary locationKey="/route-broken">
          <CrashableChild shouldCrash={true} />
        </WorkspaceErrorBoundary>
      </div>,
    );

    expect(screen.getByTestId('workspace-error-boundary')).toBeTruthy();

    rerender(
      <div data-testid="outer-app-shell">
        <WorkspaceErrorBoundary locationKey="/route-healthy">
          <CrashableChild shouldCrash={false} />
        </WorkspaceErrorBoundary>
      </div>,
    );

    expect(screen.queryByTestId('workspace-error-boundary')).toBeNull();
    expect(screen.getByTestId('healthy-workspace-child')).toBeTruthy();
    expect(screen.getByText('Nội dung phân hệ hoạt động bình thường')).toBeTruthy();
  });

  it('phục hồi và gọi onReset khi người dùng nhấn nút Tải lại trang (error-boundary-reload-btn)', () => {
    let shouldCrash = true;
    const onResetSpy = vi.fn(() => {
      shouldCrash = false;
    });

    function DynamicChild() {
      if (shouldCrash) {
        throw new Error('Simulated render crash');
      }
      return <div data-testid="healthy-workspace-child">Đã khôi phục phân hệ thành công</div>;
    }

    render(
      <div data-testid="outer-app-shell">
        <WorkspaceErrorBoundary locationKey="/inventory/opening-balances" onReset={onResetSpy}>
          <DynamicChild />
        </WorkspaceErrorBoundary>
      </div>,
    );

    expect(screen.getByTestId('workspace-error-boundary')).toBeTruthy();

    fireEvent.click(screen.getByTestId('error-boundary-reload-btn'));

    expect(onResetSpy).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('workspace-error-boundary')).toBeNull();
    expect(screen.getByTestId('healthy-workspace-child')).toBeTruthy();
    expect(screen.getByText('Đã khôi phục phân hệ thành công')).toBeTruthy();
  });

  it('hiển thị đúng nhãn và mô tả tiếng Việt cho quyền inventory_opening_balance.import', () => {
    expect(permissionLabel('inventory_opening_balance.import')).toBe(
      'Nhập tồn kho đầu kỳ (Excel/CSV)',
    );
    expect(permissionDescription('inventory_opening_balance.import')).toBe(
      'Cho phép tải lên và ghi sổ tồn kho đầu kỳ từ tệp Excel/CSV vào kho.',
    );
    expect(servicePermissionLabel('inventory_opening_balance.import')).toBe(
      'Nhập tồn kho đầu kỳ (Excel/CSV)',
    );
    expect(servicePermissionDescription('inventory_opening_balance.import')).toBe(
      'Cho phép tải lên và ghi sổ tồn kho đầu kỳ từ tệp Excel/CSV vào kho.',
    );
  });
});
