// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import MockDataLab from './MockDataLab';

describe('MockDataLab', () => {
  afterEach(cleanup);

  it('renders warehouse fixtures and switches across linked datasets', () => {
    const view = render(<MemoryRouter><MockDataLab /></MemoryRouter>);
    expect(view.getByText('WH-HCM-01')).toBeTruthy();
    expect(view.getByText('DC Hồ Chí Minh')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Products & Barcode' }));
    expect(view.getByText('SKU-1001')).toBeTruthy();
    expect(view.getByText('8938501000011, 8938501000012')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Inventory Buckets' }));
    expect(view.getByText('1250')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Transfer Conservation' }));
    expect(view.getAllByText('PASS').length).toBeGreaterThanOrEqual(3);

    fireEvent.click(view.getByRole('tab', { name: 'Recount Attempts' }));
    expect(view.getByText(/#1: 1242/)).toBeTruthy();
    expect(view.getByText(/#3: 1248 ✓/)).toBeTruthy();
  });
});
