// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import MockScenarioLab from './MockScenarioLab';

describe('MockScenarioLab', () => {
  afterEach(cleanup);

  it('renders canonical inbound scenario with steps and assertions', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    expect(view.getByText('GS-01')).toBeTruthy();
    expect(view.getByText('Inbound Receipt → Post → Putaway')).toBeTruthy();
    expect(view.getByText('Post Receipt')).toBeTruthy();
    expect(view.getByText('Receipt ledger exactly once')).toBeTruthy();
    expect(view.getByText('100', { selector: 'strong' })).toBeTruthy();
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

  it('covers returns and offline deferred synchronization', () => {
    const view = render(<MemoryRouter><MockScenarioLab /></MemoryRouter>);
    fireEvent.click(view.getByRole('button', { name: /Customer Return Inspection/ }));
    expect(view.getByText('Sellable + damaged = 6')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: /Mobile Offline Deferred Sync/ }));
    expect(view.getByText('High-risk command bị chặn offline')).toBeTruthy();
    expect(view.getByText('BLOCKED_OFFLINE')).toBeTruthy();
  });
});
