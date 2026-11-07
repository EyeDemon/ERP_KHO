// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OutboundPipelineStepper } from './OutboundPipelineStepper';
import { WorkspaceSubTabs } from './WorkspaceSubTabs';

describe('Wave-03 OutboundPipelineStepper & WorkspaceSubTabs', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  afterEach(() => {
    cleanup();
  });

  it('renders OutboundPipelineStepper in 5-step Full WMS mode and switches to 2-step Express mode', () => {
    render(
      <MemoryRouter initialEntries={['/picking-tasks']}>
        <OutboundPipelineStepper defaultMode="FULL_5_STEP" />
      </MemoryRouter>,
    );

    const stepper = screen.getByTestId('outbound-pipeline-stepper');
    expect(stepper).toBeTruthy();
    expect(screen.getByTestId('outbound-mode-toggle')).toBeTruthy();
    expect(screen.getByTestId('outbound-step-1').textContent).toContain('Đơn bán hàng và đợt lấy');
    expect(screen.getByTestId('outbound-step-2').textContent).toContain('Lấy hàng tại kệ');
    expect(screen.getByTestId('outbound-step-2').getAttribute('aria-current')).toBe('step');
    expect(screen.getByTestId('outbound-step-5').textContent).toContain('Bàn giao vận đơn');

    const expressBtn = screen.getByTestId('outbound-mode-express');
    fireEvent.click(expressBtn);

    expect(screen.getByTestId('outbound-step-1').textContent).toContain('Đơn bán hàng và phân bổ');
    expect(screen.getByTestId('outbound-step-2').textContent).toContain('Duyệt và xuất kho nhanh');
    expect(screen.queryByTestId('outbound-step-3')).toBeNull();
  });

  it('renders OutboundPipelineStepper safely even outside a Router context', () => {
    render(<OutboundPipelineStepper defaultMode="EXPRESS_2_STEP" />);
    expect(screen.getByTestId('outbound-pipeline-stepper')).toBeTruthy();
    expect(screen.getByTestId('outbound-step-1').textContent).toContain('Đơn bán hàng và phân bổ');
  });

  it('renders WorkspaceSubTabs for Inbound, Outbound, and Inventory routes and hides on Dashboard root', () => {
    const { unmount } = render(
      <MemoryRouter initialEntries={['/import-receipts']}>
        <WorkspaceSubTabs />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('workspace-sub-tabs')).toBeTruthy();
    expect(screen.getByTestId('workspace-sub-tab-import-receipts').getAttribute('aria-selected')).toBe('true');
    unmount();

    render(
      <MemoryRouter initialEntries={['/']}>
        <WorkspaceSubTabs />
      </MemoryRouter>,
    );
    expect(screen.queryByTestId('workspace-sub-tabs')).toBeNull();
  });
});
