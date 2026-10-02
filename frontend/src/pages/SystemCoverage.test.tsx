// @vitest-environment jsdom
import { cleanup, fireEvent, render, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import SystemCoverage from './SystemCoverage';

describe('SystemCoverage', () => {
  afterEach(cleanup);

  it('renders all traced capabilities and the four Screen Matrix review items', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    expect(view.getByText('Coverage & Readiness')).toBeTruthy();
    expect(view.getByText('179')).toBeTruthy();
    expect(view.getAllByText('Review Required').length).toBeGreaterThanOrEqual(4);
    expect(view.getByText('UNMAPPED — Chuyển vị trí (Location Transfer)')).toBeTruthy();
    expect(view.getByText('UNMAPPED — Lấy hàng(Picking)')).toBeTruthy();
    expect(view.getByText('UNMAPPED — Đóng gói (Packing)')).toBeTruthy();
    expect(view.getByText('UNMAPPED — Tạo phiếu chuyển kho')).toBeTruthy();
    expect(view.getAllByText('282').length).toBeGreaterThan(0);
    expect(view.getByText('Canonical Documentation Register • Specs 1–282')).toBeTruthy();
  });

  it('filters by release wave and implementation status', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Lọc release wave'), { target: { value: '5' } });
    expect(table.getByText(/AX-09 • Kitting/)).toBeTruthy();
    expect(table.queryByText(/INV-02 • Immutable/)).toBeNull();

    fireEvent.change(view.getByLabelText('Lọc release wave'), { target: { value: 'all' } });
    fireEvent.change(view.getByLabelText('Lọc implementation status'), { target: { value: 'live' } });
    expect(table.getByText(/TR-01 • Warehouse Transfer/)).toBeTruthy();
    expect(table.queryByText(/OUT-05 • Picking/)).toBeNull();
  });

  it('shows advanced canonical capabilities without promoting them to core implementation', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Tìm coverage'), { target: { value: 'Warehouse Safety' } });
    expect(table.getByText(/AX-14 • Warehouse Safety/)).toBeTruthy();
    expect(table.getByText(/Industry optional/)).toBeTruthy();
  });

  it('indexes the complete canonical documentation set separately from capability readiness', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    expect(view.getByText(/Xem toàn bộ 282 tài liệu canonical/)).toBeTruthy();
  });

  it('supports capability text search', () => {
    const view = render(<MemoryRouter><SystemCoverage /></MemoryRouter>);
    const table = within(view.getByTestId('coverage-table'));
    fireEvent.change(view.getByLabelText('Tìm coverage'), { target: { value: 'Inventory Integrity' } });
    expect(table.getByText(/INV-11 • Inventory Integrity/)).toBeTruthy();
    expect(table.queryByText(/OUT-05 • Picking/)).toBeNull();
  });
});
