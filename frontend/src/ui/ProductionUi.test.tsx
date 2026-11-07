// @vitest-environment jsdom
import { useState } from 'react';
import { render, cleanup, fireEvent, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  WMS_GLOSSARY,
  UiBadge,
  UiCard,
  UiEmptyState,
  UiErrorBanner,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiSkeleton,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
  UiTooltip,
} from './ProductionUi';

describe('Production UI primitives', () => {
  afterEach(() => {
    cleanup();
    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));
  });

  it('renders the canonical page, toolbar, card and metrics structure', () => {
    const view = render(
      <UiPage>
        <UiPageHeader eyebrow="Nhập kho" title="Phiếu nhập kho" description="Tiếp nhận hàng hóa." />
        <UiToolbar><UiToolbarField label="Kho"><select aria-label="Kho"><option>HCM</option></select></UiToolbarField></UiToolbar>
        <UiMetricGrid><UiMetric value={4} label="Tác vụ mở" /></UiMetricGrid>
        <UiCard title="Danh sách"><UiBadge tone="warning">Đang xử lý</UiBadge></UiCard>
      </UiPage>
    );

    expect(view.getByText('Nhập kho')).toBeTruthy();
    expect(view.getByText('Phiếu nhập kho')).toBeTruthy();
    expect(view.getByText('Tác vụ mở')).toBeTruthy();
    expect(view.getByText('Đang xử lý').className).toContain('warning');
  });

  it('A11Y-02: wraps wide operational tables in a keyboard-accessible scroll region with default and custom aria-label', () => {
    const view = render(
      <div>
        <UiTableScroll data-testid="default-scroll">
          <table><tbody><tr><td>SKU-1001</td></tr></tbody></table>
        </UiTableScroll>
        <UiTableScroll data-testid="custom-scroll" ariaLabel="Bảng đối soát hóa đơn có thể cuộn ngang">
          <table><tbody><tr><td>INV-2026-01</td></tr></tbody></table>
        </UiTableScroll>
      </div>
    );
    const defaultRegion = view.getByTestId('default-scroll');
    expect(defaultRegion.getAttribute('tabindex')).toBe('0');
    expect(defaultRegion.getAttribute('role')).toBe('region');
    expect(defaultRegion.getAttribute('aria-label')).toBe('Bảng dữ liệu có thể cuộn ngang (dùng phím mũi tên Trái/Phải để cuộn)');
    expect(defaultRegion.getAttribute('data-has-overflow')).toBe('false');

    const customRegion = view.getByTestId('custom-scroll');
    expect(customRegion.getAttribute('tabindex')).toBe('0');
    expect(customRegion.getAttribute('role')).toBe('region');
    expect(customRegion.getAttribute('aria-label')).toBe('Bảng đối soát hóa đơn có thể cuộn ngang');
  });

  it('renders a reusable empty state', () => {
    const view = render(<UiEmptyState title="Không có dữ liệu" detail="Hãy thay đổi bộ lọc." />);
    expect(view.getByText('Không có dữ liệu')).toBeTruthy();
    expect(view.getByText('Hãy thay đổi bộ lọc.')).toBeTruthy();
  });

  it('UX-05 & A11Y-03: exports all 13 WMS_GLOSSARY Vietnamese terms and supports hover, focus, click, Escape, and outside-click on UiTooltip', () => {
    const requiredKeys = ['FEFO', 'FIFO', 'BOM', 'WAC', 'AWB', 'GS1-128', 'RMA', 'MST', 'SoD', 'ĐVVC', 'ĐVT', 'SKU', 'Bin'] as const;
    expect(Object.keys(WMS_GLOSSARY)).toEqual(expect.arrayContaining([...requiredKeys]));
    for (const key of requiredKeys) {
      expect(WMS_GLOSSARY[key].term).toBe(key);
      expect(WMS_GLOSSARY[key].label.length).toBeGreaterThan(5);
      expect(WMS_GLOSSARY[key].description.length).toBeGreaterThan(15);
    }

    render(
      <div>
        <UiTooltip term="FEFO" data-testid="tooltip-fefo" />
        <button type="button" data-testid="outside-btn">Bên ngoài</button>
      </div>
    );

    const trigger = screen.getByRole('button', { name: 'FEFO' });
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.focus(trigger);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const tooltipOnFocus = screen.getByRole('tooltip');
    expect(tooltipOnFocus.textContent).toContain('Xuất theo hạn dùng gần nhất trước');
    expect(trigger.getAttribute('aria-describedby')).toBe(tooltipOnFocus.id);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('tooltip')).toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');

    const wrap = screen.getByTestId('tooltip-fefo');
    fireEvent.mouseEnter(wrap);
    expect(screen.getByRole('tooltip')).toBeTruthy();
    fireEvent.mouseLeave(wrap);
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.pointerDown(trigger);
    fireEvent.click(trigger);
    expect(screen.getByRole('tooltip')).toBeTruthy();
    fireEvent.mouseLeave(wrap);
    expect(screen.getByRole('tooltip')).toBeTruthy();

    fireEvent.pointerDown(screen.getByTestId('outside-btn'));
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('RES-03: collapses secondary filters on mobile <= 768px while preserving controlled/uncontrolled state and supporting Áp dụng / Xóa lọc', () => {
    const onApply = vi.fn();
    const onReset = vi.fn();

    const ToolbarHarness = () => {
      const [status, setStatus] = useState('POSTED');
      return (
        <UiToolbar data-testid="mobile-toolbar" onApplyFilters={onApply} onResetFilters={onReset}>
          <form onSubmit={(e) => e.preventDefault()}>
            <UiToolbarField label="Kho"><select defaultValue="WH01"><option value="WH01">WH01</option></select></UiToolbarField>
            <UiToolbarField label="Từ khóa"><input placeholder="Mã chứng từ" defaultValue="" /></UiToolbarField>
            <UiToolbarField label="Trạng thái">
              <select data-testid="sec-status" value={status} onChange={(e) => setStatus(e.target.value)}>
                <option value="">Tất cả</option>
                <option value="POSTED">Đã ghi sổ</option>
              </select>
            </UiToolbarField>
            <UiToolbarField label="Số lô"><input data-testid="sec-lot" defaultValue="" /></UiToolbarField>
            <div className="inventory-toolbar-actions" data-testid="inv-actions-group">
              <button type="submit">Lọc Tồn Kho</button>
              <button type="button">Xuất Excel Tồn Kho</button>
            </div>
          </form>
        </UiToolbar>
      );
    };

    Object.defineProperty(window, 'innerWidth', { value: 390, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));

    render(<ToolbarHarness />);

    const toggleBtn = screen.getByTestId('btn-toggle-toolbar-filters');
    expect(toggleBtn.textContent).toContain('Bộ lọc (2)');
    expect((toggleBtn as HTMLButtonElement).style.minHeight).toBe('44px');
    expect(screen.getByTestId('inv-actions-group').closest('.ui-toolbar-primary-bar')).toBeTruthy();
    expect(screen.getByTestId('inv-actions-group').closest('[data-testid="ui-toolbar-secondary-panel"]')).toBeNull();
    expect(screen.getByTestId('toolbar-active-filter-badge').textContent).toBe('Đang lọc: 1');
    const panel = screen.getByTestId('ui-toolbar-secondary-panel');
    expect(panel.hidden).toBe(true);

    fireEvent.click(toggleBtn);
    expect(panel.hidden).toBe(false);
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');

    const lotInput = screen.getByTestId('sec-lot') as HTMLInputElement;
    lotInput.value = 'LOT-2026';
    fireEvent.input(lotInput);
    expect(screen.getByTestId('toolbar-active-filter-badge').textContent).toBe('Đang lọc: 2');

    fireEvent.click(screen.getByTestId('btn-apply-toolbar-filters'));
    expect(onApply).toHaveBeenCalledTimes(1);
    expect(panel.hidden).toBe(true);
    expect((screen.getByTestId('sec-lot') as HTMLInputElement).value).toBe('LOT-2026');
    expect((screen.getByTestId('sec-status') as HTMLSelectElement).value).toBe('POSTED');

    fireEvent.click(toggleBtn);
    fireEvent.click(screen.getByTestId('btn-reset-toolbar-filters'));
    expect(onReset).toHaveBeenCalledTimes(1);
    expect((screen.getByTestId('sec-lot') as HTMLInputElement).value).toBe('');
    expect((screen.getByTestId('sec-status') as HTMLSelectElement).value).toBe('');
    expect(screen.queryByTestId('toolbar-active-filter-badge')).toBeNull();
  });

  it('DS-02: renders UiSkeleton and UiErrorBanner with accessible status/alert semantics and retry action', () => {
    const onRetry = vi.fn();
    render(
      <div>
        <UiSkeleton lines={4} label="Đang tải danh mục tồn kho..." data-testid="inv-skeleton" />
        <UiErrorBanner
          title="Không thể tải dữ liệu tồn kho"
          message="Máy chủ kho phản hồi quá thời gian chờ."
          errorCode="INV_UPSTREAM_TIMEOUT"
          correlationId="CORR-9988"
          onRetry={onRetry}
        />
      </div>
    );

    const skeleton = screen.getByTestId('inv-skeleton');
    expect(skeleton.getAttribute('role')).toBe('status');
    expect(skeleton.getAttribute('aria-busy')).toBe('true');
    expect(skeleton.querySelectorAll('.ui-skeleton-line').length).toBe(4);
    expect(skeleton.textContent).toContain('Đang tải danh mục tồn kho...');

    const banner = screen.getByTestId('ui-error-banner');
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('Không thể tải dữ liệu tồn kho');

    const msgEl = banner.querySelector('.ui-error-banner-message') as HTMLElement;
    expect(msgEl.textContent).toBe('Máy chủ kho phản hồi quá thời gian chờ.');
    expect(msgEl.textContent).not.toContain('INV_UPSTREAM_TIMEOUT');
    expect(msgEl.textContent).not.toContain('CORR-9988');

    const techDetails = screen.getByTestId('ui-error-banner-tech-details') as HTMLDetailsElement;
    expect(techDetails.tagName.toLowerCase()).toBe('details');
    expect(techDetails.open).toBe(false);
    expect(techDetails.querySelector('summary')?.textContent).toBe('Chi tiết kỹ thuật');
    expect(techDetails.querySelector('.ui-err-code')?.textContent).toBe('INV_UPSTREAM_TIMEOUT • CORR-9988');

    const retryBtn = screen.getByTestId('ui-error-banner-retry') as HTMLButtonElement;
    expect(retryBtn.style.minHeight).toBe('44px');
    fireEvent.click(retryBtn);
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
