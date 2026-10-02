// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import MockScenarioLab from './MockScenarioLab';

describe('MockScenarioLab', () => {
  afterEach(cleanup);

  it('renders canonical inbound scenario with steps and assertions', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    expect(view.getAllByText('GS-01').length).toBeGreaterThan(0);
    expect(view.getAllByText('Inbound Receipt → Post → Putaway').length).toBeGreaterThan(0);
    expect(view.getByText('Post Receipt')).toBeTruthy();
    expect(view.getByText('Receipt ledger exactly once')).toBeTruthy();
    expect(view.getAllByText('100', { selector: 'strong' }).length).toBeGreaterThan(0);
  });

  it('simulates scenario steps locally and resets execution state', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    const log = view.getByTestId('scenario-execution-log');
    expect(log.textContent).toContain('Chưa chạy bước nào');
    fireEvent.click(view.getByText('Chạy bước tiếp'));
    expect(log.textContent).toContain('Receive 100');
    expect(log.textContent).toContain('Không tăng warehouse OnHand');
    fireEvent.click(view.getByText('Reset'));
    expect(log.textContent).toContain('Chưa chạy bước nào');
  });

  it('switches to concurrency and idempotency scenarios', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    fireEvent.click(view.getByRole('button', { name: /Concurrent Reservation/ }));
    expect(view.getByText('Exactly one success')).toBeTruthy();
    expect(view.getByText('409 INV_INSUFFICIENT_AVAILABLE')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /Idempotent Shipment Dispatch/ }));
    expect(view.getByText('5 requests → 1 dispatch')).toBeTruthy();
    expect(view.getByText('Retry x4')).toBeTruthy();
  });

  it('opens advanced automation and finance scenarios', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    fireEvent.click(view.getByRole('button', { name: /Period Close/ }));
    expect(view.getByText('WMS quantity truth preserved')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /WCS \/ Robotics/ }));
    expect(view.getByText('Device callback not inventory truth')).toBeTruthy();
  });

  it('covers returns and offline deferred synchronization', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    fireEvent.click(view.getByRole('button', { name: /Customer Return Inspection/ }));
    expect(view.getByText('RESTOCK + QUARANTINE = 6')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /Mobile Offline Deferred Sync/ }));
    expect(view.getByText('High-risk command bị chặn offline')).toBeTruthy();
    expect(view.getByText('BLOCKED_OFFLINE')).toBeTruthy();
  });
});
