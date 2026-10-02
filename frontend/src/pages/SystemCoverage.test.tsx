// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import SystemCoverage from './SystemCoverage';

describe('SystemCoverage', () => {
  afterEach(cleanup);

  it('renders all traced capabilities and the four Screen Matrix review items', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    expect(view.getByText('Coverage & Readiness')).toBeTruthy();
    expect(view.getByText('131')).toBeTruthy();
    expect(view.getAllByText('Review Required').length).toBeGreaterThanOrEqual(4);
    expect(view.getByText(/Chuyển vị trí/)).toBeTruthy();
    expect(view.getByText(/Lấy hàng/)).toBeTruthy();
    expect(view.getByText(/Đóng gói/)).toBeTruthy();
    expect(view.getByText(/Tạo phiếu chuyển kho/)).toBeTruthy();
  });

  it('filters by release wave and implementation status', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    fireEvent.change(view.getByLabelText('Lọc release wave'), { target: { value: '5' } });
    expect(view.getByText(/AX-09 • Kitting/)).toBeTruthy();
    expect(view.queryByText(/INV-02 • Immutable/)).toBeNull();

    fireEvent.change(view.getByLabelText('Lọc release wave'), { target: { value: 'all' } });
    fireEvent.change(view.getByLabelText('Lọc implementation status'), { target: { value: 'live' } });
    expect(view.getByText(/TR-01 • Warehouse Transfer/)).toBeTruthy();
    expect(view.queryByText(/OUT-05 • Picking/)).toBeNull();
  });

  it('supports capability text search', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    fireEvent.change(view.getByLabelText('Tìm coverage'), { target: { value: 'Inventory Integrity' } });
    expect(view.getByText(/INV-11 • Inventory Integrity/)).toBeTruthy();
    expect(view.queryByText(/OUT-05 • Picking/)).toBeNull();
  });
});
